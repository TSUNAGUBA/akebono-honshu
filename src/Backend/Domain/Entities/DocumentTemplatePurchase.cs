using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class DocumentTemplatePurchase : MasterEntityBase
{
    public string Body { get; set; } = string.Empty;
}
