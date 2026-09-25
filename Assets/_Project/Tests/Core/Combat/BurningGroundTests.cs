using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// CH §3.3's fire pool: the burn, the walk, the refusals, and the heal left exactly as it was.
/// M6-07b rules 1–8, 10 and 11.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three halves.</b> The first drives a <see cref="ZoneSystem"/> directly, because the burn is
/// one branch in its pulse and the rest of the class is the heal's. The second blinks a
/// <see cref="PlayerCombat"/> the way a run does and asks where the pool went and what it was
/// built from. The third is the spec and the wiring.
/// </para>
/// <para>
/// <b>The Blink's numbers are written out</b> — 10 m in 0.05 s, a 2.0 s cooldown, a 3 m pool that
/// burns for 3 s at 4 a pulse — because <c>Soulvail.Tests.Core</c> cannot open
/// <c>Emberwright.asset</c> (M0-10). <c>EmberwrightTests.Emberwright_CarriesThePoolNumbers</c> is
/// the row in the assembly that can, and the two meet at the number. <c>Pool_IsWorthAboutOneOrb</c>
/// and <c>View_IsNotEdited</c> live there for the same reason.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BurningGroundTests
{
    private const string EmberwrightId = "character.emberwright";
    private const string HuskId = "enemy.husk";
    private const string BloaterId = "enemy.bloater";
    private const string DescentId = "mode.descent";
    private const int Seed = 7;

    // M6-07b rule 5's pool.
    private const float PoolRadius = 3f;
    private const float PoolDuration = 3f;
    private const float PoolDamage = 4f;
    private const float Interval = MovementSkillSpec.PoolPulseInterval;

    // CC §6.4's Consecrate, which M3-11b pinned and this task must not move.
    private const float ConsecrateRadius = 3.5f;
    private const float ConsecrateDuration = 6f;
    private const float ConsecrateHeal = 3f;
    private const float ConsecrateWhole = 36f;
    private const float OathboundMaxHp = 140f;

    private const float PlayerMaxHp = 70f;
    private const float HuskMaxHp = 36f;
    private const float BloaterContactDamage = 10f;
    private const float BlastRadius = 3f;

    private const float Frame = 1f / 60f;
    private const int EnemyCapacity = 32;
    private const int SessionCapacity = 8;

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = Enemies(_events, EnemyCapacity);
    }

    // ---- Rule 8: the heal path is byte-identical ---------------------------------------------------

    [Test]
    public void Zone_TheHealPathIsUnchanged()
    {
        PlayerCombat player = Player(Blink());

        // Both constructions a heal can meet: the one every fixture before this task built, and the
        // one RunSession builds now. The numbers are M3-11b's either way.
        Health bare = Unshielded();

        ZoneSystem[] systems =
        {
            new ZoneSystem(bare, new CombatBlackboard(), _events),
            new ZoneSystem(player.Health, player.Blackboard, _events, _enemies, player),
        };

        Health[] healths = { bare, player.Health };

        for (int s = 0; s < systems.Length; s++)
        {
            ZoneSystem zones = systems[s];
            Health health = healths[s];

            health.ApplyDamage(40f, 0f);
            float hurt = health.Current;

            _events.Clear();

            zones.Spawn(ConsecrateRadius, ConsecrateDuration, ConsecrateHeal, 0.5f, 0f, new object());

            Assert.That(zones.SideAt(0), Is.EqualTo(ZoneSide.HealsThePlayer), "the default side.");

            for (int pulse = 1; pulse <= 12; pulse++)
            {
                zones.Tick(pulse * 0.5f);
            }

            Assert.That(_events.Count<ZoneHealed>(), Is.EqualTo(12), $"system {s}: twelve pulses.");
            Assert.That(health.Current, Is.EqualTo(hurt + ConsecrateWhole).Within(Tolerance), $"system {s}: 36.");
            Assert.That(_events.Count<ZoneExpired>(), Is.EqualTo(1), $"system {s}: retired on its last pulse.");
            Assert.That(zones.Count, Is.Zero);
            Assert.That(_events.Count<ZoneBurned>(), Is.Zero, "a heal burns nobody.");
            Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        }
    }

    // ---- Rule 2: what it burns with ------------------------------------------------------------------

    [Test]
    public void Zone_ASideIsRequiredToBeBuiltFor()
    {
        var zones = new ZoneSystem(Unshielded(), new CombatBlackboard(), _events);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => zones.Spawn(PoolRadius, PoolDuration, PoolDamage, Interval, 0f, new object(), ZoneSide.BurnsEnemies));

        StringAssert.Contains("EnemySystem", thrown.Message, "the message names the wiring.");
        Assert.That(zones.Count, Is.Zero, "and no zone was placed.");
        Assert.That(_events.Count<ZoneSpawned>(), Is.Zero);
    }

    [Test]
    public void Zone_TheConstructorRefusesHalfAPair()
    {
        PlayerCombat player = Player(Blink());

        var noCombat = Assert.Throws<ArgumentException>(
            () => _ = new ZoneSystem(player.Health, player.Blackboard, _events, _enemies, null));

        var noEnemies = Assert.Throws<ArgumentException>(
            () => _ = new ZoneSystem(player.Health, player.Blackboard, _events, null, player));

        Assert.That(noCombat.ParamName, Is.EqualTo("combat"));
        Assert.That(noEnemies.ParamName, Is.EqualTo("enemies"));
    }

    // ---- Rules 6 and 7: the burn ---------------------------------------------------------------------

    [Test]
    public void Burn_TakesHitPointsOffWhatIsStandingInIt()
    {
        (PlayerCombat _, ZoneSystem zones) = Burning();

        EnemyAgent a = Husk(1f, 0f);
        EnemyAgent b = Husk(-1f, 1f);
        EnemyAgent outside = Husk(5f, 0f);

        int id = BurnAtOrigin(zones);

        _events.Clear();
        zones.Tick(Interval);

        IReadOnlyList<EnemyDamaged> damaged = _events.Of<EnemyDamaged>();

        Assert.That(damaged.Count, Is.EqualTo(2));
        Assert.That(damaged[0].Amount, Is.EqualTo(PoolDamage).Within(Tolerance));
        Assert.That(damaged[1].Amount, Is.EqualTo(PoolDamage).Within(Tolerance));

        IReadOnlyList<ZoneBurned> burned = _events.Of<ZoneBurned>();

        Assert.That(burned.Count, Is.EqualTo(2), "one per enemy damaged.");
        Assert.That(new[] { burned[0].EnemyId, burned[1].EnemyId }, Is.EquivalentTo(new[] { a.Id, b.Id }));
        Assert.That(burned[0].Id, Is.EqualTo(id));
        Assert.That(burned[0].Amount, Is.EqualTo(PoolDamage).Within(Tolerance));

        Assert.That(outside.Health.Current, Is.EqualTo(HuskMaxHp).Within(Tolerance), "the third is untouched.");

        // After the damage: each ZoneBurned follows its own EnemyDamaged.
        IReadOnlyList<object> all = _events.All;
        int firstDamaged = IndexOf<EnemyDamaged>(all);
        int firstBurned = IndexOf<ZoneBurned>(all);

        Assert.That(firstDamaged, Is.LessThan(firstBurned));
    }

    [Test]
    public void Burn_SkipsACorpse()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        EnemyAgent husk = Husk(1f, 0f);

        BurnAtOrigin(zones);

        _enemies.ApplyDamage(husk.Id, 1_000f, 0.1f, player);

        Assert.That(husk.IsAlive, Is.False, "the premise: dead.");
        Assert.That(_enemies.Registry.TryGet(husk.Id, out _), Is.True, "…and still registered.");

        _events.Clear();
        zones.Tick(Interval);

        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        Assert.That(_events.Count<ZoneBurned>(), Is.Zero, "registered is not breathing.");
    }

    [Test]
    public void Burn_ReachingNobodyIsSilent()
    {
        (PlayerCombat _, ZoneSystem zones) = Burning();

        BurnAtOrigin(zones);

        _events.Clear();

        for (int pulse = 1; pulse <= 5; pulse++)
        {
            zones.Tick(pulse * Interval);
        }

        Assert.That(_events.All.Count, Is.Zero, "five pulses on nobody: no event at all.");

        zones.Tick(PoolDuration);

        Assert.That(_events.All.Count, Is.EqualTo(1), "the sixth is silent too; its retirement is not.");
        Assert.That(_events.Count<ZoneExpired>(), Is.EqualTo(1));
    }

    [Test]
    public void Burn_AHealPulseOnAFullPlayerStillPublishes()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        int id = zones.Spawn(ConsecrateRadius, ConsecrateDuration, ConsecrateHeal, 0.5f, 0f, new object());

        Assert.That(player.Health.Current, Is.EqualTo(PlayerMaxHp), "the premise: full.");

        _events.Clear();
        zones.Tick(0.5f);

        ZoneHealed healed = _events.Single<ZoneHealed>();

        Assert.That(healed.Id, Is.EqualTo(id));
        Assert.That(healed.Amount, Is.Zero, "rule 7's other half: a heal's zero is an answer.");
    }

    [Test]
    public void Burn_SetsOffABloater()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        EnemyAgent bloater = _enemies.Spawn(new ContentId(BloaterId), new Vector3(1f, 0f, 0f));

        BurnAtOrigin(zones);

        _events.Clear();
        zones.Tick(Interval);

        Assert.That(bloater.IsAlive, Is.False, "the premise: one pulse kills it.");
        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1), "a kill is a kill however it died.");

        Assert.That(
            player.Health.Current,
            Is.EqualTo(PlayerMaxHp - BloaterContactDamage).Within(Tolerance),
            "The Emberwright standing in their own fire takes the blast: EnemySystem.ApplyDamage's "
                + "fourth argument doing its job.");
    }

    [Test]
    public void Burn_DoesNotTouchThePlayer()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        BurnAtOrigin(zones);

        for (int pulse = 1; pulse <= 6; pulse++)
        {
            zones.Tick(pulse * Interval);
        }

        Assert.That(player.Health.Current, Is.EqualTo(PlayerMaxHp), "rule 1: a burn acts on one side.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Burn_CatchesUpRatherThanSkipping()
    {
        (PlayerCombat _, ZoneSystem zones) = Burning();

        EnemyAgent husk = Husk(0f, 1f);

        BurnAtOrigin(zones);

        _events.Clear();
        zones.Tick(1.6f);

        Assert.That(_events.Count<ZoneBurned>(), Is.EqualTo(3), "0.5, 1.0 and 1.5 — not one.");
        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp - (3 * PoolDamage)).Within(Tolerance));
    }

    [Test]
    public void Burn_PulsesOnTheTickItExpires()
    {
        (PlayerCombat _, ZoneSystem zones) = Burning();

        Husk(0f, 1f);

        BurnAtOrigin(zones);

        _events.Clear();
        zones.Tick(PoolDuration);

        Assert.That(_events.Count<ZoneBurned>(), Is.EqualTo(6), "the sixth lands on the expiry second.");
        Assert.That(zones.Count, Is.Zero, "and then it retires.");

        IReadOnlyList<object> all = _events.All;

        Assert.That(all[all.Count - 1], Is.InstanceOf<ZoneExpired>(), "the retirement is the last thing said.");
    }

    // ---- Rule 3: the drop ----------------------------------------------------------------------------

    [Test]
    public void Blink_LeavesAPoolWhereItLeft()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        var snapshot = Snapshot(new Vector3(5f, 0f, 5f));

        BlinkAt(player, zones, snapshot, Frame);

        ZoneSpawned pool = _events.Single<ZoneSpawned>();

        Assert.That(pool.Position, Is.EqualTo(new Vector3(5f, 0f, 5f)), "where the blink started.");
        Assert.That(pool.Position, Is.Not.EqualTo(new Vector3(5f, 0f, 15f)), "not the destination.");
        Assert.That(pool.Radius, Is.EqualTo(PoolRadius).Within(Tolerance));
        Assert.That(pool.Duration, Is.EqualTo(PoolDuration).Within(Tolerance));
        Assert.That(zones.SideAt(0), Is.EqualTo(ZoneSide.BurnsEnemies));
    }

    [Test]
    public void Blink_UsesThisFramesPositionNotTheBlackboards()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        // One frame standing at the origin, so the blackboard says so.
        var snapshot = Snapshot(Vector3.Zero);

        player.Tick(Frame, Frame, snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ, null, null, zones);

        Assert.That(player.Blackboard.PlayerPosition, Is.EqualTo(Vector3.Zero), "the premise.");

        // Then two metres on, and a blink on the same tick.
        snapshot.PlayerPosition = new Vector3(2f, 0f, 0f);

        BlinkAt(player, zones, snapshot, 2f * Frame);

        Assert.That(
            _events.Single<ZoneSpawned>().Position,
            Is.EqualTo(new Vector3(2f, 0f, 0f)),
            "The snapshot's position, not last frame's blackboard — invisible when it works.");
    }

    [Test]
    public void Blink_StillDodges()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        BlinkAt(player, zones, Snapshot(Vector3.Zero), Frame);

        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1));
        Assert.That(_intents.LastCharge.Distance, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(_intents.LastCharge.Duration, Is.EqualTo(0.05f).Within(Tolerance));
        Assert.That(player.Health.IsInvulnerable, Is.True, "the Charge's i-frames.");
    }

    [Test]
    public void Blink_DropsNoDecoy()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();
        var lures = new LureSystem(_events);

        player.Charge.Request(0f);
        player.Tick(Frame, Frame, Snapshot(Vector3.Zero), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ, lures, null, zones);

        Assert.That(lures.Count, Is.Zero, "MovementSkillSpec.Decoy's line, unmoved.");
        Assert.That(_events.Count<DecoySpawned>(), Is.Zero);
        Assert.That(zones.Count, Is.EqualTo(1), "the premise: the fire did land.");
    }

    [Test]
    public void Charge_AndShroudstepLeaveNoPool()
    {
        foreach (MovementSkillSpec dash in new[] { Charge(), Shroudstep() })
        {
            _events.Clear();

            PlayerCombat player = Player(dash);
            var zones = new ZoneSystem(player.Health, player.Blackboard, _events, _enemies, player);
            var lures = new LureSystem(_events);

            player.Charge.Request(0f);
            player.Tick(Frame, Frame, Snapshot(Vector3.Zero), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ, lures, null, zones);

            Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1), $"{dash.Kind}: the premise.");
            Assert.That(zones.Count, Is.Zero, $"{dash.Kind} leaves no pool.");
            Assert.That(_events.Count<ZoneSpawned>(), Is.Zero);
        }
    }

    [Test]
    public void Blink_TwoPoolsOverlapLegitimately()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();

        EnemyAgent husk = Husk(0f, 1f);
        var snapshot = Snapshot(Vector3.Zero);

        BlinkAt(player, zones, snapshot, 0.01f);
        int first = _events.Single<ZoneSpawned>().Id;

        zones.Tick(2f);

        // Two seconds on, the cooldown is spent: a second blink from the same spot.
        _events.Clear();
        BlinkAt(player, zones, snapshot, 2.05f);
        int second = _events.Single<ZoneSpawned>().Id;

        zones.Tick(2.6f);

        Assert.That(zones.Count, Is.EqualTo(2), "3 s of fire against a 2 s cooldown: they overlap.");

        var sources = new HashSet<int>();

        foreach (ZoneBurned burn in _events.Of<ZoneBurned>())
        {
            Assert.That(burn.EnemyId, Is.EqualTo(husk.Id));
            sources.Add(burn.Id);
        }

        Assert.That(sources, Is.EquivalentTo(new[] { first, second }), "an enemy in both takes both.");

        zones.Tick(3.5f);

        Assert.That(zones.Count, Is.EqualTo(1), "and the first is gone a second later.");
    }

    // ---- Rule 4: the spec ----------------------------------------------------------------------------

    [Test]
    public void Spec_ThePoolNumbersAreKindConditional()
    {
        var onACharge = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f, 0f, poolRadius: 3f));

        Assert.That(onACharge.ParamName, Is.EqualTo("poolRadius"), "a Charge with a pool radius is a forgotten field.");

        var zeroDuration = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f, 0f, 3f, 0f, 4f));

        Assert.That(zeroDuration.ParamName, Is.EqualTo("poolDuration"), "a Blink without one is a teleport.");

        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f, 0f, 3f, 3f, bad),
                $"damage {bad}");

            Assert.That(thrown.ParamName, Is.EqualTo("poolDamagePerPulse"));
        }

        var onAShroudstep = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new MovementSkillSpec(MovementSkillKind.Shroudstep, 6f, 0.05f, 2.5f, 0.15f, 0f, 0f, 0.05f, 3f, 0f, 0f, 4f));

        Assert.That(onAShroudstep.ParamName, Is.EqualTo("poolDamagePerPulse"));
    }

    [Test]
    public void Spec_TheShippedTwoAreUnchanged()
    {
        // The nine-argument constructor, as every site before this task calls it.
        foreach (MovementSkillSpec spec in new[] { Charge(), Shroudstep() })
        {
            Assert.That(spec.PoolRadius, Is.Zero, $"{spec.Kind}");
            Assert.That(spec.PoolDuration, Is.Zero, $"{spec.Kind}");
            Assert.That(spec.PoolDamagePerPulse, Is.Zero, $"{spec.Kind}");
        }

        MovementSkillSpec charge = Charge();

        Assert.That(charge.Distance, Is.EqualTo(10f));
        Assert.That(charge.Duration, Is.EqualTo(0.22f));
        Assert.That(charge.Cooldown, Is.EqualTo(2.5f));
        Assert.That(charge.Damage, Is.EqualTo(20f));
        Assert.That(charge.Knockback, Is.EqualTo(5f));
        Assert.That(Shroudstep().DecoyDuration, Is.EqualTo(3f), "and the decoy is where it was.");
    }

    // ---- Rule 11: three Stats ------------------------------------------------------------------------

    [Test]
    public void Pool_TheThreeNumbersAreStats()
    {
        var skill = new ChargeSkill(Blink());

        foreach ((string name, float seeded) in new[]
        {
            ("PoolRadius", PoolRadius),
            ("PoolDuration", PoolDuration),
            ("PoolDamagePerPulse", PoolDamage),
        })
        {
            PropertyInfo property = typeof(ChargeSkill).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null, name);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(Stat)), $"{name} is a Stat (ADR-0008).");
            Assert.That(((Stat)property.GetValue(skill)).Value, Is.EqualTo(seeded).Within(Tolerance), $"{name} is seeded from the spec.");
        }

        // M3-12a's sequence, kept: the address arrived with the node that names it. M6-08's Ash
        // branch names two of the three, and the third stays unaddressable until a node widens a
        // pool (M7-04). This row said "none yet" until M6-08 and now says which.
        var pool = new List<string>();

        foreach (string member in Enum.GetNames(typeof(PlayerStat)))
        {
            if (member.Contains("Pool"))
            {
                pool.Add(member);
            }
        }

        Assert.That(pool, Is.EquivalentTo(new[] { nameof(PlayerStat.PoolDamage), nameof(PlayerStat.PoolDuration) }),
            "Scorching Ground and Lingering Ash name these two; no node widens a pool, so no PoolRadius.");
    }

    [Test]
    public void Pool_AStatDrivenToZeroLeavesNoPool()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();
        var source = new object();
        var snapshot = Snapshot(Vector3.Zero);

        player.Charge.PoolRadius.Add(new Modifier(ModifierKind.Flat, -3f, source));

        Assert.That(player.Charge.PoolRadius.Value, Is.Zero, "the premise: zero.");
        Assert.DoesNotThrow(() => BlinkAt(player, zones, snapshot, Frame));

        player.Charge.PoolRadius.RemoveAll(source);
        player.Charge.PoolRadius.Add(new Modifier(ModifierKind.Flat, -5f, source));

        Assert.That(player.Charge.PoolRadius.Value, Is.EqualTo(-2f).Within(Tolerance), "the premise: below zero.");
        Assert.DoesNotThrow(() => BlinkAt(player, zones, snapshot, 2.1f));

        Assert.That(zones.Count, Is.Zero, "no pool at all, twice.");
        Assert.That(_events.Count<ZoneSpawned>(), Is.Zero);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(2), "and the dash is otherwise unchanged.");
    }

    [Test]
    public void Pool_ReadsTheStatAtTheDrop()
    {
        (PlayerCombat player, ZoneSystem zones) = Burning();
        var snapshot = Snapshot(Vector3.Zero);

        BlinkAt(player, zones, snapshot, 0.01f);
        ZoneSpawned first = _events.Single<ZoneSpawned>();

        player.Charge.PoolDuration.Add(new Modifier(ModifierKind.PercentAdd, 0.5f, new object()));

        _events.Clear();
        BlinkAt(player, zones, snapshot, 2.05f);
        ZoneSpawned second = _events.Single<ZoneSpawned>();

        Assert.That(first.Duration, Is.EqualTo(3f).Within(Tolerance));
        Assert.That(second.Duration, Is.EqualTo(4.5f).Within(Tolerance), "read at the drop, +50 %.");

        _events.Clear();
        zones.Tick(3.02f);

        Assert.That(_events.Single<ZoneExpired>().Id, Is.EqualTo(first.Id), "the first is unaffected: it ends at 3 s.");
        Assert.That(zones.Count, Is.EqualTo(1));
    }

    // ---- Rule 2: the run wires it --------------------------------------------------------------------

    [Test]
    public void Run_TheZoneSystemIsWiredToBurn()
    {
        var events = new RecordingEvents();
        var session = new RunSession(
            new ContentCatalog(new[] { Emberwright() }, new[] { HuskSpec(), BloaterSpec() }, new[] { Descent() }),
            new FixedRandom(Seed),
            events,
            new RecordingIntents(),
            new RunRecorder(new FixedRandom(Seed), new FixedClock(default), events),
            SessionCapacity,
            SessionCapacity,
            SessionCapacity);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(EmberwrightId),
            Seed,
            1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
            restore: null));

        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        object zones = typeof(RunState).GetProperty("Zones", Hidden).GetValue(session.State);
        object enemies = typeof(RunState).GetProperty("Enemies", Hidden).GetValue(session.State);
        object combat = typeof(RunState).GetProperty("Combat", Hidden).GetValue(session.State);

        Assert.That(typeof(ZoneSystem).GetField("_enemies", Hidden).GetValue(zones), Is.SameAs(enemies),
            "RunSession wires the run's EnemySystem into the zones.");
        Assert.That(typeof(ZoneSystem).GetField("_combat", Hidden).GetValue(zones), Is.SameAs(combat),
            "…and its PlayerCombat, so a burn that sets off a Bloater knows whom to catch.");
    }

    // ---- Rule 10: the budget -------------------------------------------------------------------------

    [Test]
    public void Burn_AllocatesNothing()
    {
        const int Bodies = 28;
        const float Tough = 1_000_000f;
        const int Ticks = 12_500;

        var silent = new SilentEvents();
        EnemySystem enemies = Enemies(silent, EnemyCapacity, huskHp: Tough);
        var player = new PlayerCombat(Emberwright(), silent, new RecordingIntents(), EnemyCapacity);
        var zones = new ZoneSystem(player.Health, player.Blackboard, silent, enemies, player);
        var source = new object();

        var bodies = new EnemyAgent[Bodies];

        for (int i = 0; i < Bodies; i++)
        {
            bodies[i] = enemies.Spawn(new ContentId(HuskId), new Vector3((i % 7) * 0.3f, 0f, (i / 7) * 0.3f));
        }

        // A full table standing on all twenty-eight, refilled the tick the last pulse retires it, so
        // every tick lands eight pulses: 12 500 ticks — one warm-up plus 12 499 measured — is 100 000.
        for (int z = 0; z < ZoneSystem.Capacity; z++)
        {
            zones.Spawn(PoolRadius, 50f, PoolDamage, Interval, 0f, source, ZoneSide.BurnsEnemies, Vector3.Zero);
        }

        float now = 0f;

        AllocationAssert.None(
            () =>
            {
                now += Interval;
                zones.Tick(now);

                if (zones.Count == 0)
                {
                    for (int z = 0; z < ZoneSystem.Capacity; z++)
                    {
                        zones.Spawn(PoolRadius, 50f, PoolDamage, Interval, now, source, ZoneSide.BurnsEnemies, Vector3.Zero);
                    }
                }
            },
            iterations: Ticks - 1);

        Assert.That(
            bodies[0].Health.Current,
            Is.EqualTo(Tough - (100_000f * PoolDamage)).Within(1f),
            "The probe is live: every body took all 100 000 pulses.");
    }

    // ---- Fixture -------------------------------------------------------------------------------------

    private (PlayerCombat, ZoneSystem) Burning()
    {
        PlayerCombat player = Player(Blink());

        return (player, new ZoneSystem(player.Health, player.Blackboard, _events, _enemies, player));
    }

    private int BurnAtOrigin(ZoneSystem zones) =>
        zones.Spawn(PoolRadius, PoolDuration, PoolDamage, Interval, 0f, new object(), ZoneSide.BurnsEnemies, Vector3.Zero);

    private EnemyAgent Husk(float x, float z) => _enemies.Spawn(new ContentId(HuskId), new Vector3(x, 0f, z));

    /// <summary>A press and the tick that spends it, with the zones handed down as RunSession does.</summary>
    private static void BlinkAt(PlayerCombat player, ZoneSystem zones, WorldSnapshot snapshot, float now)
    {
        player.Charge.Request(now);
        player.Tick(Frame, now, snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ, null, null, zones);
    }

    private static WorldSnapshot Snapshot(Vector3 at) => new(SessionCapacity) { Dt = Frame, PlayerPosition = at };

    private static int IndexOf<T>(IReadOnlyList<object> all)
    {
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    private PlayerCombat Player(MovementSkillSpec dash) =>
        new(Emberwright(dash), _events, _intents, EnemyCapacity);

    private static Health Unshielded() => new(new Stat(OathboundMaxHp), null, 0f);

    private static EnemySystem Enemies(IDomainEvents events, int capacity, float huskHp = HuskMaxHp) => new(
        new ContentCatalog(new[] { Emberwright() }, new[] { HuskSpec(huskHp), BloaterSpec() }, new[] { Descent() }),
        events,
        new FixedRandom(Seed),
        new DepthScaling(Scalings.Design()),
        capacity);

    /// <summary>CH §3.3's Blink with M6-07b rule 5's pool.</summary>
    private static MovementSkillSpec Blink() =>
        new(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f, 0f, PoolRadius, PoolDuration, PoolDamage);

    /// <summary>CC §7's Charge, as the nine-argument constructor has always built it.</summary>
    private static MovementSkillSpec Charge() =>
        new(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f);

    /// <summary>CH §3.2's Shroudstep.</summary>
    private static MovementSkillSpec Shroudstep() =>
        new(MovementSkillKind.Shroudstep, 6f, 0.05f, 2.5f, 0.15f, 0f, 0f, 0.05f, 3f);

    /// <summary>
    /// The Emberwright as M6-07a authors it, without Kindling — nothing here is about the ramp, and
    /// <c>KindlingTests.Kindling_AZonePulseIsNotAStack</c> is where the pool meets it.
    /// </summary>
    private static CharacterSpec Emberwright(MovementSkillSpec dash = null) => new(
        new ContentId(EmberwrightId),
        new LocKey("character.emberwright.name"),
        new LocKey("character.emberwright.description"),
        PlayerMaxHp,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 3f),
        new FocusSpec(0.4f, 1f, 1f),
        dash ?? Blink(),
        null,
        0.5f);

    private static EnemySpec HuskSpec(float maxHp = HuskMaxHp) => new(
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

    /// <summary>GD §8.1's Bloater with four hit points, so one pulse sets it off.</summary>
    private static EnemySpec BloaterSpec() => new(
        new ContentId(BloaterId),
        new LocKey("enemy.bloater.name"),
        PoolDamage,
        moveSpeed: 2f,
        targetPriority: 2,
        threatCost: 8,
        xpValue: 24f,
        isElite: false,
        contactDamage: BloaterContactDamage,
        reach: 1.2f,
        windupTime: 1f,
        recoverTime: 0f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Bloater,
        explosion: new ExplosionSpec(BlastRadius));

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
