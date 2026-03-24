// FF.Application/Interfaces/Persistence/IBriefDeliveryPreferenceRepository.cs
using FF.Domain.Entities;

namespace FF.Application.Interfaces.Persistence;

public interface IBriefDeliveryPreferenceRepository
{
    Task<BriefDeliveryPreference?> GetByUserIdAsync(
        string userId, CancellationToken ct = default);

    Task UpsertAsync(
        BriefDeliveryPreference preference, CancellationToken ct = default);
}