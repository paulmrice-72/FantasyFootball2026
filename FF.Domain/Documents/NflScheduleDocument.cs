namespace FF.Domain.Documents;

/// <summary>
/// One NFL game. Collection: <c>nfl_schedule</c>.
///
/// <para>
/// FAN-178. Until this existed there was no schedule in the system at all.
/// The opponent shown against every player was carried off his most recent
/// game log, which in a carryover projection means the opponent of his *last
/// game of the previous season* — so Week 1 2026 rendered each player against
/// whoever he happened to face in January.
/// </para>
///
/// <para>
/// Source is nflverse <c>nfldata/data/games.csv</c>, which carries the full
/// season forward (all 18 weeks are present before Week 1 kicks off), plus
/// closing spread and total for every game and the final score once played.
/// That matters: <c>vegas_lines</c> comes from The Odds API and only holds
/// games with a currently-posted line, so it can never answer "who does this
/// player face in Week 12" and cannot serve as a schedule.
/// </para>
///
/// <para>
/// Team abbreviations are normalised through
/// <see cref="FF.Domain.Services.NflTeamNormalizer"/> on the way in — the feed
/// uses the nflverse convention (<c>LA</c>) and every roster join in this
/// system uses the Sleeper one (<c>LAR</c>).
/// </para>
/// </summary>
public class NflScheduleDocument
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// nflverse <c>game_id</c>, e.g. <c>2026_01_DAL_NYG</c>. Natural key —
    /// stable across re-syncs, which is what makes the upsert idempotent.
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    public int Season { get; set; }
    public int Week { get; set; }

    /// <summary>REG or POST. Only REG is imported today.</summary>
    public string GameType { get; set; } = "REG";

    /// <summary>
    /// Calendar date of the game, date component only, stamped UTC.
    /// Deliberately not a kickoff instant: the feed's time column is US
    /// Eastern local, the season straddles a DST change, and nothing that
    /// reads this needs better than day resolution. Adding a true
    /// <c>KickoffUtc</c> later is additive; getting one subtly wrong now
    /// would be a silent hour-off error in week detection.
    /// </summary>
    public DateTime Gameday { get; set; }

    /// <summary>Kickoff time as published, US Eastern, "HH:mm". Display only.</summary>
    public string GametimeEt { get; set; } = string.Empty;

    /// <summary>"Sunday", "Monday", … — display only, straight from the feed.</summary>
    public string Weekday { get; set; } = string.Empty;

    /// <summary>Normalised home team abbreviation.</summary>
    public string HomeTeam { get; set; } = string.Empty;

    /// <summary>Normalised away team abbreviation.</summary>
    public string AwayTeam { get; set; } = string.Empty;

    /// <summary>Final score, null until the game is played.</summary>
    public int? HomeScore { get; set; }

    /// <summary>Final score, null until the game is played.</summary>
    public int? AwayScore { get; set; }

    /// <summary>
    /// Closing spread from the HOME team's perspective:
    /// positive = home favoured. Same convention as
    /// <see cref="VegasLineDocument.HomeSpread"/>, deliberately, so the two
    /// can be used interchangeably by the projection game-script classifier.
    /// Null when no line was posted.
    /// </summary>
    public decimal? SpreadLine { get; set; }

    /// <summary>Closing over/under. Null when no line was posted.</summary>
    public decimal? TotalLine { get; set; }

    /// <summary>"Home" or "Neutral" — international and neutral-site games.</summary>
    public string Location { get; set; } = "Home";

    /// <summary>
    /// True once both scores are populated.
    /// A method rather than a property on purpose: the Mongo driver's AutoMap
    /// decides what to persist from the type's members, and a computed property
    /// invites either a redundant stored field or a deserialisation attempt
    /// against a member with no setter. A method is never mapped.
    /// </summary>
    public bool IsFinal() => HomeScore.HasValue && AwayScore.HasValue;

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}
