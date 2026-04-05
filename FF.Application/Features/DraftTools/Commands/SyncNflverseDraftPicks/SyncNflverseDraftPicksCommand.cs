// FF.Application/Features/DraftTools/Commands/SyncNflverseDraftPicks/SyncNflverseDraftPicksCommand.cs
using FF.Application.Common.Models;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.SyncNflverseDraftPicks;

public record SyncNflverseDraftPicksCommand(int Season) : IRequest<Result<SyncDraftPicksResult>>;

public record SyncDraftPicksResult(int Matched, int Unmatched, int Total);