using FF.Domain.ValueObjects;
using MediatR;

namespace FF.Application.Identity.Queries.GetUserContext;

public record GetUserContextQuery(string UserId) : IRequest<UserContext?>;