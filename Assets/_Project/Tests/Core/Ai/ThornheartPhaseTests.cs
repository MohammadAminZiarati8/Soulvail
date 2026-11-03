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
/// JG-20d rules 1–7. Phases are BossBehaviour's indices: 1 is the 66 % crossing, 2 the 33 % one.
/// Rows other than the first drive the inner behaviour alone and enter a phase by hand.
/// </summary>
[TestFixture]
public sealed class ThornheartPhaseTests
{
    private const string BodyId = "enemy.thornheart";
    private const string BossId = "boss.thornheart";
    private const string OathboundId = "character.oathbound";
    private const int Capacity = 32;
    private const float Contact = 20f;
    private const float Dt = 0.05f;
    private const float Tolerance = 1e-4f;

    private static readonly ContentId Pod = new ContentId("enemy.thorn_pod");
    private static readonly ContentId Rootling = new ContentId("enemy.rootling");

    /// <summary>Inside the ring at its widest and beyond every wall's reach.</summary>
    private static readonly Vector3 Inside = new Vector3(0f, 0f, ThornheartBehaviour.RingRadius - 0.5f);

    /// <summary>Outside the ring even at its widest, beyond every wall's reach.</summary>
    private static readonly Vector3 Outside = new Vector3(0f, 0f, ThornheartBehaviour.RingStartRadius + 3f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ThornSystem _thorns;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private ThornheartBehaviour _heart;
    private List<Vector3> _ground;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _thorns = new ThornSystem(_events);
        _projectiles = new ProjectileSystem(_events, 1);
        var catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Body(BodyId, 5_000f, EnemyBehaviourKind.Boss), Body(Pod.Value, 50f), Body(Rootling.Value, 20f) },
            modes: null, skills: null, trees: null,
            bosses: new[] { Boss() });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(),
            new DepthScaling(Scalings.Design()), Capacity,
            (agent, _) => new ThornheartBehaviour(agent, _thorns, new FixedRandom().Misc, Pod, Rootling));
        _agent = _enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitX;
        _heart = new ThornheartBehaviour(_agent, _thorns, new FixedRandom().Misc, Pod, Rootling);
        _ground = new List<Vector3>();
        Place(Inside);
        _now = 0f;
    }

    // Rule 1.
    [Test]
    public void Phases_AtSixtySixAndThirtyThree_BeatClearsAndWithers()
    {
        _enemies.Registry.Despawn(_agent.Id);
        _agent = _enemies.SpawnBoss(new ContentId(BossId), Vector3.Zero);
        var boss = (BossBehaviour)_agent.Behaviour;
        _heart = (ThornheartBehaviour)boss.Inner;
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitX;
        Vector3 player = new Vector3(0f, 0f, 30f);
        Place(player);
        Ground(player);

        RunUntil(() => _heart.PodCount > 0, census: true);
        Assert.That(_thorns.ActiveCount, Is.GreaterThan(0), "The opening spokes still stand.");
        Assert.That(_heart.Phase, Is.Zero);

        float[] entersBelow = { 0.66f, 0.33f };
        for (int phase = 1; phase <= 2; phase++)
        {
            float target = (entersBelow[phase - 1] - 0.01f) * _agent.Health.MaxHp.Value;
            Hit(_agent.Health.Current - target);
            Step(census: true);

            // The spawn announces phase 0, so the crossing is the latest one.
            IReadOnlyList<BossPhaseChanged> changes = _events.Of<BossPhaseChanged>();
            BossPhaseChanged changed = changes[changes.Count - 1];
            Assert.That(changed.Phase, Is.EqualTo(phase), "The event's phase.");
            Assert.That(_heart.Phase, Is.EqualTo(phase), "The inner behaviour's phase.");
            Assert.That(boss.IsInBeat, Is.True);
            Assert.That(Hit(10f).Applied, Is.Zero, "Invulnerable through the beat.");
            Assert.That(_heart.AddCount, Is.Zero, "Adds cleared.");
            Assert.That(_thorns.ActiveCount, Is.Zero, "Walls withered.");

            RunUntil(() => !boss.IsInBeat, census: true);
            if (phase == 1)
            {
                RunUntil(() => _heart.PodCount > 0, census: true);
            }
        }

        Assert.That(_events.Count<StrangleRing>(), Is.EqualTo(2), "One ring per phase entry, after each beat.");
    }

    // Rule 2.
    [Test]
    public void Ring_ClosesSeventeenToElevenInSix_PublishesEvent()
    {
        Assert.That(_heart.RingRadiusNow, Is.Zero, "No ring before 66 %.");
        _heart.OnPhaseEntered(1);
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(ThornheartBehaviour.RingStartRadius));

        Step();
        StrangleRing ring = _events.Single<StrangleRing>();
        Assert.That(ring.BossId, Is.EqualTo(_agent.Id));
        Assert.That(ring.FromRadius, Is.EqualTo(ThornheartBehaviour.RingStartRadius));
        Assert.That(ring.ToRadius, Is.EqualTo(ThornheartBehaviour.RingRadius));
        Assert.That(ring.Seconds, Is.EqualTo(ThornheartBehaviour.RingCloseSeconds));

        float announced = _now;
        RunFor(ThornheartBehaviour.RingCloseSeconds / 2f);
        float halfway = (ThornheartBehaviour.RingStartRadius + ThornheartBehaviour.RingRadius) / 2f;
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(halfway).Within(0.1f), "Linear.");

        RunUntil(() => _heart.RingRadiusNow <= ThornheartBehaviour.RingRadius);
        Assert.That(_now - announced, Is.EqualTo(ThornheartBehaviour.RingCloseSeconds).Within(Dt * 1.5f));
        RunFor(3f);
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(ThornheartBehaviour.RingRadius), "Holds once closed.");
        Assert.That(_events.Count<StrangleRing>(), Is.EqualTo(1));
    }

    [Test]
    public void Ring_ReopensOnNextPhase()
    {
        _heart.OnPhaseEntered(1);
        RunFor(ThornheartBehaviour.RingCloseSeconds + 1f);
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(ThornheartBehaviour.RingRadius));

        _heart.OnPhaseEntered(2);
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(ThornheartBehaviour.RingStartRadius));
        Step();
        Assert.That(_events.Count<StrangleRing>(), Is.EqualTo(2));
        RunFor(ThornheartBehaviour.RingCloseSeconds + 1f);
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(ThornheartBehaviour.RingRadius), "Recloses.");
    }

    // Rule 3.
    [Test]
    public void Ring_OutsidePulses_InsideSafe()
    {
        _heart.OnPhaseEntered(1);
        RunFor(ThornheartBehaviour.RingCloseSeconds + 1f);
        float hp = _player.Health.Current;
        Assert.That(hp, Is.EqualTo(_player.Health.MaxHp.Value), "Nothing bit inside.");

        Place(Outside);
        float steppedOut = _now;
        RunUntil(() => _player.Health.Current < hp);
        Assert.That(_now - steppedOut, Is.EqualTo(ThornheartBehaviour.RingPulseSeconds).Within(Dt * 1.5f), "A pulse, not a touch.");
        float bite = Contact * ThornheartBehaviour.RingDamageScale;
        Assert.That(hp - _player.Health.Current, Is.EqualTo(bite).Within(Tolerance));

        RunFor(2f);
        float pulses = (hp - _player.Health.Current) / bite;
        Assert.That(pulses, Is.EqualTo(1f + (2f / ThornheartBehaviour.RingPulseSeconds)).Within(1f));

        Place(Inside);
        float stepped = _player.Health.Current;
        RunFor(2f);
        Assert.That(_player.Health.Current, Is.EqualTo(stepped), "Stepping in is the answer.");
    }

    [Test]
    public void Ring_RollIFrames_NoDamage()
    {
        _heart.OnPhaseEntered(1);
        Place(Outside);
        _player.Health.SetExternalInvulnerable(true);
        RunFor(2f);
        Assert.That(_player.Health.Current, Is.EqualTo(_player.Health.MaxHp.Value));

        _player.Health.SetExternalInvulnerable(false);
        RunFor(1f);
        Assert.That(_player.Health.Current, Is.LessThan(_player.Health.MaxHp.Value), "Bites once the roll ends.");
    }

    // JG-20j: every bite that lands is published, so the view can draw it; a blocked one is not.
    [Test]
    public void Ring_Bite_PublishesStrangleBite_NotWhenBlocked()
    {
        _heart.OnPhaseEntered(1);
        RunFor(ThornheartBehaviour.RingCloseSeconds + 1f);
        Assert.That(_events.Count<StrangleBite>(), Is.Zero, "Nothing bit inside.");

        Place(Outside);
        _player.Health.SetExternalInvulnerable(true);
        RunFor(2f);
        Assert.That(_events.Count<StrangleBite>(), Is.Zero, "A blocked bite is not drawn.");

        _player.Health.SetExternalInvulnerable(false);
        float hp = _player.Health.Current;
        RunUntil(() => _player.Health.Current < hp);
        Assert.That(_events.Count<StrangleBite>(), Is.EqualTo(1));
        StrangleBite bite = _events.Single<StrangleBite>();
        Assert.That(bite.BossId, Is.EqualTo(_agent.Id));
        Assert.That(bite.ToHp, Is.EqualTo(hp - _player.Health.Current).Within(Tolerance));
    }

    // Rule 4.
    [Test]
    public void Spokes_ClampedInsideRing_LeaveStrafeLane()
    {
        _heart.OnPhaseEntered(1);
        RunUntilTelegraphs(1);
        Assert.That(_heart.RingRadiusNow, Is.GreaterThan(ThornheartBehaviour.RingRadius), "Armed while the ring is still wide.");

        IReadOnlyList<ThornArmed> spokes = _events.Of<ThornArmed>();
        float reach = ThornheartBehaviour.RingRadius - ThornheartBehaviour.StrafeLane;
        for (int i = 0; i < spokes.Count; i++)
        {
            Assert.That(spokes[i].Length, Is.EqualTo(reach - ThornheartBehaviour.RootInner).Within(Tolerance), $"spoke {i}");
        }

        RunFor(ThornheartBehaviour.RingCloseSeconds);
        Assert.That(_heart.RingRadiusNow, Is.EqualTo(ThornheartBehaviour.RingRadius));
        Assert.That(_thorns.ActiveCount, Is.GreaterThanOrEqualTo(spokes.Count), "The spokes still stand.");
        for (int i = 0; i < spokes.Count; i++)
        {
            Vector2 d = spokes[i].Direction;
            Vector3 lane = new Vector3(d.X, 0f, d.Y) * (ThornheartBehaviour.RingRadius - (ThornheartBehaviour.StrafeLane / 2f));
            Assert.That(_thorns.Blocks(lane, 0.5f), Is.False, $"Lane open past spoke {i}.");
        }
    }

    [Test]
    public void Slam_NotClamped()
    {
        _heart.OnPhaseEntered(1);
        RunUntil(() => _heart.Move == ThornheartMove.Slam);
        IReadOnlyList<ThornArmed> armed = _events.Of<ThornArmed>();
        ThornArmed slam = armed[armed.Count - 1];
        Assert.That(slam.Length, Is.EqualTo(ThornheartBehaviour.SlamLength));
        Assert.That(slam.HalfWidth, Is.EqualTo(ThornheartBehaviour.SlamHalfWidth));
    }

    // Rule 5.
    [Test]
    public void PhaseTwo_FourSpokes()
    {
        _heart.OnPhaseEntered(1);
        RunUntilTelegraphs(1);
        IReadOnlyList<ThornArmed> spokes = _events.Of<ThornArmed>();
        Assert.That(spokes.Count, Is.EqualTo(ThornheartBehaviour.PhaseTwoSpokes));
        float first = MathF.Atan2(spokes[0].Direction.Y, spokes[0].Direction.X);
        for (int i = 0; i < spokes.Count; i++)
        {
            float angle = first + (i * MathF.PI / 2f);
            Assert.That(spokes[i].Direction.X, Is.EqualTo(MathF.Cos(angle)).Within(Tolerance), $"spoke {i}");
            Assert.That(spokes[i].Direction.Y, Is.EqualTo(MathF.Sin(angle)).Within(Tolerance), $"spoke {i}");
        }
    }

    // Rule 6.
    [Test]
    public void PhaseThree_FasterTelegraph_ShorterGap_ThreePods()
    {
        Vector3 player = new Vector3(0f, 0f, 30f);
        Place(player);
        Ground(player);
        _heart.OnPhaseEntered(2);
        float entered = _now;

        RunUntilTelegraphs(1);
        Assert.That(_now - entered, Is.EqualTo(ThornheartBehaviour.PhaseThreeAttackGapSeconds).Within(Dt * 1.5f));
        EnemyTelegraph roots = _events.Of<EnemyTelegraph>()[0];
        Assert.That(roots.Move, Is.EqualTo((int)ThornheartMove.Roots));
        Assert.That(roots.Duration, Is.EqualTo(ThornheartBehaviour.PhaseThreeRootTelegraphSeconds));
        Assert.That(_events.Of<ThornArmed>()[0].ArmSeconds, Is.EqualTo(ThornheartBehaviour.PhaseThreeRootTelegraphSeconds));

        float rootsAt = _now;
        RunUntilTelegraphs(2);
        float cycle = ThornheartBehaviour.PhaseThreeRootTelegraphSeconds + ThornheartBehaviour.RecoverSeconds
            + ThornheartBehaviour.PhaseThreeAttackGapSeconds;
        Assert.That(_now - rootsAt, Is.EqualTo(cycle).Within(Dt * 3.5f));

        RunUntil(() => _events.Count<ThornPodPlanted>() > 0);
        Assert.That(_events.Count<ThornPodPlanted>(), Is.EqualTo(ThornheartBehaviour.PhaseThreePodsPerCast));
        Assert.That(_heart.AddCount, Is.EqualTo(ThornheartBehaviour.PhaseThreeMaxAdds));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Telegraphs_AtLeastPointSix(int phase)
    {
        _heart.OnPhaseEntered(phase);
        RunUntilTelegraphs(8);
        IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();
        for (int i = 0; i < telegraphs.Count; i++)
        {
            Assert.That(telegraphs[i].Duration, Is.GreaterThanOrEqualTo(0.6f), $"telegraph {i}");
        }
    }

    // Rule 7.
    [Test]
    public void Reset_DropsRing()
    {
        _heart.OnPhaseEntered(2);
        Place(Outside);
        Step();
        Assert.That(_heart.RingRadiusNow, Is.GreaterThan(0f));

        _heart.Reset();
        Assert.That(_heart.RingRadiusNow, Is.Zero);
        Assert.That(_heart.Phase, Is.Zero);
        float hp = _player.Health.Current;
        int rings = _events.Count<StrangleRing>();
        RunFor(2f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp), "No ring, no bite.");
        Assert.That(_events.Count<StrangleRing>(), Is.EqualTo(rings));
    }

    [Test]
    public void PhaseZero_AsBefore()
    {
        _heart.OnPhaseEntered(0);
        RunUntilTelegraphs(4);
        Assert.That(_heart.Phase, Is.Zero);
        Assert.That(_heart.RingRadiusNow, Is.Zero);
        Assert.That(_events.Count<StrangleRing>(), Is.Zero);

        IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();
        Assert.That(telegraphs[0].Duration, Is.EqualTo(ThornheartBehaviour.RootTelegraphSeconds));
        Assert.That(telegraphs[1].Duration, Is.EqualTo(ThornheartBehaviour.PodTelegraphSeconds));
        Assert.That(telegraphs[3].Duration, Is.EqualTo(ThornheartBehaviour.SlamTelegraphSeconds));

        IReadOnlyList<ThornArmed> armed = _events.Of<ThornArmed>();
        Assert.That(armed.Count, Is.EqualTo((2 * ThornheartBehaviour.RootSpokes) + 1));
        for (int i = 0; i < 2 * ThornheartBehaviour.RootSpokes; i++)
        {
            Assert.That(armed[i].Length, Is.EqualTo(ThornheartBehaviour.RootOuter - ThornheartBehaviour.RootInner), $"spoke {i}");
        }
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Oathbound(), silent, _intents, Capacity);
        var thorns = new ThornSystem(silent);
        var heart = new ThornheartBehaviour(_agent, thorns, new FixedRandom().Misc, Pod, Rootling);
        Place(Outside);
        heart.OnPhaseEntered(2);
        float now = 0f;
        Action step = () =>
        {
            now += Dt;
            thorns.Tick(now, Outside, player);
            _intents.Clear();
            heart.Tick(new EnemyTickContext(Dt, now, player, _intents, silent, _projectiles, _enemies));
        };

        // The ring announced and closed and a full rotation run first, so first-use growth is not counted.
        for (int i = 0; i < 400; i++)
        {
            step();
        }

        Assert.That(player.Health.Current, Is.LessThan(player.Health.MaxHp.Value), "The bite path ran.");
        AllocationAssert.None(step);
    }

    // Only the player moves; the boss keeps aiming +X, so no spoke or slam crosses +Z.
    private void Place(Vector3 player) => _agent.Blackboard.PlayerPosition = player;

    private void Ground(Vector3 player)
    {
        _ground.Clear();
        _ground.Add(player + new Vector3(6f, 0f, 0f));
        _ground.Add(player + new Vector3(-6f, 0f, 0f));
        _ground.Add(player + new Vector3(0f, 0f, 6f));
        _ground.Add(player + new Vector3(0f, 0f, -6f));
    }

    private void Step(bool census = false)
    {
        _now += Dt;
        _thorns.Tick(_now, _agent.Blackboard.PlayerPosition, _player);
        var ctx = new EnemyTickContext(Dt, _now, _player, _intents, _events, _projectiles, _enemies, openGround: _ground);
        if (census)
        {
            _enemies.Tick(ctx);
        }
        else
        {
            _heart.Tick(ctx);
        }
    }

    private void RunUntil(Func<bool> done, bool census = false)
    {
        for (int i = 0; i < 2_000 && !done(); i++)
        {
            Step(census);
        }

        Assert.That(done(), Is.True, "The condition never came.");
    }

    private void RunFor(float seconds)
    {
        float end = _now + seconds;
        while (_now < end - (Dt / 2f))
        {
            Step();
        }
    }

    private void RunUntilTelegraphs(int count) => RunUntil(() => _events.Count<EnemyTelegraph>() >= count);

    private DamageResult Hit(float damage) => _enemies.ApplyDamage(_agent.Id, damage, _now, _player);

    private static BossSpec Boss() => new BossSpec(
        new ContentId(BossId), new ContentId(BodyId),
        new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f), new BossPhaseSpec(0.33f) }, 1.5f);

    private static EnemySpec Body(string id, float hp, EnemyBehaviourKind kind = EnemyBehaviourKind.Static) => new EnemySpec(
        new ContentId(id), new LocKey(id + ".name"), hp, 0f, 8, 40, 300f, false,
        id == BodyId ? Contact : 0f, 2.5f, 0.9f, 1.2f, 40f, kind);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        1_000f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
