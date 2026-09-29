using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The Jungle as a place a run is played in (M7-05i): its own Husk, Descent's numbers, a small room
/// for its first stages, and the first mode a new run starts. Its Husk is the Frog since M7-05m.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Jungle's Husk is its own archetype with the Husk's numbers</b>, not the Husk in a skin:
/// identity is a <see cref="ContentId"/>, a look is keyed by it, and a place's enemies are its mode's
/// roster, so the Jungle's Husk is one roster row naming a different id. <see cref="JunglesHusk_PlaysTheHusksRole"/>
/// pins the numbers equal; they part when someone edits that row, which is the moment to decide they
/// should. The Rootling, which the Frog replaced, keeps its row: it is authored and could be rostered
/// again by one line.
/// </para>
/// <para>
/// <b>The Jungle is Descent with its own content.</b> <see cref="Jungle_IsDescentsNumbers"/> compares
/// every authored block but the four that make it the Jungle — id, name, roster and rooms — field by
/// field, so a retune of Descent that forgets the Jungle is a red row rather than two places drifting.
/// </para>
/// </remarks>
[TestFixture]
public sealed class JungleTests
{
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string RootlingPath = "Assets/_Project/Data/Enemies/Rootling.asset";
    private const string FrogPath = "Assets/_Project/Data/Enemies/Frog.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string RootlingBodyPath = "Assets/_Project/Prefabs/Enemies/Rootling.prefab";
    private const string FrogBodyPath = "Assets/_Project/Prefabs/Enemies/Frog.prefab";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string ArenaFolder = "Assets/_Project/Prefabs/Arenas";

    /// <summary>
    /// The widest floor a place may open in: GD §7.2's 36 m square is the full room, and the owner's
    /// direction of 2026-09-27 is a smaller one for the first stages. 24 m holds the spawn clearance
    /// (M7-05e); 26 allows a room a little larger than the Hollow.
    /// </summary>
    private const float SmallRoomFloor = 26f;

    private static readonly string[] SharedBlocks =
    {
        "_startingStage", "_isEndless", "_finalStage", "_scaling", "_xp", "_overflow", "_essence",
        "_sanctum", "_bossRoster", "_ordeals",
    };

    [TestCase(FrogPath, "enemy.frog")]
    [TestCase(RootlingPath, "enemy.rootling")]
    public void JunglesHusk_PlaysTheHusksRole(string path, string id)
    {
        EnemySpec ours = Load<EnemyDefinition>(path).ToSpec();
        EnemySpec husk = Load<EnemyDefinition>(HuskPath).ToSpec();

        Assert.That(ours.Id.Value, Is.EqualTo(id));
        Assert.That(ours.NameKey.Key, Is.EqualTo(id + ".name"));

        Assert.That(ours.Behaviour, Is.EqualTo(husk.Behaviour), "It beelines and swings (GD §8.1).");
        Assert.That(ours.MaxHp, Is.EqualTo(husk.MaxHp));
        Assert.That(ours.MoveSpeed, Is.EqualTo(husk.MoveSpeed));
        Assert.That(ours.TargetPriority, Is.EqualTo(husk.TargetPriority));
        Assert.That(ours.ThreatCost, Is.EqualTo(husk.ThreatCost));
        Assert.That(ours.XpValue, Is.EqualTo(husk.XpValue));
        Assert.That(ours.IsElite, Is.EqualTo(husk.IsElite));
        Assert.That(ours.ContactDamage, Is.EqualTo(husk.ContactDamage));
        Assert.That(ours.Reach, Is.EqualTo(husk.Reach));
        Assert.That(ours.WindupTime, Is.EqualTo(husk.WindupTime));
        Assert.That(ours.RecoverTime, Is.EqualTo(husk.RecoverTime));
        Assert.That(ours.AggroRange, Is.EqualTo(husk.AggroRange));
        Assert.That(ours.Projectile, Is.Null);
        Assert.That(ours.Explosion, Is.Null);
    }

    [TestCase(FrogPath, FrogBodyPath)]
    [TestCase(RootlingPath, RootlingBodyPath)]
    public void JunglesHusk_WearsItsOwnBody(string path, string bodyPath)
    {
        EnemyLook look = Load<EnemyDefinition>(path).ToLook();

        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body.gameObject, Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath)));
        Assert.That(look.Tint, Is.EqualTo(Color.white), "White, so the atlas reads as it was painted.");
        Assert.That(look.BodyScale, Is.EqualTo(1f));
    }

    [Test]
    public void Jungle_IsDescentsNumbers()
    {
        var jungle = new SerializedObject(Load<ModeDefinition>(JunglePath));
        var descent = new SerializedObject(Load<ModeDefinition>(DescentPath));

        foreach (string block in SharedBlocks)
        {
            Assert.That(Flatten(jungle.FindProperty(block)), Is.EqualTo(Flatten(descent.FindProperty(block))),
                $"The Jungle's {block} is not Descent's.");
        }
    }

    [Test]
    public void Jungle_RostersItsOwnHusk()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.Id.Value, Is.EqualTo("mode.jungle"));
        Assert.That(jungle.Roster.Select(r => (r.SpecId.Value, r.IntroducedAtStage)), Is.EqualTo(new[]
        {
            ("enemy.frog", 1),
            ("enemy.spitter", 2),
            ("enemy.bloater", 4),
        }), "The Frog where Descent has the Husk (the owner, 2026-09-29, in the Rootling's place); the Spitter and the Bloater keep their capsules.");
    }

    [Test]
    public void Jungle_OpensInASmallRoom()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.Arenas.Select(a => (a.ArenaId.Value, a.FirstStage, a.LastStage)), Is.EqualTo(new[]
        {
            ("arena.jungle.hollow", 1, 4),
            ("arena.jungle.clearing", 5, ArenaEntry.NoLastStage),
        }));

        Dictionary<ContentId, ArenaView> shipped = ShippedArenas();

        foreach (ArenaEntry entry in jungle.Arenas.Where(a => a.IsOpenAt(1)))
        {
            BoxCollider floor = shipped[entry.ArenaId].transform.Find("Collision/Floor").GetComponent<BoxCollider>();
            Vector3 size = Vector3.Scale(floor.size, floor.transform.lossyScale);

            Assert.That(Mathf.Max(size.x, size.z), Is.LessThanOrEqualTo(SmallRoomFloor),
                $"{entry.ArenaId} opens the Jungle on a {size.x} x {size.z} m floor.");
        }
    }

    [Test]
    public void Jungle_EveryArenaItRostersIsShipped()
    {
        Dictionary<ContentId, ArenaView> shipped = ShippedArenas();

        foreach (ArenaEntry entry in Load<ModeDefinition>(JunglePath).ToSpec().Arenas)
        {
            Assert.That(shipped.ContainsKey(entry.ArenaId), Is.True, $"No arena prefab carries {entry.ArenaId}.");
        }
    }

    [Test]
    public void Boot_TheJungleIsTheFirstPlace()
    {
        // A run plays the first mode the catalog holds (ClassSelectPresenter, RunTicker), and there is
        // no place-select screen yet: the Jungle first is what makes a new run a Jungle run, and
        // Descent second is what keeps a saved Descent run resuming.
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath).GetComponentInChildren<BootScope>(true);
        var serialized = new SerializedObject(boot);

        SerializedProperty modes = serialized.FindProperty("_modes");
        Assert.That(modes.arraySize, Is.EqualTo(2));
        Assert.That(modes.GetArrayElementAtIndex(0).objectReferenceValue, Is.EqualTo(Load<ModeDefinition>(JunglePath)));
        Assert.That(modes.GetArrayElementAtIndex(1).objectReferenceValue, Is.EqualTo(Load<ModeDefinition>(DescentPath)));

        SerializedProperty enemies = serialized.FindProperty("_enemies");
        var listed = Enumerable.Range(0, enemies.arraySize).Select(i => enemies.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(FrogPath)));

        // Every bodied archetype the catalog lists is prewarmed to a pool of DeviceEnemyCap + 1 at
        // every Run load, rostered or not (M7-05g). The Rootling is rostered by no mode.
        Assert.That(listed, Has.No.Member(Load<EnemyDefinition>(RootlingPath)),
            "The Rootling is listed but no mode rosters it, so a run builds 29 of its bodies for nothing.");
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }

    /// <summary>
    /// Every value under <paramref name="property"/>, as text, in serialised order — or the property's
    /// own value when it has nothing under it.
    /// </summary>
    private static List<string> Flatten(SerializedProperty property)
    {
        var values = new List<string>();
        SerializedProperty cursor = property.Copy();
        SerializedProperty end = property.GetEndProperty();

        while (cursor.Next(true) && !SerializedProperty.EqualContents(cursor, end))
        {
            string value = Value(cursor);

            if (value != null)
            {
                values.Add($"{cursor.propertyPath}={value}");
            }
        }

        if (values.Count == 0)
        {
            values.Add($"{property.propertyPath}={Value(property)}");
        }

        return values;
    }

    /// <summary>A leaf's value as text, or null for a property that only holds others.</summary>
    private static string Value(SerializedProperty property) => property.propertyType switch
    {
        SerializedPropertyType.Float => property.floatValue.ToString("R"),
        SerializedPropertyType.Integer => property.longValue.ToString(),
        SerializedPropertyType.Boolean => property.boolValue.ToString(),
        SerializedPropertyType.String => property.stringValue,
        SerializedPropertyType.ObjectReference => AssetDatabase.GetAssetPath(property.objectReferenceValue),
        SerializedPropertyType.ArraySize => property.intValue.ToString(),
        _ => null,
    };

    private static Dictionary<ContentId, ArenaView> ShippedArenas()
    {
        var shipped = new Dictionary<ContentId, ArenaView>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ArenaFolder }))
        {
            var arena = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponent<ArenaView>();

            if (arena != null)
            {
                shipped[arena.Id] = arena;
            }
        }

        return shipped;
    }
}
