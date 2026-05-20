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

// Firebase Auth + JwtBearer 認証 (Iter 4 段階 B、レビュー指摘反映後)
// - Firebase ID Token を JWKS で検証 (issuer/audience = projectId、ValidateIssuerSigningKey 明示)
// - OnTokenValidated で users.firebase_uid → users.id を引当て (60s IMemoryCache)、
//   ClaimsPrincipal に akebono_user_id を追加 (各 Endpoint は HttpContext.User から読むだけ)
// - 引当時は !IsDeleted のみで判定 (IsActive は SyncAsync / CheckXxx 各 endpoint で個別判定し、
//   inactive ユーザにも Login.Failure を残せる経路を確保)
// - DB 障害時は warn ログ + Claim 未付与で続行 (CLAUDE.md 原則 4 非ブロッキング)
// - users 未紐付け UID は Auth.UidUnboundProbe で監査記録 (攻撃者の偵察検知用)
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
            OnTokenValidated = async ctx =>
            {
                var firebaseUid = AuthEndpoints.GetFirebaseUid(ctx.Principal);
                if (string.IsNullOrEmpty(firebaseUid)) return;

                var sp = ctx.HttpContext.RequestServices;
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtBearer.OnTokenValidated");

                long? actorId;
                try
                {
                    var cache = sp.GetRequiredService<IMemoryCache>();
                    actorId = await cache.GetOrCreateAsync(
                        $"fb_uid:{firebaseUid}",
                        async entry =>
                        {
                            // 60s キャッシュ: firebase_uid UNIQUE index 前提で安全。
                            // ユーザ無効化・削除の反映は最大 60s 遅延 (各 endpoint で IsActive を別途検証するため実害なし)。
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
                    // RDS 断・コネクションプール枯渇等で認証全体をブロックしないよう、
                    // Claim 未付与で処理続行。各 endpoint は 401 を返すが、500 propagate は防ぐ。
                    logger.LogWarning(ex,
                        "Firebase UID {Uid} → users 引当てで例外発生、Claim 未付与で処理続行", firebaseUid);
                    return;
                }

                if (actorId is null)
                {
                    // Firebase 認証は通過したが users.firebase_uid に紐付け無し。
                    // 攻撃者の偵察検知のため監査記録 (非ブロッキング、CLAUDE.md 原則 4)。
                    try
                    {
                        var audit = sp.GetRequiredService<IAuditLogger>();
                        await audit.LogAsync(null, "Auth.UidUnboundProbe",
                            note: $"uid={firebaseUid}, path={ctx.HttpContext.Request.Path}",
                            success: false,
                            cancellationToken: ctx.HttpContext.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Auth.UidUnboundProbe audit 記録失敗");
                    }
                    return;
                }

                if (ctx.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(new System.Security.Claims.Claim(
                        AuthEndpoints.AkebonoUserIdClaim,
                        actorId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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
