using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class ShardbackModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = Art + "Shardback.fbx";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";

    private static readonly Dictionary<string, int> ClipFrames = new()
    {
        { "Idle", 48 }, { "Walk", 24 }, { "FanWindup", 30 }, { "FanRelease", 10 },
        { "ChargeWindup", 36 }, { "ChargeTravel", 16 }, { "BurstWindup", 36 },
        { "BurstRelease", 10 }, { "Recovery", 60 }, { "Death", 36 },
    };

    private GameObject _instance;

    [SetUp]
    public void SetUp()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Assert.That(model, Is.Not.Null, ModelPath);
        _instance = Object.Instantiate(model);
        _instance.hideFlags = HideFlags.HideAndDontSave;
    }

    [TearDown]
    public void TearDown()
    {
        if (_instance != null)
        {
            Object.DestroyImmediate(_instance);
        }
    }

    [Test]
    public void Model_UsesRefittedBeetleRigAndSeparateCases()
    {
        var glassback = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Glassback.fbx");
        Transform root = RigRoot(_instance);
        Transform source = RigRoot(glassback);
        Assert.That(root.parent.name, Is.EqualTo("Rig_Beetle"));
        Assert.That(BonePaths(root), Is.EquivalentTo(BonePaths(source)));
        Assert.That(root.Find("Body/Head").position.z, Is.GreaterThan(root.position.z));
        Assert.That(ForelegWidth(root), Is.GreaterThan(ForelegWidth(source) * 1.9f));
        float frontReach = LegReach(root, 1) / LegReach(source, 1);
        float rearReach = LegReach(root, 3) / LegReach(source, 3);
        Assert.That(frontReach, Is.GreaterThan(rearReach + 0.1f));

        var skins = _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s => s.name);
        Assert.That(skins.Keys, Is.EquivalentTo(new[] { "Shardback_Body", "Shell_L", "Shell_R" }));
        Assert.That(skins.Values.Sum(s => s.sharedMesh.triangles.Length / 3), Is.InRange(1500, 3000));
        var material = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
        Assert.That(material, Is.Not.Null);
        foreach (SkinnedMeshRenderer skin in skins.Values)
        {
            Assert.That(skin.sharedMaterials, Is.EqualTo(new[] { material }), skin.name);
            Assert.That(skin.bones, Has.None.Null, skin.name);
        }

        foreach (string side in new[] { "L", "R" })
        {
            SkinnedMeshRenderer skin = skins["Shell_" + side];
            Transform hinge = root.Find("Body/" + skin.name);
            Assert.That(skin.sharedMesh.boneWeights.All(w =>
                w.weight0 > 0.999f && skin.bones[w.boneIndex0] == hinge), Is.True, skin.name);
        }
    }

    [Test]
    public void Clips_ImportInPlaceWithExpectedTimings()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(_instance.GetComponent<Animator>().avatar.isValid, Is.True);

        Dictionary<string, AnimationClip> clips = Clips();
        Assert.That(clips.Keys, Is.EquivalentTo(ClipFrames.Keys));
        Transform[] roots = { _instance.transform, RigRoot(_instance).parent, RigRoot(_instance) };
        Vector3[] positions = roots.Select(root => root.localPosition).ToArray();
        Quaternion[] rotations = roots.Select(root => root.localRotation).ToArray();
        Vector3[] scales = roots.Select(root => root.localScale).ToArray();
        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.length * clip.frameRate, Is.EqualTo(ClipFrames[clip.name]).Within(0.01f), clip.name);
            Assert.That(clip.isLooping, Is.EqualTo(clip.name is "Idle" or "Walk" or "ChargeTravel"), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
            for (int frame = 0; frame <= ClipFrames[clip.name]; frame++)
            {
                clip.SampleAnimation(_instance, frame / clip.frameRate);
                for (int i = 0; i < roots.Length; i++)
                {
                    string context = $"{clip.name} frame {frame}, {roots[i].name}";
                    Assert.That(Vector3.Distance(roots[i].localPosition, positions[i]), Is.LessThan(1e-4f), context);
                    Assert.That(Quaternion.Angle(roots[i].localRotation, rotations[i]), Is.LessThan(0.05f), context);
                    Assert.That(Vector3.Distance(roots[i].localScale, scales[i]), Is.LessThan(1e-4f), context);
                }
            }
        }
    }

    [Test]
    public void Clips_SampleDistinctAttacksAndOpenRecovery()
    {
        Dictionary<string, AnimationClip> clips = Clips();
        Transform body = RigRoot(_instance).Find("Body");
        Transform head = body.Find("Head");
        Transform foreleg = body.Find("Leg_L_1_Upper/Leg_L_1_Lower");

        Sample("Idle", 0);
        float neutralHead = head.position.y;
        float neutralBody = body.position.y;
        Sample("FanWindup", 29);
        Assert.That(head.position.y, Is.GreaterThan(neutralHead + 0.22f));
        Sample("FanRelease", 3);
        Assert.That(head.position.y, Is.LessThan(neutralHead));
        Sample("ChargeWindup", 16);
        Assert.That(head.position.y, Is.LessThan(neutralHead - 0.22f));
        Quaternion scrape = foreleg.localRotation;
        Sample("ChargeWindup", 23);
        Assert.That(Quaternion.Angle(scrape, foreleg.localRotation), Is.GreaterThan(13f));
        Sample("ChargeTravel", 2);
        Quaternion stride = foreleg.localRotation;
        Sample("ChargeTravel", 10);
        Assert.That(Quaternion.Angle(stride, foreleg.localRotation), Is.GreaterThan(6f));
        Sample("BurstWindup", 29);
        Assert.That(body.position.y, Is.LessThan(neutralBody - 0.1f));

        foreach (string side in new[] { "L", "R" })
        {
            string name = "Shell_" + side;
            Transform hinge = body.Find(name);
            var skin = _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.name == name);
            Sample("Idle", 0);
            Quaternion closed = hinge.localRotation;
            Vector3 closedCase = CaseCenter(skin);
            Sample("BurstRelease", 3);
            Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.GreaterThan(34f), name);
            foreach (int frame in new[] { 12, 30, 46 })
            {
                Sample("Recovery", frame);
                Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.GreaterThan(49f), name);
                Vector3 openCase = CaseCenter(skin);
                Assert.That(openCase.y, Is.GreaterThan(closedCase.y + 0.25f), name);
                Assert.That(Mathf.Abs(openCase.x), Is.GreaterThan(Mathf.Abs(closedCase.x) + 0.09f), name);
            }

            Sample("Recovery", 60);
            Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.LessThan(0.05f), name);
        }

        void Sample(string clip, int frame) => clips[clip].SampleAnimation(_instance, frame / 30f);
    }

    private static Vector3 CaseCenter(SkinnedMeshRenderer skin)
    {
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            skin.BakeMesh(baked);
            return skin.transform.TransformPoint(baked.bounds.center);
        }
        finally
        {
            Object.DestroyImmediate(baked);
        }
    }

    private static float LegReach(Transform root, int number)
    {
        Transform upper = root.Find($"Body/Leg_L_{number}_Upper");
        return Vector3.Distance(upper.position, upper.GetChild(0).position);
    }

    private static float ForelegWidth(Transform root) =>
        Mathf.Abs(root.Find("Body/Leg_L_1_Upper").position.x - root.Find("Body/Leg_R_1_Upper").position.x);

    private static Transform RigRoot(GameObject model) =>
        model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");

    private static IEnumerable<string> BonePaths(Transform root) =>
        root.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, root));

    private static Dictionary<string, AnimationClip> Clips() =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToDictionary(clip => clip.name);
}
