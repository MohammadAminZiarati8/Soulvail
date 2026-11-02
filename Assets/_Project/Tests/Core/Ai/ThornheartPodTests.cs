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

/// <summary>JG-20c rules 1–6: Thornheart's seed pods, the Rootlings they grow and the roll-call.</summary>
[TestFixture]
public sealed class ThornheartPodTests
{
    private const string BodyId = "enemy.thornheart";
    private const string OathboundId = "character.oathbound";
    private const int Capacity = 32;
    private const float Dt = 0.05f;
    private const float Tolerance = 1e-4f;
    private const float Lethal = 1_000_000f;

    private static readonly ContentId Pod = new ContentId("enemy.thorn_pod");
    private static readonly ContentId Rootling = new ContentId("enemy.rootling");

    /// <summary>Far up the boss's +Z spoke's line, beyond every wall's reach.</summary>
    private static readonly Vector3 PlayerAt = new Vector3(0f, 0f, 30f);

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
            new[] { Body(BodyId, 5_000f), Body(Pod.Value, 50f), Body(Rootling.Value, 20f) },
            modes: null, skills: null, trees: null, bosses: null);
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(),
            new DepthScaling(Scalings.Design()), Capacity);
        _agent = _enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitY;
        _agent.Blackboard.PlayerPosition = PlayerAt;
        _heart = new ThornheartBehaviour(_agent, _thorns, new FixedRandom().Misc, Pod, Rootling);
        _ground = new List<Vector3>
        {
            PlayerAt + new Vector3(6f, 0f, 0f),
            PlayerAt + new Vector3(-6f, 0f, 0f),
            PlayerAt + new Vector3(0f, 0f, 6f),
            PlayerAt + new Vector3(0f, 0f, -6f),
        };
        _now = 0f;
    }

    // Rule 1.
    [Test]
    public void Rotation_RootsPodsRootsSlam()
    {
        var times = new List<float>();
        var moves = new List<int>();
        for (int i = 0; i < 400 && times.Count < 5; i++)
        {
            int before = _events.Count<EnemyTelegraph>();
            Step();
            if (_events.Count<EnemyTelegraph>() > before)
            {
                times.Add(_now);
                moves.Add(_events.Of<EnemyTelegraph>()[before].Move);
                Assert.That((int)_heart.Move, Is.EqualTo(moves[moves.Count - 1]));
            }
        }

        Assert.That(moves, Is.EqualTo(new[] { 1, 3, 1, 2, 1 }));
        float rootsCycle = Cycle(ThornheartBehaviour.RootTelegraphSeconds);
        Assert.That(times[1] - times[0], Is.EqualTo(rootsCycle).Within(Dt * 3.5f));
        Assert.That(times[2] - times[1], Is.EqualTo(Cycle(ThornheartBehaviour.PodTelegraphSeconds)).Within(Dt * 3.5f));
        Assert.That(times[3] - times[2], Is.EqualTo(rootsCycle).Within(Dt * 3.5f));
        Assert.That(times[4] - times[3], Is.EqualTo(Cycle(ThornheartBehaviour.SlamTelegraphSeconds)).Within(Dt * 3.5f));
    }

    // Rule 2.
    [Test]
    public void Pods_TwoPlanted_OnOpenGround_NotOnThorns()
    {
        RunUntilTelegraphs(2);
        float telegraphAt = _now;
        _thorns.Open(PlayerAt + new Vector3(-10f, 0f, 0f), Vector2.UnitX, 20f, 0.5f, 1_000f, 1f, 0f, _now);

        RunUntil(() => _events.Count<ThornPodPlanted>() >= 1);
        Assert.That(_now - telegraphAt, Is.EqualTo(ThornheartBehaviour.PodTelegraphSeconds).Within(Dt * 1.5f));
        IReadOnlyList<ThornPodPlanted> planted = _events.Of<ThornPodPlanted>();
        Assert.That(planted.Count, Is.EqualTo(ThornheartBehaviour.PodsPerCast));
        Assert.That(planted[0].At, Is.Not.EqualTo(planted[1].At));
        for (int i = 0; i < planted.Count; i++)
        {
            Assert.That(planted[i].BossId, Is.EqualTo(_agent.Id));
            Assert.That(planted[i].RipenSeconds, Is.EqualTo(ThornheartBehaviour.PodRipenSeconds));
            Assert.That(planted[i].At.X, Is.EqualTo(PlayerAt.X), "The walled ±X ground is never picked.");
            Assert.That(_thorns.Blocks(planted[i].At, ThornheartBehaviour.PodClearance), Is.False);
            Assert.That(_enemies.Registry.TryGet(planted[i].PodId, out EnemyAgent pod), Is.True);
            Assert.That(pod.Spec.Id, Is.EqualTo(Pod));
            Assert.That(pod.Position, Is.EqualTo(planted[i].At));
            Assert.That(pod.PaysXp, Is.False);
        }

        Assert.That(_heart.PodCount, Is.EqualTo(2));
        Assert.That(_heart.AddCount, Is.EqualTo(2 * ThornheartBehaviour.RootlingsPerPod));
    }

    [Test]
    public void Pods_PickerFails_Skipped()
    {
        _ground.Clear();
        RunUntilTelegraphs(3);
        Assert.That(_events.Count<ThornPodPlanted>(), Is.Zero, "No open ground: nothing planted.");
        Assert.That(_heart.PodCount, Is.Zero);
        Assert.That(_events.Of<EnemyTelegraph>()[2].Move, Is.EqualTo((int)ThornheartMove.Roots), "The rotation ran on.");

        _ground.Add(PlayerAt + new Vector3(6f, 0f, 0f));
        RunUntilPodsCastEnds(3);
        Assert.That(_events.Count<ThornPodPlanted>(), Is.EqualTo(1), "One spot: the second pick would stack, so it is skipped.");
    }

    // Rule 3.
    [Test]
    public void Pod_Survives_RipensIntoThreeRootlings_PayNoXp()
    {
        Plant();
        IReadOnlyList<ThornPodPlanted> planted = _events.Of<ThornPodPlanted>();
        float plantedAt = _now;

        RunUntil(() => Alive(Rootling) > 0);
        Assert.That(_now - plantedAt, Is.EqualTo(ThornheartBehaviour.PodRipenSeconds).Within(Dt * 1.5f));
        Assert.That(Alive(Rootling), Is.EqualTo(2 * ThornheartBehaviour.RootlingsPerPod));
        Assert.That(Alive(Pod), Is.Zero);
        Assert.That(_enemies.Registry.TryGet(planted[0].PodId, out _), Is.False, "The pod despawned.");
        Assert.That(_heart.PodCount, Is.Zero);
        Assert.That(_heart.AddCount, Is.EqualTo(6));

        var first = new List<EnemyAgent>();
        foreach (EnemyAgent agent in _enemies.Registry.Alive)
        {
            if (agent.Spec.Id == Rootling)
            {
                Assert.That(agent.PaysXp, Is.False);
                if (first.Count < ThornheartBehaviour.RootlingsPerPod)
                {
                    first.Add(agent);
                }
            }
        }

        for (int i = 0; i < first.Count; i++)
        {
            float angle = i * (2f * MathF.PI / ThornheartBehaviour.RootlingsPerPod);
            Vector3 expected = planted[0].At + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            Assert.That(Vector3.Distance(first[i].Position, expected), Is.LessThan(Tolerance), $"rootling {i}");
        }

        Assert.That(Kill(first[0].Id).Killed, Is.True, "Still kills.");
        Step();
        Assert.That(_heart.AddCount, Is.EqualTo(5));
    }

    [Test]
    public void Pod_Killed_GrowsNothing()
    {
        Plant();
        IReadOnlyList<ThornPodPlanted> planted = _events.Of<ThornPodPlanted>();
        Kill(planted[0].PodId);
        Kill(planted[1].PodId);
        RunFor(ThornheartBehaviour.PodRipenSeconds + 0.5f);
        Assert.That(Alive(Rootling), Is.Zero);
        Assert.That(_heart.AddCount, Is.Zero);
    }

    [Test]
    public void Ripen_NoRoom_Skipped()
    {
        Plant();
        while (_enemies.Registry.AliveCount < Capacity - 1)
        {
            _enemies.Spawn(new ContentId(BodyId), new Vector3(-60f, 0f, _enemies.Registry.AliveCount));
        }

        Assert.DoesNotThrow(() => RunUntil(() => _heart.PodCount == 0));
        Assert.That(Alive(Rootling), Is.EqualTo(1), "One slot: the rest are skipped.");
        Assert.That(_heart.AddCount, Is.EqualTo(1));
    }

    // Rule 4.
    [Test]
    public void Cap_FullRoster_NoPod()
    {
        PlantAndRipen();
        int telegraphs = _events.Count<EnemyTelegraph>();
        RunUntilPodsCastEnds(telegraphs);
        Assert.That(_events.Count<ThornPodPlanted>(), Is.EqualTo(2), "6 + 3 > 8: no pod.");

        Kill(FirstAlive(Rootling));
        telegraphs = _events.Count<EnemyTelegraph>();
        RunUntilPodsCastEnds(telegraphs);
        Assert.That(_events.Count<ThornPodPlanted>(), Is.EqualTo(3), "5 + 3 fits once; 8 + 3 does not.");
        Assert.That(_heart.AddCount, Is.EqualTo(ThornheartBehaviour.MaxAdds));
    }

    // Rule 5.
    [Test]
    public void RollCall_DeadLeaves()
    {
        Plant();
        Kill(_events.Of<ThornPodPlanted>()[0].PodId);
        Step();
        Assert.That(_heart.PodCount, Is.EqualTo(1));
        Assert.That(_heart.AddCount, Is.EqualTo(3));

        RunUntil(() => _heart.PodCount == 0);
        Assert.That(_heart.AddCount, Is.EqualTo(3));
        Kill(FirstAlive(Rootling));
        Step();
        Assert.That(_heart.AddCount, Is.EqualTo(2));
    }

    [Test]
    public void ClearAdds_DespawnsPodsAndRootlings()
    {
        PlantAndRipen();
        for (int i = 0; i < 3; i++)
        {
            Kill(FirstAlive(Rootling));
        }

        RunUntilPodsCastEnds(_events.Count<EnemyTelegraph>());
        Assert.That(_heart.PodCount, Is.EqualTo(1));
        Assert.That(Alive(Rootling), Is.EqualTo(3));

        int despawned = _events.Count<EnemyDespawned>();
        EnemyTickContext ctx = Ctx();
        _heart.ClearAdds(ctx);
        _enemies.Tick(ctx);
        Assert.That(Alive(Pod) + Alive(Rootling), Is.Zero);
        Assert.That(_events.Count<EnemyDespawned>() - despawned, Is.EqualTo(4));
        Assert.That(_heart.AddCount, Is.Zero);
        Assert.That(_heart.PodCount, Is.Zero);
    }

    // Rule 6.
    [Test]
    public void Reset_EmptiesRollCall()
    {
        Plant();
        IReadOnlyList<ThornPodPlanted> planted = _events.Of<ThornPodPlanted>();
        int despawned = _events.Count<EnemyDespawned>();
        _heart.Reset();
        Assert.That(_heart.PodCount, Is.Zero);
        Assert.That(_heart.AddCount, Is.Zero);

        // The restarted rotation plants fresh pods too, but none of them ripens in this window.
        RunFor(ThornheartBehaviour.PodRipenSeconds + 0.5f);
        for (int i = 0; i < ThornheartBehaviour.PodsPerCast; i++)
        {
            Assert.That(_enemies.Registry.TryGet(planted[i].PodId, out EnemyAgent pod) && pod.IsAlive, Is.True,
                "Not despawned, and no longer ripening.");
        }

        Assert.That(Alive(Rootling), Is.Zero);
        Assert.That(_events.Count<EnemyDespawned>(), Is.EqualTo(despawned));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var thorns = new ThornSystem(silent);
        var heart = new ThornheartBehaviour(_agent, thorns, new FixedRandom().Misc, Pod, Rootling);
        float now = 0f;
        Action step = () =>
        {
            now += Dt;
            thorns.Tick(now, PlayerAt, _player);
            _intents.Clear();
            heart.Tick(new EnemyTickContext(Dt, now, _player, _intents, silent, _projectiles, _enemies,
                openGround: _ground));
        };

        // Pods planted and ripened first, so first-use growth is not counted; the cap then holds.
        for (int i = 0; i < 400; i++)
        {
            step();
        }

        Assert.That(heart.AddCount, Is.EqualTo(6), "Warm-up reached the steady roster.");
        AllocationAssert.None(step);
    }

    private static float Cycle(float telegraph) =>
        telegraph + ThornheartBehaviour.RecoverSeconds + ThornheartBehaviour.AttackGapSeconds;

    private EnemyTickContext Ctx() =>
        new EnemyTickContext(Dt, _now, _player, _intents, _events, _projectiles, _enemies, openGround: _ground);

    private void Step()
    {
        _now += Dt;
        _thorns.Tick(_now, PlayerAt, _player);
        EnemyTickContext ctx = Ctx();
        _heart.Tick(ctx);
        _enemies.Tick(ctx);
    }

    private void RunUntil(Func<bool> done)
    {
        for (int i = 0; i < 2_000 && !done(); i++)
        {
            Step();
        }

        Assert.That(done(), Is.True, "The condition never came.");
    }

    private void RunFor(float seconds)
    {
        float end = _now + seconds;
        while (_now < end)
        {
            Step();
        }
    }

    private void RunUntilTelegraphs(int count) => RunUntil(() => _events.Count<EnemyTelegraph>() >= count);

    // Through the next Pods telegraph after `after` and past its plant.
    private void RunUntilPodsCastEnds(int after)
    {
        RunUntil(() =>
        {
            IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();
            for (int i = after; i < telegraphs.Count; i++)
            {
                if (telegraphs[i].Move == (int)ThornheartMove.Pods)
                {
                    return true;
                }
            }

            return false;
        });
        RunFor(ThornheartBehaviour.PodTelegraphSeconds + (2f * Dt));
    }

    private void Plant() => RunUntil(() => _events.Count<ThornPodPlanted>() >= ThornheartBehaviour.PodsPerCast);

    private void PlantAndRipen()
    {
        Plant();
        RunUntil(() => _heart.PodCount == 0);
        Assert.That(Alive(Rootling), Is.EqualTo(6));
    }

    private int Alive(ContentId spec)
    {
        int count = 0;
        foreach (EnemyAgent agent in _enemies.Registry.Alive)
        {
            if (agent.IsAlive && agent.Spec.Id == spec)
            {
                count++;
            }
        }

        return count;
    }

    private int FirstAlive(ContentId spec)
    {
        foreach (EnemyAgent agent in _enemies.Registry.Alive)
        {
            if (agent.IsAlive && agent.Spec.Id == spec)
            {
                return agent.Id;
            }
        }

        throw new AssertionException($"No living {spec}.");
    }

    private DamageResult Kill(int id) => _enemies.ApplyDamage(id, Lethal, _now, _player);

    private static EnemySpec Body(string id, float hp) => new EnemySpec(
        new ContentId(id), new LocKey(id + ".name"), hp, 0f, 8, 40, 300f, false,
        0f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Static);

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
