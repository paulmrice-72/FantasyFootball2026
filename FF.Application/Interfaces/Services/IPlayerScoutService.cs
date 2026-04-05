// FF.Application/Interfaces/Services/IPlayerScoutService.cs
namespace FF.Application.Interfaces.Services;

public interface IPlayerScoutService
{
    Task<string?> GeneratePlayerNarrativeAsync(
        string sleeperPlayerId,
        string fullName,
        string position,
        string? nflTeam,
        int? age,
        string? collegeTeam,
        int? draftRound,
        int? draftPick,
        double dynastyScore,
        double draftCapitalScore,
        double positionalScore,
        double valuationBlendScore,
        double fantasyProsScore,
        int? fantasyProsRank,
        CancellationToken ct = default);
}