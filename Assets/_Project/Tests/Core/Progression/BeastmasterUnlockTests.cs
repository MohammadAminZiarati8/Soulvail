using System;
using System.Numerics;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

[TestFixture]
public sealed class BeastmasterUnlockTests
{
    private const int FinalStage = 30;
    private const int Price = 5_000;
    private const int Capacity = 32;

    private static readonly ContentId Beastmaster = new ContentId("character.beastmaster");
    private static readonly ContentId Apex = new ContentId("boss.apex");
    private static readonly ContentId Warden = new ContentId("boss.warden");
    private static readonly ContentId Body = new ContentId("enemy.test_boss");
    private static readonly ContentId Executioner = new ContentId("enemy.executioner");
    private static readonly ContentId Jungle = new ContentId("mode.jungle");
    private static readonly ContentId Oathbound = new ContentId("character.oathbound");

    [Test]
    public void NewProfile_BeastmasterLocked()
    {
        Assert.That(ClassUnlocks.IsUnlocked(Beastmaster, PlayerProfile.Default, Catalog(Apex)), Is.False);
    }

    [TestCase("boss.apex", false, true, TestName = "JungleApexKill_Unlocks")]
    [TestCase("boss.warden", false, false, TestName = "OtherBossKill_DoesNot")]
    [TestCase("boss.apex", true, false, TestName = "DeathAtApex_DoesNot")]
    public void OnlyClearingTheApexEarnsTheDeed(string bossId, bool dies, bool expected)
    {
        ContentCatalog catalog = Catalog(new ContentId(bossId));
        ShardsAwarded payout = FinishRun(catalog, dies);
        var earned = new ContentId[catalog.Characters.Count];
        int count = ClassUnlocks.Earned(payout.DeepestStage, catalog.Mode(payout.ModeId), PlayerProfile.Default, catalog, earned);

        Assert.That(earned.AsSpan(0, count).ToArray(), Is.EqualTo(expected ? new[] { Beastmaster } : Array.Empty<ContentId>()));
    }

    [TestCase(false, FinalStage + 1, TestName = "ModeWon_PayoutDepthIsPastLastStage")]
    [TestCase(true, FinalStage, TestName = "Death_PayoutDepthUnchanged")]
    public void PayoutDepthCountsClearedFinalStage(bool dies, int expected)
    {
        Assert.That(FinishRun(Catalog(Apex), dies).DeepestStage, Is.EqualTo(expected));
    }

    [Test]
    public void ShardPurchase_Unlocks()
    {
        ContentCatalog catalog = Catalog(Apex);
        PlayerProfile profile = PlayerProfile.Default.WithShards(Price);

        Assert.That(ClassUnlocks.CanBuy(Beastmaster, profile.WithShards(Price - 1), catalog), Is.False);
        Assert.That(ClassUnlocks.CanBuy(Beastmaster, profile, catalog), Is.True);
    }

    [Test]
    public async Task Unlock_SurvivesSaveRoundTrip()
    {
        var saves = new InMemorySaveStore();
        await saves.SaveProfile(PlayerProfile.Default.WithUnlocked(new[] { Beastmaster }));
        PlayerProfile reloaded = (await saves.LoadProfile()).Value;

        Assert.That(ClassUnlocks.IsUnlocked(Beastmaster, reloaded, Catalog(Apex)), Is.True);
    }

    private static ShardsAwarded FinishRun(ContentCatalog catalog, bool dies)
    {
        var events = new RecordingEvents();
        var random = new FixedRandom();
        var session = new RunSession(
            catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events), Capacity, Capacity, 8);
        SpawnPlan plan = dies
            ? new SpawnPlan(new[] { new SpawnPlan.Entry(Executioner, Vector3.Zero) })
            : SpawnPlan.Empty;
        session.Start(new RunConfig(Jungle, Oathbound, random.Seed, FinalStage, plan, restore: null));

        var snapshot = new WorldSnapshot(Capacity)
        {
            Dt = 1f / 60f,
            SpawnPoints = new[] { new Vector3(0f, 0f, 10f) },
        };
        var hits = new int[1];

        for (int i = 0; i < 600 && session.IsRunning; i++)
        {
            if (!dies && events.Count<EnemySpawned>() > 0 && events.Count<EnemyDied>() == 0)
            {
                EnemySpawned boss = events.Single<EnemySpawned>();
                snapshot.PlayerPosition = boss.Position - Vector3.UnitZ;
                hits[0] = boss.Id;
                session.Tick(snapshot);
                session.ReportConeHits(hits);
            }
            else
            {
                session.Tick(snapshot);
            }
        }

        Assert.That(events.Count<ModeFinished>(), Is.EqualTo(dies ? 0 : 1), "The fixture must take the intended run-end path.");
        return events.Single<ShardsAwarded>();
    }

    private static ContentCatalog Catalog(ContentId bossId)
    {
        var mode = new ModeSpec(
            Jungle, new LocKey("mode.jungle.name"), startingStage: 1, isEndless: false, FinalStage,
            Scalings.Design(), Scalings.Xp(), new[] { new RosterEntry(Executioner, 1) },
            bossRoster: new[] { new BossRosterEntry(bossId, everyNStages: 0, atStage: FinalStage) },
            hasVeilrot: false, hasSanctum: false);

        return new ContentCatalog(
            new[] { Character(Oathbound), Character(Beastmaster, new UnlockSpec(Price, deedBossId: Apex)) },
            new[]
            {
                new EnemySpec(Body, new LocKey("enemy.test_boss.name"), 1f, 1f, 1, 4, 1f, false, 1f, 1f, 1f, 1f, 30f, EnemyBehaviourKind.Boss),
                new EnemySpec(Executioner, new LocKey("enemy.executioner.name"), 500f, 3.5f, 1, 4, 12f, false, 400f, 2f, 0.05f, 0.6f, 30f, EnemyBehaviourKind.Chaser),
            },
            new[] { mode },
            bosses: new[]
            {
                new BossSpec(Apex, Body, new[] { new BossPhaseSpec(1f) }, 1f),
                new BossSpec(Warden, Body, new[] { new BossPhaseSpec(1f) }, 1f),
            });
    }

    private static CharacterSpec Character(ContentId id, UnlockSpec unlock = null) => new CharacterSpec(
        id,
        new LocKey(id.Value + ".name"),
        new LocKey(id.Value + ".description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 100f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        unlock: unlock);
}
