namespace Akebono.Domain.Production;

/// <summary>
/// 生産指示書ステータス (production_instructions.status)。
/// 未/済バッジは「Issued 以上 (1,2) = 済」で判定。
/// 工程実績 (次段階) では Completed への遷移を多工程進捗へ発展。
/// </summary>
public enum ProductionInstructionStatus : short
{
    /// <summary>下書き (未発行)</summary>
    Draft = 0,
    /// <summary>発行済 (指示済 = 済)</summary>
    Issued = 1,
    /// <summary>生産完了</summary>
    Completed = 2,
    /// <summary>中止</summary>
    Cancelled = 9,
}
