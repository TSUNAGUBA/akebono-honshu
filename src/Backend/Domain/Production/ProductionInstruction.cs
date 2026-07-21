using Akebono.Domain.Entities;
using Akebono.Domain.Products;

using Akebono.Domain.Common;

namespace Akebono.Domain.Production;

/// <summary>
/// 生産指示書 (加工指図書) ヘッダ (Phase 5 data-design-production §4.2)。
/// 品番1つを工場 (加工先) で生産1回ぶん指示する単位。
/// 既存 PurchaseOrder と同じ「ヘッダ＋明細＋snapshot凍結＋first/last_exported_at」パターン。
/// </summary>
public class ProductionInstruction : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Idempotency-Key (AKB-DOC-12 §8)。作成 API のヘッダ値。NULL = 冪等キーなしで作成された行 (レガシー/シード)。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Idempotency-Key に対応する要求ペイロードの SHA-256 (同一キー・異ペイロード再送の検出用)。</summary>
    public string? IdempotencyPayloadHash { get; set; }

    /// <summary>生産指示番号 (例: "26-PI-00001"、作成時採番)</summary>
    public string InstructionNo { get; set; } = string.Empty;

    public Guid ProductFamilyId { get; set; }
    /// <summary>加工先 (工場、supplier 兼用)</summary>
    public Guid FactorySupplierId { get; set; }

    /// <summary>生産総数量 (明細合計と一致)</summary>
    public int PlannedQuantity { get; set; }
    /// <summary>希望納期 (工場出荷希望日)</summary>
    public DateOnly DueDate { get; set; }

    public ProductionInstructionStatus Status { get; set; }
    /// <summary>発行 (指示) 日時。NOT NULL = 指示済 (未/済バッジの済判定根拠)</summary>
    public DateTime? InstructedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    // 帳票宛名・表示の凍結 (初回 Excel 出力時にコピー)
    public string? FactoryOfficialNameSnapshot { get; set; }
    public string? FactoryCodeSnapshot { get; set; }
    public string? ProductSku9Snapshot { get; set; }
    public string? ProductNameSnapshot { get; set; }

    public string? CommunicationText { get; set; }
    public DateTime? FirstExportedAt { get; set; }
    public DateTime? LastExportedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    // ナビプロパティ
    public ProductFamily? ProductFamily { get; set; }
    // 工場 (Part2)。加工先。従来 Supplier を兼用していたが工場マスタへ分離。
    public Factory? FactorySupplier { get; set; }
    public List<ProductionInstructionLine> Lines { get; set; } = new();
}
