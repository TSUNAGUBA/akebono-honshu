namespace Akebono.Domain.Products;

/// <summary>
/// BOM の部位区分 (product_materials.material_role)。
/// 既存3部位 (甲皮/中底/底) を付属・副資材へ拡張。
/// </summary>
public enum MaterialRole : short
{
    /// <summary>甲皮 (upper)</summary>
    Upper = 0,
    /// <summary>中底 (insole)</summary>
    Insole = 1,
    /// <summary>底 (outsole)</summary>
    Outsole = 2,
    /// <summary>付属 (面ファスナー・中敷等)</summary>
    Accessory = 3,
    /// <summary>副資材 (値札・証紙・箱等)</summary>
    SubMaterial = 4,
}
