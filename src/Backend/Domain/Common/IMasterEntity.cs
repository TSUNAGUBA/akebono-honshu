namespace Akebono.Domain.Common;

/// <summary>
/// 17 マスタ共通の最小契約。共通テンプレート MasterService&lt;T&gt; の型制約として使う。
/// </summary>
public interface IMasterEntity
{
    long Id { get; set; }
    string Code { get; set; }
    string Name { get; set; }
    bool DeleteFlag { get; set; }
    DateTime CreatedAt { get; set; }
    long CreatedByUserId { get; set; }
    DateTime UpdatedAt { get; set; }
    long UpdatedByUserId { get; set; }
    string? LegacyId { get; set; }
}
