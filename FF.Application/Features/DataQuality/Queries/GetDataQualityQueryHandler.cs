// FF.Application/Features/DataQuality/Queries/GetDataQualityQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Stats.Queries.GetDataQuality;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DataQuality.Queries;

public class GetDataQualityQueryHandler(
    IPlayerGameLogRepository gameLogRepo,
    ILogger<GetDataQualityQueryHandler> logger)
    : IRequestHandler<GetDataQualityQuery, Result<DataQualityReport>>
{
    public async Task<Result<DataQualityReport>> Handle(
        GetDataQualityQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Running data quality validation");

            var report = new DataQualityReport();

            // ── Rule 1: Season document counts ───────────────────────────
            var countsBySeason = await gameLogRepo
                .GetDocumentCountsBySeasonAsync(cancellationToken);

            report.TotalDocuments = (int)countsBySeason.Values.Sum();

            foreach (var (season, expectedRange) in DataQualityRules.ExpectedSeasonCounts)
            {
                var actualCount = countsBySeason.GetValueOrDefault(season, 0);
                var (min, max) = expectedRange;

                var positionCounts = await gameLogRepo
                    .GetPositionCountsBySeasonAsync(season, cancellationToken);

                var seasonResult = new SeasonQualityResult
                {
                    Season = season,
                    DocumentCount = actualCount,
                    ExpectedMinimum = min,
                    ExpectedMaximum = max,
                    CountByPosition = positionCounts
                };

                if (actualCount == 0)
                {
                    seasonResult.Status = DataQualityStatus.Critical;
                    report.Issues.Add(new DataQualityIssue
                    {
                        Rule = DataQualityRules.RuleSeasonCount,
                        Description = $"Season {season} has no documents — import may not have run",
                        Severity = IssueSeverity.Critical,
                        Season = season,
                        ActualValue = "0",
                        ExpectedRange = $"{min:N0}–{max:N0}"
                    });
                }
                else if (!seasonResult.CountInRange)
                {
                    seasonResult.Status = DataQualityStatus.Warning;
                    report.Issues.Add(new DataQualityIssue
                    {
                        Rule = DataQualityRules.RuleSeasonCount,
                        Description = $"Season {season} document count {actualCount:N0} " +
                                      $"is outside expected range {min:N0}–{max:N0}",
                        Severity = IssueSeverity.Warning,
                        Season = season,
                        ActualValue = actualCount.ToString("N0"),
                        ExpectedRange = $"{min:N0}–{max:N0}"
                    });
                }
                else
                {
                    seasonResult.Status = DataQualityStatus.Healthy;
                }

                // ── Rule 2: Required positions present ───────────────────
                foreach (var position in DataQualityRules.RequiredPositions)
                {
                    if (!positionCounts.TryGetValue(position, out long value) || value == 0)
                    {
                        seasonResult.IssuesFound++;
                        report.Issues.Add(new DataQualityIssue
                        {
                            Rule = DataQualityRules.RuleInvalidPosition,
                            Description = $"Season {season} has no documents for position {position}",
                            Severity = IssueSeverity.Critical,
                            Season = season,
                            FieldName = "Position",
                            ActualValue = "0",
                            ExpectedRange = "> 0"
                        });
                    }
                }

                report.SeasonResults.Add(seasonResult);
            }

            // ── Rule 3: Stat range checks (sample 500 docs per season) ───
            foreach (var season in DataQualityRules.ExpectedSeasonCounts.Keys)
            {
                var sampleDocs = await gameLogRepo
                    .GetSampleBySeasonAsync(season, 500, cancellationToken);

                foreach (var doc in sampleDocs)
                {
                    if (string.IsNullOrEmpty(doc.PlayerId))
                    {
                        report.Issues.Add(new DataQualityIssue
                        {
                            Rule = DataQualityRules.RuleMissingPlayerId,
                            Description = "Document has empty PlayerId",
                            Severity = IssueSeverity.Critical,
                            Season = season,
                            PlayerName = doc.PlayerName,
                            FieldName = "PlayerId"
                        });
                    }

                    if (doc.FantasyPointsPpr < DataQualityRules.FantasyPointsRange.Min ||
                        doc.FantasyPointsPpr > DataQualityRules.FantasyPointsRange.Max)
                    {
                        report.Issues.Add(new DataQualityIssue
                        {
                            Rule = DataQualityRules.RuleMissingFantasyPoints,
                            Description = $"{doc.PlayerName} has suspicious PPR fantasy points: {doc.FantasyPointsPpr}",
                            Severity = IssueSeverity.Warning,
                            Season = season,
                            PlayerId = doc.PlayerId,
                            PlayerName = doc.PlayerName,
                            FieldName = "FantasyPointsPpr",
                            ActualValue = (doc.FantasyPointsPpr ?? 0m).ToString("F2"),
                            ExpectedRange = $"{DataQualityRules.FantasyPointsRange.Min}–{DataQualityRules.FantasyPointsRange.Max}"
                        });
                    }

                    if ((doc.Position == "WR" || doc.Position == "TE") &&
                        (doc.TargetShare < DataQualityRules.TargetShareRange.Min ||
                         doc.TargetShare > DataQualityRules.TargetShareRange.Max))
                    {
                        report.Issues.Add(new DataQualityIssue
                        {
                            Rule = DataQualityRules.RuleTargetShareRange,
                            Description = $"{doc.PlayerName} has invalid target share: {doc.TargetShare:P1}",
                            Severity = IssueSeverity.Warning,
                            Season = season,
                            PlayerId = doc.PlayerId,
                            PlayerName = doc.PlayerName,
                            FieldName = "TargetShare",
                            ActualValue = doc.TargetShare.ToString("F4"),
                            ExpectedRange = "0.0–1.0"
                        });
                    }

                    if (!DataQualityRules.ValidPositions.Contains(doc.Position))
                    {
                        report.Issues.Add(new DataQualityIssue
                        {
                            Rule = DataQualityRules.RuleInvalidPosition,
                            Description = $"{doc.PlayerName} has invalid position: '{doc.Position}'",
                            Severity = IssueSeverity.Warning,
                            Season = season,
                            PlayerId = doc.PlayerId,
                            PlayerName = doc.PlayerName,
                            FieldName = "Position",
                            ActualValue = doc.Position
                        });
                    }
                }
            }

            // ── Overall status ───────────────────────────────────────────
            report.OverallStatus = report.CriticalIssues > 0
                ? DataQualityStatus.Critical
                : report.WarningIssues > 0
                    ? DataQualityStatus.Warning
                    : DataQualityStatus.Healthy;

            logger.LogInformation(
                "Data quality check complete: {Status}, {Critical} critical, {Warnings} warnings, {Total} total documents",
                report.OverallStatus, report.CriticalIssues,
                report.WarningIssues, report.TotalDocuments);

            return Result<DataQualityReport>.Success(report);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Data quality validation failed");
            return Result.Failure<DataQualityReport>(
                new Error("DataQuality.Failed", ex.Message));
        }
    }
}