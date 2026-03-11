using MediatR;

namespace FF.Application.Identity.Commands.LinkSleeperAccount;

public record LinkSleeperAccountCommand(
    string UserId,
    string SleeperUsername
) : IRequest<LinkSleeperAccountResult>;

public record LinkSleeperAccountResult(
    bool Success,
    string? SleeperUserId,
    string? ErrorMessage
);