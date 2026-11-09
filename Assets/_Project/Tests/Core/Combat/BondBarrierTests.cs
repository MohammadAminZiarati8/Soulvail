using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>BT-08: every Wild Swap shields him; Quick Swap's break shoves.</summary>
[TestFixture]
public sealed class BondBarrierTests
{
    private const int Capacity = 8;
    private const float Frame = 0.01f;
    private const float Duration = 0.25f;
    private const float Seconds = 3f;
    private const float Deadline = Frame + Duration + Seconds;
    private const float AfterSwap = 1f;
    private static readonly ContentId Frog = new("enemy.frog");
    private static readonly ContentId Boss = new("boss.test");
    private static readonly Vector3 PlayerAt = new(6f, 0f, 0f);
    private static readonly Vector3 PetAt = new(3f, 0f, 4f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private WorldSnapshot _snapshot;
    private EnemySystem _enemies;
    private PetSystem _pets;
    private PlayerCombat _combat;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _snapshot = new WorldSnapshot(Capacity) { Dt = Frame, PlayerPosition = PlayerAt };
        CharacterSpec character = Character(0.15f, Seconds);
        _enemies = new EnemySystem(Catalog(character), _events, new FixedRandom(5),
            new DepthScaling(Scalings.Design()), Capacity);
        _combat = new PlayerCombat(character, _events, _intents, Capacity);
        _pets = new PetSystem(character.Pet, _events, _intents);
        _pets.SpawnBeside(PlayerAt, 0f);
        _snapshot.PetCount = 0;
        ref EnemySense pet = ref _snapshot.AddPet();
        pet.Id = _pets.Pets[0].Id;
        pet.Position = PetAt;
        _pets.Ingest(_snapshot);
    }

    [Test]
    public void Swap_GrantsFractionOfMaxHp()
    {
        Swap(Frame);

        Assert.That(_combat.BondBarrier, Is.EqualTo(15f).Within(0.0001f));
        Assert.That(_combat.Health.GrantedShield, Is.EqualTo(15f).Within(0.0001f));
        ShieldGranted granted = _events.Single<ShieldGranted>();
        Assert.That(granted.Amount, Is.EqualTo(15f).Within(0.0001f));
        Assert.That(granted.Duration, Is.EqualTo(Duration + Seconds).Within(0.0001f));
        Assert.That(granted.BondBarrier, Is.True);
    }

    [Test]
    public void Barrier_ExpiresAfterLandingPlusSeconds()
    {
        Swap(Frame);

        Step(Deadline - 0.02f);
        Assert.That(_combat.BondBarrier, Is.EqualTo(15f).Within(0.0001f));
        Assert.That(_events.Of<ShieldGrantExpired>(), Is.Empty);

        Step(Deadline + 0.01f);
        Assert.That(_combat.BondBarrier, Is.Zero);
        ShieldGrantExpired expired = _events.Single<ShieldGrantExpired>();
        Assert.That(expired.Removed, Is.EqualTo(15f).Within(0.0001f));
        Assert.That(expired.BondBarrier, Is.True);
    }

    [Test]
    public void Swap_Again_ReplacesNotStacks()
    {
        Swap(Frame);
        Hit(10f, AfterSwap);
        Assert.That(_combat.BondBarrier, Is.EqualTo(5f).Within(0.0001f));

        Swap(AfterSwap + 1.5f);

        Assert.That(_combat.BondBarrier, Is.EqualTo(15f).Within(0.0001f));
        Assert.That(_combat.Health.GrantedShield, Is.EqualTo(15f).Within(0.0001f));
    }

    [Test]
    public void Hit_BiggerThanBarrier_LandsDifference()
    {
        Swap(Frame);

        DamageResult result = Hit(25f, AfterSwap);

        Assert.That(result.ToHp, Is.EqualTo(10f).Within(0.0001f));
        Assert.That(_events.Of<BondBarrierBroke>(), Has.Count.EqualTo(1));
        Assert.That(_combat.Health.Current, Is.EqualTo(90f).Within(0.0001f));
        Assert.That(_combat.BondBarrier, Is.Zero);
    }

    [Test]
    public void Break_WithShove_KnocksNonBossInRange()
    {
        _combat.Charge.BarrierShove.Base = 2f;
        EnemyAgent near = _enemies.Spawn(Frog, PlayerAt + new Vector3(2f, 0f, 0f));
        _enemies.Spawn(Frog, PlayerAt + new Vector3(0f, 0f, 4f));
        _enemies.SpawnBoss(Boss, PlayerAt + new Vector3(-1f, 0f, 0f));
        Swap(Frame);
        _intents.Clear();

        Hit(20f, AfterSwap);
        BondBarrierBroke broke = _events.Single<BondBarrierBroke>();
        Step(AfterSwap + Frame);

        Assert.That(broke.Position, Is.EqualTo(PlayerAt));
        Assert.That(_intents.Knockbacks, Has.Count.EqualTo(1));
        EnemyKnockbackIntent shove = _intents.Knockbacks[0];
        Assert.That(shove.Id, Is.EqualTo(near.Id));
        Assert.That(shove.Distance, Is.EqualTo(2f));
        Assert.That(shove.DirectionXZ, Is.EqualTo(Vector2.UnitX));
    }

    [Test]
    public void Break_WithoutShove_EventOnly()
    {
        _enemies.Spawn(Frog, PlayerAt + new Vector3(1f, 0f, 0f));
        Swap(Frame);
        _intents.Clear();

        Hit(20f, AfterSwap);
        Step(AfterSwap + Frame);

        Assert.That(_events.Of<BondBarrierBroke>(), Has.Count.EqualTo(1));
        Assert.That(_intents.Knockbacks, Is.Empty);
        Assert.That(_events.Single<ShieldGrantExpired>().Total, Is.Zero);
    }

    [Test]
    public void Expiry_IsNotABreak()
    {
        _combat.Charge.BarrierShove.Base = 2f;
        _enemies.Spawn(Frog, PlayerAt + new Vector3(1f, 0f, 0f));
        Swap(Frame);
        _intents.Clear();

        Step(Deadline + 0.01f);
        Step(Deadline + 0.02f);

        Assert.That(_events.Of<BondBarrierBroke>(), Is.Empty);
        Assert.That(_intents.Knockbacks, Is.Empty);
    }

    [Test]
    public void Barrier_Zero_GrantsNothing()
    {
        _combat.Charge.Barrier.Base = 0f;

        Swap(Frame);

        Assert.That(_combat.BondBarrier, Is.Zero);
        Assert.That(_events.Of<ShieldGranted>(), Is.Empty);
    }

    [Test]
    public void Spec_BarrierOnNonSwap_Throws()
    {
        Assert.That(() => Skill(MovementSkillKind.Charge, 0.15f, 0f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Skill(MovementSkillKind.Charge, 0f, 3f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Skill(MovementSkillKind.Swap, float.NaN, 3f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Skill(MovementSkillKind.Swap, 1.5f, 3f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Skill(MovementSkillKind.Swap, 0.15f, float.PositiveInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(Skill(MovementSkillKind.Swap, 0.15f, 3f).BarrierFraction, Is.EqualTo(0.15f));
    }

    private void Swap(float now)
    {
        _intents.Clear();
        _combat.Charge.Request(now);
        Step(now);
        Assert.That(_intents.Charges, Is.Not.Empty, "the swap started");
    }

    // A tick first: the swap's i-frame flag comes down only in a Tick.
    private DamageResult Hit(float amount, float now)
    {
        Step(now);
        return _combat.ApplyDamage(amount, now);
    }

    private void Step(float now) =>
        _combat.Tick(Frame, now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ, pets: _pets);

    private static MovementSkillSpec Skill(MovementSkillKind kind, float fraction, float seconds) =>
        new(kind, 8f, Duration, 1f, 0.15f, 0f, 0f, 0.2f, barrierFraction: fraction, barrierSeconds: seconds);

    private static CharacterSpec Character(float fraction, float seconds) => new(
        new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
        new LocKey("character.beastmaster.description"),
        100f, new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
        Skill(MovementSkillKind.Swap, fraction, seconds),
        pet: new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 1.5f));

    private static ContentCatalog Catalog(CharacterSpec character)
    {
        var frog = new EnemySpec(Frog, new LocKey("enemy.frog.name"), 100f, 2f, 1, 4, 5f, false, 8f, 1.2f,
            0.2f, 0.2f, 30f, EnemyBehaviourKind.Chaser);
        var bodyId = new ContentId("enemy.boss_body");
        var body = new EnemySpec(bodyId, new LocKey("enemy.boss_body.name"), 900f, 1f, 8, 40, 120f,
            false, 20f, 2f, 1f, 1f, 30f, EnemyBehaviourKind.Boss);
        var boss = new BossSpec(Boss, bodyId, new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f);
        return new ContentCatalog(new[] { character }, new[] { frog, body }, bosses: new[] { boss });
    }
}
