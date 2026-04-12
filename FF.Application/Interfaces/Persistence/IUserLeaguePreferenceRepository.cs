using FF.Domain.Entities;

namespace FF.Application.Interfaces.Persistence;

public interface IUserLeaguePreferenceRepository
{
    Task<IReadOnlyList<UserLeaguePreference>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<UserLeaguePreference?> GetAsync(string userId, Guid leagueId, CancellationToken ct = default);
    Task UpsertAsync(UserLeaguePreference preference, CancellationToken ct = default);
}