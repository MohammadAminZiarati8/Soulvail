using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// JG-20h: stage 20 is Thornheart's — its two assets, its pod, its roster row, and a stage that is
/// a boss stage like 10 and 15: no director waves, the door shut until the boss and its adds are down.
/// </summary>
/// <remarks>
/// The session rows build the catalog the way <c>BootScope</c> does, from its own lists, so a body
/// the boss summons that the catalog lacks is a red row here rather than a throw in the arena.
/// </remarks>
[TestFixture]
public sealed class ThornheartStageTests
{
    // LV-01b: the level the player arrives at, and the +2 % weapon damage each level past 1 adds.
    private const int ArrivalLevel = 23;
    private const float LevelDamage = 1f + (0.02f * (ArrivalLevel - 1));
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string ThornheartPath = "Assets/_Project/Data/Enemies/Thornheart.asset";
    private const string ThornheartBossPath = "Assets/_Project/Data/Enemies/ThornheartBoss.asset";
    private const string PodPath = "Assets/_Project/Data/Enemies/ThornPod.asset";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string PseudoPath = "Assets/_Project/Data/Localisation/Pseudo.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string ShamanId = "enemy.shaman";

    private const int Stage = 20;
    private const float Frame = 1f / 30f;

    [Test]
    public void Jungle_Thornheart_HasItsNumbers()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(ThornheartPath);
        EnemySpec heart = definition.ToSpec();
        EnemyLook look = definition.ToLook();

        Assert.That(heart.Id.Value, Is.EqualTo("enemy.thornheart"));
        Assert.That(heart.NameKey.Key, Is.EqualTo("enemy.thornheart.name"));
        Assert.That(heart.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That((heart.MoveSpeed, heart.ContactDamage, heart.XpValue, heart.ThreatCost),
            Is.EqualTo((0f, 20f, 600f, 40)));
        Assert.That(look.BodyScale, Is.EqualTo(3f));
        Assert.That(look.Body, Is.SameAs(Load<EnemyView>("Assets/_Project/Prefabs/Enemies/Thornheart.prefab")));
        Assert.That(look.Cover == null, Is.True, "The thorns are the arena's hazard (rule 6).");
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[heart.NameKey], Is.EqualTo("Thornheart"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[heart.NameKey], Is.Not.Empty);
    }

    [Test]
    public void Jungle_ThornheartBoss_HasThreePhases()
    {
        BossSpec boss = Load<BossDefinition>(ThornheartBossPath).ToSpec();

        Assert.That(boss.Id.Value, Is.EqualTo("boss.thornheart"));
        Assert.That(boss.EnemySpecId.Value, Is.EqualTo("enemy.thornheart"));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(boss.BeatSeconds, Is.EqualTo(1.5f));
        Assert.That(boss.Phases.Select(p => p.EntersBelow), Is.EqualTo(new[] { 1f, 0.66f, 0.33f }));
        Assert.That(boss.Phases.SelectMany(p => p.Summons), Is.Empty, "The pods are its adds.");
    }

    [Test]
    public void Jungle_ThornPod_IsStaticPrioritySeven_DiesToThreeArrows()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(PodPath);
        EnemySpec pod = definition.ToSpec();
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        float arrow = Load<CharacterDefinition>(RangerPath).ToSpec().Weapon.Damage;
        float hp = pod.MaxHp * jungle.Scaling.Hp.At(Stage);

        Assert.That(pod.Id.Value, Is.EqualTo("enemy.thorn_pod"));
        Assert.That(pod.Behaviour, Is.EqualTo(EnemyBehaviourKind.Static));
        Assert.That(pod.MoveSpeed, Is.Zero);
        Assert.That(pod.TargetPriority, Is.EqualTo(7));
        Assert.That(definition.ToLook().Body, Is.SameAs(Load<EnemyView>("Assets/_Project/Prefabs/Enemies/ThornPod.prefab")));
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[pod.NameKey], Is.EqualTo("Thorn Pod"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[pod.NameKey], Is.Not.Empty);

        // XpValue must be positive in a spec; the pod is summoned (SpawnSummon), so it pays none.
        Assert.That(pod.XpValue, Is.EqualTo(0.01f).Within(1e-6f), "The floor: summons pay no XP anyway.");

        Assert.That(hp, Is.GreaterThan(2f * arrow), "Two base arrows leave it standing.");
        Assert.That(hp, Is.LessThanOrEqualTo(3f * arrow), "Three base arrows kill it.");

        // Outranks whatever can share its fight; the Thornweaver (7, from 24) never meets a pod.
        var live = new RosterEntry[jungle.Roster.Count];
        int count = jungle.RosterFor(Stage, live);
        foreach (RosterEntry entry in live.Take(count).Where(r => r.SpecId.Value != ShamanId))
        {
            string path = AssetPath(entry.SpecId);
            Assert.That(Load<EnemyDefinition>(path).ToSpec().TargetPriority, Is.LessThan(pod.TargetPriority), path);
        }
    }

    [Test]
    public void Jungle_StageTwenty_IsNotScripted()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(Stage, out _), Is.False, "A boss stage has no director waves.");
        Assert.That(jungle.TryGetBossFor(Stage, out ContentId boss), Is.True);
        Assert.That(boss.Value, Is.EqualTo("boss.thornheart"));
    }

    [Test]
    public void Jungle_Thornheart_FightIsInTheBand()
    {
        EnemySpec heart = Load<EnemyDefinition>(ThornheartPath).ToSpec();
        BossSpec boss = Load<BossDefinition>(ThornheartBossPath).ToSpec();
        float hpScale = Load<ModeDefinition>(JunglePath).ToSpec().Scaling.Hp.At(Stage);
        // Rule 6: stage-20 Ranger ~33 DPS, always targetable; the heart doubles damage for 3 s of
        // every ~12 s cycle, x1.25 on average. Pods excluded, as the Warlord's guards are.
        const float EffectiveDps = 33f * LevelDamage * 1.25f;
        float seconds = (heart.MaxHp * hpScale / EffectiveDps) + ((boss.Phases.Count - 1) * boss.BeatSeconds);

        Assert.That(hpScale, Is.EqualTo(2.14f).Within(1e-5f));
        Assert.That(seconds, Is.InRange(90f, 100f), $"Aimed at ~95 s: {seconds:F1} s.");
    }

    [Test]
    public void Jungle_StageTwenty_StartsWithThornheart()
    {
        ContentCatalog catalog = Catalog(heartHp: null);

        Assert.That(catalog.Enemy(new ContentId("enemy.thorn_pod")), Is.Not.Null);
        Assert.That(catalog.Enemy(new ContentId("enemy.rootling")), Is.Not.Null, "A ripe pod grows Rootlings.");
        Assert.That(catalog.Boss(new ContentId("boss.thornheart")), Is.Not.Null);

        var events = new RecordingEvents();
        RunSession session = Start(catalog, events);

        try
        {
            Assert.That(session.State.StageIndex, Is.EqualTo(Stage));
            WorldSnapshot world = World();
            for (int frame = 0; frame < 300 && events.Count<EnemySpawned>() == 0; frame++)
            {
                session.Tick(world);
            }

            Assert.That(events.Of<EnemySpawned>().Select(e => e.SpecId.Value), Is.EqualTo(new[] { "enemy.thornheart" }),
                "The boss, alone: no director wave stands up beside it.");
        }
        finally
        {
            session.End();
        }
    }

    /// <summary>
    /// The boss plants its pods while the Ranger walks (and so holds fire), then falls to one arrow
    /// with the pods unseen; its adds die with it (JG-26d) and the door opens.
    /// </summary>
    /// <remarks>
    /// The boss is the authored one but for its health, which is cut to one arrow's worth so the row
    /// is about the door rather than the fight. Pods are withheld from perception until the boss is
    /// down, so the Ranger cannot take them first.
    /// </remarks>
    [Test]
    public void StageTwenty_DoorOpens_WhenBossFalls_AddsFallWithIt()
    {
        ContentCatalog catalog = Catalog(heartHp: 1f);
        var events = new RecordingEvents();
        RunSession session = Start(catalog, events);

        try
        {
            WorldSnapshot world = World();
            for (int frame = 0; frame < 300 && events.Count<EnemySpawned>() == 0; frame++)
            {
                session.Tick(world);
            }

            EnemySpawned heart = events.Single<EnemySpawned>();
            world.PlayerPosition = heart.Position + new Vector3(1.5f, 0f, 0f);
            world.OpenGround = Ground(heart.Position);

            // Walking: the Ranger holds fire while the boss plants.
            world.MoveInput = new Vector2(1f, 0f);
            for (int frame = 0; frame < 600 && events.Count<ThornPodPlanted>() == 0; frame++)
            {
                Sense(world, events, heart.Id);
                session.Tick(world);
            }

            Assert.That(events.Count<ThornPodPlanted>(), Is.GreaterThan(0), "The fixture's premise: pods were planted.");

            // Standing, with only the boss in sight.
            world.MoveInput = Vector2.Zero;
            int addsStanding = 0;
            for (int frame = 0; frame < 300 && !Died(events, heart.Id); frame++)
            {
                addsStanding = Breathing(events).Count - 1;
                Sense(world, events, heart.Id);
                session.Tick(world);
            }

            Assert.That(Died(events, heart.Id), Is.True, "The fixture's premise: the boss fell.");
            Assert.That(addsStanding, Is.GreaterThan(0), "The fixture's premise: adds stood when the boss fell.");
            Assert.That(Breathing(events), Is.Empty, "Every add died on the boss's tick.");

            for (int frame = 0; frame < 30 && events.Count<StageCleared>() == 0; frame++)
            {
                Sense(world, events, onlyId: 0);
                session.Tick(world);
            }

            Assert.That(events.Count<StageCleared>(), Is.EqualTo(1));
            Assert.That(Breathing(events), Is.Empty);
        }
        finally
        {
            session.End();
        }
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>
    /// BootScope's catalog; with <paramref name="heartHp"/>, Thornheart's spec swapped for one with
    /// that authored health and nothing else changed.
    /// </summary>
    private static ContentCatalog Catalog(float? heartHp)
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        EnemySpec[] enemies = BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec());

        if (heartHp.HasValue)
        {
            EnemyDefinition copy = Object.Instantiate(Load<EnemyDefinition>(ThornheartPath));
            try
            {
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("_maxHp").floatValue = heartHp.Value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EnemySpec weak = copy.ToSpec();
                enemies = enemies.Select(e => e.Id == weak.Id ? weak : e).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        return new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            enemies,
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
    }

    private static RunSession Start(ContentCatalog catalog, RecordingEvents events)
    {
        var random = new Soulvail.Game.Adapters.SeededRandom(12);
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), Stage);

        session.Start(new RunConfig(mode, ranger, random.Seed, Stage, SpawnPlan.Empty, restore));

        return session;
    }

    private static WorldSnapshot World() => new WorldSnapshot(64)
    {
        Dt = Frame,
        SpawnPoints = new[]
        {
            new Vector3(-8f, 0f, 8f), new Vector3(8f, 0f, 8f),
            new Vector3(-8f, 0f, -8f), new Vector3(8f, 0f, -8f),
        },
    };

    /// <summary>A 2 m grid of open ground 3–9 m around <paramref name="centre"/>.</summary>
    private static IReadOnlyList<Vector3> Ground(Vector3 centre)
    {
        var ground = new List<Vector3>();
        for (int x = -4; x <= 4; x++)
        {
            for (int z = -4; z <= 4; z++)
            {
                var offset = new Vector3(x * 2f, 0f, z * 2f);
                float distance = offset.Length();
                if (distance >= 3f && distance <= 9f)
                {
                    ground.Add(centre + offset);
                }
            }
        }

        return ground;
    }

    /// <summary>
    /// Reports every living enemy where it stood up, in sight — or only <paramref name="onlyId"/>
    /// when it is not 0.
    /// </summary>
    private static void Sense(WorldSnapshot world, RecordingEvents events, int onlyId)
    {
        Vector3 player = world.PlayerPosition;
        Vector2 input = world.MoveInput;
        IReadOnlyList<Vector3> ground = world.OpenGround;
        IReadOnlyList<Vector3> points = world.SpawnPoints;

        world.Clear();
        world.Dt = Frame;
        world.PlayerPosition = player;
        world.MoveInput = input;
        world.OpenGround = ground;
        world.SpawnPoints = points;

        foreach (EnemySpawned spawned in Breathing(events))
        {
            if (onlyId != 0 && spawned.Id != onlyId)
            {
                continue;
            }

            ref EnemySense sense = ref world.AddEnemy();
            sense.Id = spawned.Id;
            sense.Position = spawned.Position;
            sense.Velocity = Vector3.Zero;
            sense.HasLineOfSight = true;
        }
    }

    /// <summary>Every enemy spawned and not since dead or despawned.</summary>
    private static List<EnemySpawned> Breathing(RecordingEvents events)
    {
        var gone = new HashSet<int>(events.Of<EnemyDied>().Select(e => e.Id)
            .Concat(events.Of<EnemyDespawned>().Select(e => e.Id)));

        return events.Of<EnemySpawned>().Where(e => !gone.Contains(e.Id)).ToList();
    }

    private static bool Died(RecordingEvents events, int id) => events.Of<EnemyDied>().Any(e => e.Id == id);

    /// <summary><c>enemy.mossguard</c> → the asset whose definition carries that id.</summary>
    private static string AssetPath(ContentId id)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/_Project/Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec().Id == id)
            {
                return path;
            }
        }

        Assert.Fail($"No enemy asset carries {id}.");
        return null;
    }

    private static TSpec[] BootSpecs<TDefinition, TSpec>(SerializedObject boot, string field, System.Func<TDefinition, TSpec> convert)
        where TDefinition : ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize)
            .Select(i => convert((TDefinition)rows.GetArrayElementAtIndex(i).objectReferenceValue)).ToArray();
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }
}
