// FF.Application/Features/Brief/GetDeliveryPreferencesQuery.cs
using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.Brief;

public record GetDeliveryPreferencesQuery(string UserId)
    : IRequest<DeliveryPreferencesResult>;

public record DeliveryPreferencesResult(
    bool EmailEnabled,
    int DeliveryDayOfWeek,
    int DeliveryHourUtc,
    string TimeZoneId,
    bool IncludeBoomCandidates,
    bool IncludeBustRisks,
    bool IncludeLeagueSections,
    bool IncludeCoachRiley);

public class GetDeliveryPreferencesQueryHandler(
    IBriefDeliveryPreferenceRepository repository)
    : IRequestHandler<GetDeliveryPreferencesQuery, DeliveryPreferencesResult>
{
    public async Task<DeliveryPreferencesResult> Handle(
        GetDeliveryPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var pref = await repository.GetByUserIdAsync(request.UserId, cancellationToken);

        // Return defaults if no preferences saved yet
        return pref is null
            ? new DeliveryPreferencesResult(true, 0, 8, "America/Chicago", true, true, true, true)
            : new DeliveryPreferencesResult(
                    pref.EmailEnabled,
                    pref.DeliveryDayOfWeek,
                    pref.DeliveryHourUtc,
                    pref.TimeZoneId,
                    pref.IncludeBoomCandidates,
                    pref.IncludeBustRisks,
                    pref.IncludeLeagueSections,
                    pref.IncludeCoachRiley);
                    }
}