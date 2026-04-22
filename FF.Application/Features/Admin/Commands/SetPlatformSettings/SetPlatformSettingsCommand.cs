using MediatR;

namespace FF.Application.Features.Admin.Commands.SetPlatformSettings;

public record SetPlatformSettingsCommand(
    bool RegistrationsEnabled,
    bool AiJobsEnabled,
    DateTime UpdatedAt,
    string UpdatedBy  
) : IRequest;