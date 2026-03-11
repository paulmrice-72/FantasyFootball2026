using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using FF.Domain.Documents;
using System.Globalization;

namespace FF.Infrastructure.ExternalApis.CsvImport.Parsers;

public class SnapCountCsvParser
{
    public IEnumerable<SnapCountDocument> Parse(Stream csvStream, int season)
    {
        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        });

        var records = new List<SnapCountDocument>();
        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            try
            {
                var playerName = csv.GetField("player") ?? string.Empty;
                var team = csv.GetField("team") ?? string.Empty;
                var weekStr = csv.GetField("week") ?? "0";
                var position = csv.GetField("pos") ?? string.Empty;
                var offSnaps = csv.GetField("offense_snaps") ?? "0";
                var offPct = csv.GetField("offense_pct") ?? "0";

                if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(team))
                    continue;

                if (!int.TryParse(weekStr, out var week) || week <= 0)
                    continue;

                records.Add(new SnapCountDocument
                {
                    PlayerName = playerName.Trim(),
                    Team = team.Trim().ToUpper(),
                    Position = position.Trim(),
                    Season = season,
                    Week = week,
                    OffenseSnaps = int.TryParse(offSnaps, out var snaps) ? snaps : 0,
                    OffensePct = decimal.TryParse(offPct, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var pct) ? pct : 0m,
                    ImportedAt = DateTime.UtcNow
                });
            }
            catch
            {
                // Skip malformed rows
            }
        }

        return records;
    }
}