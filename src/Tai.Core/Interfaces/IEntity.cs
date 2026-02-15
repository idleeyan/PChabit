namespace Tai.Core.Interfaces;

public interface IEntity
{
    Guid Id { get; }
}

public interface ITimeSeriesEntity : IEntity
{
    DateTime Timestamp { get; }
}

public interface IAggregateRoot
{
}
