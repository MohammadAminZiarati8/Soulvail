namespace Soulvail.Core.Combat;

/// <summary>
/// Everything <see cref="TargetScorer"/> is allowed to know about one enemy: the five numbers
/// and flags CC §3.2's scoring formula reads, and nothing else. See CC §3.1–3.3 and §3.6.
/// </summary>
/// <remarks>
/// <para>
/// The point of this type is what it leaves out. The scorer never sees a position, a velocity,
/// an <c>EnemyAgent</c> or a snapshot — only plain numbers — so "which enemy should the gun
/// face" is a pure function that a test can pin exactly and a designer can reason about from
/// the tuning table alone. M1-08 is what builds these from live enemies each targeting tick.
/// </para>
/// <para>
/// A <c>readonly struct</c> passed by <c>in</c> and gathered into a caller-owned
/// <see cref="System.ReadOnlySpan{T}"/>, because the targeting loop runs at 10 Hz over every
/// enemy in range — up to 28 of them (GD §11) — and AR §4.3 says that path allocates nothing.
/// </para>
/// <para>
/// The constructor neither validates nor derives, for the reason <c>PlayerMoveIntent</c> and
/// <c>EnemySense</c> give: core has already computed each of these from state it owns, so a
/// second check here would be a copy of an invariant nobody is watching. The one input that
/// could misbehave is <see cref="Distance"/>, and <see cref="TargetScorer"/> is written so that
/// a non-finite one loses every comparison rather than winning one — see
/// <see cref="TargetScorer.SelectBest"/>. Authored numbers are a different matter and are
/// guarded where they enter, in <c>TargetingSpec</c>.
/// </para>
/// </remarks>
public readonly struct TargetCandidate
{
    /// <summary>The enemy's run-stable id, as carried by <c>EnemySense.Id</c>.</summary>
    public readonly int Id;

    /// <summary>Metres from the player. Compared against <c>TargetingSpec.AcquireRange</c>.</summary>
    public readonly float Distance;

    /// <summary>
    /// The archetype's designer-tuned priority, 1–8, from GD §8.1 — Husk 1, Choir 8. CC §3.2
    /// calls this "the knob that makes auto-aim feel intelligent", and it dominates the formula
    /// on purpose: a Choir healing the pack at 11 m must outrank a Husk chewing on you at 3 m,
    /// because that is what a good player would do.
    /// </summary>
    public readonly int Priority;

    /// <summary>Whether the enemy is an Elite (M7-02), worth <c>EliteBonus</c>.</summary>
    public readonly bool IsElite;

    /// <summary>
    /// Whether damage can land on it <em>right now</em>. CC §3.6: the targeter never selects an
    /// enemy it cannot currently damage — a Warden immune from the front scores itself out of
    /// contention rather than being shot at pointlessly.
    /// </summary>
    public readonly bool IsVulnerable;

    /// <summary>
    /// Effective hit points remaining — shield plus HP, whatever the next second of damage would
    /// actually have to chew through. Compared against the caller's one-second damage estimate
    /// for the finisher bonus.
    /// </summary>
    public readonly float Hp;

    /// <param name="id">The enemy's run-stable id.</param>
    /// <param name="distance">Metres from the player.</param>
    /// <param name="priority">Archetype priority, 1–8 (GD §8.1).</param>
    /// <param name="isElite">Whether it is an Elite.</param>
    /// <param name="isVulnerable">Whether damage can land on it right now.</param>
    /// <param name="hp">Effective hit points remaining.</param>
    public TargetCandidate(int id, float distance, int priority, bool isElite, bool isVulnerable, float hp)
    {
        Id = id;
        Distance = distance;
        Priority = priority;
        IsElite = isElite;
        IsVulnerable = isVulnerable;
        Hp = hp;
    }
}
