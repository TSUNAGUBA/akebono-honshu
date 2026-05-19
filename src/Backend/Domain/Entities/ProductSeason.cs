using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class ProductSeason : MasterEntityBase
{
    public string ItemConversionCode { get; set; } = string.Empty;
    public string? ConversionOrder { get; set; }
}
