using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class FeralBiteTests
{
    private const int Capacity = 16;
    private static readonly ContentId Frog = new("enemy.frog");
    private static readonly ContentId Bullfrog = new("enemy.bullfrog");

    private EnemySystem _enemies;
    private PetSystem _pets;
    private PlayerCombat _player;
    private RecordingEvents _events;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        var intents = new RecordingIntents();
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f);
        var character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f), pet: hound);
        var catalog = new ContentCatalog(new[] { character }, new[] { Spec(Frog, true), Spec(Bullfrog, false) });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.1f), new DepthScaling(Scalings.Design()), Capacity);
        _pets = new PetSystem(hound, _events, intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _player = new PlayerCombat(character, _events, intents, Capacity);
    }

    [Test]
    public void EliteOrBoss_Light_Refused()
    {
        Assert.That(Assert.Throws<ArgumentException>(() => Spec(Frog, true, isElite: true)).Message,
            Does.Contain(Frog.Value));
        Assert.That(Assert.Throws<ArgumentException>(() => Spec(Frog, true, EnemyBehaviourKind.Boss)).Message,
            Does.Contain(Frog.Value));
    }

    [Test]
    public void EveryThirdBite_StunsLight()
    {
        SetCadence(3f);
        EnemyAgent bullfrog = Spawn(Bullfrog);
        Bite(1);
        Bite(2);
        Assert.That(bullfrog.CrowdControl.IsStunned, Is.False);

        _enemies.Despawn(bullfrog.Id);
        EnemyAgent frog = Spawn(Frog);
        Bite(3);
        Assert.That(frog.CrowdControl.IsStunned, Is.True);

        _enemies.Despawn(frog.Id);
        bullfrog = Spawn(Bullfrog);
        Bite(4);
        Bite(5);
        Bite(6);
        Assert.That(bullfrog.CrowdControl.IsStunned, Is.False);

        _enemies.Despawn(bullfrog.Id);
        frog = Spawn(Frog);
        Bite(7);
        Bite(8);
        Assert.That(frog.CrowdControl.IsStunned, Is.False);
        Bite(9);
        Assert.That(frog.CrowdControl.IsStunned, Is.True);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(9));
    }

    [Test]
    public void StunEvery_ClampsAndFloors()
    {
        Spawn(Frog);
        Bite(1);
        SetCadence(-1f);
        Bite(2);
        Assert.That(_events.Of<EnemyStatusChanged>(), Is.Empty);

        SetCadence(3.9f);
        Bite(3);
        Assert.That(_events.Of<EnemyStatusChanged>(), Has.Count.EqualTo(1));

        SetCadence(11f);
        for (int bite = 4; bite < 10; bite++)
        {
            Bite(bite);
        }

        Assert.That(_events.Of<EnemyStatusChanged>(), Has.Count.EqualTo(1));
        _enemies.Despawn(_pets.Pets[0].QuarryId);
        Spawn(Frog);
        Bite(10);
        Assert.That(_events.Of<EnemyStatusChanged>(), Has.Count.EqualTo(2));
    }

    [Test]
    public void FeralStun_ShowsAsStun()
    {
        SetCadence(1f);
        Stat duration = _pets.LeadStats.Resolve(PlayerStat.PetBiteStunSeconds);
        Assert.That(duration.Value, Is.EqualTo(0.5f));
        duration.Base = 0.75f;
        EnemyAgent frog = Spawn(Frog);
        Bite(1);

        var statuses = _events.Of<EnemyStatusChanged>();
        Assert.That(statuses, Has.Count.EqualTo(1));
        Assert.That(statuses[0].EnemyId, Is.EqualTo(frog.Id));
        Assert.That(statuses[0].Status, Is.EqualTo(_enemies.Statuses.Stunned.Id));
        Assert.That(statuses[0].Seconds, Is.EqualTo(0.75f));
    }

    private void SetCadence(float value) => _pets.LeadStats.Resolve(PlayerStat.PetBiteStunEvery).Base = value;

    private EnemyAgent Spawn(ContentId id) => _enemies.Spawn(id, _pets.Pets[0].Position);

    private void Bite(int number) => _pets.Tick(0f, number, Vector3.Zero, _enemies, _player);

    private static EnemySpec Spec(ContentId id, bool isLight, EnemyBehaviourKind kind = EnemyBehaviourKind.Chaser,
        bool isElite = false) =>
        new EnemySpec(id, new LocKey(id.Value + ".name"), 100f, 2f, 1, 4, 5f, isElite, 8f,
            1.2f, 0.2f, 0.2f, 30f, kind, isLight: isLight);
}
