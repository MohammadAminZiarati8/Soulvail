using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Soulvail.Tests.Game.Content;

[TestFixture]
public sealed class JungleArenaRotationTests
{
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string RunPath = "Assets/_Project/Scenes/Run.unity";
    private const string ArenaFolder = "Assets/_Project/Prefabs/Arenas/Jungle/";

    [Test]
    public void Jungle_NoStageRepeatsTheLast()
    {
        ModeSpec jungle = AssetDatabase.LoadAssetAtPath<ModeDefinition>(JunglePath).ToSpec();

        for (int seed = 1; seed <= 200; seed++)
        {
            ContentId previous = jungle.ArenaFor(1, seed);
            for (int stage = 2; stage <= 30; stage++)
            {
                ContentId current = jungle.ArenaFor(stage, seed);
                Assert.That(current, Is.Not.EqualTo(previous), $"Seed {seed} repeats at stage {stage}.");
                previous = current;
            }
        }
    }

    [Test]
    public void Jungle_EveryRosteredArenaIsShipped()
    {
        ModeSpec jungle = AssetDatabase.LoadAssetAtPath<ModeDefinition>(JunglePath).ToSpec();
        Scene scene = SceneManager.GetSceneByPath(RunPath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
        {
            scene = EditorSceneManager.OpenScene(RunPath, OpenSceneMode.Additive);
        }

        try
        {
            RunScope scope = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RunScope>(includeInactive: true))
                .Single();
            using var serializedScope = new SerializedObject(scope);
            SerializedProperty prefabs = serializedScope.FindProperty("_arenaPrefabs");
            var shipped = new HashSet<ContentId>();

            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var arena = (ArenaView)prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.That(arena, Is.Not.Null, $"RunScope arena slot {i} is empty.");
                shipped.Add(arena.Id);
            }

            foreach (ArenaEntry arena in jungle.Arenas)
            {
                Assert.That(shipped, Does.Contain(arena.ArenaId));
            }
        }
        finally
        {
            if (openedHere)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    [Test]
    public void Grove_IsValid()
    {
        ArenaView grove = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaFolder + "Arena_Grove.prefab").GetComponent<ArenaView>();
        ArenaView clearing = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaFolder + "Arena_Clearing.prefab").GetComponent<ArenaView>();

        Assert.That(grove.DescribeFaults(), Is.Null);
        Assert.That(grove.PlayBounds.size, Is.EqualTo(new Vector2(36f, 36f)));
        Assert.That(grove.CoverCount, Is.Zero);
        Assert.That(grove.HasGate && grove.HasEntry, Is.True);
        Assert.That(grove.PlayerStart, Is.EqualTo(clearing.PlayerStart));
        Assert.That(grove.EntryPosition, Is.EqualTo(clearing.EntryPosition));
        Assert.That(grove.GatePosition, Is.EqualTo(clearing.GatePosition));

        Assert.That(FloorTint(grove), Is.Not.EqualTo(FloorTint(clearing)));
        Assert.That(grove.transform.Find("Foliage").localRotation,
            Is.Not.EqualTo(clearing.transform.Find("Foliage").localRotation));
    }

    private static Color FloorTint(ArenaView arena) =>
        arena.transform.Find("Ground/JungleFloor").GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
}
