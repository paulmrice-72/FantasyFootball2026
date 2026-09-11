// FF.Application/Features/Schedule/Commands/SyncNflScheduleCommand.cs
using MediatR;

namespace FF.Application.Features.Schedule.Commands;

/// <summary>
/// Imports the NFL regular-season schedule for a season from nflverse (FAN-178).
/// Idempotent — keyed on nflverse <c>game_id</c>, so re-running picks up newly
/// final scores and closing lines without duplicating games.
/// </summary>
public record SyncNflScheduleCommand(int Season) : IRequest<SyncNflScheduleResult>;

public record SyncNflScheduleResult(
    int Season,
    int GamesImported,
    int WeeksCovered,
    int GamesFinal,
    int GamesWithSpread,
    bool Succeeded,
    string? Message,
    TimeSpan Elapsed);
