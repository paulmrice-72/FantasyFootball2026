namespace FF.Domain.Documents;

/// <summary>
/// Stores the generated War Room Brief for one user for one week.
/// One document per user per season per week — upserted on each generation.
/// Collection: war_room_briefs
/// </summary>
public class WarRoomBriefDocument
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public bool EmailSent { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public List<LeagueBriefSection> Leagues { get; set; } = [];
    public string? CoachRileyNarrative { get; set; }
    public List<BriefPlayerHighlight> TopBoomCandidates { get; set; } = [];
    public List<BriefPlayerHighlight> BustRisks { get; set; } = [];
}

public class LeagueBriefSection
{
    public string LeagueName { get; set; } = string.Empty;
    public string SleeperLeagueId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public List<BriefPlayerHighlight> Starters { get; set; } = [];
    public List<BriefPlayerHighlight> KeyDecisions { get; set; } = [];
    public string? LeagueNarrative { get; set; }
}

public class BriefPlayerHighlight
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public string OpponentTeam { get; set; } = string.Empty;
    public decimal Median { get; set; }
    public decimal Floor { get; set; }
    public decimal Ceiling { get; set; }
    public decimal BoomProbability { get; set; }
    public decimal BustProbability { get; set; }
    public string GameScript { get; set; } = string.Empty;
    public decimal Spread { get; set; }
    public string PlayerRole { get; set; } = string.Empty;
    public string HighlightReason { get; set; } = string.Empty;
}