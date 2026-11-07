using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class WildSwapTests
{
    private const int Capacity = 8;
    private const float Frame = 0.01f;
    private const float Duration = 0.25f;
    private const float Trail = 0.2f;
    private static readonly ContentId Beastmaster = new("character.beastmaster");
    private static readonly ContentId Frog = new("enemy.frog");
    private static readonly ContentId Mode = new("mode.swap_test");
    private static readonly Vector3 PlayerFrom = new(6f, 0f, 0f);
    private static readonly Vector3 PetFrom = new(3f, 0f, 4f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private WorldSnapshot _snapshot;
    private CharacterSpec _character;
    private EnemySystem _enemies;
    private PetSystem _pets;
    private PlayerCombat _combat;

    private int PetId => _pets.Pets[0].Id;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _snapshot = new WorldSnapshot(Capacity)
        {
            Dt = Frame,
            PlayerPosition = PlayerFrom,
            MoveInput = Vector2.UnitX,
        };
        _character = Character(new PetSpec(new ContentId("pet.hound"),
            70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f));
        _enemies = new EnemySystem(Catalog(_character), _events, new FixedRandom(5),
            new DepthScaling(Scalings.Design()), Capacity);
        _combat = new PlayerCombat(_character, _events, _intents, Capacity);
        _pets = new PetSystem(_character.Pet, _events, _intents);
        _pets.SpawnBeside(PlayerFrom, 0f);
        ReportPet(PetId, PetFrom);
        _pets.Ingest(_snapshot);
    }

    [Test]
    public void NoPet_PressIgnored_NoCooldown()
    {
        RunSession session = StartRun(Character(null));

        session.MovementSkill();
        session.Tick(_snapshot);

        Assert.That(_intents.Charges, Is.Empty);
        Assert.That(_intents.PetLeaps, Is.Empty);
        Assert.That(session.State.MovementSkillCooldownFraction, Is.Zero);
    }

    [Test]
    public void Swap_PlayerEndsAtPetsPosition()
    {
        RunSession session = StartRun(_character);
        ReportPet(_events.Single<PetSpawned>().Id, PetFrom);

        session.MovementSkill();
        session.Tick(_snapshot);
        ChargeIntent dash = _intents.Charges[0];

        ReportPet(_events.Single<PetSpawned>().Id, PlayerFrom);
        session.Tick(_snapshot);

        Assert.That(_intents.Charges.Count, Is.EqualTo(1));
        Assert.That(dash.Duration, Is.EqualTo(Duration));
        Assert.That(dash.Distance, Is.EqualTo(5f));
        Assert.That(Destination(dash), Is.EqualTo(PetFrom));
    }

    [Test]
    public void Swap_PetLeapsToPlayersStart_Immune_HuntersDropped()
    {
        EnemyAgent frog = _enemies.Spawn(Frog, PetFrom);
        _enemies.Mark(frog.Id, 5f);
        _pets.Tick(Frame, 0f, PetFrom, _enemies, _combat);
        ref EnemySense sense = ref _snapshot.AddEnemy();
        sense.Id = frog.Id;
        sense.Position = PetFrom;
        _enemies.Ingest(_snapshot, pets: _pets);
        Assert.That(_pets.HuntersOf(PetId), Is.EqualTo(1));

        Press();
        _pets.Tick(Frame, Frame, PlayerFrom, _enemies, _combat);

        Assert.That(_intents.PetLeaps.Count, Is.EqualTo(1));
        PetLeapIntent leap = _intents.PetLeaps[0];
        Assert.That(leap.PetId, Is.EqualTo(PetId));
        Assert.That(leap.To, Is.EqualTo(PlayerFrom));
        Assert.That(leap.Seconds, Is.EqualTo(Duration));
        Assert.That(_intents.PetMoves, Is.Empty);
        Assert.That(_pets.HuntersOf(PetId), Is.Zero);
        Swapped swapped = _events.Single<Swapped>();
        Assert.That(swapped.PetId, Is.EqualTo(PetId));
        Assert.That(swapped.PlayerFrom, Is.EqualTo(PlayerFrom));
        Assert.That(swapped.PlayerTo, Is.EqualTo(PetFrom));
        Assert.That(swapped.Seconds, Is.EqualTo(Duration));
        Assert.That(_pets.ApplyDamage(PetId, 1f, Frame + 0.1f, frog.Id).Blocked, Is.True);
        Assert.That(_pets.ApplyDamage(PetId, 1f, Frame + Duration + Trail - Frame, frog.Id).Blocked, Is.True);
        Assert.That(_pets.ApplyDamage(PetId, 1f, Frame + Duration + Trail, frog.Id).Applied, Is.EqualTo(1f));
    }

    [Test]
    public void FallenPet_PlayerOnBody_RescueHalfDone()
    {
        _pets.ApplyDamage(PetId, 100f, 0f, 1);

        Press();

        Assert.That(Destination(_intents.Charges[0]), Is.EqualTo(PetFrom));
        Assert.That(_pets.Pets[0].Position, Is.EqualTo(PetFrom));
        Assert.That(_intents.PetLeaps, Is.Empty);
        Assert.That(_events.Single<RescueProgressed>().Fraction, Is.EqualTo(0.5f));
    }

    [Test]
    public void Swap_PlayerInvulnerable_DashPlusWindow()
    {
        Press();
        Assert.That(_combat.ApplyDamage(1f, Frame).Blocked, Is.True);

        float lastProtected = Frame + Duration + Trail - Frame;
        Step(lastProtected);
        Assert.That(_combat.Charge.IsActive, Is.False);
        Assert.That(_combat.ApplyDamage(1f, lastProtected).Blocked, Is.True);

        float end = Frame + Duration + Trail;
        Step(end);
        Assert.That(_combat.ApplyDamage(1f, end).Applied, Is.EqualTo(1f));
    }

    [Test]
    public void Swap_Cooldown_ModifiedByStat()
    {
        var stats = new PlayerStats(_combat, new PlayerMotor(_character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        Stat cooldown = stats.Resolve(PlayerStat.MovementSkillCooldown);
        Assert.That(cooldown.Value, Is.EqualTo(6f));
        cooldown.Add(new Modifier(ModifierKind.Flat, -2f, this));

        Press();
        Step(Frame + 4f - Frame);
        Assert.That(_combat.Charge.IsReady, Is.False);
        Step(Frame + 4f);
        Assert.That(_combat.Charge.IsReady, Is.True);
    }

    [Test]
    public void PetUnderfoot_StillSwaps()
    {
        ReportPet(PetId, PlayerFrom);
        _pets.Ingest(_snapshot);

        Press();

        Assert.That(_intents.Charges[0].Distance, Is.Zero);
        Assert.That(_intents.PetLeaps[0].To, Is.EqualTo(PlayerFrom));
        Assert.That(_combat.Charge.CooldownFraction, Is.Positive);
    }

    [Test]
    public void Swap_ThroughCover_NotStopped()
    {
        _snapshot.Pets[0].HasLineOfSight = false;

        Press();

        ChargeIntent dash = _intents.Charges[0];
        Assert.That(dash.PassesCover, Is.True);
        Assert.That(Destination(dash), Is.EqualTo(PetFrom));
    }

    [Test]
    public void Blink_StillDashesAlongStick()
    {
        var blink = new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.1f, 6f, 0.15f,
            0f, 0f, Trail, poolRadius: 3f, poolDuration: 2f, poolDamagePerPulse: 1f);
        var skill = new ChargeSkill(blink);

        skill.Request(0f);

        Assert.That(skill.Tick(Frame, Frame, new Vector2(-0.5f, 0f), Vector2.UnitY), Is.True);
        Assert.That(skill.Direction, Is.EqualTo(-Vector2.UnitX));
    }

    [Test]
    public void StartEdge_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var pets = new PetSystem(_character.Pet, events, _intents);
        pets.SpawnBeside(PlayerFrom, 0f);
        var combat = new PlayerCombat(_character, events, _intents, Capacity);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            combat.Charge.Reset();
            combat.Charge.Request(0f);
            combat.Tick(Frame, Frame, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ, pets: pets);
        });
    }

    private void Press()
    {
        _intents.Clear();
        _combat.Charge.Request(0f);
        Step(Frame);
    }

    private void Step(float now) =>
        _combat.Tick(Frame, now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ, pets: _pets);

    private void ReportPet(int id, Vector3 position)
    {
        _snapshot.PetCount = 0;
        ref EnemySense sense = ref _snapshot.AddPet();
        sense.Id = id;
        sense.Position = position;
    }

    private static Vector3 Destination(ChargeIntent dash) => PlayerFrom
        + new Vector3(dash.DirectionXZ.X, 0f, dash.DirectionXZ.Y) * dash.Distance;

    private RunSession StartRun(CharacterSpec character)
    {
        _events.Clear();
        var random = new FixedRandom(5);
        var session = new RunSession(Catalog(character), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        session.Start(new RunConfig(Mode, Beastmaster, 5, 1, SpawnPlan.Empty, restore: null));
        return session;
    }

    private static CharacterSpec Character(PetSpec pet) => new(
        Beastmaster, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"),
        100f, new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Swap, 8f, Duration, 6f, 0.15f, 0f, 0f, Trail), pet: pet);

    private static ContentCatalog Catalog(CharacterSpec character) => new(new[] { character }, new[]
    {
        new EnemySpec(Frog, new LocKey("enemy.frog.name"), 100f, 2f, 1, 4, 5f, false, 8f, 1.2f,
            0.2f, 0.2f, 30f, EnemyBehaviourKind.Chaser),
    }, new[]
    {
        new ModeSpec(Mode, new LocKey("mode.swap_test.name"), 1, true, 0, Scalings.Design(), Scalings.Xp(),
            Array.Empty<RosterEntry>()),
    });
}
