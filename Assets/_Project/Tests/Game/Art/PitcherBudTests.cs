using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// JG-13b rules 1–2: a bud idles in a clip of its own, <c>Guard</c>, a lean toward its front with the
/// leaves raised and a slow pulse, so a bud turned to its mother reads as guarding her.
/// </summary>
[TestFixture]
public sealed class PitcherBudTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Pitcher.fbx";
    private const string PitcherControllerPath = "Assets/_Project/Animation/Controllers/AC_Pitcher.controller";
    private const string BudControllerPath = "Assets/_Project/Animation/Controllers/AC_PitcherBud.overrideController";
    private const string PitcherPrefabPath = "Assets/_Project/Prefabs/Enemies/Pitcher.prefab";
    private const string BudPrefabPath = "Assets/_Project/Prefabs/Enemies/Pitcher_Bud.prefab";
    private const string BudPath = "Assets/_Project/Data/Enemies/PitcherBud.asset";

    private const int GuardFrames = 60;
    private const float MinLean = 0.1f;
    private const float MinPulse = 1.06f;
    private const float MaxPulse = 1.10f;
    private const float Tolerance = 1e-3f;

    private static readonly int[] LeanFrames = { 15, 45 };

    private static readonly (string Name, int Frames)[] TheFive =
    {
        ("Attack", 36), ("Death", 30), ("Hit", 12), ("Idle", 48), ("Shuffle", 12),
    };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // Rule 1: 60 looping frames, back where it began, leaning in and swelling twice, the root still.
    [Test]
    public void PitcherModel_GuardLeansAndPulses()
    {
        AnimationClip guard = Clips()["Guard"];

        Assert.That(Mathf.RoundToInt(guard.length * guard.frameRate), Is.EqualTo(GuardFrames));
        Assert.That(guard.isLooping, Is.True);

        GameObject model = Instance();
        Transform[] bones = model.GetComponentsInChildren<Transform>(true);
        Transform root = bones.Single(t => t.name == "root");
        Transform jug = bones.Single(t => t.name == "jug");
        Transform mouth = bones.Single(t => t.name == "mouth");
        Vector3 rootRest = root.localPosition;
        Quaternion rootTurn = root.localRotation;

        guard.SampleAnimation(model, 0f);
        Vector3 mouthAtRest = mouth.position;
        var first = bones.Select(t => (t.localPosition, t.localRotation, t.localScale)).ToArray();

        guard.SampleAnimation(model, GuardFrames / guard.frameRate);
        for (int i = 0; i < bones.Length; i++)
        {
            Assert.That((bones[i].localPosition - first[i].localPosition).magnitude, Is.LessThan(Tolerance), bones[i].name);
            Assert.That(Quaternion.Angle(bones[i].localRotation, first[i].localRotation), Is.LessThan(0.1f), bones[i].name);
            Assert.That((bones[i].localScale - first[i].localScale).magnitude, Is.LessThan(Tolerance), bones[i].name);
        }

        foreach (int frame in LeanFrames)
        {
            guard.SampleAnimation(model, frame / guard.frameRate);
            float lean = Vector3.Dot(mouth.position - mouthAtRest, model.transform.forward);

            Assert.That(lean, Is.GreaterThanOrEqualTo(MinLean), $"the mouth leans {lean:F3} m in at frame {frame}");
        }

        float peak = 0f;
        for (int frame = 0; frame <= GuardFrames; frame++)
        {
            guard.SampleAnimation(model, frame / guard.frameRate);
            peak = Mathf.Max(peak, jug.localScale.x);

            Assert.That((root.localPosition - rootRest).magnitude, Is.LessThan(1e-4f), $"root moves at frame {frame}");
            Assert.That(Quaternion.Angle(root.localRotation, rootTurn), Is.LessThan(0.01f), $"root turns at frame {frame}");
        }

        Assert.That(peak, Is.InRange(MinPulse, MaxPulse), "the jug's pulse");
    }

    // Rule 1: the clips a Pitcher already plays keep their names and lengths.
    [Test]
    public void PitcherModel_KeepsItsFiveClips()
    {
        Dictionary<string, AnimationClip> clips = Clips();

        foreach ((string name, int frames) in TheFive)
        {
            Assert.That(clips.ContainsKey(name), Is.True, name);
            Assert.That(Mathf.RoundToInt(clips[name].length * clips[name].frameRate), Is.EqualTo(frames), name);
        }
    }

    // Rule 2: the bud's controller is the Pitcher's with only its idle swapped.
    [Test]
    public void PitcherBud_IdlesInGuard()
    {
        var controller = Load<AnimatorOverrideController>(BudControllerPath);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        controller.GetOverrides(overrides);
        var swapped = overrides.Where(o => o.Value != null).ToArray();

        Assert.That(controller.runtimeAnimatorController, Is.SameAs(Load<RuntimeAnimatorController>(PitcherControllerPath)));
        Assert.That(swapped, Has.Length.EqualTo(1));
        Assert.That(swapped[0].Key.name, Is.EqualTo("Idle"));
        Assert.That(swapped[0].Value, Is.SameAs(Clips()["Guard"]));
    }

    // Rule 2: the bud wears the variant; the Pitcher keeps its own controller.
    [Test]
    public void PitcherBud_BodyIsTheVariant()
    {
        var definition = Load<EnemyDefinition>(BudPath);
        EnemyLook look = definition.ToLook();
        var variant = Load<GameObject>(BudPrefabPath);
        var pitcher = Load<GameObject>(PitcherPrefabPath);

        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body.gameObject, Is.SameAs(variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(variant), Is.SameAs(pitcher));
        Assert.That(variant.GetComponentInChildren<Animator>(true).runtimeAnimatorController,
            Is.SameAs(Load<RuntimeAnimatorController>(BudControllerPath)));
        Assert.That(look.BodyScale, Is.EqualTo(0.6f));
        Assert.That(definition.ToSpec().Behaviour, Is.EqualTo(EnemyBehaviourKind.Static));
        Assert.That(definition.ToSpec().ContactDamage, Is.Zero);
        Assert.That(pitcher.GetComponentInChildren<Animator>(true).runtimeAnimatorController,
            Is.SameAs(Load<RuntimeAnimatorController>(PitcherControllerPath)));
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, path);

        return asset;
    }

    private static Dictionary<string, AnimationClip> Clips() =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name);

    private GameObject Instance()
    {
        var instance = (GameObject)Object.Instantiate(Load<GameObject>(ModelPath));
        instance.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(instance);

        return instance;
    }
}
