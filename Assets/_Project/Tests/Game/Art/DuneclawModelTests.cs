using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class DuneclawModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Duneclaw.fbx";
    private const string ScorpionPath = "Assets/_Project/Art/Enemies/Scorpion.fbx";
    private static readonly string[] ClipNames =
    {
        "Idle", "Skitter", "SweepWindup", "Sweep", "SlamWindup", "Slam", "Pinned", "Phase", "Hit", "Death",
    };
    private static readonly string[] LoopNames = { "Idle", "Skitter", "Pinned" };

    [Test]
    public void DuneclawModel_ImportsSingleSkinWithinBudget()
    {
        GameObject model = LoadModel(ModelPath);
        SkinnedMeshRenderer[] skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(skins, Has.Length.EqualTo(1));
        Assert.That(model.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
        Assert.That(skins[0].sharedMaterials, Has.Length.EqualTo(1));

        Mesh mesh = skins[0].sharedMesh;
        Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        Assert.That(mesh.GetIndexCount(0) / 3, Is.InRange(1500u, 3000u));
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));
        Assert.That(counts, Is.All.InRange((byte)1, (byte)4));
        Assert.That(skins[0].sharedMaterial,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Enemies/M_Enemy.mat")));
    }

    [Test]
    public void DuneclawRig_MatchesScorpionHierarchy()
    {
        GameObject model = LoadModel(ModelPath);
        Transform target = Find(model, "Rig_Scorpion");
        Transform source = Find(LoadModel(ScorpionPath), "Rig_Scorpion");
        Assert.That(Paths(target), Is.EquivalentTo(Paths(source)));

        Vector3 right = model.transform.InverseTransformPoint(Find(model, "Pincer_R").position);
        Vector3 left = model.transform.InverseTransformPoint(Find(model, "Pincer_L").position);
        Assert.That(right.x, Is.GreaterThan(left.x), "The FBX must keep the scorpion's handedness in Unity.");
    }

    [Test]
    public void DuneclawClips_CompleteAndInPlace()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.motionNodeName, Is.Empty);

        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal))
            .ToDictionary(clip => clip.name);
        Assert.That(clips.Keys, Is.EquivalentTo(ClipNames));

        var instance = Object.Instantiate(LoadModel(ModelPath));
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Transform root = Find(instance, "Root");
            Transform rig = Find(instance, "Rig_Scorpion");
            Transform stinger = Find(instance, "Stinger");
            Transform right = Find(instance, "Pincer_R");
            Vector3 bindRoot = root.position;
            Quaternion bindRotation = root.rotation;
            Vector3 bindRig = rig.position;
            float bindStingerHeight = stinger.position.y;

            foreach (AnimationClip clip in clips.Values)
            {
                Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
                Assert.That(clip.isLooping, Is.EqualTo(LoopNames.Contains(clip.name)), clip.name);
                Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
                Assert.That(AnimationUtility.GetCurveBindings(clip), Is.Not.Empty, clip.name);

                clip.SampleAnimation(instance, 0);
                Vector3 pin = stinger.position;
                for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
                {
                    clip.SampleAnimation(instance, frame / clip.frameRate);
                    Assert.That(Vector3.Distance(root.position, bindRoot), Is.LessThan(1e-4f), clip.name);
                    Assert.That(Quaternion.Angle(root.rotation, bindRotation), Is.LessThan(0.05f), clip.name);
                    Assert.That(Vector3.Distance(rig.position, bindRig), Is.LessThan(1e-4f), clip.name);
                    if (clip.name == "Pinned")
                    {
                        Assert.That(Vector3.Distance(stinger.position, pin), Is.LessThan(1e-3f));
                    }
                }
            }

            AnimationClip sweep = clips["Sweep"];
            sweep.SampleAnimation(instance, 0);
            Quaternion windup = right.localRotation;
            sweep.SampleAnimation(instance, 10f / sweep.frameRate);
            Assert.That(Quaternion.Angle(right.localRotation, windup), Is.GreaterThan(80f));

            AnimationClip slam = clips["Slam"];
            slam.SampleAnimation(instance, 0);
            Assert.That(stinger.position.y, Is.GreaterThan(bindStingerHeight + 0.1f));
            slam.SampleAnimation(instance, slam.length);
            Vector3 landing = stinger.position;
            clips["Pinned"].SampleAnimation(instance, 0);
            Assert.That(Vector3.Distance(stinger.position, landing), Is.LessThan(1e-3f));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static GameObject LoadModel(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(model, Is.Not.Null, path);
        return model;
    }

    private static Transform Find(GameObject model, string name) =>
        model.GetComponentsInChildren<Transform>(true).Single(transform => transform.name == name);

    private static string[] Paths(Transform rig) =>
        rig.Find("Root").GetComponentsInChildren<Transform>(true)
            .Select(transform => AnimationUtility.CalculateTransformPath(transform, rig)).ToArray();
}
