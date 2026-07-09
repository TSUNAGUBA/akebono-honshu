namespace Akebono.Domain.Entities;

/// <summary>
/// テナントレジストリのローカル投影。
/// SoT はプラットフォーム (akebono-backoffice, AKB-DOC-09) であり、本アプリは
/// テナントの発行・契約状態の変更を行わない (読取専用。DB GRANT でも書込を剥奪済み)。
/// </summary>
public class Tenant
{
    public Guid TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>trial / active / suspended / terminating / terminated (text + CHECK)</summary>
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
