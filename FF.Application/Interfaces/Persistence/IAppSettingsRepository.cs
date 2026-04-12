using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IAppSettingsRepository
{
    Task<AppSettingsDocument> GetAsync();
    Task UpsertAsync(AppSettingsDocument settings);
}