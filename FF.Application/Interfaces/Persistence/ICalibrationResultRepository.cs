// FF.Application/Interfaces/Persistence/ICalibrationResultRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface ICalibrationResultRepository
{
    Task InsertAsync(CalibrationResultDocument document, CancellationToken ct = default);
    Task<List<CalibrationResultDocument>> GetRecentAsync(int count = 10, CancellationToken ct = default);
    Task<CalibrationResultDocument?> GetLatestAsync(CancellationToken ct = default);
}