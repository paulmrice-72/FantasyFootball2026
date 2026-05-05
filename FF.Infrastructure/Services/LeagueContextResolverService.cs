// FF.Infrastructure/Services/LeagueContextResolverService.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class LeagueContextResolverService : ILeagueContextResolverService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private const string CacheKeyPrefix = "league_ctx_";

    private readonly IUnitOfWork _uow;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LeagueContextResolverService> _logger;

    public LeagueContextResolverService(
        IUnitOfWork uow,
        IMemoryCache cache,
        ILogger<LeagueContextResolverService> logger)
    {
        _uow = uow;
        _cache = cache;
        _logger = logger;
    }

    public async Task<LeagueContext?> ResolveAsync(Guid leagueId, CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{leagueId}";

        if (_cache.TryGetValue(cacheKey, out LeagueContext? cached))
            return cached;

        var league = await _uow.Leagues.GetByIdAsync(leagueId, ct);
        if (league is null)
        {
            _logger.LogWarning("LeagueContextResolver: league {LeagueId} not found", leagueId);
            return null;
        }

        var scoringFormat = league.RecPerReception switch
        {
            >= 1m => ScoringFormat.FullPpr,
            >= 0.5m => ScoringFormat.HalfPpr,
            _ => ScoringFormat.Standard
        };

        var context = new LeagueContext(
            LeagueId: leagueId,
            ScoringFormat: scoringFormat,
            LeagueType: league.LeagueType,
            TeamCount: league.TotalTeams,
            RosterConfig: league.GetRosterConfiguration()
        );

        _cache.Set(cacheKey, context, CacheDuration);

        _logger.LogDebug(
            "LeagueContextResolver: resolved {LeagueId} → {Format}, {Type}, {Teams} teams",
            leagueId, scoringFormat, league.LeagueType, league.TotalTeams);

        return context;
    }
}