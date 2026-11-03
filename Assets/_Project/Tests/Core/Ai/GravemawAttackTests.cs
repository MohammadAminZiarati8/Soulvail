using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
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

/// <summary>JG-25c: each surfaced window runs one move, a tremor ring then a rock spit, alternating.</summary>
/// <remarks>
/// The boss stands at the origin and the quarry stands still at (0, 0, 3), so the ring arms and the
/// tremor leaves from (0, 0, 3). A window's move arms 0.4 s in and releases 0.8 s after that.
/// </remarks>
[TestFixture]
public sealed class GravemawAttackTests
{
    private const float Contact = 20f;
    private static readonly ContentId Frog = new("enemy.frog");
    private const float Frame = 1f / 64f;
    private static readonly Vector3 Centre = new(0f, 0f, 3f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private PlayerMotor _motor;
    private ProjectileSystem _projectiles;
    private ShockwaveSystem _shockwaves;
    private GravemawStats _stats;
    private GravemawBehaviour _gravemaw;
    private RunSession _session;
    private Vector3 _bossAt;
    private Vector3 _playerAt;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _motor = new PlayerMotor(Character().Movement, Vector3.UnitZ);
        _projectiles = new ProjectileSystem(_events, 8);
        _shockwaves = new ShockwaveSystem(_events);
        _stats = new GravemawStats();
        _gravemaw = new GravemawBehaviour(_agent, _stats, new FixedRandom(0.5f).Misc, _motor, _shockwaves, Frog);
        _session = null;
        _now = 0f;
        Place(Centre);
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [Test]
    public void Windows_Alternate_TremorThenSpit()
    {
        Assert.That(_gravemaw.NextMove, Is.EqualTo(GravemawMove.Tremor));
        Surface();
        Assert.That(_gravemaw.NextMove, Is.EqualTo(GravemawMove.Spit));
        RunWindow();
        Surface();
        RunWindow();
        Surface();
        RunWindow();
        Assert.That(MoveKinds(), Is.EqualTo(new[]
        {
            GravemawBehaviour.TremorMove, GravemawBehaviour.SpitMove, GravemawBehaviour.TremorMove,
        }));
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.EqualTo(2));
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(3), "One move a window: one volley.");
    }

    [Test]
    public void Reset_StartsAtTremor()
    {
        Surface();
        RunWindow();
        Assert.That(_gravemaw.NextMove, Is.EqualTo(GravemawMove.Spit));
        _gravemaw.Reset();
        Assert.That(_gravemaw.NextMove, Is.EqualTo(GravemawMove.Tremor));
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Tremor_AfterEruption_TelegraphsPointEight_ThenEmits()
    {
        Surface();
        Tick(0.25f);
        Assert.That(MoveKinds(), Is.Empty, "Not before 0.4 s.");
        Tick(0.25f);
        EnemyTelegraph telegraph = MoveTelegraphs()[0];
        Assert.That(telegraph.Move, Is.EqualTo(GravemawBehaviour.TremorMove));
        Assert.That(telegraph.Duration, Is.EqualTo(0.8f));
        Tick(0.75f);
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.Zero);
        Tick(0.125f);
        ShockwaveEmitted ring = _events.Single<ShockwaveEmitted>();
        Assert.That(ring.Origin, Is.EqualTo(Centre));
        Assert.That(ring.Speed, Is.EqualTo(7f));
        Assert.That(ring.MaxRadius, Is.EqualTo(9f));
        Assert.That(ring.Thickness, Is.EqualTo(1f));
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced), "The move never extends the window.");
    }

    [Test]
    public void Tremor_FullSystem_Dropped()
    {
        for (int i = 0; i < ShockwaveSystem.Capacity; i++)
        {
            _shockwaves.Emit(new Vector3(20f, 0f, 20f), 1f, 1f, 1f, 1f, 0f);
        }

        Surface();
        RunWindow();
        Assert.That(MoveKinds(), Is.EqualTo(new[] { GravemawBehaviour.TremorMove }));
        Assert.That(_shockwaves.ActiveCount, Is.EqualTo(ShockwaveSystem.Capacity));
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.EqualTo(ShockwaveSystem.Capacity), "Dropped, not queued.");
    }

    // ---- Rule 3 -------------------------------------------------------------------------------------

    [Test]
    public void Tremor_StandingInBand_Hurts()
    {
        SessionToTremor(new Vector3(0f, 0f, 8f));
        SessionFor(1f);
        Assert.That(Landed(), Is.EqualTo(1), "The eruption missed; the band at 5 m bit.");
        Assert.That(_events.Single<PlayerDamaged>().ToHp, Is.EqualTo(Contact));
    }

    [Test]
    public void Tremor_RollIFramesInBand_NoDamage()
    {
        SessionToTremor(new Vector3(0f, 0f, 8f));
        SessionFor(0.55f);
        _session.MovementSkill();
        SessionFor(1f);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1), "Sanity: the roll went out.");
        PlayerDamaged turned = _events.Single<PlayerDamaged>();
        Assert.That(turned.Blocked, Is.True, "The band reached the player inside the roll's i-frames.");
        Assert.That(Landed(), Is.Zero);
    }

    [Test]
    public void Tremor_OutsideMaxRadius_NoDamage()
    {
        SessionToTremor(new Vector3(0f, 0f, 13f));
        SessionFor(2f);
        Assert.That(_events.Count<ShockwavePassed>(), Is.EqualTo(1), "Sanity: the ring ran out.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Spit_TelegraphsPointEight_FiresThreeWithOwnSpecId()
    {
        Surface();
        RunWindow();
        _events.Clear();
        Surface();
        Tick(0.4f);
        EnemyTelegraph telegraph = MoveTelegraphs()[0];
        Assert.That(telegraph.Move, Is.EqualTo(GravemawBehaviour.SpitMove));
        Assert.That(telegraph.Duration, Is.EqualTo(0.8f));
        Tick(0.75f);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Tick(0.125f);
        IReadOnlyList<ProjectileFired> rocks = _events.Of<ProjectileFired>();
        Assert.That(rocks.Count, Is.EqualTo(3));
        var targets = new List<Vector3>();
        foreach (ProjectileFired rock in rocks)
        {
            Assert.That(rock.SpecId, Is.EqualTo(Body().Id));
            Assert.That(rock.SourceId, Is.EqualTo(_agent.Id));
            targets.Add(rock.Target);
        }

        Assert.That(targets, Is.EquivalentTo(new[]
        {
            Centre, Centre + new Vector3(2.5f, 0f, 0f), Centre - new Vector3(2.5f, 0f, 0f),
        }));
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.Zero, "The spit window has no tremor.");
    }

    // ---- Rule 5 -------------------------------------------------------------------------------------

    [Test]
    public void Moves_OnlyWhileSurfaced()
    {
        Tick(0f);
        Tick(2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Tick(0.5f);
        Assert.That(MoveKinds(), Is.Empty, "Nothing while chasing or tunnelling.");
        Tick(0.5f);
        RunWindow();
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        Tick(0.4f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        Assert.That(MoveKinds().Length, Is.EqualTo(1), "Nothing more while diving.");
    }

    [Test]
    public void Window_EndDropsArmedMove()
    {
        _stats.WindowSeconds.Base = 1f;
        Surface();
        Tick(0.5f);
        Assert.That(MoveKinds().Length, Is.EqualTo(1), "Armed.");
        Tick(0.6f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive), "The window closed on its clock.");
        Tick(0.5f);
        Tick(0.5f);
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.Zero);
    }

    [Test]
    public void Phase_DropsArmedMove()
    {
        Surface();
        Tick(0.5f);
        Assert.That(MoveKinds().Length, Is.EqualTo(1), "Armed.");
        _gravemaw.OnPhaseEntered(1);
        Tick(1f);
        Tick(1f);
        Assert.That(_gravemaw.State, Is.Not.EqualTo(GravemawState.Surfaced));
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.Zero);
    }

    // ---- Rule 6 -------------------------------------------------------------------------------------

    [Test]
    public void Telegraphs_AtLeastPointSix()
    {
        Surface();
        RunWindow();
        Surface();
        RunWindow();
        IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();
        Assert.That(telegraphs.Count, Is.EqualTo(4), "Two eruptions, a tremor and a spit.");
        foreach (EnemyTelegraph telegraph in telegraphs)
        {
            Assert.That(telegraph.Duration, Is.GreaterThanOrEqualTo(0.6f), $"Move {telegraph.Move}.");
        }
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        float[] cycle = { 0f, 2.5f, 1f, 0.5f, 1f, 3f, 0.5f, 2.5f, 1f, 0.5f, 1f };
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        player.Health.SetExternalInvulnerable(true);
        var projectiles = new ProjectileSystem(silent, 8);
        var shockwaves = new ShockwaveSystem(silent);
        var gravemaw = new GravemawBehaviour(_agent, _stats, new FixedRandom(0.5f).Misc, _motor, shockwaves, Frog);
        Place(Centre + new Vector3(5f, 0f, 0f));
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            projectiles.Clear();
            shockwaves.Clear();
            gravemaw.Reset();
            foreach (float dt in cycle)
            {
                _now += dt;
                gravemaw.Tick(new EnemyTickContext(dt, _now, player, _intents, silent, projectiles, _enemies));
            }
        }, 2);
        Assert.That(shockwaves.ActiveCount, Is.EqualTo(1), "The probe never reached the tremor.");
        Assert.That(projectiles.InFlightCount, Is.EqualTo(3), "The probe never reached the spit.");
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>Hits that landed: a hit the i-frames turned away still publishes, blocked.</summary>
    private int Landed()
    {
        int landed = 0;
        foreach (PlayerDamaged hit in _events.Of<PlayerDamaged>())
        {
            landed += hit.Blocked ? 0 : 1;
        }

        return landed;
    }

    private int[] MoveKinds()
    {
        EnemyTelegraph[] moves = MoveTelegraphs();
        var kinds = new int[moves.Length];
        for (int i = 0; i < moves.Length; i++)
        {
            kinds[i] = moves[i].Move;
        }

        return kinds;
    }

    private EnemyTelegraph[] MoveTelegraphs()
    {
        var moves = new List<EnemyTelegraph>();
        foreach (EnemyTelegraph telegraph in _events.Of<EnemyTelegraph>())
        {
            if (telegraph.Move != GravemawBehaviour.EruptionMove)
            {
                moves.Add(telegraph);
            }
        }

        return moves.ToArray();
    }

    /// <summary>From a fresh chase or a dive, on to the eruption that opens the next window.</summary>
    private void Surface()
    {
        if (_gravemaw.State == GravemawState.Dive)
        {
            Tick(0.5f);
        }

        Tick(0f);
        Tick(2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Tick(1f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
    }

    /// <summary>Arm, release, and close the window: 0.5 + 1 + 3 s of a 4 s window.</summary>
    private void RunWindow()
    {
        Tick(0.5f);
        Tick(1f);
        Tick(3f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
    }

    private void Tick(float dt)
    {
        _now += dt;
        _intents.Clear();
        _motor.ClearHold();
        _gravemaw.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
    }

    private void Place(Vector3 player)
    {
        EnemyBlackboard board = _agent.Blackboard;
        var flat = new Vector2(player.X, player.Z);
        board.SelfPosition = Vector3.Zero;
        board.PlayerPosition = player;
        board.DistanceToPlayer = flat.Length();
        board.DirectionToPlayer = flat.LengthSquared() > 0f ? Vector2.Normalize(flat) : Vector2.Zero;
        board.PathDirectionToPlayer = board.DirectionToPlayer;
    }

    /// <summary>
    /// A real session: the ring arms on (0, 0, 3), the player steps to <paramref name="standAt"/>
    /// so the eruption misses, and the run goes on to the tick the tremor leaves.
    /// </summary>
    private void SessionToTremor(Vector3 standAt)
    {
        var random = new FixedRandom(0.5f);
        _session = new RunSession(Catalog(), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), 8, 8, 8);
        _session.Start(new RunConfig(new ContentId("mode.test"), Character().Id, 0, 1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
        _enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_session.State);
        _agent = _enemies.SpawnBoss(new ContentId("boss.gravemaw"), Vector3.Zero);
        _gravemaw = (GravemawBehaviour)((BossBehaviour)_agent.Behaviour).Inner;
        _bossAt = Vector3.Zero;
        _playerAt = Centre;
        SessionUntil(() => _gravemaw.State == GravemawState.Tunnel, 4f);
        _playerAt = standAt;
        SessionUntil(() => _gravemaw.State == GravemawState.Surfaced, 1.5f);
        Assert.That(_events.Single<GravemawErupted>().Hit, Is.False, "Sanity: the eruption missed.");
        SessionUntil(() => _events.Count<ShockwaveEmitted>() == 1, 1.5f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    private void SessionTick()
    {
        var snapshot = new WorldSnapshot(8) { Dt = Frame, PlayerPosition = _playerAt };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = _agent.Id;
        sense.Position = _bossAt;
        sense.PathDirectionToPlayer = Vector2.Normalize(new Vector2(_playerAt.X - _bossAt.X, _playerAt.Z - _bossAt.Z));
        sense.HasLineOfSight = true;
        bool tunnelling = _gravemaw.State == GravemawState.Tunnel;
        _intents.Clear();
        _session.Tick(snapshot);
        if (tunnelling)
        {
            _bossAt += _intents.LastEnemyMove.Velocity * Frame;
        }
    }

    private void SessionFor(float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);
        for (int i = 0; i < frames; i++)
        {
            SessionTick();
        }
    }

    private void SessionUntil(Func<bool> done, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);
        for (int i = 0; i < frames && !done(); i++)
        {
            SessionTick();
        }

        Assert.That(done(), Is.True, "The awaited state never came.");
    }

    private static ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.gravemaw"), Body().Id,
            new[] { new BossPhaseSpec(1f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private static EnemySpec Body() => new(new ContentId("enemy.gravemaw"), new LocKey("enemy.gravemaw.name"),
        3000f, 4f, 8, 40, 300f, false, Contact, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
