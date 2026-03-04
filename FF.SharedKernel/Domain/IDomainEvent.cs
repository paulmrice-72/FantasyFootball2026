using MediatR;

namespace FF.SharedKernel.Domain;

public interface IDomainEvent : INotification
{
    Guid Id { get; }
    DateTime OccurredOnUtc { get; }
}