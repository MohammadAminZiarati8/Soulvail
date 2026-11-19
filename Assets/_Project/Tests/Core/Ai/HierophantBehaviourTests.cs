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

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _projectiles = new ProjectileSystem(_events, 8);
        CharacterSpec character = ShardbackBehaviourTests.Character();
        _player = new PlayerCombat(character, _events, _intents, 8);
        var body = new EnemySpec(new ContentId("enemy.hierophant"), new LocKey("enemy.hierophant.name"),
            500f, 1.8f, 8, 40, 300f, false, 12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);
        _enemies = new EnemySystem(new ContentCatalog(new[] { character }, new[] { body }), _events,
            new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(body, Vector3.Zero);
        _stats = new HierophantStats();
        _boss = new HierophantBehaviour(_agent, _stats);
        _now = 0f;
    }

    [TestCase(-100f, 0)]
    [TestCase(-4f, 0)]
    [TestCase(-2f, 0)]
    [TestCase(-1.99f, 1)]
    [TestCase(0f, 1)]
    [TestCase(2f, 1)]
    [TestCase(2.01f, 2)]
    [TestCase(4f, 2)]
    [TestCase(float.MaxValue, 2)]
    public void Seal_ChoosesNearestAndLocks(float x, int slot)
    {
        _agent.Blackboard.SelfPosition = new Vector3(30f, 4f, -20f);
        Place(x, 20f);
        Tick(10f);
        var layout = (HierophantSealsPlaced)_events.All[0];
        Assert.That((layout.BossId, layout.West, layout.Centre, layout.East),
            Is.EqualTo((_agent.Id, -Vector2.UnitX * 4f, Vector2.Zero, Vector2.UnitX * 4f)));
        HierophantArmed arm = LastArm();
        float centre = (slot - 1) * 4f;
        Assert.That((arm.BossId, arm.Move, arm.SealSlot, arm.Origin, arm.Facing),
            Is.EqualTo((_agent.Id, HierophantMove.SunSeal, slot, Vector3.UnitX * centre, Vector2.UnitY)));
        Assert.That((arm.Reach, arm.HalfWidth, arm.HalfAngleDegrees, arm.Seconds), Is.EqualTo((9f, 2f, 0f, 1.3f)));
        Assert.That(_events.Count<HierophantTellProgress>(), Is.Zero);

        _stats.SealHalfWidth.Base = 0.5f;
        _stats.SealHalfLength.Base = 1f;
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 100f;
        Place(centre + 2f, 9f);
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
    public void Sweep_PathsThenCommits()
    {
        Place(0f, 20f);
        Tick(0f);
        Tick(1.3f);
        Tick(1.875f);
        Place(4f, 0f);
        _agent.Blackboard.HasLineOfSight = false;
        _agent.Blackboard.PathDirectionToPlayer = -Vector2.UnitY;
        Tick(0.125f, -Vector3.UnitZ * 2.25f);
        Assert.That(_intents.LastEnemyMove.FacingXZ.X, Is.EqualTo(MathF.Sqrt(0.5f)).Within(0.0001f));
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));

        _agent.Blackboard.SelfPosition = new Vector3(2f, 1f, 0f);
        Place(5.25f, 0f);
        Tick(0.15f);
        HierophantArmed arm = LastArm();
        Assert.That((arm.Move, arm.SealSlot, arm.Origin, arm.Facing),
            Is.EqualTo((HierophantMove.StaffSweep, -1, _agent.Blackboard.SelfPosition, Vector2.UnitX)));
        Assert.That((arm.Reach, arm.HalfWidth, arm.HalfAngleDegrees, arm.Seconds), Is.EqualTo((3.25f, 0f, 50f, 1f)));

        Place(5.25f, 0f);
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 100f;
        _stats.SweepRange.Base = 1f;
        _stats.SweepDamage.Base = 1f;
        _stats.SweepRecoverySeconds.Base = 0.25f;
        Tick(1f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(arm.Facing));
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
        Assert.That(_events.Of<HierophantImpact>()[1].RecoverySeconds, Is.EqualTo(1.25f));
        Tick(1f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));
        Tick(0.25f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.SunSeal));
    }

    [TestCase(false, 0f, 0f, true)]
    [TestCase(false, 2f, -9f, true)]
    [TestCase(false, 2.001f, 0f, false)]
    [TestCase(false, 0f, 9.001f, false)]
    [TestCase(false, float.NaN, 0f, false)]
    [TestCase(true, 0f, 0f, true)]
    [TestCase(true, 0f, 3.25f, true)]
    [TestCase(true, 0f, 3.251f, false)]
    [TestCase(true, 0.76604444f, 0.64278764f, true)] // 50 degrees.
    [TestCase(true, 0.77f, 0.64f, false)]
    [TestCase(true, 0f, -1f, false)]
    [TestCase(true, float.PositiveInfinity, 0f, false)]
    [TestCase(false, 0f, 0f, false, "decoy")]
    [TestCase(true, 0f, 1f, false, "decoy")]
    [TestCase(false, 0f, 0f, false, "dodge")]
    [TestCase(true, 0f, 1f, false, "dodge")]
    public void Impact_UsesHeroAndArmedGeometry(bool sweep, float x, float z, bool hit, string defense = "")
    {
        Place(0f, sweep ? 20f : 2f);
        Tick(0f);
        if (sweep)
        {
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

        Assert.That(_player.Health.Current, Is.EqualTo(hit ? sweep ? 84f : 80f : 100f));
        object[] timing = _events.All.Where(e => e is HierophantTellProgress or HierophantImpact).ToArray();
        Assert.That(timing.Length, Is.EqualTo(3));
        Assert.That(((HierophantTellProgress)timing[1]).Fraction, Is.EqualTo(1f));
        var impact = (HierophantImpact)timing[2];
        Assert.That((impact.BossId, impact.Move, impact.SealSlot, impact.RecoverySeconds),
            Is.EqualTo((_agent.Id, arm.Move, arm.SealSlot, sweep ? 1.25f : 2f)));
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
        Place(firstX, 20f);
        Tick(100f);
        Assert.That(LastArm().SealSlot, Is.EqualTo(first));
        Tick(100f);
        Assert.That(_events.Single<HierophantImpact>().RecoverySeconds, Is.Zero);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));

        Tick(0.15f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));
        Place(secondX, 20f);
        Tick(0.15f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));
        Assert.That(LastArm().SealSlot, Is.EqualTo(second));
        Assert.That(LastArm().Seconds, Is.EqualTo(1.3f));
        Assert.That(_events.Count<HierophantTellProgress>(), Is.EqualTo(1));

        Tick(1.25f);
        Assert.That(_events.Count<HierophantImpact>(), Is.EqualTo(1));
        Tick(100f);
        HierophantImpact impact = _events.Of<HierophantImpact>()[1];
        Assert.That((impact.SealSlot, impact.RecoverySeconds), Is.EqualTo((second, 2f)));
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));

        Place(0f, 2f);
        Tick(1.75f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(2));
        Tick(0.25f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.StaffSweep));
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
        Assert.That(_events.Single<HierophantImpact>().RecoverySeconds, Is.EqualTo(2f));
        Tick(2f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.StaffSweep));
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
        Tick(2f);
        Assert.That(_events.Count<HierophantArmed>(), Is.EqualTo(1));
        Place(0f, 2f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(HierophantMove.StaffSweep));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(ShardbackBehaviourTests.Character(), silent, _intents, 8);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            player.Health.Reset();
            _boss.Reset();
            _boss.OnPhaseEntered(2);
            Place(0f, 2f);
            for (int i = 0; i < 9; i++)
            {
                // Two seal tells and releases, a sweep approach, tell, release and recovery.
                Place(0f, i == 5 ? 12f : 2f);
                _boss.Tick(new EnemyTickContext(3f, i * 3f, player, _intents, silent, _projectiles, _enemies));
            }
        }, 100);
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
