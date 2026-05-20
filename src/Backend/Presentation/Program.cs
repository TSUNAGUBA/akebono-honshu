using System.Text;
using System.Text.Json.Serialization;
using Akebono.Api.Endpoints;
using Akebono.Application.Common;
using Akebono.Infrastructure;
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

// Firebase Auth + JwtBearer 認証 (Iter 4 段階 B、3 周目レビュー反映の最終形)
// - Firebase ID Token を JWKS で検証 (issuer/audience = projectId、ValidateIssuerSigningKey 明示)
// - OnTokenValidated で users.firebase_uid → users.id を 2 段引当 (詳細は下記コメント)
// - 第 1 段 `!IsDeleted && IsActive` で hit → Claim 付与で通常認証成立
// - 第 1 段失敗時に第 2 段 `!IsDeleted` で再検索:
//   - hit → `Auth.LoginRejected.Inactive` で actor_user_id 付き監査 (SEC-15、個別追跡可能)
//   - hit せず → `Auth.UidUnboundProbe` で actor_user_id=null 監査 (偵察検知)
// - DB 障害時は warn ログ + Claim 未付与で続行 (CLAUDE.md 原則 4 非ブロッキング)
// - 監査記録は `cache.GetOrCreateAsync` で per-UID 5 分 atomic de-dup (TOCTOU レース防止 + DoS 増幅対策)
// - cache 注意: user 編集 API (段階 C 着手後 P-12) で firebase_uid 変更 / 論理削除 / IsActive 変更した際は
//   cache.Remove($"fb_uid:{uid}") と cache.Remove($"fb_uid_any:{uid}") を呼んで 60s 遅延を回避すること
// - multi-instance 注意: 段階 C で App Runner を 2 instance 以上に水平拡張する場合 IMemoryCache は
//   プロセスローカルかつ memory pressure で eviction される (de-dup の信頼性も低下)。架構判断は
//   architecture.md §4.5 参照
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
            // OnTokenValidated は 2 段 lookup + action 分離で SEC-12 / SEC-15 を満たす (3 周目レビュー P0-1 反映):
            //   1. 第 1 段 `!IsDeleted && IsActive` → hit したら Claim 付与で通常認証成立
            //   2. 第 1 段失敗時に第 2 段 `!IsDeleted` (IsActive 問わず) で users 行を再検索
            //      - hit → `Auth.LoginRejected.Inactive` を actor_user_id 付きで監査 (個別追跡可能、SEC-15)
            //      - hit せず → `Auth.UidUnboundProbe` を actor_user_id=null で監査 (未紐付け攻撃検知)
            //   3. 監査記録は `cache.GetOrCreate` で per-UID 5 分 atomic de-dup (TOCTOU レース防止、P1-1)
            // 1〜2 周目で SyncAsync 側に分散していた inactive Login.Failure は dead code 化したため
            // AuthService.cs から削除済。/auth/sync 経路スキップロジック (2 周目導入) も撤廃 (de-dup で十分)。
            OnTokenValidated = async ctx =>
            {
                var firebaseUid = AuthEndpoints.GetFirebaseUid(ctx.Principal);
                if (string.IsNullOrEmpty(firebaseUid)) return;

                var sp = ctx.HttpContext.RequestServices;
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtBearer.OnTokenValidated");
                var cache = sp.GetRequiredService<IMemoryCache>();

                long? activeActorId;
                try
                {
                    activeActorId = await cache.GetOrCreateAsync(
                        $"fb_uid:{firebaseUid}",
                        async entry =>
                        {
                            // 60s キャッシュ: firebase_uid UNIQUE index 前提で安全。
                            // ユーザ無効化・soft-delete の反映は最大 60s 遅延 (段階 C で
                            // 編集 API 追加時に cache.Remove($"fb_uid:{uid}") を呼ぶ仕組みを入れる前提)。
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                            var db = sp.GetRequiredService<IAkebonoDbContext>();
                            return await db.Users
                                .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted && u.IsActive)
                                .Select(u => (long?)u.Id)
                                .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
                        });
                }
                catch (Exception ex)
                {
                    // RDS 断・コネクションプール枯渇等で認証全体をブロックしないよう、
                    // Claim 未付与で処理続行。各 endpoint は 401 を返すが、500 propagate は防ぐ。
                    logger.LogWarning(ex,
                        "Firebase UID {Uid} → users 引当てで例外発生、Claim 未付与で処理続行", firebaseUid);
                    return;
                }

                if (activeActorId is null)
                {
                    // 第 1 段で hit せず → 第 2 段 (IsActive 問わず) で inactive/deleted の区別を行う
                    long? inactiveActorId = null;
                    try
                    {
                        inactiveActorId = await cache.GetOrCreateAsync(
                            $"fb_uid_any:{firebaseUid}",
                            async entry =>
                            {
                                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                                var db = sp.GetRequiredService<IAkebonoDbContext>();
                                return await db.Users
                                    .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted)
                                    .Select(u => (long?)u.Id)
                                    .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
                            });
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Firebase UID {Uid} → 第 2 段 users 引当てで例外発生、監査記録スキップ", firebaseUid);
                        return;
                    }

                    // 監査記録は cache.GetOrCreateAsync で atomic 化し TOCTOU レースを防止 (P1-1)。
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

                            if (inactiveActorId is not null)
                            {
                                // inactive (または soft-deleted ではないが IsActive=false) ユーザの拒否。
                                // actor_user_id 付きで記録することで個別追跡可能 (SEC-12 / SEC-15)。
                                await audit.LogAsync(inactiveActorId.Value, "Auth.LoginRejected.Inactive",
                                    entityType: "User", entityId: inactiveActorId.Value,
                                    note: note, success: false,
                                    cancellationToken: ctx.HttpContext.RequestAborted);
                            }
                            else
                            {
                                // users 行自体が無い → 未紐付け Firebase UID による偵察試行。
                                await audit.LogAsync(null, "Auth.UidUnboundProbe",
                                    note: note, success: false,
                                    cancellationToken: ctx.HttpContext.RequestAborted);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Auth.* audit 記録失敗 (uid={Uid})", firebaseUid);
                        }
                        return true;
                    });
                    return;
                }

                if (ctx.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(new System.Security.Claims.Claim(
                        AuthEndpoints.AkebonoUserIdClaim,
                        activeActorId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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

// wwwroot/uploads/product-images/ ディレクトリを起動時に確保 (Iteration 2 ローカルファイル保存)
// Iteration 4 で S3 + Pre-signed URL に置換予定
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var uploadDir = Path.Combine(webRoot, "uploads", "product-images");
Directory.CreateDirectory(uploadDir);

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
