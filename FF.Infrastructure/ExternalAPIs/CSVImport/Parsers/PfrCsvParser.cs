// FF.Infrastructure/ExternalApis/CsvImport/Parsers/PfrCsvParser.cs
//
// Parses Pro Football Reference season fantasy CSV exports.
// Used for VALIDATION only — cross-checks nflfastR season totals.
//
// PFR QUIRKS HANDLED:
//   1. Duplicate column names (Att, Yds, TD repeat for pass/rush/rec)
//      → Handled with a custom ClassMap using column INDEX not name
//   2. Repeated header rows mid-file (PFR inserts header every 30 rows)
//      → Detected by checking if Rk column is non-numeric, row skipped
//   3. Player names sometimes include "*" (Pro Bowl) or "+" (All-Pro)
//      → Stripped in normalisation
//   4. Team column is 3-char abbreviation, may differ from nflfastR
//      → Not used for matching; player name + season is the join key

using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using FF.Infrastructure.ExternalApis.CsvImport.Dtos;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FF.Infrastructure.ExternalApis.CsvImport.Parsers;

public class PfrCsvParser
{
    private readonly ILogger<PfrCsvParser> _logger;

    public PfrCsvParser(ILogger<PfrCsvParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a PFR fantasy season CSV. Injects season year (from filename convention).
    /// Returns only rows where fantasy points are present and position is a skill position.
    /// </summary>
    public async Task<List<PfrRowDto>> ParseFileAsync(string filePath, int season)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PFR CSV not found: {filePath}");

        var results = new List<PfrRowDto>();
        var skipped = 0;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        csv.Context.RegisterClassMap<PfrRowClassMap>();

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            try
            {
                // Skip repeated header rows (PFR inserts "Rk" header every 30 rows)
                var rankValue = csv.GetField("Rk");
                if (string.IsNullOrWhiteSpace(rankValue) || !int.TryParse(rankValue, out _))
                {
                    skipped++;
                    continue;
                }

                var row = csv.GetRecord<PfrRowDto>();
                if (row is null) { skipped++; continue; }

                // Skip if no fantasy points (PFR includes players with 0 games)
                if (row.FantPt is null && row.PPR is null) { skipped++; continue; }

                // Skip non-skill positions
                var pos = row.FantPos?.Trim().ToUpperInvariant();
                if (pos is null or "" or "FB") { skipped++; continue; }

                // Normalise player name — strip Pro Bowl (*) and All-Pro (+) markers
                row.Player = row.Player
                    .Replace("*", "")
                    .Replace("+", "")
                    .Trim();

                row.Season = season;
                results.Add(row);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipped malformed PFR row in {File}", Path.GetFileName(filePath));
                skipped++;
            }
        }

        _logger.LogInformation(
            "Parsed PFR {File}: {Imported} rows, {Skipped} skipped",
            Path.GetFileName(filePath), results.Count, skipped);

        return results;
    }
}

// ── ClassMap: maps PFR columns by INDEX to handle duplicate column names ──────
//
// PFR CSV column order (0-indexed):
//  0  Rk
//  1  Player
//  2  Tm
//  3  FantPos
//  4  Age
//  5  G
//  6  GS
//  7  Cmp        (passing completions)
//  8  Att        (passing attempts)     ← duplicate name
//  9  Yds        (passing yards)        ← duplicate name
//  10 TD         (passing TDs)          ← duplicate name
//  11 Int
//  12 Att        (rushing attempts)     ← SAME name as col 8
//  13 Yds        (rushing yards)        ← SAME name as col 9
//  14 TD         (rushing TDs)          ← SAME name as col 10
//  15 Rec
//  16 Yds        (receiving yards)      ← SAME name as cols 9, 13
//  17 TD         (receiving TDs)        ← SAME name as cols 10, 14
//  18 Tgt
//  19 FL
//  20 2PM
//  21 FantPt
//  22 PPR
//  23 DKPt
//  24 FDPt
//  25 VBD
//  26 PosRank
//  27 OvRank

public sealed class PfrRowClassMap : ClassMap<PfrRowDto>
{
    public PfrRowClassMap()
    {
        Map(m => m.Rk).Index(0);
        Map(m => m.Player).Index(1);
        Map(m => m.Tm).Index(2);
        Map(m => m.FantPos).Index(3);
        Map(m => m.Age).Index(4);
        Map(m => m.G).Index(5);
        Map(m => m.GS).Index(6);
        Map(m => m.Cmp).Index(7);
        Map(m => m.Att).Index(8);       // passing attempts
        Map(m => m.Yds).Index(9);       // passing yards
        Map(m => m.TD).Index(10);       // passing TDs
        Map(m => m.Int).Index(11);
        Map(m => m.RushAtt).Index(12);  // rushing attempts
        Map(m => m.RushYds).Index(13);  // rushing yards
        Map(m => m.RushTD).Index(14);   // rushing TDs
        Map(m => m.Rec).Index(15);
        Map(m => m.RecYds).Index(16);   // receiving yards
        Map(m => m.RecTD).Index(17);    // receiving TDs
        Map(m => m.Tgt).Index(18);
        Map(m => m.FL).Index(19);
        Map(m => m.TwoPM).Index(20);
        Map(m => m.FantPt).Index(21);
        Map(m => m.PPR).Index(22);
        Map(m => m.DKPt).Index(23).Optional();
        Map(m => m.FDPt).Index(24).Optional();
    }
}
