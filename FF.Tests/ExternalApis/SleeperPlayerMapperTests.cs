// FF.Tests/ExternalApis/SleeperPlayerMapperTests.cs
//
// Unit tests for the Sleeper → Domain mapper.
// These run in CI (no internet required - pure logic tests).

using FF.Domain.Enums;
using FF.Infrastructure.ExternalApis.Sleeper.Dtos;
using FF.Infrastructure.ExternalApis.Sleeper.Mappers;
using FluentAssertions;
using Xunit;

namespace FF.Tests.ExternalApis;

public class SleeperPlayerMapperTests
{
    [Fact(DisplayName = "ToDomainEntity maps valid QB correctly")]
    public void ToDomainEntity_ValidQb_MapsAllFields()
    {
        // Arrange
        var dto = new SleeperPlayerDto
        {
            PlayerId = "4046",
            FirstName = "Patrick",
            LastName = "Mahomes",
            Position = "QB",
            Team = "KC",
            Age = 29,
            YearsExp = 7,
            Status = "Active"
        };

        // Act
        var player = SleeperPlayerMapper.ToDomainEntity(dto);

        // Assert
        player.Should().NotBeNull();
        player!.FirstName.Should().Be("Patrick");
        player.LastName.Should().Be("Mahomes");
        player.Position.Should().Be(Position.QB);
        player.NflTeam.Should().Be("KC");
        player.SleeperPlayerId.Should().Be("4046");
    }

    [Theory(DisplayName = "ToDomainEntity maps all fantasy-relevant positions")]
    [InlineData("QB", Position.QB)]
    [InlineData("RB", Position.RB)]
    [InlineData("WR", Position.WR)]
    [InlineData("TE", Position.TE)]
    [InlineData("K", Position.K)]
    [InlineData("DEF", Position.DEF)]
    public void ToDomainEntity_FantasyPosition_MapsCorrectly(string sleeperPosition, Position expected)
    {
        // Arrange
        var dto = new SleeperPlayerDto
        {
            PlayerId = "test-id",
            FirstName = "Test",
            LastName = "Player",
            Position = sleeperPosition
        };

        // Act
        var player = SleeperPlayerMapper.ToDomainEntity(dto);

        // Assert
        player.Should().NotBeNull();
        player!.Position.Should().Be(expected);
    }

    [Theory(DisplayName = "ToDomainEntity returns null for non-fantasy positions")]
    [InlineData("LB")]
    [InlineData("CB")]
    [InlineData("S")]
    [InlineData("DL")]
    [InlineData("OL")]
    [InlineData("OT")]
    [InlineData("DB")]
    public void ToDomainEntity_NonFantasyPosition_ReturnsNull(string position)
    {
        // Arrange
        var dto = new SleeperPlayerDto
        {
            PlayerId = "test-id",
            FirstName = "Test",
            LastName = "Player",
            Position = position
        };

        // Act
        var player = SleeperPlayerMapper.ToDomainEntity(dto);

        // Assert
        player.Should().BeNull();
    }

    [Fact(DisplayName = "ToDomainEntity returns null when first name is missing")]
    public void ToDomainEntity_MissingFirstName_ReturnsNull()
    {
        var dto = new SleeperPlayerDto { LastName = "Smith", Position = "WR" };
        SleeperPlayerMapper.ToDomainEntity(dto).Should().BeNull();
    }

    [Fact(DisplayName = "ToDomainEntity returns null when last name is missing")]
    public void ToDomainEntity_MissingLastName_ReturnsNull()
    {
        var dto = new SleeperPlayerDto { FirstName = "John", Position = "WR" };
        SleeperPlayerMapper.ToDomainEntity(dto).Should().BeNull();
    }

    [Theory(DisplayName = "MapStatus maps injury status correctly")]
    [InlineData("IR", PlayerStatus.IR)]
    [InlineData("Out", PlayerStatus.Out)]
    [InlineData("Doubtful", PlayerStatus.Doubtful)]
    [InlineData("D", PlayerStatus.Doubtful)]
    [InlineData("Questionable", PlayerStatus.Questionable)]
    [InlineData("Q", PlayerStatus.Questionable)]
    public void MapStatus_InjuryStatus_MapsCorrectly(string injuryStatus, PlayerStatus expected)
    {
        // Arrange
        var dto = new SleeperPlayerDto
        {
            Status = "Active",
            InjuryStatus = injuryStatus
        };

        // Act
        var status = SleeperPlayerMapper.MapStatus(dto);

        // Assert
        status.Should().Be(expected);
    }

    [Fact(DisplayName = "MapStatus uses injury status over general status")]
    public void MapStatus_BothStatusFields_InjuryStatusTakesPriority()
    {
        // Arrange - player is "Active" generally but "Questionable" for injury
        var dto = new SleeperPlayerDto
        {
            Status = "Active",
            InjuryStatus = "Q"
        };

        // Act
        var status = SleeperPlayerMapper.MapStatus(dto);

        // Assert
        status.Should().Be(PlayerStatus.Questionable);
    }
}