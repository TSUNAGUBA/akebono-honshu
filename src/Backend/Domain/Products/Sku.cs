namespace Akebono.Domain.Products;

/// <summary>
/// 11 桁品番の生成と検証を担う Value Object (Phase 5 §4.2 + sequence_no 仕様)。
///
/// 構成 (11 桁):
///   [1]   planned_year_code               1 桁 (A-K / N / Z)
///   [2]   product_types.item_conversion_code   1 桁
///   [3]   product_seasons.item_conversion_code 1 桁
///   [4-6] sequence_no                     3 桁 (4桁目=サブ分類、5-6=連番)
///   [7]   suppliers.item_conversion_code  1 桁 (factory)
///   [8-9] colors.item_conversion_code     2 桁
///   [10-11] sizes.item_conversion_code 末尾 2 桁 (例: "110M" → "10", "AS" → "AS")
///
/// Application 層は本 record の Build() で組み立て、UNIQUE 制約 (products の (tenant_id, sku))
/// で 2 重生成を防ぐ (CLAUDE.md 原則 2 冪等性)。
/// </summary>
public readonly record struct Sku(string Value)
{
    public override string ToString() => Value;

    /// <summary>SKU を組み立てる。各引数の長さは事前検証必須 (Application 層で実施)。</summary>
    /// <param name="sizeItemConversionCode">サイズの item_conversion_code (VARCHAR(4))、末尾 2 桁を使用</param>
    public static Sku Build(
        char plannedYearCode,
        char productTypeCode,
        char productSeasonCode,
        string sequenceNo,
        char factoryCode,
        string colorCode,
        string sizeItemConversionCode)
    {
        if (sequenceNo.Length != 3)
            throw new ArgumentException($"sequence_no must be 3 chars, got '{sequenceNo}'", nameof(sequenceNo));
        if (colorCode.Length != 2)
            throw new ArgumentException($"color item_conversion_code must be 2 chars, got '{colorCode}'", nameof(colorCode));
        if (sizeItemConversionCode.Length < 2)
            throw new ArgumentException($"size item_conversion_code must be >= 2 chars, got '{sizeItemConversionCode}'", nameof(sizeItemConversionCode));

        var sizeTail = sizeItemConversionCode[^2..];
        return new Sku($"{plannedYearCode}{productTypeCode}{productSeasonCode}{sequenceNo}{factoryCode}{colorCode}{sizeTail}");
    }
}
