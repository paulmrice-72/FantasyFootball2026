using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FF.Tests.Infrastructure;

/// <summary>
/// FAN-139 / FAN-178. Week resolution order: admin override, then the imported
/// schedule, then the calendar heuristic.
///
/// <para>
/// The heuristic assumes week 1 begins on the first Thursday on or after
/// September 1. For 2026 that computes Thursday 3 September against a season that
/// opened on Wednesday 9 September, so production moved to week 1 six days early
/// with no job, no deploy and no log line, and every consumer querying by exact
/// week resolved nothing. It is wrong in kind rather than by a few days: in a
/// normal year the opener is the Thursday <i>after Labor Day</i>, which is not the
/// first Thursday of the month whenever September 1 falls on a Tue/Wed/Thu.
/// </para>
/// </summary>
public class NflContextServiceTests
{
    private static NflContextService Build(
        IAppSettingsRepository settingsRepo,
        INflScheduleRepository scheduleRepo) =>
        new(settingsRepo, scheduleRepo, NullLogger<NflContextService>.Instance);

    private static IAppSettingsRepository SettingsWith(int? season = null, int? week = null)
    {
        var repo = Substitute.For<IAppSettingsRepository>();
        repo.GetAsync().Returns(new AppSettingsDocument
        {
            SimulationSeasonOverride = season,
            SimulationWeekOverride = week
        });
        return repo;
    }

    private static INflScheduleRepository ScheduleReturning(int? week)
    {
        var repo = Substitute.For<INflScheduleRepository>();
        repo.GetCurrentWeekAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(week);
        return repo;
    }

    // ── The calendar heuristic itself ─────────────────────────────────────

    /// <summary>
    /// Pins the defect rather than the desired behaviour. If someone "fixes"
    /// CalcWeek in place this fails, which is the prompt to check whether the
    /// schedule path made it unnecessary instead.
    /// </summary>
    [Fact]
    public void CalcWeek_Is_SixDaysEarly_ForThe2026WednesdayOpener()
    {
        // Real Week 1: Wed 9 Sept (NE @ SEA) through Mon 14 Sept.
        var seasonOpener = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);

        NflContextService.CalcWeek(seasonOpener.AddDays(-6), 2026)
            .Should().Be(1, "the heuristic rolled to week 1 on 3 September, before a game existed");
    }

    [Fact]
    public void CalcWeek_Disagrees_WithTheRealWeek_MidWeekOne()
    {
        // 11 September 2026 is inside real Week 1.
        NflContextService.CalcWeek(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc), 2026)
            .Should().Be(2, "eight days after its assumed 3 September start — the schedule says 1");
    }

    // ── Resolution order ──────────────────────────────────────────────────

    [Fact]
    public async Task Override_Wins_OverTheSchedule()
    {
        var sut = Build(SettingsWith(week: 0), ScheduleReturning(1));

        var (_, week) = await sut.GetContextAsync();

        week.Should().Be(0,
            "a deliberate admin pin outranks the data — that is what the pin is for");
    }

    [Fact]
    public async Task Schedule_Wins_OverTheCalendarHeuristic()
    {
        var sut = Build(SettingsWith(), ScheduleReturning(1));

        var (season, week) = await sut.GetContextAsync();

        season.Should().Be(2026);
        week.Should().Be(1, "the schedule knows when games actually are");
    }

    [Fact]
    public async Task Preseason_ResolvesToWeekZero_WhenTheScheduleSaysSo()
    {
        var sut = Build(SettingsWith(), ScheduleReturning(0));

        var (_, week) = await sut.GetContextAsync();

        week.Should().Be(0);
    }

    /// <summary>
    /// Null means "no schedule imported", which is different from 0. The caller
    /// must fall back deliberately rather than reading 0 as preseason.
    /// </summary>
    [Fact]
    public async Task FallsBackToCalendar_OnlyWhenNoScheduleExists()
    {
        var now = DateTime.UtcNow;
        var sut = Build(SettingsWith(), ScheduleReturning(null));

        var (season, week) = await sut.GetContextAsync();

        week.Should().Be(NflContextService.CalcWeek(now, season));
    }

    /// <summary>
    /// The week is read on nearly every page, so a schedule lookup that throws
    /// must degrade to the old behaviour rather than take the site down.
    /// </summary>
    [Fact]
    public async Task ScheduleFailure_DegradesToCalendar_RatherThanThrowing()
    {
        var scheduleRepo = Substitute.For<INflScheduleRepository>();
        scheduleRepo
            .GetCurrentWeekAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<int?>(_ => throw new TimeoutException("mongo unreachable"));

        var sut = Build(SettingsWith(), scheduleRepo);

        var act = async () => await sut.GetContextAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetWeekAsync_And_GetContextAsync_Agree()
    {
        var sut = Build(SettingsWith(), ScheduleReturning(1));

        var week = await sut.GetWeekAsync();
        var (_, contextWeek) = await sut.GetContextAsync();

        week.Should().Be(contextWeek,
            "two entry points resolving the same question differently is how this " +
            "rule ended up with four implementations in the first place");
    }

    [Fact]
    public async Task SeasonOverride_IsHonoured_AndDrivesTheScheduleLookup()
    {
        var scheduleRepo = ScheduleReturning(5);
        var sut = Build(SettingsWith(season: 2025), scheduleRepo);

        var (season, week) = await sut.GetContextAsync();

        season.Should().Be(2025);
        week.Should().Be(5);

        await scheduleRepo.Received().GetCurrentWeekAsync(
            2025, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
