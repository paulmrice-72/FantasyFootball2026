// FF.Domain/Entities/Player.cs
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
    public string? InjuryStatus { get; private set; }

    // ── E10 Dynasty Draft ─────────────────────────────────────────────────
    public int? DraftRound { get; private set; }
    public int? DraftPick { get; private set; }
    public string? CollegeTeam { get; private set; }

    private Player() { }

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
        string? gsisId = null,
        string? injuryStatus = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Position = position;
        Age = age;
        YearsExperience = yearsExperience;
        JerseyNumber = jerseyNumber;
        if (gsisId != null) GsisId = gsisId;
        InjuryStatus = injuryStatus;
    }

    /// <summary>
    /// Populated after NFL draft (April 24-26). Null until draft occurs.
    /// </summary>
    public void UpdateDraftCapital(int? draftRound, int? draftPick, string? collegeTeam)
    {
        DraftRound = draftRound;
        DraftPick = draftPick;
        if (collegeTeam != null) CollegeTeam = collegeTeam;
        SetUpdated();
    }
}