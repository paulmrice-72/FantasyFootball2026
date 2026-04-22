using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class WarRoomBriefRepository(
    MongoDbContext database,
    ILogger<WarRoomBriefRepository> logger) : IWarRoomBriefRepository
{
    private readonly IMongoCollection<WarRoomBriefDocument> _collection =
        database.GetCollection<WarRoomBriefDocument>("war_room_briefs");
    private readonly ILogger<WarRoomBriefRepository> _logger = logger;

    public async Task UpsertAsync(WarRoomBriefDocument document, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        var filter = Builders<WarRoomBriefDocument>.Filter.And(
            Builders<WarRoomBriefDocument>.Filter.Eq(x => x.UserId, document.UserId),
            Builders<WarRoomBriefDocument>.Filter.Eq(x => x.Season, document.Season),
            Builders<WarRoomBriefDocument>.Filter.Eq(x => x.Week, document.Week));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            await _collection.InsertOneAsync(document, cancellationToken: ct);
            _logger.LogDebug("WarRoomBrief inserted for User {UserId} Season {Season} Week {Week}",
                document.UserId, document.Season, document.Week);
        }
        else
        {
            var update = Builders<WarRoomBriefDocument>.Update
                .Set(x => x.GeneratedAt, document.GeneratedAt)
                .Set(x => x.Leagues, document.Leagues)
                .Set(x => x.CoachRileyNarrative, document.CoachRileyNarrative)
                .Set(x => x.TopBoomCandidates, document.TopBoomCandidates)
                .Set(x => x.BustRisks, document.BustRisks)
                .Set(x => x.EmailSent, document.EmailSent)
                .Set(x => x.EmailSentAt, document.EmailSentAt);

            await _collection.UpdateOneAsync(
                Builders<WarRoomBriefDocument>.Filter.Eq(x => x.Id, existing.Id),
                update, cancellationToken: CancellationToken.None);

            _logger.LogDebug("WarRoomBrief updated for User {UserId} Season {Season} Week {Week}",
                document.UserId, document.Season, document.Week);
        }
    }

    public async Task<WarRoomBriefDocument?> GetLatestAsync(
        string userId, int season, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.UserId == userId && x.Season == season)
            .SortByDescending(x => x.Week)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WarRoomBriefDocument?> GetByWeekAsync(
        string userId, int season, int week, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.UserId == userId && x.Season == season && x.Week == week)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<WarRoomBriefDocument>> GetAllForUserAsync(
        string userId, int season, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.UserId == userId && x.Season == season)
            .SortByDescending(x => x.Week)
            .ToListAsync(ct);
    }
}