using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using Microsoft.Extensions.Configuration;

namespace FF.Infrastructure.Persistence.Mongo;

public class MongoHealthCheck : IHealthCheck
{
    private readonly MongoDbContext _context;

    public MongoHealthCheck(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ping the database — fastest way to verify connectivity
            await _context.Database.RunCommandAsync(
                (Command<MongoDB.Bson.BsonDocument>)"{ping:1}",
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("MongoDB is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "MongoDB is unreachable.",
                exception: ex);
        }
    }
}