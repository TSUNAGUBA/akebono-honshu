using Akebono.Application.Auth;
using Akebono.Application.Common;
using Akebono.Application.Users;
using Akebono.Infrastructure.Audit;
using Akebono.Infrastructure.Auth;
using Akebono.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akebono.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAkebonoInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未設定");

        services.AddDbContext<AkebonoDbContext>(opt => opt.UseNpgsql(connection));
        services.AddScoped<IAkebonoDbContext>(sp => sp.GetRequiredService<AkebonoDbContext>());

        services.AddSingleton<ITokenService, DummyTokenService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserQueryService>();

        return services;
    }
}
