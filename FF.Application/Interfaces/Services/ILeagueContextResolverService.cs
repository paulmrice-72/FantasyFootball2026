// FF.Application/Interfaces/Services/ILeagueContextResolverService.cs
using FF.Domain.ValueObjects;

namespace FF.Application.Interfaces.Services;

public enum ScoringFormat
{
    Standard = 0,
    HalfPpr = 1,
    Ppr = 2
}

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