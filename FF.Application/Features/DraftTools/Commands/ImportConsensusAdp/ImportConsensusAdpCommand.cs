// FF.Application/Features/DraftTools/Commands/ImportConsensusAdp/ImportConsensusAdpCommand.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;

public record ImportConsensusAdpCommand(string CsvContent, int Season, string Source)
    : IRequest<Result<ImportConsensusAdpResult>>;

public record ImportConsensusAdpResult(int Imported, int Unmatched, int Season, string Source);