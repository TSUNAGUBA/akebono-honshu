using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class DocumentTextPurchase : MasterEntityBase
{
    public string Body { get; set; } = string.Empty;
    public bool StandardPrintFlag { get; set; }
}
