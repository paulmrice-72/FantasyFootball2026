using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;

// FAN-112 (2026-08-30): this off-season fallback previously ranked EVERY
// league — dynasty or redraft — by Dynasty Trade Value, since that's the
// only valuation that exists before Week 1 projections are calculated.
// Paul (redraft league) correctly flagged that dynasty trade value carries
// no signal for a one-year league. Decision: redraft leagues now rank by
// Redraft ADP instead (FFC consensus, already zombie-pruned as of FAN-105)
// — dynasty leagues are unaffected and keep the original Dynasty Value
// ranking, since that IS the right pre-season signal for them.
public class GetOffSeasonAvailablePlayersQueryHandler(
    IDynastyValuationRepository dynastyRepository,
    IRedraftAdpRepository redraftAdpRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ILeagueRepository leagueRepository,
    INflContextService nflContext)
    : IRequestHandler<GetOffSeasonAvailablePlayersQuery, IReadOnlyList<OffSeasonAvailablePlayerDto>>
{
    // Sleeper placeholder names to exclude — same set InjuryAlertSyncJob already
    // filters. Both dynasty_valuations and redraftAdpCache are sourced from the
    // full Sleeper player pool and were never filtered for these, so junk
    // records like "Duplicate Player" could rank alongside real players.
    private static readonly HashSet<string> InvalidPlayerNames =
        ["Player Invalid", "Duplicate Player", "Deprecated Player", "Test Player"];

    public async Task<IReadOnlyList<OffSeasonAvailablePlayerDto>> Handle(
        GetOffSeasonAvailablePlayersQuery request,
        CancellationToken cancellationToken)
    {
        var season = await nflContext.GetSeasonAsync();

        // League type isn't part of this query's contract — resolve it here so
        // the Blazor page/controller didn't need to change. Unknown/missing
        // league falls back to the original Dynasty Value behavior.
        var league = await leagueRepository.GetBySleeperIdAsync(
            request.LeagueId, season, cancellationToken);
        var isRedraft = league?.LeagueType == "Redraft";

        // 1 — Load rostered Sleeper IDs for this league
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.LeagueId, cancellationToken);

        var rosteredIds = leagueRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return isRedraft
            ? await BuildFromRedraftAdpAsync(request, season, rosteredIds, cancellationToken)
            : await BuildFromDynastyValueAsync(request, rosteredIds, cancellationToken);
    }

    private async Task<IReadOnlyList<OffSeasonAvailablePlayerDto>> BuildFromDynastyValueAsync(
        GetOffSeasonAvailablePlayersQuery request,
        HashSet<string> rosteredIds,
        CancellationToken cancellationToken)
    {
        // Fetch a generous buffer of top dynasty valuations
        var valuations = await dynastyRepository
            .GetTopByTradeValueAsync(200, request.Position, cancellationToken);

        // Exclude rostered players and known Sleeper placeholder records, apply top N
        var available = valuations
                    .Where(v => !string.IsNullOrEmpty(v.SleeperPlayerId)
                                && !rosteredIds.Contains(v.SleeperPlayerId)
                                && !InvalidPlayerNames.Contains(v.PlayerName))
                    .Take(request.Top)
                    .ToList();

        var collegeLookup = await BuildCollegeLookupAsync(
            available.Select(v => v.SleeperPlayerId), cancellationToken);

        return available
            .Select((v, i) => new OffSeasonAvailablePlayerDto(
                SleeperPlayerId: v.SleeperPlayerId,
                PlayerName: v.PlayerName,
                Position: v.Position,
                NflTeam: v.NflTeam,
                Age: v.Age,
                Value: v.TradeValue,
                ValueLabel: "Dynasty Value",
                Rank: i + 1,
                CollegeTeam: collegeLookup.TryGetValue(v.SleeperPlayerId, out var college)
                    ? college : null))
            .ToList();
    }

    private async Task<IReadOnlyList<OffSeasonAvailablePlayerDto>> BuildFromRedraftAdpAsync(
        GetOffSeasonAvailablePlayersQuery request,
        int season,
        HashSet<string> rosteredIds,
        CancellationToken cancellationToken)
    {
        var adpDocs = await redraftAdpRepository.GetBySeasonAsync(
            season, cancellationToken: cancellationToken);

        var available = adpDocs
            .Where(a => !string.IsNullOrEmpty(a.SleeperPlayerId)
                        && !rosteredIds.Contains(a.SleeperPlayerId)
                        && !InvalidPlayerNames.Contains(a.PlayerName)
                        && (request.Position is null || a.Position == request.Position))
            .OrderBy(a => a.Adp) // lower ADP = drafted earlier = more valuable
            .Take(request.Top)
            .ToList();

        // Age isn't stored on the ADP cache doc — pull it from SQL Players,
        // same bulk lookup this handler already does for CollegeTeam.
        var availableIds = available.Select(a => a.SleeperPlayerId).ToList();
        var players = await playerRepository.GetBySleeperIdsAsync(availableIds, cancellationToken);
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        return available
            .Select((a, i) => new OffSeasonAvailablePlayerDto(
                SleeperPlayerId: a.SleeperPlayerId,
                PlayerName: a.PlayerName,
                Position: a.Position,
                NflTeam: a.NflTeam,
                Age: playerLookup.TryGetValue(a.SleeperPlayerId, out var p) ? p.ComputedAge ?? 0 : 0,
                Value: Math.Round(a.Adp, 1),
                ValueLabel: "ADP",
                Rank: i + 1,
                CollegeTeam: playerLookup.TryGetValue(a.SleeperPlayerId, out var p2) ? p2.CollegeTeam : null))
            .ToList();
    }

    private async Task<Dictionary<string, string?>> BuildCollegeLookupAsync(
        IEnumerable<string> sleeperPlayerIds, CancellationToken cancellationToken)
    {
        var ids = sleeperPlayerIds.ToList();
        var players = await playerRepository.GetBySleeperIdsAsync(ids, cancellationToken);
        return players
            .Where(p => p.SleeperPlayerId != null && p.CollegeTeam != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p.CollegeTeam);
    }
}
