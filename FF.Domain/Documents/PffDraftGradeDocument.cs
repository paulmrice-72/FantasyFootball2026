// FF.Domain/Documents/PffDraftGradeDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// PFF draft grades imported via CSV post-combine.
/// Collection: pff_draft_grades
/// Grade scale: PFF uses 0-100. We store raw and normalize in the calculator.
/// </summary>
public class PffDraftGradeDocument
{
    public string Id { get; set; } = string.Empty;           // SleeperPlayerId (matched) or "unmatched-{rank}"
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public double PffGrade { get; set; }                     // 0-100 raw PFF grade
    public int? PffRank { get; set; }                        // Overall rank within class
    public int Season { get; set; }
    public DateTime ImportedAt { get; set; }
}