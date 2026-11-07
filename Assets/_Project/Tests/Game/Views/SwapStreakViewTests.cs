using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using Num3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

public sealed class SwapStreakViewTests
{
    private const string Root = "Assets/_Project/";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly Swapped _swap = new Swapped(1, new Num3(2f, 0f, 4f), new Num3(-1f, 0f, 8f), 0.25f);
    private GameObject _parent;
    private DomainEventHub _hub;
    private SwapStreakView _view;
    private LineRenderer _line;

    [TearDown]
    public void Dispose()
    {
        _view?.Dispose();
        _hub?.Dispose();
        Object.DestroyImmediate(_parent);
        _view = null;
        _hub = null;
        _parent = null;
    }

    [Test]
    public void Swapped_StreakJoinsBothPoints()
    {
        CreateStreak();
        _hub.Publish(_swap);

        Assert.That(_line.gameObject.activeInHierarchy, Is.True);
        Assert.That(_line.GetPosition(0), Is.EqualTo(new Vector3(2f, 0.3f, 4f)));
        Assert.That(_line.GetPosition(1), Is.EqualTo(new Vector3(-1f, 0.3f, 8f)));
        Assert.That(_line.startColor.a, Is.EqualTo(1f));
        Assert.That(_line.endColor.a, Is.EqualTo(1f));
    }

    [Test]
    public void Streak_FadesInPointThree_ThenPooled()
    {
        CreateStreak();
        _hub.Publish(_swap);

        _view.Step(0.04f);
        Assert.That(_line.startColor.a, Is.EqualTo(1f));
        _view.Step(0.11f);
        Assert.That(_line.startColor.a, Is.InRange(0.01f, 0.99f));
        Assert.That(_line.gameObject.activeInHierarchy, Is.True);

        _view.Step(0.15f);
        Assert.That(_line.startColor.a, Is.Zero);
        Assert.That(_line.endColor.a, Is.Zero);
        Assert.That(_line.gameObject.activeSelf, Is.False);

        _hub.Publish(_swap);
        Assert.That(_parent.GetComponentsInChildren<LineRenderer>(true), Is.EqualTo(new[] { _line }));
        Assert.That(_line.gameObject.activeInHierarchy, Is.True);
        Assert.That(_line.startColor.a, Is.EqualTo(1f));
    }

    [Test]
    public void Streak_IsPlayerCyan()
    {
        CreateStreak();
        _hub.Publish(_swap);

        Assert.That(_line.startColor, Is.EqualTo(Palette.Player));
        Assert.That(_line.endColor, Is.EqualTo(Palette.Player));
    }

    [Test]
    public void SkillButton_SwapCharacter_WearsSwapIcon()
    {
        WithRunScene(scene =>
        {
            SkillButton source = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<SkillButton>(true)).Single();
            SkillButton button = Object.Instantiate(source);
            _parent = button.gameObject;
            var data = new SerializedObject(button);
            var icon = (Image)data.FindProperty("_icon").objectReferenceValue;
            var swapIcon = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/UI/Icons/Icon_Swap.png");
            var rollIcon = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/UI/Icons/Icon_Roll.png");
            Assert.That(swapIcon, Is.Not.Null);
            Assert.That(rollIcon, Is.Not.Null);

            CharacterSpec beastmaster = Character("Beastmaster");
            CharacterSpec ranger = Character("Ranger");
            ModeSpec jungle = AssetDatabase.LoadAssetAtPath<ModeDefinition>(Root + "Data/Modes/Jungle.asset").ToSpec();
            EnemySpec husk = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Husk.asset").ToSpec();
            var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
                1, true, 0, jungle.Scaling, jungle.Xp, new[] { new RosterEntry(husk.Id, 1) }, jungle.Arenas);
            var catalog = new ContentCatalog(new[] { beastmaster, ranger }, new[] { husk }, new[] { mode });
            var random = new FixedRandom(3);
            _hub = new DomainEventHub();
            var session = new RunSession(catalog, random, _hub, new RecordingIntents(),
                new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _hub), 64, 28, 8);
            button.Construct(session);

            session.Start(new RunConfig(mode.Id, beastmaster.Id, random.Seed, 1, SpawnPlan.Empty, restore: null));
            typeof(SkillButton).GetMethod("Start", Private).Invoke(button, null);
            Assert.That(icon.sprite, Is.SameAs(swapIcon));

            session.End();
            session.Start(new RunConfig(mode.Id, ranger.Id, random.Seed, 1, SpawnPlan.Empty, restore: null));
            typeof(SkillButton).GetMethod("Update", Private).Invoke(button, null);
            Assert.That(icon.sprite, Is.SameAs(rollIcon));
            session.End();
        });
    }

    [Test]
    public void RunScope_MissingStreakPrefab_Throws()
    {
        WithRunScene(scene =>
        {
            RunScope source = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RunScope>(true)).Single();
            _parent = new GameObject("Undressed scope");
            _parent.SetActive(false);
            var scope = _parent.AddComponent<RunScope>();
            EditorUtility.CopySerialized(source, scope);

            var data = new SerializedObject(scope);
            data.FindProperty("_swapStreakPrefab").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();

            var exception = Assert.Throws<TargetInvocationException>(() =>
                typeof(RunScope).GetMethod("Configure", Private).Invoke(scope, new object[] { new ContainerBuilder() }));

            Assert.That(exception.InnerException, Is.TypeOf<MissingReferenceException>());
            Assert.That(exception.InnerException.Message, Does.Contain("_swapStreakPrefab"));
        });
    }

    private static void WithRunScene(Action<Scene> check)
    {
        const string scenePath = Root + "Scenes/Run.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.isLoaded;
        if (opened)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        try
        {
            check(scene);
        }
        finally
        {
            if (opened)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    private void CreateStreak()
    {
        _parent = new GameObject("Swap streak test");
        _hub = new DomainEventHub();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Vfx/SwapStreak.prefab");
        _view = new SwapStreakView(_hub, prefab.GetComponent<LineRenderer>(), _parent.transform);
        _line = _parent.GetComponentInChildren<LineRenderer>(true);
    }

    private static CharacterSpec Character(string name) =>
        AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/" + name + ".asset").ToSpec();
}
