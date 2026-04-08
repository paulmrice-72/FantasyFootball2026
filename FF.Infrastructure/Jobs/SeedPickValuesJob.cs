// FF.Infrastructure/Jobs/SeedPickValuesJob.cs
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using Hangfire;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Jobs;

public class SeedPickValuesJob(
    MongoDbContext db,
    ILogger<SeedPickValuesJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task SeedAsync()
    {
        var collection = db.GetCollection<PickValueDocument>("pick_values");
        var count = await collection.CountDocumentsAsync(FilterDefinition<PickValueDocument>.Empty);

        if (count > 0)
        {
            logger.LogInformation("Pick values already seeded ({Count} documents) — skipping", count);
            return;
        }

        var baseValues = new Dictionary<(int Round, string Tier), double>
        {
            { (1, "Early"), 90.0 },
            { (1, "Mid"),   70.0 },
            { (1, "Late"),  50.0 },
            { (2, "Early"), 30.0 },
            { (2, "Mid"),   22.0 },
            { (2, "Late"),  15.0 },
            { (3, "Early"), 10.0 },
            { (3, "Mid"),    7.0 },
            { (3, "Late"),   5.0 },
        };

        var decayByYear = new Dictionary<int, double>
        {
            { 2026, 1.00 },
            { 2027, 0.85 },
            { 2028, 0.70 },
        };

        var docs = new List<PickValueDocument>();

        foreach (var year in decayByYear.Keys)
            foreach (var kvp in baseValues)
            {
                docs.Add(new PickValueDocument
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    Round = kvp.Key.Round,
                    Tier = kvp.Key.Tier,
                    Year = year,
                    Value = Math.Round(kvp.Value * decayByYear[year], 1),
                    UpdatedAt = DateTime.UtcNow
                });
            }

        await collection.InsertManyAsync(docs);
        logger.LogInformation("Pick values seeded — {Count} documents inserted", docs.Count);
    }
}