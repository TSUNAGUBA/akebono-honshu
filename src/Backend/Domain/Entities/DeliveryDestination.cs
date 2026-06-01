using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class DeliveryDestination : MasterEntityBase
{
    public string? CustomerName { get; set; }
    public string? Remark1 { get; set; }
    public string? Remark2 { get; set; }
    public string? Remark3 { get; set; }
}
