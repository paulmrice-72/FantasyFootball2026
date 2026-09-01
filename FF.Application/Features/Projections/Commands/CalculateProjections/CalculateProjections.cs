// FF.Application/Features/Projections/Commands/CalculateProjections/CalculateProjectionsCommand.cs
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Projections.Commands.CalculateProjections;

public record CalculateProjectionsCommand(int Season, int Week) : IRequest<Result<CalculateProjectionsResult>>;

/// <summary>
/// <paramref name="Basis"/> and <paramref name="BasisSeason"/> report what the run
/// actually projected from. If you ask for 2026 and get back
/// "PriorSeasonCarryover"/2025, every number produced is last season's — that is
/// legitimate in preseason, but it must be visible rather than silent.
///
/// <paramref name="RookiesProjected"/> counts players built from the no-history
/// prior (depth chart + combine + consensus) rather than from game logs.
/// <paramref name="RookiesSkipped"/> counts rookies with no usable signal at all —
/// those still have no projection and will render as missing downstream.
/// </summary>
public record CalculateProjectionsResult(
    int ProjectionsCalculated,
    int PlayersSkipped,
    int Season,
    int Week,
    TimeSpan Elapsed,
    string Basis = "None",
    int BasisSeason = 0,
    int RookiesProjected = 0,
    int RookiesSkipped = 0);
