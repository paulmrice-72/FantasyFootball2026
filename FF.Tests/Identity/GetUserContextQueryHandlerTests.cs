using FF.Application.Identity.Interfaces;
using FF.Application.Identity.Queries.GetUserContext;
using FF.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace FF.Tests.Identity;

public class GetUserContextQueryHandlerTests
{
    private readonly IUserRepository _userRepository;
    private readonly ILeagueMembershipRepository _leagueMembershipRepository;
    private readonly GetUserContextQueryHandler _handler;

    public GetUserContextQueryHandlerTests()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _leagueMembershipRepository = Substitute.For<ILeagueMembershipRepository>();
        _handler = new GetUserContextQueryHandler(_userRepository, _leagueMembershipRepository);
    }

    [Fact]
    public async Task Handle_UserWithLinkedSleeper_ReturnsContext()
    {
        var query = new GetUserContextQuery("user-123");
        var user = new AppUserDto("user-123", "sleeper-456", "testuser", "test@example.com");
        var leagues = new List<LeagueContext>
        {
            new("league-1", "Test League", 2025, "member", true)
        };

        _userRepository.GetByIdAsync("user-123", Arg.Any<CancellationToken>()).Returns(user);
        _leagueMembershipRepository.GetLeaguesForUserAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LeagueContext>)leagues);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsSleeperLinked.Should().BeTrue();
        result.SleeperUsername.Should().Be("testuser");
        result.Leagues.Should().HaveCount(1);
        result.ActiveLeagueId.Should().Be("league-1");
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNull()
    {
        var query = new GetUserContextQuery("user-999");
        _userRepository.GetByIdAsync("user-999", Arg.Any<CancellationToken>())
            .Returns((AppUserDto?)null);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UserWithNoSleeper_IsSleeperLinkedFalse()
    {
        var query = new GetUserContextQuery("user-123");
        var user = new AppUserDto("user-123", null, null, "test@example.com");

        _userRepository.GetByIdAsync("user-123", Arg.Any<CancellationToken>()).Returns(user);
        _leagueMembershipRepository.GetLeaguesForUserAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LeagueContext>)new List<LeagueContext>());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsSleeperLinked.Should().BeFalse();
        result.Leagues.Should().BeEmpty();
        result.ActiveLeagueId.Should().BeNull();
    }
}
