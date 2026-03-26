using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

public record BuildAgingCurvesCommand : IRequest<List<AgingCurveDocument>>;