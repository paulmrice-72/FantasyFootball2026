// FF.Infrastructure/Persistence/Mongo/Documents/PlayerGameLogDocument.cs
//
// MongoDB document representing a single player's stats for one game/week.
// This is the primary data store for the analytics engine.
//
// WHY MONGODB FOR THIS?
// Game logs are document-shaped — each record is a self-contained snapshot
// of a player's performance. They are read-heavy (projection engine reads
// thousands at once) and never need relational joins. MongoDB handles this
// pattern much better than SQL Server for analytics workloads.
//
// COLLECTION: PlayerGameLogs
// INDEX: (SleeperPlayerId, Season, Week) — unique, primary query pattern
// INDEX: (Season, Week) — for bulk weekly queries
// INDEX: (NflTeam, Season) — for team-level analysis

namespace FF.Domain.Documents;

public class PlayerGameLogDocument
{
    public string? Id { get; set; }

    // ── Player Identity ───────────────────────────────────────────────────
    public string PlayerId { get; set; } = string.Empty;       // nflfastR player_id
    public string PlayerName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;       // QB/RB/WR/TE/K
    public string NflTeam { get; set; } = string.Empty;
    public string? OpponentTeam { get; set; }
    public string? HeadshotUrl { get; set; }
    public string? SleeperPlayerId { get; set; }               // linked after import

    // ── Game Context ──────────────────────────────────────────────────────
    public int Season { get; set; }
    public int Week { get; set; }
    public string SeasonType { get; set; } = string.Empty;     // "REG", "POST"

    // ── Passing Stats ─────────────────────────────────────────────────────
    public int Completions { get; set; }
    public int Attempts { get; set; }
    public decimal PassingYards { get; set; }
    public int PassingTds { get; set; }
    public int Interceptions { get; set; }
    public int Sacks { get; set; }
    public decimal SackYards { get; set; }
    public int SackFumbles { get; set; }
    public int SackFumblesLost { get; set; }
    public decimal PassingAirYards { get; set; }
    public decimal PassingYardsAfterCatch { get; set; }
    public int PassingFirstDowns { get; set; }
    public decimal PassingEpa { get; set; }
    public int Passing2PtConversions { get; set; }
    public decimal Pacr { get; set; }                          // passing air conversion ratio
    public decimal Dakota { get; set; }                        // adjusted EPA metric

    // ── Rushing Stats ─────────────────────────────────────────────────────
    public int Carries { get; set; }
    public decimal RushingYards { get; set; }
    public int RushingTds { get; set; }
    public int RushingFumbles { get; set; }
    public int RushingFumblesLost { get; set; }
    public int RushingFirstDowns { get; set; }
    public decimal RushingEpa { get; set; }
    public int Rushing2PtConversions { get; set; }

    // ── Receiving Stats ───────────────────────────────────────────────────
    public int Receptions { get; set; }
    public int Targets { get; set; }
    public decimal ReceivingYards { get; set; }
    public int ReceivingTds { get; set; }
    public int ReceivingFumbles { get; set; }
    public int ReceivingFumblesLost { get; set; }
    public decimal ReceivingAirYards { get; set; }
    public decimal ReceivingYardsAfterCatch { get; set; }
    public int ReceivingFirstDowns { get; set; }
    public decimal ReceivingEpa { get; set; }
    public int Receiving2PtConversions { get; set; }

    // ── Efficiency / Usage Metrics ────────────────────────────────────────
    // These are the key inputs for the projection model in Phase 3
    public decimal Racr { get; set; }                          // receiver air conversion ratio
    public decimal TargetShare { get; set; }                   // % of team targets
    public decimal AirYardsShare { get; set; }                 // % of team air yards
    public decimal Wopr { get; set; }                          // weighted opportunity rating

    // ── Special Teams ─────────────────────────────────────────────────────
    public int SpecialTeamsTds { get; set; }

    // ── Fantasy Points ────────────────────────────────────────────────────
    public decimal FantasyPoints { get; set; }                 // standard scoring
    public decimal FantasyPointsPpr { get; set; }              // PPR scoring

    // ── Data Quality ──────────────────────────────────────────────────────
    public string DataSource { get; set; } = string.Empty;     // "nflfastr" or "pfr"
    public bool PfrValidated { get; set; }                     // true if PFR cross-check passed
    public decimal? PfrFantasyPoints { get; set; }             // PFR's fantasy point total
    public decimal? PfrVariance { get; set; }                  // difference vs nflfastR
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
