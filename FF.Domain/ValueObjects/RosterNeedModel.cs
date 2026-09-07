// FF.Domain/ValueObjects/RosterNeedModel.cs
namespace FF.Domain.ValueObjects;

/// <summary>
/// How badly a roster needs another player at a given position, given the
/// league's starting lineup and how many picks are left.
///
/// 2026-09-07. This logic used to live inside RookieDraftBoard.razor, where the
/// test project cannot reach it (FF.Tests references Domain, Application and
/// Infrastructure — not FF.WebBlazor, and there is no bUnit). It shipped
/// untested, and what shipped was a single term that silently evaluated to zero
/// for every player from about round six of every draft onward:
///
///     score = DraftValue(p) + 8 * starterGap(p.Position)
///
/// Once the starting lineup is full every gap is 0, so score collapses to
/// DraftValue — and since DraftValue is a monotonically decreasing function of
/// ADP, ordering by it is identical to ordering by ADP, which is exactly how the
/// Best Player Available list directly above it was already ordered. The "Best
/// Fit (Value + Roster Need)" panel was therefore not approximately the same as
/// the panel above it. It was the same list, in the same order, with the same
/// numbers, under a heading asserting that the roster had been taken into
/// account. Paul finished a draft with six receivers against two WR slots and
/// two W/R slots being told, three times, to draft a seventh.
///
/// Three terms now, so that the model has something to say in the half of the
/// draft where roster construction is the only thing left to decide:
///
///   Gap     — this player would walk into a starting slot.
///   Depth   — the lineup is full, but a starting slot has nobody behind it.
///   Urgency — a required slot is empty and the picks are running out.
///
/// Every one of them is pure and takes its inputs explicitly, so every number
/// in the tests below is reproducible by hand.
/// </summary>
public sealed class RosterNeedModel(RosterConfiguration configuration)
{
    private readonly RosterConfiguration _config = configuration;

    /// <summary>
    /// Positions that take part in the flex simulation. A player here can turn
    /// into a starter through more than one route, so "how many more of these
    /// would start" is a real question with a non-obvious answer.
    /// </summary>
    public static readonly string[] SkillPositions = ["QB", "RB", "WR", "TE"];

    /// <summary>
    /// Required, but deliberately given no gap or depth credit.
    ///
    /// A kicker and a defense are replacement level: the tenth-best kicker is
    /// worth nearly the best one, and a backup kicker is worth nothing at all.
    /// Their entire draft value is that the slot must not be empty on Sunday.
    /// Treating an empty K slot as an ordinary starter gap would rank a kicker
    /// above real players from the first pick of the draft; treating it as
    /// nothing leaves a required slot at zero value forever. Neither is right,
    /// so these get their own term — <see cref="MandatoryUrgency"/> — which is
    /// worth nothing until the picks run short and then worth more than
    /// anything else on the board.
    /// </summary>
    public static readonly string[] MandatoryPositions = ["K", "DEF"];

    public static bool IsMandatory(string position) =>
        MandatoryPositions.Contains(position, StringComparer.OrdinalIgnoreCase);

    // ── Tuning constants ─────────────────────────────────────────────────────

    /// <summary>
    /// Diminishing: your first receiver matters more than your fourth, even in a
    /// league that can start four. Without this a 2×W/R league weights RB and WR
    /// so heavily that a top-five tight end falling a full round never surfaces.
    /// </summary>
    public static readonly double[] StarterGapWeights = [1.0, 0.6, 0.3];

    /// <summary>
    /// One unfilled starting slot is worth roughly a round of draft capital in
    /// the middle of a draft — enough to break a tie between adjacent players,
    /// deliberately not enough to reach past a tier.
    /// </summary>
    public const double ValuePerStarterGap = 8.0;

    /// <summary>
    /// Insurance is worth less than a starter. Small on purpose: this should
    /// break ties between comparable players, not reorder tiers.
    /// </summary>
    public const double ValuePerDepthGap = 3.0;

    /// <summary>
    /// Large enough that at zero slack nothing else on the board can outrank it,
    /// which is correct — at that point every remaining pick is spoken for and a
    /// slot you never fill scores zero every week of the season.
    /// </summary>
    public const double MandatoryUrgencyMax = 60.0;

    /// <summary>
    /// Picks of slack at which urgency switches on. Three means: while you have
    /// three more picks than unfilled slots this term is silent and you simply
    /// draft the best player. At two it is worth 20, at one 40, at zero 60.
    /// </summary>
    public const int MandatoryUrgencyHorizon = 3;

    // ── Lineup simulation ────────────────────────────────────────────────────

    /// <summary>
    /// The result of filling the starting lineup with a given set of position
    /// counts. <see cref="Filled"/> is how many starting slots the roster can
    /// actually field. <see cref="UsedByPosition"/> is how many players at each
    /// position that consumed — which is what says whether anyone is left over
    /// behind the starters.
    /// </summary>
    public sealed record LineupFill(
        int Filled,
        IReadOnlyDictionary<string, int> UsedByPosition);

    /// <summary>
    /// Starting slots this model can reason about: the optimizer's QB/RB/WR/TE
    /// and flex, plus K and DEF. Excludes RosterConfiguration.UnsupportedSlots —
    /// an IDP slot is a real starting slot, but this model has no player pool for
    /// it and would only produce a need it can never satisfy. The draft board
    /// names those slots on screen instead.
    /// </summary>
    public int TotalStartingSlots =>
        _config.TotalStarters + _config.KSlots + _config.DefSlots;

    /// <summary>Dedicated (non-flex) starting slots required at a position.</summary>
    public int RequiredSlots(string position) => position.ToUpperInvariant() switch
    {
        "QB" => _config.QbSlots,
        "RB" => _config.RbSlots,
        "WR" => _config.WrSlots,
        "TE" => _config.TeSlots,
        "K" => _config.KSlots,
        "DEF" or "DST" => _config.DefSlots,
        _ => 0
    };

    /// <summary>
    /// Fills dedicated slots first, then flex slots — most restrictive flex
    /// first, each drawing from the eligible position with the deepest surplus,
    /// so a W/R slot cannot be used up by a tight end that a broader FLEX slot
    /// could have taken instead.
    /// </summary>
    public LineupFill FillLineup(IReadOnlyDictionary<string, int> counts)
    {
        var leftover = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filled = 0;

        foreach (var pos in SkillPositions.Concat(MandatoryPositions))
        {
            var owned = counts.TryGetValue(pos, out var c) ? c : 0;
            var required = Math.Max(0, RequiredSlots(pos));
            var taken = Math.Min(owned, required);

            filled += taken;
            used[pos] = taken;
            leftover[pos] = owned - taken;
        }

        foreach (var slot in _config.FlexSlotDefinitions
                     .OrderBy(f => f.EligiblePositions.Count))
        {
            var best = slot.EligiblePositions
                .Where(p => leftover.TryGetValue(p, out var l) && l > 0)
                .OrderByDescending(p => leftover[p])
                .FirstOrDefault();

            if (best is null) continue;

            leftover[best]--;
            used[best] = used.TryGetValue(best, out var u) ? u + 1 : 1;
            filled++;
        }

        return new LineupFill(filled, used);
    }

    public int CountFilledStarters(IReadOnlyDictionary<string, int> counts) =>
        FillLineup(counts).Filled;

    /// <summary>
    /// Starting slots this roster cannot currently field. The K and DEF a league
    /// requires and you have not drafted are exactly this — which is the entire
    /// reason the number matters.
    /// </summary>
    public int UnfillableStarterSlots(IReadOnlyDictionary<string, int> counts) =>
        Math.Max(0, TotalStartingSlots - CountFilledStarters(counts));

    // ── Term 1: starter gap ──────────────────────────────────────────────────

    /// <summary>
    /// How many more players at this position would each turn into a starter,
    /// capped at three. Answered by simulation rather than arithmetic: add one,
    /// ask whether the number of fillable starting slots went up, repeat.
    ///
    /// This is what makes flex types work without special-casing any of them. In
    /// a 2 × W/R league a receiver keeps converting through WR1, WR2 and both
    /// flex slots, while a tight end converts once and stops — because there is
    /// no slot left he is eligible for. Superflex falls out the same way.
    /// </summary>
    public int StarterGap(string position, IReadOnlyDictionary<string, int> counts)
    {
        var probe = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
        var previous = CountFilledStarters(probe);
        var gap = 0;

        for (var i = 0; i < StarterGapWeights.Length; i++)
        {
            probe[position] = (probe.TryGetValue(position, out var c) ? c : 0) + 1;
            var now = CountFilledStarters(probe);

            if (now <= previous) break;

            gap++;
            previous = now;
        }

        return gap;
    }

    /// <summary>Weighted starter gap, with the diminishing curve applied.</summary>
    public double StarterGapWeight(string position, IReadOnlyDictionary<string, int> counts)
    {
        // K and DEF excluded by design, not by oversight. See MandatoryPositions.
        if (IsMandatory(position)) return 0d;

        var gap = StarterGap(position, counts);
        var total = 0d;
        for (var i = 0; i < gap && i < StarterGapWeights.Length; i++)
            total += StarterGapWeights[i];
        return total;
    }

    // ── Term 2: depth ────────────────────────────────────────────────────────

    /// <summary>
    /// How exposed a position is if one player misses time: 1.0 when the
    /// starters have nobody behind them at all, 0.4 with a single backup, 0
    /// beyond that. Zero for a position the roster does not start.
    /// </summary>
    public double DepthWeight(string position, IReadOnlyDictionary<string, int> counts)
    {
        if (IsMandatory(position)) return 0d;   // a backup kicker is not a roster need

        var fill = FillLineup(counts);
        var starting = fill.UsedByPosition.TryGetValue(position, out var u) ? u : 0;
        if (starting == 0) return 0d;           // he does not start here at all

        var owned = counts.TryGetValue(position, out var c) ? c : 0;

        return (owned - starting) switch
        {
            <= 0 => 1.0,
            1 => 0.4,
            _ => 0d
        };
    }

    // ── Term 3: urgency ──────────────────────────────────────────────────────

    /// <summary>
    /// How urgently a required-but-unfilled slot at this position needs filling,
    /// given how many picks are actually left.
    ///
    /// Deliberately general rather than K/DEF-specific: a roster somehow short of
    /// a required tight end with two picks left is in the same trouble for the
    /// same reason, and should get the same answer.
    /// </summary>
    /// <param name="picksRemaining">
    /// The drafter's own remaining picks. Pass 0 when unknown — the term stays
    /// silent rather than inventing a deadline.
    /// </param>
    public double MandatoryUrgency(
        string position,
        IReadOnlyDictionary<string, int> counts,
        int picksRemaining)
    {
        var owned = counts.TryGetValue(position, out var c) ? c : 0;
        var required = Math.Max(0, RequiredSlots(position));
        if (owned >= required) return 0d;

        if (picksRemaining <= 0) return 0d;

        var slack = picksRemaining - UnfillableStarterSlots(counts);
        if (slack >= MandatoryUrgencyHorizon) return 0d;

        return MandatoryUrgencyMax
               * (MandatoryUrgencyHorizon - Math.Max(0, slack))
               / MandatoryUrgencyHorizon;
    }

    // ── Combined ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every need term for one position, plus the combined bonus and a one-line
    /// reason. The reason exists so that a UI row has to state why it is there —
    /// a panel that must explain itself cannot quietly degrade into the list
    /// above it, which is the failure this whole type was rewritten to end.
    /// </summary>
    public sealed record NeedAssessment(
        string Position,
        int Gap,
        double GapWeight,
        double DepthWeight,
        double Urgency,
        double Bonus,
        string Reason)
    {
        public bool HasNeed => Bonus > 0;
    }

    public NeedAssessment Assess(
        string position,
        IReadOnlyDictionary<string, int> counts,
        int picksRemaining)
    {
        var gapW = StarterGapWeight(position, counts);
        var depthW = DepthWeight(position, counts);
        var urgency = MandatoryUrgency(position, counts, picksRemaining);
        var gap = IsMandatory(position) ? 0 : StarterGap(position, counts);

        var bonus = (ValuePerStarterGap * gapW)
                  + (ValuePerDepthGap * depthW)
                  + urgency;

        // Ordered by what actually decides the pick, loudest first.
        var reason =
            urgency > 0
                ? $"{position} required · {picksRemaining} pick{(picksRemaining == 1 ? "" : "s")} left"
            : gapW > 0
                ? gap == 1 ? "would start" : $"{gap} starting slots open"
            : depthW >= 1.0
                ? $"no {position} behind the starter"
            : depthW > 0
                ? $"thin at {position}"
            : string.Empty;

        return new NeedAssessment(position, gap, gapW, depthW, urgency, bonus, reason);
    }
}
