// FF.Application/Interfaces/Services/ILeagueContextResolverService.cs
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Interfaces.Services;

public record LeagueContext(
    Guid LeagueId,
    ScoringFormat ScoringFormat,
    string LeagueType,
    int TeamCount,
    RosterConfiguration RosterConfig
);

public interface ILeagueContextResolverService
{
    Task<LeagueContext?> ResolveAsync(Guid leagueId, CancellationToken ct = default);
}