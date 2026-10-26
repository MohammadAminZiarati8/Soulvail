using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// Old Croaker's model, as <c>Tools/Blender/croaker.py</c> writes it on the Frog's builder (JG-04a).
/// </summary>
/// <remarks>
/// The boss shares the Frog's skeleton so the Frog's upper-body mask and controller shape carry over,
/// and its clips are timed to JG-04b's telegraphs, so the frame counts here are the docstring's. The
/// atlas's reserved colours are <see cref="RootlingModelTests"/>' rows, which read every pixel the
/// Croaker samples too. The last row pins the Frog as it was before <c>frog.py</c> was split for reuse.
/// </remarks>
[TestFixture]
public sealed class CroakerModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Croaker.fbx";
    private const string FrogPath = "Assets/_Project/Art/Enemies/Frog.fbx";
    private const string FrogPrefabPath = "Assets/_Project/Prefabs/Enemies/Frog.prefab";

    /// <summary>The spec's "about 3.4 m at 1×", crown included.</summary>
    private const float MinHeight = 3.2f;

    private const float MaxHeight = 3.6f;

    /// <summary>Against the Frog as it stands in the arena, its model at the prefab's scale.</summary>
    private const float MinFrogs = 2.8f;

    private const float MaxFrogs = 3.6f;

    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        ["Croak"] = 48,
        ["Death"] = 36,
        ["Hit"] = 12,
        ["Hop"] = 24,
        ["Idle"] = 60,
        ["Lash"] = 42,
        ["Slam"] = 45,
    };

    private static readonly Dictionary<string, int> FrogClipFrames = new Dictionary<string, int>
    {
        ["Attack"] = 36,
        ["Death"] = 30,
        ["Hit"] = 12,
        ["Hop"] = 18,
        ["Idle"] = 48,
        ["Leap"] = 30,
    };

    private static readonly string[] Loops = { "Hop", "Idle" };

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

    [Test]
    public void Croaker_SharesTheFrogsBones()
    {
        // AM_Frog_UpperBody masks by path, so every bone must have the Frog's name and parent.
        Assert.That(Bones(Load(ModelPath)), Is.EqualTo(Bones(Load(FrogPath))));
    }

    [Test]
    public void Croaker_StandsAboutThreeFrogsTall()
    {
        float height = Body(Load(ModelPath)).sharedMesh.bounds.size.y;
        SkinnedMeshRenderer frog = Load(FrogPrefabPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
        float frogHeight = frog.sharedMesh.bounds.size.y * frog.transform.lossyScale.y;

        Assert.That(height, Is.InRange(MinHeight, MaxHeight), "The Croaker's height at 1×, in metres.");
        Assert.That(height / frogHeight, Is.InRange(MinFrogs, MaxFrogs),
            $"The Croaker is {height / frogHeight:F2} Frogs tall.");
    }

    [Test]
    public void Croaker_HasEveryClip_WithTheNamedLengths()
    {
        // Generic and unoptimised, as the Frog is; every clip named, looped where a body cycles in it,
        // 30 fps and the docstring's length; and none moves the root, which core owns.
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.optimizeGameObjects, Is.False);

        Dictionary<string, AnimationClip> clips = ClipsByName(ModelPath);

        Assert.That(clips.Keys.OrderBy(n => n), Is.EqualTo(ClipFrames.Keys.OrderBy(n => n)));

        GameObject model = Instance(Load(ModelPath));
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        Vector3 rest = root.localPosition;

        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(Frames(clip), Is.EqualTo(ClipFrames[clip.name]), $"{clip.name}'s length in frames");
            Assert.That(clip.isLooping, Is.EqualTo(Loops.Contains(clip.name)), $"{clip.name}'s loop flag");

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name} animates '{binding.path}', which the Croaker does not have.");
            }

            for (int frame = 0; frame <= Frames(clip); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);

                Assert.That((root.localPosition - rest).magnitude, Is.LessThan(1e-4f),
                    $"{clip.name} moves the root at frame {frame}.");
            }
        }
    }

    [Test]
    public void Frog_UnchangedByTheRefactor()
    {
        // The Frog as frog.py wrote it before the split: the same vertices, bounds and clips.
        Mesh mesh = Body(Load(FrogPath)).sharedMesh;

        Assert.That(mesh.vertexCount, Is.EqualTo(1322));
        Assert.That(mesh.bounds.size.x, Is.EqualTo(1.2508f).Within(1e-3f));
        Assert.That(mesh.bounds.size.y, Is.EqualTo(0.8278f).Within(1e-3f));
        Assert.That(mesh.bounds.size.z, Is.EqualTo(1.6712f).Within(1e-3f));
        Assert.That(ClipsByName(FrogPath).ToDictionary(p => p.Key, p => Frames(p.Value)), Is.EquivalentTo(FrogClipFrames));
    }

    private static GameObject Load(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(model, Is.Not.Null, $"Nothing at {path}. Run Tools/Blender/croaker.py.");

        return model;
    }

    private static SkinnedMeshRenderer Body(GameObject model) => model.GetComponentInChildren<SkinnedMeshRenderer>(true);

    private static Dictionary<string, string> Bones(GameObject model) =>
        model.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == "Rig_Frog")
            .GetComponentsInChildren<Transform>(true)
            .Where(t => t.name != "Rig_Frog") // its parent is the model, named for the file
            .ToDictionary(t => t.name, t => t.parent.name);

    private static Dictionary<string, AnimationClip> ClipsByName(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name);

    private static int Frames(AnimationClip clip) => Mathf.RoundToInt(clip.length * clip.frameRate);

    private GameObject Instance(GameObject model)
    {
        var instance = (GameObject)Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(instance);

        return instance;
    }
}
