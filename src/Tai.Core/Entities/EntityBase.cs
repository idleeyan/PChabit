using Tai.Core.Interfaces;

namespace Tai.Core.Entities;

public abstract class EntityBase : IEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
