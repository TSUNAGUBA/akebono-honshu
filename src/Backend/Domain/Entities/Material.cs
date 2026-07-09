using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class Material : MasterEntityBase
{
    public Guid MaterialClassificationId { get; set; }

    public MaterialClassification? MaterialClassification { get; set; }
}
