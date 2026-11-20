using System;
using System.Linq;
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

[TestFixture]
public sealed class HierophantBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private HierophantBehaviour _boss;
    private HierophantStats _stats;
    private float _now;
    private readonly ContentId _priest = new("enemy.jackal-priest");
    private readonly ContentId _slinger = new("enemy.jackal");

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _projectiles = new ProjectileSystem(_events, 8);
        CharacterSpec character = ShardbackBehaviourTests.Character();
        _player = new PlayerCombat(character, _events, _intents, 8);
        var body = new EnemySpec(new ContentId("enemy.hierophant"), new LocKey("enemy.hierophant.name"),
            2100f, 1.8f, 8, 40, 300f, false, 12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);
        var priest = AddSpec(_priest);
        var slinger = AddSpec(_slinger);
        var spec = new BossSpec(new ContentId("boss.hierophant"), body.Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f) }, 1.5f);
        _stats = new HierophantStats();
        _enemies = new EnemySystem(new ContentCatalog(new[] { character }, new[] { body, priest, slinger },
                bosses: new[] { spec }), _events, new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), 8,
            (agent, _) => new HierophantBehaviour(agent, _stats));
        _agent = _enemies.SpawnBoss(spec.Id, Vector3.Zero);
        _boss = (HierophantBehaviour)((BossBehaviour)_agent.Behaviour).Inner;
        _events.Clear();
        _now = 0f;
    }

    [TestCase(-17.5f, 0)]
    [TestCase(-4f, 0)]
    [TestCase(-2f, 0)]
    [TestCase(-1.99f, 1)]
    [TestCase(0f, 1)]
    [TestCase(2f, 1)]
    [TestCase(2.01f, 2)]
    [TestCase(4f, 2)]
    [TestCase(17.5f, 2)]
    public void Seal_ChoosesNearestAndLocks(float x, int slot)
    {
        var origin = new Vector3(1f, 4f, -2f);
        _agent.Blackboard.SelfPosition = origin;
        Place(x, 12f);
        Tick(10f);
        var layout = (HierophantSealsPlaced)_events.All[0];
        Assert.That((layout.BossId, layout.West, layout.Centre, layout.East),
            Is.EqualTo((_agent.Id, -Vector2.UnitX * 4f, Vector2.Zero, Vector2.UnitX * 4f)));
        HierophantArmed arm = LastArm();
        Assert.That((arm.BossId, arm.Move, arm.SealSlot, arm.Origin),
            Is.EqualTo((_agent.Id, HierophantMove.SunSeal, slot, new Vector3(origin.X, 0f, origin.Z))));
        Assert.That(Vector2.Distance(arm.Facing, Vector2.Normalize(new Vector2(x - origin.X, 12f - origin.Z))),
            Is.LessThan(0.0001f));
        Vector3 end = arm.Origin + new Vector3(arm.Facing.X, 0f, arm.Facing.Y) * arm.Reach;
        Assert.That(MathF.Max(MathF.Abs(end.X), MathF.Abs(end.Z)), Is.EqualTo(18f).Within(0.0001f));
        Assert.That((arm.HalfWidth, arm.Seconds), Is.EqualTo((2f, 1.3f)));
        Assert.That(_events.Count<HierophantTellProgress>(), Is.Zero);

        _stats.SealHalfWidth.Base = 0.5f;
        _stats.DunesHalfExtent.Base = 1f;
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 100f;
        Tick(1.3f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Assert.That(_events.Single<HierophantImpact>().SealSlot, Is.EqualTo(slot));
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));

        _boss.OnPhaseEntered(1);
        Place(1.5f, 0f);
        Tick(0f);
        Assert.That(_events.Count<HierophantSealsPlaced>(), Is.EqualTo(1));
        Assert.That((LastArm().SealSlot, LastArm().HalfWidth), Is.EqualTo((1, 2f)));
    }

    [Test]
    public void FireThrow_PathsThenCommits()
    {
        Place(0f, 20f);
        Tick(0f);
        Place(3f, 10f);
        Tick(1.3f);
        // Keep the fire out of its arming range on the tick that ends recovery.
        Place(0f, 8f);
        Tick(1.5f, Vector3.UnitZ * 2.25f);
        Place(8f, 0f);
        _agent.Blackboard.HasLineOfSight = false;
        _agent.Blackboard.PathDirectionToPlayer = -Vector2.UnitY;
        Tick(0.125f, -Vector3.UnitZ * 2.25f);
        Assert.That(_intents.LastEnemyMove.FacingXZ.X, Is.EqualTo(MathF.Sqrt(0.5f)).Within(0.0001f));
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));

        _agent.Blackboard.SelfPosition = new Vector3(2f, 1f, 0f);
        Place(9f, 0f);
        Tick(0.15f);
        HierophantArmed arm = LastArm();
        Assert.That((arm.Move, arm.SealSlot, arm.Origin, arm.Facing),
            Is.EqualTo((HierophantMove.FireThrow, -1, _agent.Blackboard.SelfPosition, Vector2.UnitX)));
        Assert.That((arm.Reach, arm.Seconds), Is.EqualTo((7f, 1f)));
        Assert.That(arm.HalfWidth, Is.EqualTo(4f * MathF.Sin(MathF.PI / 3f)).Within(0.00001f));

        Place(9f, 0f);
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 100f;
        _stats.FireReach.Base = 1f;
        _stats.FireDamage.Base = 1f;
        _stats.FireRecoverySeconds.Base = 0.25f;
        Tick(1f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(arm.Facing));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.8f);
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
        Assert.That(_events.Of<HierophantImpact>()[1].RecoverySeconds, Is.EqualTo(1.25f));
        Tick(0.25f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));
        Tick(0.21f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.SunSeal));
    }

    [Test]
    public void FireWave_HitsOnlyWhenTheStripReachesTheHero_AndOnlyOnce()
    {
        Place(0f, 20f);
        Tick(0f);
        Place(3f, 10f);
        Tick(1.3f);
        Place(0f, 5f);
        Tick(1.5f);
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));

        Tick(0.4f);
        HierophantFireWave middle = _events.Of<HierophantFireWave>()[^1];
        Assert.That((middle.Active, middle.Front), Is.EqualTo((true, 3.5f)));
        Assert.That(middle.Rear, Is.EqualTo(2.6f).Within(0.001f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.2f);
        Assert.That(_events.Of<HierophantFireWave>()[^1].Front, Is.EqualTo(5.25f).Within(0.001f));
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
        Place(0f, 7f);
        Tick(0.2f);
        Assert.That(_events.Of<HierophantFireWave>()[^1].Front, Is.EqualTo(7f));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        Tick(0.1f);
        Assert.That(_events.Of<HierophantFireWave>()[^1].Active, Is.False);
    }

    [Test]
    public void PhaseChange_CancelsFireInFlight()
    {
        Place(0f, 20f);
        Tick(0f);
        Place(3f, 10f);
        Tick(1.3f);
        Place(0f, 7f);
        Tick(1.5f);
        Tick(1f);
        Tick(0.4f);
        int updates = _events.Count<HierophantFireWave>();
        _boss.OnPhaseEntered(1);
        Tick(0.4f);
        Assert.That(_events.Count<HierophantFireWave>(), Is.EqualTo(updates));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    [TestCase(false, 0f, 0f, true)]
    [TestCase(false, 2f, 18f, true)]
    [TestCase(false, 0f, -0.001f, false)]
    [TestCase(false, 2.001f, 0f, false)]
    [TestCase(false, 0f, 18.001f, false)]
    [TestCase(false, float.NaN, 0f, false)]
    [TestCase(true, 0f, 0f, true)]
    [TestCase(true, 0f, 7f, true)]
    [TestCase(true, 0f, 7.001f, false)]
    [TestCase(true, 3.464f, 0.5f, true)]
    [TestCase(true, 3.465f, 0.5f, false)]
    [TestCase(true, 0f, -1f, false)]
    [TestCase(true, float.PositiveInfinity, 0f, false)]
    [TestCase(false, 0f, 0f, false, "decoy")]
    [TestCase(true, 0f, 1f, false, "decoy")]
    [TestCase(false, 0f, 0f, false, "dodge")]
    [TestCase(true, 0f, 1f, false, "dodge")]
    public void Impact_UsesHeroAndArmedGeometry(bool fire, float x, float z, bool hit, string defense = "")
    {
        Place(0f, fire ? 20f : 2f);
        Tick(0f);
        if (fire)
        {
            Place(3f, 10f);
            Tick(1.3f);
            Place(0f, 2f);
            Tick(2f);
        }
        HierophantArmed arm = LastArm();
        _events.Clear();
        _agent.Blackboard.HasLineOfSight = false;
        Tick(arm.Seconds * 0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<HierophantImpact>(), Is.Zero);
        HierophantTellProgress half = _events.Single<HierophantTellProgress>();
        Assert.That((half.BossId, half.Fraction), Is.EqualTo((_agent.Id, 0.5f)));

        Place(x, z);
        _agent.Blackboard.QuarryIsADecoy = defense == "decoy";
        if (defense == "dodge")
        {
            _player.Charge.Request(_now);
            _player.Tick(0f, _now, new WorldSnapshot(8), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            Assert.That(_player.Charge.IsInvulnerable, Is.True);
        }
        Tick(arm.Seconds * 0.5f);
        if (fire)
            Tick(0.8f);

        Assert.That(_player.Health.Current, Is.EqualTo(hit ? fire ? 84f : 80f : 100f));
        object[] timing = _events.All.Where(e => e is HierophantTellProgress or HierophantImpact).ToArray();
        Assert.That(timing.Length, Is.EqualTo(3));
        Assert.That(((HierophantTellProgress)timing[1]).Fraction, Is.EqualTo(1f));
        var impact = (HierophantImpact)timing[2];
        Assert.That((impact.BossId, impact.Move, impact.SealSlot, impact.RecoverySeconds),
            Is.EqualTo((_agent.Id, arm.Move, arm.SealSlot, fire ? 1.25f : 1.5f)));
        if (defense == "dodge")
            Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1), "The dodge blocks the damage attempt.");

        Tick(0f);
        Assert.That(_events.Count<HierophantImpact>(), Is.EqualTo(1));
    }

    [TestCase(-4f, 2f, 0, 1)]
    [TestCase(0f, 0f, 1, 0)]
    [TestCase(4f, -2f, 2, 0)]
    [TestCase(-4f, 12f, 0, 2)]
    public void FinalPair_ReaimsWithFullTiming(float firstX, float secondX, int first, int second)
    {
        _boss.OnPhaseEntered(2);
        Place(firstX, 12f);
        Tick(100f);
        Assert.That(LastArm().SealSlot, Is.EqualTo(first));
        Tick(100f);
        Assert.That(_events.Single<HierophantImpact>().RecoverySeconds, Is.Zero);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));

        Tick(0.15f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));
        Place(secondX, 12f);
        Tick(0.15f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));
        Assert.That(LastArm().SealSlot, Is.EqualTo(second));
        Assert.That(Vector2.Distance(LastArm().Facing, Vector2.Normalize(new Vector2(secondX, 12f))),
            Is.LessThan(0.0001f));
        Assert.That(LastArm().Seconds, Is.EqualTo(1.3f));
        Assert.That(_events.Count<HierophantTellProgress>(), Is.EqualTo(1));

        Tick(1.25f);
        Assert.That(_events.Count<HierophantImpact>(), Is.EqualTo(1));
        Tick(100f);
        HierophantImpact impact = _events.Of<HierophantImpact>()[1];
        Assert.That((impact.SealSlot, impact.RecoverySeconds), Is.EqualTo((second, 1.5f)));
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));

        Place(0f, 2f);
        Tick(1.25f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));
        Tick(0.25f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.FireThrow));
        Assert.That(_events.Count<HierophantImpact>(), Is.EqualTo(2));
        Tick(100f);
        Tick(100f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.SunSeal));
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(false, 3)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    [TestCase(true, 3)]
    public void PhaseOrReset_CancelsPendingWork(bool reset, int steps)
    {
        _boss.OnPhaseEntered(2);
        Place(0f, 20f);
        Tick(0f);
        for (int i = 0; i < steps; i++)
            Tick(100f); // First tell, gap, second tell, recovery.

        _events.Clear();
        _stats.SealHalfWidth.Base = 3f;
        if (reset)
            _boss.Reset();
        else
            _boss.OnPhaseEntered(1);

        Place(0f, 2f);
        Tick(0f);
        Assert.That(_events.Count<HierophantImpact>(), Is.Zero);
        Assert.That(_events.Count<HierophantTellProgress>(), Is.Zero);
        Assert.That(_events.Count<HierophantSealsPlaced>(), Is.EqualTo(reset ? 1 : 0));
        Assert.That((LastArm().Move, LastArm().SealSlot, LastArm().HalfWidth),
            Is.EqualTo((HierophantMove.SunSeal, 1, reset ? 3f : 2f)));
        Tick(1.3f);
        Assert.That(_events.Single<HierophantImpact>().RecoverySeconds, Is.EqualTo(1.5f));
        Tick(2f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.FireThrow));
        Tick(1f);
        Tick(1.25f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.SunSeal));
    }

    [Test]
    public void InvalidInputs_DoNotPoisonState()
    {
        Assert.Throws<ArgumentNullException>(() => new HierophantBehaviour(null, _stats));
        Assert.Throws<ArgumentNullException>(() => new HierophantBehaviour(_agent, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HierophantTellProgress(_agent.Id, float.NaN));
        Place(float.PositiveInfinity, 0f);
        Tick(1f);
        Assert.That(_events.Count<HierophantArmed>(), Is.Zero);
        Place(0f, 20f);
        Tick(1f);
        Tick(1.3f);
        _agent.Blackboard.PathDirectionToPlayer = new Vector2(float.NaN, 0f);
        Tick(1.5f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));
        Place(0f, 2f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.FireThrow));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void OutOfFireReach_RecastsAfterChase(int phase)
    {
        _boss.OnPhaseEntered(phase);
        Place(15f, 0f);
        Tick(0f);
        Tick(1.3f);
        if (phase == 2)
        {
            Tick(0.3f);
            Tick(1.3f);
        }

        int before = _events.Count<HierophantArmed>();
        Tick(1.5f, Vector3.UnitX * 2.25f);
        Tick(1.75f, Vector3.UnitX * 2.25f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(before));
        Place(0f, 15f);
        Tick(0.25f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.SunSeal));
        Assert.That(LastArm().Facing, Is.EqualTo(Vector2.UnitY));

        if (phase == 2)
        {
            Tick(1.3f);
            Place(-15f, 0f);
            Tick(0.3f);
            Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.SunSeal));
            Assert.That(LastArm().Facing, Is.EqualTo(-Vector2.UnitX));
        }
    }

    [Test]
    public void Escort_WaitsForOpeningAndBothSlots_ThenReplacesOnlyAfterLastLoss()
    {
        EnemyAgent regularPriest = _enemies.Spawn(_priest, Vector3.Zero);
        EnemyAgent regularSlinger = _enemies.Spawn(_slinger, Vector3.Zero);
        float regularSlingerHp = regularSlinger.Health.Current;
        float regularSlingerDamage = regularSlinger.ContactDamage.Value;
        for (int i = 0; i < 4; i++)
            _enemies.Spawn(_slinger, Vector3.Zero);

        Place(0f, 2f);
        Tick(0f);
        Tick(1.3f);
        Tick(1.5f);
        Tick(1f);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(7));
        Tick(1.25f);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(7), "One free slot cannot produce half a pair.");
        _enemies.Despawn(regularSlinger.Id);
        Tick(0f);

        EnemyAgent priest = _enemies.Registry.Alive[^2];
        EnemyAgent slinger = _enemies.Registry.Alive[^1];
        Assert.That((priest.Spec.Id, slinger.Spec.Id), Is.EqualTo((_priest, _slinger)));
        Assert.That(priest.Health.Current, Is.EqualTo(regularPriest.Health.MaxHp.Value * 1.25f).Within(0.01f));
        Assert.That(slinger.Health.Current, Is.EqualTo(regularSlingerHp * 1.25f).Within(0.01f));
        Assert.That(slinger.ContactDamage.Value, Is.EqualTo(regularSlingerDamage * 1.25f).Within(0.01f));
        Assert.That(priest.IsSummon && slinger.IsSummon, Is.True);
        _enemies.ApplyDamage(priest.Id, 1000f, _now, _player);
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.False);
        _enemies.Despawn(priest.Id);
        Tick(20f);
        Assert.That(_enemies.Registry.Alive[^1], Is.SameAs(slinger), "One survivor holds the pair slot.");
        _enemies.ApplyDamage(slinger.Id, 1000f, _now, _player);
        _enemies.Despawn(slinger.Id);
        int before = _events.Count<EnemySpawned>();
        Tick(17.9f);
        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(before));
        Tick(0.2f);
        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(before + 2));
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.True);
        _enemies.Despawn(_enemies.Registry.Alive[^2].Id);
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.False);
    }

    [TestCase(400f, 105f)]
    [TestCase(40f, 40f)]
    public void Priest_HealsOnceAfterTheWardBreak(float missing, float restored)
    {
        EnemyAgent priest = OpenWard();
        _agent.Health.ApplyDamage(missing, _now);
        _enemies.ApplyDamage(priest.Id, 1f, _now, _player);
        Tick(4.9f);
        Assert.That(_events.Count<HierophantHealChannel>(), Is.Zero);
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.False);

        Tick(0.11f);
        Assert.That(_events.Single<HierophantHealChannel>().Active, Is.True);
        Tick(1.49f);
        Assert.That(_events.Count<EnemyHealed>(), Is.Zero);
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.False);
        Tick(0.02f);
        EnemyHealed heal = _events.Single<EnemyHealed>();
        Assert.That((heal.EnemyId, heal.Amount), Is.EqualTo((_agent.Id, restored)));
        Assert.That(_events.Of<HierophantHealChannel>()[^1].Active, Is.False);
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.True);

        _agent.Health.ApplyDamage(50f, _now);
        _enemies.ApplyDamage(priest.Id, 1f, _now, _player);
        Tick(7f);
        Assert.That(_events.Count<EnemyHealed>(), Is.EqualTo(1));
        Assert.That(_events.Of<HierophantWard>()[^1].Active, Is.True);
    }

    [TestCase("hit")]
    [TestCase("priest death")]
    [TestCase("priest despawn")]
    [TestCase("boss death")]
    [TestCase("boss despawn")]
    [TestCase("phase")]
    public void Priest_ChannelCancellation_DoesNotDeliverOrRetry(string cause)
    {
        EnemyAgent priest = OpenWard();
        var wrapper = (BossBehaviour)_agent.Behaviour;
        _agent.Health.ApplyDamage(400f, _now);
        _enemies.ApplyDamage(priest.Id, 1f, _now, _player);
        Tick(5.1f);
        Assert.That(_events.Single<HierophantHealChannel>().Active, Is.True);

        if (cause == "phase")
        {
            _enemies.ApplyDamage(_agent.Id, 500f, _now, _player);
            wrapper.Tick(new EnemyTickContext(0.01f, _now, _player, _intents, _events, _projectiles, _enemies));
            Assert.That(wrapper.IsInBeat, Is.True);
        }
        else if (cause.EndsWith("despawn", StringComparison.Ordinal))
            _enemies.Despawn(cause.StartsWith("boss", StringComparison.Ordinal) ? _agent.Id : priest.Id);
        else
            _enemies.ApplyDamage(cause == "boss death" ? _agent.Id : priest.Id,
                cause == "hit" ? 1f : 9000f, _now, _player);

        Assert.That(_events.Count<HierophantHealChannel>(), Is.EqualTo(2));
        Assert.That(_events.Of<HierophantHealChannel>()[^1].Active, Is.False);
        if (cause != "boss death" && cause != "boss despawn")
            Tick(7f);
        Assert.That(_events.Count<HierophantHealChannel>(), Is.EqualTo(2));
        Assert.That(_events.Count<EnemyHealed>(), Is.Zero);
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(ShardbackBehaviourTests.Character(), silent, _intents, 8);
        for (int i = 0; i < 6; i++)
            _enemies.Spawn(_slinger, Vector3.Zero);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            player.Health.Reset();
            _boss.Reset();
            _boss.OnPhaseEntered(2);
            Place(0f, 2f);
            for (int i = 0; i < 9; i++)
            {
                // Two seal tells and releases, a fire approach, tell, release and recovery.
                Place(0f, i == 5 ? 12f : 2f);
                _boss.Tick(new EnemyTickContext(3f, i * 3f, player, _intents, silent, _projectiles, _enemies));
            }
        }, 100);
    }

    private static EnemySpec AddSpec(ContentId id) =>
        new(id, new LocKey(id.Value + ".name"), 100f, 2f, 3, 10, 20f, false,
            12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);

    private EnemyAgent OpenWard()
    {
        Place(0f, 20f);
        Tick(0f);
        Tick(1.3f);
        Place(0f, 2f);
        Tick(1.5f);
        Tick(1f);
        Tick(1.25f);
        Assert.That(_events.Single<HierophantWard>().Active, Is.True);
        return _enemies.Registry.Alive[^2];
    }

    private HierophantArmed LastArm() => _events.Of<HierophantArmed>()[^1];

    private void Place(float x, float z)
    {
        _agent.Blackboard.PlayerPosition = new Vector3(x, 40f, z);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.Zero;
    }

    private void Tick(float dt, Vector3 velocity = default)
    {
        int before = _intents.EnemyMoves.Count;
        _now += dt;
        _boss.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(before + 1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(velocity));
    }
}
