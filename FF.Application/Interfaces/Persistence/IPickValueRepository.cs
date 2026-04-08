using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IPickValueRepository
{
    Task<PickValueDocument?> GetAsync(int round, string tier, int year, CancellationToken ct = default);
    Task<IReadOnlyList<PickValueDocument>> GetAllAsync(CancellationToken ct = default);
}