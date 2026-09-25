using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// RS-03a rules 1–5: a class that holds its fire while it moves. <see cref="PlayerCombat.FireWhileMoving"/>
/// is a <see cref="Stat"/>, still means stopped, a hold starts no swing and drops a draw, the
/// facing follows the run, and every change of the hold is published once.
/// </summary>
/// <remarks>
/// <para>
/// <b>A bow built in code, not the Ranger.</b> RS-03c authors the class; these rows are about the
/// machinery, so the numbers are round: two shots a second, the arrow loosed halfway through the
/// draw, a Husk that cannot die standing 8 m ahead. The Focus ramp is off, so the cadence stays two.
/// </para>
/// <para>
/// <b>Assembled by hand rather than through a run</b>, for <c>PlayerProjectileTests</c>' reason:
/// <c>RunState.Combat</c> is internal. The fixture ticks <see cref="PlayerCombat"/> in
/// <c>RunSession.Tick</c>'s order and takes each shot the tick it is offered, as the run does. The
/// body is never moved: the stick and <c>WorldSnapshot.PlayerVelocity</c> are what a row sets, and
/// they are the only two things the hold reads.
/// </para>
/// <para>Rule 6 — nothing that ships changes — is <c>CharacterDefinitionTests</c>' row, since it reads the assets.</para>
/// </remarks>
[TestFixture]
public sealed class HoldFireTests
{
    private const string BowmanId = "character.bowman";
    private const string HuskId = "enemy.husk";

    private const float ShotDamage = 10f;
    private const float ShotsPerSecond = 2f;
    private const float Interval = 1f / ShotsPerSecond;
    private const float ShotRange = 12f;
    private const float ShotSpeed = 40f;
    private const float ShotRadius = 0.5f;

    /// <summary>Halfway: the arrow leaves 0.25 s into a 0.5 s swing.</summary>
    private const float DamageFrame = 0.5f;

    private const float FullCircle = 360f;

    /// <summary>Inside the bow's reach, straight ahead of a player at the origin.</summary>
    private const float TargetRange = 8f;

    private const float Frame = 1f / 120f;

    /// <summary>Several targeting decisions and several swings: enough for anything a row waits on.</summary>
    private const int MaxFrames = 240;

    private const int EnemyCapacity = 8;

    private static readonly Vector2 Push = new(1f, 0f);

    /// <summary>Running along +X at the fixture's top speed.</summary>
    private static readonly Vector3 Running = new(3f, 0f, 0f);

    /// <summary>Letting go of the stick: centred, and the body still sliding at 0.5 m/s.</summary>
    private static readonly Vector3 Sliding = new(0.5f, 0f, 0f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private WorldSnapshot _snapshot;
    private EnemySense _husk;

    private float _now;

    /// <summary>Shots the run would have put in the air — each taken the tick it was offered.</summary>
    private int _shots;

    /// <summary>When each <see cref="PlayerAttacked"/> went out, on the fixture's clock.</summary>
    private List<float> _swingStarts;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();

        var catalog = new ContentCatalog(
            new[] { Bowman(firesWhileMoving: false) }, new[] { Husk() }, new[] { Descent() });

        _enemies = new EnemySystem(
            catalog, _events, new FixedRandom(7), new DepthScaling(Scalings.Design()), EnemyCapacity);

        EnemyAgent husk = _enemies.Spawn(new ContentId(HuskId), new Vector3(0f, 0f, TargetRange));

        _husk = new EnemySense
        {
            Id = husk.Id,
            Position = husk.Position,
            Velocity = Vector3.Zero,
            PathDirectionToPlayer = Vector2.Zero,
            HasLineOfSight = true,
        };

        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
        _now = 0f;
        _shots = 0;
        _swingStarts = new List<float>();
    }

    // ---- Rule 1: a Stat, seeded from the weapon, reachable for every class ----------------------

    [Test]
    public void FireWhileMoving_SeedsFromTheWeapon()
    {
        Assert.That(Combat(firesWhileMoving: true).FireWhileMoving.Base, Is.EqualTo(1f));
        Assert.That(Combat(firesWhileMoving: false).FireWhileMoving.Base, Is.Zero);

        Assert.That(Combat(firesWhileMoving: false).FireWhileMoving.ModifierCount, Is.Zero,
            "Seeded clean: a hold carrying a modifier from birth would be a skill nobody took.");

        // And every WeaponSpec written before RS-03a fires on the move, by default.
        var censer = new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f);

        Assert.That(censer.FiresWhileMoving, Is.True);
    }

    [Test]
    public void FireWhileMoving_ResolvesForEveryClass()
    {
        foreach (bool fires in new[] { true, false })
        {
            PlayerCombat combat = Combat(fires);
            PlayerStats stats = Stats(combat);

            Assert.That(stats.Resolve(PlayerStat.FireWhileMoving), Is.SameAs(combat.FireWhileMoving),
                $"firesWhileMoving {fires}: the very stat, never a copy.");
            Assert.That(stats.Has(PlayerStat.FireWhileMoving), Is.True, $"firesWhileMoving {fires}.");
        }
    }

    // ---- Rules 2 and 3: still means stopped, and a hold starts nothing -------------------------

    [Test]
    public void Hold_NoSwingStartsOnTheMove()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        Run(bowman, MaxFrames, Push, Running);

        Assert.That(bowman.IsHoldingFire, Is.True);
        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero, "no swing starts on the move.");
        Assert.That(_shots, Is.Zero, "and no arrow leaves.");

        // The premise: the same bow on a class that fires on the move does swing here, so the
        // silence above is the hold and not a target out of reach.
        PlayerCombat control = Combat(firesWhileMoving: true);

        Run(control, MaxFrames, Push, Running);

        Assert.That(_events.Count<PlayerAttacked>(), Is.GreaterThan(0));
        Assert.That(_shots, Is.GreaterThan(0));
    }

    [Test]
    public void Hold_ADrawnShotIsDroppedWhenTheStickMoves()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        RunUntilSwingStarts(bowman);

        // One frame into a 0.25 s draw.
        Assert.That(bowman.Weapon.IsSwinging, Is.True, "Sanity: drawing.");

        Step(bowman, Push, Running);

        Assert.That(bowman.Weapon.IsSwinging, Is.False, "the draw is dropped.");
        Assert.That(bowman.PendingShot, Is.Null);

        // Past where the dropped arrow would have left, still running.
        Run(bowman, (int)(Interval / Frame), Push, Running);

        Assert.That(_shots, Is.Zero, "no arrow leaves on the move.");
    }

    [Test]
    public void Hold_StoppedTheNextShotStartsAtOnce()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        RunUntilShot(bowman);

        Run(bowman, (int)(2f / Frame), Push, Running);

        int before = _events.Count<PlayerAttacked>();

        Step(bowman, Vector2.Zero, Vector3.Zero);

        Assert.That(bowman.IsHoldingFire, Is.False);
        Assert.That(_events.Count<PlayerAttacked>(), Is.EqualTo(before + 1),
            "the first still tick draws, not a cadence later.");
    }

    [Test]
    public void Hold_SlowingToAStopIsNotStill()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        Run(bowman, MaxFrames, Vector2.Zero, Sliding);

        Assert.That(bowman.IsHoldingFire, Is.True, "0.5 m/s with the stick centred is still moving.");
        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero);

        // Under PlayerMotor's 0.05 m/s is stopped. Not the boundary itself: a float squared at the
        // edge of a comparison is a row about rounding, not about the rule.
        Step(bowman, Vector2.Zero, new Vector3(0.04f, 0f, 0f));

        Assert.That(bowman.IsHoldingFire, Is.False);
        Assert.That(_events.Count<PlayerAttacked>(), Is.EqualTo(1));
    }

    [Test]
    public void Hold_ADashIsNotStill()
    {
        // Rule 2's third clause. The stick centred and the reported velocity zero, and the roll in
        // flight is still movement.
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        bowman.Charge.Request(_now);

        Step(bowman, Vector2.Zero, Vector3.Zero);

        Assert.That(bowman.Charge.IsActive, Is.True, "Sanity: rolling.");
        Assert.That(bowman.IsHoldingFire, Is.True);
    }

    [Test]
    public void Hold_AModifierLiftsIt()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        new ModifyStatHandler(Stats(bowman)).Apply(
            new ModifyStat(PlayerStat.FireWhileMoving, ModifierKind.Flat, 1f), new object());

        Run(bowman, MaxFrames, Push, Running);

        Assert.That(bowman.IsHoldingFire, Is.False);
        Assert.That(_shots, Is.GreaterThan(0), "the running shot, from one node and no new code.");
        Assert.That(bowman.FaceDirection, Is.Not.Null, "and it faces its target while it runs.");
    }

    [Test]
    public void Hold_AStepAfterTheShotKeepsTheCadence()
    {
        // The resolution named in RS-03a's As built. A swing whose arrow has left is not reset by a
        // hold, because a reset clears the cadence: a tap of the stick after every arrow would start
        // the next draw early, and stepping would out-shoot standing still.
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        RunUntilShot(bowman);

        float drawn = _swingStarts[0];

        Run(bowman, 2, Push, Running);

        Assert.That(bowman.IsHoldingFire, Is.True, "Sanity: the step held.");

        int before = _swingStarts.Count;

        Run(bowman, MaxFrames, Vector2.Zero, Vector3.Zero);

        Assert.That(_swingStarts.Count, Is.GreaterThan(before), "Sanity: it drew again.");
        Assert.That(_swingStarts[before] - drawn, Is.GreaterThanOrEqualTo(Interval - (Frame / 2f)),
            "the next draw waits out the interval the last one started.");
    }

    // ---- Rule 4: running faces the run, a centred stick faces the target -----------------------

    [Test]
    public void Face_RunningFacesTheWayItRuns()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        RunUntilFacing(bowman);

        Step(bowman, Push, Running);

        Assert.That(bowman.FaceDirection, Is.Null, "the motor faces where the Ranger runs.");
        Assert.That(bowman.Targeter.CurrentTargetId, Is.EqualTo(_husk.Id),
            "the target is kept for when it stops.");

        // A class that fires on the move keeps facing its target, as every shipped class does.
        PlayerCombat control = Combat(firesWhileMoving: true);

        RunUntilFacing(control);

        Step(control, Push, Running);

        Assert.That(control.FaceDirection, Is.Not.Null);
    }

    [Test]
    public void Face_StickCentredFacesTheTarget()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        Run(bowman, MaxFrames, Vector2.Zero, Sliding);

        Assert.That(bowman.IsHoldingFire, Is.True, "Sanity: still sliding, still holding.");
        Assert.That(bowman.FaceDirection, Is.Not.Null, "the turn starts before the body has stopped.");
        Assert.That(bowman.FaceDirection.Value.Z, Is.EqualTo(1f).Within(1e-5f), "at the Husk, dead ahead.");
    }

    // ---- Rule 5: one event per change, and none for a class that fires on the move -------------

    [Test]
    public void HoldFireChanged_PublishedOnChangeOnly()
    {
        PlayerCombat bowman = Combat(firesWhileMoving: false);

        Run(bowman, 10, Vector2.Zero, Vector3.Zero);

        Assert.That(_events.Count<HoldFireChanged>(), Is.Zero, "still from the start: nothing changed.");

        Run(bowman, 10, Push, Running);

        Assert.That(_events.Count<HoldFireChanged>(), Is.EqualTo(1), "one for ten ticks of running.");
        Assert.That(_events.Of<HoldFireChanged>()[0].IsHolding, Is.True);

        Run(bowman, 10, Vector2.Zero, Sliding);

        Assert.That(_events.Count<HoldFireChanged>(), Is.EqualTo(1), "sliding is still holding.");

        Step(bowman, Vector2.Zero, Vector3.Zero);

        Assert.That(_events.Count<HoldFireChanged>(), Is.EqualTo(2));
        Assert.That(_events.Of<HoldFireChanged>()[1].IsHolding, Is.False);
    }

    [Test]
    public void HoldFireChanged_NeverForAClassThatFiresMoving()
    {
        PlayerCombat archer = Combat(firesWhileMoving: true);

        Run(archer, 10, Vector2.Zero, Vector3.Zero);
        Run(archer, 10, Push, Running);
        Run(archer, 10, Vector2.Zero, Sliding);
        Run(archer, 10, Vector2.Zero, Vector3.Zero);

        Assert.That(archer.IsHoldingFire, Is.False);
        Assert.That(_events.Count<HoldFireChanged>(), Is.Zero);
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>One frame of the player's fight, and the run taking the shot it offered.</summary>
    private void Step(PlayerCombat player, Vector2 stick, Vector3 velocity)
    {
        _now += Frame;

        _snapshot.MoveInput = stick;
        _snapshot.PlayerVelocity = velocity;
        _snapshot.EnemyCount = 0;
        _snapshot.AddEnemy() = _husk;

        _enemies.Ingest(_snapshot);

        int before = _events.Count<PlayerAttacked>();

        player.Tick(Frame, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);

        if (_events.Count<PlayerAttacked>() > before)
        {
            _swingStarts.Add(_now);
        }

        if (player.TryTakeShot(out _))
        {
            _shots++;
        }
    }

    private void Run(PlayerCombat player, int frames, Vector2 stick, Vector3 velocity)
    {
        for (int i = 0; i < frames; i++)
        {
            Step(player, stick, velocity);
        }
    }

    private void RunUntilSwingStarts(PlayerCombat player)
    {
        int before = _events.Count<PlayerAttacked>();

        for (int i = 0; i < MaxFrames; i++)
        {
            Step(player, Vector2.Zero, Vector3.Zero);

            if (_events.Count<PlayerAttacked>() > before)
            {
                return;
            }
        }

        throw new InvalidOperationException($"No swing within {MaxFrames} still ticks.");
    }

    private void RunUntilShot(PlayerCombat player)
    {
        int before = _shots;

        for (int i = 0; i < MaxFrames; i++)
        {
            Step(player, Vector2.Zero, Vector3.Zero);

            if (_shots > before)
            {
                return;
            }
        }

        throw new InvalidOperationException($"No arrow within {MaxFrames} still ticks.");
    }

    private void RunUntilFacing(PlayerCombat player)
    {
        for (int i = 0; i < MaxFrames; i++)
        {
            Step(player, Vector2.Zero, Vector3.Zero);

            if (player.FaceDirection is not null)
            {
                return;
            }
        }

        throw new InvalidOperationException($"No target faced within {MaxFrames} still ticks.");
    }

    private PlayerCombat Combat(bool firesWhileMoving) =>
        new(Bowman(firesWhileMoving), _events, _intents, EnemyCapacity);

    private PlayerStats Stats(PlayerCombat combat) => new(
        combat,
        new PlayerMotor(new MovementSpec(3f, 0.06f, 0.08f, 720f), Vector3.UnitZ),
        new LevelTracker(Scalings.Xp(), _events));

    /// <summary>
    /// A bow on a class with a roll: the shape RS-03c will author, with round numbers. No Aegis, and
    /// the Focus ramp off.
    /// </summary>
    private static CharacterSpec Bowman(bool firesWhileMoving) => new(
        new ContentId(BowmanId),
        new LocKey("character.bowman.name"),
        new LocKey("character.bowman.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(ShotRange, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(
            WeaponKind.Projectile, ShotDamage, ShotsPerSecond, ShotRange, FullCircle, DamageFrame,
            ShotSpeed, ShotRadius, firesWhileMoving: firesWhileMoving),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
        null,
        0.5f);

    /// <summary>A Husk that stands still and cannot be killed by anything these rows do.</summary>
    private static EnemySpec Husk() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        1000f,
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

    private static ModeSpec Descent() => new(
        new ContentId("mode.descent"),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());
}
