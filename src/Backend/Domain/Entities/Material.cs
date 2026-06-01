using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class Material : MasterEntityBase
{
    public long MaterialClassificationId { get; set; }

    public MaterialClassification? MaterialClassification { get; set; }
}
