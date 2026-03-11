using FF.Application.Identity.Commands.LinkSleeperAccount;
using FF.Application.Identity.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace FF.Tests.Identity;

public class LinkSleeperAccountCommandHandlerTests
{
    private readonly ISleeperIdentityService _sleeperIdentityService;
    private readonly IUserRepository _userRepository;
    private readonly LinkSleeperAccountCommandHandler _handler;

    public LinkSleeperAccountCommandHandlerTests()
    {
        _sleeperIdentityService = Substitute.For<ISleeperIdentityService>();
        _userRepository = Substitute.For<IUserRepository>();
        _handler = new LinkSleeperAccountCommandHandler(_sleeperIdentityService, _userRepository);
    }

    [Fact]
    public async Task Handle_ValidUsername_ReturnsSuccess()
    {
        // Arrange
        var command = new LinkSleeperAccountCommand("user-123", "testuser");
        var sleeperUser = new SleeperUserInfo("sleeper-456", "testuser", "Test User", null);
        var appUser = new AppUserDto("user-123", null, null, "test@example.com");

        _sleeperIdentityService.GetUserByUsernameAsync("testuser", Arg.Any<CancellationToken>())
            .Returns(sleeperUser);
        _userRepository.GetBySleeperUserIdAsync("sleeper-456", Arg.Any<CancellationToken>())
            .Returns((AppUserDto?)null);
        _userRepository.GetByIdAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(appUser);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.SleeperUserId.Should().Be("sleeper-456");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SleeperUserNotFound_ReturnsFailure()
    {
        // Arrange
        var command = new LinkSleeperAccountCommand("user-123", "nonexistent");

        _sleeperIdentityService.GetUserByUsernameAsync("nonexistent", Arg.Any<CancellationToken>())
            .Returns((SleeperUserInfo?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_SleeperAccountAlreadyLinkedToAnotherUser_ReturnsFailure()
    {
        // Arrange
        var command = new LinkSleeperAccountCommand("user-123", "testuser");
        var sleeperUser = new SleeperUserInfo("sleeper-456", "testuser", "Test User", null);
        var differentUser = new AppUserDto("user-999", "sleeper-456", "testuser", "other@example.com");

        _sleeperIdentityService.GetUserByUsernameAsync("testuser", Arg.Any<CancellationToken>())
            .Returns(sleeperUser);
        _userRepository.GetBySleeperUserIdAsync("sleeper-456", Arg.Any<CancellationToken>())
            .Returns(differentUser);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already linked");
    }

    [Fact]
    public async Task Handle_AppUserNotFound_ReturnsFailure()
    {
        // Arrange
        var command = new LinkSleeperAccountCommand("user-123", "testuser");
        var sleeperUser = new SleeperUserInfo("sleeper-456", "testuser", "Test User", null);

        _sleeperIdentityService.GetUserByUsernameAsync("testuser", Arg.Any<CancellationToken>())
            .Returns(sleeperUser);
        _userRepository.GetBySleeperUserIdAsync("sleeper-456", Arg.Any<CancellationToken>())
            .Returns((AppUserDto?)null);
        _userRepository.GetByIdAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((AppUserDto?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("User not found");
    }
}