using MediatR;

namespace FF.Application.Features.Admin.Commands.SetPlatformSettings;

public record SetPlatformSettingsCommand(
    bool RegistrationsEnabled,
    string UpdatedBy) : IRequest;