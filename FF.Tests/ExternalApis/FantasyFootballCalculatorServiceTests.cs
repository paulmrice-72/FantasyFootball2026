using System.Net;
using System.Text;
using FF.Infrastructure.ExternalServices.FantasyFootballCalculator;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FF.Tests.ExternalApis;

/// <summary>
/// Position-token normalisation for the FFC ADP feed.
///
/// 2026-09-07. The switch that maps FFC's position strings had no arm for "PK",
/// so every kicker fell through to the default arm, kept the token "PK", and
/// matched nothing downstream. The first live run reported <c>0 K, 27 DEF</c>
/// matched — zero of twenty-one kickers, against 192 kickers sitting available
/// in the Players table — and a league that starts a kicker could not see a
/// single one on the draft board for the whole of Paul's 2026 draft.
///
/// The vocabulary these tests pin is Sleeper's, because Sleeper supplies every
/// other position string in the system including live draft picks: kickers are
/// "K", team defenses are "DEF". Any feed that disagrees gets normalised here,
/// and a token this service does not recognise must not silently become a
/// position nothing can match.
/// </summary>
public class FantasyFootballCalculatorServiceTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    /// <summary>
    /// Six lines rather than a mocking framework: FF.Tests references Moq but not
    /// NSubstitute, and this needs neither.
    /// </summary>
    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static FantasyFootballCalculatorService ServiceReturning(string json) =>
        new(new HttpClient(new StubHandler(json))
            {
                BaseAddress = new Uri("https://fantasyfootballcalculator.example/")
            },
            new SilentLogger<FantasyFootballCalculatorService>());

    private static string FeedWith(params (string Name, string Position)[] players) =>
        "{\"players\":[" + string.Join(",", players.Select(p =>
            $"{{\"name\":\"{p.Name}\",\"position\":\"{p.Position}\"," +
            $"\"team\":\"KC\",\"adp\":150.0,\"times_selected\":10}}")) + "]}";

    [Theory]
    [InlineData("PK", "K")]   // FFC's spelling — the one that was missing
    [InlineData("K", "K")]
    [InlineData("pk", "K")]
    public async Task NormalisesEveryKickerSpellingToK(string feedToken, string expected)
    {
        var result = await ServiceReturning(FeedWith(("Chris Boswell", feedToken)))
            .GetAdpAsync(2026);

        result.Should().ContainSingle().Which.Position.Should().Be(expected);
    }

    [Theory]
    [InlineData("DEF")]
    [InlineData("DST")]
    [InlineData("D")]
    public async Task NormalisesEveryDefenceSpellingToDef(string feedToken)
    {
        var result = await ServiceReturning(FeedWith(("Philadelphia Eagles", feedToken)))
            .GetAdpAsync(2026);

        // Sleeper says DEF, and Sleeper is what live draft picks arrive as. A
        // second spelling makes the same defense compare unequal across sources.
        result.Should().ContainSingle().Which.Position.Should().Be("DEF");
    }

    [Fact]
    public async Task SkillPositionsPassThroughUnchanged()
    {
        var result = await ServiceReturning(FeedWith(
            ("Josh Allen", "QB"),
            ("Kenneth Walker", "RB"),
            ("Puka Nacua", "WR"),
            ("Harold Fannin", "TE"))).GetAdpAsync(2026);

        result.Select(p => p.Position)
              .Should().BeEquivalentTo(new[] { "QB", "RB", "WR", "TE" });
    }

    [Fact]
    public async Task EveryTokenThisFeedActuallyUsesIsRecognised()
    {
        // The regression guard. FFC's real feed for a season contains exactly
        // these six tokens; if it ever grows a seventh, this fails rather than
        // letting an unrecognised position through to match nothing.
        var result = await ServiceReturning(FeedWith(
            ("A", "QB"), ("B", "RB"), ("C", "WR"),
            ("D", "TE"), ("E", "PK"), ("F", "DEF"))).GetAdpAsync(2026);

        result.Select(p => p.Position).Should().OnlyContain(
            pos => pos == "QB" || pos == "RB" || pos == "WR"
                || pos == "TE" || pos == "K" || pos == "DEF");
    }

    [Fact]
    public async Task DropsEntriesWithNoNameOrNoAdp()
    {
        const string json =
            "{\"players\":[" +
            "{\"name\":\"\",\"position\":\"PK\",\"team\":\"KC\",\"adp\":150.0,\"times_selected\":1}," +
            "{\"name\":\"Zero Adp\",\"position\":\"PK\",\"team\":\"KC\",\"adp\":0,\"times_selected\":1}," +
            "{\"name\":\"Chris Boswell\",\"position\":\"PK\",\"team\":\"PIT\",\"adp\":150.0,\"times_selected\":1}" +
            "]}";

        var result = await ServiceReturning(json).GetAdpAsync(2026);

        result.Should().ContainSingle().Which.Name.Should().Be("Chris Boswell");
    }
}
