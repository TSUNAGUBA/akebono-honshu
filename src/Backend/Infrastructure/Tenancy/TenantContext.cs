using Akebono.Application.Common;

namespace Akebono.Infrastructure.Tenancy;

/// <summary>
/// <see cref="ITenantContext"/> のリクエストスコープ実装。
/// TenantResolutionMiddleware (Presentation) が認証クレームから解決した値を設定する。
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }

    public Guid RequireTenantId()
        => TenantId ?? throw new DomainException(
            AkbErrorCodes.TenantContextNotInjected,
            500,
            "テナントコンテキストが未確定のままテナントスコープ操作が要求されました (内部安全違反)。",
            userAction: "時間をおいて再試行し、解消しない場合は管理者へ連絡してください");
}
