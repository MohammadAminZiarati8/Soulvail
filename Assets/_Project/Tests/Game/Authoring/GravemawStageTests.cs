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
/// JG-25g: stage 25 is Gravemaw's — its two assets, its ruins, its rock, its roster row, and a boss
/// stage like 10, 15 and 20: no director waves, the door shut until the boss and its Frogs are down.
/// </summary>
/// <remarks>
/// The session rows build the catalog the way <c>BootScope</c> does, from its own lists, so a body
/// the boss summons that the catalog lacks is a red row here rather than a throw in the arena.
/// Gravemaw travels, so the fixture walks every body by the velocity core asked of it.
/// </remarks>
[TestFixture]
public sealed class GravemawStageTests
{
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string GravemawPath = "Assets/_Project/Data/Enemies/Gravemaw.asset";
    private const string GravemawBossPath = "Assets/_Project/Data/Enemies/GravemawBoss.asset";
    private const string RuinsPath = "Assets/_Project/Prefabs/Arenas/Jungle/Cover_GravemawRuins.prefab";
    private const string RockPath = "Assets/_Project/Prefabs/Projectiles/GravemawRock.prefab";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string PseudoPath = "Assets/_Project/Data/Localisation/Pseudo.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string FrogId = "enemy.frog";

    private const int Stage = 25;
    private const float Frame = 1f / 30f;

    private readonly Dictionary<int, Vector3> _at = new();
    private int _movesRead;

    [SetUp]
    public void SetUp()
    {
        _at.Clear();
        _movesRead = 0;
    }

    [Test]
    public void Jungle_Gravemaw_HasItsNumbers()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(GravemawPath);
        EnemySpec maw = definition.ToSpec();
        EnemyLook look = definition.ToLook();

        Assert.That(maw.Id.Value, Is.EqualTo("enemy.gravemaw"));
        Assert.That(maw.NameKey.Key, Is.EqualTo("enemy.gravemaw.name"));
        Assert.That(maw.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That((maw.MoveSpeed, maw.ContactDamage, maw.XpValue, maw.ThreatCost, maw.TargetPriority),
            Is.EqualTo((2.8f, 20f, 600f, 40, 2)), "2.8: every class outruns it (GD §6.1); the tunnel runs at 3x.");
        Assert.That(look.BodyScale, Is.EqualTo(3f));
        Assert.That(look.Body, Is.SameAs(Load<EnemyView>("Assets/_Project/Prefabs/Enemies/Gravemaw.prefab")));
        Assert.That(look.Projectile, Is.SameAs(Load<ProjectileView>(RockPath)));
        Assert.That(look.MarksLanding, Is.True, "The spit's rocks land in rings.");
        Assert.That(look.Cover, Is.SameAs(Load<GameObject>(RuinsPath)));
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[maw.NameKey], Is.EqualTo("Gravemaw"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[maw.NameKey], Is.Not.Empty);
    }

    [Test]
    public void Jungle_GravemawBoss_HasItsPhases()
    {
        BossSpec boss = Load<BossDefinition>(GravemawBossPath).ToSpec();

        Assert.That(boss.Id.Value, Is.EqualTo("boss.gravemaw"));
        Assert.That(boss.EnemySpecId.Value, Is.EqualTo("enemy.gravemaw"));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(boss.BeatSeconds, Is.EqualTo(1.5f));
        Assert.That(boss.Phases.Select(p => p.EntersBelow), Is.EqualTo(new[] { 1f, 0.5f }));
        Assert.That(boss.Phases.SelectMany(p => p.Summons), Is.Empty, "The cracks are its adds.");
    }

    [Test]
    public void Jungle_GravemawRock_IsAnArcingProjectileView()
    {
        var rock = Load<ProjectileView>(RockPath);

        Assert.That(new SerializedObject(rock).FindProperty("_arcHeight").floatValue, Is.GreaterThan(0f), "A lob, not a dart.");
        Renderer[] renderers = rock.GetComponentsInChildren<Renderer>();
        Assert.That(renderers, Is.Not.Empty);
        Assert.That(rock.GetComponentsInChildren<Collider>(), Is.Empty, "Core decides the hit, not a collider.");
    }

    [Test]
    public void Jungle_StageTwentyFive_IsNotScripted()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(Stage, out _), Is.False, "A boss stage has no director waves.");
        Assert.That(jungle.TryGetBossFor(Stage, out ContentId boss), Is.True);
        Assert.That(boss.Value, Is.EqualTo("boss.gravemaw"));
        Assert.That(jungle.TryGetBossFor(30, out ContentId last), Is.True);
        Assert.That(last.Value, Is.EqualTo("boss.croaker"), "30 waits for the Apex.");
    }

    [Test]
    public void Jungle_GravemawRuins_AreCoverAndRingTheCentre()
    {
        GameObject prefab = Load<GameObject>(RuinsPath);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab),
            Is.SameAs(Load<GameObject>("Assets/_Project/Prefabs/Arenas/Jungle/Cover_MotherStones.prefab")),
            "A variant of the Mother's stones.");
        GameObject ruins = Object.Instantiate(prefab, UnityEngine.Vector3.zero, Quaternion.identity);
        try
        {
            Physics.SyncTransforms();
            BoxCollider[] boxes = ruins.GetComponentsInChildren<BoxCollider>();
            Assert.That(boxes.Length, Is.EqualTo(4));
            var angles = new List<float>();
            foreach (BoxCollider box in boxes)
            {
                Assert.That(box.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")), box.name);
                Assert.That(box.isTrigger, Is.False, box.name);
                UnityEngine.Vector3 half = UnityEngine.Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
                Assert.That(Mathf.Min(half.x, half.z), Is.GreaterThanOrEqualTo(1.2f), box.name);
                UnityEngine.Vector3 at = box.bounds.center;
                float distance = new UnityEngine.Vector2(at.x, at.z).magnitude;
                Assert.That(distance, Is.InRange(5.5f, 7f), box.name);
                Assert.That(distance - new UnityEngine.Vector2(half.x, half.z).magnitude, Is.GreaterThan(3f), $"{box.name} clears the centre.");
                angles.Add(Mathf.Repeat(Mathf.Atan2(at.x, at.z) * Mathf.Rad2Deg, 360f));
            }

            angles.Sort();
            float[] gaps = angles.Select((a, i) => Mathf.Repeat(angles[(i + 1) % angles.Count] - a, 360f)).ToArray();
            Assert.That(gaps.Max(), Is.LessThanOrEqualTo(120f), "Every side has a pillar to be thrown against.");
            Assert.That(gaps.Max() - gaps.Min(), Is.GreaterThan(5f), "Uneven angles.");
        }
        finally
        {
            Object.DestroyImmediate(ruins);
        }
    }

    [Test]
    public void Jungle_Gravemaw_FightIsInTheBand()
    {
        EnemySpec maw = Load<EnemyDefinition>(GravemawPath).ToSpec();
        BossSpec boss = Load<BossDefinition>(GravemawBossPath).ToSpec();
        float hpScale = Load<ModeDefinition>(JunglePath).ToSpec().Scaling.Hp.At(Stage);
        // Rule 6: stage-25 Ranger ~33 DPS, half of it — a 4 s window in an ~8 s cycle. Frogs excluded.
        const float EffectiveDps = 33f * 0.5f;
        float seconds = (maw.MaxHp * hpScale / EffectiveDps) + ((boss.Phases.Count - 1) * boss.BeatSeconds);

        Assert.That(hpScale, Is.EqualTo(2.44f).Within(1e-5f));
        Assert.That(seconds, Is.InRange(90f, 100f), $"Aimed at ~95 s: {seconds:F1} s.");
    }

    [Test]
    public void Jungle_StageTwentyFive_StartsWithGravemaw()
    {
        ContentCatalog catalog = Catalog(scaledHp: null);

        Assert.That(catalog.Enemy(new ContentId(FrogId)), Is.Not.Null, "Cracks spill Frogs.");
        Assert.That(catalog.Boss(new ContentId("boss.gravemaw")), Is.Not.Null);

        var events = new RecordingEvents();
        RunSession session = Start(catalog, events, new RecordingIntents());

        try
        {
            Assert.That(session.State.StageIndex, Is.EqualTo(Stage));
            WorldSnapshot world = World();
            for (int frame = 0; frame < 300 && events.Count<EnemySpawned>() == 0; frame++)
            {
                session.Tick(world);
            }

            Assert.That(events.Of<EnemySpawned>().Select(e => e.SpecId.Value), Is.EqualTo(new[] { "enemy.gravemaw" }),
                "The boss, alone: no director wave stands up beside it.");
        }
        finally
        {
            session.End();
        }
    }

    /// <summary>
    /// One arrow takes the boss into phase 2; its eruptions there open cracks that spill Frogs; a
    /// second arrow kills it with the Frogs unseen. The Frogs die with it (JG-26d) and the door opens.
    /// </summary>
    /// <remarks>
    /// The boss is the authored one but for its health, cut to a arrow and a half so the row is about
    /// the door rather than the fight. The Ranger steps 6 m clear of every ring as it arms, so no
    /// eruption lands; Frogs are withheld from perception until the boss is down.
    /// </remarks>
    [Test]
    public void StageTwentyFive_DoorOpens_WhenBossFalls_FrogsFallWithIt()
    {
        float arrow = Load<CharacterDefinition>(RangerPath).ToSpec().Weapon.Damage;
        ContentCatalog catalog = Catalog(scaledHp: 1.5f * arrow);
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        RunSession session = Start(catalog, events, intents);

        try
        {
            WorldSnapshot world = World();
            for (int frame = 0; frame < 300 && events.Count<EnemySpawned>() == 0; frame++)
            {
                session.Tick(world);
            }

            EnemySpawned maw = events.Single<EnemySpawned>();
            world.PlayerPosition = maw.Position + new Vector3(0f, 0f, 8f);
            world.OpenGround = Ground(maw.Position);
            int rings = 0;
            int frogsStanding = 0;

            // Standing (so firing), with only the boss in sight, stepping clear of every ring.
            for (int frame = 0; frame < 1500 && !Died(events, maw.Id); frame++)
            {
                frogsStanding = Breathing(events).Count(e => e.SpecId.Value == FrogId);
                rings = Dodge(world, events, rings);
                Sense(world, events, intents, maw.Id);
                session.Tick(world);
            }

            Assert.That(events.Of<BossPhaseChanged>().Any(e => e.Phase == 1), Is.True, "The fixture's premise: phase 2.");
            Assert.That(Died(events, maw.Id), Is.True, "The fixture's premise: the boss fell.");
            Assert.That(frogsStanding, Is.GreaterThan(0), "The fixture's premise: Frogs stood when the boss fell.");
            Assert.That(Breathing(events), Is.Empty, "Every Frog died on the boss's tick.");

            for (int frame = 0; frame < 30 && events.Count<StageCleared>() == 0; frame++)
            {
                Sense(world, events, intents, onlyId: 0);
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
    /// Walks every body by the velocity core last asked of it, then reports every living enemy, in sight — or only <paramref name="onlyId"/> when it is not 0.
    /// </summary>
    private void Sense(WorldSnapshot world, RecordingEvents events, RecordingIntents intents, int onlyId)
    {
        IReadOnlyList<EnemyMoveIntent> moves = intents.EnemyMoves;
        for (; _movesRead < moves.Count; _movesRead++)
        {
            EnemyMoveIntent move = moves[_movesRead];
            if (_at.TryGetValue(move.Id, out Vector3 at))
            {
                _at[move.Id] = at + (move.Velocity * Frame);
            }
        }

        Vector3 player = world.PlayerPosition;
        IReadOnlyList<Vector3> ground = world.OpenGround;
        IReadOnlyList<Vector3> points = world.SpawnPoints;

        world.Clear();
        world.Dt = Frame;
        world.PlayerPosition = player;
        world.MoveInput = Vector2.Zero;
        world.OpenGround = ground;
        world.SpawnPoints = points;

        foreach (EnemySpawned spawned in Breathing(events))
        {
            if (!_at.ContainsKey(spawned.Id))
            {
                _at[spawned.Id] = spawned.Position;
            }

            if (onlyId != 0 && spawned.Id != onlyId)
            {
                continue;
            }

            ref EnemySense sense = ref world.AddEnemy();
            sense.Id = spawned.Id;
            sense.Position = _at[spawned.Id];
            sense.Velocity = Vector3.Zero;
            sense.HasLineOfSight = true;
        }
    }

    /// <summary>Moves the reported player 6 m out of each newly armed ring.</summary>
    private static int Dodge(WorldSnapshot world, RecordingEvents events, int seen)
    {
        IReadOnlyList<GravemawRingArmed> armed = events.Of<GravemawRingArmed>();
        for (int i = seen; i < armed.Count; i++)
        {
            Vector3 away = world.PlayerPosition - armed[i].At;
            away.Y = 0f;
            away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitX;
            world.PlayerPosition = armed[i].At + (away * (armed[i].Radius + 3f));
        }

        return armed.Count;
    }

    /// <summary>
    /// BootScope's catalog; with <paramref name="scaledHp"/>, Gravemaw's spec swapped for one whose
    /// health at stage 25 is that much and nothing else changed.
    /// </summary>
    private static ContentCatalog Catalog(float? scaledHp)
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        EnemySpec[] enemies = BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec());

        if (scaledHp.HasValue)
        {
            float hpScale = Load<ModeDefinition>(JunglePath).ToSpec().Scaling.Hp.At(Stage);
            EnemyDefinition copy = Object.Instantiate(Load<EnemyDefinition>(GravemawPath));
            try
            {
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("_maxHp").floatValue = scaledHp.Value / hpScale;
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

    private static RunSession Start(ContentCatalog catalog, RecordingEvents events, RecordingIntents intents)
    {
        var random = new Soulvail.Game.Adapters.SeededRandom(12);
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, intents, recorder, 64, 28, 64);
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

    /// <summary>A 2 m grid of open ground 3–11 m around <paramref name="centre"/>.</summary>
    private static IReadOnlyList<Vector3> Ground(Vector3 centre)
    {
        var ground = new List<Vector3>();
        for (int x = -5; x <= 5; x++)
        {
            for (int z = -5; z <= 5; z++)
            {
                var offset = new Vector3(x * 2f, 0f, z * 2f);
                float distance = offset.Length();
                if (distance >= 3f && distance <= 11f)
                {
                    ground.Add(centre + offset);
                }
            }
        }

        return ground;
    }

    /// <summary>Every enemy spawned and not since dead or despawned.</summary>
    private static List<EnemySpawned> Breathing(RecordingEvents events)
    {
        var gone = new HashSet<int>(events.Of<EnemyDied>().Select(e => e.Id)
            .Concat(events.Of<EnemyDespawned>().Select(e => e.Id)));

        return events.Of<EnemySpawned>().Where(e => !gone.Contains(e.Id)).ToList();
    }

    private static bool Died(RecordingEvents events, int id) => events.Of<EnemyDied>().Any(e => e.Id == id);

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
