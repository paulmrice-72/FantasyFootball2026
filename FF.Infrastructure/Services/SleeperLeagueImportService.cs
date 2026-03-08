// FF.Infrastructure/Services/SleeperLeagueImportService.cs
//
// The real implementation of ISleeperLeagueImportService.
// This is where Sleeper API calls happen and data gets persisted to SQL Server.
//
// IDEMPOTENT UPSERT PATTERN used throughout:
// "Insert if not exists, update if exists" — safe to call multiple times.
// We check by SleeperLeagueId, SleeperRosterId, etc. before inserting.
// This means if the job runs twice, you get the same result as running it once.

using FF.Application.Interfaces.Services;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalApis.Sleeper.Mappers;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FF.Application.Leagues.Commands;
using FF.Application.Leagues.Commands.ImportLeague;

namespace FF.Infrastructure.Services;

public class SleeperLeagueImportService(
    ISleeperApiClient sleeperApi,
    FFDbContext dbContext,
    ILogger<SleeperLeagueImportService> logger) : ISleeperLeagueImportService
{
    private readonly ISleeperApiClient _sleeperApi = sleeperApi;
    private readonly FFDbContext _dbContext = dbContext;
    private readonly ILogger<SleeperLeagueImportService> _logger = logger;

    // Import transactions for the last 2 seasons (current + previous)
    // Sleeper seasons are stored as strings e.g. "2024", "2025"
    private const int SeasonsToImport = 2;

    // Sleeper has 18 regular season weeks + playoffs
    private const int MaxWeeksPerSeason = 22;

    public async Task<ImportLeagueResult> ImportLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: Fetch league details from Sleeper ─────────────────────
        _logger.LogInformation("Fetching league {LeagueId} from Sleeper", sleeperLeagueId);
        var sleeperLeague = await _sleeperApi.GetLeagueAsync(sleeperLeagueId, cancellationToken) ?? throw new InvalidOperationException($"League {sleeperLeagueId} not found on Sleeper");
        var season = int.TryParse(sleeperLeague.Season, out var s) ? s : DateTime.UtcNow.Year;

        // ── Step 2: Upsert the League entity ──────────────────────────────
        var isNewLeague = false;
        var league = await _dbContext.Leagues
            .FirstOrDefaultAsync(l => l.SleeperLeagueId == sleeperLeagueId, cancellationToken);

        if (league is null)
        {
            league = League.Create(
                name: sleeperLeague.Name ?? "Unknown League",
                sleeperLeagueId: sleeperLeagueId,
                season: season,
                totalTeams: sleeperLeague.TotalRosters);

            _dbContext.Leagues.Add(league);
            isNewLeague = true;
            _logger.LogInformation("Creating new league: {LeagueName}", league.Name);
        }
        else
        {
            // Update mutable fields on existing league
            league.SetUpdated();
            _logger.LogInformation("Updating existing league: {LeagueName}", league.Name);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // ── Step 3: Import users (owners) and rosters ─────────────────────
        var (rostersImported, playersImported) = await ImportRostersAsync(
            league, sleeperLeagueId, cancellationToken);

        // ── Step 4: Import players referenced by rosters ──────────────────
        // (players are already in DB from the player sync job in PBI-016,
        //  but we ensure any missing ones get created here too)
        var additionalPlayers = await EnsureRosterPlayersExistAsync(
            sleeperLeagueId, cancellationToken);

        // ── Step 5: Import transaction history (last 2 seasons) ───────────
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

        // Sync rosters (picks up ownership changes, adds/drops)
        await ImportRostersAsync(league, sleeperLeagueId, cancellationToken);

        // Get current NFL state to know which week to sync transactions for
        var nflState = await _sleeperApi.GetNflStateAsync(cancellationToken);
        var currentWeek = nflState.Week;

        // Only sync the current week's transactions
        await ImportTransactionsForWeekAsync(
            league, sleeperLeagueId, currentWeek, cancellationToken);

        _logger.LogInformation("Sync complete for league {LeagueId}", sleeperLeagueId);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task<(int rostersImported, int playersImported)> ImportRostersAsync(
        League league,
        string sleeperLeagueId,
        CancellationToken cancellationToken)
    {
        var sleeperRosters = await _sleeperApi.GetRostersAsync(sleeperLeagueId, cancellationToken);
        var sleeperUsers = await _sleeperApi.GetUsersInLeagueAsync(sleeperLeagueId, cancellationToken);

        // Build a lookup: owner_id → display info
        var userLookup = sleeperUsers
            .Where(u => u.UserId is not null)
            .ToDictionary(u => u.UserId!, u => u);

        var rostersImported = 0;
        var playersTracked = 0;

        foreach (var sleeperRoster in sleeperRosters)
        {
            var rosterId = sleeperRoster.RosterId.ToString();

            // Look up the owner's display name and team name
            var ownerName = "Unknown Owner";
            var teamName = $"Team {sleeperRoster.RosterId}";

            if (sleeperRoster.OwnerId is not null &&
                userLookup.TryGetValue(sleeperRoster.OwnerId, out var owner))
            {
                ownerName = owner.DisplayName ?? ownerName;
                teamName = owner.Metadata?.TeamName ?? ownerName;
            }

            // Upsert the roster
            var roster = await _dbContext.Rosters
                .FirstOrDefaultAsync(r =>
                    r.LeagueId == league.Id &&
                    r.SleeperRosterId == rosterId,
                    cancellationToken);

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
            "Imported {Count} rosters for league {LeagueId}",
            rostersImported, sleeperLeagueId);

        return (rostersImported, playersTracked);
    }

    private async Task<int> EnsureRosterPlayersExistAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken)
    {
        // Get all player IDs currently on rosters
        var sleeperRosters = await _sleeperApi.GetRostersAsync(sleeperLeagueId, cancellationToken);
        var rosterPlayerIds = sleeperRosters
            .Where(r => r.Players is not null)
            .SelectMany(r => r.Players!)
            .Distinct()
            .ToList();

        if (rosterPlayerIds.Count == 0)
            return 0;

        // Find which ones we don't have in our DB yet
        var existingIds = await _dbContext.Players
            .Where(p => p.SleeperPlayerId != null &&
                        rosterPlayerIds.Contains(p.SleeperPlayerId))
            .Select(p => p.SleeperPlayerId!)
            .ToListAsync(cancellationToken);

        var missingIds = rosterPlayerIds.Except(existingIds).ToList();

        if (missingIds.Count == 0)
            return 0;

        // Fetch full player data for missing players only
        _logger.LogInformation(
            "Fetching {Count} players not yet in local DB",
            missingIds.Count);

        var allSleeperPlayers = await _sleeperApi.GetAllPlayersAsync(cancellationToken);

        var newPlayers = 0;
        foreach (var playerId in missingIds)
        {
            if (!allSleeperPlayers.TryGetValue(playerId, out var sleeperPlayer))
                continue;

            var player = SleeperPlayerMapper.ToDomainEntity(sleeperPlayer);
            if (player is null) continue;

            _dbContext.Players.Add(player);
            newPlayers++;
        }

        if (newPlayers > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return newPlayers;
    }

    private async Task<int> ImportTransactionHistoryAsync(
        League league,
        FF.Infrastructure.ExternalApis.Sleeper.Dtos.SleeperLeagueDto sleeperLeague,
        CancellationToken cancellationToken)
    {
        var currentSeason = int.TryParse(sleeperLeague.Season, out var s) ? s : DateTime.UtcNow.Year;
        var totalImported = 0;

        // Import last 2 seasons
        for (var seasonOffset = 0; seasonOffset < SeasonsToImport; seasonOffset++)
        {
            var targetSeason = currentSeason - seasonOffset;

            // For previous seasons we need the previous league ID chain
            // Sleeper chains dynasty leagues via previous_league_id
            var leagueIdForSeason = seasonOffset == 0
                ? sleeperLeague.LeagueId!
                : sleeperLeague.PreviousLeagueId;

            if (string.IsNullOrEmpty(leagueIdForSeason))
            {
                _logger.LogInformation(
                    "No previous league found for season {Season}, stopping history import",
                    targetSeason);
                break;
            }

            for (var week = 1; week <= MaxWeeksPerSeason; week++)
            {
                var count = await ImportTransactionsForWeekAsync(
                    league, leagueIdForSeason, week, cancellationToken);
                totalImported += count;

                // Small delay to be respectful to Sleeper's API
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

        if (transactions.Count == 0)
            return 0;

        var imported = 0;

        foreach (var sleeperTx in transactions)
        {
            if (string.IsNullOrEmpty(sleeperTx.TransactionId))
                continue;

            // Idempotent check - skip if we already have this transaction
            var exists = await _dbContext.Transactions
                .AnyAsync(t => t.SleeperTransactionId == sleeperTx.TransactionId,
                    cancellationToken);

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

        if (imported > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return imported;
    }
}
