using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;

namespace Soulvail.Core.Ai;

/// <summary>
/// One Wight: the health it stands up with, the three numbers a node can move, the clock that takes
/// it away again, and what it is walking at. Owned by <see cref="MinionSystem"/>, which is the only
/// thing that creates or retires one. See CH §3.2, AR §9 and M5-04a rules 1 and 2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shaped deliberately like an <see cref="EnemyAgent"/> and deliberately not one</b> (rule 1).
/// Reusing <see cref="EnemyAgent"/> would have been free at this line and expensive at six others:
/// <c>SpawnDirector.IsStageComplete</c> would hold the door shut until the player's own Wights had
/// died, <c>PlayerCombat.BuildCandidates</c> would offer them to the targeter so the player would
/// shoot them, <c>EnemySystem.ApplyDamage</c>'s kill path would pay experience for one, and
/// <c>WaveComposer</c> would need a roster entry the director must never draw. Each of those is a
/// separate place to remember an exception, found one at a time. The cost of a separate type is this
/// class and one more <c>IStatBlock</c> in M5-04b.
/// </para>
/// <para>
/// <b>It carries exactly the three <see cref="Stat"/>s an <see cref="EnemyAgent"/> carries, and that
/// is on purpose</b> (rule 2). <see cref="MaxHp"/> through <see cref="Health"/>,
/// <see cref="MoveSpeed"/>, <see cref="ContactDamage"/> — the same three <c>CombatantStats</c>
/// answers and refuses everything else around. It is what lets M5-04b's <c>MinionStats</c> be a
/// mirror rather than an invention, and it is why CH §3.2's Legion branch can buff a Wight's damage
/// through the <c>ModifyStat</c> the player already uses instead of a second mechanism.
/// </para>
/// <para>
/// <b>It has no blackboard, and that is a ruling rather than an omission</b> (rule 5).
/// <see cref="EnemyBlackboard"/> carries eleven fields of which a Wight would use none, and three
/// whose <em>names</em> would be lies on a friendly body — <c>DistanceToPlayer</c>,
/// <c>HasLineOfSight</c>, <c>LungeDirection</c>. Its whole working memory is
/// <see cref="QuarryId"/>, <see cref="NextAttackAt"/> and <see cref="ExpiresAt"/>, and those are
/// here where a reader can see them.
/// </para>
/// <para>
/// <b>Recycled, not rebuilt</b>, for <see cref="EnemyAgent"/>'s reason one class over:
/// <see cref="MinionSystem"/> builds its whole army at construction and hands the same instances out
/// again with fresh ids, because AR §4.3 forbids allocation on the per-frame paths and Rise
/// (M5-04b) puts a spawn on the kill path. <see cref="Health"/> is deliberately <em>not</em> rebuilt
/// — its constructor subscribes to <c>Stat.Changed</c> and never unsubscribes — and
/// <see cref="Initialise"/> wipes all three stacks before re-basing them, so a Wight handed back out
/// cannot arrive wearing the modifiers of the one that died.
/// </para>
/// <para>
/// <b>It is born from the run's <see cref="MinionRecipe"/> rather than from its
/// <see cref="MinionSpec"/></b> (M5-06a rule 3). The spec is what a designer typed and never moves;
/// the recipe is that spec's three numbers as live <c>Stat</c>s, which is where CH §3.2's Legion
/// nodes land. <see cref="Initialise"/> is the one place the difference shows, and it is why a node
/// taken while three Wights are up buffs the <em>fourth</em>: this body re-bases when it is handed
/// out, not when the modifier arrives.
/// </para>
/// </remarks>
public sealed class MinionAgent
{
    /// <summary>
    /// What this run says a Wight is born with. Held rather than read through
    /// <see cref="MinionSystem"/> so that <see cref="Initialise"/> stays the single place a spawned
    /// Wight's state is set; the instance is the run's one recipe, stable for the run's whole life.
    /// </summary>
    private readonly MinionRecipe _recipe;

    /// <param name="id">The run-stable id the system assigned.</param>
    /// <param name="spec">The minion this is an instance of — the run's one <c>minion.wight</c>.</param>
    /// <param name="recipe">
    /// This run's live numbers for that minion — what it is born with, after whatever nodes the
    /// player has taken.
    /// </param>
    /// <param name="position">Where it stood up.</param>
    /// <param name="expiresAt">Simulated run time at which it dissolves.</param>
    /// <remarks>
    /// Internal, and unguarded on purpose: <see cref="MinionSystem"/> is the only caller, it has
    /// already checked every argument on its own public surface, and <c>Soulvail.Tests.Core</c> has
    /// no <c>InternalsVisibleTo</c> — so a guard here would be unreachable from any test that could
    /// prove it works. The same decision <see cref="EnemyAgent"/>'s internal constructor made.
    /// </remarks>
    internal MinionAgent(
        int id,
        MinionSpec spec,
        MinionRecipe recipe,
        Vector3 position,
        float expiresAt)
    {
        Spec = spec;
        _recipe = recipe;

        // Built once and reused for the life of the system — see the class remarks. No shield and
        // no i-frames, exactly like an enemy: a Wight takes every hit that reaches it, and nothing
        // reaches one until something is written that hurts one (rule 9).
        Health = new Health(new Stat(recipe.MaxHp.Value), shield: null, hitIFrames: 0f);

        MoveSpeed = new Stat(recipe.MoveSpeed.Value);
        ContactDamage = new Stat(recipe.ContactDamage.Value);

        Initialise(id, position, expiresAt);
    }

    /// <summary>
    /// Stable identity for the run. Core answers in ids; views resolve them to objects. Ids are
    /// never reused within a run, so an id held across a despawn is stale rather than misleading —
    /// <c>EnemyRegistry</c>'s rule, and for its reason.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// The minion this is an instance of. One per run, shared by every Wight in the army, because
    /// <c>CharacterSpec.Minions</c> is a class's minions rather than a roster of them.
    /// </summary>
    public MinionSpec Spec { get; }

    /// <summary>
    /// Its health, built from <see cref="MinionSpec.MaxHp"/> with no shield and no i-frames. The
    /// instance is stable across recycling; its <see cref="Combat.Health.MaxHp"/> is re-based on
    /// each spawn.
    /// </summary>
    public Health Health { get; }

    /// <summary>The live maximum. Where <em>"+20 minion HP"</em> would go (rule 2).</summary>
    public Stat MaxHp => Health.MaxHp;

    /// <summary>How fast it walks, in metres per second. Read every tick it moves.</summary>
    public Stat MoveSpeed { get; }

    /// <summary>
    /// What one of its strikes costs an enemy. Where CH §3.2's Legion damage nodes land (rule 2).
    /// </summary>
    public Stat ContactDamage { get; }

    /// <summary>Where the body last reported it to be. Written by ingestion only (AR §3).</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>How fast it is actually moving, as last ingested.</summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>Simulated run time at which it dissolves. CH §3.2's twenty seconds.</summary>
    public float ExpiresAt { get; internal set; }

    /// <summary>
    /// The enemy id it is walking at, or 0 for none. Re-chosen on
    /// <see cref="MinionSystem.RetargetInterval"/>'s cadence, and dropped immediately when its
    /// quarry dies (rule 6).
    /// </summary>
    /// <remarks>
    /// Zero rather than −1 for "nobody", because <c>EnemyRegistry</c> issues ids from 1 precisely so
    /// that a default-initialised id field reads as nobody rather than as the first Husk of the run.
    /// </remarks>
    public int QuarryId { get; internal set; }

    /// <summary>
    /// Simulated run time at which it may strike again. Negative infinity on a Wight that has never
    /// struck, so "has never" is unambiguous — <c>EnemyAgent.DiedAt</c>'s spelling.
    /// </summary>
    public float NextAttackAt { get; internal set; }

    /// <summary>Still has hit points.</summary>
    /// <remarks>
    /// Unlike an <see cref="EnemyAgent"/>, a Wight leaves the army on the tick it dies — there is no
    /// corpse time, because a death and an expiry are announced separately and a view is given the
    /// position on the event (rule 10). So this is false only inside the call that killed it.
    /// </remarks>
    public bool IsAlive => !Health.IsDead;

    /// <summary>
    /// Establishes a fresh Wight: a new id, a position, a clock, no quarry, full health and three
    /// stats wiped and re-based to <em>this run's recipe</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single place a spawned Wight's state is set, whether it was just constructed or taken
    /// back off the pool — so the two paths cannot drift. <c>EnemyAgent.Initialise</c>'s shape, with
    /// its orderings kept for its reasons: every modifier goes before the bases are re-set, so
    /// nothing is ever briefly true, and <c>Health.MaxHp.Base</c> is set before
    /// <c>Health.Reset</c>, which refills from it.
    /// </para>
    /// <para>
    /// <b>The bases come from <see cref="_recipe"/>'s live values rather than from
    /// <see cref="Spec"/></b> (M5-06a rule 3), and this line is the whole of what <em>"a Legion node
    /// changes what being born means"</em> costs. It is also why it is here rather than in
    /// <c>MinionSystem.Spawn</c>: <c>Health.Reset</c> refills <c>Current</c> from
    /// <c>MaxHp.Value</c>, so a caller that re-based afterwards would stand every buffed Wight up on
    /// the unbuffed maximum.
    /// </para>
    /// <para>
    /// A <c>Stat</c> clamps nothing (ADR-0008), so a modifier stack could in principle drive one of
    /// the three somewhere absurd. <c>Stat.Base</c> refuses a non-finite value at its own door,
    /// which is the case that would otherwise spread silently — a NaN maximum makes every health
    /// fraction NaN for the body's whole life. The rest is the same exposure
    /// <see cref="MinionSystem.Cap"/> has and is deliberately not re-litigated here; that one is
    /// clamped because it indexes an array.
    /// </para>
    /// </remarks>
    internal void Initialise(int id, Vector3 position, float expiresAt)
    {
        Id = id;
        Position = position;
        Velocity = Vector3.Zero;
        ExpiresAt = expiresAt;

        // No quarry and no strike behind it. A zero quarry is what makes a freshly raised Wight
        // choose on its first tick rather than waiting out the retarget cadence (rule 6).
        QuarryId = 0;
        NextAttackAt = float.NegativeInfinity;

        // Every modifier goes, whoever put it there. Nothing in the build puts one on a *body* —
        // M5-06a's Legion nodes land on the recipe below, which is the point of rule 3 — so this is
        // a wipe of an empty stack today and the thing that keeps it empty tomorrow. The
        // no-argument overload rather than a list of known sources, because a list is what gets one
        // entry short the first time something else buffs a Wight (Stat.RemoveAll).
        Health.MaxHp.RemoveAll();
        MoveSpeed.RemoveAll();
        ContactDamage.RemoveAll();

        MoveSpeed.Base = _recipe.MoveSpeed.Value;
        ContactDamage.Base = _recipe.ContactDamage.Value;

        // Base before Reset, and the order is load-bearing: Health.Reset refills Current from
        // MaxHp.Value, so re-basing afterwards would leave a recycled Wight at whatever maximum the
        // previous life's modifiers had left standing.
        Health.MaxHp.Base = _recipe.MaxHp.Value;
        Health.Reset();
    }
}
