using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class ProductType : MasterEntityBase
{
    public string ItemConversionCode { get; set; } = string.Empty;
    public string SizeDemographicCode { get; set; } = string.Empty;
}
