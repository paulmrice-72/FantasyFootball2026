// FF.Application/Services/WarRoomBriefService.cs
using FF.Application.Identity.Interfaces;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using FF.Infrastructure.Services;

namespace FF.Application.Services;

/// <summary>
/// Assembles the War Room Brief for a user:
/// 1. Load all active league memberships for the user
/// 2. Find their roster in each league via SleeperUserId
/// 3. Resolve starter SleeperPlayerIds → GsisIds
/// 4. Load simulation results for each starter
/// 5. Identify boom/bust candidates and key decisions
/// </summary>
public class WarRoomBriefService(
    IWarRoomBriefRepository briefRepository,
    ILeagueMembershipRepository leagueMembershipRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    ISimulationResultRepository simulationResultRepository,
    IPlayerRepository playerRepository,
    IEmailService emailService,
    ICoachRileyService coachRileyService,
    ILogger<WarRoomBriefService> logger)
{
    private const decimal BoomThreshold = 0.30m;
    private const decimal BustThreshold = 0.25m;
    private const int MaxKeyDecisions = 3;
    private const int MaxHighlights = 5;

    public async Task<WarRoomBriefDocument> GenerateBriefAsync(
        string userId,
        string userEmail,
        int season,
        int week,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Generating War Room Brief for user {UserId} Season {Season} Week {Week}",
            userId, season, week);

        var brief = new WarRoomBriefDocument
        {
            UserId = userId,
            UserEmail = userEmail,
            Season = season,
            Week = week,
            GeneratedAt = DateTime.UtcNow
        };

        var memberships = await leagueMembershipRepository
            .GetMembershipsForUserAsync(userId, ct);


        // Temporary debug
        logger.LogInformation(
            "Found {Total} memberships, {Active} active for season {Season}",
            memberships.Count,
            memberships.Count(m => m.Season == season && m.IsActive),
            season);

        var activeMemberships = memberships
            .Where(m => m.Season == season && m.IsActive)
            .ToList();

        if (activeMemberships.Count == 0)
        {
            logger.LogWarning("No active memberships for user {UserId} season {Season}",
                userId, season);
            return brief;
        }

        var allBoomCandidates = new List<BriefPlayerHighlight>();
        var allBustRisks = new List<BriefPlayerHighlight>();

        foreach (var membership in activeMemberships)
        {
            var section = await BuildLeagueSectionAsync(
                membership, season, week, ct);

            if (section is null) continue;

            brief.Leagues.Add(section);

            allBoomCandidates.AddRange(
                section.Starters.Where(p => p.BoomProbability >= BoomThreshold));
            allBustRisks.AddRange(
                section.Starters.Where(p => p.BustProbability >= BustThreshold));
        }

        brief.TopBoomCandidates = [.. allBoomCandidates
            .OrderByDescending(p => p.BoomProbability)
            .DistinctBy(p => p.PlayerId)
            .Take(MaxHighlights)];

        brief.BustRisks = [.. allBustRisks
            .OrderByDescending(p => p.BustProbability)
            .DistinctBy(p => p.PlayerId)
            .Take(MaxHighlights)];

        // Generate Coach Riley narrative
        brief.CoachRileyNarrative = await coachRileyService
            .GenerateNarrativeAsync(brief, ct);

        await briefRepository.UpsertAsync(brief, ct);
        await briefRepository.UpsertAsync(brief, ct);

        await briefRepository.UpsertAsync(brief, ct);

        logger.LogInformation(
            "War Room Brief generated — {Leagues} leagues, " +
            "{Boom} boom candidates, {Bust} bust risks",
            brief.Leagues.Count,
            brief.TopBoomCandidates.Count,
            brief.BustRisks.Count);

        // Send email if user has an email address
        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            try
            {
                var subject = $"⚡ Your Week {week} War Room Brief is ready";
                var html = EmailTemplateRenderer.RenderWarRoomBrief(brief);
                await emailService.SendWarRoomBriefAsync(userEmail, subject, html, ct);

                brief.EmailSent = true;
                brief.EmailSentAt = DateTime.UtcNow;
                await briefRepository.UpsertAsync(brief, ct);

                logger.LogInformation(
                    "War Room Brief email sent to {Email}", userEmail);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send War Room Brief email to {Email}", userEmail);
                // Don't rethrow — brief generation succeeded, email failure is non-fatal
            }
        }

        return brief;
    }


    private async Task<LeagueBriefSection?> BuildLeagueSectionAsync(
        FF.Domain.Entities.LeagueMembership membership,
        int season,
        int week,
        CancellationToken ct)
    {
        // Find the user's roster by SleeperUserId
        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(membership.LeagueId, ct);

        var userRoster = rosters.FirstOrDefault(r =>
            r.SleeperUserId == membership.SleeperUserId);

        if (userRoster is null)
        {
            logger.LogDebug(
                "No roster found for SleeperUserId {SleeperUserId} in league {LeagueId}",
                membership.SleeperUserId, membership.LeagueId);
            return null;
        }

        if (userRoster.StarterIds.Count == 0)
        {
            logger.LogDebug(
                "No starters set for roster {RosterId} — off-season",
                userRoster.SleeperRosterId);
            return null;
        }

        // Resolve each starter: SleeperPlayerId → GsisId → SimulationResult
        var starterSims = new List<SimulationResultDocument>();

        foreach (var sleeperPlayerId in userRoster.StarterIds)
        {
            var player = await playerRepository
                .GetBySleeperIdAsync(sleeperPlayerId, ct);

            if (player?.GsisId is null) continue;

            var sim = await simulationResultRepository
                .GetByPlayerAsync(player.GsisId, season, week, ct);

            if (sim is not null)
                starterSims.Add(sim);
        }

        if (starterSims.Count == 0) return null;

        var starters = starterSims
            .OrderByDescending(s => s.Median)
            .Select(s => MapToHighlight(s, "Starter"))
            .ToList();

        var keyDecisions = starters
            .Where(p => p.BustProbability >= BustThreshold ||
                        (p.BoomProbability >= BoomThreshold &&
                         p.BustProbability >= 0.15m))
            .Take(MaxKeyDecisions)
            .ToList();

        return new LeagueBriefSection
        {
            LeagueName = membership.LeagueName,
            SleeperLeagueId = membership.LeagueId,
            TeamName = userRoster.TeamName,
            Starters = starters,
            KeyDecisions = keyDecisions
        };
    }

    private static BriefPlayerHighlight MapToHighlight(
        SimulationResultDocument sim,
        string reason) => new()
        {
            PlayerId = sim.PlayerId,
            PlayerName = sim.PlayerName,
            Position = sim.Position,
            NflTeam = sim.NflTeam,
            OpponentTeam = sim.OpponentTeam,
            Median = sim.Median,
            Floor = sim.Floor,
            Ceiling = sim.Ceiling,
            BoomProbability = sim.BoomProbability,
            BustProbability = sim.BustProbability,
            GameScript = sim.GameScript,
            Spread = sim.Spread,
            PlayerRole = sim.PlayerRole,
            HighlightReason = reason
        };
}