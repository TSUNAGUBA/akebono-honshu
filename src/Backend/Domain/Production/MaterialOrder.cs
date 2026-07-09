using Akebono.Domain.Entities;
using Akebono.Domain.Products;

using Akebono.Domain.Common;

namespace Akebono.Domain.Production;

/// <summary>
/// 素材発注書 (生地材料発注) ヘッダ (Phase 5 data-design-production §4.4)。
/// 素材仕入先1社あての素材発注。生産指示を起点に作成 (任意で紐付け)。
/// 既存の完成品発注 (PurchaseOrder) とは別系統。
/// </summary>
public class MaterialOrder : ITenantScoped
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Idempotency-Key (AKB-DOC-12 §8)。作成 API のヘッダ値。NULL = 冪等キーなしで作成された行 (レガシー/シード)。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Idempotency-Key に対応する要求ペイロードの SHA-256 (同一キー・異ペイロード再送の検出用)。</summary>
    public string? IdempotencyPayloadHash { get; set; }

    /// <summary>素材発注番号 (例: "26-MO-00001"、作成時採番)</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>素材仕入先 (supplier 兼用)</summary>
    public long MaterialSupplierId { get; set; }
    /// <summary>起点の生産指示 (NULL 可 = 指示なしの単独素材発注も許容)</summary>
    public long? ProductionInstructionId { get; set; }

    /// <summary>素材納入希望日</summary>
    public DateOnly DueDate { get; set; }

    public MaterialOrderStatus Status { get; set; }
    /// <summary>発注確定日時。NOT NULL = 発注済 (未/済バッジの済判定根拠)</summary>
    public DateTime? InstructedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    // 帳票宛名の凍結 (既存 purchase_orders と同方針)
    public string? SupplierOfficialNameSnapshot { get; set; }
    public string? SupplierCodeSnapshot { get; set; }

    public string? CommunicationText { get; set; }
    public DateTime? FirstExportedAt { get; set; }
    public DateTime? LastExportedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    // ナビプロパティ
    public Supplier? MaterialSupplier { get; set; }
    public ProductionInstruction? ProductionInstruction { get; set; }
    public List<MaterialOrderLine> Lines { get; set; } = new();
}
