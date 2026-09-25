using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// M6-08's primitive: <see cref="SpawnBurnZone"/> and its handler, over a real
/// <see cref="ZoneSystem"/> wired to burn — <c>SpawnHealZoneTests</c>' subject, the other side.
/// </summary>
/// <remarks>
/// <b>The numbers are Emberfall's and Cinder Nova's</b> (rule 3), written here as the spec states
/// them. The shipped assets pin their own copies in <c>EmberwrightTreeTests</c>; this file is about
/// what a burn <em>does</em>, and would be the same file over any three positive numbers.
/// Registration in a real run is <c>EmberwrightTreeTests.Burn_IsRegisteredForEveryClass</c>, because
/// the run it needs is the shipped catalog's.
/// </remarks>
[TestFixture]
public sealed class SpawnBurnZoneTests
{
    private const string EmberwrightId = "character.emberwright";
    private const string HuskId = "enemy.husk";
    private const string DescentId = "mode.descent";
    private const int Seed = 7;

    /// <summary>Emberfall: 4 m for 4 s at 6 a pulse — eight pulses, 48.</summary>
    private const float EmberfallRadius = 4f;
    private const float EmberfallDuration = 4f;
    private const float EmberfallDamage = 6f;

    /// <summary>Cinder Nova: 6 m for 1.5 s at 10 a pulse — three pulses, 30.</summary>
    private const float NovaRadius = 6f;
    private const float NovaDuration = 1.5f;
    private const float NovaDamage = 10f;

    private const float Interval = MovementSkillSpec.PoolPulseInterval;

    /// <summary>Tough enough that no row here kills what it is measuring.</summary>
    private const float HuskMaxHp = 1_000f;

    private const float PlayerMaxHp = 70f;
    private const int EnemyCapacity = 32;
    private const float Tolerance = 1e-3f;

    private RecordingEvents _events;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ZoneSystem _zones;
    private SimulatedClock _clock;
    private SpawnBurnZoneHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _enemies = Enemies(_events);
        _player = new PlayerCombat(Emberwright(), _events, new RecordingIntents(), EnemyCapacity);
        _zones = new ZoneSystem(_player.Health, _player.Blackboard, _events, _enemies, _player);
        _clock = new SimulatedClock();
        _handler = new SpawnBurnZoneHandler(_zones, _clock);
    }

    // ---- Rule 3: what one cast does ---------------------------------------------------------------

    [Test]
    public void Burn_SpawnsAZoneThatBurns()
    {
        EnemyAgent inside = Husk(1f, 1f);
        EnemyAgent outside = Husk(5f, 0f);

        _handler.Apply(Emberfall(), this);

        IReadOnlyList<ZoneSpawned> spawned = _events.Of<ZoneSpawned>();

        Assert.That(spawned, Has.Count.EqualTo(1));
        Assert.That(spawned[0].Radius, Is.EqualTo(EmberfallRadius).Within(Tolerance));
        Assert.That(spawned[0].Duration, Is.EqualTo(EmberfallDuration).Within(Tolerance));
        Assert.That(_zones.SideAt(0), Is.EqualTo(ZoneSide.BurnsEnemies), "a burn, not a heal.");

        for (int pulse = 1; pulse <= 8; pulse++)
        {
            _zones.Tick(pulse * Interval);
        }

        Assert.That(_events.Count<ZoneBurned>(), Is.EqualTo(8), "eight pulses on the one inside.");
        Assert.That(inside.Health.Current, Is.EqualTo(HuskMaxHp - 48f).Within(Tolerance), "8 × 6.");
        Assert.That(outside.Health.Current, Is.EqualTo(HuskMaxHp).Within(Tolerance), "4 m reaches 4 m.");
        Assert.That(_zones.Count, Is.Zero, "and retired on its last pulse.");
        Assert.That(_events.Count<ZoneHealed>(), Is.Zero, "a burn heals nobody.");
    }

    [Test]
    public void Burn_CinderNovaIsTightAndFast()
    {
        EnemyAgent inside = Husk(5f, 0f);

        _handler.Apply(new SpawnBurnZone(NovaRadius, NovaDuration, NovaDamage), this);

        for (int pulse = 1; pulse <= 3; pulse++)
        {
            _zones.Tick(pulse * Interval);
        }

        Assert.That(inside.Health.Current, Is.EqualTo(HuskMaxHp - 30f).Within(Tolerance), "3 × 10, at 5 m.");
        Assert.That(_zones.Count, Is.Zero, "over in a second and a half.");
    }

    [Test]
    public void Burn_IsPlacedAtThePlayersFeetOnTheRunsClock()
    {
        // ZoneSystem.Spawn's existing rule: the blackboard's position, and the handler's clock.
        _player.Blackboard.PlayerPosition = new Vector3(2f, 0f, -3f);
        _clock.Now = 10f;

        _handler.Apply(Emberfall(), this);

        Assert.That(_zones.PositionAt(0), Is.EqualTo(new Vector3(2f, 0f, -3f)));

        _zones.Tick(10f + EmberfallDuration - Interval);
        Assert.That(_zones.Count, Is.EqualTo(1), "still burning half a second before its end.");

        _zones.Tick(10f + EmberfallDuration);
        Assert.That(_zones.Count, Is.Zero, "four seconds after it was cast, not after zero.");
    }

    [Test]
    public void Burn_RefusesAnImpossibleZone()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpawnBurnZone(bad, 4f, 6f)).ParamName,
                Is.EqualTo("radius"), $"radius {bad}");
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpawnBurnZone(4f, bad, 6f)).ParamName,
                Is.EqualTo("duration"), $"duration {bad}");
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpawnBurnZone(4f, 4f, bad)).ParamName,
                Is.EqualTo("damagePerPulse"), $"damage {bad}");
        }

        // One interval past MaxPulses: a hang, not a long fire.
        float tooLong = (ZoneSystem.MaxPulses + 1) * Interval;

        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpawnBurnZone(4f, tooLong, 6f)).ParamName,
            Is.EqualTo("duration"));

        Assert.DoesNotThrow(() => _ = new SpawnBurnZone(4f, ZoneSystem.MaxPulses * Interval, 6f), "exactly at the cap.");
    }

    [Test]
    public void Burn_RemoveDoesNothing()
    {
        SpawnBurnZone effect = Emberfall();

        _handler.Apply(effect, this);
        _handler.Remove(effect, this);

        Assert.That(_zones.Count, Is.EqualTo(1), "a zone owns its own life (ZoneSystem rule 9).");

        _zones.Tick(EmberfallDuration);

        Assert.That(_zones.Count, Is.Zero, "and it ends on its own clock.");
    }

    [Test]
    public void Burn_RecastingPlacesASecond()
    {
        SpawnBurnZone effect = Emberfall();

        _handler.Apply(effect, this);
        _clock.Now = 1f;
        _handler.Apply(effect, this);

        Assert.That(_zones.Count, Is.EqualTo(2), "two zones, not one refreshed.");
        Assert.That(_events.Count<ZoneSpawned>(), Is.EqualTo(2));

        _zones.Tick(EmberfallDuration);

        Assert.That(_zones.Count, Is.EqualTo(1), "the first retired on its own clock; the second stands.");
    }

    [Test]
    public void Burn_Guards()
    {
        Assert.That(
            Assert.Throws<ArgumentNullException>(() => _ = new SpawnBurnZoneHandler(null, _clock)).ParamName,
            Is.EqualTo("zones"));
        Assert.That(
            Assert.Throws<ArgumentNullException>(() => _ = new SpawnBurnZoneHandler(_zones, null)).ParamName,
            Is.EqualTo("clock"));

        Assert.Throws<ArgumentNullException>(() => _handler.Apply(null, this));
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(Emberfall(), null));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(null, this));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(Emberfall(), null));

        Assert.That(_zones.Count, Is.Zero, "no guard placed anything.");
    }

    [Test]
    public void Burn_OnASystemThatCannotBurnIsRefused()
    {
        // ZoneSystem's own door (M6-07b rule 2), reached through the handler: RunSession wires the
        // pair for every run, so this is a fixture's mistake and it says so rather than burning nobody.
        var heals = new ZoneSystem(_player.Health, _player.Blackboard, _events);
        var handler = new SpawnBurnZoneHandler(heals, _clock);

        Assert.Throws<InvalidOperationException>(() => handler.Apply(Emberfall(), this));
        Assert.That(heals.Count, Is.Zero);
    }

    [Test]
    public void Burn_TheRegistryDispatchesIt()
    {
        var registry = new EffectRegistry();

        Assert.That(registry.CanApply(Emberfall()), Is.False, "the premise: nothing registered.");

        registry.Register<SpawnBurnZone>(_handler);

        Assert.That(registry.CanApply(Emberfall()), Is.True);

        registry.Apply(Emberfall(), this);

        Assert.That(_zones.Count, Is.EqualTo(1));
        Assert.That(_zones.SideAt(0), Is.EqualTo(ZoneSide.BurnsEnemies));
    }

    // ---- Rule 12: the budget ----------------------------------------------------------------------

    [Test]
    public void Burn_AllocatesNothing()
    {
        const float Tough = 10_000_000f;
        const int Iterations = 100_000;

        var silent = new SilentEvents();
        EnemySystem enemies = Enemies(silent, Tough);
        var player = new PlayerCombat(Emberwright(), silent, new RecordingIntents(), EnemyCapacity);
        var zones = new ZoneSystem(player.Health, player.Blackboard, silent, enemies, player);
        var clock = new SimulatedClock();
        var handler = new SpawnBurnZoneHandler(zones, clock);
        SpawnBurnZone effect = Emberfall();
        var source = new object();

        EnemyAgent body = enemies.Spawn(new ContentId(HuskId), new Vector3(1f, 0f, 0f));
        var casts = 0;
        var pulses = 0L;

        // Every iteration casts if there is room and lands one pulse on every standing zone: an
        // Emberfall lives eight intervals, so the table sits full and every tick is eight pulses.
        AllocationAssert.None(
            () =>
            {
                if (zones.Count < ZoneSystem.Capacity)
                {
                    handler.Apply(effect, source);
                    casts++;
                }

                pulses += zones.Count;
                clock.Now += Interval;
                zones.Tick(clock.Now);
            },
            iterations: Iterations);

        Assert.That(casts, Is.GreaterThan(Iterations / 2), "the probe cast, not only ticked.");
        Assert.That(
            body.Health.Current,
            Is.LessThan(Tough - (pulses * EmberfallDamage * 0.9f)),
            "The probe is live: the body took the pulses the zones counted.");
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    private static SpawnBurnZone Emberfall() =>
        new SpawnBurnZone(EmberfallRadius, EmberfallDuration, EmberfallDamage);

    private EnemyAgent Husk(float x, float z) => _enemies.Spawn(new ContentId(HuskId), new Vector3(x, 0f, z));

    private static EnemySystem Enemies(IDomainEvents events, float huskHp = HuskMaxHp) => new(
        new ContentCatalog(new[] { Emberwright() }, new[] { HuskSpec(huskHp) }, new[] { Descent() }),
        events,
        new FixedRandom(Seed),
        new DepthScaling(Scalings.Design()),
        EnemyCapacity);

    private static CharacterSpec Emberwright() => new(
        new ContentId(EmberwrightId),
        new LocKey("character.emberwright.name"),
        new LocKey("character.emberwright.description"),
        PlayerMaxHp,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 3f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f, 0f, 3f, 3f, 4f),
        null,
        0.5f);

    private static EnemySpec HuskSpec(float maxHp) => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp,
        2f,
        1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());
}
