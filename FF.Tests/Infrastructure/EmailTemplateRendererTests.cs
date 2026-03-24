// FF.Tests/Infrastructure/EmailTemplateRendererTests.cs
using FF.Domain.Documents;
using FF.Infrastructure.Services;
using FluentAssertions;

namespace FF.Tests.Infrastructure;

public class EmailTemplateRendererTests
{
    private static WarRoomBriefDocument BuildBrief(
        List<BriefPlayerHighlight>? boom = null,
        List<BriefPlayerHighlight>? bust = null,
        List<LeagueBriefSection>? leagues = null,
        string? narrative = null) => new()
        {
            UserId = "user-1",
            UserEmail = "test@example.com",
            Season = 2025,
            Week = 10,
            TopBoomCandidates = boom ?? [],
            BustRisks = bust ?? [],
            Leagues = leagues ?? [],
            CoachRileyNarrative = narrative,
            EmailSent = false
        };

    [Fact]
    public void Render_ContainsHeaderBranding()
    {
        var brief = BuildBrief();
        var html = EmailTemplateRenderer.RenderWarRoomBrief(brief);
        html.Should().Contain("FantasyCombine.AI");
    }

    [Fact]
    public void Render_ContainsSeasonAndWeek()
    {
        var brief = BuildBrief();
        var html = EmailTemplateRenderer.RenderWarRoomBrief(brief);
        html.Should().Contain("Season 2025");
        html.Should().Contain("Week 10");
    }

    [Fact]
    public void Render_WithBoomCandidate_ContainsPlayerName()
    {
        var boom = new List<BriefPlayerHighlight>
        {
            new()
            {
                PlayerName = "Justin Jefferson",
                Position = "WR",
                Median = 22.5m,
                Ceiling = 38.0m,
                Floor = 10.0m,
                BoomProbability = 0.42m,
                BustProbability = 0.10m,
                HighlightReason = "Soft CB matchup"
            }
        };
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief(boom: boom));
        html.Should().Contain("Justin Jefferson");
        html.Should().Contain("Boom Candidates");
    }

    [Fact]
    public void Render_WithBustRisk_ContainsPlayerName()
    {
        var bust = new List<BriefPlayerHighlight>
        {
            new()
            {
                PlayerName = "Davante Adams",
                Position = "WR",
                Median = 11.0m,
                Ceiling = 20.0m,
                Floor = 2.5m,
                BoomProbability = 0.12m,
                BustProbability = 0.38m,
                HighlightReason = "Shadow coverage expected"
            }
        };
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief(bust: bust));
        html.Should().Contain("Davante Adams");
        html.Should().Contain("Bust Risks");
    }

    [Fact]
    public void Render_NoBoomCandidates_OmitsBoomSection()
    {
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief());
        html.Should().NotContain("Boom Candidates");
    }

    [Fact]
    public void Render_NoBustRisks_OmitsBustSection()
    {
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief());
        html.Should().NotContain("Bust Risks");
    }

    [Fact]
    public void Render_WithCoachRileyNarrative_ContainsNarrative()
    {
        const string narrative = "Stack Mahomes with Hill this week.";
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief(narrative: narrative));
        html.Should().Contain("Coach Riley");
        html.Should().Contain(narrative);
    }

    [Fact]
    public void Render_NoNarrative_OmitsCoachRileySection()
    {
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief(narrative: null));
        html.Should().NotContain("Coach Riley");
    }

    [Fact]
    public void Render_WithLeague_ContainsLeagueName()
    {
        var leagues = new List<LeagueBriefSection>
    {
        new()
        {
            LeagueName = "Bizarro League (Redraft 2025)",
            TeamName = "Paul's Team",
            SleeperLeagueId = "league-1",
            Starters = [],
            KeyDecisions = [],
            LeagueNarrative = "Strong week ahead."
        }
    };
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief(leagues: leagues));
        html.Should().Contain("Bizarro League");
        // HTML-encodes apostrophe — check the raw string instead
        (html.Contains("Paul's Team") || html.Contains("Paul&#x27;s Team"))
            .Should().BeTrue();
    }

    [Fact]
    public void Render_ContainsCtaLink()
    {
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief());
        html.Should().Contain("my-brief");
    }

    [Fact]
    public void Render_ReturnsValidHtmlDocument()
    {
        var html = EmailTemplateRenderer.RenderWarRoomBrief(BuildBrief());
        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("</html>");
    }
}