using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// The M1-05 spec's eight rules, plus four rows this task added: rule 4's second clause and rule
/// 8's re-initialisation, neither of which the spec's table pinned, and the two guards on the
/// registry's public surface.
/// </summary>
/// <remarks>
/// <para>
/// Everything goes through <see cref="EnemyRegistry"/>, because that is the only route there is:
/// <c>EnemyAgent</c>'s constructor is internal and <c>Soulvail.Tests.Core</c> has no
/// <c>InternalsVisibleTo</c> (M0-10). The blackboard is reached the same way, through a spawned
/// agent, which is also how M1-06 will reach it.
/// </para>
/// <para>
/// GD §8.1's Husk throughout — 36 HP, priority 1 — with the four numbers M1-05 originates
/// (contact 8, reach 1.2, windup 0.4, recover 0.6) carried along so a row that reads them is
/// reading what <c>Husk.asset</c> will ship in M1-07.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EnemyRegistryTests
{
    /// <summary>The pool size rule 8's row measures against.</summary>
    private const int WarmupCapacity = 64;

    /// <summary>Where the allocation row parks what it reads, so the calls cannot be elided.</summary>
    private int _sink;

    // ---- Rule 1: ids start at 1, increase, and are never reused --------------------------------

    [Test]
    public void Spawn_AssignsIncreasingIds()
    {
        var registry = new EnemyRegistry(8);

        EnemyAgent first = registry.Spawn(Husk, Somewhere);
        EnemyAgent second = registry.Spawn(Husk, Somewhere);
        EnemyAgent third = registry.Spawn(Husk, Somewhere);

        Assert.That(first.Id, Is.EqualTo(1));
        Assert.That(second.Id, Is.EqualTo(2));
        Assert.That(third.Id, Is.EqualTo(3));
    }

    [Test]
    public void Ids_NeverReusedWithinRun()
    {
        var registry = new EnemyRegistry(8);

        EnemyAgent first = registry.Spawn(Husk, Somewhere);
        Assert.That(first.Id, Is.EqualTo(1));

        Assert.That(registry.Despawn(first.Id), Is.True);

        // The agent object comes back — that is rule 8 — but the id does not. An event already
        // published, or a view mid-dissolve, still names 1, and 1 must resolve to nothing rather
        // than to the stranger now standing in that pool slot.
        EnemyAgent recycled = registry.Spawn(Husk, Somewhere);

        Assert.That(recycled.Id, Is.EqualTo(2));
        Assert.That(registry.TryGet(1, out _), Is.False, "The retired id must not resolve.");
    }

    // ---- Rule 2: a full registry refuses ------------------------------------------------------

    [Test]
    public void Spawn_WhenFull_Throws()
    {
        var registry = new EnemyRegistry(2);

        registry.Spawn(Husk, Somewhere);
        registry.Spawn(Husk, Somewhere);

        // Loud rather than silent: dropping the enemy would leave the director believing it spawned
        // a wave it did not, and growing the pool would allocate mid-frame.
        Assert.Throws<InvalidOperationException>(() => registry.Spawn(Husk, Somewhere));

        Assert.That(registry.AliveCount, Is.EqualTo(2));
        Assert.That(registry.Capacity, Is.EqualTo(2));
    }

    // ---- Rule 3: despawn compacts, and keeps spawn order --------------------------------------

    [Test]
    public void Despawn_RemovesFromAlive_KeepsOrder()
    {
        var registry = new EnemyRegistry(8);

        registry.Spawn(Husk, Somewhere);
        registry.Spawn(Husk, Somewhere);
        registry.Spawn(Husk, Somewhere);

        Assert.That(registry.Despawn(2), Is.True);

        // [1, 3], not [1] with 3 swapped into the hole. A swap would make iteration order depend on
        // the history of deaths, which feeds TargetScorer's tie-break, which would let two runs from
        // one seed diverge on the strength of who died first.
        Assert.That(AliveIds(registry), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(registry.AliveCount, Is.EqualTo(2));
    }

    [Test]
    public void Despawn_Unknown_ReturnsFalse()
    {
        var registry = new EnemyRegistry(8);

        // Not an error, so a caller cleaning up after a wave can despawn unconditionally and
        // M1-11's death flow can run twice for one corpse without remembering.
        Assert.That(registry.Despawn(99), Is.False);
    }

    // ---- Rule 4: Alive means registered, not breathing ----------------------------------------

    [Test]
    public void Alive_KeepsDeadAgentUntilDespawned()
    {
        // Beyond the spec's table, and the half of rule 4 it left unpinned. The rule's first
        // sentence says Alive holds "only living" agents and its second says a dead-but-not-
        // despawned one is still there; the second is what the death flow needs, so it is what is
        // built. AliveCount counts registered agents, and a reader that cares asks IsAlive.
        var registry = new EnemyRegistry(8);

        EnemyAgent agent = registry.Spawn(Husk, Somewhere);

        agent.Health.ApplyDamage(1_000f, now: 0f);

        Assert.That(agent.IsAlive, Is.False, "Sanity: overkill killed it.");
        Assert.That(registry.AliveCount, Is.EqualTo(1));
        Assert.That(AliveIds(registry), Is.EqualTo(new[] { 1 }));
        Assert.That(
            registry.TryGet(1, out _),
            Is.True,
            "The corpse must still resolve, or M1-11 cannot publish a death that names it.");

        Assert.That(registry.Despawn(1), Is.True);
        Assert.That(registry.AliveCount, Is.EqualTo(0));
    }

    // ---- Rule 5: lookup by id ------------------------------------------------------------------

    [Test]
    public void TryGet_FindsAlive_NotDespawned()
    {
        var registry = new EnemyRegistry(8);

        EnemyAgent agent = registry.Spawn(Husk, Somewhere);

        Assert.That(registry.TryGet(agent.Id, out EnemyAgent found), Is.True);
        Assert.That(found, Is.SameAs(agent));

        Assert.That(registry.Despawn(agent.Id), Is.True);

        Assert.That(registry.TryGet(agent.Id, out EnemyAgent gone), Is.False);
        Assert.That(gone, Is.Null);
    }

    // ---- Rule 6: health comes from the spec ---------------------------------------------------

    [Test]
    public void Agent_HasHealthFromSpec()
    {
        var registry = new EnemyRegistry(8);

        EnemyAgent agent = registry.Spawn(Husk, Somewhere);

        Assert.That(agent.Health.MaxHp.Value, Is.EqualTo(36f));
        Assert.That(agent.Health.Current, Is.EqualTo(36f));

        // No shield and no i-frames: an enemy takes every hit that reaches it, which is what makes
        // the player's damage legible. HasShield asks whether there is a ShieldSpec at all.
        Assert.That(agent.Health.HasShield, Is.False);
        Assert.That(agent.Health.ShieldMax, Is.EqualTo(0f));

        // Two hits in the same instant both land, which is what zero i-frames means.
        Assert.That(agent.Health.ApplyDamage(5f, now: 0f).ToHp, Is.EqualTo(5f));
        Assert.That(agent.Health.ApplyDamage(5f, now: 0f).ToHp, Is.EqualTo(5f));
        Assert.That(agent.Health.Current, Is.EqualTo(26f));
    }

    [Test]
    public void Agent_DefaultsVulnerable()
    {
        var registry = new EnemyRegistry(8);

        EnemyAgent agent = registry.Spawn(Husk, Somewhere);

        // True for everything in M1. The first archetype that lowers it is the Warden, whose front
        // shield blocks all damage (GD §8.1, M7-01).
        Assert.That(agent.IsVulnerable, Is.True);
    }

    [Test]
    public void Agent_CarriesSpecAndPosition()
    {
        var registry = new EnemyRegistry(8);
        EnemySpec husk = Husk;
        var position = new Vector3(3f, 0f, -4f);

        EnemyAgent agent = registry.Spawn(husk, position);

        Assert.That(agent.Spec, Is.SameAs(husk), "The spec is shared, never copied.");
        Assert.That(agent.Position, Is.EqualTo(position));
        Assert.That(agent.Velocity, Is.EqualTo(Vector3.Zero));
    }

    // ---- Rule 7: the blackboard zeroes ---------------------------------------------------------

    [Test]
    public void Blackboard_Reset_Zeroes()
    {
        var registry = new EnemyRegistry(8);

        EnemyBlackboard blackboard = registry.Spawn(Husk, Somewhere).Blackboard;

        Fill(blackboard);
        blackboard.Reset();

        AssertBlank(blackboard);
    }

    // ---- Rule 8: recycling, and what it must clear --------------------------------------------

    [Test]
    public void Spawn_RecycledAgent_IsFullyReinitialised()
    {
        // Beyond the spec's table. Rule 8 is stated as an allocation budget, but the thing that
        // budget forces — reusing the object — is where the bugs are: a recycled agent that keeps
        // the last one's hit points, blackboard or vulnerability is a wave of Husks arriving with a
        // Warden's health and a stale distance to the player.
        var registry = new EnemyRegistry(1);

        EnemyAgent first = registry.Spawn(Husk, new Vector3(1f, 0f, 1f));

        first.Health.ApplyDamage(26f, now: 0f);
        first.Blackboard.DistanceToPlayer = 7f;
        first.Blackboard.StateTimer = 2f;

        Assert.That(first.Health.Current, Is.EqualTo(10f), "Sanity: it was hurt.");

        Assert.That(registry.Despawn(first.Id), Is.True);

        var position = new Vector3(5f, 0f, -5f);
        EnemySpec tougher = Tougher;
        EnemyAgent second = registry.Spawn(tougher, position);

        Assert.That(second, Is.SameAs(first), "Sanity: the pool handed the same object back.");
        Assert.That(second.Id, Is.EqualTo(2));
        Assert.That(second.Spec, Is.SameAs(tougher), "A recycled agent may return as another archetype.");

        // The maximum is re-based *before* the refill, or the recycled agent would come back at the
        // previous archetype's hit points.
        Assert.That(second.Health.MaxHp.Value, Is.EqualTo(90f));
        Assert.That(second.Health.Current, Is.EqualTo(90f));
        Assert.That(second.IsAlive, Is.True);

        // Vacuous today, and deliberately kept as the row that will stop being vacuous. Nothing
        // outside core can lower IsVulnerable — its setter is internal and there is no
        // InternalsVisibleTo (M0-10) — so no test can yet put an agent into the state whose reset
        // this checks. The first thing that lowers it is the Warden's facing rule (M7-01), and that
        // is the task that owes this row its teeth.
        Assert.That(second.IsVulnerable, Is.True);
        Assert.That(second.Position, Is.EqualTo(position));
        Assert.That(second.Velocity, Is.EqualTo(Vector3.Zero));

        AssertBlank(second.Blackboard);
    }

    [Test]
    public void Spawn_AfterWarmup_AllocatesNothing()
    {
        var registry = new EnemyRegistry(WarmupCapacity);

        // Hoisted out of the measured body: the fixture property builds a spec on every read, which
        // would be measured as the registry allocating.
        EnemySpec husk = Husk;
        var position = new Vector3(1f, 0f, 2f);

        // Warm-up: every agent in the pool is constructed once and then retired, so the measured
        // body can only ever take the recycling path.
        for (int i = 0; i < WarmupCapacity; i++)
        {
            registry.Spawn(husk, position);
        }

        Assert.That(registry.AliveCount, Is.EqualTo(WarmupCapacity));

        for (int id = 1; id <= WarmupCapacity; id++)
        {
            Assert.That(registry.Despawn(id), Is.True);
        }

        Assert.That(registry.AliveCount, Is.EqualTo(0));

        AllocationAssert.None(() =>
        {
            EnemyAgent agent = registry.Spawn(husk, position);
            registry.Despawn(agent.Id);
        });

        // Ten thousand spawns later the ids are well past the pool size, which is the part of rule 1
        // that could quietly have cost an allocation: the id dictionary is pre-sized to the capacity
        // and reuses its own free list, so monotonic ids never grow it.
        _sink = registry.AliveCount;
        Assert.That(_sink, Is.EqualTo(0));

        // And the probe is live under this harness, not merely in AllocationAssert's own fixture —
        // M1-01's lesson: a measurement that cannot fail proves nothing.
        Assert.Throws<AssertionException>(
            () => AllocationAssert.None(() => _sink = new int[8].Length, iterations: 8));
    }

    // ---- Rule 1 again: Clear is the between-runs reset -----------------------------------------

    [Test]
    public void Clear_ResetsIdsAndAlive()
    {
        var registry = new EnemyRegistry(8);

        registry.Spawn(Husk, Somewhere);
        registry.Spawn(Husk, Somewhere);
        registry.Spawn(Husk, Somewhere);

        registry.Clear();

        Assert.That(registry.AliveCount, Is.EqualTo(0));
        Assert.That(AliveIds(registry), Is.Empty);
        Assert.That(registry.TryGet(1, out _), Is.False);

        // Ids start again, which is safe exactly because Clear is a between-runs call: nothing
        // outside is still holding an id from the run that ended.
        Assert.That(registry.Spawn(Husk, Somewhere).Id, Is.EqualTo(1));

        // And the cleared agents went back on the free list rather than being dropped, so the next
        // wave still costs nothing.
        Assert.That(registry.AliveCount, Is.EqualTo(1));
    }

    // ---- EnemySpec validation -----------------------------------------------------------------

    [Test]
    public void Spec_Validation()
    {
        // A priority outside GD §8.1's 1–8 would out-shout or under-shout every archetype at once,
        // and priority dominates TargetScorer's formula on purpose.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(targetPriority: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(targetPriority: 9));

        // An enemy that starts dead, and one that can never be killed.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(maxHp: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(maxHp: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(maxHp: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(maxHp: float.PositiveInfinity));

        // A reach of zero can never land a strike; an infinite one strikes from across the arena.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(reach: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(reach: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(reach: float.PositiveInfinity));

        // The four that may be zero but not negative, and never non-finite. NaN is the one each
        // guard's `!(v >= 0f)` spelling exists for — `v < 0f` waves it straight through.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(moveSpeed: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(moveSpeed: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(windupTime: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(windupTime: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(recoverTime: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(recoverTime: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(contactDamage: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(contactDamage: float.NaN));

        // Aggro range is Positive rather than NonNegative, and the zero case is the one worth
        // spelling out: an archetype that notices the player at 0 m never leaves Idle, so it is a
        // Static enemy authored the long way round — which the behaviour field already says
        // properly (M2-06 rule 4).
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: float.PositiveInfinity));

        // A malformed identity is a different question and gets a different exception, exactly as
        // CharacterSpec does: ArgumentException for the id, ArgumentOutOfRangeException for a number.
        Assert.Throws<ArgumentException>(
            () => new EnemySpec(
                default,
                new LocKey("enemy.husk.name"),
                maxHp: 36f,
                moveSpeed: 3.5f,
                targetPriority: 1,
                threatCost: 4,
                isElite: false,
                contactDamage: 8f,
                reach: 1.2f,
                windupTime: 0.4f,
                recoverTime: 0.6f,
                aggroRange: 30f,
                behaviour: EnemyBehaviourKind.Chaser));

        // The legal edges hold: a stationary, harmless, untelegraphed enemy is a coherent thing to
        // author — GD §8.1's Choir never attacks — and zero recovery is a strike with no punish
        // window.
        Assert.DoesNotThrow(
            () => Spec(moveSpeed: 0f, contactDamage: 0f, windupTime: 0f, recoverTime: 0f));
        Assert.DoesNotThrow(() => Spec(targetPriority: 8));

        // M2-04 rule 12: a threat cost of zero is not a cheap archetype, it is a non-terminating
        // WaveComposer — the fill loop buys while anything is affordable, and a free body is always
        // affordable. Guarded here rather than in the composer, because the composer cannot report
        // it in a way that names the asset. Negative is refused by the same comparison; 1 is the
        // floor rather than GD §8.1's cheapest 4, so a future archetype cheaper than a Husk is a
        // tuning decision and not a code change.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(threatCost: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(threatCost: -1));
        Assert.DoesNotThrow(() => Spec(threatCost: 1));
    }

    // ---- The guards on the registry's public surface -------------------------------------------

    [Test]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        // Beyond the spec's table. A registry that can hold no enemies is a configuration mistake
        // rather than a valid state to run with — the same guard as WorldSnapshot's, and it also
        // keeps the three preallocated buffers from being zero-length.
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyRegistry(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyRegistry(-1));
    }

    [Test]
    public void Spawn_RejectsNullSpec()
    {
        // Beyond the spec's table, and the reason EnemyAgent's internal constructor carries no
        // guards: this is the public door, so this is where the check belongs and the only place a
        // test could reach one.
        var registry = new EnemyRegistry(8);

        Assert.Throws<ArgumentNullException>(() => registry.Spawn(null, Somewhere));
        Assert.That(registry.AliveCount, Is.EqualTo(0), "A refused spawn registers nothing.");
        Assert.That(registry.Spawn(Husk, Somewhere).Id, Is.EqualTo(1), "And spends no id.");
    }

    // ---- Fixture -------------------------------------------------------------------------------

    /// <summary>
    /// GD §8.1's Husk, with the five numbers M1-05 originates. A property rather than a
    /// <c>static readonly</c> field because <c>.editorconfig</c> carries both an underscore-camel
    /// rule for private fields and a PascalCase rule for static readonly ones, and which wins is
    /// not obvious from reading it (M1-04).
    /// </summary>
    private static EnemySpec Husk => Spec();

    /// <summary>
    /// A second archetype for the recycling row, distinguished only by its health — GD §8.1's
    /// Warden HP. <c>Static</c> because no behaviour for it exists yet; only <c>MaxHp</c> matters
    /// here.
    /// </summary>
    private static EnemySpec Tougher => new EnemySpec(
        new ContentId("enemy.warden"),
        new LocKey("enemy.warden.name"),
        maxHp: 90f,
        moveSpeed: 2f,
        targetPriority: 3,
        threatCost: 14,
        isElite: false,
        contactDamage: 14f,
        reach: 1.6f,
        windupTime: 0.6f,
        recoverTime: 0.8f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>A position no row depends on, for the rows that are not about positions.</summary>
    private static Vector3 Somewhere => new Vector3(2f, 0f, 2f);

    /// <summary>
    /// A Husk with one field overridden, so a validation row names the one number it is about.
    /// </summary>
    private static EnemySpec Spec(
        float maxHp = 36f,
        float moveSpeed = 3.5f,
        int targetPriority = 1,
        int threatCost = 4,
        float contactDamage = 8f,
        float reach = 1.2f,
        float windupTime = 0.4f,
        float recoverTime = 0.6f,
        float aggroRange = 30f)
        => new EnemySpec(
            new ContentId("enemy.husk"),
            new LocKey("enemy.husk.name"),
            maxHp: maxHp,
            moveSpeed: moveSpeed,
            targetPriority: targetPriority,
            threatCost: threatCost,
            isElite: false,
            contactDamage: contactDamage,
            reach: reach,
            windupTime: windupTime,
            recoverTime: recoverTime,
            aggroRange: aggroRange,
            behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>
    /// The ids in <see cref="EnemyRegistry.Alive"/>, in order. Copied out because a
    /// <c>ReadOnlySpan</c> cannot be handed to an NUnit constraint.
    /// </summary>
    private static int[] AliveIds(EnemyRegistry registry)
    {
        ReadOnlySpan<EnemyAgent> alive = registry.Alive;
        var ids = new int[alive.Length];

        for (int i = 0; i < alive.Length; i++)
        {
            ids[i] = alive[i].Id;
        }

        return ids;
    }

    /// <summary>Writes something non-default into every field, both halves.</summary>
    private static void Fill(EnemyBlackboard blackboard)
    {
        blackboard.SelfPosition = new Vector3(1f, 2f, 3f);
        blackboard.SelfVelocity = new Vector3(4f, 5f, 6f);
        blackboard.PlayerPosition = new Vector3(7f, 8f, 9f);
        blackboard.DistanceToPlayer = 10f;
        blackboard.DirectionToPlayer = new Vector2(0f, 1f);
        blackboard.PathDirectionToPlayer = new Vector2(1f, 0f);
        blackboard.HasLineOfSight = true;
        blackboard.AlliesNearby = 5;

        blackboard.StateTimer = 11f;
        blackboard.LungeDirection = new Vector2(0.6f, 0.8f);
        blackboard.NextAttackAt = 12f;
    }

    private static void AssertBlank(EnemyBlackboard blackboard)
    {
        Assert.That(blackboard.SelfPosition, Is.EqualTo(Vector3.Zero));
        Assert.That(blackboard.SelfVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(blackboard.PlayerPosition, Is.EqualTo(Vector3.Zero));
        Assert.That(blackboard.DistanceToPlayer, Is.EqualTo(0f));
        Assert.That(blackboard.DirectionToPlayer, Is.EqualTo(Vector2.Zero));
        Assert.That(blackboard.PathDirectionToPlayer, Is.EqualTo(Vector2.Zero));
        Assert.That(blackboard.HasLineOfSight, Is.False);
        Assert.That(blackboard.AlliesNearby, Is.EqualTo(0));

        Assert.That(blackboard.StateTimer, Is.EqualTo(0f));
        Assert.That(blackboard.LungeDirection, Is.EqualTo(Vector2.Zero));

        // Zero is in the past for every real `now`, so a spawned enemy may strike immediately
        // rather than being gifted a cooldown it did not earn.
        Assert.That(blackboard.NextAttackAt, Is.EqualTo(0f));
    }
}
