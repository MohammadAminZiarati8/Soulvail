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

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// The corpse decoy from the arena's end: an enemy that is told a corpse is its quarry, what it
/// does about that, what it stops doing when the corpse rots, and where in the frame the two are
/// decided.
/// </summary>
/// <remarks>
/// <para>
/// <b>The redirect is one local variable in <c>EnemySystem.Perceive</c> and no behaviour was
/// touched</b> (M5-03 rule 2) — so these rows are written against real perception rather than
/// against blackboards poked by hand, which is the only way the claim can be proved. A Husk is
/// spawned, a snapshot is ingested with the decoys beside it, and what comes out is read off the
/// blackboard and off the intents.
/// </para>
/// <para>
/// <b>This is not a targeting system and the rows say so</b> (rule 8). Every living enemy is
/// taunted, including one standing further from the decoy than from the player, and there is no
/// per-enemy taunt to set up: the only state anywhere is <c>QuarryIsADecoy</c>, recomputed every
/// tick from the one question <c>LureSystem.TryGetLure</c> answers the same way about everybody.
/// </para>
/// <para>
/// GD §8.1's Husk throughout — 36 HP, 8 contact damage, 1.2 m reach, a 0.4 s windup and a 30 m
/// aggro range — at depth 1, so a failure reads as "the enemy we ship stopped behaving" rather than
/// as an arithmetic puzzle.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LurePerceptionTests
{
    private const string HuskId = "enemy.husk";
    private const string GravecallerId = "character.gravecaller";
    private const string DescentId = "mode.descent";
    private const int Seed = 11;

    /// <summary>CH §3.2: a decoy taunts for three seconds.</summary>
    private const float DecoySeconds = 3f;

    // GD §8.1's Husk, and the three numbers a strike is made of.
    private const float HuskMaxHp = 36f;
    private const float ContactDamage = 8f;
    private const float MoveSpeed = 3.5f;
    private const float Reach = 1.2f;
    private const float WindupTime = 0.4f;
    private const float AggroRange = 30f;

    private const float PlayerMaxHp = 140f;

    // CH §3.2 and CH §4: the class that can leave a corpse at all.
    private const float BlinkDistance = 6f;
    private const float BlinkDuration = 0.05f;
    private const float BoltRange = 12f;

    private const int Capacity = 8;
    private const int DeviceCap = 8;
    private const int ProjectileCapacity = 8;

    /// <summary>
    /// The frame these rows run at. An eighth of a second is exact in binary, so a run ticked with
    /// it reaches 3.125 by addition rather than nearly — which is what <see cref="Run_TheExpiryIsAbovePerception"/>
    /// needs, since its whole subject is one tick's worth of ordering. It is also inside the
    /// Shroudstep's 0.15 s input buffer, so a press survives to the first tick.
    /// </summary>
    private const float Frame = 0.125f;

    /// <summary>How many steps a behaviour row gives a Husk to walk through its state machine.</summary>
    private const int MaxSteps = 32;

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private ContentCatalog _catalog;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LureSystem _lures;

    /// <summary>The fixture's simulated clock — <c>RunState.Time</c>'s stand-in.</summary>
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _catalog = Catalog();
        _system = new EnemySystem(_catalog, _events, new FixedRandom(Seed), Scaling(), Capacity);
        _player = new PlayerCombat(Gravecaller(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _lures = new LureSystem(_events);
        _now = 0f;
    }

    // ---- Rule 2: the four fields, and the fifth that is zeroed -----------------------------------

    [Test]
    public void Perception_ALuredEnemyIsToldTheDecoy()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), At(0f, 10f));

        _lures.Drop(At(0f, 0f), _now, DecoySeconds);

        // A path the body computed against the *player*, which is the one sense that lies while a
        // decoy stands: left alone it would steer this Husk around the arena towards somebody it is
        // not walking at any more.
        _system.Ingest(Snapshot(At(20f, 10f), husk, path: new Vector2(1f, 0f)), _lures);

        EnemyBlackboard blackboard = husk.Blackboard;

        Assert.That(blackboard.PlayerPosition, Is.EqualTo(At(0f, 0f)),
            "The four fields mean 'where this enemy's quarry is' while a corpse stands.");
        Assert.That(blackboard.DistanceToPlayer, Is.EqualTo(10f).Within(Tolerance),
            "Ten metres to the decoy, not twenty to the player.");
        Assert.That(blackboard.DirectionToPlayer.X, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(blackboard.DirectionToPlayer.Y, Is.EqualTo(-1f).Within(Tolerance),
            "Straight back down −Z at the corpse.");

        Assert.That(blackboard.PathDirectionToPlayer, Is.EqualTo(Vector2.Zero),
            "Zeroed, so the behaviours fall back to the straight line M1-19 built for a missing "
                + "NavMesh — which is the first thing to use it on purpose (rule 2).");

        Assert.That(blackboard.QuarryIsADecoy, Is.True);
    }

    [Test]
    public void Perception_AnUnluredEnemyIsUnchanged()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), At(0f, 10f));

        var path = new Vector2(0.6f, -0.8f);

        // The build before this task, spelled as the call that still exists: no lure argument at
        // all. Whatever comes out of it is what an unlured enemy must go on perceiving.
        _system.Ingest(Snapshot(At(3f, 14f), husk, path));

        EnemyBlackboard blackboard = husk.Blackboard;

        Vector3 player = blackboard.PlayerPosition;
        float distance = blackboard.DistanceToPlayer;
        Vector2 direction = blackboard.DirectionToPlayer;
        Vector2 pathed = blackboard.PathDirectionToPlayer;

        // And now the same frame with an empty lure system in it. Empty and null mean the same
        // thing, which is what makes "nothing changed for anyone else" true rather than nearly.
        _system.Ingest(Snapshot(At(3f, 14f), husk, path), _lures);

        Assert.That(blackboard.PlayerPosition, Is.EqualTo(player));
        Assert.That(blackboard.DistanceToPlayer, Is.EqualTo(distance));
        Assert.That(blackboard.DirectionToPlayer, Is.EqualTo(direction));
        Assert.That(blackboard.PathDirectionToPlayer, Is.EqualTo(pathed),
            "The body's path survives untouched when there is nothing to be lured by.");

        Assert.That(pathed, Is.EqualTo(path), "Sanity: the sense was copied down in the first place.");
        Assert.That(blackboard.QuarryIsADecoy, Is.False);
    }

    [Test]
    public void Perception_TheDecoyRotsAndTheEnemyTurnsBack()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), At(0f, 10f));

        var path = new Vector2(1f, 0f);

        _lures.Drop(At(0f, 0f), _now, DecoySeconds);
        _system.Ingest(Snapshot(At(20f, 10f), husk, path), _lures);

        Assert.That(husk.Blackboard.QuarryIsADecoy, Is.True, "Sanity: it is lured to begin with.");

        _lures.Tick(DecoySeconds);
        _system.Ingest(Snapshot(At(20f, 10f), husk, path), _lures);

        EnemyBlackboard blackboard = husk.Blackboard;

        Assert.That(blackboard.QuarryIsADecoy, Is.False);
        Assert.That(blackboard.PlayerPosition, Is.EqualTo(At(20f, 10f)), "Back to the player.");
        Assert.That(blackboard.DistanceToPlayer, Is.EqualTo(20f).Within(Tolerance));
        Assert.That(blackboard.DirectionToPlayer.X, Is.EqualTo(1f).Within(Tolerance));

        Assert.That(blackboard.PathDirectionToPlayer, Is.EqualTo(path),
            "And the path is restored from the sense rather than left at the zero the taunt wrote.");
    }

    [Test]
    public void Perception_ADecoyBehindTheEnemyStillTakesIt()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), At(0f, 0f));

        // Twenty-five metres away, with the player standing two metres from the Husk's face. A
        // taunt is not a proximity check: "nearest" decides *which* decoy, never *whether* — and
        // this row exists because "nearest decoy" and "nearer than the player" are easy to conflate.
        _lures.Drop(At(0f, -25f), _now, DecoySeconds);

        _system.Ingest(Snapshot(At(0f, 2f), husk), _lures);

        Assert.That(husk.Blackboard.QuarryIsADecoy, Is.True);
        Assert.That(husk.Blackboard.DistanceToPlayer, Is.EqualTo(25f).Within(Tolerance));
    }

    // ---- Rules 2 and 7: what the enemy does about it ---------------------------------------------

    [Test]
    public void Chaser_WalksAtTheDecoy()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), At(0f, 10f));

        _lures.Drop(At(0f, 0f), _now, DecoySeconds);

        // Ingested, then ticked, in the order a run does it. The first tick takes the machine out
        // of Idle; the second is the one that walks.
        Step(husk, At(20f, 10f));
        _intents.Clear();
        Step(husk, At(20f, 10f));

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity.X, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(move.Velocity.Z, Is.EqualTo(-MoveSpeed).Within(Tolerance),
            "At the corpse, at the agent's own speed — and away from a player standing at +X.");
    }

    [Test]
    public void Chaser_StrikesTheDecoyAndHurtsNobody()
    {
        // The control first, and it is what makes the row mean something: with the *player* stood
        // where the corpse will be, this same Husk lands its 8 damage. So the assertion below is
        // about the decoy rather than about a machine that never reaches its strike.
        // Several strikes rather than one: the row runs for four seconds and a Husk's windup,
        // strike and recovery cycle is a little over one, so what is asserted is that the machine
        // reaches its damage frame at all and that every hit is worth the archetype's 8.
        float unlured = DamageFromAStrike(withDecoy: false, out int unluredHits);

        Assert.That(unlured, Is.GreaterThanOrEqualTo(ContactDamage),
            "Sanity: a Husk in reach of the player hits them.");
        Assert.That(unlured % ContactDamage, Is.EqualTo(0f).Within(Tolerance),
            "…for GD §8.1's 8 a swing, at depth 1.");
        Assert.That(unluredHits, Is.GreaterThan(0), "…and every one of them was announced.");

        float lured = DamageFromAStrike(withDecoy: true, out int luredHits);

        Assert.That(lured, Is.Zero,
            "It swings at the corpse and hurts nobody (rule 7). A decoy has no health, no registry "
                + "slot and no place in the snapshot — it cannot be hit, hurt or killed, and it "
                + "cannot pass a blow through to somebody six metres away either.");

        Assert.That(luredHits, Is.Zero,
            "And nothing was published: a blocked hit is news, a hit that never reached anybody "
                + "is not.");
    }

    // ---- Rule 10: where in the frame the two are decided -----------------------------------------

    [Test]
    public void Run_TheExpiryIsAbovePerception()
    {
        RunSession session = Session();

        var snapshot = new WorldSnapshot(Capacity) { Dt = Frame, PlayerPosition = At(0f, 0f) };

        // One blink, from the origin, on the first tick. The corpse therefore expires at
        // Frame + 3 = 3.125, which this clock reaches exactly.
        session.MovementSkill();
        session.Tick(snapshot);

        Assert.That(_events.Count<DecoySpawned>(), Is.EqualTo(1), "Sanity: the blink left a corpse.");

        float expiresAt = session.State.Time + DecoySeconds;

        // Forty metres away, so that "walking at the corpse" and "walking at the player" are two
        // obviously different directions — and so that the Husk at (0, 0, 20) is outside the Bone
        // Bolt's 12 m reach for the whole row and is never shot at.
        snapshot.PlayerPosition = At(40f, 0f);

        // Stops one whole tick short of the expiry. The tolerance is what keeps the comparison from
        // depending on the last addition landing exactly on the boundary — the clock is exact in
        // binary at an eighth of a second, and the row below is what says so.
        while (session.State.Time < expiresAt - Frame - Tolerance)
        {
            _intents.Clear();
            session.Tick(snapshot);
        }

        Assert.That(session.State.Time, Is.EqualTo(expiresAt - Frame).Within(Tolerance),
            "Sanity: the clock is one tick short of the expiry, exactly.");

        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.LessThan(0f),
            "The tick before: still walking at the corpse behind it.");
        Assert.That(_events.Count<DecoyExpired>(), Is.Zero);

        _intents.Clear();
        session.Tick(snapshot);

        // The tick the two coincide on. The expiry runs immediately above the ingest, so the decoy
        // is already gone by the time this enemy is told which way its quarry is — it is never
        // redirected at a corpse that has already rotted.
        Assert.That(session.State.Time, Is.EqualTo(expiresAt).Within(Tolerance));
        Assert.That(_events.Count<DecoyExpired>(), Is.EqualTo(1));

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity.X, Is.GreaterThan(0f), "Turned back towards the player at +X.");
        Assert.That(move.Velocity.Z, Is.LessThan(0f).Or.EqualTo(0f),
            "Sanity: the player is at −Z of it as well, so the walk is the diagonal to them.");
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>A point on the ground plane. Y is a rendering detail here (AR §18.4).</summary>
    private static Vector3 At(float x, float z) => new(x, 0f, z);

    /// <summary>
    /// How much damage one Husk's strike did to the player, with or without a corpse standing
    /// where it is aimed.
    /// </summary>
    /// <remarks>
    /// The two halves are the same frames with one difference: the thing at the origin is either
    /// the player or a decoy. <c>ChaserBehaviour</c> is driven through the real
    /// <c>EnemySystem.Tick</c>, so the strike it reaches is the one a run would reach.
    /// </remarks>
    private float DamageFromAStrike(bool withDecoy, out int playerDamagedEvents)
    {
        // Its own recorder, not the fixture's: the two halves of this row are two separate four
        // second runs, and a shared sink would let the control's three hits be counted against the
        // lured half that is supposed to have none.
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var system = new EnemySystem(_catalog, events, new FixedRandom(Seed), Scaling(), Capacity);
        var player = new PlayerCombat(Gravecaller(), events, intents, Capacity);
        var projectiles = new ProjectileSystem(events, ProjectileCapacity);
        var lures = new LureSystem(events);

        // Standing on the thing it is about to hit, whichever thing that is.
        EnemyAgent husk = system.Spawn(new ContentId(HuskId), At(0f, 0f));

        // The player is six metres away when a corpse is doing the taunting — the Shroudstep's own
        // distance, which is the whole scenario: the blink bought the distance and the corpse is
        // what holds the Husk in it.
        Vector3 playerPosition = withDecoy ? At(BlinkDistance, 0f) : At(0f, 0f);

        if (withDecoy)
        {
            lures.Drop(At(0f, 0f), 0f, DecoySeconds);
        }

        float before = player.Health.Current;
        var now = 0f;

        for (int i = 0; i < MaxSteps; i++)
        {
            var snapshot = new WorldSnapshot(Capacity) { Dt = Frame, PlayerPosition = playerPosition };

            AddSense(snapshot, husk.Id, husk.Position);

            system.Ingest(snapshot, lures);

            now += Frame;

            system.Tick(new EnemyTickContext(Frame, now, player, intents, events, projectiles, system));
        }

        Assert.That(now, Is.GreaterThan(WindupTime),
            "Sanity: the row ran for longer than the telegraph, so a strike was reachable.");

        playerDamagedEvents = events.Count<PlayerDamaged>();

        return before - player.Health.Current;
    }

    /// <summary>One frame for <paramref name="husk"/>: ingest with the fixture's lures, then tick.</summary>
    private void Step(EnemyAgent husk, Vector3 playerPosition)
    {
        _system.Ingest(Snapshot(playerPosition, husk), _lures);

        _now += Frame;

        _system.Tick(new EnemyTickContext(
            Frame, _now, _player, _intents, _events, _projectiles, _system));
    }

    /// <summary>This frame's world: where the player is, and where the one Husk is.</summary>
    private static WorldSnapshot Snapshot(Vector3 playerPosition, EnemyAgent husk, Vector2 path = default)
    {
        var snapshot = new WorldSnapshot(Capacity) { Dt = Frame, PlayerPosition = playerPosition };

        AddSense(snapshot, husk.Id, husk.Position, path);

        return snapshot;
    }

    /// <summary>
    /// Writes one enemy into the snapshot's next slot.
    /// </summary>
    /// <remarks>
    /// By <c>ref</c>, like every other fixture that fills one: <c>AddEnemy</c> hands back a
    /// reference into a reused array, and a copy would be written into nothing while the row passed
    /// (M0-05). Every field is assigned, zeroes included, for the same reason.
    /// </remarks>
    private static void AddSense(WorldSnapshot snapshot, int id, Vector3 position, Vector2 path = default)
    {
        ref EnemySense sense = ref snapshot.AddEnemy();

        sense.Id = id;
        sense.Position = position;
        sense.Velocity = Vector3.Zero;
        sense.PathDirectionToPlayer = path;
        sense.HasLineOfSight = true;
    }

    /// <summary>
    /// A Gravecaller's run with one Husk standing twenty metres up +Z, started and ticking.
    /// </summary>
    /// <remarks>
    /// Twenty metres is chosen twice over: inside GD §8.1's 30 m aggro range, so the Husk walks
    /// from the first tick, and outside CH §4's 12 m Bone Bolt, so the player never shoots the
    /// subject of the row out from under it.
    /// </remarks>
    private RunSession Session()
    {
        var session = new RunSession(
            _catalog,
            new FixedRandom(Seed),
            _events,
            _intents,
            new RunRecorder(new FixedRandom(Seed), new FixedClock(default), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(GravecallerId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), At(0f, 20f)) }),
            restore: null));

        _events.Clear();
        _intents.Clear();

        return session;
    }

    private ContentCatalog Catalog() => new(
        new[] { Gravecaller() },
        new[] { Husk() },
        new[] { Descent() });

    /// <summary>GD §8.1's Husk, authored <c>Chaser</c> — the archetype that walks and strikes.</summary>
    private static EnemySpec Husk() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
        MoveSpeed,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        ContactDamage,
        Reach,
        WindupTime,
        recoverTime: 0.6f,
        AggroRange,
        EnemyBehaviourKind.Chaser);

    /// <summary>
    /// The Gravecaller of CH §3.2, with the Shroudstep that can leave a corpse at all.
    /// </summary>
    /// <remarks>
    /// <b>No Aegis</b>, so the one row that reads hit points is reading hit points rather than a
    /// shield, and the Focus ramp is switched off with a multiplier of 1 — neither is what any row
    /// here is about.
    /// </remarks>
    private static CharacterSpec Gravecaller() => new(
        new ContentId(GravecallerId),
        new LocKey("character.gravecaller.name"),
        new LocKey("character.gravecaller.description"),
        PlayerMaxHp,
        new MovementSpec(3.1f, 0.06f, 0.08f, 720f),
        new TargetingSpec(BoltRange, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 9f, 4f, BoltRange, 360f, 0.15f, 40f, 0.8f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Shroudstep, BlinkDistance, BlinkDuration, 2.5f, 0.15f,
            0f, 0f, 0.05f, DecoySeconds),
        null,
        0.5f);

    /// <summary>
    /// Descent with an <b>empty roster</b>, for the reason every fixture that starts a run gives:
    /// <c>RunSession.Start</c> resolves every roster id before it announces a run, and what the one
    /// session row spawns comes from a <c>SpawnPlan</c>.
    /// </summary>
    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());

    /// <summary>Depth scaling at its shipped curves — every row here runs at depth 1.</summary>
    private static DepthScaling Scaling() => new(Scalings.Design());
}
