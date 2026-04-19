using MediatR;

namespace FF.Application.Features.Admin.Queries.GetPlatformSettings;

public record GetPlatformSettingsQuery : IRequest<PlatformSettingsDto>;

public record PlatformSettingsDto(
    bool RegistrationsEnabled,
    DateTime UpdatedAt,
    string UpdatedBy);