using FF.Application.Common.Models;
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.ImportFantasyProsDynastyRankings;

/// <summary>
/// Annual import of the full FantasyPros dynasty overall rankings CSV.
/// Matches against ALL active players (not just rookies).
/// Stores to fantasyPros_rookie_rankings collection with RankingType = "Dynasty".
/// Used by the calibration harness to benchmark our dynasty_valuations.
/// CSV format: "RK",TIERS,"PLAYER NAME",TEAM,"POS","AGE","BEST","WORST","AVG.","STD.DEV","ECR VS. ADP"
/// </summary>
public record ImportFantasyProsDynastyRankingsCommand(
    string CsvContent,
    int Season) : IRequest<Result<ImportFantasyProsResult>>;