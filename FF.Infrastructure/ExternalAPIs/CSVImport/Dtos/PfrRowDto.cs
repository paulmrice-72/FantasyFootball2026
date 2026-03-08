// FF.Infrastructure/ExternalApis/CsvImport/Dtos/PfrRowDto.cs
//
// Maps to Pro Football Reference fantasy season stats CSV export.
// SOURCE: https://www.pro-football-reference.com/years/{year}/fantasy.htm
//         → Share & Export → Get table as CSV
//
// PFR provides SEASON-LEVEL totals (not weekly). We use this as a
// validation cross-check against nflfastR's summed season totals.
//
// IMPORTANT: PFR CSV has a quirky format — the first row after the header
// is sometimes a duplicate header row (rank column header repeats).
// The parser skips rows where the 'rank' column is not numeric.
//
// PFR COLUMN REFERENCE (as exported from fantasy.htm):
//   Rk, Player, Tm, FantPos, Age, G, GS,
//   Passing: Cmp, Att, Yds, TD, Int
//   Rushing: Att (rush), Yds (rush), TD (rush)
//   Receiving: Rec, Yds (rec), TD (rec), Tgt
//   Fumbles: FL
//   Scoring: 2PM, FantPt, PPR, DKPt, FDPt, VBD, PosRank, OvRank

namespace FF.Infrastructure.ExternalApis.CsvImport.Dtos;

public class PfrRowDto
{
    // ── Identity ──────────────────────────────────────────────────────────
    public string? Rk { get; set; }                // rank — used to detect duplicate header rows
    public string Player { get; set; } = string.Empty;
    public string? Tm { get; set; }                // team abbreviation
    public string? FantPos { get; set; }           // QB/RB/WR/TE
    public decimal? Age { get; set; }
    public decimal? G { get; set; }                // games played
    public decimal? GS { get; set; }               // games started

    // ── Passing ───────────────────────────────────────────────────────────
    public decimal? Cmp { get; set; }
    public decimal? Att { get; set; }              // passing attempts
    public decimal? Yds { get; set; }              // passing yards (first 'Yds' column)
    public decimal? TD { get; set; }               // passing TDs
    public decimal? Int { get; set; }

    // ── Rushing ───────────────────────────────────────────────────────────
    // NOTE: PFR CSV has duplicate column names (Att, Yds, TD appear for passing AND rushing)
    // CsvHelper maps by position index when names collide — we handle this
    // with a custom ClassMap in the parser.
    public decimal? RushAtt { get; set; }
    public decimal? RushYds { get; set; }
    public decimal? RushTD { get; set; }

    // ── Receiving ─────────────────────────────────────────────────────────
    public decimal? Rec { get; set; }
    public decimal? RecYds { get; set; }
    public decimal? RecTD { get; set; }
    public decimal? Tgt { get; set; }

    // ── Misc ──────────────────────────────────────────────────────────────
    public decimal? FL { get; set; }               // fumbles lost
    public decimal? TwoPM { get; set; }            // 2-point conversions (column header: 2PM)
    public decimal? FantPt { get; set; }           // standard fantasy points
    public decimal? PPR { get; set; }              // PPR fantasy points
    public decimal? DKPt { get; set; }             // DraftKings points
    public decimal? FDPt { get; set; }             // FanDuel points

    // ── Computed after parse ──────────────────────────────────────────────
    public int Season { get; set; }               // injected by parser (from filename)
}
