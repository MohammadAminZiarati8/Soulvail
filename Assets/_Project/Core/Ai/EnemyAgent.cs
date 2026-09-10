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
/// <b>A recycled agent forgets everything, and <see cref="Initialise"/> is where.</b> All three of
/// its stats are wiped with <c>Stat.RemoveAll()</c> — the no-argument overload — before they are
/// re-based, so an agent handed back out cannot arrive wearing the previous life's depth scaling
/// (M2-03) or, later, the previous life's Elite affixes (M7-02) and player-inflicted debuffs (M3).
/// This was ledger row 2 and it is closed here. A source token cleared at despawn was the
/// alternative and it was rejected for the reason <c>Stat.RemoveAll()</c> documents: a token
/// covers only its own source, so every future source would have to be enumerated at this exact
/// line, and that list gets one entry too short.
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

        // The other two numbers depth and affixes move. Built here rather than in Initialise for
        // Health's reason: a fresh Stat per spawn would allocate on a path a wave walks sixty
        // times, and Initialise re-bases these two exactly as it re-bases MaxHp.
        MoveSpeed = new Stat(spec.MoveSpeed);
        ContactDamage = new Stat(spec.ContactDamage);

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

    /// <summary>
    /// How fast it walks, in metres per second — GD §12.3's s(n) applied to
    /// <see cref="EnemySpec.MoveSpeed"/>. Read by <c>ChaserBehaviour</c> every tick it moves.
    /// </summary>
    /// <remarks>
    /// A <see cref="Stat"/> rather than a read of the spec, ruled by the owner at M2-00b: the
    /// architecture's rule is that every gameplay number carries a modifier stack (ADR-0008), and
    /// the alternative — a scalar the behaviour multiplies by — has no answer for M7-02's Hasted
    /// affix except a second mechanism. <see cref="EnemySpec"/>'s float stays what a designer
    /// typed and seeds the base.
    /// </remarks>
    public Stat MoveSpeed { get; }

    /// <summary>
    /// What one strike costs the player — GD §12.3's d(n) applied to
    /// <see cref="EnemySpec.ContactDamage"/>. Read by <c>ChaserBehaviour</c> on its damage frame.
    /// </summary>
    /// <remarks>
    /// A <see cref="Stat"/> for <see cref="MoveSpeed"/>'s reason, and with GD §12.4's one-shot
    /// rule sitting over it: no non-boss attack may exceed 35 % of the player's max HP at any
    /// depth, which is an acceptance check against d(n) rather than a clamp here (M2-15).
    /// </remarks>
    public Stat ContactDamage { get; }

    /// <summary>Its perception and working memory. The instance is stable across recycling.</summary>
    public EnemyBlackboard Blackboard { get; }

    /// <summary>
    /// The state machine that decides what this enemy does, or <see langword="null"/> for an
    /// archetype that decides nothing — <see cref="EnemyBehaviourKind.Static"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not to be confused with <c>Spec.Behaviour</c>, which is the <em>kind</em>.</b> That enum is
    /// authored data naming which code moves this archetype; this is the live object that does the
    /// moving. <c>EnemySystem.Tick</c> reads the first to decide whether to tick the second, which is
    /// the one place in the project that knows the full set of kinds (M1-06).
    /// </para>
    /// <para>
    /// <b>Typed as <see cref="ChaserBehaviour"/> rather than as an interface, deliberately.</b> There
    /// is one behaviour in the game today and an <c>IEnemyBehaviour</c> with a single implementer
    /// would be an abstraction invented for a second one nobody has written yet — M2-07's Spitter and
    /// M2-08's Bloater are where the shape of the seam becomes knowable. The dispatch that has to
    /// change with it is a single <c>switch</c> in <c>EnemySystem.Tick</c>.
    /// </para>
    /// <para>
    /// <b>Created once and reset, never rebuilt.</b> The same bargain <see cref="Health"/> and
    /// <see cref="Blackboard"/> make, and for a sharper reason: a <c>StateMachine</c> allocates three
    /// dictionaries and a delegate per handler, and <see cref="EnemyRegistry"/> recycles an agent on
    /// every spawn of a wave. So it is built on the first spawn whose archetype wants one and kept
    /// afterwards — a Chaser recycled as a Static keeps the object and stops being ticked, and comes
    /// back to it if it is recycled as a Chaser again.
    /// </para>
    /// </remarks>
    public ChaserBehaviour Behaviour { get; private set; }

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
    /// Simulated run time at which this agent's HP reached zero, or negative infinity while it is
    /// alive. Read only by <c>EnemySystem</c>'s corpse sweep, which retires it
    /// <c>EnemySystem.CorpseTime</c> seconds later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept here rather than in a table beside the registry because it is per-agent state with the
    /// same lifetime as every other field on this class — and because a side table keyed by id
    /// would have to be pruned in exactly the places <see cref="Initialise"/> already resets.
    /// </para>
    /// <para>
    /// Negative infinity, not zero, so "has never died" is unambiguous. It matters for a corpse
    /// that appeared without going through <c>EnemySystem.ApplyDamage</c> — the one door that
    /// stamps this: such an agent is retired on the next tick rather than lingering until the
    /// clock happens to pass 0.6, which is the loud direction to fail in.
    /// </para>
    /// </remarks>
    internal float DiedAt { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// Whether damage can land on it right now. True for everything in M1 — the first archetype
    /// that lowers it is the Warden, whose front shield blocks all damage (GD §8.1, M7-01) — and
    /// read by <c>TargetScorer</c> through a candidate, which never selects an enemy it cannot
    /// currently hurt.
    /// </summary>
    public bool IsVulnerable { get; internal set; }

    /// <summary>
    /// Establishes a fresh agent: a new id, an archetype, a position, three stats wiped and
    /// re-based to that archetype, full health and a blank blackboard.
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

        // Back to "has never died", so a recycled agent cannot inherit the previous life's death
        // stamp and be swept the moment it spawns.
        DiedAt = float.NegativeInfinity;

        Blackboard.Reset();

        // After the blackboard, and the order is load-bearing the same way the two health lines
        // below are: ChaserBehaviour.Reset clears StateTimer, and doing it before Blackboard.Reset
        // would simply have that clear it again — harmless today, and exactly the kind of ordering
        // that stops being harmless the first time a behaviour remembers something Reset does not.
        if (spec.Behaviour == EnemyBehaviourKind.Chaser)
        {
            Behaviour ??= new ChaserBehaviour(this);
        }

        // Reset whatever exists, including on an agent recycled as a Static: a behaviour left in
        // Windup would come back mid-telegraph the next time this agent is a Chaser.
        Behaviour?.Reset();

        // Ledger row 2, and the whole of its answer. Every modifier goes, whoever put it there:
        // this agent may have died at stage 40 wearing DepthScaling's three PercentMults, and it
        // is about to be handed back out as a fresh Husk at whatever depth the arena is on now.
        // Wiped *before* the re-basing below rather than after, so nothing is ever briefly true —
        // and with the no-argument overload rather than a list of known sources, because a list is
        // what gets one entry short the first time something else buffs an enemy (Stat.RemoveAll).
        Health.MaxHp.RemoveAll();
        MoveSpeed.RemoveAll();
        ContactDamage.RemoveAll();

        MoveSpeed.Base = spec.MoveSpeed;
        ContactDamage.Base = spec.ContactDamage;

        // Base before Reset, and the order is load-bearing: Health.Reset refills Current from
        // MaxHp.Value, so re-basing afterwards would leave a recycled agent at the previous
        // archetype's hit points — a Husk arriving with a Warden's 90.
        Health.MaxHp.Base = spec.MaxHp;
        Health.Reset();
    }
}
