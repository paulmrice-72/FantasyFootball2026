// FF.Application/Features/Dynasty/Commands/AnalyzeTradeCommand.cs
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

public record AnalyzeTradeCommand(
    string UserId,
    List<string> MyPlayerIds,
    List<string> TheirPlayerIds,
    List<TradePickRequest> MyPicks,
    List<TradePickRequest> TheirPicks,
    int Season,
    string? LeagueId = null,       // null = generic mode
    string? SleeperUserId = null)  // required when LeagueId is set
    : IRequest<TradeAnalysisDocument>;

public record TradePickRequest(int Round, string Tier, int Year);

public class AnalyzeTradeCommandHandler(
    ITradeAnalyzerService tradeAnalyzerService,
    ITradeAnalysisRepository tradeAnalysisRepository)
    : IRequestHandler<AnalyzeTradeCommand, TradeAnalysisDocument>
{
    public async Task<TradeAnalysisDocument> Handle(
        AnalyzeTradeCommand request,
        CancellationToken ct)
    {
        var analysis = await tradeAnalyzerService.AnalyzeAsync(
            request.UserId,
            request.MyPlayerIds,
            request.TheirPlayerIds,
            request.MyPicks,
            request.TheirPicks,
            request.Season,
            request.LeagueId,
            request.SleeperUserId,
            ct);

        await tradeAnalysisRepository.InsertAsync(analysis, ct);
        return analysis;
    }
}
