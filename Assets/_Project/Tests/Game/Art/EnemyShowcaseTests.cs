using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Sandbox;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// <c>EnemyShowcase.unity</c>: every enemy body looping each of its clips side by side, the Frog's
/// six, the Rootling's five and the Pitcher's five (M7-05r), on the Jungle's ground.
/// </summary>
/// <remarks>
/// The scene is a review tool, so what can rot is its wiring: a clip renamed in <c>frog.py</c>, a
/// state renamed in a controller, a one-shot that plays once and freezes. Opened additively for the
/// fixture, as <c>RunSceneTests</c> opens <c>Run.unity</c>, and left alone if the owner has it open.
/// </remarks>
[TestFixture]
public sealed class EnemyShowcaseTests
{
    private const string ScenePath = "Assets/_Project/Scenes/EnemyShowcase.unity";
    private const string FrogPath = "Assets/_Project/Art/Enemies/Frog.fbx";
    private const string FrogControllerPath = "Assets/_Project/Animation/Controllers/AC_Frog_Showcase.controller";
    private const string PitcherPath = "Assets/_Project/Art/Enemies/Pitcher.fbx";
    private const string PitcherControllerPath = "Assets/_Project/Animation/Controllers/AC_Pitcher_Showcase.controller";

    private Scene _scene;
    private bool _openedHere;
    private ShowcaseClip[] _bodies;

    [OneTimeSetUp]
    public void OpenShowcase()
    {
        _scene = SceneManager.GetSceneByPath(ScenePath);
        _openedHere = !_scene.isLoaded;

        if (_openedHere)
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        _bodies = _scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ShowcaseClip>(true)).ToArray();
    }

    [OneTimeTearDown]
    public void CloseShowcase()
    {
        if (_openedHere && _scene.IsValid())
        {
            EditorSceneManager.CloseScene(_scene, removeScene: true);
        }
    }

    [Test]
    public void Showcase_EveryBodyStartsInAStateOfItsController()
    {
        Assert.That(_bodies, Is.Not.Empty, "EnemyShowcase.unity shows no body.");

        foreach (ShowcaseClip body in _bodies)
        {
            var controller = body.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
            Assert.That(controller, Is.Not.Null, $"'{body.name}' has no showcase controller.");
            Assert.That(
                StatesOf(controller).Select(s => s.name),
                Has.Member(body.State),
                $"'{body.name}' starts in '{body.State}', which {controller.name} does not have, so it stands in its default state.");
        }
    }

    [Test]
    public void Showcase_ShowsEveryFrogClip()
    {
        ShowsEveryClip("Frog_", FrogPath, FrogControllerPath, new[] { "Attack", "Death", "Hit", "Hop", "Idle", "Leap" });
    }

    [Test]
    public void Showcase_ShowsEveryPitcherClip()
    {
        ShowsEveryClip("Pitcher_", PitcherPath, PitcherControllerPath, new[] { "Attack", "Death", "Hit", "Idle", "Shuffle" });
    }

    [Test]
    public void Showcase_EveryStateLoops()
    {
        foreach (AnimatorController controller in _bodies.Select(b => b.GetComponent<Animator>().runtimeAnimatorController).OfType<AnimatorController>().Distinct())
        {
            foreach (AnimatorState state in StatesOf(controller))
            {
                bool loops = state.motion is AnimationClip { isLooping: true };
                bool replays = state.transitions.Any(t => t.destinationState == state && t.hasExitTime && t.exitTime >= 1f);
                Assert.That(loops || replays, Is.True, $"{controller.name}'s '{state.name}' plays once and freezes on its last frame.");
            }
        }
    }

    [Test]
    public void Showcase_AOneShotPlaysAgain()
    {
        var body = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FrogPath));
        try
        {
            var animator = body.GetComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(FrogControllerPath);
            var clip = body.AddComponent<ShowcaseClip>();
            var so = new SerializedObject(clip);
            so.FindProperty("_state").stringValue = "Hit";
            so.ApplyModifiedPropertiesWithoutUndo();

            animator.Rebind();
            typeof(ShowcaseClip).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clip, null);
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"), Is.True, "ShowcaseClip did not start the body in its state.");

            // Hit is 0.4 s and holds its last frame for 80 % of that, then blends back into itself.
            float peak = 0f;
            for (int i = 0; i < 60; i++)
            {
                animator.Update(1f / 60f);
                peak = Mathf.Max(peak, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            }

            AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(peak, Is.GreaterThan(1f), "The flinch never reached its end.");
            Assert.That(now.IsName("Hit") && now.normalizedTime < 1f, Is.True, $"After a second the flinch is at {now.normalizedTime:F2}, not playing again.");
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    /// <summary>
    /// A row names each of its model's clips once, and each state of its controller plays the clip of
    /// its own name from that model: a clip renamed in the model's script is a red row, not a body
    /// standing in its default state.
    /// </summary>
    private void ShowsEveryClip(string row, string modelPath, string controllerPath, string[] expected)
    {
        string model = System.IO.Path.GetFileName(modelPath);
        string[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview")).Select(c => c.name).OrderBy(n => n).ToArray();
        string[] shown = _bodies.Where(b => b.name.StartsWith(row)).Select(b => b.State).OrderBy(n => n).ToArray();

        Assert.That(clips, Is.EquivalentTo(expected), $"{model}'s clips changed.");
        Assert.That(shown, Is.EqualTo(clips), $"The {row} row does not show each of {model}'s clips once.");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        foreach (AnimatorState state in StatesOf(controller))
        {
            Assert.That(state.motion, Is.InstanceOf<AnimationClip>(), $"State '{state.name}' plays nothing.");
            Assert.That(state.motion.name, Is.EqualTo(state.name), $"State '{state.name}' plays '{state.motion.name}'.");
            Assert.That(AssetDatabase.GetAssetPath(state.motion), Is.EqualTo(modelPath), $"State '{state.name}' plays a clip from another model.");
        }
    }

    private static IEnumerable<AnimatorState> StatesOf(AnimatorController controller)
    {
        return controller.layers[0].stateMachine.states.Select(s => s.state);
    }
}
