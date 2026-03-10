using FF.Domain.Enums;
using FF.SharedKernel;

namespace FF.Domain.Entities;

public class Player : Entity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public Position Position { get; private set; }
    public PlayerStatus Status { get; private set; }
    public string? NflTeam { get; private set; }
    public int? JerseyNumber { get; private set; }
    public string? SleeperPlayerId { get; private set; }
    public int? Age { get; private set; }
    public int? YearsExperience { get; private set; }
    public string? GsisId { get; set; }
    private Player() { } // EF Core constructor

    public static Player Create(
        string firstName,
        string lastName,
        Position position,
        string? nflTeam = null,
        string? sleeperPlayerId = null,
        string? gsisId = null)
    {
        return new Player
        {
            FirstName = firstName,
            LastName = lastName,
            Position = position,
            NflTeam = nflTeam,
            SleeperPlayerId = sleeperPlayerId,
            GsisId = gsisId
        };
    }

    public void UpdateStatus(PlayerStatus status)
    {
        Status = status;
        SetUpdated();
    }

    public void UpdateTeam(string? nflTeam)
    {
        NflTeam = nflTeam;
        SetUpdated();
    }

    public void UpdateFields(
        string firstName,
        string lastName,
        Position position,
        int? age,
        int? yearsExperience,
        int? jerseyNumber,
        string? gsisId = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Position = position;
        Age = age;
        YearsExperience = yearsExperience;
        JerseyNumber = jerseyNumber;
        if (gsisId != null) GsisId = gsisId; // only overwrite if Sleeper provides it
    }
}