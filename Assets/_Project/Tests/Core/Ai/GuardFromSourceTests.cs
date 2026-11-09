using System.Collections.Generic;
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

/// <summary>BT-09b: a frontal guard judges where a pet's hit comes from.</summary>
[TestFixture]
public sealed class GuardFromSourceTests
{
    private const int Capacity = 16;
    private const float Dt = 0.1f;
    private static readonly ContentId BulwarkId = new("enemy.guard_bulwark");
    private static readonly ContentId FrogId = new("enemy.guard_frog");
    private static readonly ContentId WarlordBodyId = new("enemy.guard_warlord");
    private static readonly ContentId MossguardId = new("enemy.guard_mossguard");
    private static readonly ContentId WarlordId = new("boss.guard_warlord");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f);
    private static readonly PetSpec Boar = new(new ContentId("pet.boar"), 60f, 3.6f, 6f, 1.5f, 1.4f, 1.5f,
        isRanged: true);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private PetSystem _pets;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _projectiles = new ProjectileSystem(_events, Capacity);
        _lanes = new LaneSystem(_events);
        var random = new FixedRandom(0.5f);
        _enemies = new EnemySystem(Catalog(), _events, random, new DepthScaling(Scalings.Design()), Capacity,
            (agent, _) => new WarlordBehaviour(agent, _projectiles, random.Misc, MossguardId));
        _combat = new PlayerCombat(Character(), _events, _intents, Capacity);
        _now = 0f;
    }

    // Rules 2 and 5: a raised Mossguard faces the player at +Z; the bite is judged from the hound.
    [TestCase(0f, -1f, true, TestName = "Bite_FromBehindBulwark_Lands")]
    [TestCase(0f, 1f, false, TestName = "Bite_FromFrontOfBulwark_Blocked")]
    [TestCase(1f, 0f, true, TestName = "Bite_FromSideOutsideArc_Lands")]
    public void Bite_AtRaisedBulwark_JudgedFromTheHound(float x, float z, bool lands)
    {
        EnemyAgent bulwark = RaisedBulwark();
        Bite(new Vector3(x, 0f, z));

        Assert.That(_events.Count<PetBit>(), Is.EqualTo(1));
        Assert.That(DamageTo(bulwark), lands ? Is.EqualTo(7f).Within(1e-4f) : Is.Zero);
    }

    // Rules 1, 2 and 4: a boss's inner Warlord is the guard while the boss is outside its beat.
    [Test]
    public void Bite_AtWarlordBack_Lands()
    {
        EnemyAgent warlord = GuardingWarlord();
        Bite(-Vector3.UnitZ);

        Assert.That(DamageTo(warlord), Is.EqualTo(7f).Within(1e-4f));
    }

    // Rule 2: a phase beat still blocks everything, the back included.
    [Test]
    public void Bite_AtWarlordInBeat_Blocked()
    {
        EnemyAgent warlord = GuardingWarlord();
        Face(warlord, -Vector2.UnitY);
        EnemyTick();
        Assert.That(warlord.IsVulnerable, Is.True, "Player behind: the guard is down.");
        _enemies.ApplyDamage(warlord.Id, warlord.Health.MaxHp.Value * 0.4f, _now, _combat);
        Face(warlord, Vector2.UnitY);
        EnemyTick();
        var boss = (BossBehaviour)warlord.Behaviour;
        Assert.That(boss.IsInBeat, Is.True);
        float before = warlord.Health.Current;

        Bite(-Vector3.UnitZ);

        Assert.That(warlord.Health.Current, Is.EqualTo(before));
        IReadOnlyList<EnemyDamaged> damaged = _events.Of<EnemyDamaged>();
        Assert.That(damaged[damaged.Count - 1].Blocked, Is.True);
    }

    // Rule 3: a source on top of the enemy has no direction, so it is covered as today.
    [Test]
    public void Bite_FromSourceOnTop_TreatedAsCovered()
    {
        EnemyAgent bulwark = RaisedBulwark();
        Bite(bulwark.Position);

        Assert.That(_events.Count<PetBit>(), Is.EqualTo(1));
        Assert.That(DamageTo(bulwark), Is.Zero);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void ApplyDamageFrom_NonFiniteSource_TreatedAsCovered(float bad)
    {
        EnemyAgent bulwark = RaisedBulwark();
        DamageResult result = _enemies.ApplyDamageFrom(bulwark.Id, 10f, _now, _combat, new Vector3(bad, 0f, -1f));

        Assert.That(result.Blocked, Is.True);
        Assert.That(DamageTo(bulwark), Is.Zero);
    }

    // Rule 5: a glob is judged from where it flew in, not from the target's centre it lands on.
    [TestCase(-4f, true, TestName = "Glob_FromBehind_Lands")]
    [TestCase(4f, false, TestName = "Glob_FromFront_Blocked")]
    public void Glob_AtRaisedBulwark_JudgedFromTheGlob(float boarZ, bool lands)
    {
        EnemyAgent bulwark = RaisedBulwark();
        _pets = new PetSystem(Hound, Boar, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _pets.LeadStats.Resolve(PlayerStat.BoarCount).Base = 1f;
        PetAgent lead = _pets.Pets[0];
        PetAgent boar = _pets.Pets[1];
        Report(lead, new Vector3(30f, 0f, 30f));
        Report(boar, new Vector3(0f, 0f, boarZ));
        _pets.ApplyDamage(lead.Id, 1000f, 0f, 1);

        for (int i = 0; i <= 10; i++)
        {
            _pets.Tick(Dt, i * Dt, Vector3.Zero, _enemies, _combat);
        }

        Assert.That(_events.Count<GlobLanded>(), Is.EqualTo(1));
        Assert.That(_events.Single<GlobLanded>().Hit, Is.True);
        Assert.That(DamageTo(bulwark) > 0f, Is.EqualTo(lands));
    }

    // Rule 6: a dive comes from above, so the raised shield never covers it.
    [Test]
    public void HawkDive_AtRaisedGuard_Lands()
    {
        EnemyAgent bulwark = RaisedBulwark();
        _enemies.Mark(bulwark.Id, 10f);
        var stats = new PlayerStats(_combat, new PlayerMotor(Character().Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        var hawk = new HawkSystem(_events, stats);
        _combat.Weapon.Damage.Base = 15f;
        hawk.Call(0f, 2f, 0.4f);

        for (float now = 0f; now < 2.5f && _events.Count<HawkStruck>() == 0; now += 0.01f)
        {
            hawk.Tick(0.01f, now, Vector3.Zero, _enemies, _combat);
        }

        Assert.That(_events.Count<HawkStruck>(), Is.EqualTo(1));
        Assert.That(bulwark.IsVulnerable, Is.False, "The shield was up for the player.");
        Assert.That(DamageTo(bulwark), Is.EqualTo(30f).Within(1e-4f));
    }

    // Rule 1: without a guard, the direction changes nothing.
    [Test]
    public void NoGuard_ApplyDamageFrom_SameAsApplyDamage()
    {
        EnemyAgent first = _enemies.Spawn(FrogId, Vector3.Zero);
        EnemyAgent second = _enemies.Spawn(FrogId, Vector3.UnitX * 5f);

        DamageResult plain = _enemies.ApplyDamage(first.Id, 12f, _now, _combat);
        DamageResult from = _enemies.ApplyDamageFrom(second.Id, 12f, _now, _combat, Vector3.UnitX * 6f);

        Assert.That(from.Applied, Is.EqualTo(plain.Applied));
        Assert.That(from.Blocked, Is.EqualTo(plain.Blocked));
        Assert.That(from.Killed, Is.EqualTo(plain.Killed));
        Assert.That(second.Health.Current, Is.EqualTo(first.Health.Current));
    }

    private EnemyAgent RaisedBulwark()
    {
        EnemyAgent bulwark = _enemies.Spawn(BulwarkId, Vector3.Zero);
        Assert.That(bulwark.Behaviour, Is.InstanceOf<BulwarkBehaviour>());
        Face(bulwark, Vector2.UnitY);
        EnemyTick();
        Assert.That(bulwark.IsVulnerable, Is.False, "Raised at the player in front.");
        return bulwark;
    }

    private EnemyAgent GuardingWarlord()
    {
        EnemyAgent warlord = _enemies.SpawnBoss(WarlordId, Vector3.Zero);
        Assert.That(((BossBehaviour)warlord.Behaviour).Inner, Is.InstanceOf<WarlordBehaviour>());
        Face(warlord, Vector2.UnitY);
        EnemyTick();
        Assert.That(warlord.IsVulnerable, Is.False, "Guarding the player in front.");
        return warlord;
    }

    private static void Face(EnemyAgent agent, Vector2 towardPlayer)
    {
        EnemyBlackboard board = agent.Blackboard;
        board.SelfPosition = agent.Position;
        board.PlayerPosition = agent.Position + new Vector3(towardPlayer.X, 0f, towardPlayer.Y) * 10f;
        board.DirectionToPlayer = towardPlayer;
        board.DirectionToRealPlayer = towardPlayer;
        board.PathDirectionToPlayer = towardPlayer;
        board.DistanceToPlayer = 10f;
    }

    private void EnemyTick()
    {
        _intents.Clear();
        _enemies.Tick(new EnemyTickContext(0f, _now, _combat, _intents, _events, _projectiles, _enemies, _lanes));
    }

    private void Bite(Vector3 hound)
    {
        _pets = new PetSystem(Hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        Report(_pets.Pets[0], hound);
        _intents.Clear();
        _pets.Tick(Dt, _now, Vector3.Zero, _enemies, _combat);
    }

    private void Report(PetAgent pet, Vector3 position)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddPet();
        sense.Id = pet.Id;
        sense.Position = position;
        _pets.Ingest(snapshot);
    }

    private static float DamageTo(EnemyAgent enemy) => enemy.Health.MaxHp.Value - enemy.Health.Current;

    private static ContentCatalog Catalog() => new(
        new[] { Character() },
        new[]
        {
            new EnemySpec(BulwarkId, new LocKey("enemy.guard_bulwark.name"),
                maxHp: 90f, moveSpeed: 1.6f, targetPriority: 3, threatCost: 14, xpValue: 42f,
                isElite: false, contactDamage: 16f, reach: 1.6f, windupTime: 0.7f,
                recoverTime: 1.5f, aggroRange: 30f, behaviour: EnemyBehaviourKind.Bulwark,
                bulwark: new BulwarkSpec(60f, 75f, 45f)),
            Body(FrogId, 100f, EnemyBehaviourKind.Static),
            Body(WarlordBodyId, 1000f, EnemyBehaviourKind.Boss),
            Body(MossguardId, 10f, EnemyBehaviourKind.Static),
        },
        modes: null, skills: null, trees: null,
        bosses: new[]
        {
            new BossSpec(WarlordId, WarlordBodyId,
                new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f), new BossPhaseSpec(0.33f) }, 1.5f),
        });

    private static EnemySpec Body(ContentId id, float hp, EnemyBehaviourKind behaviour) =>
        new(id, new LocKey(id.Value + ".name"), hp, 1.8f, 8, 40, 30f, false, 20f, 2.5f, 0.9f, 1.2f, 40f, behaviour);

    private static CharacterSpec Character() => new(
        new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
        new LocKey("character.beastmaster.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        pet: Hound, boar: Boar);
}
