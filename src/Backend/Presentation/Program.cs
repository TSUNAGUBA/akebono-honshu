using System.Text;
using System.Text.Json.Serialization;
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

// (Iter 4 段階 B 後続レビュー指摘 SA P1-2 で削除)
// 旧: AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
// SystemTime.Now が常に Kind=Unspecified を返す設計に統一できているため Legacy switch は不要。
// 残すと将来 timestamptz 追加時の silent conversion bug の温床になる (レビュー指摘)。

var builder = WebApplication.CreateBuilder(args);

// AWS Secrets Manager 経由で秘密情報 (DB 接続文字列等) を IConfiguration に注入 (Iter 4 段階 C-2)。
// - dev/test/CI: `Secrets:Provider=Environment` (default) → 何もしない。環境変数 / User Secrets /
//   appsettings.Development.json で値が解決される (既存挙動)。
// - prod (App Runner): 環境変数 `Secrets__Provider=AwsSecretsManager` + `Secrets__AwsPrefix=akebono/prod/`
//   で有効化。`ConnectionStrings:Postgres` 等が Secrets Manager 経由で上書きされる。
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
    .WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    // Content-Disposition は CORS のデフォルト simple response header に
    // 含まれないため明示 expose。Frontend の Excel ダウンロード (useOrders.
    // downloadExcel) で filename 抽出に使用 (Iter 3 O-06)。
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
// - multi-instance 注意: 段階 C で App Runner を 2 instance 以上に水平拡張する場合 IMemoryCache は
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

                (long? ActiveId, long? AnyId) resolved;
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
                            var row = await db.Users
                                .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted)
                                .Select(u => new { u.Id, u.IsActive })
                                .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
                            if (row is null)
                            {
                                return ((long?)null, (long?)null);
                            }
                            return (row.IsActive ? (long?)row.Id : null, (long?)row.Id);
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
        Description = "あけぼの本州 アパレル生産管理システム API (Phase 7 MVP)",
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
    Directory.CreateDirectory(uploadDir);
}

app.UseCors();

// 商品画像のローカル配信 (Iteration 2)
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Akebono Honshu API v1");
    options.DocumentTitle = "Akebono Honshu API";
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapMasterEndpoints();
app.MapProductEndpoints();
app.MapOrderEndpoints();
app.MapLegacyImportEndpoints();

app.Run();
