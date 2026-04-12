// FF.Tests/Infrastructure/ProjectionRefreshJobTests.cs
using FF.Application.Features.Projections.Commands.CalculateProjections;
using FF.Application.Features.Simulations.Commands.RunSimulations;
using FF.Application.Interfaces.Services;
using FF.Infrastructure.Jobs;
using FF.SharedKernel.Common;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NSubstitute;
using Xunit;

namespace FF.Tests.Infrastructure;

public class ProjectionRefreshJobTests
{
    private static ProjectionRefreshJob BuildJob(IMediator mediator, int season = 2024, int week = 1)
    {
        var nflContext = new Mock<INflContextService>();
        nflContext.Setup(x => x.GetContextAsync()).ReturnsAsync((season, week));
        return new ProjectionRefreshJob(mediator, nflContext.Object, Mock.Of<ILogger<ProjectionRefreshJob>>());
    }

    private static IMediator BuildMediator(bool projSuccess = true, bool simSuccess = true)
    {
        var mediator = Substitute.For<IMediator>();

        var projResult = projSuccess
            ? Result.Success(new CalculateProjectionsResult(10, 2, 2024, 18, TimeSpan.FromSeconds(1)))
            : Result.Failure<CalculateProjectionsResult>(new Error("Proj.Failed", "fail"));

        var simResult = simSuccess
            ? Result.Success(new RunSimulationsResult(10, 2, 2024, 18, TimeSpan.FromSeconds(1)))
            : Result.Failure<RunSimulationsResult>(new Error("Sim.Failed", "fail"));

        mediator.Send(Arg.Any<CalculateProjectionsCommand>(), Arg.Any<CancellationToken>())
            .Returns(projResult);
        mediator.Send(Arg.Any<RunSimulationsCommand>(), Arg.Any<CancellationToken>())
            .Returns(simResult);

        return mediator;
    }

    [Fact]
    public async Task RunAsync_calls_calculate_then_simulate()
    {
        var mediator = BuildMediator();
        var job = BuildJob(mediator);

        await job.RunAsync("Test");

        await mediator.Received(1).Send(
            Arg.Any<CalculateProjectionsCommand>(), Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(
            Arg.Any<RunSimulationsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_stops_if_projection_fails()
    {
        var mediator = BuildMediator(projSuccess: false);
        var job = BuildJob(mediator);

        await job.RunAsync("Test");

        // Simulation should NOT be called if projection failed
        await mediator.DidNotReceive().Send(
            Arg.Any<RunSimulationsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_logs_and_returns_if_simulation_fails()
    {
        var mediator = BuildMediator(simSuccess: false);
        var job = BuildJob(mediator);

        // Should not throw
        var act = async () => await job.RunAsync("Test");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TnfRefreshJob_delegates_to_refresh_job()
    {
        var mediator = BuildMediator();
        var refreshJob = BuildJob(mediator);
        var tnfJob = new TnfRefreshJob(refreshJob,
            NullLogger<TnfRefreshJob>.Instance);

        await tnfJob.RunAsync();

        await mediator.Received(1).Send(
            Arg.Any<CalculateProjectionsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SundayRefreshJob_delegates_to_refresh_job()
    {
        var mediator = BuildMediator();
        var refreshJob = BuildJob(mediator);
        var sundayJob = new SundayRefreshJob(refreshJob,
            NullLogger<SundayRefreshJob>.Instance);

        await sundayJob.RunAsync();

        await mediator.Received(1).Send(
            Arg.Any<CalculateProjectionsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MnfRefreshJob_delegates_to_refresh_job()
    {
        var mediator = BuildMediator();
        var refreshJob = BuildJob(mediator);
        var mnfJob = new MnfRefreshJob(refreshJob,
            NullLogger<MnfRefreshJob>.Instance);

        await mnfJob.RunAsync();

        await mediator.Received(1).Send(
            Arg.Any<CalculateProjectionsCommand>(), Arg.Any<CancellationToken>());
    }
}