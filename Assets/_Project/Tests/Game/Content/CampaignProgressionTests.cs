using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;

namespace Soulvail.Tests.Game.Content;

[TestFixture]
public sealed class CampaignProgressionTests
{
    private const string Root = "Assets/_Project/";
    private const int Capacity = 64;
    private const int Seed = 12;
    private const float XpTolerance = 0.01f;
    private static readonly ContentId Ranger = new("character.ranger");

    [Test]
    public void Campaigns_FullClearMatchesAtEveryBoundary()
    {
        ModeSpec jungle = Mode("Jungle");
        ModeSpec sunscar = Mode("Sunscar");
        Boundary[] jungleEntries = Play(Catalog(jungle), jungle, 25);
        Boundary[] sunscarEntries = Play(Catalog(sunscar), sunscar, 25);

        for (int stage = 1; stage <= 25; stage++)
        {
            AssertProgress(sunscarEntries[stage], jungleEntries[stage], $"Stage {stage} entry");
        }

        foreach (var (cleared, level) in new[] { (1, 2), (4, 8), (9, 15), (14, 20), (19, 25), (23, 29), (24, 30) })
        {
            Assert.That(jungleEntries[cleared + 1].Level, Is.EqualTo(level), $"After clearing stage {cleared}");
            Assert.That(jungleEntries[cleared + 1].Points, Is.EqualTo(level - 1), $"After clearing stage {cleared}");
        }

        Assert.That(jungleEntries[24].Xp, Is.EqualTo(477.09f).Within(XpTolerance));
        Assert.That(jungleEntries[25].Xp, Is.Zero);
        foreach (Boundary[] entries in new[] { jungleEntries, sunscarEntries })
        {
            Assert.That(Enumerable.Range(2, 24).Where(stage => entries[stage].ReachedCap),
                Is.EqualTo(new[] { 25 }), "The only level-30 event is on stage 24.");
            Assert.That(entries[25].RegularKillReachedCap, Is.True);
        }
    }

    [TestCase("Jungle")]
    [TestCase("Sunscar")]
    public void Campaigns_EveryRegularStagePaysItsExactBaseBudget(string place)
    {
        ModeSpec mode = Mode(place);
        Boundary[] entries = Play(Catalog(mode), mode, 30);
        for (int stage = 1; stage < 30; stage++)
        {
            if (!mode.TryGetBossFor(stage, out _))
            {
                Assert.That(entries[stage + 1].BaseXp, Is.EqualTo(mode.CampaignXp.RegularBudget(stage)),
                    $"{place} stage {stage}");
            }
        }
    }

    [TestCase("Jungle")]
    [TestCase("Sunscar")]
    public void TestStart_MatchesNormalArrivalInBothCampaigns(string place)
    {
        ModeSpec mode = Mode(place);
        ContentCatalog catalog = Catalog(mode);
        Boundary[] entries = Play(catalog, mode, 30);
        var testStart = new TestStart(catalog);

        for (int stage = 1; stage <= 30; stage++)
        {
            var random = new SeededRandom(Seed);
            RunSnapshot snapshot = testStart.At(mode.Id, Ranger, Seed, random.Capture(), stage);
            Boundary expected = entries[stage];
            Assert.That(snapshot.Level, Is.EqualTo(expected.Level), $"Stage {stage} snapshot level");
            Assert.That(snapshot.Xp, Is.EqualTo(expected.Xp).Within(XpTolerance), $"Stage {stage} snapshot XP");
            Assert.That(snapshot.PendingLevelUps, Is.EqualTo(expected.Points), $"Stage {stage} snapshot points");

            var events = new RecordingEvents();
            RunSession restored = Start(catalog, mode, events, random, stage, snapshot);
            try
            {
                AssertProgress(Read(restored.State), expected, $"Restored {place} stage {stage}");
            }
            finally
            {
                restored.End();
            }
        }
    }

    [Test]
    public void CampaignAuthoring_UsesTheSharedProfile()
    {
        var shared = new CampaignXpProfile();
        foreach (string place in new[] { "Jungle", "Sunscar" })
        {
            ModeSpec mode = Mode(place);
            Assert.That(mode.CampaignXp, Is.Not.Null, place);
            Assert.That(mode.Xp.MaxLevel, Is.EqualTo(shared.Curve.MaxLevel), place);
            Assert.That(mode.CampaignXp.BossFraction, Is.EqualTo(shared.BossFraction), place);
            for (int stage = 1; stage <= mode.FinalStage; stage++)
            {
                Assert.That(mode.CampaignXp.RegularBudget(stage), Is.EqualTo(shared.RegularBudget(stage)), place);
                Assert.That(mode.Xp.ToReach(stage), Is.EqualTo(shared.Curve.ToReach(stage)), place);
            }
        }

        ModeSpec descent = Mode("Descent");
        Assert.That(descent.CampaignXp, Is.Null);
        Assert.That(descent.Xp.MaxLevel, Is.Zero);
        Assert.That(descent.Xp.ToReach(2), Is.EqualTo(20f + 12f * MathF.Pow(2f, 1.4f)).Within(XpTolerance));
    }

    private static Boundary[] Play(ContentCatalog catalog, ModeSpec mode, int lastEntry)
    {
        var events = new RecordingEvents();
        RunSession session = Start(catalog, mode, events, new SeededRandom(Seed), 1, null);
        const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        var enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", Internal).GetValue(session.State);
        var combat = (PlayerCombat)typeof(RunState).GetProperty("Combat", Internal).GetValue(session.State);
        var entries = new Boundary[lastEntry + 1];
        entries[1] = Read(session.State);
        var world = new WorldSnapshot(Capacity)
        {
            Dt = 0.1f,
            HasGate = true,
            GatePosition = new Vector3(0f, 0f, 18f),
            SpawnPoints = new[] { new Vector3(-8f, 0f, 10f), new Vector3(8f, 0f, 10f) },
        };

        try
        {
            for (int stage = 1; stage < lastEntry; stage++)
            {
                events.Clear();
                world.PlayerPosition = Vector3.Zero;
                float baseXp = 0f;
                bool regularKillReachedCap = false;

                for (int tick = 0; tick < 2400 && events.Count<StageCleared>() == 0 && session.IsRunning; tick++)
                {
                    foreach (EnemyAgent enemy in enemies.Registry.Alive.ToArray())
                    {
                        if (enemy.IsAlive)
                        {
                            enemies.ApplyGuardPiercingDamage(enemy.Id, 1e9f, session.State.Time, combat);
                        }
                    }

                    // The production drain is still observable after the level tracker reaches MAX.
                    float regularXp = enemies.PendingXp;
                    baseXp += regularXp;
                    bool wasBelowCap = !session.State.IsMaxLevel;
                    session.Tick(world);
                    regularKillReachedCap |= wasBelowCap && session.State.IsMaxLevel && regularXp > 0f;
                }

                StageCleared cleared = events.Single<StageCleared>();
                Assert.That(cleared.Stage, Is.EqualTo(stage));
                bool reachedCap = events.Of<LeveledUp>().Any(level => level.Level == 30);
                entries[stage + 1] = new Boundary(session.State, baseXp, reachedCap, regularKillReachedCap);

                world.PlayerPosition = cleared.GatePosition;
                for (int tick = 0; tick < 240 && session.State.StageIndex == stage; tick++)
                {
                    session.Tick(world);
                }

                Assert.That(session.State.StageIndex, Is.EqualTo(stage + 1), $"{mode.Id} exited stage {stage}");
                AssertProgress(Read(session.State), entries[stage + 1], $"{mode.Id} entered stage {stage + 1}");
            }
        }
        finally
        {
            session.End();
        }

        return entries;
    }

    private static Boundary Read(RunState state) => new(state);

    private static void AssertProgress(Boundary actual, Boundary expected, string context)
    {
        Assert.That(actual.Level, Is.EqualTo(expected.Level), context);
        Assert.That(actual.Xp, Is.EqualTo(expected.Xp).Within(XpTolerance), context);
        Assert.That(actual.Points, Is.EqualTo(expected.Points), context);
        Assert.That(actual.Unspent, Is.EqualTo(expected.Unspent), context);
    }

    private static RunSession Start(ContentCatalog catalog, ModeSpec mode, RecordingEvents events,
        SeededRandom random, int stage, RunSnapshot? restore)
    {
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, Capacity, 28, 64);
        session.Start(new RunConfig(mode.Id, Ranger, Seed, stage, SpawnPlan.Empty, restore));
        return session;
    }

    private static ModeSpec Mode(string place) =>
        AssetDatabase.LoadAssetAtPath<ModeDefinition>(Root + $"Data/Modes/{place}.asset").ToSpec();

    private static ContentCatalog Catalog(ModeSpec mode)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(Root + "Prefabs/Composition/BootScope.prefab");
        var boot = new SerializedObject(prefab.GetComponent<BootScope>());
        CharacterSpec ranger = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Ranger.asset").ToSpec();
        return new ContentCatalog(new[] { ranger },
            Definitions<EnemyDefinition>(boot, "_enemies").Select(enemy => enemy.ToSpec()).ToArray(),
            new[] { mode },
            bosses: Definitions<BossDefinition>(boot, "_bosses").Select(boss => boss.ToSpec()).ToArray());
    }

    private static T[] Definitions<T>(SerializedObject boot, string field) where T : UnityEngine.ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize)
            .Select(index => (T)rows.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
    }

    private readonly struct Boundary
    {
        public Boundary(RunState state, float baseXp = 0f, bool reachedCap = false, bool regularKillReachedCap = false)
        {
            Level = state.Level;
            Xp = state.Xp;
            Points = state.PendingLevelUps;
            Unspent = state.SkillPoints;
            BaseXp = baseXp;
            ReachedCap = reachedCap;
            RegularKillReachedCap = regularKillReachedCap;
        }

        public int Level { get; }
        public float Xp { get; }
        public int Points { get; }
        public int Unspent { get; }
        public float BaseXp { get; }
        public bool ReachedCap { get; }
        public bool RegularKillReachedCap { get; }
    }
}
