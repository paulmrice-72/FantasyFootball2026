// FF.Application/Interfaces/Services/ICoachRileyService.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface ICoachRileyService
{
    Task<string?> GenerateNarrativeAsync(
        WarRoomBriefDocument brief,
        CancellationToken ct = default);
}