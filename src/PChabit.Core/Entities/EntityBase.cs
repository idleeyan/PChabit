using PChabit.Core.Interfaces;

namespace PChabit.Core.Entities;

public abstract class EntityBase : IEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
