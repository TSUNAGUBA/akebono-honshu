namespace Akebono.Domain.Common;

/// <summary>
/// 17 マスタ共通の最小契約。共通テンプレート MasterService&lt;T&gt; の型制約として使う。
/// </summary>
public interface IMasterEntity
{
    Guid Id { get; set; }
    string Code { get; set; }
    string Name { get; set; }
    DateTime? DeletedAt { get; set; }
    DateTime CreatedAt { get; set; }
    Guid CreatedByUserId { get; set; }
    DateTime UpdatedAt { get; set; }
    Guid UpdatedByUserId { get; set; }
    string? LegacyId { get; set; }
}
