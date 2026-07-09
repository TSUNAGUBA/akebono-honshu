using System.Text;
using System.Text.Json.Serialization;
using Akebono.Api;
using Akebono.Api.Endpoints;
using Akebono.Application.Common;
using Akebono.Infrastructure;
using Akebono.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// MIG-3 CSV 取込で Shift_JIS (CP932) を読込むため、CodePagesEncodingProvider を登録。
// .NET Core / .NET 5+ は既定で限定的なエンコーディングのみ対応のため必須。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// プラットフォーム統合改修: 旧 JST-naive 設計 (Npgsql.EnableLegacyTimestampBehavior +
// timestamp without time zone) は廃止。あけぼの SCM プラットフォーム標準 (ADR-006 相当) の
// timestamptz/UTC 統一に合わせ、Npgsql 既定 (非レガシー) の
// 「DateTime Kind=Utc ⇔ timestamptz」マッピングをそのまま使う。
// 格納は SystemTime.UtcNow、表示・帳票・採番年度は SystemTime.JstNow (Domain/Common/SystemTime.cs)。

var builder = WebApplication.CreateBuilder(args);

// AWS Secrets Manager 経由で秘密情報 (DB 接続文字列等) を IConfiguration に注入 (Iter 4 段階 C-2)。
// - dev/test/CI: `Secrets:Provider=Environment` (default) → 何もしない。環境変数 / User Secrets /
//   appsettings.Development.json で値が解決される (既存挙動)。
// - prod (EC2/docker compose): 既定は `Secrets:Provider=Environment` で、repository secrets から
//   生成した .env (ConnectionStrings__Postgres 等) で値が解決される (deploy/README.md)。
//   AWS Secrets Manager 運用に切替える場合は `Secrets__Provider=AwsSecretsManager` +
//   `Secrets__AwsPrefix=akebono/prod/` を設定する (EC2 インスタンスプロファイルに権限が必要)。
// SecretMappings の対象 Secret 群 (`db-connection`, `firebase-sa-key` ほか) は事前定義されている
// (Infrastructure/Secrets/SecretMappings.cs)。
// **fail-fast SoT (SA-P0-1):** prefix の null / placeholder 検証は AddAkebonoAwsSecretsManager 拡張に
// 集約。ここでは null/empty もそのまま渡し、拡張内で 1 箇所だけ throw する (二重 fail-fast はメッセージ
// 齟齬の温床)。
var secretsProvider = builder.Configuration["Secrets:Provider"] ?? SecretsProviders.Environment;
if (string.Equals(secretsProvider, SecretsProviders.AwsSecretsManager, StringComparison.OrdinalIgnoreCase))
{
    var prefix = builder.Configuration["Secrets:AwsPrefix"];
    var region = builder.Configuration["AWS:Region"];
    builder.Configuration.AddAkebonoAwsSecretsManager(prefix, regionName: region);
}

builder.Services.AddAkebonoInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();

// Enum を JSON で文字列としてやりとり (Phase 5 EditReason "quantity" 等の仕様準拠、
// camelCase: "Quantity" → "quantity")
builder.Services.ConfigureHttpJsonOptions(opt =>
{
    opt.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    // 空要素・前後空白を除去 (誤設定で空文字 origin が紛れても無害化、SA 指摘)。
    .WithOrigins(builder.Configuration["Cors:Origins"]
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    // Content-Disposition は CORS のデフォルト simple response header に
    // 含まれないため明示 expose。Frontend の帳票出力 (useOrders.exportOrder /
    // bulkExport) で filename 抽出に使用 (O-06)。
    .WithExposedHeaders("Content-Disposition")));

// Firebase Auth + JwtBearer 認証 (Iter 4 段階 B、4 周目レビュー反映の最終形)
// - Firebase ID Token を JWKS で検証 (issuer/audience = projectId、ValidateIssuerSigningKey 明示)
// - OnTokenValidated で users.firebase_uid → users.id を **単一 cache + タプル引当** で取得
//   - cache key `fb_uid_resolve:{uid}` (60s) に `(ActiveId, AnyId)` を atomic に格納
//   - ActiveId: `!IsDeleted && IsActive` 引当結果 (null なら拒否対象)
//   - AnyId:    `!IsDeleted` 引当結果 (null なら未紐付け、有値なら inactive ユーザを示す)
//   - 4 周目レビューで指摘された 2 段 cache 乖離 (CR P1-1) を解消するため単一 cache に統合
// - 判定分岐:
//   - ActiveId 有値 → Claim 付与 (通常認証成立)
//   - ActiveId=null かつ AnyId 有値 → `Auth.LoginRejected.Inactive` を actor_user_id 付き監査 (SEC-15)
//   - ActiveId=null かつ AnyId=null → `Auth.UidUnboundProbe` を actor_user_id=null 監査 (偵察検知)
// - 監査記録は `cache.GetOrCreateAsync($"audit_logged:{uid}", ...)` で per-UID 5 分 atomic de-dup
// - DB 障害時は warn ログ + Claim 未付与で続行 (CLAUDE.md 原則 4 非ブロッキング)
// - cache 無効化責務 (段階 C P-12 user 編集 API 着手後):
//   firebase_uid 変更 / 論理削除 / IsActive 変更時は **2 つの cache を同時に Remove** すること
//   1. `cache.Remove($"fb_uid_resolve:{uid}")` — 引当キャッシュ flush (60s 遅延回避)
//   2. `cache.Remove($"audit_logged:{uid}")` — de-dup 解除 (復活後の新規拒否を即座に監査可能化)
// - multi-instance 注意: Backend を 2 インスタンス以上に水平拡張する場合 (EC2 複数台 / 複数コンテナ等) IMemoryCache は
//   プロセスローカルかつ memory pressure で eviction される (de-dup の信頼性も低下)。架構判断は
//   architecture.md §4.5 参照
// - soft-deleted (`IsDeleted=true`) ユーザの扱い: 引当 WHERE が `!IsDeleted` のため hit せず
//   `(ActiveId=null, AnyId=null)` 組合せで `Auth.UidUnboundProbe` 扱い (actor_user_id=null)。
//   これは「Firebase Auth `disabled=true` 同期を SoT 防御の単一ポイント」とする設計判断
//   (architecture.md §5.1 参照)。退職者の不正試行は Firebase 側で先に拒否される前提
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
if (string.IsNullOrEmpty(firebaseProjectId) || firebaseProjectId == "__OVERRIDE_ME__")
{
    throw new InvalidOperationException(
        "Firebase:ProjectId が未設定またはプレースホルダー (__OVERRIDE_ME__) のままです。" +
        "dev は appsettings.Development.json で、prod は環境変数 Firebase__ProjectId で実値を指定してください。" +
        "dev/prod で別 Firebase project を使い分けることで dev 認証情報の本番流用リスクを防ぎます。");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true,
            // SDK の既定値は false。Authority 経由で JWKS 取得 + 検証は行われるが、
            // 将来バージョンでの挙動変化を防ぐため明示的に true。
            ValidateIssuerSigningKey = true,
        };
        options.Events = new JwtBearerEvents
        {
            // 401/403 も封筒 {error:{code,...}} で返す (AKB-DOC-12 §6.3。既定の空ボディは規約違反)。
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                if (ctx.Response.HasStarted) return;
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                // 失敗種別でコードを出し分ける (AKB-DOC-12 §14.5: 001 欠如 / 002 不正 / 003 期限切れ)
                var (code, message, userAction) = ctx.AuthenticateFailure switch
                {
                    Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException =>
                        (AkbErrorCodes.AuthTokenExpired, "認証トークンの有効期限が切れています", "再ログインまたはトークン更新をしてください"),
                    not null =>
                        (AkbErrorCodes.AuthTokenInvalid, "認証トークンが不正です", "ログインし直してください"),
                    _ =>
                        (AkbErrorCodes.AuthTokenMissing, "認証が必要です", "ログインし直してください"),
                };
                await ctx.Response.WriteAsJsonAsync(ApiEnvelope.ErrorBody(ctx.HttpContext, code, message, userAction));
            },
            OnForbidden = async ctx =>
            {
                if (ctx.Response.HasStarted) return;
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(ApiEnvelope.ErrorBody(
                    ctx.HttpContext, AkbErrorCodes.AuthInsufficientPermission,
                    "この操作を行う権限がありません"));
            },
            // OnTokenValidated は **単一 cache + タプル引当** で SEC-12 / SEC-15 を満たす (4 周目 P1-1 反映):
            // 旧実装の 2 段 cache (`fb_uid:{uid}` + `fb_uid_any:{uid}`) は独立 TTL のため、inactive→active
            // 切替直後に「active ユーザを Auth.LoginRejected.Inactive で誤監査」する状態乖離が起きる。
            // factory 内で 1 度の DB lookup から `(ActiveId, AnyId)` を atomic に算出することで乖離不能化。
            // 監査記録は `cache.GetOrCreateAsync($"audit_logged:{uid}", ...)` で per-UID 5 分 atomic de-dup。
            OnTokenValidated = async ctx =>
            {
                var firebaseUid = AuthEndpoints.GetFirebaseUid(ctx.Principal);
                if (string.IsNullOrEmpty(firebaseUid)) return;

                var sp = ctx.HttpContext.RequestServices;
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtBearer.OnTokenValidated");
                var cache = sp.GetRequiredService<IMemoryCache>();

                (long? ActiveId, long? AnyId, Guid? TenantId, string? TenantStatus) resolved;
                try
                {
                    resolved = await cache.GetOrCreateAsync(
                        $"fb_uid_resolve:{firebaseUid}",
                        async entry =>
                        {
                            // 60s キャッシュ: firebase_uid UNIQUE index 前提で安全。
                            // ユーザ無効化・soft-delete の反映は最大 60s 遅延 (段階 C で編集 API 追加時に
                            // cache.Remove を呼ぶ仕組みを入れる前提、Program.cs 上部コメント参照)。
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                            var db = sp.GetRequiredService<IAkebonoDbContext>();
                            // 1 度の DB クエリで (IsActive な行、!IsDeleted な行) の id を同時取得し、
                            // ActiveId / AnyId のペアを atomic に確定させる。同じスナップショットから
                            // 派生するため必ず整合する (ActiveId 有値 → AnyId も同値)。
                            //
                            // テナント解決: 本引当はテナントコンテキスト確立「前」に走る認証エントリ
                            // ポイントのため IgnoreQueryFilters を明示する (users は RLS 適用除外
                            // テーブル。詳細は db/init/08-tenancy-rls.sql / AkebonoDbContext 参照)。
                            var row = await db.Users
                                .IgnoreQueryFilters()
                                .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted)
                                .Select(u => new
                                {
                                    u.Id,
                                    u.IsActive,
                                    u.TenantId,
                                    // tenant.status (AKB-TENANT-004 判定用。tenant は RLS 適用外の
                                    // レジストリ投影のため認証前でも参照可能)
                                    TenantStatus = db.Tenants
                                        .Where(t => t.TenantId == u.TenantId)
                                        .Select(t => t.Status)
                                        .FirstOrDefault(),
                                })
                                .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
                            if (row is null)
                            {
                                return ((long?)null, (long?)null, (Guid?)null, (string?)null);
                            }
                            return (row.IsActive ? (long?)row.Id : null, (long?)row.Id,
                                (Guid?)row.TenantId, (string?)row.TenantStatus);
                        });
                }
                catch (Exception ex)
                {
                    // RDS 断・コネクションプール枯渇等で認証全体をブロックしないよう、
                    // Claim 未付与で処理続行。各 endpoint は 401 を返すが、500 propagate は防ぐ。
                    // CloudWatch Logs への UID 露出最小化のため uidShort 化 (audit_logs.note と整合)。
                    var uidShortForLog = firebaseUid.Length > 8
                        ? firebaseUid.Substring(0, 8) + "..."
                        : firebaseUid;
                    logger.LogWarning(ex,
                        "Firebase UID {UidShort} → users 引当てで例外発生、Claim 未付与で処理続行",
                        uidShortForLog);
                    return;
                }

                if (resolved.ActiveId is null)
                {
                    // 拒否経路: AnyId 有値なら inactive ユーザ、null なら未紐付け UID。
                    // 監査記録は cache.GetOrCreateAsync で atomic 化し TOCTOU レースを防止。
                    // factory が呼ばれた = 新規 5 分窓、cache hit = de-dup 期間中。
                    var probeKey = $"audit_logged:{firebaseUid}";
                    await cache.GetOrCreateAsync(probeKey, async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                        try
                        {
                            var audit = sp.GetRequiredService<IAuditLogger>();
                            // UID は擬似識別子。個人特定回避のため先頭 8 文字に短縮 + path はクエリストリング除外。
                            var uidShort = firebaseUid.Length > 8
                                ? firebaseUid.Substring(0, 8) + "..."
                                : firebaseUid;
                            var path = ctx.HttpContext.Request.Path.Value ?? string.Empty;
                            var note = $"uid={uidShort}, method={ctx.HttpContext.Request.Method}, path={path}";

                            if (resolved.AnyId is not null)
                            {
                                // IsActive=false ユーザの拒否。actor_user_id 付きで個別追跡可能 (SEC-15)。
                                await audit.LogAsync(resolved.AnyId.Value, "Auth.LoginRejected.Inactive",
                                    entityType: "User", entityId: resolved.AnyId.Value,
                                    note: note, success: false,
                                    cancellationToken: ctx.HttpContext.RequestAborted);
                            }
                            else
                            {
                                // users 行が無い (未紐付け or soft-deleted)。soft-deleted は Firebase Auth
                                // 側 disabled=true で防御する設計のため、ここに到達するのは原則「未紐付け
                                // 偵察試行」のみ (architecture.md §5.1 設計判断参照)。
                                await audit.LogAsync(null, "Auth.UidUnboundProbe",
                                    note: note, success: false,
                                    cancellationToken: ctx.HttpContext.RequestAborted);
                            }
                        }
                        catch (Exception ex)
                        {
                            // CloudWatch Logs への UID 露出最小化のため、try 内と同じ短縮ルールを再適用
                            // (try スコープの uidShort 変数は catch から見えないため再算出)。
                            var uidShortForLog = firebaseUid.Length > 8
                                ? firebaseUid.Substring(0, 8) + "..."
                                : firebaseUid;
                            logger.LogWarning(ex, "Auth.* audit 記録失敗 (uid={UidShort})", uidShortForLog);
                        }
                        return true;
                    });
                    return;
                }

                if (ctx.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(new System.Security.Claims.Claim(
                        AuthEndpoints.AkebonoUserIdClaim,
                        resolved.ActiveId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

                    // テナントコンテキスト (AKB-DOC-12 §10.1)。
                    // 一次ソース = Firebase Custom Claims の tenant_id (SoT は akebono-backoffice。
                    // プラットフォームのプロビジョニング/Claims 同期が接続された環境で発行される)。
                    // MVP 暫定フォールバック = users.tenant_id (backoffice 未接続の間は RDS が権威。
                    // Claims は「RDS 先行 → Custom Claims 後追い」のキャッシュ、AKB-DOC-09)。
                    var tokenTenantClaim = ctx.Principal.FindFirst("tenant_id")?.Value;
                    var isTokenTenant = false;
                    Guid? resolvedTenantId;
                    if (tokenTenantClaim is not null && Guid.TryParse(tokenTenantClaim, out var tokenTenantId))
                    {
                        isTokenTenant = true;
                        resolvedTenantId = tokenTenantId;
                    }
                    else
                    {
                        resolvedTenantId = resolved.TenantId;
                    }
                    if (resolvedTenantId is { } tenantId)
                    {
                        identity.AddClaim(new System.Security.Claims.Claim(
                            AuthEndpoints.AkebonoTenantIdClaim, tenantId.ToString()));
                        // ステータスは RDS 解決経路でのみ付与 (Custom Claims 経路では
                        // 停止 = Claims 剥奪がプラットフォーム側の責務、AKB-DOC-09)。
                        // TenantResolutionMiddleware が trial/active 以外を 403 AKB-TENANT-004 で拒否。
                        if (!isTokenTenant && resolved.TenantStatus is { } tenantStatus)
                        {
                            identity.AddClaim(new System.Security.Claims.Claim(
                                AuthEndpoints.AkebonoTenantStatusClaim, tenantStatus));
                        }
                    }
                }
            },
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Akebono Honshu API",
        Version = "v1",
        Description = "Akebono Honshu アパレル生産管理システム API (Phase 7 MVP)",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Firebase JS SDK の getIdToken() で取得した ID Token を貼り付けてください (Bearer プレフィックス不要)",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });
});

var app = builder.Build();

// wwwroot/uploads/product-images/ ディレクトリを起動時に確保 (LocalImageStorage 使用時のみ)。
// Iter 4 段階 C-1 で `ImageStorage:Provider` 切替を導入したため、S3 モード時は無関係のディレクトリ
// を作らないようガード (reviewer 指摘 auditor m3)。
var imageProvider = builder.Configuration["ImageStorage:Provider"] ?? "Local";
if (!string.Equals(imageProvider, "S3", StringComparison.OrdinalIgnoreCase))
{
    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var uploadDir = Path.Combine(webRoot, "uploads", "product-images");
    // 補助的なディレクトリ確保の失敗で起動全体を止めない (CLAUDE.md 原則 4)。
    // 本番 (EC2 docker) では named volume の所有者を deploy.sh が appuser(UID 1000) に整える。
    try
    {
        Directory.CreateDirectory(uploadDir);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "画像アップロード用ディレクトリ {UploadDir} の作成に失敗しました " +
            "(画像アップロードは失敗しますが起動は継続します)。", uploadDir);
    }
}

app.UseCors();

// 中央例外ハンドラ (AKB-DOC-12 §6.3)。CORS の後段 (エラー応答にも CORS ヘッダを載せる)、
// 認証・エンドポイントの前段に置き、DomainException 等をエラー封筒へ変換する。
app.UseMiddleware<ApiExceptionMiddleware>();

// 商品画像のローカル配信 (Iteration 2)
app.UseStaticFiles();

app.UseAuthentication();

// テナントコンテキスト確定 (認証クレーム → ITenantContext、X-Tenant-Id 突合)。
// UseAuthentication の後・UseAuthorization / エンドポイントの前に必須 (AKB-DOC-12 §10)。
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthorization();

// Swagger UI は本番では公開しない (API スキーマ漏洩防止)。
// migration-plan §4.2.3「C-2 範囲外の本番セキュリティ TODO P1-5」を段階 D で解消。
// dev/staging では従来通り /swagger を提供し、Production (ASPNETCORE_ENVIRONMENT=Production) で無効化。
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Akebono Honshu API v1");
        options.DocumentTitle = "Akebono Honshu API";
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapMasterEndpoints();
app.MapProductEndpoints();
app.MapOrderEndpoints();
app.MapProductionEndpoints();
app.MapLegacyImportEndpoints();

app.Run();
