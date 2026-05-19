using Akebono.Api.Endpoints;
using Akebono.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkebonoInfrastructure(builder.Configuration);

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapUserEndpoints();

app.Run();
