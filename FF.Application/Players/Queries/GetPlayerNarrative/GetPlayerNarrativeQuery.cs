// FF.Application/Players/Queries/GetPlayerNarrative/GetPlayerNarrativeQuery.cs
using FF.Application.Common.Models;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Players.Queries.GetPlayerNarrative;

public record GetPlayerNarrativeQuery(string SleeperPlayerId)
    : IRequest<Result<PlayerNarrativeDto>>;

public record PlayerNarrativeDto(string Narrative, bool FromCache);