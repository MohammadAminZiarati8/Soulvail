using System;
using System.Numerics;
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

[TestFixture]
public sealed class GlassbackShineTests
{
    private const int Capacity = 8;
    private const int TicksPerSecond = 32;
    private const float Frame = 1f / TicksPerSecond;

    private static readonly ContentId Glassback = new("enemy.glassback");
    private static readonly ContentId Scorpion = new("enemy.scorpion");
    private static readonly ContentId Other = new("enemy.other");

    private RecordingEvents _events;
    private IDomainEvents _domainEvents;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;
    private EnemyAgent _agent;
    private float _clock;

    private ChaserBehaviour Chaser => (ChaserBehaviour)_agent.Behaviour;
    private ChaserState State => Chaser.State;

    [Test]
    public void NoShine_ChaserUnchanged()
    {
        Build(id: Scorpion, distance: 1f);
        Target(_agent);
        Reach(ChaserState.Windup);
        Reach(ChaserState.Strike);
        Reach(ChaserState.Recover);
        Reach(ChaserState.Chase);
        Assert.That(_events.Count<EnemyShineChanged>(), Is.Zero);
    }

    [Test]
    public void Spec_BadPairings_Throw()
    {
        ShineSpec shine = Shell();
        Assert.That(Assert.Throws<ArgumentException>(() => Spec(Other, shine, EnemyBehaviourKind.Static)).ParamName,
            Is.EqualTo("shine"));
        Assert.That(Assert.Throws<ArgumentException>(() => Spec(Glassback, shine, pack: new PackSpec(3f, 30f, 1f))).ParamName,
            Is.EqualTo("shine"));
        Assert.That(Assert.Throws<ArgumentException>(() => Spec(Glassback, shine,
            leap: new LeapSpec(2f, 5f, 0.5f, 0.5f, 2f, 1f, 1f))).ParamName, Is.EqualTo("shine"));
        Assert.That(Assert.Throws<ArgumentException>(() => Spec(Glassback, shine,
            brood: new BroodSpec(Other, 1, EnemySystem.CorpseTime / 2f, 1f))).ParamName, Is.EqualTo("shine"));

        float[] defaults = { 0.5f, 2f, 1f, 4f, 12f, 2f, 6f, 14f, 0.8f, 0.3f, 0.4f };
        for (int i = 0; i < defaults.Length; i++)
        {
            foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                var values = (float[])defaults.Clone();
                values[i] = invalid;
                Assert.Throws<ArgumentOutOfRangeException>(() => new ShineSpec(values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7], values[8], values[9], values[10]));
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new ShineSpec(0.5f, 2f, 1f, 4f, 12f, 0.5f, 6f, 14f, 0.8f, 0.3f, 0.4f));
    }

    [Test]
    public void Targeted_InRange_Glints()
    {
        Build();
        Target(_agent);
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Chase));
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Glint));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(-Vector2.UnitY));
        AssertPhase(_events.Single<EnemyShineChanged>(), ShinePhase.Glint, 0.5f);
    }

    [Test]
    public void NotTargeted_NeverGlints()
    {
        Build();
        EnemyAgent other = _system.Spawn(Other, Vector3.UnitX);
        Target(other);
        Advance(10 * TicksPerSecond);
        Assert.That(State, Is.EqualTo(ChaserState.Chase));

        _snapshot.PlayerPosition = _agent.Position - Vector3.UnitZ;
        Reach(ChaserState.Windup);
        Target(_agent);
        Reach(ChaserState.Strike);
        Reach(ChaserState.Recover);
        Reach(ChaserState.Chase);
        Assert.That(_events.Count<EnemyShineChanged>(), Is.Zero);
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Glint));
    }

    [Test]
    public void OutOfRange_NeverGlints()
    {
        Build(distance: 13f);
        Target(_agent);
        Advance(2);
        Assert.That(State, Is.EqualTo(ChaserState.Chase));
        Assert.That(_events.Count<EnemyShineChanged>(), Is.Zero);
    }

    [Test]
    public void Glint_DamageLands()
    {
        Build();
        Target(_agent);
        Reach(ChaserState.Glint);
        Assert.That(Hit(10f).Applied, Is.EqualTo(10f));
    }

    [Test]
    public void Glint_Stunned_CancelsAndCools()
    {
        Build();
        Target(_agent);
        Reach(ChaserState.Glint);
        Assert.That(_system.Stun(_agent.Id, Frame / 2f), Is.True);
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Chase));
        Advance(4 * TicksPerSecond - 1);
        Assert.That(State, Is.EqualTo(ChaserState.Chase));
        Assert.That(_events.Of<EnemyShineChanged>().Count, Is.EqualTo(2));
        AssertPhase(_events.Of<EnemyShineChanged>()[1], ShinePhase.None, 0f);
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Glint));
    }

    [Test]
    public void Shine_BlocksDamage_StaysTargetable()
    {
        Build();
        Target(_agent);
        Reach(ChaserState.Shine);
        DamageResult hit = Hit(10f);
        Assert.That(hit.Blocked, Is.True);
        Assert.That(hit.Applied, Is.Zero);
        Assert.That(_agent.IsVulnerable, Is.True);
        _player.Targeter.ClearFocus();
        TickPlayer();
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(_agent.Id));
        Assert.That(_player.Targeter.IsCurrentBlocked, Is.False);
    }

    [Test]
    public void Open_DoublesDamage_ThenRestores()
    {
        Build();
        Target(_agent);
        Reach(ChaserState.Open);
        Assert.That(Hit(10f).Applied, Is.EqualTo(20f));

        var source = new object();
        _agent.DamageTaken.Add(new Modifier(ModifierKind.PercentMult, 0.5f, source));
        Assert.That(Hit(10f).Applied, Is.EqualTo(30f));
        Reach(ChaserState.Chase);
        Assert.That(Hit(10f).Applied, Is.EqualTo(15f));
        _agent.DamageTaken.RemoveAll(source);
        Assert.That(Hit(10f).Applied, Is.EqualTo(10f));
    }

    [Test]
    public void Cycle_TimesAndEvents()
    {
        Build();
        Target(_agent);
        Reach(ChaserState.Glint);
        RemainsThenTransitions(0.5f, ChaserState.Shine);
        RemainsThenTransitions(2f, ChaserState.Open);
        RemainsThenTransitions(1f, ChaserState.Chase);
        RemainsThenTransitions(4f, ChaserState.Glint);
        var phases = _events.Of<EnemyShineChanged>();
        Assert.That(phases.Count, Is.EqualTo(5));
        AssertPhase(phases[0], ShinePhase.Glint, 0.5f);
        AssertPhase(phases[1], ShinePhase.Shine, 2f);
        AssertPhase(phases[2], ShinePhase.Open, 1f);
        AssertPhase(phases[3], ShinePhase.None, 0f);
        AssertPhase(phases[4], ShinePhase.Glint, 0.5f);
    }

    [Test]
    public void Shine_StunDoesNotEnd()
    {
        Build();
        Target(_agent);
        Reach(ChaserState.Shine);
        Assert.That(_system.Stun(_agent.Id, 5f), Is.True);
        RemainsThenTransitions(2f, ChaserState.Open);
        RemainsThenTransitions(1f, ChaserState.Chase);
        Assert.That(_agent.CrowdControl.IsStunned, Is.True);
    }

    [TestCase(ChaserState.Open)]
    [TestCase(ChaserState.Shine)]
    public void DeathMidShine_Cleans(ChaserState phase)
    {
        Build();
        Target(_agent);
        Reach(phase);
        DamageResult result = phase == ChaserState.Open
            ? Hit(1000f)
            : _system.Execute(_agent.Id, _clock, _player);
        Assert.That(result.Killed, Is.True);
        Assert.That(_agent.Health.IsInvulnerable, Is.False);
        Assert.That(_agent.DamageTaken.Value, Is.EqualTo(1f));
        Assert.That(State, Is.EqualTo(ChaserState.Idle));

        var phases = _events.Of<EnemyShineChanged>();
        Assert.That(phases.Count, Is.EqualTo(phase == ChaserState.Open ? 4 : 3));
        AssertPhase(phases[phases.Count - 1], ShinePhase.None, 0f);
    }

    [TestCase(ChaserState.Shine)]
    [TestCase(ChaserState.Open)]
    public void Reset_MidShine_Cleans(ChaserState phase)
    {
        Build();
        Target(_agent);
        Reach(phase);
        Chaser.Reset();
        Assert.That(State, Is.EqualTo(ChaserState.Idle));
        Assert.That(Hit(10f).Applied, Is.EqualTo(10f));
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Chase));
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Glint));
    }

    [Test]
    public void Tick_Shine_NoAlloc()
    {
        Build(events: new SilentEvents());
        Target(_agent);
        Reach(ChaserState.Glint);
        AllocationAssert.None(Step, iterations: 300);
        Assert.That(State, Is.EqualTo(ChaserState.Shine), "The measurement crossed a full cycle and entered the second shine.");
    }

    private void Build(ContentId id = default, float distance = 10f, IDomainEvents events = null)
    {
        _events = new RecordingEvents();
        _domainEvents = events ?? _events;
        _intents = new RecordingIntents();
        _clock = 0f;
        _snapshot = new WorldSnapshot(Capacity);
        _snapshot.Dt = Frame;
        CharacterSpec character = Oathbound();
        var catalog = new ContentCatalog(new[] { character }, new[]
        {
            Spec(Glassback, Shell()),
            Spec(Scorpion, skitter: new SkitterSpec(4f, 120f, 3f, 1.8f, 0.5f)),
            Spec(Other, behaviour: EnemyBehaviourKind.Static),
        });
        _system = new EnemySystem(catalog, _domainEvents, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(character, _domainEvents, _intents, Capacity);
        _projectiles = new ProjectileSystem(_domainEvents, Capacity);
        _agent = _system.Spawn(id.Value is null ? Glassback : id, new Vector3(0f, 20f, distance));
    }

    private void Target(EnemyAgent agent)
    {
        _player.Targeter.Focus(agent.Id);
        TickPlayer();
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(agent.Id));
    }

    private void TickPlayer() =>
        _player.Tick(0.125f, _clock, _snapshot, _system.Registry.Alive, Vector3.UnitZ);

    private void Step()
    {
        _system.Ingest(_snapshot, now: _clock);
        _intents.Clear();
        _system.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _domainEvents, _projectiles, _system));
        _clock += Frame;
    }

    private void Advance(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Step();
        }
    }

    private void Reach(ChaserState phase)
    {
        for (int i = 0; i < 4 * TicksPerSecond && State != phase; i++)
        {
            Step();
        }

        Assert.That(State, Is.EqualTo(phase));
    }

    private void RemainsThenTransitions(float seconds, ChaserState next)
    {
        ChaserState current = State;
        int ticks = (int)(seconds * TicksPerSecond);
        for (int i = 1; i < ticks; i++)
        {
            Step();
            Assert.That(State, Is.EqualTo(current));
        }

        Step();
        Assert.That(State, Is.EqualTo(next));
    }

    private void AssertPhase(EnemyShineChanged change, ShinePhase phase, float seconds)
    {
        Assert.That(change.Id, Is.EqualTo(_agent.Id));
        Assert.That(change.Phase, Is.EqualTo(phase));
        Assert.That(change.Seconds, Is.EqualTo(seconds));
    }

    private DamageResult Hit(float damage) => _system.ApplyDamage(_agent.Id, damage, _clock, _player);

    private static ShineSpec Shell() => new(0.5f, 2f, 1f, 4f, 12f, 2f, 6f, 14f, 0.8f, 0.3f, 0.4f);

    private static EnemySpec Spec(ContentId id, ShineSpec shine = null, EnemyBehaviourKind behaviour = EnemyBehaviourKind.Chaser,
        PackSpec pack = null, LeapSpec leap = null, BroodSpec brood = null, SkitterSpec skitter = null) =>
        new(id, new LocKey("enemy.glassback.name"), maxHp: 200f, moveSpeed: 2.2f, targetPriority: 1, threatCost: 4,
            xpValue: 18f, isElite: false, contactDamage: 8f, reach: 1.5f, windupTime: 0.6f, recoverTime: 0.8f,
            aggroRange: 30f, behaviour: behaviour, pack: pack, leap: leap, brood: brood, skitter: skitter, shine: shine);

    private static CharacterSpec Oathbound() => new(
        new ContentId("character.oathbound"),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(20f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
