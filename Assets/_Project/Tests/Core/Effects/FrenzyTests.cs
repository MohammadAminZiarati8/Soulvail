using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class FrenzyTests
{
    private const int Capacity = 8;
    private static readonly ContentId EnemyId = new("enemy.frenzy_target");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 1.5f);
    private readonly Frenzy _frenzy = new();
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PetSystem _pets;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private CharacterSpec _character;
    private EnemySystem _enemies;
    private SimulatedClock _clock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0.4f,
                shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Swap, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f), pet: Hound);
        _combat = new PlayerCombat(_character, _events, _intents, Capacity);
        _stats = new PlayerStats(_combat, new PlayerMotor(_character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _pets = new PetSystem(Hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _clock = new SimulatedClock();
    }

    // ---- Rule 1: +30% bite rate, move speed and bite damage for FrenzySeconds ---------------------

    [Test]
    public void Frenzy_BitesThirtyPercentFaster()
    {
        _enemies.Spawn(EnemyId, _pets.Pets[0].Position + 0.1f * Vector3.UnitX);
        Assert.That(Runner().Cast(0, 0f, auto: false), Is.True);

        Tick(0f);
        Tick(0.19f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(1));
        Tick(0.2f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(2), "0.25 s / 1.3 ≈ 0.192 s.");
    }

    [Test]
    public void Frenzy_MovesThirtyPercentFaster()
    {
        _enemies.Spawn(EnemyId, _pets.Pets[0].Position + 5f * Vector3.UnitX);
        Tick(0.1f, 0f);
        Assert.That(LastPetSpeed(), Is.EqualTo(4f).Within(0.0001f));

        Assert.That(Runner().Cast(0, 0.1f, auto: false), Is.True);
        _intents.Clear();
        Tick(0.1f, 0.1f);
        Assert.That(LastPetSpeed(), Is.EqualTo(5.2f).Within(0.0001f));
    }

    [Test]
    public void Frenzy_BitesThirtyPercentHarder()
    {
        _enemies.Spawn(EnemyId, _pets.Pets[0].Position + 0.1f * Vector3.UnitX);
        Assert.That(_stats.FrenzyBonus.Value, Is.EqualTo(0.3f));
        Assert.That(Runner().Cast(0, 0f, auto: false), Is.True);

        Tick(0f);
        Assert.That(_events.Single<PetBit>().Amount, Is.EqualTo(9.1f).Within(0.0001f));
        PetsFrenzied frenzied = _events.Single<PetsFrenzied>();
        Assert.That((frenzied.Seconds, frenzied.Bonus), Is.EqualTo((8f, 0.3f)));
    }

    [Test]
    public void Frenzy_Expires_AfterSeconds()
    {
        _enemies.Spawn(EnemyId, _pets.Pets[0].Position + 0.1f * Vector3.UnitX);
        Assert.That(_stats.FrenzySeconds.Value, Is.EqualTo(8f));
        _stats.Resolve(PlayerStat.FrenzySeconds).Base = 2f;
        Assert.That(Runner().Cast(0, 0f, auto: false), Is.True);
        int id = _pets.Pets[0].Id;

        Assert.That(_pets.IsFrenzied(id, 1.99f), Is.True);
        Assert.That(_pets.IsFrenzied(id, 2f), Is.False);
        Tick(2f);
        Assert.That(_events.Single<PetBit>().Amount, Is.EqualTo(7f));
    }

    [Test]
    public void Frenzy_Recast_RefreshesAndKeepsLarger()
    {
        _enemies.Spawn(EnemyId, _pets.Pets[0].Position + 0.1f * Vector3.UnitX);
        int id = _pets.Pets[0].Id;

        Assert.That(_pets.Frenzy(0.5f, 2f, 0f), Is.EqualTo(1));
        Assert.That(_pets.Frenzy(0.3f, 2f, 1.5f), Is.EqualTo(1));
        Assert.That(_pets.IsFrenzied(id, 3.49f), Is.True, "Refreshed to 1.5 + 2.");
        Assert.That(_pets.IsFrenzied(id, 3.5f), Is.False);
        Tick(2.5f);
        Assert.That(_events.Of<PetBit>()[0].Amount, Is.EqualTo(10.5f).Within(0.0001f), "Kept 0.5.");

        _pets.Frenzy(0.3f, 2f, 4f);
        Tick(4.5f);
        Assert.That(_events.Of<PetBit>()[1].Amount, Is.EqualTo(9.1f).Within(0.0001f), "Expired, so the new bonus.");
    }

    // ---- Rules 1 and 2: who holds the buff ---------------------------------------------------------

    [Test]
    public void Frenzy_FallenSkipped_FallEndsBuff()
    {
        _pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 2f;
        int lead = _pets.Pets[0].Id;
        int second = _pets.Pets[1].Id;
        int fallen = _pets.Pets[2].Id;
        _pets.ApplyDamage(fallen, 100f, 0f, 0);

        Assert.That(_pets.Frenzy(0.3f, 8f, 0f), Is.EqualTo(2));
        Assert.That(_pets.IsFrenzied(lead, 1f), Is.True);
        Assert.That(_pets.IsFrenzied(second, 1f), Is.True);
        Assert.That(_pets.IsFrenzied(fallen, 1f), Is.False);

        _pets.ApplyDamage(second, 100f, 1f, 0);
        Assert.That(_pets.IsFrenzied(second, 1f), Is.False);
        _pets.AddRescueHeadStart(second, 1f);
        Assert.That(_pets.Pets[1].State, Is.Not.EqualTo(PetState.Fallen));
        Assert.That(_pets.IsFrenzied(second, 2f), Is.False, "Risen mid-Frenzy is not buffed.");
    }

    [Test]
    public void Frenzy_SpawnedMidway_NotBuffed()
    {
        Assert.That(_pets.Frenzy(0.3f, 8f, 0f), Is.EqualTo(1));
        _pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 2f;

        Assert.That(_pets.Pets.Length, Is.EqualTo(3));
        Assert.That(_pets.IsFrenzied(_pets.Pets[0].Id, 1f), Is.True);
        Assert.That(_pets.IsFrenzied(_pets.Pets[1].Id, 1f), Is.False);
        Assert.That(_pets.IsFrenzied(_pets.Pets[2].Id, 1f), Is.False);
    }

    // ---- Rule 4: the cast guard ---------------------------------------------------------------------

    [Test]
    public void Frenzy_NoStandingPet_CannotCast()
    {
        SkillRunner runner = Runner();
        _pets.ApplyDamage(_pets.Pets[0].Id, 100f, 0f, 0);

        Assert.That(runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(runner.CooldownFraction(0), Is.Zero);
        Assert.That(_events.Of<PetsFrenzied>(), Is.Empty);
    }

    // ---- Rule 5: append-only --------------------------------------------------------------------------

    [Test]
    public void PlayerStat_Ordinals_Unchanged()
    {
        Assert.That(Enum.GetValues(typeof(PlayerStat)).Length, Is.EqualTo(85), "BT-07a appended three, BT-08 two.");
        Assert.That((int)PlayerStat.HowlHeal, Is.EqualTo(68));
        Assert.That((int)PlayerStat.HowlSeconds, Is.EqualTo(69), "Retired, kept.");
        Assert.That((int)PlayerStat.StampedeHalfWidth, Is.EqualTo(77));
        Assert.That((int)PlayerStat.FrenzySeconds, Is.EqualTo(78));
        Assert.That((int)PlayerStat.FrenzyBonus, Is.EqualTo(79));
        Assert.That((int)PlayerStat.BoarCount, Is.EqualTo(80));
        Assert.That((int)PlayerStat.SpitSlow, Is.EqualTo(82));
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    private void Tick(float now) => Tick(0f, now);

    private void Tick(float dt, float now) => _pets.Tick(dt, now, Vector3.Zero, _enemies, _combat);

    private float LastPetSpeed()
    {
        EnemyMoveIntent move = _intents.PetMoves[_intents.PetMoves.Count - 1];
        return move.Velocity.Length();
    }

    private SkillRunner Runner()
    {
        var registry = new EffectRegistry();
        registry.Register<Frenzy>(new FrenzyHandler(_pets, _stats, _clock));
        var runner = new SkillRunner(registry, _combat.Blackboard, _events);
        runner.Add(new SkillSpec(new ContentId("skill.frenzy"), new LocKey("skill.frenzy.name"),
            new LocKey("skill.frenzy.desc"), SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(30f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.LowestPetHpFraction, TriggerComparison.Below, 0.5f) }),
                new IEffect[] { _frenzy })));
        return runner;
    }

    private ContentCatalog Catalog() =>
        new(new[] { _character }, new[] { new EnemySpec(EnemyId, new LocKey("enemy.test.name"), 1000f, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static) });
}
