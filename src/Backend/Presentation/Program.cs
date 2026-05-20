using System.Text;
using System.Text.Json.Serialization;
using Akebono.Api.Endpoints;
using Akebono.Application.Common;
using Akebono.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// MIG-3 CSV 取込で Shift_JIS (CP932) を読込むため、CodePagesEncodingProvider を登録。
// .NET Core / .NET 5+ は既定で限定的なエンコーディングのみ対応のため必須。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkebonoInfrastructure(builder.Configuration);

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

// Firebase Auth + JwtBearer 認証 (Iter 4 段階 B)
// - Firebase ID Token を JWKS で検証 (issuer/audience = projectId)
// - OnTokenValidated で users.firebase_uid → users.id を引当て、ClaimsPrincipal に
//   akebono_user_id を追加 (各 Endpoint は HttpContext.User からこの値を読むだけ)
// - users 未紐付け Firebase UID は 401 ではなく Claim 未付与 → Endpoint 側で 403 表示
//   (/auth/sync で「Firebase は通ったが業務ユーザが紐付いていない」を区別)
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"]
    ?? throw new InvalidOperationException(
        "Firebase:ProjectId 未設定。appsettings.json または環境変数 Firebase__ProjectId で指定してください");

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
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var firebaseUid = ctx.Principal?.FindFirst("user_id")?.Value
                    ?? ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(firebaseUid)) return;

                var db = ctx.HttpContext.RequestServices.GetRequiredService<IAkebonoDbContext>();
                var actor = await db.Users
                    .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted && u.IsActive)
                    .Select(u => new { u.Id })
                    .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

                if (actor is not null && ctx.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(new System.Security.Claims.Claim(
                        AuthEndpoints.AkebonoUserIdClaim,
                        actor.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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
