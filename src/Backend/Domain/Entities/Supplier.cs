using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class Supplier : MasterEntityBase
{
    public string? OfficialName { get; set; }
    public string ItemConversionCode { get; set; } = string.Empty;
    public long CountryId { get; set; }
    public short SupplierType { get; set; }
    public short AlertTarget { get; set; }

    public Country? Country { get; set; }
}
