// FF.Infrastructure/ExternalApis/CsvImport/Dtos/NflfastrRowDto.cs
//
// Maps directly to nflfastR player_stats CSV column headers.
// Property names match CSV headers exactly — CsvHelper uses these names
// for automatic column binding (case-insensitive by default).
//
// SOURCE: https://github.com/nflverse/nflverse-data/releases/tag/player_stats
// FILES:  player_stats_2022.csv, player_stats_2023.csv, player_stats_2024.csv

namespace FF.Infrastructure.ExternalApis.CsvImport.Dtos;

public class NflfastrRowDto
{
    // ── Player Identity ───────────────────────────────────────────────────
    public string player_id { get; set; } = string.Empty;
    public string player_name { get; set; } = string.Empty;
    public string player_display_name { get; set; } = string.Empty;
    public string position { get; set; } = string.Empty;
    public string position_group { get; set; } = string.Empty;
    public string? headshot_url { get; set; }
    public string recent_team { get; set; } = string.Empty;

    // ── Game Context ──────────────────────────────────────────────────────
    public int season { get; set; }
    public int week { get; set; }
    public string season_type { get; set; } = string.Empty;
    public string? opponent_team { get; set; }

    // ── Passing ───────────────────────────────────────────────────────────
    public decimal? completions { get; set; }
    public decimal? attempts { get; set; }
    public decimal? passing_yards { get; set; }
    public decimal? passing_tds { get; set; }
    public decimal? interceptions { get; set; }
    public decimal? sacks { get; set; }
    public decimal? sack_yards { get; set; }
    public decimal? sack_fumbles { get; set; }
    public decimal? sack_fumbles_lost { get; set; }
    public decimal? passing_air_yards { get; set; }
    public decimal? passing_yards_after_catch { get; set; }
    public decimal? passing_first_downs { get; set; }
    public decimal? passing_epa { get; set; }
    public decimal? passing_2pt_conversions { get; set; }
    public decimal? pacr { get; set; }
    public decimal? dakota { get; set; }

    // ── Rushing ───────────────────────────────────────────────────────────
    public decimal? carries { get; set; }
    public decimal? rushing_yards { get; set; }
    public decimal? rushing_tds { get; set; }
    public decimal? rushing_fumbles { get; set; }
    public decimal? rushing_fumbles_lost { get; set; }
    public decimal? rushing_first_downs { get; set; }
    public decimal? rushing_epa { get; set; }
    public decimal? rushing_2pt_conversions { get; set; }

    // ── Receiving ─────────────────────────────────────────────────────────
    public decimal? receptions { get; set; }
    public decimal? targets { get; set; }
    public decimal? receiving_yards { get; set; }
    public decimal? receiving_tds { get; set; }
    public decimal? receiving_fumbles { get; set; }
    public decimal? receiving_fumbles_lost { get; set; }
    public decimal? receiving_air_yards { get; set; }
    public decimal? receiving_yards_after_catch { get; set; }
    public decimal? receiving_first_downs { get; set; }
    public decimal? receiving_epa { get; set; }
    public decimal? receiving_2pt_conversions { get; set; }

    // ── Efficiency / Usage ────────────────────────────────────────────────
    public decimal? racr { get; set; }
    public decimal? target_share { get; set; }
    public decimal? air_yards_share { get; set; }
    public decimal? wopr { get; set; }

    // ── Special Teams ─────────────────────────────────────────────────────
    public decimal? special_teams_tds { get; set; }

    // ── Fantasy Points ────────────────────────────────────────────────────
    public decimal? fantasy_points { get; set; }
    public decimal? fantasy_points_ppr { get; set; }
}
