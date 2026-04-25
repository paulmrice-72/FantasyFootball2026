// FF.Application/Features/DepthChart/Commands/SyncDepthChartsCommand.cs
using MediatR;

namespace FF.Application.Features.DepthChart.Commands;

public record SyncDepthChartsCommand(int Season) : IRequest<SyncDepthChartsResult>;

public record SyncDepthChartsResult(
    int Synced,
    int Skipped,
    TimeSpan Elapsed);