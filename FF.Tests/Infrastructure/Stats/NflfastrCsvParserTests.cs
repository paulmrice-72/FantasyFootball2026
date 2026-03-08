// FF.Tests/Infrastructure/Stats/NflfastrCsvParserTests.cs
//
// Unit tests for the nflfastR CSV parser.
// Uses in-memory CSV strings — no real files needed in CI.

using FF.Infrastructure.ExternalApis.CsvImport.Parsers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Xunit;

namespace FF.Tests.Infrastructure.Stats;

public class NflfastrCsvParserTests
{
    private readonly NflfastrCsvParser _parser;

    // Minimal valid CSV header matching nflfastR player_stats format
    private const string Header =
        "player_id,player_name,player_display_name,position,position_group,headshot_url," +
        "recent_team,season,week,season_type,opponent_team," +
        "completions,attempts,passing_yards,passing_tds,interceptions," +
        "sacks,sack_yards,sack_fumbles,sack_fumbles_lost," +
        "passing_air_yards,passing_yards_after_catch,passing_first_downs,passing_epa," +
        "passing_2pt_conversions,pacr,dakota," +
        "carries,rushing_yards,rushing_tds,rushing_fumbles,rushing_fumbles_lost," +
        "rushing_first_downs,rushing_epa,rushing_2pt_conversions," +
        "receptions,targets,receiving_yards,receiving_tds,receiving_fumbles," +
        "receiving_fumbles_lost,receiving_air_yards,receiving_yards_after_catch," +
        "receiving_first_downs,receiving_epa,receiving_2pt_conversions," +
        "racr,target_share,air_yards_share,wopr," +
        "special_teams_tds,fantasy_points,fantasy_points_ppr";

    public NflfastrCsvParserTests()
    {
        _parser = new NflfastrCsvParser(NullLogger<NflfastrCsvParser>.Instance);
    }

    [Fact]
    public async Task ParseFileAsync_ValidQbRow_ReturnsDocument()
    {
        // Arrange
        var csv = Header + "\n" +
            "00-0023459,P.Mahomes,Patrick Mahomes,QB,QB,,KC,2024,1,REG,BAL," +
            "25,35,300,3,0,1,-5,0,0,280,90,18,8.5,0,0.8,0.75," +
            "5,30,1,0,0,3,1.2,0," +
            "0,0,0,0,0,0,0,0,0,0,0," +
            "0,0,0,0," +
            "0,32.0,32.0";

        var path = await WriteTempCsvAsync(csv);

        // Act
        var result = await _parser.ParseFileAsync(path);

        // Assert
        result.Should().HaveCount(1);
        var doc = result[0];
        doc.PlayerId.Should().Be("00-0023459");
        doc.PlayerName.Should().Be("P.Mahomes");
        doc.DisplayName.Should().Be("Patrick Mahomes");
        doc.Position.Should().Be("QB");
        doc.NflTeam.Should().Be("KC");
        doc.Season.Should().Be(2024);
        doc.Week.Should().Be(1);
        doc.PassingYards.Should().Be(300);
        doc.PassingTds.Should().Be(3);
        doc.FantasyPoints.Should().Be(32.0m);
        doc.DataSource.Should().Be("nflfastr");
    }

    [Fact]
    public async Task ParseFileAsync_PostseasonRow_IsSkipped()
    {
        // Arrange — POST season type should be filtered out
        var csv = Header + "\n" +
            "00-0023459,P.Mahomes,Patrick Mahomes,QB,QB,,KC,2024,1,POST,BAL," +
            "25,35,300,3,0,1,-5,0,0,280,90,18,8.5,0,0.8,0.75," +
            "5,30,1,0,0,3,1.2,0," +
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,30.0,30.0";

        var path = await WriteTempCsvAsync(csv);

        // Act
        var result = await _parser.ParseFileAsync(path);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseFileAsync_NonSkillPosition_IsSkipped()
    {
        // Arrange — LB position should be filtered out
        var csv = Header + "\n" +
            "00-0099999,T.Player,Test Player,LB,LB,,KC,2024,1,REG,BAL," +
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0," +
            "0,0,0,0,0,0,0,0," +
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0";

        var path = await WriteTempCsvAsync(csv);

        // Act
        var result = await _parser.ParseFileAsync(path);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseFileAsync_EmptyPlayerId_IsSkipped()
    {
        // Arrange — empty player_id rows are bye-week placeholders
        var csv = Header + "\n" +
            ",Unknown,Unknown,RB,RB,,FA,2024,7,REG,," +
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0," +
            "5,22,0,0,0,2,0.5,0," +
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,3.2,3.2";

        var path = await WriteTempCsvAsync(csv);

        // Act
        var result = await _parser.ParseFileAsync(path);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseFileAsync_NullableFields_DefaultToZero()
    {
        // Arrange — many stats columns are empty (e.g. a kicker has no passing stats)
        var csv = Header + "\n" +
            "00-0037077,E.McPherson,Evan McPherson,K,K,,CIN,2024,1,REG,NE," +
            ",,,,,,,,,,,,,,,," +  // 16 passing stats empty
            ",,,,,,,," +          // 8 rushing stats empty (was 9 - off by one)
            ",,,,,,,,,,," +       // 11 receiving stats empty
            ",,,," +             // 4 efficiency metrics empty
            "1,9.0,9.0";          // special_teams_tds, fantasy_points, fantasy_points_ppr

        var path = await WriteTempCsvAsync(csv);

        // Act
        var result = await _parser.ParseFileAsync(path);

        // Assert
        result.Should().HaveCount(1);
        var doc = result[0];
        doc.PassingYards.Should().Be(0);
        doc.Carries.Should().Be(0);
        doc.Targets.Should().Be(0);
        doc.SpecialTeamsTds.Should().Be(1);
        doc.FantasyPoints.Should().Be(9.0m);
    }

    [Fact]
    public async Task ParseFileAsync_MultipleRows_AllValid_ReturnsAll()
    {
        // Arrange — 3 skill position players, all REG
        var row1 = "00-0023459,P.Mahomes,Patrick Mahomes,QB,QB,,KC,2024,1,REG,BAL," +
                   "25,35,300,3,0,1,-5,0,0,280,90,18,8.5,0,0.8,0.75," +
                   "5,30,1,0,0,3,1.2,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,32.0,32.0";
        var row2 = "00-0036898,T.Hill,Tyreek Hill,WR,WR,,MIA,2024,1,REG,NE," +
                   "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0," +
                   "8,10,120,1,0,0,85,75,7,4.2,0,0.95,0.28,0.42,0.45,0,26.0,34.0";
        var row3 = "00-0034844,D.Henry,Derrick Henry,RB,RB,,TEN,2024,1,REG,OAK," +
                   "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,22,110,2,0,0,8,5.5,0," +
                   "3,4,25,0,0,0,15,18,2,0.8,0,0,0.08,0,0.06,0,29.5,30.5";

        var csv = Header + "\n" + row1 + "\n" + row2 + "\n" + row3;
        var path = await WriteTempCsvAsync(csv);

        // Act
        var result = await _parser.ParseFileAsync(path);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Position).Should().BeEquivalentTo(["QB", "WR", "RB"]);
    }

    [Fact]
    public async Task ParseFileAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _parser.ParseFileAsync("/nonexistent/path/file.csv"))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    // Helper: write CSV content to a temp file, return path
    private static async Task<string> WriteTempCsvAsync(string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        return path;
    }
}
