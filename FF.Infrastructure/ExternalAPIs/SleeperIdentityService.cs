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