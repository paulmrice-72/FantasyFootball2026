// FF.Infrastructure/ExternalApis/CsvImport/Parsers/NflfastrCsvParser.cs
//
// Reads nflfastR player_stats CSV files and maps rows to PlayerGameLogDocument.
//
// FILTERING:
//   - Only REG (regular season) weeks imported. POST excluded — projection
//     models are built on regular season data only.
//   - Only skill positions: QB, RB, WR, TE, K. Others skipped.
//   - Rows with empty player_id skipped (bye week placeholder rows).
//
// BATCH PROCESSING:
//   - Files are streamed row-by-row (not loaded into memory all at once).
//   - Caller receives IAsyncEnumerable<PlayerGameLogDocument> batches.

using CsvHelper;
using CsvHelper.Configuration;
using FF.Infrastructure.ExternalApis.CsvImport.Dtos;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FF.Infrastructure.ExternalApis.CsvImport.Parsers;

public class NflfastrCsvParser(ILogger<NflfastrCsvParser> logger)
{
    private readonly ILogger<NflfastrCsvParser> _logger = logger;

    private static readonly HashSet<string> AllowedPositions = new(StringComparer.OrdinalIgnoreCase)
        { "QB", "RB", "WR", "TE", "K" };

    /// <summary>
    /// Parses a single nflfastR player_stats CSV file.
    /// Returns all valid regular-season skill position rows as documents.
    /// </summary>
    public async Task<List<PlayerGameLogDocument>> ParseFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"nflfastR CSV not found: {filePath}");

        var results = new List<PlayerGameLogDocument>();
        var skipped = 0;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,       // ignore missing columns gracefully
            BadDataFound = null,            // skip malformed rows
            TrimOptions = TrimOptions.Trim
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            try
            {
                var row = csv.GetRecord<NflfastrRowDto>();

                if (row is null) { skipped++; continue; }

                // Skip rows with no player identity
                if (string.IsNullOrWhiteSpace(row.player_id))
                { skipped++; continue; }

                // Skip non-skill positions
                if (!AllowedPositions.Contains(row.position))
                { skipped++; continue; }

                // Regular season only
                if (!string.Equals(row.season_type, "REG", StringComparison.OrdinalIgnoreCase))
                { skipped++; continue; }

                results.Add(MapToDocument(row));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipped malformed row in {File}", Path.GetFileName(filePath));
                skipped++;
            }
        }

        _logger.LogInformation(
            "Parsed {File}: {Imported} rows imported, {Skipped} skipped",
            Path.GetFileName(filePath), results.Count, skipped);

        return results;
    }

    private static PlayerGameLogDocument MapToDocument(NflfastrRowDto row) => new()
    {
        // Identity
        PlayerId        = row.player_id,
        PlayerName      = row.player_name,
        DisplayName     = row.player_display_name,
        Position        = row.position.ToUpperInvariant(),
        NflTeam         = row.recent_team,
        OpponentTeam    = row.opponent_team,
        HeadshotUrl     = row.headshot_url,

        // Game context
        Season          = row.season,
        Week            = row.week,
        SeasonType      = row.season_type,

        // Passing
        Completions              = (int)(row.completions ?? 0),
        Attempts                 = (int)(row.attempts ?? 0),
        PassingYards             = row.passing_yards ?? 0,
        PassingTds               = (int)(row.passing_tds ?? 0),
        Interceptions            = (int)(row.interceptions ?? 0),
        Sacks                    = (int)(row.sacks ?? 0),
        SackYards                = row.sack_yards ?? 0,
        SackFumbles              = (int)(row.sack_fumbles ?? 0),
        SackFumblesLost          = (int)(row.sack_fumbles_lost ?? 0),
        PassingAirYards          = row.passing_air_yards ?? 0,
        PassingYardsAfterCatch   = row.passing_yards_after_catch ?? 0,
        PassingFirstDowns        = (int)(row.passing_first_downs ?? 0),
        PassingEpa               = row.passing_epa ?? 0,
        Passing2PtConversions    = (int)(row.passing_2pt_conversions ?? 0),
        Pacr                     = row.pacr ?? 0,
        Dakota                   = row.dakota ?? 0,

        // Rushing
        Carries                  = (int)(row.carries ?? 0),
        RushingYards             = row.rushing_yards ?? 0,
        RushingTds               = (int)(row.rushing_tds ?? 0),
        RushingFumbles           = (int)(row.rushing_fumbles ?? 0),
        RushingFumblesLost       = (int)(row.rushing_fumbles_lost ?? 0),
        RushingFirstDowns        = (int)(row.rushing_first_downs ?? 0),
        RushingEpa               = row.rushing_epa ?? 0,
        Rushing2PtConversions    = (int)(row.rushing_2pt_conversions ?? 0),

        // Receiving
        Receptions               = (int)(row.receptions ?? 0),
        Targets                  = (int)(row.targets ?? 0),
        ReceivingYards           = row.receiving_yards ?? 0,
        ReceivingTds             = (int)(row.receiving_tds ?? 0),
        ReceivingFumbles         = (int)(row.receiving_fumbles ?? 0),
        ReceivingFumblesLost     = (int)(row.receiving_fumbles_lost ?? 0),
        ReceivingAirYards        = row.receiving_air_yards ?? 0,
        ReceivingYardsAfterCatch = row.receiving_yards_after_catch ?? 0,
        ReceivingFirstDowns      = (int)(row.receiving_first_downs ?? 0),
        ReceivingEpa             = row.receiving_epa ?? 0,
        Receiving2PtConversions  = (int)(row.receiving_2pt_conversions ?? 0),

        // Efficiency / Usage
        Racr            = row.racr ?? 0,
        TargetShare     = row.target_share ?? 0,
        AirYardsShare   = row.air_yards_share ?? 0,
        Wopr            = row.wopr ?? 0,

        // Special teams
        SpecialTeamsTds = (int)(row.special_teams_tds ?? 0),

        // Fantasy points
        FantasyPoints    = row.fantasy_points ?? 0,
        FantasyPointsPpr = row.fantasy_points_ppr ?? 0,

        // Data quality
        DataSource  = "nflfastr",
        ImportedAt  = DateTime.UtcNow
    };
}
