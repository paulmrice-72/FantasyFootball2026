// FF.Infrastructure/Services/SleeperLeagueImportService.cs
using FF.Application.Features.Leagues.Commands.ImportLeague;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalApis.Sleeper.Mappers;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SleeperLeagueImportService(
    ISleeperApiClient sleeperApi,
    FFDbContext dbContext,
    IRosterPlayerRepository rosterPlayerRepository,
    ILogger<SleeperLeagueImportService> logger)
    : ISleeperLeagueImportService
{
    private readonly ISleeperApiClient _sleeperApi = sleeperApi;
    private readonly FFDbContext _dbContext = dbContext;
    private readonly IRosterPlayerRepository _rosterPlayerRepository = rosterPlayerRepository;
    private readonly ILogger<SleeperLeagueImportService> _logger = logger;

    private const int SeasonsToImport = 2;
    private const int MaxWeeksPerSeason = 22;

    public async Task<ImportLeagueResult> ImportLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching league {LeagueId} from Sleeper", sleeperLeagueId);

        var sleeperLeague = await _sleeperApi.GetLeagueAsync(sleeperLeagueId, cancellationToken)
            ?? throw new InvalidOperationException($"League {sleeperLeagueId} not found on Sleeper");

        var season = int.TryParse(sleeperLeague.Season, out var s) ? s : DateTime.UtcNow.Year;

        var isNewLeague = false;
        var league = await _dbContext.Leagues
            .FirstOrDefaultAsync(l => l.SleeperLeagueId == sleeperLeagueId, cancellationToken);

        if (league is null)
        {
            league = League.Create(
                name: sleeperLeague.Name ?? "Unknown League",
                sleeperLeagueId: sleeperLeagueId,
                season: season,
                totalTeams: sleeperLeague.TotalRosters,
                leagueType: MapLeagueType(sleeperLeague.Settings?.Type ?? 0));
            _dbContext.Leagues.Add(league);
            isNewLeague = true;
        }
        else
        {
            league.UpdateLeagueType(MapLeagueType(sleeperLeague.Settings?.Type ?? 0));
        }

        league.UpdateAvatar(sleeperLeague.Avatar);

        if (sleeperLeague.ScoringSettings is not null)
        {
            var rec = sleeperLeague.ScoringSettings.GetValueOrDefault("rec", 1m);
            var passTd = sleeperLeague.ScoringSettings.GetValueOrDefault("pass_td", 4m);
            var bonusRecTe = sleeperLeague.ScoringSettings.GetValueOrDefault("bonus_rec_te", 0m);
            league.UpdateScoringSettings(rec, passTd, bonusRecTe);

            var draftRounds = sleeperLeague.Settings?.DraftRounds ?? 3;
            var tradePickLimit = sleeperLeague.Settings?.TradePickLimit ?? 0;
            league.UpdateDraftSettings(draftRounds, tradePickLimit);

            if (sleeperLeague.RosterPositions is { Count: > 0 })
                league.UpdateRosterPositions(sleeperLeague.RosterPositions);

            _logger.LogInformation(
                "Scoring settings synced for {LeagueName}: rec={Rec}, passTd={PassTd}, bonusRecTe={BonusRecTe}",
                league.Name, rec, passTd, bonusRecTe);
        }
        else
        {
            league.SetUpdated();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var (rostersImported, playersImported) = await ImportRostersAsync(
            league, sleeperLeagueId, cancellationToken);

        var additionalPlayers = await EnsureRosterPlayersExistAsync(
            sleeperLeagueId, cancellationToken);

        var transactionsImported = await ImportTransactionHistoryAsync(
            league, sleeperLeague, cancellationToken);

        return new ImportLeagueResult(
            LeagueName: league.Name,
            LeagueId: sleeperLeagueId,
            RostersImported: rostersImported,
            PlayersImported: playersImported + additionalPlayers,
            TransactionsImported: transactionsImported,
            WasNewLeague: isNewLeague);
    }

    public async Task SyncLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Syncing league {LeagueId}", sleeperLeagueId);

        var league = await _dbContext.Leagues
            .FirstOrDefaultAsync(l => l.SleeperLeagueId == sleeperLeagueId, cancellationToken);

        if (league is null)
        {
            _logger.LogWarning(
                "Sync requested for unknown league {LeagueId} — running full import instead",
                sleeperLeagueId);
            await ImportLeagueAsync(sleeperLeagueId, cancellationToken);
            return;
        }

        var sleeperLeague = await _sleeperApi.GetLeagueAsync(sleeperLeagueId, cancellationToken);
        if (sleeperLeague is not null)
        {
            league.UpdateLeagueType(MapLeagueType(sleeperLeague.Settings?.Type ?? 0));
            league.UpdateDraftSettings(
                sleeperLeague.Settings?.DraftRounds ?? 3,
                sleeperLeague.Settings?.TradePickLimit ?? 0);
            if (sleeperLeague.RosterPositions is { Count: > 0 })
                league.UpdateRosterPositions(sleeperLeague.RosterPositions);
            league.UpdateAvatar(sleeperLeague.Avatar);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await ImportRostersAsync(league, sleeperLeagueId, cancellationToken);

        var nflState = await _sleeperApi.GetNflStateAsync(cancellationToken);
        var currentWeek = nflState.Week;

        await ImportTransactionsForWeekAsync(league, sleeperLeagueId, currentWeek, cancellationToken);

        _logger.LogInformation("Sync complete for league {LeagueId}", sleeperLeagueId);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<(int rostersImported, int playersImported)> ImportRostersAsync(
        League league,
        string sleeperLeagueId,
        CancellationToken cancellationToken)
    {
        var sleeperRosters = await _sleeperApi.GetRostersAsync(sleeperLeagueId, cancellationToken);
        var sleeperUsers = await _sleeperApi.GetUsersInLeagueAsync(sleeperLeagueId, cancellationToken);

        // ── Compute owned picks per roster ───────────────────────────────────
        // Strategy:
        //   1. Seed: every roster owns all its own picks for current season + PickYearsOut years
        //   2. Override with /traded_picks — each entry records the CURRENT owner of a pick
        //      that has changed hands. Picks never traded remain with original roster.
        var currentSeason = league.Season;
        var yearsOut = league.PickYearsOut > 0 ? league.PickYearsOut : 2;
        var draftRounds = league.DraftRounds > 0 ? league.DraftRounds : 3;

        // pickOwnership[(season, round, originalRosterId)] = currentOwnerId
        var pickOwnership = new Dictionary<(int Season, int Round, string Original), string>();
        foreach (var roster in sleeperRosters)
        {
            var rid = roster.RosterId.ToString();
            for (var yr = 0; yr <= yearsOut; yr++)
                for (var rd = 1; rd <= draftRounds; rd++)
                    pickOwnership[(currentSeason + yr, rd, rid)] = rid;
        }

        // Apply traded picks — override ownership for picks that have moved
        List<FF.Infrastructure.ExternalApis.Sleeper.Dtos.SleeperDraftPickDto> tradedPicks;
        try
        {
            tradedPicks = await _sleeperApi.GetTradedPicksAsync(sleeperLeagueId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not fetch traded picks for league {LeagueId} — pick ownership will reflect original ownership only",
                sleeperLeagueId);
            tradedPicks = [];
        }

        foreach (var pick in tradedPicks)
        {
            if (pick.Season is null) continue;
            if (!int.TryParse(pick.Season, out var pickSeason)) continue;
            if (pickSeason < currentSeason) continue; // skip past picks

            // Sleeper traded_picks: roster_id = original owner, owner_id = current owner
            var key = (pickSeason, pick.Round, pick.RosterId.ToString());
            pickOwnership[key] = pick.OwnerId.ToString();
        }

        // Invert: build per-roster list of picks they currently own
        var ownedPicksByRoster = sleeperRosters
            .ToDictionary(r => r.RosterId.ToString(), _ => new List<RosterPickDto>());

        foreach (var ((season, round, _), currentOwner) in pickOwnership)
            if (ownedPicksByRoster.TryGetValue(currentOwner, out var list))
                list.Add(new RosterPickDto(season, round));

        // ── Upsert SQL roster records ────────────────────────────────────────
        var userLookup = sleeperUsers
            .Where(u => u.UserId is not null)
            .ToDictionary(u => u.UserId!, u => u);

        var rostersImported = 0;
        var playersTracked = 0;

        foreach (var sleeperRoster in sleeperRosters)
        {
            var rosterId = sleeperRoster.RosterId.ToString();
            var ownerName = "Unknown Owner";
            var teamName = $"Team {sleeperRoster.RosterId}";

            if (sleeperRoster.OwnerId is not null &&
                userLookup.TryGetValue(sleeperRoster.OwnerId, out var owner))
            {
                ownerName = owner.DisplayName ?? ownerName;
                teamName = owner.Metadata?.TeamName ?? ownerName;
            }

            var roster = await _dbContext.Rosters
                .FirstOrDefaultAsync(r => r.LeagueId == league.Id &&
                                          r.SleeperRosterId == rosterId, cancellationToken);
            if (roster is null)
            {
                roster = Roster.Create(
                    leagueId: league.Id,
                    ownerName: ownerName,
                    teamName: teamName,
                    sleeperRosterId: rosterId);
                _dbContext.Rosters.Add(roster);
                rostersImported++;
            }
            else
            {
                roster.SetUpdated();
            }

            playersTracked += sleeperRoster.Players?.Count ?? 0;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Imported {Count} rosters for league {LeagueId}", rostersImported, sleeperLeagueId);

        // ── Upsert MongoDB roster documents (players + picks) ────────────────
        var rosterDocs = sleeperRosters.Select(sleeperRoster =>
        {
            var rosterId = sleeperRoster.RosterId.ToString();
            var ownerName = "Unknown Owner";
            var teamName = $"Team {sleeperRoster.RosterId}";
            string? sleeperUserId = null;
            string? ownerAvatar = null;

            if (sleeperRoster.OwnerId is not null &&
                userLookup.TryGetValue(sleeperRoster.OwnerId, out var owner))
            {
                ownerName = owner.DisplayName ?? ownerName;
                teamName = owner.Metadata?.TeamName ?? ownerName;
                sleeperUserId = sleeperRoster.OwnerId;
                ownerAvatar = owner.Metadata?.Avatar ?? owner.Avatar;
            }

            ownedPicksByRoster.TryGetValue(rosterId, out var picks);

            return new RosterPlayerDocument
            {
                SleeperLeagueId = sleeperLeagueId,
                SleeperRosterId = rosterId,
                OwnerName = ownerName,
                TeamName = teamName,
                SleeperUserId = sleeperUserId,
                PlayerIds = sleeperRoster.Players ?? [],
                StarterIds = sleeperRoster.Starters ?? [],
                IrIds = sleeperRoster.Reserve ?? [],
                TaxiIds = sleeperRoster.Taxi ?? [],
                OwnedPicks = picks ?? [],
                Season = currentSeason,
                Wins = sleeperRoster.Settings?.Wins ?? 0,
                Losses = sleeperRoster.Settings?.Losses ?? 0,
                Ties = sleeperRoster.Settings?.Ties ?? 0,
                WaiverPosition = sleeperRoster.Settings?.WaiverPosition ?? 0,
                SyncedAt = DateTime.UtcNow,
                OwnerAvatar = ownerAvatar
            };
        }).ToList();

        await _rosterPlayerRepository.UpsertBatchAsync(rosterDocs, cancellationToken);

        _logger.LogInformation(
            "Persisted {Count} roster documents with pick ownership for league {LeagueId}",
            rosterDocs.Count, sleeperLeagueId);

        // Prune any roster documents Sleeper no longer returns for this
        // league — e.g. a team removed, or roster_ids renumbered after the
        // league's team count changed. Upsert alone never removes these, so
        // without this they hang around forever and get counted as extra
        // "teams" everywhere roster documents are read (Standings, Roster
        // Grades, League Teams). Same shape as the FAN-105 zombie-cache fix.
        var currentRosterIds = sleeperRosters.Select(r => r.RosterId.ToString()).ToList();
        await _rosterPlayerRepository.DeleteStaleRostersAsync(
            sleeperLeagueId, currentRosterIds, cancellationToken);

        return (rostersImported, playersTracked);
    }

    private async Task<int> EnsureRosterPlayersExistAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken)
    {
        var sleeperRosters = await _sleeperApi.GetRostersAsync(sleeperLeagueId, cancellationToken);
        var rosterPlayerIds = sleeperRosters
            .Where(r => r.Players is not null)
            .SelectMany(r => r.Players!)
            .Distinct()
            .ToList();

        if (rosterPlayerIds.Count == 0) return 0;

        var existingIds = await _dbContext.Players
            .Where(p => p.SleeperPlayerId != null && rosterPlayerIds.Contains(p.SleeperPlayerId))
            .Select(p => p.SleeperPlayerId!)
            .ToListAsync(cancellationToken);

        var missingIds = rosterPlayerIds.Except(existingIds).ToList();
        if (missingIds.Count == 0) return 0;

        _logger.LogInformation("Fetching {Count} players not yet in local DB", missingIds.Count);

        var allSleeperPlayers = await _sleeperApi.GetAllPlayersAsync(cancellationToken);
        var newPlayers = 0;

        foreach (var playerId in missingIds)
        {
            if (!allSleeperPlayers.TryGetValue(playerId, out var sleeperPlayer)) continue;
            var player = SleeperPlayerMapper.ToDomainEntity(sleeperPlayer);
            if (player is null) continue;
            _dbContext.Players.Add(player);
            newPlayers++;
        }

        if (newPlayers > 0) await _dbContext.SaveChangesAsync(cancellationToken);
        return newPlayers;
    }

    private async Task<int> ImportTransactionHistoryAsync(
        League league,
        FF.Infrastructure.ExternalApis.Sleeper.Dtos.SleeperLeagueDto sleeperLeague,
        CancellationToken cancellationToken)
    {
        var currentSeason = int.TryParse(sleeperLeague.Season, out var s) ? s : DateTime.UtcNow.Year;
        var totalImported = 0;

        for (var seasonOffset = 0; seasonOffset < SeasonsToImport; seasonOffset++)
        {
            var leagueIdForSeason = seasonOffset == 0
                ? sleeperLeague.LeagueId!
                : sleeperLeague.PreviousLeagueId;

            if (string.IsNullOrEmpty(leagueIdForSeason)) break;

            for (var week = 1; week <= MaxWeeksPerSeason; week++)
            {
                var count = await ImportTransactionsForWeekAsync(
                    league, leagueIdForSeason, week, cancellationToken);
                totalImported += count;
                await Task.Delay(50, cancellationToken);
            }
        }

        return totalImported;
    }

    private async Task<int> ImportTransactionsForWeekAsync(
        League league,
        string sleeperLeagueId,
        int week,
        CancellationToken cancellationToken)
    {
        List<FF.Infrastructure.ExternalApis.Sleeper.Dtos.SleeperTransactionDto> transactions;
        try
        {
            transactions = await _sleeperApi.GetTransactionsAsync(
                sleeperLeagueId, week, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not fetch transactions for league {LeagueId} week {Week}",
                sleeperLeagueId, week);
            return 0;
        }

        if (transactions.Count == 0) return 0;

        var imported = 0;
        foreach (var sleeperTx in transactions)
        {
            if (string.IsNullOrEmpty(sleeperTx.TransactionId)) continue;

            var exists = await _dbContext.Transactions
                .AnyAsync(t => t.SleeperTransactionId == sleeperTx.TransactionId, cancellationToken);
            if (exists) continue;

            var transaction = Domain.Entities.Transaction.Create(
                leagueId: league.Id,
                sleeperTransactionId: sleeperTx.TransactionId,
                type: sleeperTx.Type ?? "unknown",
                status: sleeperTx.Status ?? "unknown",
                createdAt: DateTimeOffset.FromUnixTimeMilliseconds(sleeperTx.Created).UtcDateTime,
                week: week,
                adds: sleeperTx.Adds,
                drops: sleeperTx.Drops);

            _dbContext.Transactions.Add(transaction);
            imported++;
        }

        if (imported > 0) await _dbContext.SaveChangesAsync(cancellationToken);
        return imported;
    }

    private static string MapLeagueType(int sleeperLeagueType) => sleeperLeagueType switch
    {
        2 => "Dynasty",
        1 => "Keeper",
        _ => "Redraft"
    };
}