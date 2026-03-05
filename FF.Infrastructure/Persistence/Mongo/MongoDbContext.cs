using MongoDB.Driver;
using Microsoft.Extensions.Configuration;

namespace FF.Infrastructure.Persistence.Mongo;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IConfiguration configuration)
    {
        var host = configuration["MongoDB:Host"]
            ?? throw new InvalidOperationException("MongoDB:Host not configured.");
        var port = int.Parse(configuration["MongoDB:Port"] ?? "27017");
        var database = configuration["MongoDB:Database"]
            ?? throw new InvalidOperationException("MongoDB:Database not configured.");
        var username = configuration["MongoDB:Username"]
            ?? throw new InvalidOperationException("MongoDB:Username not configured.");
        var password = configuration["MongoDB:Password"]
            ?? throw new InvalidOperationException("MongoDB:Password not configured.");

        var settings = new MongoClientSettings
        {
            Server = new MongoServerAddress(host, port),
            Credential = MongoCredential.CreateCredential("admin", username, password),
            MaxConnectionPoolSize = 10,
            MinConnectionPoolSize = 1,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ServerSelectionTimeout = TimeSpan.FromSeconds(10)
        };

        var client = new MongoClient(settings);
        _database = client.GetDatabase(database);
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName)
        => _database.GetCollection<T>(collectionName);

    public IMongoDatabase Database => _database;
}