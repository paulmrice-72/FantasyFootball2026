// FF.Application/DraftTools/Commands/ImportFantasyProsRookieRankings/ImportFantasyProsRookieRankingsCommand.cs
using FF.Application.Common.Models;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;

/// <summary>
/// One-time annual import. Triggered manually via admin endpoint.
/// CSV columns expected: Rank, PlayerName, Position, Team, [PositionRank], [Tier]
/// SleeperPlayerId matched by fuzzy name lookup against Players table.
/// </summary>
public record ImportFantasyProsRookieRankingsCommand(
    string CsvContent,
    int Season) : IRequest<Result<ImportFantasyProsResult>>;

public record ImportFantasyProsResult(int Imported, int Unmatched, int Season);