// FF.Tests/Infrastructure/VegasLineSyncJobTests.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.ExternalServices.OddsAPI;
using FF.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FF.Tests.Infrastructure;

public class VegasLineSyncJobTests
{
    private static OddsApiGame MakeGame(
        string home, string away,
        decimal homePoint, decimal overUnder = 47.5m) =>
        new(
            Id: Guid.NewGuid().ToString(),
            CommenceTime: DateTime.UtcNow.AddDays(3),
            HomeTeam: home,
            AwayTeam: away,
            Bookmakers:
            [
                new OddsApiBookmaker("draftkings",
                [
                    new OddsApiMarket("spreads",
                    [
                        new OddsApiOutcome(home, homePoint),
                        new OddsApiOutcome(away, -homePoint)
                    ]),
                    new OddsApiMarket("totals",
                    [
                        new OddsApiOutcome("Over",  overUnder),
                        new OddsApiOutcome("Under", overUnder)
                    ])
                ])
            ]);

    [Fact]
    public async Task RunAsync_maps_spreads_and_upserts_both_teams()
    {
        var oddsClient = Substitute.For<IOddsApiClient>();
        oddsClient
            .GetNflOddsAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([MakeGame("Kansas City Chiefs", "Buffalo Bills", homePoint: 3.5m)]);

        var repo = Substitute.For<IVegasLineRepository>();
        var settings = Options.Create(new OddsApiSettings { ApiKey = "test" });
        var job = new VegasLineSyncJob(oddsClient, repo, settings,
            NullLogger<VegasLineSyncJob>.Instance);

        await job.RunAsync();

        await repo.Received(1).UpsertBatchAsync(
            Arg.Is<IEnumerable<VegasLineDocument>>(docs =>
                docs.Any(d => d.HomeTeam == "KC" && d.HomeSpread == 3.5m) &&
                docs.Any(d => d.AwayTeam == "BUF" && d.AwaySpread == -3.5m)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_skips_unresolvable_team_names()
    {
        var oddsClient = Substitute.For<IOddsApiClient>();
        oddsClient
            .GetNflOddsAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([MakeGame("Unknown FC", "Buffalo Bills", homePoint: 2.5m)]);

        var repo = Substitute.For<IVegasLineRepository>();
        var settings = Options.Create(new OddsApiSettings { ApiKey = "test" });
        var job = new VegasLineSyncJob(oddsClient, repo, settings,
            NullLogger<VegasLineSyncJob>.Instance);

        await job.RunAsync();

        await repo.Received(1).UpsertBatchAsync(
            Arg.Is<IEnumerable<VegasLineDocument>>(docs => !docs.Any()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_handles_empty_response_gracefully()
    {
        var oddsClient = Substitute.For<IOddsApiClient>();
        oddsClient
            .GetNflOddsAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var repo = Substitute.For<IVegasLineRepository>();
        var settings = Options.Create(new OddsApiSettings { ApiKey = "test" });
        var job = new VegasLineSyncJob(oddsClient, repo, settings,
            NullLogger<VegasLineSyncJob>.Instance);

        await job.RunAsync();

        await repo.DidNotReceive().UpsertBatchAsync(
            Arg.Any<IEnumerable<VegasLineDocument>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TeamNameResolver_resolves_all_32_teams()
    {
        var fullNames = new[]
        {
            "Arizona Cardinals", "Atlanta Falcons", "Baltimore Ravens", "Buffalo Bills",
            "Carolina Panthers", "Chicago Bears", "Cincinnati Bengals", "Cleveland Browns",
            "Dallas Cowboys", "Denver Broncos", "Detroit Lions", "Green Bay Packers",
            "Houston Texans", "Indianapolis Colts", "Jacksonville Jaguars",
            "Kansas City Chiefs", "Las Vegas Raiders", "Los Angeles Chargers",
            "Los Angeles Rams", "Miami Dolphins", "Minnesota Vikings",
            "New England Patriots", "New Orleans Saints", "New York Giants",
            "New York Jets", "Philadelphia Eagles", "Pittsburgh Steelers",
            "San Francisco 49ers", "Seattle Seahawks", "Tampa Bay Buccaneers",
            "Tennessee Titans", "Washington Commanders"
        };

        foreach (var name in fullNames)
            TeamNameResolver.Resolve(name).Should().NotBeNull(
                because: $"{name} must resolve to an abbreviation");
    }
}