using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IVegasLineRepository
{
    Task UpsertAsync(VegasLineDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<VegasLineDocument> documents, CancellationToken ct = default);

    /// <summary>Returns spread for a team in a given week. Returns null if not found.</summary>
    Task<VegasLineDocument?> GetByTeamAsync(string nflTeam, int season, int week, CancellationToken ct = default);

    Task<IReadOnlyList<VegasLineDocument>> GetByWeekAsync(int season, int week, CancellationToken ct = default);
}