using FF.Application.Identity.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FF.Infrastructure.ExternalAPIs;

public class SleeperIdentityService(HttpClient httpClient) : ISleeperIdentityService
{
    public async Task<SleeperUserInfo?> GetUserByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"/v1/user/{Uri.EscapeDataString(username)}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var sleeperUser = JsonSerializer.Deserialize<SleeperUserResponse>(json);

            if (sleeperUser?.UserId is null)
                return null;

            return new SleeperUserInfo(
                sleeperUser.UserId,
                sleeperUser.Username ?? username,
                sleeperUser.DisplayName,
                sleeperUser.Avatar
            );
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> VerifyLeagueMembershipAsync(
        string sleeperUserId,
        string leagueId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"/v1/league/{leagueId}/rosters", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var rosters = JsonSerializer.Deserialize<List<SleeperRosterResponse>>(json);

            return rosters?.Any(r => r.OwnerUserId == sleeperUserId) ?? false;
        }
        catch
        {
            return false;
        }
    }
    public async Task<IReadOnlyList<SleeperUserLeague>> GetUserLeaguesAsync(
    string sleeperUserId,
    int season,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"/v1/user/{sleeperUserId}/leagues/nfl/{season}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var leagues = JsonSerializer.Deserialize<List<SleeperLeagueResponse>>(json);

            if (leagues is null) return [];

            return leagues
                .Where(l => l.LeagueId is not null && l.Name is not null)
                .Select(l => new SleeperUserLeague(
                    l.LeagueId!,
                    l.Name!,
                    int.TryParse(l.Season, out var s) ? s : season,
                    l.Status ?? "unknown",
                    l.TotalRosters,
                    l.Settings?.Type switch          // ← was l.LeagueType
                    {
                        2 => "Dynasty",
                        1 => "Keeper",
                        _ => "Redraft"
                    }))
                .ToList()
                .AsReadOnly();
        }
        catch
        {
            return [];
        }
    }

    private record SleeperLeagueResponse(
        [property: JsonPropertyName("league_id")] string? LeagueId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("season")] string? Season,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("total_rosters")] int TotalRosters,
        [property: JsonPropertyName("settings")] SleeperLeagueSettings? Settings
    );

    private record SleeperLeagueSettings(
        [property: JsonPropertyName("type")] int Type
    );
    private record SleeperUserResponse(
        [property: JsonPropertyName("user_id")] string? UserId,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("avatar")] string? Avatar
    );

    private record SleeperRosterResponse(
        [property: JsonPropertyName("owner_id")] string? OwnerUserId
    );
}