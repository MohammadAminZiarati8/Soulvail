using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class SwapRoarTests
{
    private const int Capacity = 8;
    private const float LeapSeconds = 0.25f;
    private static readonly Vector3 Landing = new(6f, 0f, 0f);
    private static readonly ContentId Husk = new("enemy.husk");
    private static readonly ContentId Boss = new("boss.test");

    private RecordingEvents _events;
    private PetSystem _pets;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private WeaponSpec _weapon;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        var intents = new RecordingIntents();
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f);
        var character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0.4f,
                shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Swap, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f), pet: hound);
        var husk = new EnemySpec(Husk, new LocKey("enemy.husk.name"), 100f, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static);
        var bodyId = new ContentId("enemy.boss_body");
        var body = new EnemySpec(bodyId, new LocKey("enemy.boss_body.name"), 900f, 1f, 8, 40, 120f,
            false, 20f, 2f, 1f, 1f, 30f, EnemyBehaviourKind.Boss);
        var bossSpec = new BossSpec(Boss, bodyId, new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f);
        var catalog = new ContentCatalog(new[] { character }, new[] { husk, body }, bosses: new[] { bossSpec });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(character, _events, intents, Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _weapon = character.Weapon;
        _pets = new PetSystem(hound, _events, intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
    }

    [Test]
    public void SwapLanding_RoarSlowsWithinThree()
    {
        int petId = _pets.Pets[0].Id;
        _pets.LeadStats.Resolve(PlayerStat.SwapRoarSlow).Add(new Modifier(ModifierKind.Flat, 0.35f, this));
        EnemyAgent edge = _enemies.Spawn(Husk, Landing + new Vector3(0f, 4f, 3f));
        EnemyAgent inside = _enemies.Spawn(Husk, Landing + Vector3.UnitX);
        EnemyAgent outside = _enemies.Spawn(Husk, Landing + 3.01f * Vector3.UnitX);
        EnemyAgent atTakeoff = _enemies.Spawn(Husk, _pets.Pets[0].Position);
        EnemyAgent dead = _enemies.Spawn(Husk, Landing);
        dead.Health.ApplyDamage(100f, 0f);

        Assert.That(_pets.SwapTo(Landing, 0f, LeapSeconds), Is.True);
        Tick(LeapSeconds / 2f);
        Assert.That(_events.Of<PetRoared>(), Is.Empty);
        Assert.That(_events.Of<EnemyStatusChanged>(), Is.Empty);

        Tick(LeapSeconds);
        Tick(LeapSeconds + 0.01f);

        PetRoared roar = _events.Single<PetRoared>();
        Assert.That(roar.PetId, Is.EqualTo(petId));
        Assert.That(roar.Position, Is.EqualTo(Landing));
        Assert.That(roar.Radius, Is.EqualTo(3f));
        Assert.That(edge.CrowdControl.SlowFraction, Is.EqualTo(0.35f));
        Assert.That(inside.CrowdControl.SlowFraction, Is.EqualTo(0.35f));
        Assert.That(outside.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(atTakeoff.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(dead.CrowdControl.SlowFraction, Is.Zero);
        var statuses = _events.Of<EnemyStatusChanged>();
        Assert.That(statuses, Has.Count.EqualTo(2));
        Assert.That(statuses[0].Seconds, Is.EqualTo(2f));
        Assert.That(statuses[1].Seconds, Is.EqualTo(2f));
    }

    [Test]
    public void SwapRoar_Boss_Immune()
    {
        _pets.LeadStats.Resolve(PlayerStat.SwapRoarSlow).Base = 0.35f;
        EnemyAgent boss = _enemies.SpawnBoss(Boss, Landing);

        Assert.That(_pets.SwapTo(Landing, 0f, LeapSeconds), Is.True);
        Tick(LeapSeconds);

        Assert.That(_events.Of<PetRoared>(), Has.Count.EqualTo(1));
        Assert.That(boss.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(_events.Of<EnemyStatusChanged>(), Is.Empty);
    }

    [Test]
    public void FallenSwap_NoRoar()
    {
        int petId = _pets.Pets[0].Id;
        _pets.LeadStats.Resolve(PlayerStat.SwapRoarSlow).Base = 0.35f;
        EnemyAgent enemy = _enemies.Spawn(Husk, Landing);
        _pets.ApplyDamage(petId, 100f, 1f, enemy.Id);
        Assert.That(_pets.Pets[0].State, Is.EqualTo(PetState.Fallen));
        _events.Clear();
        Assert.That(_pets.SwapTo(Landing, 1f, LeapSeconds), Is.True);
        Tick(1f + LeapSeconds);
        Assert.That(_events.Of<PetRoared>(), Is.Empty);
        Assert.That(_events.Of<EnemyStatusChanged>(), Is.Empty);
    }

    [Test]
    public void ZeroStats_Silent()
    {
        Assert.That(_pets.LeadStats.Resolve(PlayerStat.SwapRoarSlow).Base, Is.Zero);
        var axes = new AxeSystem(_events, _stats, _weapon);
        EnemyAgent enemy = _enemies.Spawn(Husk, Landing);
        Assert.That(axes.TryThrow(Vector3.Zero, Landing, 10f, 0f), Is.True);
        axes.Tick(1f, 1f, Vector3.Zero, _player, _enemies);
        axes.Tick(1f, 2f, Vector3.Zero, _player, _enemies);
        Assert.That(_events.Of<AxeHit>(), Has.Count.EqualTo(2));

        Assert.That(_pets.SwapTo(Landing, 2f, LeapSeconds), Is.True);
        Tick(2f + LeapSeconds);

        Assert.That(enemy.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(_events.Of<EnemyStatusChanged>(), Is.Empty);
        Assert.That(_events.Of<PetRoared>(), Is.Empty);
    }

    private void Tick(float now) => _pets.Tick(0f, now, Vector3.Zero, _enemies, _player);
}
