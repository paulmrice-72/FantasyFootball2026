// FF.Tests/Application/Stats/GetDataQualityQueryHandlerTests.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Stats.Queries.GetDataQuality;
using FluentAssertions;
using Moq;
using System.Text;

namespace FF.Tests.Application.Stats;

public class DataQualityRulesTests
{
    [Fact]
    public void ExpectedSeasonCounts_ContainsAllThreeSeasons()
    {
        DataQualityRules.ExpectedSeasonCounts.Should().ContainKey(2022);
        DataQualityRules.ExpectedSeasonCounts.Should().ContainKey(2023);
        DataQualityRules.ExpectedSeasonCounts.Should().ContainKey(2024);
    }

    [Theory]
    [InlineData(2022, 5250, true)]   // known good count
    [InlineData(2022, 4986, false)]  // just below minimum
    [InlineData(2022, 5513, false)]  // just above maximum
    [InlineData(2023, 5293, true)]   // known good count
    [InlineData(2024, 5226, true)]   // known good count
    [InlineData(2024, 0, false)]     // empty — critical
    public void SeasonQualityResult_CountInRange_ReturnsCorrectly(
        int season, long count, bool expectedInRange)
    {
        var (min, max) = DataQualityRules.ExpectedSeasonCounts[season];

        var result = new SeasonQualityResult
        {
            Season = season,
            DocumentCount = count,
            ExpectedMinimum = min,
            ExpectedMaximum = max
        };

        result.CountInRange.Should().Be(expectedInRange);
    }

    [Fact]
    public void ValidPositions_ContainsAllExpectedPositions()
    {
        DataQualityRules.ValidPositions.Should().Contain("QB");
        DataQualityRules.ValidPositions.Should().Contain("RB");
        DataQualityRules.ValidPositions.Should().Contain("WR");
        DataQualityRules.ValidPositions.Should().Contain("TE");
        DataQualityRules.ValidPositions.Should().Contain("K");
    }

    [Fact]
    public void RequiredPositions_DoesNotRequireKicker()
    {
        DataQualityRules.RequiredPositions.Should().Contain("QB");
        DataQualityRules.RequiredPositions.Should().Contain("RB");
        DataQualityRules.RequiredPositions.Should().Contain("WR");
        DataQualityRules.RequiredPositions.Should().Contain("TE");
        DataQualityRules.RequiredPositions.Should().NotContain("K");
    }

    [Fact]
    public void DataQualityReport_OverallStatus_IsCritical_WhenCriticalIssuesExist()
    {
        var report = new DataQualityReport();
        report.Issues.Add(new DataQualityIssue
        {
            Rule = DataQualityRules.RuleSeasonCount,
            Description = "Season 2022 has no documents",
            Severity = IssueSeverity.Critical,
            Season = 2022
        });

        // Simulate what the handler does
        report.OverallStatus = report.CriticalIssues > 0
            ? DataQualityStatus.Critical
            : report.WarningIssues > 0
                ? DataQualityStatus.Warning
                : DataQualityStatus.Healthy;

        report.OverallStatus.Should().Be(DataQualityStatus.Critical);
        report.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void DataQualityReport_OverallStatus_IsWarning_WhenOnlyWarningsExist()
    {
        var report = new DataQualityReport();
        report.Issues.Add(new DataQualityIssue
        {
            Rule = DataQualityRules.RuleStatRange,
            Description = "Suspicious fantasy points value",
            Severity = IssueSeverity.Warning,
            Season = 2024
        });

        report.OverallStatus = report.CriticalIssues > 0
            ? DataQualityStatus.Critical
            : report.WarningIssues > 0
                ? DataQualityStatus.Warning
                : DataQualityStatus.Healthy;

        report.OverallStatus.Should().Be(DataQualityStatus.Warning);
        report.CriticalIssues.Should().Be(0);
        report.WarningIssues.Should().Be(1);
    }

    [Fact]
    public void DataQualityReport_IsHealthy_WhenNoIssues()
    {
        var report = new DataQualityReport
        {
            OverallStatus = DataQualityStatus.Healthy,
            TotalDocuments = 15769
        };

        report.IsHealthy.Should().BeTrue();
        report.TotalIssues.Should().Be(0);
        report.CriticalIssues.Should().Be(0);
        report.WarningIssues.Should().Be(0);
    }

    [Fact]
    public void DataQualityIssue_PopulatesAllFields_Correctly()
    {
        var issue = new DataQualityIssue
        {
            Rule = DataQualityRules.RuleMissingPlayerId,
            Description = "Document has empty PlayerId",
            Severity = IssueSeverity.Critical,
            Season = 2024,
            PlayerName = "Test Player",
            FieldName = "PlayerId"
        };

        issue.Rule.Should().Be(DataQualityRules.RuleMissingPlayerId);
        issue.Severity.Should().Be(IssueSeverity.Critical);
        issue.Season.Should().Be(2024);
    }
}
