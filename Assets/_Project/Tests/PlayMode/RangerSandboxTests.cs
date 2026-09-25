#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using Soulvail.Game.Sandbox;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// RS-02a: the Ranger sandbox played. Loads <c>RangerShowcase.unity</c> and drives it — the loop's
/// rules L1–L10, the draw guard of rule V4 against the real <c>AC_Ranger</c>, and the scene and
/// asset rules S1–S3. RS-03d: the hold the loop publishes (rule 5), and the roll kept in its
/// capsule (rule 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Editor-only, like the scene.</b> <c>RangerShowcase.unity</c> is out of
/// <c>EditorBuildSettings</c> (rule S1), so it is loaded by path through
/// <see cref="EditorSceneManager.LoadSceneAsyncInPlayMode"/>, which only the Editor has.
/// </para>
/// <para>
/// <b>Rows wait in simulated seconds, not wall seconds.</b> The loop clamps each step at
/// <see cref="SnapshotBuilder.MaxDt"/>, so on an Editor running slowly the simulation runs slower
/// than the clock. <see cref="Simulate"/> sums the same clamped step the loop does, so a row that
/// counts shots counts them on the loop's own clock.
/// </para>
/// </remarks>
public sealed class RangerSandboxTests
{
    private const string ScenePath = "Assets/_Project/Scenes/RangerShowcase.unity";
    private const string PlayerPath = "Assets/_Project/Prefabs/Player/Player_Ranger.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Ranger.controller";
    private const string DummyControllerPath = "Assets/_Project/Animation/Controllers/AC_TrainingDummy.controller";

    /// <summary>The upper-body layer's index in <c>AC_Ranger</c>.</summary>
    private const int UpperLayer = 1;

    /// <summary>The sandbox's bow: one shot a second, 10 m, 12 m to acquire, 30 m/s, aimed 0.9 m up.</summary>
    private const float Range = 10f;

    private const float ArrowSpeed = 30f;
    private const float AimHeight = 0.9f;

    /// <summary>Dummy_1 stands at (0, 0, 14). These are the spots the rows put the Ranger on.</summary>
    private static readonly Vector3 Dummy1 = new Vector3(0f, 0f, 14f);

    /// <summary>11 m from Dummy_1: inside the 12 m acquire range, outside the 10 m bow.</summary>
    private static readonly Vector3 Acquiring = new Vector3(0f, 0f, 3f);

    /// <summary>8 m from Dummy_1: inside the bow's range, and more than 12 m from every other dummy.</summary>
    private static readonly Vector3 Shooting = new Vector3(0f, 0f, 6f);

    private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
    private readonly List<TargetChanged> _targets = new List<TargetChanged>();
    private readonly List<bool> _holds = new List<bool>();
    private readonly List<ProjectileFired> _fired = new List<ProjectileFired>();
    private readonly List<ProjectileImpacted> _impacted = new List<ProjectileImpacted>();

    private readonly int _shootId = Animator.StringToHash("Shoot");
    private readonly int _aimingId = Animator.StringToHash("Aiming");
    private readonly int _moveXId = Animator.StringToHash("MoveX");
    private readonly int _moveZId = Animator.StringToHash("MoveZ");
    private readonly int _moveSpeedId = Animator.StringToHash("MoveSpeed");

    private RangerSandboxScope _scope;
    private GameObject _player;
    private PlayerView _body;
    private Animator _ranger;
    private Animator _dummy1;
    private Transform _bow;
    private int _attacked;
    private Gamepad _pad;

    private readonly List<GameObject> _created = new List<GameObject>();

    private readonly List<InputDevice> _silenced = new List<InputDevice>();

    private InputSettings.BackgroundBehavior _backgroundBehavior;

    /// <remarks>
    /// <para>
    /// <b>A row's stick has to survive focus, and nobody else's keys may reach it</b> (Traps §8).
    /// By default a change of application focus resets and disables a device that cannot run in
    /// the background, and a gamepad a test adds is one. A stick one row pushed was dropped
    /// mid-pass, and the Ranger shot on a run that had never started. <c>IgnoreFocus</c> keeps
    /// the pad, but it also lets keys typed in another window through. With the Game view's routing
    /// opened as well, a pass typed over turned the Ranger to (−0.71, −0.71), which is <b>A</b> and
    /// <b>S</b> held.
    /// </para>
    /// <para>
    /// So the rows ignore focus and silence every keyboard and pointer, and
    /// <see cref="ReleaseTheSandbox"/> gives both back. The scene's own floating stick is a virtual
    /// gamepad added after this runs, so it is not silenced.
    /// </para>
    /// </remarks>
    [UnitySetUp]
    public IEnumerator LoadTheSandbox()
    {
        _backgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

        foreach (InputDevice device in InputSystem.devices.ToArray())
        {
            if ((device is Keyboard || device is Pointer) && device.enabled)
            {
                InputSystem.DisableDevice(device);
                _silenced.Add(device);
            }
        }

        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));

        while (!load.isDone)
        {
            yield return null;
        }

        // One frame for every Start, including the loop's.
        yield return null;

        _scope = Object.FindFirstObjectByType<RangerSandboxScope>();
        _player = GameObject.Find("Player_Ranger");
        _body = _player.GetComponent<PlayerView>();
        _ranger = _player.GetComponentInChildren<Animator>();
        _dummy1 = GameObject.Find("Dummy_1").GetComponent<Animator>();
        _bow = _player.GetComponentsInChildren<Transform>(true).First(t => t.name == "Bow");

        DomainEventHub hub = _scope.Container.Resolve<DomainEventHub>();

        _attacked = 0;
        _subscriptions.Add(hub.Subscribe<TargetChanged>(evt => _targets.Add(evt)));
        _subscriptions.Add(hub.Subscribe<HoldFireChanged>(evt => _holds.Add(evt.IsHolding)));
        _subscriptions.Add(hub.Subscribe<PlayerAttacked>(_ => _attacked++));
        _subscriptions.Add(hub.Subscribe<ProjectileFired>(evt => _fired.Add(evt)));
        _subscriptions.Add(hub.Subscribe<ProjectileImpacted>(evt => _impacted.Add(evt)));
    }

    [TearDown]
    public void ReleaseTheSandbox()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _targets.Clear();
        _holds.Clear();
        _fired.Clear();
        _impacted.Clear();

        if (_pad != null)
        {
            InputSystem.RemoveDevice(_pad);
            _pad = null;
        }

        InputSystem.settings.backgroundBehavior = _backgroundBehavior;

        foreach (InputDevice device in _silenced)
        {
            if (device.added)
            {
                InputSystem.EnableDevice(device);
            }
        }

        _silenced.Clear();

        foreach (GameObject created in _created)
        {
            if (created != null)
            {
                Object.Destroy(created);
            }
        }

        _created.Clear();
    }

    // ---------------------------------------------------------------- L1, L2, L6: the target

    /// <summary>L1: every dummy stands 14 m or more from the start, so nothing is faced and nothing fires.</summary>
    [UnityTest]
    public IEnumerator Sandbox_StartsIdleWithNothingInRange()
    {
        yield return Simulate(0.5f);

        Assert.That(_targets.Where(t => t.Id >= 0), Is.Empty, "No dummy is inside 12 m, so none is faced.");
        Assert.That(_ranger.GetBool(_aimingId), Is.False);
        Assert.That(_attacked, Is.Zero);
        Assert.That(UpperState(), Is.EqualTo("Empty"));
    }

    /// <summary>L2: inside the acquire range and outside the bow's, the dummy is faced and the bow raised, and nothing is loosed.</summary>
    [UnityTest]
    public IEnumerator Sandbox_FacesADummyInReachAndRaisesTheBow()
    {
        _body.Teleport(Acquiring);

        yield return Simulate(0.6f);

        Assert.That(_targets.Last().Id, Is.EqualTo(1), "Dummy_1 is candidate 1.");
        Assert.That(_ranger.GetBool(_aimingId), Is.True);
        Assert.That(Vector3.Dot(_player.transform.forward, Toward(Dummy1)), Is.GreaterThan(0.99f));
        Assert.That(UpperState(), Is.EqualTo("Draw").Or.EqualTo("Aim"));
        Assert.That(_attacked, Is.Zero, "11 m is outside the 10 m bow.");
    }

    /// <summary>L6: out of reach again, the target is dropped and the bow comes down.</summary>
    [UnityTest]
    public IEnumerator Sandbox_LowersTheBowWhenNothingIsInReach()
    {
        _body.Teleport(Acquiring);

        yield return Simulate(0.6f);

        _body.Teleport(Vector3.zero);

        yield return Simulate(1f);

        Assert.That(_targets.Last().Id, Is.EqualTo(-1));
        Assert.That(_ranger.GetBool(_aimingId), Is.False);
        Assert.That(UpperState(), Is.EqualTo("Empty"));
    }

    // ---------------------------------------------------------------- L3, L4: the bow

    /// <summary>
    /// L3: inside the bow's range it draws at the weapon's cadence, one shot a second, and each
    /// arrow leaves from the bow, at the dummy's chest, timed as XZ distance over 30 m/s.
    /// </summary>
    [UnityTest]
    public IEnumerator Sandbox_ShootsAtTheWeaponsCadence()
    {
        _body.Teleport(Shooting);

        yield return Simulate(3.4f);

        // Draws at ~0, 1, 2 and 3 s and releases 0.72 s into each. Each swing may start up to a
        // frame late, so the window leaves room for three frames of drift and no fifth draw.
        Assert.That(_attacked, Is.EqualTo(4));
        Assert.That(_fired.Count, Is.EqualTo(3));

        foreach (ProjectileFired shot in _fired)
        {
            Assert.That(shot.SourceId, Is.Zero, "The player's shot names no enemy.");

            Vector3 target = shot.Target.ToUnity();
            Vector3 origin = shot.Origin.ToUnity();

            Assert.That(Vector3.Distance(target, Dummy1 + (Vector3.up * AimHeight)), Is.LessThan(1e-3f));
            Assert.That(
                shot.FlightTime,
                Is.EqualTo(new Vector2(target.x - origin.x, target.z - origin.z).magnitude / ArrowSpeed).Within(1e-4f));
        }

        Assert.That(Vector3.Distance(_fired.Last().Origin.ToUnity(), _bow.position), Is.LessThan(0.6f), "An arrow leaves from the bow.");
    }

    /// <summary>L4: an arrow lands when its flight is up — the census gives the body back and the dummy flinches.</summary>
    [UnityTest]
    public IEnumerator Sandbox_AnArrowLandsAndTheDummyFlinches()
    {
        ProjectileViews census = _scope.Container.Resolve<ProjectileViews>();
        bool flinched = false;
        bool inTheAir = false;

        _body.Teleport(Shooting);

        float simulated = 0f;

        while (simulated < 1.6f)
        {
            yield return null;

            simulated += Mathf.Min(Time.deltaTime, SnapshotBuilder.MaxDt);
            inTheAir |= census.Count > 0;
            flinched |= _dummy1.GetCurrentAnimatorStateInfo(0).IsName("Hit");
        }

        Assert.That(inTheAir, Is.True, "The census drew the arrow.");
        Assert.That(_impacted, Is.Not.Empty);
        Assert.That(_impacted[0].Id, Is.EqualTo(_fired[0].Id));
        Assert.That(_impacted[0].Hit, Is.True);
        Assert.That(flinched, Is.True);
    }

    /// <summary>
    /// V4's guard, on the real controller: a bow raised at a dummy out of range is at full draw
    /// when the dummy comes into range, so the first shot's release follows without a second draw.
    /// </summary>
    [UnityTest]
    public IEnumerator Sandbox_ABowAlreadyDrawnReleasesWithoutDrawingAgain()
    {
        _body.Teleport(Acquiring);

        yield return Simulate(1.8f);

        Assert.That(UpperState(), Is.EqualTo("Aim"), "The bow is held at full draw.");

        _body.Teleport(Shooting);

        var seen = new List<string>();
        float simulated = 0f;

        while (simulated < 1f && _fired.Count == 0)
        {
            yield return null;

            simulated += Mathf.Min(Time.deltaTime, SnapshotBuilder.MaxDt);
            seen.Add(UpperState());
            Assert.That(_ranger.GetBool(_shootId), Is.False, "No draw is left pending on a drawn bow.");
        }

        Assert.That(_fired, Is.Not.Empty);
        Assert.That(seen, Has.No.Member("Draw"), "Released from Aim, not drawn again first.");
    }

    // ---------------------------------------------------------------- L5: it shoots standing still

    /// <summary>
    /// L5: running, the Ranger faces where it runs, the bow is down, and the shot it was drawing is
    /// dropped. No arrow leaves on the move. RS-03d rule 5: the bow comes down on the hold a run
    /// publishes, and the target stays named.
    /// </summary>
    [UnityTest]
    public IEnumerator Sandbox_RunsWithTheBowDown()
    {
        _body.Teleport(Shooting);

        yield return Simulate(0.3f);

        Assert.That(_attacked, Is.EqualTo(1), "A shot was being drawn when the run began.");
        Assert.That(_holds, Is.Empty, "Standing still, nothing is held.");

        Push(new Vector2(1f, 0f));

        yield return Simulate(1.2f);

        Assert.That(_fired, Is.Empty, "No arrow leaves on the run.");
        Assert.That(_holds, Is.EqualTo(new[] { true }), "HoldFireChanged(true), once, as core publishes it.");
        Assert.That(_targets.Last().Id, Is.EqualTo(1), "TargetChanged still names the dummy it will shoot when it stops.");
        Assert.That(_ranger.GetBool(_aimingId), Is.False);
        Assert.That(UpperState(), Is.EqualTo("Empty"));
        Assert.That(Vector3.Dot(_player.transform.forward, Vector3.right), Is.GreaterThan(0.99f), "Facing where it runs.");
        Assert.That(_ranger.GetFloat(_moveZId), Is.GreaterThan(0.9f), "The forward run, not a strafe.");
        Assert.That(Mathf.Abs(_ranger.GetFloat(_moveXId)), Is.LessThan(0.2f));
        Assert.That(_ranger.GetFloat(_moveSpeedId), Is.GreaterThan(1f), "3 m/s is faster than the run's feet at this scale.");
    }

    /// <summary>L5: let go of the stick, and the Ranger turns to its target and shoots.</summary>
    [UnityTest]
    public IEnumerator Sandbox_StopsAndShoots()
    {
        _body.Teleport(Shooting);
        Push(new Vector2(1f, 0f));

        yield return Simulate(0.5f);

        Assert.That(_fired, Is.Empty);

        Push(Vector2.zero);

        yield return Simulate(1.2f);

        Assert.That(_fired, Is.Not.Empty, "Stopped, it shoots.");
        Assert.That(_targets.Last().Id, Is.EqualTo(1));
        Assert.That(Vector3.Dot(_player.transform.forward, Toward(Dummy1)), Is.GreaterThan(0.99f));
    }

    // ---------------------------------------------------------------- RS-03d rule 4: the roll

    /// <summary>
    /// RS-03d rule 4: a roll never moves the body. Each of the four directions is rolled on the
    /// real <c>AC_Ranger</c>, its full length and the crossfade out, and on every frame the hips
    /// stay within 0.3 m of the <see cref="PlayerView"/> on XZ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The roll is published into the scope's hub</b>, as core would publish it: the sandbox has
    /// no dash, and nothing moves the body, so any distance the hips put between themselves and the
    /// player is the clip's own translation reaching the mesh. KayKit's four <c>Dodge_*</c> carry it
    /// on <c>root</c>, 0.25 to 0.65 m in model units, and forward is the shortest, so a row that
    /// rolled forward alone would pass on the imported clips.
    /// </para>
    /// <para>
    /// Timed on <c>Time.deltaTime</c>, not the loop's clamped step, because the Animator is what is
    /// being watched and it runs on the frame's own delta. A roll is 0.3 s of dash and 0.05 s of
    /// i-frames before <see cref="ChargeEnded"/>, as the Ranger's roll is authored.
    /// </para>
    /// </remarks>
    [UnityTest]
    public IEnumerator Roll_TheBodyStaysInItsCapsule()
    {
        DomainEventHub hub = _scope.Container.Resolve<DomainEventHub>();
        Transform hips = _player.GetComponentsInChildren<Transform>(true).First(t => t.name == "hips");
        var directions = new[]
        {
            new System.Numerics.Vector2(0f, 1f),
            new System.Numerics.Vector2(0f, -1f),
            new System.Numerics.Vector2(-1f, 0f),
            new System.Numerics.Vector2(1f, 0f),
        };

        foreach (System.Numerics.Vector2 direction in directions)
        {
            float farthest = 0f;
            bool rolled = false;

            hub.Publish(new ChargeStarted(direction));

            for (float elapsed = 0f; elapsed < 0.35f; elapsed += Time.deltaTime)
            {
                yield return null;

                farthest = Mathf.Max(farthest, FromThePlayer(hips));
                rolled |= _ranger.GetCurrentAnimatorStateInfo(0).IsName("Dodge");
            }

            hub.Publish(new ChargeEnded());

            for (float elapsed = 0f; elapsed < 0.4f; elapsed += Time.deltaTime)
            {
                yield return null;

                farthest = Mathf.Max(farthest, FromThePlayer(hips));
            }

            Assert.That(rolled, Is.True, $"The roll ({direction.X}, {direction.Y}) played the Dodge state.");
            Assert.That(farthest, Is.LessThan(0.3f), $"The hips left the capsule on the roll ({direction.X}, {direction.Y}).");
        }
    }

    // ---------------------------------------------------------------- S1–S3: the scene and the assets

    /// <summary>S1: the game's camera, at the Run scene's numbers, following the Ranger.</summary>
    [UnityTest]
    public IEnumerator Scene_FollowsTheRangerWithTheRunCamera()
    {
        FollowCamera follow = Camera.main.GetComponent<FollowCamera>();
        var so = new SerializedObject(follow);

        Assert.That(so.FindProperty("_target").objectReferenceValue, Is.EqualTo(_player.transform));
        Assert.That(so.FindProperty("_pitchDeg").floatValue, Is.EqualTo(57f));
        Assert.That(so.FindProperty("_yawDeg").floatValue, Is.Zero, "Yaw 0 is what makes the stick camera-relative.");
        Assert.That(so.FindProperty("_distance").floatValue, Is.EqualTo(16f));
        Assert.That(so.FindProperty("_smoothTime").floatValue, Is.EqualTo(0.12f));

        yield break;
    }

    /// <summary>S1: the game's floating stick on the left 45 %, fed through the Input System's UI module.</summary>
    [UnityTest]
    public IEnumerator Scene_HasTheGamesFloatingStick()
    {
        FloatingStick stick = Object.FindFirstObjectByType<FloatingStick>();
        var rect = (RectTransform)stick.transform;

        Assert.That(rect.anchorMin.x, Is.Zero);
        Assert.That(rect.anchorMax.x, Is.EqualTo(0.45f));
        Assert.That(new SerializedObject(stick).FindProperty("_controlPath").stringValue, Is.EqualTo("<Gamepad>/leftStick"));
        Assert.That(Object.FindFirstObjectByType<InputSystemUIInputModule>(), Is.Not.Null);

        yield break;
    }

    /// <summary>S1: a sandbox, out of the build.</summary>
    [Test]
    public void Scene_IsOutOfTheBuild()
    {
        Assert.That(EditorBuildSettings.scenes.Select(s => s.path), Has.No.Member(ScenePath));
    }

    /// <summary>
    /// S2: the bow in the left handslot at the owner's angle, and AC_Ranger with no root motion (AR §18).
    /// </summary>
    /// <remarks>
    /// The bow sat at identity from RS-02a until the owner's review of RS-03c on 2026-09-25 turned it
    /// X 45 and Z 180, which is how it reads in the hand.
    /// </remarks>
    [Test]
    public void Ranger_HoldsItsBowInTheLeftHandslot()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        Transform bow = prefab.GetComponentsInChildren<Transform>(true).First(t => t.name == "Bow");
        Animator animator = prefab.GetComponentInChildren<Animator>();
        // On the nested body since RS-02b, not on the prefab's root.
        var view = new SerializedObject(prefab.GetComponentInChildren<RangerAnimatorView>());

        Assert.That(bow.parent.name, Is.EqualTo("handslot.l"), "Ranged_Bow_Draw pulls with the right hand.");
        Assert.That(bow.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(
            Quaternion.Angle(bow.localRotation, Quaternion.Euler(45f, 0f, 180f)),
            Is.LessThan(0.01f),
            "the owner's angle in the hand: X 45, Z 180.");
        Assert.That(animator.runtimeAnimatorController.name, Is.EqualTo("AC_Ranger"));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(view.FindProperty("_animator").objectReferenceValue, Is.EqualTo(animator));
        Assert.That(view.FindProperty("_bow").objectReferenceValue, Is.EqualTo(bow.GetComponentInChildren<SkinnedMeshRenderer>()));
    }

    /// <summary>S3: the arms override the legs from the spine up, and never the hips or the legs.</summary>
    [Test]
    public void Ranger_TheArmsAreALayerMaskedToTheSpineAndUp()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimatorControllerLayer upper = controller.layers[UpperLayer];

        Assert.That(upper.name, Is.EqualTo(RangerAnimatorView.UpperBodyLayer));
        Assert.That(upper.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
        Assert.That(upper.defaultWeight, Is.EqualTo(1f));

        AvatarMask mask = upper.avatarMask;

        Assert.That(mask, Is.Not.Null);

        for (int i = 0; i < mask.transformCount; i++)
        {
            string path = mask.GetTransformPath(i);

            Assert.That(mask.GetTransformActive(i), Is.EqualTo(path.Contains("/spine")), path);
        }

        Assert.That(
            Enumerable.Range(0, mask.transformCount).Count(i => mask.GetTransformActive(i)),
            Is.EqualTo(13),
            "spine, chest, head, and five bones down each arm.");
    }

    /// <summary>S3: the legs are a directional blend — idle, forward run, reversed run, both strafes — on MoveX and MoveZ.</summary>
    [Test]
    public void Ranger_TheLegsAreADirectionalBlend()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        // By name: the base layer holds the roll beside it since RS-03d.
        ChildAnimatorState locomotion = controller.layers[0].stateMachine.states.Single(s => s.state.name == "Locomotion");
        var tree = (BlendTree)locomotion.state.motion;

        Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.FreeformDirectional2D));
        Assert.That(tree.blendParameter, Is.EqualTo("MoveX"));
        Assert.That(tree.blendParameterY, Is.EqualTo("MoveZ"));
        Assert.That(locomotion.state.speedParameterActive, Is.True);
        Assert.That(locomotion.state.speedParameter, Is.EqualTo("MoveSpeed"));

        var byPosition = tree.children.ToDictionary(c => c.position);

        Assert.That(byPosition[Vector2.zero].motion.name, Is.EqualTo("Ranged_Bow_Idle"));
        Assert.That(byPosition[new Vector2(0f, 1f)].motion.name, Is.EqualTo("Running_HoldingBow"));
        Assert.That(byPosition[new Vector2(0f, -1f)].motion.name, Is.EqualTo("Running_HoldingBow"));
        Assert.That(byPosition[new Vector2(0f, -1f)].timeScale, Is.EqualTo(-1f), "Walking_Backwards' feet travel at 0.46 m/s here.");
        Assert.That(byPosition[new Vector2(-1f, 0f)].motion.name, Is.EqualTo("Running_Strafe_Left"));
        Assert.That(byPosition[new Vector2(1f, 0f)].motion.name, Is.EqualTo("Running_Strafe_Right"));
    }

    // ---------------------------------------------------------------- L7–L9: the loop on its own

    [Test]
    public void Loop_NullArgumentsAreRefused()
    {
        LoopParts parts = Parts();

        Assert.That(() => parts.Build(input: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(hub: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(player: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(arrows: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(movement: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(targeting: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(weapon: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(muzzle: null), Throws.ArgumentNullException);
        Assert.That(() => parts.Build(dummies: null), Throws.ArgumentNullException);
    }

    [Test]
    public void Loop_ABowThatIsNotAProjectileWeaponIsRefused()
    {
        LoopParts parts = Parts();
        var censer = new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f);

        Assert.That(() => parts.Build(weapon: censer), Throws.ArgumentException);
    }

    [TestCase(-0.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Loop_ANegativeOrNonFiniteAimHeightIsRefused(float aimHeight)
    {
        LoopParts parts = Parts();

        Assert.That(() => parts.Build(aimHeight: aimHeight), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Loop_StartReadsTheStickAndDisposeStops()
    {
        LoopParts parts = Parts();
        RangerSandboxLoop loop = parts.Build();

        loop.Start();

        Assert.That(parts.Input.IsEnabled, Is.True);

        loop.Dispose();

        Assert.That(parts.Input.IsEnabled, Is.False);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Loop_ADegenerateStepDoesNothing(float dt)
    {
        LoopParts parts = Parts();
        RangerSandboxLoop loop = parts.Build();
        int changes = 0;

        using IDisposable subscription = parts.Hub.Subscribe<TargetChanged>(_ => changes++);

        loop.Step(dt);

        Assert.That(changes, Is.Zero, "A dummy 5 m away would have been faced on any real step.");
        Assert.That(loop.CurrentTargetId, Is.EqualTo(-1));
    }

    /// <summary>L7: a hitch is a 50 ms step, so two ten-second frames are a tenth of a second of bow.</summary>
    [Test]
    public void Loop_AHitchIsClampedToTheSnapshotStep()
    {
        LoopParts parts = Parts();
        RangerSandboxLoop loop = parts.Build();
        int fired = 0;

        using IDisposable subscription = parts.Hub.Subscribe<ProjectileFired>(_ => fired++);

        loop.Step(10f);
        loop.Step(10f);

        Assert.That(loop.CurrentTargetId, Is.EqualTo(1));
        Assert.That(fired, Is.Zero, "0.1 s in, the arrow is 0.62 s from leaving.");

        for (int i = 0; i < 14; i++)
        {
            loop.Step(SnapshotBuilder.MaxDt);
        }

        Assert.That(fired, Is.EqualTo(1));
    }

    /// <summary>
    /// L5 on the loop alone: pushed, the dummy is chosen, fire is held and nothing fires; let go,
    /// and it shoots. The target is named while it runs, since RS-03d rule 5.
    /// </summary>
    [Test]
    public void Loop_ShootsOnlyStandingStill()
    {
        LoopParts parts = Parts();
        RangerSandboxLoop loop = parts.Build();
        var faced = new List<int>();
        var holds = new List<bool>();
        int fired = 0;

        using IDisposable facing = parts.Hub.Subscribe<TargetChanged>(evt => faced.Add(evt.Id));
        using IDisposable holding = parts.Hub.Subscribe<HoldFireChanged>(evt => holds.Add(evt.IsHolding));
        using IDisposable firing = parts.Hub.Subscribe<ProjectileFired>(_ => fired++);

        loop.Start();
        InputSystem.Update();
        PushNow(new Vector2(1f, 0f));

        for (int i = 0; i < 30; i++)
        {
            loop.Step(SnapshotBuilder.MaxDt);
        }

        Assert.That(fired, Is.Zero, "No arrow on the run.");
        Assert.That(holds, Is.EqualTo(new[] { true }), "Fire held on the run.");
        Assert.That(faced, Has.Member(1), "Chosen, and named while it runs.");
        Assert.That(loop.CurrentTargetId, Is.EqualTo(1));

        PushNow(Vector2.zero);

        for (int i = 0; i < 30; i++)
        {
            loop.Step(SnapshotBuilder.MaxDt);
        }

        Assert.That(faced.Last(), Is.EqualTo(1));
        Assert.That(holds, Is.EqualTo(new[] { true, false }), "Stopped, the hold ends.");
        Assert.That(fired, Is.EqualTo(1), "Stopped, the first arrow leaves a draw later.");
    }

    /// <summary>The running shot, switched on: the Ranger faces its target and shoots on the move.</summary>
    [Test]
    public void Loop_TheRunningShotShootsOnTheMove()
    {
        LoopParts parts = Parts();
        RangerSandboxLoop loop = parts.Build(shootWhileMoving: true);
        var faced = new List<int>();
        int fired = 0;

        using IDisposable facing = parts.Hub.Subscribe<TargetChanged>(evt => faced.Add(evt.Id));
        using IDisposable firing = parts.Hub.Subscribe<ProjectileFired>(_ => fired++);

        loop.Start();
        InputSystem.Update();
        PushNow(new Vector2(1f, 0f));

        for (int i = 0; i < 30; i++)
        {
            loop.Step(SnapshotBuilder.MaxDt);
        }

        Vector3 toward = parts.Dummies[0].transform.position - parts.Player.transform.position;

        toward.y = 0f;

        Assert.That(faced.Last(), Is.EqualTo(1));
        Assert.That(fired, Is.EqualTo(1));
        Assert.That(Vector3.Dot(parts.Player.transform.forward, toward.normalized), Is.GreaterThan(0.99f), "It faces the dummy, not the way it runs.");
    }

    /// <summary>L9: disposed with an arrow in the air, the arrow is landed as a miss so its body goes back to the pool.</summary>
    [Test]
    public void Loop_DisposeLandsEveryArrowInTheAir()
    {
        LoopParts parts = Parts();
        RangerSandboxLoop loop = parts.Build();
        var landed = new List<ProjectileImpacted>();

        using IDisposable subscription = parts.Hub.Subscribe<ProjectileImpacted>(evt => landed.Add(evt));

        for (int i = 0; i < 40 && loop.ArrowsInFlight == 0; i++)
        {
            loop.Step(SnapshotBuilder.MaxDt);
        }

        Assert.That(loop.ArrowsInFlight, Is.EqualTo(1), "One arrow loosed within two seconds.");

        loop.Dispose();

        Assert.That(landed, Has.Count.EqualTo(1));
        Assert.That(landed[0].Hit, Is.False);
        Assert.That(loop.ArrowsInFlight, Is.Zero);
        Assert.That(parts.Arrows.Count, Is.Zero);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Yields frames until <paramref name="seconds"/> of clamped simulation have passed.</summary>
    private static IEnumerator Simulate(float seconds)
    {
        float simulated = 0f;

        while (simulated < seconds)
        {
            yield return null;

            simulated += Mathf.Min(Time.deltaTime, SnapshotBuilder.MaxDt);
        }
    }

    /// <summary>Pushes the stick of a gamepad this row adds, from the next input update on.</summary>
    private void Push(Vector2 stick)
    {
        _pad ??= InputSystem.AddDevice<Gamepad>();

        InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = stick });
    }

    /// <summary><see cref="Push"/>, applied now, for a row that steps a loop by hand.</summary>
    private void PushNow(Vector2 stick)
    {
        Push(stick);
        InputSystem.Update();
    }

    private string UpperState()
    {
        AnimatorStateInfo state = _ranger.GetCurrentAnimatorStateInfo(UpperLayer);

        foreach (string name in new[] { "Empty", "Draw", "Aim", "Release" })
        {
            if (state.IsName(name))
            {
                return name;
            }
        }

        return "?";
    }

    /// <summary>How far <paramref name="bone"/> stands from the <see cref="PlayerView"/>, on XZ.</summary>
    private float FromThePlayer(Transform bone)
    {
        Vector3 offset = bone.position - _body.transform.position;

        offset.y = 0f;

        return offset.magnitude;
    }

    private Vector3 Toward(Vector3 point)
    {
        Vector3 toward = point - _player.transform.position;

        toward.y = 0f;

        return toward.normalized;
    }

    /// <summary>A loop's parts, built fresh in the loaded scene: a body at the origin and one dummy 5 m ahead.</summary>
    private LoopParts Parts()
    {
        var body = new GameObject("TestRanger");
        var dummy = new GameObject("TestDummy");
        var arrowTemplate = new GameObject("TestArrow");

        _created.Add(body);
        _created.Add(dummy);
        _created.Add(arrowTemplate);

        body.transform.position = new Vector3(100f, 0f, 100f);
        dummy.transform.position = new Vector3(100f, 0f, 105f);

        Animator dummyAnimator = dummy.AddComponent<Animator>();
        dummyAnimator.runtimeAnimatorController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DummyControllerPath);

        var hub = new DomainEventHub();
        var input = new InputAdapter();

        var parts = new LoopParts
        {
            Hub = hub,
            Input = input,
            Player = body.AddComponent<PlayerView>(),
            Arrows = new ProjectileViews(
                new ContainerBuilder().Build(),
                arrowTemplate.AddComponent<ProjectileView>(),
                null,
                hub),
            Movement = new MovementSpec(3f, 0.06f, 0.08f, 720f),
            Targeting = new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            Weapon = new WeaponSpec(WeaponKind.Projectile, 1f, 1f, Range, 360f, 0.72f, ArrowSpeed, 0.3f),
            Muzzle = body.transform,
            Dummies = new[] { dummyAnimator },
        };

        _subscriptions.Add(parts.Arrows);
        _subscriptions.Add(input);
        _subscriptions.Add(hub);

        return parts;
    }

    private sealed class LoopParts
    {
        public DomainEventHub Hub;
        public InputAdapter Input;
        public PlayerView Player;
        public ProjectileViews Arrows;
        public MovementSpec Movement;
        public TargetingSpec Targeting;
        public WeaponSpec Weapon;
        public Transform Muzzle;
        public Animator[] Dummies;

        public RangerSandboxLoop Build(
            Optional<InputAdapter> input = default,
            Optional<DomainEventHub> hub = default,
            Optional<PlayerView> player = default,
            Optional<ProjectileViews> arrows = default,
            Optional<MovementSpec> movement = default,
            Optional<TargetingSpec> targeting = default,
            Optional<WeaponSpec> weapon = default,
            Optional<Transform> muzzle = default,
            Optional<Animator[]> dummies = default,
            float aimHeight = AimHeight,
            bool shootWhileMoving = false)
        {
            return new RangerSandboxLoop(
                input.Or(Input),
                hub.Or(Hub),
                player.Or(Player),
                arrows.Or(Arrows),
                movement.Or(Movement),
                targeting.Or(Targeting),
                weapon.Or(Weapon),
                muzzle.Or(Muzzle),
                dummies.Or(Dummies),
                aimHeight,
                shootWhileMoving);
        }
    }

    /// <summary>
    /// A parameter that can be left out or passed as null, which a plain optional parameter cannot
    /// tell apart. The implicit conversion is what lets a row write <c>Build(hub: null)</c>.
    /// </summary>
    private readonly struct Optional<T>
        where T : class
    {
        private readonly T _value;
        private readonly bool _given;

        private Optional(T value)
        {
            _value = value;
            _given = true;
        }

        public static implicit operator Optional<T>(T value) => new Optional<T>(value);

        public T Or(T fallback) => _given ? _value : fallback;
    }
}
#endif
