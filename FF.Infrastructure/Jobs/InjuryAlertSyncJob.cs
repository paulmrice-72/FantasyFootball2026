// FF.Infrastructure/Jobs/InjuryAlertSyncJob.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalApis.Sleeper.Mappers;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class InjuryAlertSyncJob(
    ISleeperApiClient sleeperClient,
    IInjuryAlertRepository injuryAlertRepository,
    ILogger<InjuryAlertSyncJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("InjuryAlertSyncJob starting");

        var allPlayers = await sleeperClient.GetAllPlayersAsync(ct);

        var injured = allPlayers.Values
            .Where(p => !string.IsNullOrWhiteSpace(p.InjuryStatus))
            .Select(p => new
            {
                Dto = p,
                Designation = SleeperPlayerMapper.MapInjuryDesignation(p)
            })
            .Where(x => x.Designation is not null)
            .Select(x => new InjuryAlertDocument
            {
                SleeperPlayerId = x.Dto.PlayerId ?? string.Empty,
                PlayerName = x.Dto.FullName ?? $"{x.Dto.FirstName} {x.Dto.LastName}".Trim(),
                Position = x.Dto.Position ?? string.Empty,
                NflTeam = x.Dto.Team,
                Designation = x.Designation!,
                SyncedAt = DateTime.UtcNow
            })
            .Where(a => !string.IsNullOrEmpty(a.SleeperPlayerId))
            .ToList();

        // Replace the entire collection on each sync — injury reports change week to week
        await injuryAlertRepository.DeleteAllAsync(ct);
        await injuryAlertRepository.UpsertBatchAsync(injured, ct);

        logger.LogInformation(
            "InjuryAlertSyncJob complete — {Count} players with active designations",
            injured.Count);
    }
}