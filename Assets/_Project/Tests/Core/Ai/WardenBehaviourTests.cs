using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// The Warden of Ash choosing what to do and doing it: which attack is legal from where, how long
/// each telegraphs for, where the ring comes from, where the crack goes, and what the whole of it
/// costs per frame. GD §9.1 rules 1, 2, 4 and 7, GD §9.2, M4-02 rules 1, 2, 3, 6, 7 and 8.
/// </summary>
/// <remarks>
/// <para>
/// <b>The boss stands up through the door a real stage uses</b> — <c>EnemySystem.SpawnBoss</c> —
/// and its attacks arrive as the <c>inner</c> of a <see cref="BossBehaviour"/>, through the
/// boss-behaviour factory <c>RunSession</c> hands the census. <b>A fixture cannot build that inner
/// itself, and that is a property of the code rather than of the fixture:</b> every
/// <see cref="IEnemyBehaviour"/> holds the agent it drives, and the agent does not exist until
/// <c>SpawnBoss</c> has spawned one. So the rows that need a phase machine wire the factory the
/// way the run does, which is also the only way the wiring gets proved at all.
/// </para>
/// <para>
/// <b>The rows about selection drive the behaviour directly instead</b>, on an ordinary agent
/// wearing the boss's body — <c>SpitterBehaviourTests</c>' shape, and its reason: what is being
/// measured is the choice, and a phase machine above it would add a beat and a summon to every
/// assertion.
/// </para>
/// <para>
/// <b>Against real hazard systems throughout</b>, never fakes — <c>ZoneSystemTests</c>' bargain:
/// half of what is asserted here is that a ring exists with the right origin, and a fake would
/// agree with whatever the behaviour told it. What those systems then <em>do</em> with a ring is
/// <c>ShockwaveAndFissureTests</c>'.
/// </para>
/// <para>
/// <b>The adds are authored <c>Static</c></b>, <c>BossPhasesTests</c>' choice and its reason:
/// nothing here is about what a Husk does, and a Chaser would add its own telegraphs to every
/// assertion about what the boss published.
/// </para>
/// <para>
/// <b>Two ticks to reach an attack, always, and it is the machine's rule rather than a quirk.</b>
/// <see cref="Soulvail.Core.Common.StateMachine{TState}"/> defers a transition requested from
/// inside a handler until that handler returns, so the tick that decides is never the tick that
/// acts. Every row here goes through <see cref="TickUntil"/> rather than counting frames.
/// </para>
/// </remarks>
[TestFixture]
public sealed class WardenBehaviourTests
{
    private const string BossId = "boss.warden";
    private const string WardenEnemyId = "enemy.warden";
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";

    // ---- Warden.asset, exactly as it ships. A retune reddens the rows that describe it. ----------
    private const float WardenMaxHp = 4200f;
    private const float WardenMoveSpeed = 1.8f;
    private const float WardenContactDamage = 22f;
    private const float WardenWindup = 0.9f;
    private const float WardenRecover = 1.2f;
    private const float WardenAggro = 40f;

    // ---- WardenBoss.asset, exactly as it ships. ---------------------------------------------------
    private const float Beat = 1.5f;
    private const float SecondPhase = 0.66f;
    private const float ThirdPhase = 0.33f;
    private const int SecondPhaseHusks = 3;
    private const int ThirdPhaseHusks = 4;

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
    private FixedRandom _random;
    private EnemySystem _enemies;
    private EnemyAgent _driven;
    private float _clock;

    /// <summary>
    /// The counters the allocation row drives its clock from. Fields rather than locals, so the
    /// measured closure is built over them once instead of capturing fresh variables — a closure
    /// created inside the measurement would be the allocation it reported.
    /// </summary>
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

        // Always slam when the choice is real, unless a row scripts otherwise — so a row about the
        // ring is about the ring rather than about a coin.
        _random = new FixedRandom().SetMisc(0f);

        _enemies = NewEnemies();
        _clock = 0f;
        _allocationClock = 0f;
        _allocationStep = 0;
    }

    // ---- Rule 1: every attack telegraphs, and the number is authored ------------------------------

    [Test]
    public void Warden_EveryAttackTelegraphsLongEnough()
    {
        // **The shipped numbers clear GD §9.1 rule 1's floor**, which is the half of the claim this
        // assembly can make — the other half reads `Warden.asset` off disk and lives in
        // `ContentValidationTests.Boss_EveryAttackTelegraphsLongEnough`, because `Soulvail.Core`
        // references no Unity assembly and so cannot load an asset.
        Assert.That(WardenWindup, Is.GreaterThanOrEqualTo(WardenBehaviour.MinTelegraphSeconds));

        Assert.That(
            Warden().TelegraphSeconds,
            Is.EqualTo(WardenWindup).Within(Tolerance),
            "Both attacks telegraph for the body's authored WindupTime.");

        // **And it is authored rather than constant** (rule 1). A body with a *longer* wind-up
        // telegraphs longer, which a hard-coded 0.6 or 0.9 could not do.
        Assert.That(Warden(windup: 1.4f).TelegraphSeconds, Is.EqualTo(1.4f).Within(Tolerance));

        // **And the floor holds whatever an asset says**, which is the other direction and the one
        // that makes rule 1 structural. A clamp rather than a refusal: see WardenBehaviour's class
        // remarks for why a hazard that threw here would be worse than either.
        Assert.That(
            Warden(windup: 0.3f).TelegraphSeconds,
            Is.EqualTo(WardenBehaviour.MinTelegraphSeconds).Within(Tolerance),
            "GD §9.1 rule 1 is marked non-negotiable, so it is a floor in code.");
    }

    [Test]
    public void Warden_TelegraphsBothAttacksForTheAuthoredWindow()
    {
        // The same EnemyTelegraph a Husk publishes, so M1-12's swell draws it with nothing added —
        // and it carries the window the player actually gets rather than the raw authored number.
        WardenBehaviour warden = InRange();

        TickUntil(warden, WardenState.Slam, 1f);

        Assert.That(
            _events.Single<EnemyTelegraph>().Duration,
            Is.EqualTo(WardenWindup).Within(Tolerance));

        // Out to where only a fissure is legal, then let the slam land. The events are cleared at
        // the far side of it rather than before, because the fissure cooldown has never been spent
        // and the boss would otherwise have chosen one before the clear ran.
        Place(warden, distance: 12f);

        TickUntil(warden, WardenState.Recover, 3f);

        _events.Clear();

        TickUntil(warden, WardenState.Fissure, 3f);

        Assert.That(
            _events.Count<EnemyTelegraph>(),
            Is.EqualTo(1),
            "A fissure announces itself too — the crack on the floor is the other half of it.");

        Assert.That(
            _events.Single<EnemyTelegraph>().Duration,
            Is.EqualTo(WardenWindup).Within(Tolerance));
    }

    // ---- Rule 2: the ring leaves the ground it stood on -------------------------------------------

    [Test]
    public void Slam_RingLeavesTheGroundItStoodOn()
    {
        // The spec's row: the boss slams, then walks five metres, and the ring's centre is still
        // the slam point. The origin is taken at the instant the telegraph ended and is never
        // revised — a ring that followed the body would be inescapable by walking, which is what
        // GD §9.1 rule 2 forbids.
        var slamPoint = new Vector3(0f, 0f, 6f);

        WardenBehaviour warden = Warden(at: slamPoint);

        Place(warden, distance: 2.5f);

        TickUntil(warden, WardenState.Recover, 3f);

        Assert.That(
            _shockwaves.ActiveCount,
            Is.EqualTo(1),
            "One ring, on the tick the telegraph ended rather than one frame later.");

        Assert.That(_shockwaves.OriginAt(0), Is.EqualTo(slamPoint));

        // Five metres, the hard way: the body is reported somewhere else on the next snapshot —
        // which is the only way a position ever changes in this architecture, core being told
        // rather than deciding — and the ring is ticked again.
        Move(slamPoint + new Vector3(0f, 0f, -5f));

        Assert.That(_driven.Position.Z, Is.EqualTo(1f).Within(Tolerance), "It really did walk.");

        _shockwaves.Tick(_clock, Vector3.Zero, _player);

        Assert.That(_shockwaves.OriginAt(0), Is.EqualTo(slamPoint));

        Assert.That(
            _events.Single<ShockwaveEmitted>().Origin,
            Is.EqualTo(slamPoint),
            "And the event said so at the time.");
    }

    [Test]
    public void Slam_CostsTheBodysOwnDamage()
    {
        // The agent's ContactDamage *stat* rather than the spec's float, so GD §12.3's d(n) and
        // M7-02's affixes are already in a ring without a second mechanism — SpitterBehaviour's
        // rule for a thrown shot, applied to a hazard.
        WardenBehaviour warden = InRange();

        _driven.ContactDamage.Add(new Modifier(ModifierKind.PercentAdd, 0.5f, this));

        TickUntil(warden, WardenState.Recover, 3f);

        float before = _player.Health.Current + _player.Health.Shield;

        // Well past the ring's ceiling, with the player standing on the slam point.
        Vector3 slamPoint = _shockwaves.OriginAt(0);

        for (int i = 1; i <= 120; i++)
        {
            _shockwaves.Tick(_clock + (i * Frame), slamPoint, _player);
        }

        Assert.That(
            before - (_player.Health.Current + _player.Health.Shield),
            Is.EqualTo(WardenContactDamage * 1.5f).Within(Tolerance));
    }

    // ---- Rule 3: the crack opens where the player was ---------------------------------------------

    [Test]
    public void Fissure_OpensUnderThePlayerAndDoesNotTrack()
    {
        // Out of slam range, so the fissure is the only legal attack and the row is about it
        // rather than about a coin.
        WardenBehaviour warden = Warden();

        var playerAt = new Vector3(-4f, 0f, 9f);

        Place(warden, distance: 12f, playerPosition: playerAt);

        TickUntil(warden, WardenState.Fissure, 1f);

        Assert.That(
            _fissures.ActiveCount,
            Is.EqualTo(1),
            "Opened on the way in, because the arm *is* the telegraph — a boss that wound up "
                + "first would telegraph twice for one attack.");

        Assert.That(_fissures.PositionAt(0), Is.EqualTo(playerAt));

        FissureArmed armed = _events.Single<FissureArmed>();

        Assert.That(armed.At, Is.EqualTo(playerAt));
        Assert.That(armed.ArmSeconds, Is.EqualTo(WardenWindup).Within(Tolerance));
        Assert.That(armed.Radius, Is.EqualTo(WardenBehaviour.FissureRadius).Within(Tolerance));

        // The player walks off during the arm, which is the only way it could have tracked.
        Place(warden, distance: 12f, playerPosition: new Vector3(20f, 0f, -20f));

        TickFor(warden, WardenWindup + Frame);

        Assert.That(_fissures.PositionAt(0), Is.EqualTo(playerAt), "It stayed where it opened.");
    }

    // ---- Rule 7: selection is cooldown, range, and one coin ---------------------------------------

    [Test]
    public void Slam_IsChosenOnlyInsideItsRange()
    {
        // Both attacks are off cooldown at the opening, so the only thing deciding is distance:
        // inside SlamRange the coin runs, outside it the fissure is the whole answer. Which is
        // also why a Warden cannot be starved by standing off — rule 7's second half.
        WardenBehaviour close = Warden();

        Place(close, distance: WardenBehaviour.SlamRange - 0.5f);

        TickUntil(close, WardenState.Slam, 1f);

        SetUp();

        WardenBehaviour far = Warden();

        Place(far, distance: WardenBehaviour.SlamRange + 0.5f);

        TickUntil(far, WardenState.Fissure, 1f);

        Assert.That(
            _shockwaves.ActiveCount,
            Is.Zero,
            "Out of slam range the crack is the answer — a boss with nothing to do at 10 m is a "
                + "fight won by standing off.");
    }

    [Test]
    public void Selection_IsDeterministic()
    {
        // **The same seed and the same state give the same order, twice.** The draw happens only
        // where both attacks would have been legal, so what is pinned is the whole of selection:
        // the two cooldowns, the range test, and the one coin between them.
        string first = AttackOrder(0.9f, 0.1f, 0.9f, 0.1f);
        string second = AttackOrder(0.9f, 0.1f, 0.9f, 0.1f);

        Assert.That(second, Is.EqualTo(first));

        Assert.That(
            first,
            Does.Contain("Slam").And.Contain("Fissure"),
            "The fixture's own claim: the script really did reach both branches, so this row is "
                + "about a sequence rather than about one attack repeated.");

        // And a different script gives a different order, which is what stops the row above being
        // true of a behaviour that ignores the stream entirely.
        Assert.That(AttackOrder(0.1f, 0.9f, 0.1f, 0.9f), Is.Not.EqualTo(first));
    }

    [Test]
    public void Selection_UsesNoEngineRandom()
    {
        // **The standing purity row, extended to the one class in the project that picks between
        // two attacks.** `Soulvail.Core` cannot name `UnityEngine.Random`, because it references
        // no Unity assembly at all — asserted here against the assembly `WardenBehaviour` actually
        // lives in rather than inherited from `AssemblyPurityTests`' `StateMachine<>`.
        foreach (AssemblyName reference in typeof(WardenBehaviour).Assembly.GetReferencedAssemblies())
        {
            Assert.That(
                reference.Name,
                Does.Not.StartWith("UnityEngine").And.Not.StartWith("UnityEditor"),
                $"Soulvail.Core references {reference.Name}.");
        }

        // And the half that is this class's rather than the assembly's: the draw comes through the
        // port, handed in, so a run's own generator is the only thing that can move it (ADR-0011).
        ConstructorInfo constructor = typeof(WardenBehaviour).GetConstructors()[0];

        Assert.That(
            Array.Exists(constructor.GetParameters(), p => p.ParameterType == typeof(IRandomStream)),
            Is.True,
            "Nothing in core holds a generator it was not given.");
    }

    [Test]
    public void Warden_RecoversBeforeItAttacksAgain()
    {
        // The window GD §9.2's hook is about — "attack from behind" needs a moment in which the
        // boss is doing nothing — and the number is the body's authored RecoverTime rather than a
        // constant here.
        WardenBehaviour warden = InRange();

        TickUntil(warden, WardenState.Recover, 3f);

        TickFor(warden, WardenRecover - (2f * Frame));

        Assert.That(warden.State, Is.EqualTo(WardenState.Recover), "Still rooted.");

        // Ticked until it leaves rather than for a counted number of frames: the window in
        // Approach is exactly one tick wide — the next one chooses an attack — and counting
        // twelve additions of 0.1f onto a 1.2 s deadline is a coin toss besides.
        TickUntil(warden, WardenState.Approach, 1f);
    }

    [Test]
    public void Warden_ClosesWhenItCannotAttack()
    {
        // Both cooldowns running and the player out of reach: it walks, at the agent's *stat*
        // speed rather than the spec's float, along the direction the blackboard gives it.
        WardenBehaviour warden = InRange();

        RunOutTheAttack(warden);

        // Spend the fissure too, so neither attack is legal.
        Place(warden, distance: 12f);

        TickUntil(warden, WardenState.Fissure, 2f);

        RunOutTheAttack(warden);

        Place(warden, distance: 20f);

        _intents.Clear();

        Tick(warden);

        Assert.That(warden.State, Is.EqualTo(WardenState.Approach), "Nothing is legal from here.");

        EnemyMoveIntent moved = _intents.LastEnemyMove;

        Assert.That(moved.Velocity.Z, Is.EqualTo(WardenMoveSpeed).Within(Tolerance));
        Assert.That(moved.Velocity.Y, Is.Zero, "A walk is flat.");
    }

    [Test]
    public void Warden_EmitsExactlyOneIntentPerTick()
    {
        // IEnemyBehaviour.Tick's contract, in every state including the standing ones: EnemyView
        // folds gravity into the same Move that carries the walk, so a tick with no intent is a
        // tick this body is not pinned to the floor by.
        WardenBehaviour warden = InRange();

        var seen = 0;
        WardenState previous = warden.State;

        // Long enough to pass through every one of the five states at least once.
        for (int i = 0; i < 120; i++)
        {
            _intents.Clear();

            Tick(warden);

            Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(1), $"One intent in {warden.State}.");

            if (warden.State != previous)
            {
                seen++;
                previous = warden.State;
            }
        }

        Assert.That(seen, Is.GreaterThanOrEqualTo(4), "The fixture's own claim: the walk really "
            + "did pass through the states rather than sitting in one.");
    }

    // ---- Rule 6: what its phases author, through M4-01b's path ------------------------------------

    [Test]
    public void Warden_SummonsWhatItsPhasesAuthor()
    {
        // **The mechanism is M4-01b's and is not re-implemented** (rule 6). What this row asserts
        // is the *content*: WardenBoss.asset authors three Husks at 66 % and four at 33 %, and the
        // bodies that appear are ordinary agents in the same registry, spawned through the same
        // door and worth the same experience.
        EnemyAgent boss = SpawnBoss();

        Assert.That(LivingHusks(), Is.Zero, "The opening phase summons nothing.");

        Cross(boss, SecondPhase - 0.01f);

        Assert.That(LivingHusks(), Is.EqualTo(SecondPhaseHusks));

        // Out the other side of the beat — it cannot be hurt during one — then across the second
        // threshold.
        TickWorldFor(Beat + (2f * Frame));

        Assert.That(boss.IsVulnerable, Is.True, "The beat is over.");

        Cross(boss, ThirdPhase - 0.01f);

        Assert.That(
            LivingHusks(),
            Is.EqualTo(ThirdPhaseHusks),
            "The last phase's four, and the middle phase's three are gone — the beat clears what "
                + "it called in before it calls the next lot in (M4-01b rule 4).");
    }

    [Test]
    public void Warden_DoesNotActDuringTheBeat()
    {
        // Inherited rather than re-implemented, and pinned because this is the first `inner`
        // behaviour there has ever been: `BossBehaviour` returns before it delegates, so there is
        // no beat check anywhere in `WardenBehaviour` and there must never need to be.
        EnemyAgent boss = SpawnBoss();

        Place(boss, distance: 2f);

        // Crossed before it has acted at all, so both cooldowns are still at zero: anything ticked
        // during the beat *would* attack.
        Cross(boss, SecondPhase - 0.01f);

        _events.Clear();

        TickWorldFor(Beat - (2f * Frame));

        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero, "Nothing wound up.");
        Assert.That(_shockwaves.ActiveCount, Is.Zero, "No ring left the ground.");
        Assert.That(_fissures.ActiveCount, Is.Zero, "And no crack opened.");

        // The counter-claim, which is what stops the three above being true of a boss that never
        // attacks at all: past the beat, it does.
        TickWorldFor(Beat);

        Assert.That(_events.Count<EnemyTelegraph>(), Is.GreaterThan(0));
    }

    // ---- Rule 8: a full table costs the attack rather than the run --------------------------------

    [Test]
    public void Slam_SurvivesAFullHazardTable()
    {
        // The behaviour's side of rule 8: `Emit` answers 0 and this does not look, which is
        // `ProjectileSystem.Fire`'s bargain — the Warden believes it slammed. The cooldown is
        // spent either way, so a full table costs the boss the attack rather than making it spam
        // one.
        for (int i = 0; i < ShockwaveSystem.Capacity; i++)
        {
            _shockwaves.Emit(Vector3.Zero, 1f, 500f, 1f, 1f, 0f);
        }

        WardenBehaviour warden = InRange();

        Assert.DoesNotThrow(() => TickUntil(warden, WardenState.Recover, 3f));

        Assert.That(warden.NextSlamAt, Is.GreaterThan(0f), "And it paid the cooldown.");
        Assert.That(_shockwaves.ActiveCount, Is.EqualTo(ShockwaveSystem.Capacity));
    }

    // ---- The budget -------------------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        // A silent sink on both ends: RecordingEvents boxes every payload, so the telegraphs and
        // the hazard events this cycle publishes would be counted as core allocating when it is
        // the fake doing it.
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, ProjectileCapacity);
        var shockwaves = new ShockwaveSystem(silent);
        var fissures = new FissureSystem(silent);
        var enemies = new EnemySystem(
            Catalog(),
            silent,
            new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()),
            Capacity);

        EnemyAgent agent = enemies.Spawn(new ContentId(WardenEnemyId), Vector3.Zero);

        var warden = new WardenBehaviour(
            agent, shockwaves, fissures, new FixedRandom(0.1f).Misc);

        Place(agent, distance: 3f);

        // Warm-up: every one of the machine's transitions — the part of a state machine that
        // touches a dictionary at all — has run before anything is measured, which is
        // SpitterBehaviourTests' care and its reason.
        for (int i = 0; i < 600; i++)
        {
            intents.Clear();

            warden.Tick(new EnemyTickContext(
                Frame, i * Frame, player, intents, silent, projectiles, enemies));
        }

        Assert.That(warden.State, Is.Not.EqualTo(WardenState.Idle), "Sanity: the cycle is running.");

        _allocationClock = 600 * Frame;

        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;
            _allocationStep++;

            warden.Tick(new EnemyTickContext(
                Frame, _allocationClock, player, intents, silent, projectiles, enemies));
        });

        Assert.That(_allocationStep, Is.GreaterThan(10_000), "The probe is live.");
    }

    // ---- Guards -----------------------------------------------------------------------------------

    [Test]
    public void Warden_Guards()
    {
        EnemyAgent agent = _enemies.Spawn(new ContentId(HuskId), Vector3.Zero);

        Assert.Throws<ArgumentNullException>(
            () => new WardenBehaviour(null, _shockwaves, _fissures, _random.Misc));
        Assert.Throws<ArgumentNullException>(
            () => new WardenBehaviour(agent, null, _fissures, _random.Misc));
        Assert.Throws<ArgumentNullException>(
            () => new WardenBehaviour(agent, _shockwaves, null, _random.Misc));
        Assert.Throws<ArgumentNullException>(
            () => new WardenBehaviour(agent, _shockwaves, _fissures, null));
    }

    [Test]
    public void Warden_ResetPutsItBackWhereItStarted()
    {
        // What a recycled agent gets instead of a new behaviour (M2-07b rule 4). The cooldowns go
        // with the state: the first thing a respawned boss meets is an arena it was just put into
        // at the director's clearance, well beyond SlamRange.
        WardenBehaviour warden = InRange();

        TickUntil(warden, WardenState.Recover, 3f);

        Assert.That(warden.NextSlamAt, Is.GreaterThan(0f));

        warden.Reset();

        Assert.That(warden.State, Is.EqualTo(WardenState.Idle));
        Assert.That(warden.NextSlamAt, Is.Zero);
        Assert.That(warden.NextFissureAt, Is.Zero);
    }

    [Test]
    public void Warden_IsIdleUntilThePlayerIsWithinAggro()
    {
        WardenBehaviour warden = Warden();

        Place(warden, distance: WardenAggro + 1f);

        TickFor(warden, 1f);

        Assert.That(warden.State, Is.EqualTo(WardenState.Idle));
        Assert.That(_fissures.ActiveCount, Is.Zero, "An unaware boss opens nothing.");

        Place(warden, distance: WardenAggro - 1f);

        TickFor(warden, Frame);

        Assert.That(warden.State, Is.Not.EqualTo(WardenState.Idle));
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    /// <summary>
    /// A Warden on an ordinary agent wearing the boss's body, driven directly rather than through
    /// a phase machine. See the class remarks for why the two halves of this fixture differ.
    /// </summary>
    private WardenBehaviour Warden(float windup = WardenWindup, Vector3 at = default)
    {
        _enemies = NewEnemies(windup);

        // Authored `Boss`, so `EnemyAgent.Initialise` deliberately leaves it with no behaviour at
        // all (M4-01b) — which is what lets this hold the inner and drive it by hand.
        _driven = _enemies.Spawn(new ContentId(WardenEnemyId), at);

        return new WardenBehaviour(_driven, _shockwaves, _fissures, _random.Misc);
    }

    /// <summary>
    /// Reports the driven body at <paramref name="to"/> on the next snapshot.
    /// </summary>
    /// <remarks>
    /// The only way a position ever changes in this architecture: Unity reports where a body ended
    /// up and core writes it down (AR §3). Nothing in the test assembly can assign one, which is
    /// what <c>EnemyAgent.Position</c>'s <c>internal set</c> is for.
    /// </remarks>
    private void Move(Vector3 to)
    {
        var snapshot = new WorldSnapshot(Capacity);

        snapshot.Clear();
        snapshot.Dt = Frame;
        snapshot.PlayerPosition = _player.Blackboard.PlayerPosition;

        ref EnemySense sense = ref snapshot.AddEnemy();

        sense.Id = _driven.Id;
        sense.Position = to;
        sense.Velocity = Vector3.Zero;
        sense.HasLineOfSight = true;

        _enemies.Ingest(snapshot);
    }

    /// <summary>A Warden with the player already inside <see cref="WardenBehaviour.SlamRange"/>.</summary>
    private WardenBehaviour InRange()
    {
        WardenBehaviour warden = Warden();

        Place(warden, distance: 2.5f);

        return warden;
    }

    /// <summary>
    /// The boss standing up through the door a real stage uses, with its attacks arriving as the
    /// <c>inner</c> the run's factory builds.
    /// </summary>
    private EnemyAgent SpawnBoss()
    {
        _driven = _enemies.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));

        return _driven;
    }

    /// <summary>Drives the boss's health to <paramref name="fraction"/> and ticks the world once.</summary>
    private void Cross(EnemyAgent boss, float fraction)
    {
        float target = boss.Health.MaxHp.Value * fraction;

        // Through the census, which is the one door damage reaches an enemy through — and the one
        // that a beat's invulnerability actually turns away.
        _enemies.ApplyDamage(boss.Id, boss.Health.Current - target, _clock, _player);

        TickWorld();
    }

    private void Place(WardenBehaviour warden, float distance, Vector3? playerPosition = null) =>
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

    /// <summary>One tick at <see cref="Frame"/>, advancing the fixture's clock with it.</summary>
    /// <remarks>
    /// The clock is a field shared by every helper here rather than a counter restarted per call,
    /// because a hazard stamps <c>Health</c>'s i-frames with it: a fixture that replayed the same
    /// seconds on each helper would have every hit after the first blocked by the one before it.
    /// </remarks>
    private void Tick(WardenBehaviour warden)
    {
        warden.Tick(Context());

        _clock += Frame;
    }

    private void TickFor(WardenBehaviour warden, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Tick(warden);
        }
    }

    /// <summary>Ticks until the behaviour reaches <paramref name="state"/>, or fails loudly.</summary>
    private void TickUntil(WardenBehaviour warden, WardenState state, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames && warden.State != state; i++)
        {
            Tick(warden);
        }

        Assert.That(warden.State, Is.EqualTo(state), $"Never reached {state} within {seconds} s.");
    }

    /// <summary>Runs the current attack out to the far side of its recovery.</summary>
    private void RunOutTheAttack(WardenBehaviour warden) =>
        TickFor(warden, warden.TelegraphSeconds + WardenRecover + (2f * Frame));

    /// <summary>One tick of the arena, in the order <c>RunSession</c> puts the two steps in.</summary>
    private void TickWorld()
    {
        _enemies.Tick(Context());

        _shockwaves.Tick(_clock, _player.Blackboard.PlayerPosition, _player);
        _fissures.Tick(_clock, _player.Blackboard.PlayerPosition, _player);

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

    /// <summary>
    /// The attacks a fresh Warden makes, in order, against a scripted <c>Misc</c> stream.
    /// </summary>
    /// <remarks>
    /// The player is parked inside <see cref="WardenBehaviour.SlamRange"/> so that every decision
    /// is a real one — outside it the coin would never be reached, and the row would prove nothing
    /// about the stream.
    /// </remarks>
    private string AttackOrder(params float[] draws)
    {
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), events, intents, Capacity);
        var projectiles = new ProjectileSystem(events, ProjectileCapacity);
        var shockwaves = new ShockwaveSystem(events);
        var fissures = new FissureSystem(events);
        var random = new FixedRandom().SetMisc(draws);

        var enemies = new EnemySystem(
            Catalog(),
            events,
            new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()),
            Capacity);

        EnemyAgent agent = enemies.Spawn(new ContentId(WardenEnemyId), Vector3.Zero);

        var warden = new WardenBehaviour(agent, shockwaves, fissures, random.Misc);

        Place(agent, distance: 2.5f);

        var order = string.Empty;
        WardenState previous = warden.State;

        // Long enough to run through both cooldowns several times, so the script is consumed
        // rather than half read.
        for (int i = 1; i <= 400; i++)
        {
            warden.Tick(new EnemyTickContext(
                Frame, i * Frame, player, intents, events, projectiles, enemies));

            if (warden.State != previous
                && (warden.State == WardenState.Slam || warden.State == WardenState.Fissure))
            {
                order += warden.State + " ";
            }

            previous = warden.State;
        }

        return order;
    }

    private EnemySystem NewEnemies(float windup = WardenWindup) => new EnemySystem(
        Catalog(windup),
        _events,
        new FixedRandom(0.1f),
        new DepthScaling(Scalings.Design()),
        Capacity,
        // The run's own wiring, in a fixture: the factory is the only way an `inner` can reach a
        // boss, because a behaviour holds the agent it drives.
        (agent, _) => new WardenBehaviour(agent, _shockwaves, _fissures, _random.Misc));

    private static ContentCatalog Catalog(float windup = WardenWindup) => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), WardenBody(windup) },
        modes: null,
        skills: null,
        trees: null,
        bosses: new[] { WardenBoss() });

    /// <summary><c>WardenBoss.asset</c>, exactly as it ships.</summary>
    private static BossSpec WardenBoss() => new BossSpec(
        new ContentId(BossId),
        new ContentId(WardenEnemyId),
        new[]
        {
            new BossPhaseSpec(1f),
            new BossPhaseSpec(
                SecondPhase,
                new[] { new AddWave(new ContentId(HuskId), SecondPhaseHusks) }),
            new BossPhaseSpec(
                ThirdPhase,
                new[] { new AddWave(new ContentId(HuskId), ThirdPhaseHusks) }),
        },
        Beat);

    /// <summary><c>Warden.asset</c>, exactly as it ships.</summary>
    private static EnemySpec WardenBody(float windup = WardenWindup) => new EnemySpec(
        new ContentId(WardenEnemyId),
        new LocKey("enemy.warden.name"),
        WardenMaxHp,
        WardenMoveSpeed,
        8,
        threatCost: 40,
        xpValue: 300f,
        isElite: false,
        WardenContactDamage,
        2.5f,
        windup,
        WardenRecover,
        aggroRange: WardenAggro,
        EnemyBehaviourKind.Boss);

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
