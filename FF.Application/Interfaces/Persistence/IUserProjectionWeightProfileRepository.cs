// FF.Application/Interfaces/Persistence/IUserProjectionWeightProfileRepository.cs
using FF.Domain.Entities;

namespace FF.Application.Interfaces.Persistence;

public interface IUserProjectionWeightProfileRepository
{
    Task<UserProjectionWeightProfile?> GetActiveByUserAsync(
        string appUserId, CancellationToken ct = default);
    Task UpsertAsync(
        UserProjectionWeightProfile profile, CancellationToken ct = default);
}