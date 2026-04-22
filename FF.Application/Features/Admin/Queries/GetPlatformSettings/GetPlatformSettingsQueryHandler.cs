using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.Admin.Queries.GetPlatformSettings;

public class GetPlatformSettingsQueryHandler(IPlatformSettingsRepository repo)
    : IRequestHandler<GetPlatformSettingsQuery, PlatformSettingsDto>
{
    public async Task<PlatformSettingsDto> Handle(
        GetPlatformSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await repo.GetAsync();
        return new PlatformSettingsDto(
            settings.RegistrationsEnabled,
            settings.AiJobsEnabled,
            settings.UpdatedAt,
            settings.UpdatedBy);
    }
}