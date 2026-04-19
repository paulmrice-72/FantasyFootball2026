using System.Net.Http.Json;

namespace FF.WebBlazor.Services;

/// <summary>
/// Fetches the active NFL season and week from the API,
/// honouring any admin simulation override.
/// </summary>
public class NflContextClientService(IHttpClientFactory httpFactory)
{
    public async Task<(int Season, int Week)> GetContextAsync()
    {
        try
        {
            var http = httpFactory.CreateClient("AuthAPI");
            var ctx = await http.GetFromJsonAsync<NflContextResponse>(
                "api/v1/admin/nfl-context/public");
            if (ctx is not null)
                return (ctx.ActiveSeason, ctx.ActiveWeek);
        }
        catch { }

        var now = DateTime.UtcNow;
        var season = now.Month >= 3 ? now.Year : now.Year - 1;
        return (season, 18);
    }

    private record NflContextResponse(
        int CalendarSeason, int CalendarWeek,
        int ActiveSeason, int ActiveWeek,
        bool IsOverrideActive);
}