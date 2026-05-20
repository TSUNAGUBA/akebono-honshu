using Akebono.Api.Endpoints;
using Akebono.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkebonoInfrastructure(builder.Configuration);

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

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
        BearerFormat = "Token",
        In = ParameterLocation.Header,
        Description = "POST /api/v1/auth/login で取得した access_token を貼り付けてください (Bearer プレフィックス不要)",
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

app.Run();
