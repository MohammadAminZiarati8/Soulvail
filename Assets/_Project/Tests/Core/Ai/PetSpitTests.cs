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
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>BT-07a rules 3 and 4: the boar's globs fly, hit, slow, and die at cover.</summary>
[TestFixture]
public sealed class PetSpitTests
{
    private const int Capacity = 8;
    private const float Dt = 0.1f;
    private static readonly ContentId EnemyId = new("enemy.spit_target");
    private static readonly ContentId BossBodyId = new("enemy.warden");
    private static readonly ContentId BossId = new("boss.warden");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 8f, 0.5f, 1.4f, 1.5f);
    private static readonly PetSpec Boar = new(new ContentId("pet.boar"), 60f, 3.6f, 6f, 1.5f, 1.4f, 1.5f,
        isRanged: true);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;
    private PetAgent _boar;

    [SetUp]
    public void SetUp() => Build(_events = new RecordingEvents());

    [Test]
    public void Spit_EveryInterval_HitsQuarry()
    {
        Set(PlayerStat.SpitDamage, 1.5f);
        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.UnitX * 4f);

        for (int i = 0; i <= 32; i++)
        {
            Tick(i * Dt);
        }

        Assert.That(_events.Count<PetSpat>(), Is.EqualTo(3), "At 0, 1.5 and 3 s.");
        PetSpat spat = _events.Of<PetSpat>()[0];
        Assert.That(spat.PetId, Is.EqualTo(_boar.Id));
        Assert.That(spat.TargetId, Is.EqualTo(target.Id));
        Assert.That(spat.From, Is.EqualTo(Vector3.Zero));
        Assert.That(_events.Count<GlobLanded>(), Is.EqualTo(2), "The third is still in the air.");
        Assert.That(_events.Of<GlobLanded>()[0].Hit, Is.True);
        Assert.That(_events.Of<GlobLanded>()[0].GlobId, Is.EqualTo(spat.GlobId));
        Assert.That(_pets.Spit.InFlight.Length, Is.EqualTo(1));
        Assert.That(DamageTo(target), Is.EqualTo(18f).Within(0.001f), "Two globs of 6 x 1.5.");
    }

    [Test]
    public void Spit_CoverOnPath_Dies()
    {
        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.UnitX * 4f);
        bool coverUp = false;
        Func<Vector3, Vector3, Vector3?> cover = (from, to) => coverUp ? from + (to - from) * 0.5f : null;

        Tick(0f, cover);
        Assert.That(_pets.Spit.InFlight.Length, Is.EqualTo(1));

        coverUp = true;
        Tick(Dt, cover);

        GlobLanded landed = _events.Single<GlobLanded>();
        Assert.That(landed.Hit, Is.False);
        Assert.That(landed.Position.X, Is.EqualTo(0.6f).Within(0.001f), "Halfway along its 1.2 m step.");
        Assert.That(_pets.Spit.InFlight.Length, Is.Zero);
        Assert.That(DamageTo(target), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Spit_SlowStat_Slows_BossImmune(bool boss)
    {
        Set(PlayerStat.SpitSlow, 0.25f);
        EnemyAgent target = boss
            ? _enemies.SpawnBoss(BossId, Vector3.UnitX * 4f)
            : _enemies.Spawn(EnemyId, Vector3.UnitX * 4f);

        for (int i = 0; i <= 3; i++)
        {
            Tick(i * Dt);
        }

        Assert.That(_events.Single<GlobLanded>().Hit, Is.True);
        Assert.That(target.CrowdControl.SlowFraction, Is.EqualTo(boss ? 0f : 0.25f));
    }

    [Test]
    public void Spit_Tick_NoAllocation()
    {
        Build(new SilentEvents());
        Func<Vector3, Vector3, Vector3?> clear = (_, _) => null;
        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.UnitX * 4f);
        float now = -2f;
        int hits = 0;
        AllocationAssert.None(() =>
        {
            target.Health.Reset();
            Tick(now += 2f, clear);
            Tick(now + 0.4f, clear, 0.4f);
            hits++;
        });

        Assert.That(DamageTo(target), Is.EqualTo(6f).Within(0.001f));
        Assert.That(_pets.Spit.InFlight.Length, Is.Zero);
        Assert.That(hits, Is.GreaterThan(1));
    }

    // The boar at the origin with the lead felled far away, so only the boar hunts.
    private void Build(IDomainEvents events)
    {
        _intents = new RecordingIntents();
        CharacterSpec character = Character();
        _combat = new PlayerCombat(character, events, _intents, Capacity);
        _enemies = new EnemySystem(Catalog(character), events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        _pets = new PetSystem(Hound, Boar, events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        Set(PlayerStat.BoarCount, 1f);
        PetAgent lead = _pets.Pets[0];
        _boar = _pets.Pets[1];
        ReportPosition(lead, new Vector3(30f, 0f, 30f));
        ReportPosition(_boar, Vector3.Zero);
        _pets.ApplyDamage(lead.Id, 1000f, 0f, 1);
    }

    private void Tick(float now, Func<Vector3, Vector3, Vector3?> traceCover = null, float dt = Dt)
    {
        _intents.Clear();
        _pets.Tick(dt, now, Vector3.Zero, _enemies, _combat, traceCover: traceCover);
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

    private static float DamageTo(EnemyAgent target) => target.Health.MaxHp.Value - target.Health.Current;

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
        var boss = new BossSpec(BossId, BossBodyId, new[] { new BossPhaseSpec(1f) }, 1.5f);
        var mode = new ModeSpec(new ContentId("mode.spit_test"), new LocKey("mode.spit_test.name"), 1, true, 0,
            new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                new ConcurrencyCurve(1, 1000), new StatCurve(0.06f, 4f, 1, 1),
                new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
            Scalings.Xp(), new[] { new RosterEntry(EnemyId, 1) }, hasSanctum: false);
        return new ContentCatalog(new[] { character },
            new[] { Body(EnemyId, EnemyBehaviourKind.Chaser), Body(BossBodyId, EnemyBehaviourKind.Boss) },
            new[] { mode }, bosses: new[] { boss });
    }

    private static EnemySpec Body(ContentId id, EnemyBehaviourKind behaviour) =>
        new(id, new LocKey(id.Value + ".name"), 100f, 2f, 1, 4, 5f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, behaviour);
}
