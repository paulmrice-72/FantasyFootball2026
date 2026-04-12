namespace FF.Application.Interfaces.Services;

/// <summary>
/// Provides the current NFL season and week, honoring any admin simulation override.
/// Inject this everywhere instead of calling DateTime.UtcNow calculations directly.
/// </summary>
public interface INflContextService
{
    Task<int> GetSeasonAsync();
    Task<int> GetWeekAsync();
    Task<(int Season, int Week)> GetContextAsync();
}