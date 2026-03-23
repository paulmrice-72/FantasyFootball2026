using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IWarRoomBriefRepository
{
    Task UpsertAsync(WarRoomBriefDocument document, CancellationToken ct = default);

    Task<WarRoomBriefDocument?> GetLatestAsync(
        string userId, int season, CancellationToken ct = default);

    Task<WarRoomBriefDocument?> GetByWeekAsync(
        string userId, int season, int week, CancellationToken ct = default);

    Task<IReadOnlyList<WarRoomBriefDocument>> GetAllForUserAsync(
        string userId, int season, CancellationToken ct = default);
}