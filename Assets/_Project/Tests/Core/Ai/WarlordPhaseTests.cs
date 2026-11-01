using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class WarlordPhaseTests
{
    private const string BodyId = "enemy.mossguard_warlord";
    private const string GuardId = "enemy.mossguard";
    private const string BossId = "boss.mossguard_warlord";
    private const int Capacity = 8;
    private const float Tolerance = 1e-4f;

    private static readonly Vector3 Near = Vector3.UnitZ * 3f;
    private static readonly Vector3 Far = Vector3.UnitZ * 8f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private FixedRandom _random;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private WarlordBehaviour _warlord;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, 32);
        _lanes = new LaneSystem(_events);
        _random = new FixedRandom(0.5f);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body(BodyId, 1000f), Body(GuardId, 10f) },
            modes: null, skills: null, trees: null, bosses: new[] { Boss() });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), Capacity, (agent, _) => Warlord(agent));
        _agent = _enemies.Registry.Spawn(Body(BodyId, 1000f), Vector3.Zero);
        _warlord = Warlord(_agent);
        _now = 0f;
        Place(Vector3.Zero, Near);
    }

    // Rule 1: from phase 1 a far player draws a stood lob telegraph, ranked before the charge.
    [Test]
    public void Lob_PhaseZero_Never()
    {
        FillRegistry();
        Place(Vector3.Zero, Far);
        for (int i = 0; i < 100; i++)
        {
            Tick(0.1f);
        }

        Assert.That(Telegraphs(WarlordMove.Lob), Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(Telegraphs(WarlordMove.Charge), Is.GreaterThan(0));
    }

    [Test]
    public void Lob_PhaseOne_FarPlayer_TelegraphsPointEight()
    {
        _warlord.OnPhaseEntered(1);
        Place(Vector3.Zero, Far);
        Tick(1.4f);
        Assert.That(_warlord.Phase, Is.EqualTo(1));
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Lob));
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraph.Duration, Is.EqualTo(0.8f));
        Assert.That(telegraph.Move, Is.EqualTo(4));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.79f);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
    }

    [TestCase(3f, WarlordMove.Sweep)]
    [TestCase(4.9f, WarlordMove.Charge)]
    public void Lob_NearPlayer_NotChosen(float distance, WarlordMove expected)
    {
        _warlord.OnPhaseEntered(1);
        Place(Vector3.Zero, Vector3.UnitZ * distance);
        Tick(1.4f);
        Assert.That(_warlord.Move, Is.EqualTo(expected));
        Assert.That(Telegraphs(WarlordMove.Lob), Is.Zero);
    }

    [Test]
    public void Lob_ThreeSpears_PlayerAndBothSides_OwnSpecId()
    {
        var self = new Vector3(1f, 0f, 2f);
        _warlord.OnPhaseEntered(1);
        Place(self, self + Far);
        Tick(1.4f);
        Tick(0.79f);
        // Aimed at the player's position on the release, across the line to the player.
        var player = new Vector3(7f, 0f, 10f);
        Place(self, player);
        Tick(0.01f);
        var shots = _events.Of<ProjectileFired>();
        Assert.That(shots.Count, Is.EqualTo(3));
        var across = new Vector3(-0.8f, 0f, 0.6f) * 2.5f;
        Assert.That(Vector3.Distance(shots[0].Target, player), Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(shots[1].Target, player + across), Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(shots[2].Target, player - across), Is.LessThan(Tolerance));
        float flight = 0f;
        foreach (ProjectileFired shot in shots)
        {
            Assert.That(shot.SpecId, Is.EqualTo(_agent.Spec.Id));
            Assert.That(shot.SourceId, Is.EqualTo(_agent.Id));
            Assert.That(shot.Origin, Is.EqualTo(self));
            flight = MathF.Max(flight, shot.FlightTime);
        }

        Assert.That(shots[0].FlightTime, Is.EqualTo(10f / 9f).Within(Tolerance));
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_random.Capture().Misc, Is.LessThanOrEqualTo(1));
        float before = _player.Health.Current;
        _projectiles.Tick(_now + flight + 0.01f, player, _player, _enemies);
        Assert.That(before - _player.Health.Current, Is.EqualTo(_agent.ContactDamage.Value).Within(Tolerance));
    }

    // 4.9 s after the release is still cooling down, though 5.7 s have passed since the arming.
    [TestCase(4.9f, WarlordMove.Charge)]
    [TestCase(5.05f, WarlordMove.Lob)]
    public void Lob_Cooldown_FromRelease(float sinceRelease, WarlordMove expected)
    {
        _warlord.OnPhaseEntered(1);
        Place(Vector3.Zero, Far);
        Tick(1.4f);
        Tick(0.8f);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(3));
        // 3.8 m chooses nothing: too far to sweep, too near to charge or lob. Recovery, then the gap.
        Place(Vector3.Zero, Vector3.UnitZ * 3.8f);
        Tick(0.8f);
        Tick(1.4f);
        Tick(sinceRelease - 2.2f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Place(Vector3.Zero, Far);
        Tick(0f);
        Assert.That(_warlord.Move, Is.EqualTo(expected));
    }

    // Rule 2: phase 2 shatters the shield once: no guard for the rest of the fight.
    [Test]
    public void Shatter_PhaseTwo_GuardZero_PublishesOnce()
    {
        _warlord.OnPhaseEntered(1);
        Tick(0.01f);
        Assert.That(_warlord.IsShattered, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
        Assert.That(_agent.IsVulnerable, Is.False);
        _warlord.OnPhaseEntered(2);
        Assert.That(_warlord.Phase, Is.EqualTo(2));
        Assert.That(_warlord.IsShattered, Is.True);
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
        Tick(0.01f);
        Assert.That(_events.Single<WarlordShattered>().BossId, Is.EqualTo(_agent.Id));
        Assert.That(_events.Of<WarlordGuardArc>()[_events.Count<WarlordGuardArc>() - 1].HalfArcDeg, Is.Zero);
        Assert.That(_agent.IsVulnerable, Is.True);
        _warlord.OnPhaseEntered(2);
        Tick(0.01f);
        Tick(0.01f);
        Assert.That(_events.Count<WarlordShattered>(), Is.EqualTo(1));
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
    }

    [Test]
    public void Shatter_LockNoLongerWidens()
    {
        Vector3[] ground = { Vector3.UnitX * 3f, -Vector3.UnitX * 3f };
        Tick(8f, ground);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.WarCry));
        Tick(1f, ground);
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(90f));
        _warlord.OnPhaseEntered(2);
        Tick(0.01f);
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));
        Assert.That(_warlord.IsLocked, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
        Assert.That(_agent.IsVulnerable, Is.True);
    }

    [Test]
    public void Shatter_Faster_TurnsFaster_ShorterGaps()
    {
        float speed = _agent.MoveSpeed.Value;
        Place(Vector3.Zero, Vector3.UnitZ * 20f);
        Tick(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(speed).Within(Tolerance));
        Place(Vector3.Zero, Vector3.UnitX * 20f);
        Tick(0.5f);
        Assert.That(Degrees(_warlord.Facing, Vector2.UnitY), Is.EqualTo(20f).Within(0.01f));

        _warlord.OnPhaseEntered(2);
        Assert.That(_agent.MoveSpeed.Value, Is.EqualTo(speed * 1.4f).Within(Tolerance));
        Vector2 before = _warlord.Facing;
        Tick(0.25f);
        Assert.That(Degrees(_warlord.Facing, before), Is.EqualTo(30f).Within(0.01f));
        Place(Vector3.Zero, new Vector3(_warlord.Facing.X, 0f, _warlord.Facing.Y) * 20f);
        Tick(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(speed * 1.4f).Within(Tolerance));

        // The phase entry's gap is 0.9 s; after the combo the recovery is 0.5 s, then 0.9 s again.
        _warlord.OnPhaseEntered(2);
        Place(Vector3.Zero, new Vector3(_warlord.Facing.X, 0f, _warlord.Facing.Y) * 3f);
        Tick(0.88f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Tick(0.02f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Combo));
        Tick(0.8f);
        Tick(0.6f);
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(2));
        Tick(0.5f);
        Tick(0.88f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Tick(0.02f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Combo));
    }

    // Rule 3: shattered, the sweep becomes a two-hit combo; the second re-aims with its own cone.
    [Test]
    public void Combo_TwoTelegraphs_TwoHits_SecondReaims()
    {
        _warlord.OnPhaseEntered(2);
        Tick(0.9f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Combo));
        EnemyTelegraph first = _events.Single<EnemyTelegraph>();
        Assert.That(first.Duration, Is.EqualTo(0.8f));
        Assert.That(first.Move, Is.EqualTo(5));
        Assert.That(_events.Single<SprayArmed>().Direction, Is.EqualTo(Vector2.UnitY));
        // The player steps out of the first cone, to the boss's side.
        Place(Vector3.Zero, Vector3.UnitX * 3f);
        float before = _player.Health.Current;
        Tick(0.8f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.False);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.ComboSecond));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(2));
        EnemyTelegraph second = _events.Of<EnemyTelegraph>()[1];
        Assert.That(second.Id, Is.EqualTo(_agent.Id));
        Assert.That(second.Move, Is.EqualTo(6));
        Assert.That(_events.Count<SprayArmed>(), Is.EqualTo(2));
        Assert.That(Vector2.Distance(_events.Of<SprayArmed>()[1].Direction, Vector2.UnitX), Is.LessThan(Tolerance));
        Assert.That(Vector2.Distance(_warlord.Facing, Vector2.UnitX), Is.LessThan(Tolerance));
        Tick(0.6f);
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(2));
        Assert.That(_events.Of<SprayFired>()[1].Hit, Is.True);
        Assert.That(before - _player.Health.Current,
            Is.EqualTo(_agent.ContactDamage.Value * 1.5f).Within(Tolerance));
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
    }

    [Test]
    public void Combo_SecondWindupIsPointSix()
    {
        _warlord.OnPhaseEntered(2);
        Tick(0.9f);
        Tick(0.8f);
        Assert.That(_events.Of<EnemyTelegraph>()[1].Duration, Is.EqualTo(0.6f));
        Assert.That(_events.Of<SprayArmed>()[1].Seconds, Is.EqualTo(0.6f));
        Tick(0.58f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.ComboSecond));
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(1));
        Tick(0.03f);
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(2));
        foreach (EnemyTelegraph telegraph in _events.Of<EnemyTelegraph>())
        {
            Assert.That(telegraph.Duration, Is.GreaterThanOrEqualTo(0.6f));
        }
    }

    // Rule 4: shattered, the war cry and the charge go on, and a daze still lasts 2 s.
    [Test]
    public void Shattered_StillCharges_StillWarCries()
    {
        Vector3[] ground = { Vector3.UnitX * 3f, -Vector3.UnitX * 3f };
        _warlord.OnPhaseEntered(2);
        Tick(8f, ground);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.WarCry));
        Tick(1f, ground);
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));

        Place(Vector3.Zero, Vector3.UnitZ * 4.5f);
        Tick(0.5f);
        Tick(0.9f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        _warlord.OnLaneStop(true, 2f);
        Tick(0.9f);
        Place(Vector3.UnitZ * 2f, Vector3.UnitZ * 4.5f);
        Tick(0.01f);
        Assert.That(_warlord.IsDazed, Is.True);
        Assert.That(_events.Single<WarlordDazed>().Seconds, Is.EqualTo(2f));
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
        Tick(1.98f);
        Assert.That(_warlord.IsDazed, Is.True);
        Tick(0.03f);
        Assert.That(_warlord.IsDazed, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
    }

    // Rule 5: BossBehaviour's beats at 0.66 and 0.33 drop the armed move, then apply the phase.
    [Test]
    public void Phase_DropsMove_ThenApplies()
    {
        _enemies.Registry.Despawn(_agent.Id);
        _agent = _enemies.SpawnBoss(new ContentId(BossId), Vector3.Zero);
        var boss = (BossBehaviour)_agent.Behaviour;
        _warlord = (WarlordBehaviour)boss.Inner;
        FillRegistry();

        Vector3 ahead = Near;
        TickUntilSweep(ahead);
        StepBehind(ahead, 0.65f);
        Assert.That(boss.Phase, Is.EqualTo(1));
        Assert.That(_warlord.Phase, Is.EqualTo(1));
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_events.Count<SprayCleared>(), Is.EqualTo(1));
        Assert.That(_events.Count<SprayFired>(), Is.Zero);
        Assert.That(_warlord.IsShattered, Is.False);
        Assert.That(boss.IsInBeat, Is.True);

        // Phase 1's change: after the beat a far player draws a lob.
        Place(Vector3.Zero, -Far);
        for (int i = 0; i < 60 && _warlord.Move != WarlordMove.Lob; i++)
        {
            EnemyTick(0.05f);
        }

        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Lob));

        ahead = -Near;
        TickUntilSweep(ahead);
        int cleared = _events.Count<SprayCleared>();
        StepBehind(ahead, 0.32f);
        Assert.That(boss.Phase, Is.EqualTo(2));
        Assert.That(_warlord.Phase, Is.EqualTo(2));
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_events.Count<SprayCleared>(), Is.EqualTo(cleared + 1));
        Assert.That(_warlord.IsShattered, Is.True);
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);

        // Phase 2's change: the next close move is the combo, and the shatter was announced once.
        Place(Vector3.Zero, ahead);
        for (int i = 0; i < 80 && _warlord.Move != WarlordMove.Combo; i++)
        {
            EnemyTick(0.05f);
        }

        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Combo));
        Assert.That(_events.Count<WarlordShattered>(), Is.EqualTo(1));
    }

    // Rule 6: reset drops the bonus and the shatter; ticking allocates nothing.
    [Test]
    public void Reset_RemovesBonus_Unshatters()
    {
        float speed = _agent.MoveSpeed.Value;
        int modifiers = _agent.MoveSpeed.ModifierCount;
        _warlord.OnPhaseEntered(2);
        Assert.That(_agent.MoveSpeed.ModifierCount, Is.EqualTo(modifiers + 1));
        _warlord.Reset();
        Assert.That(_agent.MoveSpeed.Value, Is.EqualTo(speed));
        Assert.That(_agent.MoveSpeed.ModifierCount, Is.EqualTo(modifiers));
        Assert.That(_warlord.IsShattered, Is.False);
        Assert.That(_warlord.Phase, Is.Zero);
        Tick(0.01f);
        Assert.That(_events.Count<WarlordShattered>(), Is.Zero);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
        _warlord.OnPhaseEntered(2);
        Assert.That(_agent.MoveSpeed.Value, Is.EqualTo(speed * 1.4f).Within(Tolerance));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        FillRegistry();
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var projectiles = new ProjectileSystem(silent, 8);
        var lanes = new LaneSystem(silent);
        // The lob fires through the injected system, so this one must publish silently too.
        _warlord = new WarlordBehaviour(_agent, projectiles, _random.Misc, new ContentId(GuardId));
        // A whole lob each time: arm, stand, release three spears.
        AllocationAssert.None(() =>
        {
            _warlord.Reset();
            _warlord.OnPhaseEntered(1);
            _intents.Clear();
            Place(Vector3.Zero, Far);
            projectiles.Clear();
            _warlord.Tick(new EnemyTickContext(1.4f, 1f, player, _intents, silent, projectiles, _enemies, lanes));
            _warlord.Tick(new EnemyTickContext(0.8f, 2f, player, _intents, silent, projectiles, _enemies, lanes));
        }, 100);
        Assert.That(projectiles.InFlightCount, Is.EqualTo(3));
        _warlord.OnPhaseEntered(2);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            Place(Vector3.Zero, Near);
            _warlord.Tick(new EnemyTickContext(0.1f, 1f, player, _intents, silent, projectiles, _enemies, lanes));
        }, 100);
    }

    private void TickUntilSweep(Vector3 ahead)
    {
        Place(Vector3.Zero, ahead);
        for (int i = 0; i < 100 && _warlord.Move != WarlordMove.Sweep; i++)
        {
            EnemyTick(0.05f);
        }

        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Sweep));
    }

    // Behind the guard so the hit lands, then down to the given fraction.
    private void StepBehind(Vector3 ahead, float fraction)
    {
        Place(Vector3.Zero, -ahead);
        EnemyTick(0.01f);
        Assert.That(_agent.IsVulnerable, Is.True);
        float damage = _agent.Health.Current - (fraction * _agent.Health.Current / _agent.Health.Fraction);
        _enemies.ApplyDamage(_agent.Id, damage, _now, _player);
        Assert.That(_agent.Health.Fraction, Is.EqualTo(fraction).Within(1e-3f));
        EnemyTick(0.01f);
    }

    private void FillRegistry()
    {
        while (_enemies.Registry.AliveCount < Capacity)
        {
            _enemies.Spawn(new ContentId(GuardId), Vector3.UnitX * 40f);
        }
    }

    private void Tick(float dt, Vector3[] ground = null)
    {
        _now += dt;
        _warlord.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies,
            _lanes, ground));
    }

    private void EnemyTick(float dt)
    {
        _now += dt;
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies,
            _lanes));
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

    private int Telegraphs(WarlordMove move)
    {
        int count = 0;
        foreach (EnemyTelegraph telegraph in _events.Of<EnemyTelegraph>())
        {
            if (telegraph.Move == (int)move)
            {
                count++;
            }
        }

        return count;
    }

    private static float Degrees(Vector2 a, Vector2 b) =>
        MathF.Acos(MathF.Min(1f, Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)))) * 180f / MathF.PI;

    private WarlordBehaviour Warlord(EnemyAgent agent) =>
        new(agent, _projectiles, _random.Misc, new ContentId(GuardId));

    private static BossSpec Boss() => new(new ContentId(BossId), new ContentId(BodyId),
        new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f), new BossPhaseSpec(0.33f) }, 1.5f);

    private static EnemySpec Body(string id, float hp) => new(new ContentId(id), new LocKey(id + ".name"),
        hp, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f,
        id == BodyId ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);

    private static CharacterSpec Character() => new(new ContentId("character.test"),
        new LocKey("character.test.name"), new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
