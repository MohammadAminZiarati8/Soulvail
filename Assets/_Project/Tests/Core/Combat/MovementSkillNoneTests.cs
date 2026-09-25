using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// RS-03a rules 7 and 8: a class with no movement skill, and a dash that deals nothing. Rule 9, the
/// hidden button, is <c>SkillButtonTests</c>' row, since it is a Unity component.
/// </summary>
/// <remarks>
/// <b>The owner's switch for playing the Ranger without its roll.</b> <see cref="MovementSkillKind.None"/>
/// is refused at the press, so nothing downstream of <c>ChargeSkill.Request</c> ever sees a dash —
/// the rows check the three places one would show: the event, the intent, and the motor's
/// suspension. The press goes through a real run, so the command port is covered too; the
/// skill's own <see cref="ChargeSkill.IsActive"/> is read off a hand-built
/// <see cref="PlayerCombat"/>, since <c>RunState.Combat</c> is internal.
/// </remarks>
[TestFixture]
public sealed class MovementSkillNoneTests
{
    private const string RollerId = "character.roller";
    private const string HuskId = "enemy.husk";
    private const string DescentId = "mode.descent";
    private const int Seed = 5;

    private const float HuskMaxHp = 36f;

    private const float Frame = 1f / 120f;

    /// <summary>Two seconds of ticks: most of a roll's cooldown, and many times its duration.</summary>
    private const int TwoSeconds = 240;

    private const int EnemyCapacity = 8;
    private const int DeviceCap = 8;
    private const int ProjectileCapacity = 8;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private WorldSnapshot _snapshot;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
    }

    // ---- Rule 7: None does nothing, and is validated like any other kind ------------------------

    [Test]
    public void None_APressDoesNothing()
    {
        RunSession session = Session(MovementSkillKind.None);

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(RollerId), Seed, 1, SpawnPlan.Empty, restore: null));

        for (int i = 0; i < TwoSeconds; i++)
        {
            // Pressed every quarter second, so a press that was merely buffered would have fired.
            if (i % 30 == 0)
            {
                session.MovementSkill();
            }

            session.Tick(_snapshot);
        }

        Assert.That(_events.Count<ChargeStarted>(), Is.Zero, "no dash is announced.");
        Assert.That(_events.Count<ChargeEnded>(), Is.Zero);
        Assert.That(_intents.Charges, Is.Empty, "no dash is sent to the body.");
        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(TwoSeconds),
            "the motor was never suspended: one move intent every tick.");
        Assert.That(session.State.MovementSkillCooldownFraction, Is.Zero, "no cooldown runs.");

        // The skill itself, which a run keeps behind an internal handle.
        var combat = new PlayerCombat(Roller(MovementSkillKind.None), _events, _intents, EnemyCapacity);
        var registry = new EnemyRegistry(EnemyCapacity);

        combat.Charge.Request(0f);

        for (int i = 1; i <= TwoSeconds; i++)
        {
            combat.Tick(Frame, i * Frame, _snapshot, registry.Alive, Vector3.UnitZ);

            Assert.That(combat.Charge.IsActive, Is.False, $"tick {i}.");
        }
    }

    [Test]
    public void None_ValidatesAsAnyOtherKind()
    {
        MovementSkillSpec none = null;

        Assert.That(
            () => none = new MovementSkillSpec(MovementSkillKind.None, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
            Throws.Nothing);
        Assert.That(none.Kind, Is.EqualTo(MovementSkillKind.None));

        // The three numbers the authoring asset always carries are still checked, though never read.
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new MovementSkillSpec(MovementSkillKind.None, 0f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f)).ParamName,
            Is.EqualTo("distance"));
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new MovementSkillSpec(MovementSkillKind.None, 6f, 0.3f, 0f, 0.15f, 0f, 0f, 0.05f)).ParamName,
            Is.EqualTo("cooldown"));

        // And a decoy or a pool on it is a forgotten field, as on a Charge.
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new MovementSkillSpec(
                    MovementSkillKind.None, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f, decoyDuration: 3f)).ParamName,
            Is.EqualTo("decoyDuration"));
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new MovementSkillSpec(
                    MovementSkillKind.None, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f, poolRadius: 3f)).ParamName,
            Is.EqualTo("poolRadius"));
    }

    // ---- Rule 8: a dash that deals nothing touches nobody ----------------------------------------

    [Test]
    public void EmptyDash_TouchesNobody()
    {
        (PlayerCombat roller, EnemySystem enemies, int[] ids) = Dashing(damage: 0f, knockback: 0f);

        roller.ResolveChargeHits(ids, 2f * Frame, enemies);

        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero, "no hit, not even a zero one.");
        Assert.That(_intents.Knockbacks, Is.Empty, "no zero-metre shove.");

        foreach (int id in ids)
        {
            Assert.That(enemies.Registry.TryGet(id, out EnemyAgent agent), Is.True);
            Assert.That(agent.Health.Current, Is.EqualTo(HuskMaxHp));
        }
    }

    [Test]
    public void EmptyDash_AnyDamageStillHits()
    {
        // Knockback alone: the shove lands, and no zero-damage hit comes with it.
        (PlayerCombat shover, EnemySystem enemies, int[] ids) = Dashing(damage: 0f, knockback: 1f);

        shover.ResolveChargeHits(ids, 2f * Frame, enemies);

        Assert.That(_intents.Knockbacks.Count, Is.EqualTo(ids.Length), "one shove each.");
        Assert.That(_intents.Knockbacks[0].Distance, Is.EqualTo(1f));
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);

        // Damage alone: the hit lands, and no zero-metre shove comes with it.
        _events.Clear();
        _intents.Clear();

        (PlayerCombat striker, EnemySystem struck, int[] struckIds) = Dashing(damage: 5f, knockback: 0f);

        striker.ResolveChargeHits(struckIds, 2f * Frame, struck);

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(struckIds.Length), "one hit each.");
        Assert.That(_intents.Knockbacks, Is.Empty);
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>
    /// A class whose roll deals <paramref name="damage"/> and shoves <paramref name="knockback"/> m,
    /// one tick into that roll, with two Husks in its path. The record is cleared, so a row asserts on
    /// the report alone.
    /// </summary>
    private (PlayerCombat Player, EnemySystem Enemies, int[] Ids) Dashing(float damage, float knockback)
    {
        var catalog = new ContentCatalog(
            new[] { Roller(MovementSkillKind.Charge) }, new[] { Husk() }, new[] { Descent() });
        var enemies = new EnemySystem(
            catalog, _events, new FixedRandom(Seed), new DepthScaling(Scalings.Design()), EnemyCapacity);

        int[] ids =
        {
            enemies.Spawn(new ContentId(HuskId), new Vector3(0f, 0f, 2f)).Id,
            enemies.Spawn(new ContentId(HuskId), new Vector3(0f, 0f, 4f)).Id,
        };

        var player = new PlayerCombat(
            Roller(MovementSkillKind.Charge, damage, knockback), _events, _intents, EnemyCapacity);

        player.Charge.Request(0f);
        player.Tick(Frame, Frame, _snapshot, enemies.Registry.Alive, Vector3.UnitZ);

        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1), "Sanity: rolling.");

        _events.Clear();
        _intents.Clear();

        return (player, enemies, ids);
    }

    private RunSession Session(MovementSkillKind kind) => new(
        new ContentCatalog(new[] { Roller(kind) }, new[] { Husk() }, new[] { Descent() }),
        new FixedRandom(Seed),
        _events,
        _intents,
        new RunRecorder(new FixedRandom(Seed), new FixedClock(default), _events),
        EnemyCapacity,
        DeviceCap,
        ProjectileCapacity);

    /// <summary>
    /// A class with the Ranger's roll — 6 m in 0.3 s, a 3 s cooldown — or with none. Damage and
    /// knockback 0 unless a row says otherwise, which is what the roll will author.
    /// </summary>
    private static CharacterSpec Roller(MovementSkillKind kind, float damage = 0f, float knockback = 0f) => new(
        new ContentId(RollerId),
        new LocKey("character.roller.name"),
        new LocKey("character.roller.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(kind, 6f, 0.3f, 3f, 0.15f, damage, knockback, 0.05f),
        null,
        0.5f);

    private static EnemySpec Husk() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
        3.5f,
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

    /// <summary>Descent with an empty roster, so a run spawns nothing it was not asked to.</summary>
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
