using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Ai;

/// <summary>
/// One living enemy: its identity for the run, the archetype it is an instance of, its health, and
/// its working state. Owned by <see cref="EnemyRegistry"/>, which is the only thing that creates
/// or re-initialises one. See AR §9.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing and ticks nothing.</b> This is the state an enemy *is*; what an enemy
/// *does* belongs to <c>EnemySystem</c> (M1-06) and the behaviour it dispatches (M1-18). The split
/// is the same one <c>Health</c> makes — a type that both sides of a fight read cannot name either
/// side's rules.
/// </para>
/// <para>
/// <b>Recycled, not rebuilt.</b> <see cref="EnemyRegistry"/> keeps every agent it has ever created
/// and hands a despawned one back out with a new <see cref="Id"/>, because AR §4.3 forbids
/// allocation on the per-frame paths and a wave of Husks is exactly that path. Two consequences
/// worth knowing: <see cref="Id"/> and <see cref="Spec"/> are settable within core, so a recycled
/// agent can come back as a different archetype; and <see cref="Health"/> is deliberately *not*
/// rebuilt, because its constructor subscribes to <c>Stat.Changed</c> and never unsubscribes — a
/// fresh one per spawn would allocate and leave the old one wired to a stat nobody owns.
/// </para>
/// <para>
/// <b>A recycled agent inherits its previous life's stat modifiers, and nothing here can stop
/// that.</b> <c>Stat</c> removes modifiers by source reference only; there is no "drop
/// everything". Nothing applies a modifier to an enemy's <see cref="Health.MaxHp"/> today, so the
/// reuse is sound as built — but the first thing that does (depth scaling, M2-03; Elite affixes,
/// M7-02) must either remove its own modifiers at despawn or give <c>Stat</c> a way to clear them,
/// or every recycled Husk will arrive wearing the last one's affixes. Carried on the PROGRESS
/// watch list rather than pre-solved here, because an API with no caller is one no test can
/// honestly exercise.
/// </para>
/// </remarks>
public sealed class EnemyAgent
{
    /// <param name="id">The run-stable id the registry assigned.</param>
    /// <param name="spec">The archetype this is an instance of.</param>
    /// <param name="position">Where it was spawned.</param>
    /// <remarks>
    /// Internal, and unguarded on purpose: <see cref="EnemyRegistry"/> is the only caller, it has
    /// already checked both arguments on its own public surface, and <c>Soulvail.Tests.Core</c> has
    /// no <c>InternalsVisibleTo</c> — so a guard here would be unreachable from any test that could
    /// prove it works. The same decision <c>RunState</c>'s internal constructor made in M0-10.
    /// </remarks>
    internal EnemyAgent(int id, EnemySpec spec, Vector3 position)
    {
        // Built once and then reused for the life of the registry — see the class remarks. No
        // shield and no i-frames: an enemy takes every hit that reaches it, which is what makes
        // the player's damage legible.
        Health = new Health(new Stat(spec.MaxHp), shield: null, hitIFrames: 0f);
        Blackboard = new EnemyBlackboard();

        Initialise(id, spec, position);
    }

    /// <summary>
    /// Stable identity for the run. Core answers in ids; views resolve them to objects. Ids are
    /// never reused within a run, so an id held across a despawn is stale rather than misleading.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>The archetype this is an instance of. Never null on a registered agent.</summary>
    public EnemySpec Spec { get; private set; }

    /// <summary>
    /// Its health, built from <see cref="EnemySpec.MaxHp"/> with no shield and no i-frames. The
    /// instance is stable across recycling; its <see cref="Health.MaxHp"/> is re-based on each
    /// spawn.
    /// </summary>
    public Health Health { get; }

    /// <summary>Its perception and working memory. The instance is stable across recycling.</summary>
    public EnemyBlackboard Blackboard { get; }

    /// <summary>Where it is, as last ingested from the snapshot (M1-06).</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>How fast it is actually moving, as last ingested (M1-06).</summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>
    /// Still has hit points. Distinct from being registered: a dead agent stays in
    /// <see cref="EnemyRegistry.Alive"/> until <c>EnemySystem</c> despawns it, so anything
    /// iterating that span checks this.
    /// </summary>
    public bool IsAlive => !Health.IsDead;

    /// <summary>
    /// Whether damage can land on it right now. True for everything in M1 — the first archetype
    /// that lowers it is the Warden, whose front shield blocks all damage (GD §8.1, M7-01) — and
    /// read by <c>TargetScorer</c> through a candidate, which never selects an enemy it cannot
    /// currently hurt.
    /// </summary>
    public bool IsVulnerable { get; internal set; }

    /// <summary>
    /// Establishes a fresh agent: a new id, an archetype, a position, full health and a blank
    /// blackboard.
    /// </summary>
    /// <remarks>
    /// The single place a spawned agent's state is set, whether it was just constructed or pulled
    /// off the free list — so the two paths cannot drift. Unguarded for the reason the constructor
    /// documents.
    /// </remarks>
    internal void Initialise(int id, EnemySpec spec, Vector3 position)
    {
        Id = id;
        Spec = spec;
        Position = position;
        Velocity = Vector3.Zero;
        IsVulnerable = true;

        Blackboard.Reset();

        // Base before Reset, and the order is load-bearing: Health.Reset refills Current from
        // MaxHp.Value, so re-basing afterwards would leave a recycled agent at the previous
        // archetype's hit points — a Husk arriving with a Warden's 90.
        Health.MaxHp.Base = spec.MaxHp;
        Health.Reset();
    }
}
