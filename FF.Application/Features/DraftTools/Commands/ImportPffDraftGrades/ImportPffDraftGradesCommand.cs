// FF.Application/Features/DraftTools/Commands/ImportPffDraftGrades/ImportPffDraftGradesCommand.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.ImportPffDraftGrades;

public record ImportPffDraftGradesCommand(string CsvContent, int Season)
    : IRequest<Result<ImportPffDraftGradesResult>>;

public record ImportPffDraftGradesResult(int Imported, int Unmatched, int Season);