// FF.Application/Features/Brief/UpsertDeliveryPreferencesCommand.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using MediatR;

namespace FF.Application.Features.Brief;

public record UpsertDeliveryPreferencesCommand(
    string UserId,
    bool EmailEnabled,
    int DeliveryDayOfWeek,
    int DeliveryHourUtc,
    string TimeZoneId,
    bool IncludeBoomCandidates,
    bool IncludeBustRisks,
    bool IncludeLeagueSections,
    bool IncludeCoachRiley) : IRequest<UpsertDeliveryPreferencesResult>;

public record UpsertDeliveryPreferencesResult(bool Success, string? ErrorMessage);

public class UpsertDeliveryPreferencesCommandHandler(
    IBriefDeliveryPreferenceRepository repository)
    : IRequestHandler<UpsertDeliveryPreferencesCommand, UpsertDeliveryPreferencesResult>
{
    public async Task<UpsertDeliveryPreferencesResult> Handle(
        UpsertDeliveryPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var preference = new BriefDeliveryPreference
            {
                UserId = request.UserId,
                EmailEnabled = request.EmailEnabled,
                DeliveryDayOfWeek = request.DeliveryDayOfWeek,
                DeliveryHourUtc = request.DeliveryHourUtc,
                TimeZoneId = request.TimeZoneId,
                IncludeBoomCandidates = request.IncludeBoomCandidates,
                IncludeBustRisks = request.IncludeBustRisks,
                IncludeLeagueSections = request.IncludeLeagueSections,
                IncludeCoachRiley = request.IncludeCoachRiley
            };

            await repository.UpsertAsync(preference, cancellationToken);
            return new UpsertDeliveryPreferencesResult(true, null);
        }
        catch (Exception ex)
        {
            return new UpsertDeliveryPreferencesResult(false, ex.Message);
        }
    }
}