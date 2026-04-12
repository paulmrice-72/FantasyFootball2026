using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class AppSettingsRepository(MongoDbContext context) : IAppSettingsRepository
{
    private readonly IMongoCollection<AppSettingsDocument> _col =
        context.Database.GetCollection<AppSettingsDocument>("app_settings");

    public async Task<AppSettingsDocument> GetAsync()
    {
        var doc = await _col.Find(x => x.Id == "global").FirstOrDefaultAsync();
        return doc ?? new AppSettingsDocument();
    }

    public async Task UpsertAsync(AppSettingsDocument settings)
    {
        settings.Id = "global";
        settings.UpdatedAt = DateTime.UtcNow;
        var opts = new ReplaceOptions { IsUpsert = true };
        await _col.ReplaceOneAsync(x => x.Id == "global", settings, opts);
    }
}