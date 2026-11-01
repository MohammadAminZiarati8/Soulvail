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
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class WarlordChargeTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private WarlordBehaviour _warlord;
    private RunSession _session;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _projectiles = new ProjectileSystem(_events, 8);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _lanes = new LaneSystem(_events);
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _warlord = new WarlordBehaviour(_agent, _projectiles, new FixedRandom(0.5f).Misc,
            new ContentId("enemy.mossguard"));
        _session = null;
        _now = 0f;
        Place(Vector3.Zero, new Vector3(0f, 0f, 10f));
    }

    // Rule 1: sweep has priority; otherwise charge catches one immutable line off cooldown.
    [TestCase(4f)]
    [TestCase(10f)]
    public void Charge_FarPlayer_ArmsTelegraphsAndProbes(float distance)
    {
        Place(new Vector3(2f, 3f, 4f), new Vector3(2f + distance, 103f, 4f));
        Tick(1.4f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraph.Duration, Is.EqualTo(0.9f));
        Assert.That(telegraph.Move, Is.EqualTo(2));
        Assert.That(_intents.LaneProbes.Count, Is.EqualTo(1));
        LaneProbeIntent probe = _intents.LaneProbes[0];
        Assert.That(probe.EnemyId, Is.EqualTo(_agent.Id));
        Assert.That(probe.From, Is.EqualTo(new Vector3(2f, 3f, 4f)));
        Assert.That(probe.Direction, Is.EqualTo(Vector3.UnitX));
        Assert.That(probe.Reach, Is.EqualTo(14f));
        Assert.That(probe.Radius, Is.EqualTo(0.8f));
        Place(Vector3.Zero, -Vector3.UnitZ * 10f);
        Tick(0.1f);
        Assert.That(_warlord.Facing, Is.EqualTo(Vector2.UnitX));
        Assert.That(_intents.LaneProbes.Count, Is.EqualTo(1));
    }

    [Test]
    public void Charge_NearPlayer_Sweeps()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 3.5f);
        Tick(1.4f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Sweep));
        Assert.That(_intents.LaneProbes, Is.Empty);
        Assert.That(_events.Single<EnemyTelegraph>().Move, Is.EqualTo(1));
    }

    [Test]
    public void Charge_OnCooldown_NotArmed()
    {
        Dash(false);
        Place(Vector3.UnitZ * 14f, Vector3.UnitZ * 24f);
        Tick(0.01f);
        Tick(0.8f);
        Tick(1.4f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1));
        // A full registry keeps the 8 s war cry (JG-15c) from outranking the charge.
        while (_enemies.Registry.AliveCount < _enemies.Registry.Capacity)
        {
            _enemies.Registry.Spawn(Body(), Vector3.UnitX * 30f);
        }

        Tick(3.8f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(2));
    }

    // Rule 2: answer once or fall back, and arm only for the wind-up still owed.
    [TestCase(6f)]
    [TestCase(20f)]
    public void Lane_ProbeHit_LengthIsTheStone(float distance)
    {
        Tick(1.4f);
        _warlord.OnLaneStop(true, distance);
        Tick(0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(MathF.Min(14f, distance)));
        _warlord.OnLaneStop(true, 2f);
        Tick(0.1f);
        Assert.That(_events.Count<LaneArmed>(), Is.EqualTo(1));
    }

    [Test]
    public void Lane_ProbeMiss_Full()
    {
        Tick(1.4f);
        _warlord.OnLaneStop(false, 0f);
        Tick(0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(14f));
    }

    [Test]
    public void Lane_NoAnswer_OpensFullOnSecondTick()
    {
        Tick(1.4f);
        Tick(0.1f);
        Assert.That(_events.Count<LaneArmed>(), Is.Zero);
        Tick(0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(14f));
        _warlord.OnLaneStop(true, 2f);
        Tick(0.71f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(16f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Lane_ArmedForTheRemainder_BitesAtDash(bool coarseTick)
    {
        Tick(1.4f);
        _warlord.OnLaneStop(false, 0f);
        Tick(coarseTick ? 0.9f : 0.1f);
        LaneArmed lane = _events.Single<LaneArmed>();
        Assert.That(lane.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(lane.Direction, Is.EqualTo(Vector2.UnitY));
        Assert.That(lane.HalfWidth, Is.EqualTo(1.1f));
        if (!coarseTick)
        {
            Assert.That(lane.ArmSeconds, Is.EqualTo(0.8f).Within(1e-5f));
            _lanes.Tick(_now + 0.79f, Vector3.UnitZ * 10f, _player);
            Assert.That(_player.Health.Current, Is.EqualTo(100f));
            Tick(0.8f);
        }
        _lanes.Tick(_now, Vector3.UnitZ * 10f, _player);
        Assert.That(_player.Health.Current, Is.EqualTo(60f));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.UnitZ * 16f));
        Tick(0.1f);
        Assert.That(_player.Health.Current, Is.EqualTo(60f), "The dash itself does no damage.");
    }

    // Rule 3: reported XZ displacement, lane-end tolerance, and a bounded blocked dash.
    [Test]
    public void Dash_MeasuresReportedTravel()
    {
        Dash(true, 6f);
        Place(new Vector3(0f, 100f, 0f), Vector3.UnitZ * 10f);
        Tick(0.4f); // Commanded travel would already be 6.4 m; reported XZ travel is zero.
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Assert.That(_warlord.IsDazed, Is.False);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(16f));
    }

    [Test]
    public void Dash_EndsAtLaneEnd()
    {
        Dash(false);
        Place(Vector3.UnitZ * 13.89f, Vector3.UnitZ * 20f);
        Tick(0.01f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Place(Vector3.UnitZ * 13.91f, Vector3.UnitZ * 20f);
        Tick(0.01f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Dash_TimesOut()
    {
        Dash(false);
        Tick(1.17f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Tick(0.01f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    // Rule 4: a reached stone dazes with the shield down, then restores it exactly once.
    [TestCase(6f, 0.01f)]
    [TestCase(5.01f, 0.68f)]
    public void Stone_Reached_Dazes2s_GuardZero_Publishes(float travel, float dt)
    {
        Dash(true, 6f);
        Place(Vector3.UnitZ * travel, Vector3.UnitZ * 10f);
        Tick(dt);
        Assert.That(_warlord.IsDazed, Is.True);
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.Health.IsInvulnerable, Is.False);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        WarlordDazed daze = _events.Single<WarlordDazed>();
        Assert.That(daze.BossId, Is.EqualTo(_agent.Id));
        Assert.That(daze.At, Is.EqualTo(Vector3.UnitZ * travel));
        Assert.That(daze.Seconds, Is.EqualTo(2f));
        Assert.That(_events.Of<WarlordGuardArc>()[1].HalfArcDeg, Is.Zero);
    }

    [Test]
    public void Daze_Ends_GuardRestored()
    {
        Dash(true, 6f);
        Place(Vector3.UnitZ * 6f, Vector3.UnitZ * 10f);
        Tick(0.01f);
        Tick(1.99f);
        Assert.That(_warlord.IsDazed, Is.True);
        Tick(0.02f);
        Assert.That(_warlord.IsDazed, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
        Assert.That(_agent.Health.IsInvulnerable, Is.True);
        Assert.That(_events.Count<WarlordDazed>(), Is.EqualTo(1));
        Assert.That(_events.Of<WarlordGuardArc>()[2].HalfArcDeg, Is.EqualTo(60f));
    }

    [Test]
    public void NoStone_NoDaze()
    {
        Dash(false);
        Place(Vector3.UnitZ * 14f, Vector3.UnitZ * 24f);
        Tick(0.01f);
        Assert.That(_warlord.IsDazed, Is.False);
        Assert.That(_events.Count<WarlordDazed>(), Is.Zero);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
    }

    // Rule 5: real session routing through RootGate, without CC knowledge in the behaviour.
    [Test]
    public void Rooted_InLane_NoDaze()
    {
        StartSession();
        SessionDash(true, 6f);
        Assert.That(_enemies.Root(_agent.Id, 4f), Is.True);
        SessionTick(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        SessionTick(0.59f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_warlord.IsDazed, Is.False);
        Assert.That(_events.Count<WarlordDazed>(), Is.Zero);
    }

    // Rule 6: a boss keeps half the stun and its dash timer freezes until release.
    [Test]
    public void Stunned_MidDash_NotTicked_HalfDuration()
    {
        StartSession();
        SessionDash(false);
        SessionTick(0.8f);
        Assert.That(_enemies.Stun(_agent.Id, 2f), Is.True);
        SessionTick(0.99f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        SessionTick(0.02f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(16f));
        SessionTick(0.34f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        SessionTick(0.02f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
    }

    // Rule 7: stale reports are inert; a phase drops motion but honours an opened lane.
    [Test]
    public void Report_UnknownOrDeadId_Ignored()
    {
        StartSession();
        SessionTick(1.4f);
        Assert.DoesNotThrow(() => _session.ReportLaneStop(int.MaxValue, true, 2f));
        _agent.Health.SetExternalInvulnerable(false);
        _agent.Health.ApplyDamage(10_000f, 0f);
        Assert.That(_agent.IsAlive, Is.False);
        Assert.DoesNotThrow(() => _session.ReportLaneStop(_agent.Id, true, 2f));
        // Ticking directly exposes whether the dead report incorrectly reached the listener.
        Tick(0.1f);
        Tick(0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(14f));
        _session.End();
        Assert.Throws<InvalidOperationException>(() => _session.ReportLaneStop(_agent.Id, false, 0f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Phase_MidCharge_LaneStillBites(bool duringDash)
    {
        Tick(1.4f);
        _warlord.OnLaneStop(false, 0f);
        Tick(0.1f);
        if (duringDash)
        {
            Tick(0.8f);
        }
        _warlord.OnPhaseEntered(1);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Tick(0f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.LessThan(16f));
        _lanes.Tick(2.31f, Vector3.UnitZ * 10f, _player);
        Assert.That(_player.Health.Current, Is.EqualTo(60f));
        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1));
        Assert.That(_events.Count<WarlordDazed>(), Is.Zero);
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var lanes = new LaneSystem(silent);
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var projectiles = new ProjectileSystem(silent, 8);
        AllocationAssert.None(() =>
        {
            _warlord.Reset();
            Place(Vector3.Zero, Vector3.UnitZ * 10f);
            _intents.Clear();
            _warlord.Tick(new EnemyTickContext(1.4f, 1.4f, player, _intents, silent, projectiles, _enemies, lanes));
            _warlord.OnLaneStop(true, 6f);
            _warlord.Tick(new EnemyTickContext(0.1f, 1.5f, player, _intents, silent, projectiles, _enemies, lanes));
            _warlord.Tick(new EnemyTickContext(0.8f, 2.3f, player, _intents, silent, projectiles, _enemies, lanes));
            Place(Vector3.UnitZ * 6f, Vector3.UnitZ * 10f);
            _warlord.Tick(new EnemyTickContext(0.01f, 2.31f, player, _intents, silent, projectiles, _enemies, lanes));
            _warlord.Tick(new EnemyTickContext(2f, 4.31f, player, _intents, silent, projectiles, _enemies, lanes));
            _warlord.OnPhaseEntered(1);
            lanes.Tick(10f, Vector3.Zero, player);
        }, 100);
    }

    private void Dash(bool hit, float distance = 0f)
    {
        Tick(1.4f);
        _warlord.OnLaneStop(hit, distance);
        Tick(0.1f);
        Tick(0.8f);
    }

    private void Tick(float dt)
    {
        _now += dt;
        _warlord.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, _lanes));
    }

    private void Place(Vector3 self, Vector3 player)
    {
        Vector3 delta = player - self;
        var flat = new Vector2(delta.X, delta.Z);
        _agent.Blackboard.SelfPosition = self;
        _agent.Blackboard.PlayerPosition = player;
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
    }

    private void StartSession()
    {
        var random = new FixedRandom(0.5f);
        _session = new RunSession(Catalog(), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), 8, 8, 8);
        Assert.Throws<InvalidOperationException>(() => _session.ReportLaneStop(1, false, 0f));
        _session.Start(new RunConfig(new ContentId("mode.test"), Character().Id, 0, 1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
        _enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_session.State);
        _agent = _enemies.SpawnBoss(new ContentId("boss.mossguard_warlord"), Vector3.Zero);
        _warlord = (WarlordBehaviour)((BossBehaviour)_agent.Behaviour).Inner;
    }

    private void SessionDash(bool hit, float distance = 0f)
    {
        SessionTick(1.4f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        _session.ReportLaneStop(_agent.Id, hit, distance);
        SessionTick(0.1f);
        SessionTick(0.8f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(16f));
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(hit ? distance : 14f));
    }

    private void SessionTick(float dt)
    {
        var snapshot = new WorldSnapshot(8) { Dt = dt, PlayerPosition = Vector3.UnitZ * 10f };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = _agent.Id;
        sense.Position = Vector3.Zero;
        sense.PathDirectionToPlayer = Vector2.UnitY;
        sense.HasLineOfSight = true;
        _intents.Clear();
        _session.Tick(snapshot);
        _now = _session.State.Time;
    }

    private ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.mossguard_warlord"), Body().Id,
            new[] { new BossPhaseSpec(1f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private EnemySpec Body() => new(new ContentId("enemy.mossguard_warlord"), new LocKey("enemy.warlord.name"),
        3200f, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
