using FF.Domain.Entities;

namespace FF.Application.Interfaces.Persistence;

public interface IPlatformSettingsRepository
{
    Task<PlatformSettings> GetAsync();
    Task SaveAsync(PlatformSettings settings);
}