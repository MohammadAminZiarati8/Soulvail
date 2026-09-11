using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// The rule that keeps an arena populated: when the dead are replaced, how fast, and where they are
/// allowed to appear. M1-19's half of the answer to "the fight runs out after eight seconds".
/// </summary>
/// <remarks>
/// <para>
/// Every row drives <c>EnemySystem.ApplyRespawn</c> directly rather than through <c>Tick</c>. The
/// method is the rule; <c>Tick</c> is the schedule, and it is one line — the interesting mistakes
/// are all in what the rule decides, and calling it directly is what lets a row state a time and a
/// player position instead of building a snapshot to imply them.
/// </para>
/// <para>
/// The randomness is scripted, so "it picked the far position" is a fact of the test rather than
/// something it waits for. <see cref="FixedRandom"/> shares one stream between all five names until
/// one is set on its own, which is exactly what makes the stream row below able to prove that
/// spawning draws from <c>Spawn</c> and nowhere else (ADR-0011).
/// </para>
/// </remarks>
[TestFixture]
public sealed class RespawnPolicyTests
{
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";

    /// <summary>Roomy enough that nothing here ever meets the registry's own limit.</summary>
    private const int Capacity = 64;

    /// <summary>M1-19's arena numbers, and the ones <c>RunScope</c> is dressed with.</summary>
    private const int KeepAlive = 12;

    private const float Delay = 2f;
    private const float MinDistance = 6f;

    /// <summary>Well outside <see cref="MinDistance"/> of every position any row uses.</summary>
    private static readonly Vector3 FarAway = new Vector3(500f, 0f, 500f);

    private RecordingEvents _events;
    private ContentCatalog _catalog;
    private FixedRandom _random;
    private EnemySystem _system;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _catalog = Catalog();
        _random = new FixedRandom();
        _system = new EnemySystem(_catalog, _events, _random, Scaling(), Capacity);
    }

    [Test]
    public void Respawn_WhenBelowKeepAlive_AfterDelay()
    {
        Fill(KeepAlive);

        // One dies at t = 0, which starts the clock the delay is measured against and leaves the
        // arena one short. The corpse stays registered for CorpseTime — the census that matters
        // here counts the breathing, not the registered.
        Kill(1, at: 0f);

        Assert.That(Living(), Is.EqualTo(KeepAlive - 1), "Sanity: the kill left the arena short.");

        Assert.That(Respawn(now: 1.9f), Is.Null, "A tenth of a second early is early.");
        Assert.That(Living(), Is.EqualTo(KeepAlive - 1));

        Assert.That(Respawn(now: 2.1f), Is.Not.Null, "Past the delay, the arena refills.");
        Assert.That(Living(), Is.EqualTo(KeepAlive));
    }

    [Test]
    public void Respawn_OnePerTick()
    {
        Fill(5);

        // Nothing has died, so the delay is already elapsed — see EnemySystem's _lastDeathAt. An
        // arena that opens under its quota fills immediately rather than waiting out a pause
        // measured from a death that never happened.
        Respawn(now: 10f);
        Respawn(now: 10f);
        Respawn(now: 10f);

        Assert.That(
            Living(),
            Is.EqualTo(8),
            "Three ticks, three enemies. A wipe refills over a few frames rather than in one, "
                + "which is both cheaper and better to look at.");
    }

    [Test]
    public void Respawn_SkipsPositionsNearPlayer()
    {
        var near = new Vector3(1f, 0f, 0f);
        var far = new Vector3(40f, 0f, 0f);

        // 0.0 draws index 0 out of two, which is the near one. The walk from there finds the far
        // one, and that is the whole rule: the draw chooses where to start looking, not where to
        // spawn.
        _random.SetSpawn(0f);

        EnemyAgent spawned = Respawn(Policy(near, far), player: Vector3.Zero, now: 10f);

        Assert.That(spawned, Is.Not.Null);
        Assert.That(spawned.Position, Is.EqualTo(far));
    }

    [Test]
    public void Respawn_NoSafePosition_Skips()
    {
        var first = new Vector3(1f, 0f, 0f);
        var second = new Vector3(0f, 0f, 2f);

        Assert.That(
            Respawn(Policy(first, second), player: Vector3.Zero, now: 10f),
            Is.Null,
            "The player is standing in the middle of the spawn ring. GD §12.4 would rather the "
                + "arena paused than put something on top of them.");

        Assert.That(Living(), Is.Zero);
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
    }

    [Test]
    public void Respawn_UsesSpawnStream()
    {
        var a = new Vector3(10f, 0f, 0f);
        var b = new Vector3(20f, 0f, 0f);
        var c = new Vector3(30f, 0f, 0f);
        var d = new Vector3(40f, 0f, 0f);

        // Every other stream says "the first one"; Spawn says "the last one". Only one of those
        // answers can show up in the result, and which it is proves which stream was drawn from.
        _random = new FixedRandom(0f).SetSpawn(0.99f);
        _system = new EnemySystem(_catalog, _events, _random, Scaling(), Capacity);

        EnemyAgent spawned = Respawn(Policy(a, b, c, d), player: FarAway, now: 10f);

        Assert.That(spawned, Is.Not.Null);
        Assert.That(
            spawned.Position,
            Is.EqualTo(d),
            "Spawning draws from IRandom.Spawn and nothing else (ADR-0011). A draw from another "
                + "stream would have landed on the first position.");
    }

    [Test]
    public void Respawn_NotAboveKeepAlive()
    {
        Fill(KeepAlive);

        Assert.That(Respawn(now: 10f), Is.Null);
        Assert.That(Living(), Is.EqualTo(KeepAlive));
    }

    [Test]
    public void Ctor_RefusesAPolicyThatCouldNeverSpawn()
    {
        var position = new Vector3(1f, 0f, 1f);

        Assert.Throws<ArgumentException>(
            () => new RespawnPolicy(default, new[] { position }, KeepAlive, Delay, MinDistance),
            "A policy naming no archetype would fail one layer down as the catalog's \"no enemy "
                + "with id ''\", pointing at content that was never at fault.");

        Assert.Throws<ArgumentException>(
            () => new RespawnPolicy(new ContentId(HuskId), Array.Empty<Vector3>(), KeepAlive, Delay, MinDistance),
            "Nowhere to spawn is an arena that silently stays empty.");

        Assert.Throws<ArgumentNullException>(
            () => new RespawnPolicy(new ContentId(HuskId), null, KeepAlive, Delay, MinDistance));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RespawnPolicy(new ContentId(HuskId), new[] { position }, 0, Delay, MinDistance),
            "Keeping nobody alive is spelled by having no policy at all.");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RespawnPolicy(new ContentId(HuskId), new[] { position }, KeepAlive, float.NaN, MinDistance),
            "A NaN delay never elapses, so the arena would empty once and stay empty in silence.");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RespawnPolicy(
                new ContentId(HuskId),
                new[] { new Vector3(float.NaN, 0f, 0f) },
                KeepAlive,
                Delay,
                MinDistance),
            "Every comparison against a NaN position is false, so the safety check would accept it.");
    }

    [Test]
    public void Positions_AreCopied()
    {
        var positions = new[] { new Vector3(10f, 0f, 0f) };

        var policy = new RespawnPolicy(new ContentId(HuskId), positions, KeepAlive, Delay, MinDistance);

        positions[0] = new Vector3(999f, 0f, 0f);

        Assert.That(
            policy.Positions[0],
            Is.EqualTo(new Vector3(10f, 0f, 0f)),
            "An arena that keeps filling its own array afterwards cannot change a policy it "
                + "already handed over.");
    }

    /// <summary>Spawns <paramref name="count"/> Husks, well away from every player position used here.</summary>
    private void Fill(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _system.Spawn(new ContentId(HuskId), new Vector3(100f + i, 0f, 100f));
        }
    }

    /// <summary>Kills the enemy with <paramref name="id"/> outright at <paramref name="at"/>.</summary>
    private void Kill(int id, float at)
    {
        _system.ApplyDamage(id, 10_000f, at);
    }

    /// <summary>How many registered enemies are breathing — corpses excluded, which is the census the rule reads.</summary>
    private int Living()
    {
        ReadOnlySpan<EnemyAgent> agents = _system.Registry.Alive;

        int count = 0;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].IsAlive)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>One respawn tick against a default policy, with the player standing far away.</summary>
    private EnemyAgent Respawn(float now) =>
        Respawn(Policy(new Vector3(20f, 0f, 20f)), FarAway, now);

    private EnemyAgent Respawn(RespawnPolicy policy, Vector3 player, float now) =>
        _system.ApplyRespawn(policy, player, now, _random.Spawn);

    private static RespawnPolicy Policy(params Vector3[] positions) =>
        new RespawnPolicy(new ContentId(HuskId), positions, KeepAlive, Delay, MinDistance);

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
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
        behaviour: EnemyBehaviourKind.Static);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        120f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk() });

    /// <summary>
    /// The depth scaling every <c>EnemySystem</c> in this fixture is built with, required as of
    /// M2-03.
    /// </summary>
    /// <remarks>
    /// Inert in every row here, and that is by construction rather than by luck: the system's
    /// <c>Depth</c> defaults to 1, where GD §12.3's three multipliers are all exactly 1, so an
    /// enemy spawned by this fixture wears its archetype's authored numbers. The rows that are
    /// about depth are <c>DepthScalingTests</c>' and <c>EnemySystemTests.Spawn_AppliesDepth</c>.
    /// </remarks>
    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());
}
