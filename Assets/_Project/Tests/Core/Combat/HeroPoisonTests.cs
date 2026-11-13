using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;

namespace Soulvail.Tests.Core.Combat;

/// <summary>SS-12a: the hero's Poison stacks, pulses, status and clears.</summary>
[TestFixture]
public sealed class HeroPoisonTests
{
    private const int Capacity = 8;
    private const float Fraction = 0.02f;
    private static readonly ContentId PoisonedId = new("status.poisoned");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private WorldSnapshot _snapshot;
    private PlayerCombat _player;
    private StatusBoard _status;
    private PetSystem _pets;
    private float _now;

    [SetUp]
    public void SetUp() => Make(MovementSkillKind.Charge, 100f);

    [Test]
    public void Poison_StacksToThree()
    {
        for (int i = 0; i < 4; i++)
        {
            Assert.That(_player.ApplyPoison(Fraction, 4f, 3), Is.True);
            Step(1f);
        }

        Assert.That(_player.PoisonStacks, Is.EqualTo(3));
    }

    [Test]
    public void Poison_PulsesNoIFrames()
    {
        Make(MovementSkillKind.Charge, 90f);
        _player.ApplyDamage(8f, _now);
        _player.ApplyPoison(Fraction, 4f, 3);
        Assert.That(_player.Health.Current, Is.EqualTo(82f).Within(0.0001f));

        Step(1f);

        Assert.That(_player.Health.Current, Is.EqualTo(80.2f).Within(0.0001f));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(3));

        Make(MovementSkillKind.Charge, 100f);
        Assert.That(_player.ApplyPulse(1f, _now).Applied, Is.EqualTo(1f));
        Assert.That(_player.Health.IsInvulnerable, Is.False);
        Assert.That(_player.ApplyDamage(5f, _now).Applied, Is.EqualTo(5f));
    }

    [Test]
    public void Poison_RefusedWhileImmune()
    {
        _player.Charge.Request(_now);
        Step(0.01f);
        Assert.That(_player.Health.IsInvulnerable, Is.True);

        DamageResult pulse = _player.ApplyPulse(5f, _now);

        Assert.That(pulse.Blocked, Is.True);
        Assert.That(_player.ApplyPoison(Fraction, 4f, 3), Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_player.PoisonStacks, Is.Zero);
        Assert.That(_status.Count, Is.Zero);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Poison_RefreshThenEnd()
    {
        _player.ApplyPoison(Fraction, 4f, 3);
        Run(3f);
        _player.ApplyPoison(Fraction, 4f, 3);
        Run(3.5f);

        Assert.That(_player.PoisonStacks, Is.EqualTo(2));
        Assert.That(_status.Count, Is.EqualTo(1));

        Step(0.6f);

        Assert.That(_player.PoisonStacks, Is.Zero);
        Assert.That(_status.Count, Is.Zero);
        float hp = _player.Health.Current;
        Step(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
    }

    [Test]
    public void Poison_StatusShowsStacks()
    {
        _player.ApplyPoison(Fraction, 4f, 3);
        Step(1f);
        _player.ApplyPoison(Fraction, 4f, 3);

        Assert.That(_status.Count, Is.EqualTo(1));
        StatusEntry entry = _status[0];
        Assert.That(entry.Id, Is.EqualTo(PoisonedId));
        Assert.That(entry.IsBuff, Is.False);
        Assert.That(entry.Stacks, Is.EqualTo(2));
        Assert.That(entry.Remaining, Is.EqualTo(4f).Within(0.0001f));
    }

    [TestCase(MovementSkillKind.Charge)]
    [TestCase(MovementSkillKind.Shroudstep)]
    [TestCase(MovementSkillKind.Blink)]
    [TestCase(MovementSkillKind.Swap)]
    public void Poison_SpaceMoveClears(MovementSkillKind kind)
    {
        Make(kind, 100f);
        for (int i = 0; i < 3; i++)
        {
            _player.ApplyPoison(Fraction, 4f, 3);
        }

        Assert.That(_player.PoisonStacks, Is.EqualTo(3));
        Assert.That(_status.Count, Is.EqualTo(1));

        _intents.Clear();
        _player.Charge.Request(_now);
        Step(0.01f);

        Assert.That(_intents.Charges, Is.Not.Empty, "the move started");
        Assert.That(_player.PoisonStacks, Is.Zero);
        Assert.That(_status.Count, Is.Zero);
    }

    [Test]
    public void Poison_PulseFlagAndKill()
    {
        _player.ApplyDamage(99f, _now);
        _player.ApplyPulse(5f, _now);

        var hits = _events.Of<PlayerDamaged>();
        Assert.That(hits.Count, Is.EqualTo(2));
        Assert.That(hits[0].IsPulse, Is.False);
        Assert.That(hits[1].IsPulse, Is.True);
        Assert.That(_player.IsDead, Is.True);
        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1));

        _player.ApplyPulse(5f, _now);
        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(2));
    }

    [Test]
    public void Poison_DeathAndResetClear()
    {
        _player.ApplyPoison(Fraction, 4f, 3);
        _player.ApplyDamage(500f, _now);
        Assert.That(_player.PoisonStacks, Is.Zero);
        Assert.That(_status.Count, Is.Zero);

        _player.Reset();
        _player.ApplyPoison(Fraction, 4f, 3);
        _player.Reset();
        Assert.That(_player.PoisonStacks, Is.Zero);
        Assert.That(_status.Count, Is.Zero);
    }

    private void Run(float seconds)
    {
        for (float t = 0f; t < seconds - 0.0001f; t += 0.5f)
        {
            Step(0.5f);
        }
    }

    private void Step(float dt)
    {
        _now += dt;
        _snapshot.Dt = dt;
        _player.Tick(dt, _now, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ, pets: _pets);
    }

    private void Make(MovementSkillKind kind, float hp)
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _snapshot = new WorldSnapshot(Capacity) { Dt = 0.01f, PlayerPosition = new Vector3(6f, 0f, 0f) };
        CharacterSpec hero = Hero(kind, hp);
        _player = new PlayerCombat(hero, _events, _intents, Capacity);
        _status = new StatusBoard();
        _player.AttachStatus(_status);
        _pets = null;
        _now = 0f;
        if (kind == MovementSkillKind.Swap)
        {
            _pets = new PetSystem(hero.Pet, _events, _intents);
            _pets.SpawnBeside(_snapshot.PlayerPosition, 0f);
            _snapshot.PetCount = 0;
            ref EnemySense pet = ref _snapshot.AddPet();
            pet.Id = _pets.Pets[0].Id;
            pet.Position = new Vector3(3f, 0f, 4f);
            _pets.Ingest(_snapshot);
        }
    }

    private static MovementSkillSpec Skill(MovementSkillKind kind) => kind switch
    {
        MovementSkillKind.Shroudstep => new MovementSkillSpec(kind, 6f, 0.1f, 1f, 0.15f, 0f, 0f, 0.05f,
            decoyDuration: 3f),
        MovementSkillKind.Blink => new MovementSkillSpec(kind, 10f, 0.1f, 1f, 0.15f, 0f, 0f, 0.05f,
            poolRadius: 2f, poolDuration: 3f, poolDamagePerPulse: 2f),
        MovementSkillKind.Swap => new MovementSkillSpec(kind, 8f, 0.25f, 1f, 0.15f, 0f, 0f, 0.2f,
            barrierFraction: 0.15f, barrierSeconds: 3f),
        _ => new MovementSkillSpec(kind, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f),
    };

    private static CharacterSpec Hero(MovementSkillKind kind, float hp) => new(
        new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), hp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
        Skill(kind),
        pet: kind == MovementSkillKind.Swap
            ? new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 1.5f)
            : null);
}
