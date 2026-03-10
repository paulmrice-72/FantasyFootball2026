// FF.Infrastructure/ExternalApis/Sleeper/Mappers/SleeperPlayerMapper.cs
//
// Converts Sleeper API DTOs into FF.Domain entities.
// This is the translation layer between "what Sleeper gives us"
// and "what our domain model understands".
//
// Mapping lives in Infrastructure because it depends on both
// the Sleeper DTOs (infrastructure) and domain entities.

using FF.Domain.Entities;
using FF.Domain.Enums;
using FF.Infrastructure.ExternalApis.Sleeper.Dtos;

namespace FF.Infrastructure.ExternalApis.Sleeper.Mappers;

public static class SleeperPlayerMapper
{
    /// <summary>
    /// Maps a Sleeper player DTO to a new Player domain entity.
    /// Returns null if the player doesn't have the minimum required fields
    /// (first name, last name, and a recognisable position).
    /// </summary>
    public static Player? ToDomainEntity(SleeperPlayerDto dto)
    {
        // Skip entries with no name - Sleeper includes some placeholder entries
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            return null;

        // Skip if we can't map the position - keeps our domain clean
        var position = MapPosition(dto.Position);
        if (position is null)
            return null;

        return Player.Create(
            firstName: dto.FirstName,
            lastName: dto.LastName,
            position: position.Value,
            nflTeam: dto.Team,
            sleeperPlayerId: dto.PlayerId,
            gsisId: dto.GsisId        // ← add this
        );
    }

    /// <summary>
    /// Maps the current status fields from a Sleeper DTO to a PlayerStatus enum.
    /// Sleeper uses two fields: status (active/inactive) and injury_status (Q/D/Out/IR).
    /// Injury status takes priority if present.
    /// </summary>
    public static PlayerStatus MapStatus(SleeperPlayerDto dto)
    {
        // Injury status takes priority over general status
        if (!string.IsNullOrWhiteSpace(dto.InjuryStatus))
        {
            return dto.InjuryStatus.ToUpperInvariant() switch
            {
                "IR" => PlayerStatus.IR,
                "OUT" => PlayerStatus.Out,
                "DOUBTFUL" or "D" => PlayerStatus.Doubtful,
                "QUESTIONABLE" or "Q" => PlayerStatus.Questionable,
                _ => PlayerStatus.Active
            };
        }

        return dto.Status?.ToUpperInvariant() switch
        {
            "ACTIVE" => PlayerStatus.Active,
            "INACTIVE" => PlayerStatus.Injured,
            "SUSPENDED" => PlayerStatus.Suspended,
            "RETIRED" => PlayerStatus.Retired,
            _ => PlayerStatus.Active
        };
    }

    /// <summary>
    /// Maps a Sleeper position string to our Position enum.
    /// Returns null for positions we don't track (LB, CB, S, etc.)
    /// Returning null causes the caller to skip that player entirely.
    /// </summary>
    private static Position? MapPosition(string? sleeperPosition)
    {
        return sleeperPosition?.ToUpperInvariant() switch
        {
            "QB" => Position.QB,
            "RB" => Position.RB,
            "WR" => Position.WR,
            "TE" => Position.TE,
            "K" => Position.K,
            "DEF" => Position.DEF,
            _ => null  // LB, CB, S, DL, OL etc - we don't track these
        };
    }
}