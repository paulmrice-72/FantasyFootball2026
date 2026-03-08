// FF.Tests/ExternalApis/SleeperApiContractTests.cs
//
// CONTRACT TESTS - these hit the REAL Sleeper API over the internet.
// They verify that Sleeper's API still returns what we expect.
// APIs change without warning sometimes - these tests catch that.
//
// These are NOT unit tests and do NOT run in CI (no internet on GitHub runners).
// Run them manually from Visual Studio when you want to verify the integration.
// The [Trait("Category", "Integration")] attribute marks them as integration tests.
//
// TO RUN MANUALLY in Visual Studio:
//   Test Explorer → filter by Trait "Category=Integration" → Run
//
// TO EXCLUDE FROM CI (already done in build.yml via no filter):
//   The CI pipeline runs: dotnet test (no category filter)
//   Add --filter "Category!=Integration" to build.yml if you want to be explicit.

using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalApis.Sleeper.Dtos;
using FluentAssertions;
using Refit;
using Xunit;

namespace FF.Tests.ExternalApis;

[Trait("Category", "Integration")]
public class SleeperApiContractTests
{
    private readonly ISleeperApiClient _client;

    public SleeperApiContractTests()
    {
        // Build the real client directly - no DI needed for contract tests
        _client = RestService.For<ISleeperApiClient>(
            new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri("https://api.sleeper.app"),
                Timeout = TimeSpan.FromSeconds(30)
            },
            new RefitSettings
            {
                ContentSerializer = new SystemTextJsonContentSerializer()
            });
    }

    [Fact(DisplayName = "GetNflState returns current season and week")]
    public async Task GetNflState_ReturnsValidState()
    {
        // Act
        var state = await _client.GetNflStateAsync();

        // Assert
        state.Should().NotBeNull();
        state.Season.Should().NotBeNullOrEmpty();
        state.Week.Should().BeGreaterThanOrEqualTo(0);
        state.SeasonType.Should().BeOneOf("pre", "regular", "post", "off");
    }

    [Fact(DisplayName = "GetAllPlayers returns populated player dictionary")]
    public async Task GetAllPlayers_ReturnsDictionary_WithNflPlayers()
    {
        // Act
        var players = await _client.GetAllPlayersAsync();

        // Assert
        players.Should().NotBeNull();
        players.Should().HaveCountGreaterThan(1000); // There are thousands of NFL players

        // Verify structure of a known active player
        // Patrick Mahomes - Sleeper player_id "4046"
        players.Should().ContainKey("4046");
        var mahomes = players["4046"];
        mahomes.FirstName.Should().Be("Patrick");
        mahomes.LastName.Should().Be("Mahomes");
        mahomes.Position.Should().Be("QB");
        mahomes.Team.Should().Be("KC");
    }

    [Fact(DisplayName = "GetUserByUsername returns user for known Sleeper username")]
    public async Task GetUserByUsername_KnownUser_ReturnsUser()
    {
        // Act - "sleeperbot" is Sleeper's own official test account, always exists
        var user = await _client.GetUserByUsernameAsync("sleeperbot");

        // Assert
        user.Should().NotBeNull();
        user.UserId.Should().NotBeNullOrEmpty();
        user.Username.Should().Be("sleeperbot");
    }

    [Fact(DisplayName = "GetLeague returns league details for valid league ID")]
    public async Task GetLeague_ValidLeagueId_ReturnsLeague()
    {
        const string demoLeagueId = "1326771899543355392";

        // Act
        var league = await _client.GetLeagueAsync(demoLeagueId);

        // Assert
        league.Should().NotBeNull();
        league.LeagueId.Should().Be(demoLeagueId);
        league.TotalRosters.Should().BeGreaterThan(0);
        league.ScoringSettings.Should().NotBeNull();
        league.ScoringSettings.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "GetRosters returns rosters for valid league ID")]
    public async Task GetRosters_ValidLeagueId_ReturnsRosters()
    {
        const string demoLeagueId = "1326771899543355392";

        // Act
        var rosters = await _client.GetRostersAsync(demoLeagueId);

        // Assert
        rosters.Should().NotBeNull();
        rosters.Should().NotBeEmpty();
        rosters.All(r => r.RosterId > 0).Should().BeTrue();
    }

    [Fact(DisplayName = "GetUsersInLeague returns users for valid league ID")]
    public async Task GetUsersInLeague_ValidLeagueId_ReturnsUsers()
    {
        const string demoLeagueId = "1326771899543355392";

        // Act
        var users = await _client.GetUsersInLeagueAsync(demoLeagueId);

        // Assert
        users.Should().NotBeNull();
        users.Should().NotBeEmpty();
        users.All(u => !string.IsNullOrEmpty(u.UserId)).Should().BeTrue();
    }
}