using FF.SharedKernel.Common;

namespace FF.SharedKernel.Domain;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id)
    {
        Id = Guard.AgainstNull(id == Guid.Empty ? null : (Guid?)id, nameof(id))
            ?? throw new ArgumentException("Id cannot be empty.", nameof(id));
    }

    protected Entity() { } // EF Core

    public Guid Id { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}