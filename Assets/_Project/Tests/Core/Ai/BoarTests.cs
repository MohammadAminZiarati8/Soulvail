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

namespace Soulvail.Tests.Core.Ai;

/// <summary>BT-07a rules 1, 2, 5, 6 and 7: the boar joins, holds off, is hunted, falls, and Pack Tactics is gone.</summary>
[TestFixture]
public sealed class BoarTests
{
    private const int Capacity = 8;
    private const float Dt = 0.1f;
    private static readonly ContentId EnemyId = new("enemy.boar_target");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 8f, 0.5f, 1.4f, 1.5f);
    private static readonly PetSpec Boar = new(new ContentId("pet.boar"), 60f, 3.6f, 6f, 1.5f, 1.4f, 1.5f,
        isRanged: true);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;

    private PetAgent Lead => _pets.Pets[0];

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        CharacterSpec character = Character();
        _combat = new PlayerCombat(character, _events, _intents, Capacity);
        _enemies = new EnemySystem(Catalog(character), _events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        _pets = new PetSystem(Hound, Boar, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
    }

    [Test]
    public void BoarCount_One_SpawnsBoarInSlotThree()
    {
        Set(PlayerStat.BoarCount, 1f);

        Assert.That(_pets.Pets.Length, Is.EqualTo(2));
        PetAgent boar = _pets.Pets[1];
        Assert.That(boar.Kind, Is.EqualTo(PetKind.Boar));
        Assert.That(boar.IsLead, Is.False);
        Assert.That(boar.Position.X, Is.EqualTo(-1.2f).Within(0.001f), "Slot 3's offset, facing forward.");
        Assert.That(boar.Position.Z, Is.EqualTo(-1.6f).Within(0.001f));
        PetSpawned spawned = _events.Of<PetSpawned>()[1];
        Assert.That(spawned.Id, Is.EqualTo(boar.Id));
        Assert.That(spawned.Kind, Is.EqualTo(PetKind.Boar));
        Assert.That(_events.Of<PetSpawned>()[0].Kind, Is.EqualTo(PetKind.Hound));

        Set(PlayerStat.PackHounds, 2f);
        Assert.That(_pets.Pets.Length, Is.EqualTo(4));
        Assert.That(_pets.Pets[3], Is.SameAs(boar), "Hounds fill slots 0-2; the boar keeps slot 3.");
        Assert.That(_pets.Pets[1].Kind, Is.EqualTo(PetKind.PackHound));

        // Level growth and Thick Hide land on the lead; the boar grows by the same ratio and heals the gain.
        Set(PlayerStat.MaxHp, 77f);
        Set(PlayerStat.ContactDamage, 8.8f);
        Assert.That(boar.MaxHp.Value, Is.EqualTo(66f).Within(0.001f));
        Assert.That(boar.Health.Current, Is.EqualTo(66f).Within(0.001f));
        Assert.That(boar.ContactDamage.Value, Is.EqualTo(6.6f).Within(0.001f));

        Assert.That(_pets.Frenzy(0.3f, 8f, 0f), Is.EqualTo(4));
        Assert.That(_pets.IsFrenzied(boar.Id, 1f), Is.True);
    }

    [Test]
    public void BoarCount_Zero_Removes()
    {
        Set(PlayerStat.BoarCount, 1f);
        int boarId = _pets.Pets[1].Id;

        Set(PlayerStat.BoarCount, 0f);

        Assert.That(_pets.Pets.Length, Is.EqualTo(1));
        Assert.That(_pets.Pets[0].Kind, Is.EqualTo(PetKind.Hound));
        Assert.That(_events.Single<PetDespawned>().Id, Is.EqualTo(boarId));
    }

    [TestCase(10f, 1f)]
    [TestCase(4f, 0f)]
    [TestCase(2f, -1f)]
    public void Boar_HoldsAtFive_BacksOffInsideThree(float enemyDistance, float direction)
    {
        PetAgent boar = JoinBoarAlone();
        _enemies.Spawn(EnemyId, Vector3.UnitX * enemyDistance);

        Tick(0f);

        EnemyMoveIntent move = MoveOf(boar.Id);
        Assert.That(move.Velocity.X, Is.EqualTo(3.6f * direction).Within(0.001f));
        Assert.That(move.Velocity.Z, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void Boar_BlockedLine_MovesNotSpits()
    {
        PetAgent boar = JoinBoarAlone();
        _enemies.Spawn(EnemyId, Vector3.UnitX * 4f);
        Func<Vector3, Vector3, Vector3?> wall = (from, to) => (from + to) * 0.5f;

        for (int i = 0; i <= 20; i++)
        {
            Tick(i * Dt, wall);
            Assert.That(MoveOf(boar.Id).Velocity.X, Is.EqualTo(3.6f).Within(0.001f), "It walks to find a line.");
        }

        Assert.That(_events.Count<PetSpat>(), Is.Zero);
    }

    [Test]
    public void SpitHit_MeleeEnemy_MayHuntBoar()
    {
        Vector3 player = Vector3.UnitX * -6f;
        PetAgent boar = JoinBoarAlone();
        _enemies.Spawn(EnemyId, Vector3.UnitX * 4f);

        for (int i = 0; i <= 3; i++)
        {
            Tick(i * Dt, player: player);
        }

        Assert.That(_events.Count<GlobLanded>(), Is.EqualTo(1));
        // The bite mark is internal to core; the hunt below is its public consequence.
        Assert.That(_events.Of<GlobLanded>()[0].Hit, Is.True);

        _enemies.Ingest(new WorldSnapshot(Capacity) { PlayerPosition = player }, pets: _pets, now: 3 * Dt);
        Assert.That(_pets.HuntersOf(boar.Id), Is.EqualTo(1));
    }

    [Test]
    public void Boar_Falls_RescuedLikeHound()
    {
        PetAgent boar = JoinBoarAlone();
        _pets.ApplyDamage(boar.Id, 1000f, 0f, 1);
        Assert.That(boar.State, Is.EqualTo(PetState.Fallen));
        Assert.That(_events.Of<PetFell>()[1].PetId, Is.EqualTo(boar.Id));

        for (int i = 1; i <= 30 && _events.Count<PetRose>() == 0; i++)
        {
            Tick(i * Dt, player: boar.Position);
        }

        Assert.That(_events.Single<PetRose>().PetId, Is.EqualTo(boar.Id));
        Assert.That(boar.State, Is.EqualTo(PetState.Heel));
        Assert.That(boar.Health.Current, Is.EqualTo(30f).Within(0.001f), "Half of its own maximum.");
    }

    [Test]
    public void Swap_TradesWithHoundNotBoar()
    {
        Set(PlayerStat.BoarCount, 1f);
        PetAgent boar = _pets.Pets[1];
        Vector3 boarAt = boar.Position;

        Assert.That(_pets.SwapTo(Vector3.UnitX * 3f, 0f, 0.3f), Is.True);

        Assert.That(_intents.PetLeaps.Count, Is.EqualTo(1));
        Assert.That(_intents.PetLeaps[0].PetId, Is.EqualTo(Lead.Id));
        Assert.That(_events.Single<Swapped>().PetId, Is.EqualTo(Lead.Id));
        Assert.That(boar.Position, Is.EqualTo(boarAt));
    }

    [Test]
    public void SharedQuarry_NoBonus()
    {
        Set(PlayerStat.PackHounds, 1f);
        Set(PlayerStat.PackTactics, 0.2f);
        Set(PlayerStat.PackTacticsSlow, 0.3f);
        foreach (PetAgent pet in _pets.Pets)
        {
            ReportPosition(pet, Vector3.Zero);
        }

        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.Zero);
        Tick(0f);

        Assert.That(target.Health.MaxHp.Value - target.Health.Current, Is.EqualTo(12f).Within(0.001f),
            "8 from the lead, 4 from the half-share hound, nothing more.");
        Assert.That(target.CrowdControl.SlowFraction, Is.Zero);
    }

    // The boar at the origin with the lead felled far away, so only the boar hunts.
    private PetAgent JoinBoarAlone()
    {
        Set(PlayerStat.BoarCount, 1f);
        PetAgent boar = _pets.Pets[1];
        ReportPosition(Lead, new Vector3(30f, 0f, 30f));
        ReportPosition(boar, Vector3.Zero);
        _pets.ApplyDamage(Lead.Id, 1000f, 0f, 1);
        return boar;
    }

    private void Tick(float now, Func<Vector3, Vector3, Vector3?> traceCover = null, Vector3 player = default)
    {
        _intents.Clear();
        _pets.Tick(Dt, now, player, _enemies, _combat, traceCover: traceCover);
    }

    private EnemyMoveIntent MoveOf(int petId)
    {
        foreach (EnemyMoveIntent move in _intents.PetMoves)
        {
            if (move.Id == petId)
            {
                return move;
            }
        }

        throw new AssertionException($"No move for pet {petId}.");
    }

    private void ReportPosition(PetAgent pet, Vector3 position)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddPet();
        sense.Id = pet.Id;
        sense.Position = position;
        _pets.Ingest(snapshot);
    }

    private void Set(PlayerStat stat, float value) => _pets.LeadStats.Resolve(stat).Base = value;

    private static CharacterSpec Character() => new(
        new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
        new LocKey("character.beastmaster.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f), pet: Hound, boar: Boar);

    private static ContentCatalog Catalog(CharacterSpec character)
    {
        var mode = new ModeSpec(new ContentId("mode.boar_test"), new LocKey("mode.boar_test.name"), 1, true, 0,
            new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                new ConcurrencyCurve(1, 1000), new StatCurve(0.06f, 4f, 1, 1),
                new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
            Scalings.Xp(), new[] { new RosterEntry(EnemyId, 1) }, hasSanctum: false);
        return new ContentCatalog(new[] { character },
            new[]
            {
                new EnemySpec(EnemyId, new LocKey("enemy.boar_target.name"), 100f, 2f, 1, 4, 5f, false, 8f, 1.2f,
                    0.4f, 0.6f, 30f, EnemyBehaviourKind.Chaser),
            },
            new[] { mode });
    }
}
