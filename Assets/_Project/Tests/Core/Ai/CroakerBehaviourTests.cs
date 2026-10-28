using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// Old Croaker approaching, hop-slamming onto where the player stood, chaining hops by phase,
/// croaking on a phase's entry, and recovering — JG-04b rules 1–7 — and lashing its tongue while
/// the slam cools, JG-04e rule 4 — and closing in hops, JG-05c.
/// </summary>
/// <remarks>
/// <see cref="WardenBehaviourTests"/>' fixture and its two halves: the rows about one attack drive
/// the behaviour bare, on an ordinary agent wearing the boss's body (phase 0, one hop); the rows
/// about phases stand the boss up through <c>EnemySystem.SpawnBoss</c> with the behaviour as its
/// <c>inner</c>, the way the run will. Real hazard systems throughout, never fakes.
/// </remarks>
[TestFixture]
public sealed class CroakerBehaviourTests
{
    private const string BossId = "boss.croaker";
    private const string CroakerEnemyId = "enemy.croaker";
    private const string HopperId = "enemy.croaker.hopper";

    /// <summary><c>Croaker.asset</c>'s gait since JG-05c: the Hop clip's landing, then a sit.</summary>
    private const float HopperHopTime = 0.7f;
    private const float HopperRestTime = 0.75f;
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";

    private const float CroakerMoveSpeed = 2f;
    private const float CroakerContactDamage = 20f;
    private const float CroakerAggro = 40f;

    private const float Beat = 1.5f;
    private const float SecondPhase = 0.66f;
    private const float ThirdPhase = 0.33f;
    private const int SecondPhaseHusks = 3;

    private const float Frame = 0.1f;
    private const int Capacity = 32;
    private const int ProjectileCapacity = 8;

    private const float Tolerance = 1e-3f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private ShockwaveSystem _shockwaves;
    private FissureSystem _fissures;
    private LaneSystem _lanes;
    private FixedRandom _random;
    private EnemySystem _enemies;
    private EnemyAgent _driven;
    private float _clock;

    private float _allocationClock;

    private int _allocationStep;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _shockwaves = new ShockwaveSystem(_events);
        _fissures = new FissureSystem(_events);
        _lanes = new LaneSystem(_events);
        _random = new FixedRandom().SetMisc(0f);
        _enemies = NewEnemies();
        _clock = 0f;
        _allocationClock = 0f;
        _allocationStep = 0;
    }

    // ---- Rule 1 -----------------------------------------------------------------------------------

    [Test]
    public void Approach_StopsAtSlamRange()
    {
        CroakerBehaviour croaker = Croaker();

        Place(distance: CroakerBehaviour.SlamRange + 4f);

        TickUntil(croaker, CroakerState.Approach, 1f);

        _intents.Clear();
        Tick(croaker);

        Assert.That(
            _intents.LastEnemyMove.Velocity.Z,
            Is.EqualTo(CroakerMoveSpeed).Within(Tolerance),
            "Outside SlamRange it closes at the archetype's speed.");
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach));

        Place(distance: CroakerBehaviour.SlamRange);

        _intents.Clear();
        Tick(croaker);

        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "At SlamRange it stops.");
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Slam), "And slams, being off cooldown.");

        RunOutTheSlam(croaker);

        // Back in Approach, inside range, on cooldown: it sits rather than slamming or closing.
        // Inside the lash's minimum too (JG-04e), so the tongue does not take the turn either.
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach));

        Place(distance: CroakerBehaviour.LashMinRange - 1f);

        _intents.Clear();
        Tick(croaker);

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach), "The cooldown holds the next slam.");
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(croaker.NextSlamAt, Is.GreaterThan(_clock));
    }

    // ---- Rule 2 -----------------------------------------------------------------------------------

    [Test]
    public void Slam_TelegraphsMove1_AndArmsAFissureWhereThePlayerStood()
    {
        CroakerBehaviour croaker = Croaker();

        var stood = new Vector3(1f, 0f, 6f);

        Place(distance: 6f, playerPosition: stood);

        TickUntil(croaker, CroakerState.Slam, 1f);

        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();

        Assert.That(telegraph.Id, Is.EqualTo(_driven.Id));
        Assert.That(telegraph.Move, Is.EqualTo(CroakerBehaviour.SlamMove));
        Assert.That(telegraph.Duration, Is.EqualTo(0.9f).Within(Tolerance), "Croaker.fbx's Slam impact.");
        Assert.That(
            telegraph.Duration,
            Is.GreaterThanOrEqualTo(WardenBehaviour.MinTelegraphSeconds),
            "GD §9.1 rule 1's floor.");

        Assert.That(_fissures.ActiveCount, Is.EqualTo(1));
        Assert.That(_fissures.PositionAt(0), Is.EqualTo(stood), "Where the player stood, no lead.");
        Assert.That(_fissures.RadiusAt(0), Is.EqualTo(2.5f).Within(Tolerance));

        // The player moves; the circle does not follow.
        Place(distance: 6f, playerPosition: new Vector3(-3f, 0f, 4f));

        Tick(croaker);

        Assert.That(_fissures.PositionAt(0), Is.EqualTo(stood));
    }

    // ---- Rule 3 -----------------------------------------------------------------------------------

    [Test]
    public void Landing_MovesTheBody_FiresTheFissure_EmitsAShockwave()
    {
        CroakerBehaviour croaker = Croaker();

        var stood = new Vector3(3f, 0f, 4f);

        Place(distance: 5f, playerPosition: stood);

        TickUntil(croaker, CroakerState.Slam, 1f);

        Vector3 hop = stood - _driven.Position;

        _intents.Clear();

        int airborne = 0;

        for (int i = 0; i < 20 && _shockwaves.ActiveCount == 0; i++)
        {
            Tick(croaker);

            Assert.That(
                Vector3.Distance(
                    _intents.LastEnemyMove.Velocity * CroakerBehaviour.SlamTelegraphSeconds,
                    new Vector3(hop.X, 0f, hop.Z)),
                Is.LessThanOrEqualTo(Tolerance),
                "Every airborne tick carries the body from take-off to the landing in the telegraph.");

            airborne++;
        }

        Assert.That(
            airborne * Frame,
            Is.EqualTo(CroakerBehaviour.SlamTelegraphSeconds).Within(Frame + Tolerance),
            "It lands when the telegraph is over.");

        Assert.That(_shockwaves.ActiveCount, Is.EqualTo(1));

        ShockwaveEmitted ring = _events.Single<ShockwaveEmitted>();

        Assert.That(ring.Origin, Is.EqualTo(stood), "The ring leaves from the fissure's centre.");
        Assert.That(ring.Speed, Is.EqualTo(7f).Within(Tolerance));
        Assert.That(ring.MaxRadius, Is.EqualTo(6f).Within(Tolerance));

        // The fissure was armed for exactly the telegraph, so it fires on its own clock now.
        _fissures.Tick(_clock, new Vector3(50f, 0f, 50f), _player);

        Assert.That(_events.Count<FissureFired>(), Is.EqualTo(1), "The landing fires the fissure.");
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Recover), "Phase 0 hops once.");
    }

    // ---- Rule 4 -----------------------------------------------------------------------------------

    [Test]
    public void Phase2_HopsTwice_Phase3_HopsThrice()
    {
        EnemyAgent boss = SpawnBoss();
        CroakerBehaviour croaker = InnerOf(boss);

        Place(distance: 4f);

        Cross(boss, SecondPhase - 0.01f);

        List<float> armed = RunOneSlam(croaker);

        Assert.That(croaker.HopCount, Is.EqualTo(2));
        Assert.That(armed.Count, Is.EqualTo(2), "Each hop arms its own fissure.");
        Assert.That(SlamTelegraphs(), Is.EqualTo(2), "Each hop gets its own telegraph.");
        AssertHopSpacing(armed);

        // Crossed while it recovers, so the next slam is the third phase's — the beat and the
        // croak outlast what is left of the cooldown.
        Cross(boss, ThirdPhase - 0.01f);

        armed = RunOneSlam(croaker);

        Assert.That(croaker.HopCount, Is.EqualTo(3));
        Assert.That(armed.Count, Is.EqualTo(3));
        Assert.That(SlamTelegraphs(), Is.EqualTo(3));
        AssertHopSpacing(armed);
    }

    // ---- Rule 5 -----------------------------------------------------------------------------------

    [Test]
    public void PhaseEntry_CroaksAfterTheBeat()
    {
        EnemyAgent boss = SpawnBoss();
        CroakerBehaviour croaker = InnerOf(boss);

        Place(distance: 4f);

        Cross(boss, SecondPhase - 0.01f);

        _events.Clear();

        TickWorldFor(Beat - (2f * Frame));

        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero, "Nothing during the beat.");

        for (int i = 0; i < 10 && _events.Count<EnemyTelegraph>() == 0; i++)
        {
            TickWorld();
        }

        EnemyTelegraph croak = _events.Single<EnemyTelegraph>();

        Assert.That(croak.Move, Is.EqualTo(CroakerBehaviour.CroakMove));
        Assert.That(croak.Duration, Is.EqualTo(1.0f).Within(Tolerance), "Croaker.fbx's Croak call.");
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Croak));

        TickWorldFor(CroakerBehaviour.CroakTelegraphSeconds + (2f * Frame));

        Assert.That(croaker.State, Is.Not.EqualTo(CroakerState.Croak), "The call ends.");
        Assert.That(
            _events.Count<EnemyTelegraph>() - SlamTelegraphs(),
            Is.EqualTo(1),
            "One croak per phase entry.");
        Assert.That(
            LivingHusks(),
            Is.EqualTo(SecondPhaseHusks),
            "The adds are BossBehaviour's summons; the croak calls none of its own.");
    }

    // ---- Rule 6 -----------------------------------------------------------------------------------

    [Test]
    public void Recover_LastsItsTime_ThenApproaches()
    {
        CroakerBehaviour croaker = Croaker();

        Place(distance: 5f);

        TickUntil(croaker, CroakerState.Recover, 3f);

        float recoverFrames = MathF.Floor((CroakerBehaviour.RecoverSeconds - Tolerance) / Frame);

        for (int i = 0; i < recoverFrames - 1; i++)
        {
            _intents.Clear();
            Tick(croaker);

            Assert.That(croaker.State, Is.EqualTo(CroakerState.Recover));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "Rooted.");
        }

        // Far away now: it still approaches rather than going idle, and walks.
        Place(distance: CroakerAggro + 10f);

        TickUntil(croaker, CroakerState.Approach, 3f * Frame);

        _intents.Clear();
        Tick(croaker);

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach));
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.GreaterThan(0f));
    }

    // ---- JG-04e rule 4 ----------------------------------------------------------------------------

    [Test]
    public void Croaker_Lash_HoldsFacingWhilePlayerCircles()
    {
        CroakerBehaviour croaker = EnterLash();
        Vector2 aimed = _lanes.DirectionAt(0);

        _driven.Blackboard.DirectionToPlayer = new Vector2(1f, 0f);

        while (croaker.State == CroakerState.Lash)
        {
            _intents.Clear();
            Tick(croaker);

            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(aimed));
        }
    }

    [Test]
    public void Croaker_Lash_FacingMatchesTheLane()
    {
        CroakerBehaviour croaker = EnterLash();

        _intents.Clear();
        Tick(croaker);

        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(_lanes.DirectionAt(0)));
    }

    [Test]
    public void Croaker_AfterLash_FacesThePlayerAgain()
    {
        CroakerBehaviour croaker = EnterLash();

        _driven.Blackboard.DirectionToPlayer = new Vector2(1f, 0f);
        TickUntil(croaker, CroakerState.Approach, CroakerBehaviour.LashTelegraphSeconds + (2f * Frame));

        _intents.Clear();
        Tick(croaker);

        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(new Vector2(1f, 0f)));
    }

    [Test]
    public void Croaker_Lash_LaneCarriesATongue()
    {
        // JG-07a rule 12: the lash's lane says a tongue strikes down it, so the view draws one.
        CroakerBehaviour croaker = Croaker();

        Place(distance: 6f);

        TickUntil(croaker, CroakerState.Slam, 1f);

        RunOutTheSlam(croaker);

        Place(distance: 6f);

        _events.Clear();

        for (int i = 0; i < 40 && croaker.State != CroakerState.Lash; i++)
        {
            Tick(croaker);
        }

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Lash), "Sanity: it lashed.");
        Assert.That(_events.Single<LaneArmed>().Tongue, Is.True);
    }

    [Test]
    public void Croaker_LashesInRange_WhenSlamIsCooling()
    {
        CroakerBehaviour croaker = Croaker();

        // Slam ready and in range: the slam wins, and no lane opens.
        Place(distance: 6f);

        TickUntil(croaker, CroakerState.Slam, 1f);

        Assert.That(_lanes.ActiveCount, Is.Zero, "A ready slam is never a lash.");

        RunOutTheSlam(croaker);

        Assert.That(croaker.NextSlamAt, Is.GreaterThan(_clock), "Sanity: the slam is cooling.");
        Assert.That(
            croaker.NextAttackAt,
            Is.EqualTo(_clock - Frame + CroakerBehaviour.AttackGapSeconds).Within(Tolerance),
            "The recover's end starts the rest.");

        // Too close, then too far: no lash.
        Place(distance: CroakerBehaviour.LashMinRange - 0.5f);
        Tick(croaker);

        Place(distance: CroakerBehaviour.LashRange + 0.5f);
        Tick(croaker);

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach));
        Assert.That(_lanes.ActiveCount, Is.Zero, "Outside 3–9 m the tongue stays in.");

        // Mid-range while the slam cools: the lash — but only once the rest after the slam is over.
        Place(distance: 6f);

        _events.Clear();

        float windup = _clock;

        for (int i = 0; i < 40 && croaker.State != CroakerState.Lash; i++)
        {
            windup = _clock;
            Tick(croaker);
        }

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Lash));
        Assert.That(
            windup,
            Is.EqualTo(croaker.NextAttackAt).Within(Frame + Tolerance),
            "It waits out the rest after the slam, then lashes on the first legal tick.");

        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();

        Assert.That(telegraph.Id, Is.EqualTo(_driven.Id));
        Assert.That(telegraph.Move, Is.EqualTo(CroakerBehaviour.LashMove));
        Assert.That(telegraph.Move, Is.EqualTo(2));
        Assert.That(telegraph.Duration, Is.EqualTo(0.8f).Within(Tolerance));

        Assert.That(_lanes.ActiveCount, Is.EqualTo(1));
        Assert.That(_lanes.OriginAt(0), Is.EqualTo(_driven.Position), "From the mouth.");
        Assert.That(_lanes.DirectionAt(0), Is.EqualTo(new Vector2(0f, 1f)), "At the player.");
        Assert.That(_lanes.LengthAt(0), Is.EqualTo(9f).Within(Tolerance));
        Assert.That(_lanes.HalfWidthAt(0), Is.EqualTo(0.8f).Within(Tolerance));

        LaneArmed armed = _events.Single<LaneArmed>();

        Assert.That(armed.ArmSeconds, Is.EqualTo(0.8f).Within(Tolerance), "It bites as the telegraph ends.");
        Assert.That(
            croaker.NextLashAt,
            Is.EqualTo(windup + CroakerBehaviour.LashCooldown).Within(Tolerance),
            "From the wind-up.");

        // The player sidesteps; the lane was aimed at the wind-up's start and does not follow.
        _driven.Blackboard.DirectionToPlayer = Vector2.Normalize(new Vector2(1f, 1f));

        TickUntil(croaker, CroakerState.Approach, CroakerBehaviour.LashTelegraphSeconds + (2f * Frame));

        Assert.That(_lanes.DirectionAt(0), Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(
            _clock - windup,
            Is.EqualTo(CroakerBehaviour.LashTelegraphSeconds).Within((2f * Frame) + Tolerance),
            "Rooted for the telegraph, then back to approaching.");
        Assert.That(
            croaker.NextAttackAt,
            Is.EqualTo(_clock - Frame + CroakerBehaviour.AttackGapSeconds).Within(Tolerance),
            "The lash's end starts the rest too.");

        // On cooldown: the next ticks hold the tongue until the slam is ready again.
        Place(distance: 6f);

        _events.Clear();

        while (_clock < croaker.NextSlamAt - Tolerance)
        {
            Tick(croaker);

            Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach), "Resting, and the lash is cooling.");
        }

        Assert.That(_events.Count<LaneArmed>(), Is.Zero);
    }

    // ---- JG-05c -----------------------------------------------------------------------------------

    [Test]
    public void HopGait_MatchesTheChasersOldArithmetic()
    {
        // Rule 1: the arithmetic ChaserBehaviour.TickChase carried inline until JG-05c, step for step.
        const float speed = 2f, hop = 0.5f, rest = 0.3f, dt = 0.1f;

        float timer = 0f, oldTimer = 0f;

        for (int i = 0; i < 40; i++)
        {
            float cycle = hop + rest;
            float phase = oldTimer % cycle;
            oldTimer = phase + dt;
            float expected = phase < hop ? speed * cycle / hop : 0f;

            Assert.That(HopGait.Speed(speed, hop, rest, ref timer, dt), Is.EqualTo(expected), $"Tick {i}.");
            Assert.That(timer, Is.EqualTo(oldTimer), $"Tick {i}'s timer.");
        }
    }

    [Test]
    public void HopGait_WalkerGetsItsSpeed()
    {
        float timer = 0.25f;

        Assert.That(HopGait.Speed(2f, 0f, 0.3f, ref timer, 0.1f), Is.EqualTo(2f));
        Assert.That(timer, Is.EqualTo(0.25f), "A walker's timer is not the gait's to touch.");
    }

    [Test]
    public void Approach_StartsInTheAir()
    {
        // Rule 2: every approach leaves at the top of a leap, at the speed that keeps the average.
        CroakerBehaviour croaker = Croaker(HopperId);

        Place(distance: CroakerBehaviour.SlamRange + 20f);

        TickUntil(croaker, CroakerState.Approach, 1f);

        _intents.Clear();
        Tick(croaker);

        float leap = CroakerMoveSpeed * (HopperHopTime + HopperRestTime) / HopperHopTime;

        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(leap).Within(Tolerance));
    }

    [Test]
    public void Approach_MovesOnlyInTheAir()
    {
        // Rule 2: a leap of HopTime, then HopRestTime sat still, and never a walk.
        CroakerBehaviour croaker = Croaker(HopperId);

        Place(distance: CroakerBehaviour.SlamRange + 20f);

        TickUntil(croaker, CroakerState.Approach, 1f);

        float leap = CroakerMoveSpeed * (HopperHopTime + HopperRestTime) / HopperHopTime;
        int moving = 0, still = 0, starts = 0;
        bool wasMoving = false;

        // Three whole cycles.
        int ticks = (int)MathF.Round(3f * (HopperHopTime + HopperRestTime) / Frame);

        for (int i = 0; i < ticks; i++)
        {
            _intents.Clear();
            Tick(croaker);

            float z = _intents.LastEnemyMove.Velocity.Z;

            Assert.That(
                MathF.Abs(z) < Tolerance || MathF.Abs(z - leap) < Tolerance,
                Is.True,
                $"Tick {i}: {z} is neither a leap nor a sit.");

            bool isMoving = z > Tolerance;

            moving += isMoving ? 1 : 0;
            still += isMoving ? 0 : 1;
            starts += isMoving && !wasMoving ? 1 : 0;
            wasMoving = isMoving;
        }

        Assert.That(starts, Is.EqualTo(3), "One takeoff per cycle.");
        Assert.That(moving * Frame, Is.EqualTo(3f * HopperHopTime).Within(3f * Frame), "In the air for HopTime each.");
        Assert.That(still * Frame, Is.EqualTo(3f * HopperRestTime).Within(3f * Frame), "Sat for HopRestTime each.");
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach));
    }

    [Test]
    public void Approach_FinishesTheLeap_ThenSlams()
    {
        // Rule 3: the player steps into slam range mid-leap. The frog lands first, then slams.
        CroakerBehaviour croaker = Croaker(HopperId);

        Place(distance: CroakerBehaviour.SlamRange + 20f);

        TickUntil(croaker, CroakerState.Approach, 1f);

        float takeoff = _clock;

        Tick(croaker);
        Tick(croaker);

        Place(distance: CroakerBehaviour.SlamRange - 1f);

        _intents.Clear();
        Tick(croaker);

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Approach), "Never a slam from mid-air.");
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.GreaterThan(0f), "The leap flies on.");

        float slammed = _clock;

        for (int i = 0; i < 20 && croaker.State != CroakerState.Slam; i++)
        {
            slammed = _clock;
            Tick(croaker);
        }

        Assert.That(croaker.State, Is.EqualTo(CroakerState.Slam));
        Assert.That(slammed - takeoff, Is.GreaterThanOrEqualTo(HopperHopTime - Tolerance), "Only after the landing.");
        Assert.That(slammed - takeoff, Is.LessThanOrEqualTo(HopperHopTime + (2f * Frame) + Tolerance), "And at once after it.");
    }

    [Test]
    public void Approach_InRangeOnTheGround_SlamsAsBefore()
    {
        // Rule 4: on the ground and in range, nothing changed. It stands and slams on the first tick.
        CroakerBehaviour croaker = Croaker(HopperId);

        Place(distance: CroakerBehaviour.SlamRange - 1f);

        TickUntil(croaker, CroakerState.Approach, 1f);

        _intents.Clear();
        Tick(croaker);

        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(croaker.State, Is.EqualTo(CroakerState.Slam));
        Assert.That(CroakerBehaviour.SlamRange, Is.EqualTo(8f), "Ranges and cooldowns keep their values.");
        Assert.That(CroakerBehaviour.SlamCooldown, Is.EqualTo(8f));
        Assert.That(CroakerBehaviour.LashCooldown, Is.EqualTo(8f));
        Assert.That(CroakerBehaviour.AttackGapSeconds, Is.EqualTo(2.5f));
    }

    // ---- Rule 7 -----------------------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, ProjectileCapacity);
        var shockwaves = new ShockwaveSystem(silent);
        var fissures = new FissureSystem(silent);
        var lanes = new LaneSystem(silent);
        var enemies = new EnemySystem(
            Catalog(),
            silent,
            new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()),
            Capacity);

        EnemyAgent agent = enemies.Spawn(new ContentId(CroakerEnemyId), Vector3.Zero);

        var croaker = new CroakerBehaviour(agent, shockwaves, fissures, lanes, new FixedRandom(0.1f).Misc);

        // Inside the slam's range and at the lash's minimum, so the cycle runs both attacks.
        Place(agent, distance: 3f);

        for (int i = 0; i < 600; i++)
        {
            intents.Clear();

            croaker.Tick(new EnemyTickContext(
                Frame, i * Frame, player, intents, silent, projectiles, enemies));

            // The hazard tables drain as the run's would, so every slam is a full one.
            shockwaves.Tick(i * Frame, new Vector3(50f, 0f, 50f), player);
            fissures.Tick(i * Frame, new Vector3(50f, 0f, 50f), player);
            lanes.Tick(i * Frame, new Vector3(50f, 0f, 50f), player);
        }

        Assert.That(croaker.HopCount, Is.EqualTo(1), "Sanity: the cycle has slammed.");
        Assert.That(croaker.NextLashAt, Is.GreaterThan(0f), "Sanity: and lashed.");

        _allocationClock = 600 * Frame;

        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;
            _allocationStep++;

            croaker.Tick(new EnemyTickContext(
                Frame, _allocationClock, player, intents, silent, projectiles, enemies));

            shockwaves.Tick(_allocationClock, new Vector3(50f, 0f, 50f), player);
            fissures.Tick(_allocationClock, new Vector3(50f, 0f, 50f), player);
            lanes.Tick(_allocationClock, new Vector3(50f, 0f, 50f), player);
        });

        Assert.That(_allocationStep, Is.GreaterThan(10_000), "The probe is live.");
    }

    // ---- Why this shape ---------------------------------------------------------------------------

    [Test]
    public void EnemyTelegraph_MoveDefaultsToZero()
    {
        Assert.That(new EnemyTelegraph(1, 0.4f).Move, Is.Zero, "Every existing publisher sends 0.");
        Assert.That(new EnemyTelegraph(1, 0.4f, 3).Move, Is.EqualTo(3));
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    /// <summary>A Croaker on an ordinary agent wearing the boss's body, driven bare: phase 0.</summary>
    private CroakerBehaviour Croaker(string bodyId = CroakerEnemyId)
    {
        _driven = _enemies.Spawn(new ContentId(bodyId), Vector3.Zero);

        return new CroakerBehaviour(_driven, _shockwaves, _fissures, _lanes, _random.Misc);
    }

    private EnemyAgent SpawnBoss()
    {
        _driven = _enemies.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));

        return _driven;
    }

    private static CroakerBehaviour InnerOf(EnemyAgent boss) =>
        (CroakerBehaviour)((BossBehaviour)boss.Behaviour).Inner;

    private void Cross(EnemyAgent boss, float fraction)
    {
        float target = boss.Health.MaxHp.Value * fraction;

        _enemies.ApplyDamage(boss.Id, boss.Health.Current - target, _clock, _player);

        TickWorld();
    }

    /// <summary>
    /// Runs the world from a crossing through the beat, the croak and one whole slam, and answers
    /// the clock at each hop's take-off.
    /// </summary>
    private List<float> RunOneSlam(CroakerBehaviour croaker)
    {
        _events.Clear();

        var armed = new List<float>();
        bool slammed = false;

        for (int i = 0; i < 200; i++)
        {
            int before = _events.Count<FissureArmed>();

            TickWorld();

            if (_events.Count<FissureArmed>() > before)
            {
                armed.Add(_clock);
            }

            slammed |= croaker.State == CroakerState.Slam;

            if (slammed && croaker.State == CroakerState.Recover)
            {
                return armed;
            }
        }

        Assert.Fail("The slam never ran out.");

        return armed;
    }

    /// <summary>Take-off to take-off is a hop's telegraph plus the gap between landings.</summary>
    private static void AssertHopSpacing(List<float> armed)
    {
        float spacing = CroakerBehaviour.SlamTelegraphSeconds + CroakerBehaviour.HopGapSeconds;

        for (int i = 1; i < armed.Count; i++)
        {
            Assert.That(
                armed[i] - armed[i - 1],
                Is.InRange(spacing - Tolerance, spacing + (2f * Frame) + Tolerance),
                "0.4 s between landings.");
        }
    }

    private int SlamTelegraphs()
    {
        IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();

        var count = 0;

        for (int i = 0; i < telegraphs.Count; i++)
        {
            if (telegraphs[i].Move == CroakerBehaviour.SlamMove)
            {
                count++;
            }
        }

        return count;
    }

    private void Place(float distance, Vector3? playerPosition = null) =>
        Place(_driven, distance, playerPosition);

    private static void Place(EnemyAgent agent, float distance, Vector3? playerPosition = null)
    {
        EnemyBlackboard blackboard = agent.Blackboard;

        Vector3 player = playerPosition ?? (agent.Position + new Vector3(0f, 0f, distance));

        blackboard.SelfPosition = agent.Position;
        blackboard.PlayerPosition = player;
        blackboard.DistanceToPlayer = distance;
        blackboard.DirectionToPlayer = new Vector2(0f, 1f);
        blackboard.HasLineOfSight = true;
    }

    private void Tick(CroakerBehaviour croaker)
    {
        croaker.Tick(Context());

        _clock += Frame;
    }

    private void TickUntil(CroakerBehaviour croaker, CroakerState state, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames && croaker.State != state; i++)
        {
            Tick(croaker);
        }

        Assert.That(croaker.State, Is.EqualTo(state), $"Never reached {state} within {seconds} s.");
    }

    private void RunOutTheSlam(CroakerBehaviour croaker)
    {
        TickUntil(croaker, CroakerState.Recover, 2f);
        TickUntil(croaker, CroakerState.Approach, CroakerBehaviour.RecoverSeconds + (2f * Frame));
    }

    private CroakerBehaviour EnterLash()
    {
        CroakerBehaviour croaker = Croaker();

        Place(distance: 6f);
        TickUntil(croaker, CroakerState.Slam, 1f);
        RunOutTheSlam(croaker);
        Place(distance: 6f);
        TickUntil(croaker, CroakerState.Lash, CroakerBehaviour.AttackGapSeconds + (2f * Frame));

        return croaker;
    }

    /// <summary>
    /// One tick of the arena in <c>RunSession</c>'s order. The hazards are ticked against the
    /// player's own position, which is the origin — well clear of every circle and ring here.
    /// </summary>
    private void TickWorld()
    {
        _enemies.Tick(Context());

        _shockwaves.Tick(_clock, _player.Blackboard.PlayerPosition, _player);
        _fissures.Tick(_clock, _player.Blackboard.PlayerPosition, _player);
        _lanes.Tick(_clock, _player.Blackboard.PlayerPosition, _player);

        _clock += Frame;
    }

    private void TickWorldFor(float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            TickWorld();
        }
    }

    private EnemyTickContext Context() => new EnemyTickContext(
        Frame, _clock, _player, _intents, _events, _projectiles, _enemies);

    private int LivingHusks()
    {
        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;

        var husk = new ContentId(HuskId);
        var count = 0;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].Spec.Id == husk && agents[i].IsAlive)
            {
                count++;
            }
        }

        return count;
    }

    private EnemySystem NewEnemies() => new EnemySystem(
        Catalog(),
        _events,
        new FixedRandom(0.1f),
        new DepthScaling(Scalings.Design()),
        Capacity,
        (agent, _) => new CroakerBehaviour(agent, _shockwaves, _fissures, _lanes, _random.Misc));

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), CroakerBody(), HopperBody() },
        modes: null,
        skills: null,
        trees: null,
        bosses: new[] { CroakerBoss() });

    private static BossSpec CroakerBoss() => new BossSpec(
        new ContentId(BossId),
        new ContentId(CroakerEnemyId),
        new[]
        {
            new BossPhaseSpec(1f),
            new BossPhaseSpec(
                SecondPhase,
                new[] { new AddWave(new ContentId(HuskId), SecondPhaseHusks) }),
            new BossPhaseSpec(ThirdPhase),
        },
        Beat);

    private static EnemySpec CroakerBody() => new EnemySpec(
        new ContentId(CroakerEnemyId),
        new LocKey("enemy.croaker.name"),
        3000f,
        CroakerMoveSpeed,
        8,
        threatCost: 40,
        xpValue: 300f,
        isElite: false,
        CroakerContactDamage,
        2.5f,
        0.9f,
        1.2f,
        aggroRange: CroakerAggro,
        EnemyBehaviourKind.Boss);

    /// <summary>The boss's body with <c>Croaker.asset</c>'s gait (JG-05c); the plain one walks.</summary>
    private static EnemySpec HopperBody() => new EnemySpec(
        new ContentId(HopperId),
        new LocKey("enemy.croaker.name"),
        3000f,
        CroakerMoveSpeed,
        8,
        threatCost: 40,
        xpValue: 300f,
        isElite: false,
        CroakerContactDamage,
        2.5f,
        0.9f,
        1.2f,
        aggroRange: CroakerAggro,
        EnemyBehaviourKind.Boss,
        hopTime: HopperHopTime,
        hopRestTime: HopperRestTime);

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
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

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
