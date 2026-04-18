// FF.Application/Features/DraftTools/Commands/SyncCombineData/SyncCombineDataCommand.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.SyncCombineData;

public record SyncCombineDataCommand(int Season) : IRequest<Result<SyncCombineDataResult>>;

public record SyncCombineDataResult(
    int Matched,
    int Unmatched,
    int TotalRows,
    TimeSpan Duration);