using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class DocumentTemplateConfirmation : MasterEntityBase
{
    public string Body { get; set; } = string.Empty;
    public bool StandardPrintFlag { get; set; }
}
