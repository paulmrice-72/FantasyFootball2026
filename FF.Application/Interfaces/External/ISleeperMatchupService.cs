// FF.Application/Interfaces/External/ISleeperMatchupService.cs
namespace FF.Application.Interfaces.External;

public record SleeperMatchupEntry(
    int MatchupId,
    int RosterId,
    List<string> Starters,
    List<string> Players);

public interface ISleeperMatchupService
{
    Task<IReadOnlyList<SleeperMatchupEntry>> GetMatchupsAsync(
        string leagueId, int week, CancellationToken ct = default);
}