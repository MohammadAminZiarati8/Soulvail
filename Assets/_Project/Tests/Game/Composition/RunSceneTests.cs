using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Soulvail.Tests.Game.Composition;

/// <summary>
/// What the shipped <c>Run.unity</c> stands in its arena before the director's first wave: nothing,
/// with the enemy pool still built.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every run from M2 to M6 began with eight Husks already standing</b> — M1's chaser dummies,
/// left on <c>RunScope</c> as a ring at 8–11 m and spawned by <c>RunSession.Start</c> above
/// <c>_flow.Begin</c>: before <c>StageArrived</c>, outside the threat budget, and inside GD §7.1's
/// two seconds of arrival. M6-11's probe found them as eight stage-1 kills it could not match to a
/// spawn. Dressing an arena stays a mechanism — <c>FrameOrderTests</c> and
/// <c>PlayerProjectileTests</c> dress their own — so what is asserted is this scene's content.
/// </para>
/// <para>
/// <b>The scene is opened, not read as text.</b> <c>TableLocalizerTests</c> reads <c>m_text:</c>
/// lines because a string is what it sweeps for; the question here is what <c>RunScope</c>
/// deserializes to, and a YAML reader would be a second deserializer that could disagree with
/// Unity's. Opened additively once for the fixture and closed again — unless it was already open, in
/// which case it is the owner's, and is read as it stands and left alone.
/// </para>
/// <para>
/// <b>Each row asserts the field and then the answer it produces</b>, by invoking the private method
/// that reads it. The field alone is a premise: <c>_dummySpec</c> is only worth keeping because
/// <see cref="BootInstaller.DeviceEnemyCap"/> + 1 bodies hang on it, so that number is what is
/// checked. <c>Configure</c> is out of reach without a parent scope, and neither method needs one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RunSceneTests
{
    private const string RunScenePath = "Assets/_Project/Scenes/Run.unity";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private Scene _scene;
    private bool _openedHere;
    private RunScope _scope;

    [OneTimeSetUp]
    public void OpenRunScene()
    {
        _scene = SceneManager.GetSceneByPath(RunScenePath);
        _openedHere = !_scene.isLoaded;

        if (_openedHere)
        {
            _scene = EditorSceneManager.OpenScene(RunScenePath, OpenSceneMode.Additive);
        }

        _scope = SoleScope(_scene);
    }

    [OneTimeTearDown]
    public void CloseRunScene()
    {
        if (_openedHere && _scene.IsValid())
        {
            EditorSceneManager.CloseScene(_scene, removeScene: true);
        }
    }

    [Test]
    public void RunScene_DressesNoEnemy()
    {
        Assert.That(
            Field("_dummyPositions").arraySize,
            Is.Zero,
            "Run.unity dresses enemies into the arena again. Each one stands there at t = 0 — before "
                + "StageArrived, outside the threat budget and inside GD §7.1's two seconds of "
                + "arrival — and is killed for experience stage 1 was never tuned to pay.");

        Assert.That(
            Invoke("BuildSpawnPlan"),
            Is.SameAs(SpawnPlan.Empty),
            "The positions are empty but the plan is not, so RunSession.Start spawns something the "
                + "director did not compose.");
    }

    [Test]
    public void RunScene_StillPrewarmsTheEnemyPool()
    {
        Assert.That(
            Field("_dummySpec").objectReferenceValue,
            Is.Not.Null,
            "_dummySpec was cleared with the dummies. It is what PrewarmCount keys on, so the enemy "
                + "pool now builds nothing up front and instantiates through the whole of wave 1.");

        Assert.That(
            Invoke("PrewarmCount"),
            Is.EqualTo(BootInstaller.DeviceEnemyCap + 1),
            "The pool is sized to the device cap plus the one spare a dissolving corpse holds.");
    }

    private SerializedProperty Field(string name)
    {
        SerializedProperty property = new SerializedObject(_scope).FindProperty(name);

        Assert.That(property, Is.Not.Null, $"RunScope has no {name}, so this row tests nothing.");

        return property;
    }

    private object Invoke(string method)
    {
        MethodInfo info = typeof(RunScope).GetMethod(method, Private);

        Assert.That(info, Is.Not.Null, $"RunScope has no {method}, so this row tests nothing.");

        return info.Invoke(_scope, null);
    }

    private static RunScope SoleScope(Scene scene)
    {
        Assert.That(scene.isLoaded, Is.True, $"{RunScenePath} did not open.");

        var found = new List<RunScope>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            found.AddRange(root.GetComponentsInChildren<RunScope>(includeInactive: true));
        }

        Assert.That(found, Has.Count.EqualTo(1), $"{RunScenePath} should hold exactly one RunScope.");

        return found[0];
    }
}
