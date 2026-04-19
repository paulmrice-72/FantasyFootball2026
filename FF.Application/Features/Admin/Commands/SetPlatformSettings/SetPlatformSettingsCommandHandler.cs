using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.Admin.Commands.SetPlatformSettings;

public class SetPlatformSettingsCommandHandler(IPlatformSettingsRepository repo)
    : IRequestHandler<SetPlatformSettingsCommand>
{
    public async Task Handle(SetPlatformSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await repo.GetAsync();
        settings.RegistrationsEnabled = request.RegistrationsEnabled;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = request.UpdatedBy;
        await repo.SaveAsync(settings);
    }
}