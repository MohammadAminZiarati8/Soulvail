using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// The four rolls, as <c>Tools/Blender/roll.py</c> writes them and <c>AC_Ranger</c> plays them (RS-06d).
/// </summary>
/// <remarks>
/// KayKit has no roll, so these are authored, and a changed script or an import setting reset by an
/// update could ship a roll that hops, drifts off its root, sinks through the floor or turns the
/// wrong way. Each is sampled on KayKit's own <c>Ranger.fbx</c>, the body that plays it, because a
/// Generic clip binds by transform path and plays a bone at the offsets the clip's own rig had.
/// </remarks>
[TestFixture]
public sealed class RollClipTests
{
    private const string ClipsPath = "Assets/_Project/Animation/Clips/A_Roll.fbx";
    private const string BodyPath = "Assets/ThirdParty/KayKit/Adventurers/Characters/Ranger.fbx";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Ranger.controller";

    /// <summary>A roll's length: a <c>Dodge_*</c> clip's 12 frames at 30 fps, so AC_Ranger's timings hold.</summary>
    private const float Length = 0.4f;

    private const float FrameRate = 30f;

    /// <summary>
    /// How far the body's lowest vertex may sit from the floor on any frame, in metres. The script
    /// grounds each frame to a millimetre; this allows for the importer's keyframe reduction.
    /// </summary>
    private const float FloorTolerance = 0.03f;

    /// <summary>How far the head must lead the hips the roll's way before it goes under them, in metres.</summary>
    private const float Lead = 0.2f;

    /// <summary>
    /// How far the hips may stray from the root on XZ, in metres at model scale: RS-03d's 0.3 m
    /// capsule, measured on the body in a run, which is smaller than the model.
    /// </summary>
    private const float HipsDrift = 0.3f;

    /// <summary>Each roll and the way it goes, in the body's frame: +Z forward, +X right.</summary>
    private static readonly Dictionary<string, Vector3> Directions = new Dictionary<string, Vector3>
    {
        ["Roll_Forward"] = Vector3.forward,
        ["Roll_Backward"] = Vector3.back,
        ["Roll_Left"] = Vector3.left,
        ["Roll_Right"] = Vector3.right,
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

    [Test]
    public void Roll_ImportsGenericWithFourClips()
    {
        // Generic, as every Rig_Medium clip is (KayKit VERSIONS.md), and four rolls of a dodge's length.
        var importer = (ModelImporter)AssetImporter.GetAtPath(ClipsPath);

        Assert.That(importer, Is.Not.Null, $"No clips at {ClipsPath}. Run Tools/Blender/roll.py.");
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);

        Dictionary<string, AnimationClip> clips = Clips();

        Assert.That(clips.Keys.OrderBy(n => n), Is.EqualTo(Directions.Keys.OrderBy(n => n)));

        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.isLooping, Is.False, $"{clip.name} loops");
            Assert.That(clip.frameRate, Is.EqualTo(FrameRate), clip.name);
            Assert.That(clip.length, Is.EqualTo(Length).Within(1e-3f), clip.name);
        }
    }

    [Test]
    public void Roll_EveryCurveBindsOnTheRanger()
    {
        GameObject body = Body();

        foreach (AnimationClip clip in Clips().Values)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || body.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name} animates '{binding.path}', which the Ranger does not have.");
            }
        }
    }

    [Test]
    public void Roll_LeavesTheRootAlone()
    {
        // Core owns the body's position and ChargeMotion carries it through a roll (RS-03d rule 4):
        // a clip that moved the root would carry the mesh off its capsule.
        GameObject body = Body();
        Transform root = Bone(body, "root");
        Vector3 position = root.localPosition;
        Quaternion rotation = root.localRotation;

        foreach (AnimationClip clip in Clips().Values)
        {
            foreach (float t in Frames(clip))
            {
                clip.SampleAnimation(body, t);

                Assert.That((root.localPosition - position).magnitude, Is.LessThan(1e-4f), $"{clip.name} moves the root at {t:F3} s.");
                Assert.That(Quaternion.Angle(root.localRotation, rotation), Is.LessThan(0.1f), $"{clip.name} turns the root at {t:F3} s.");
            }
        }
    }

    [Test]
    public void Roll_KeepsTheHipsOverTheRoot()
    {
        // RS-03d rule 4's capsule, at model scale: the body turns about its hips, so they stay over
        // the root while the rest tumbles round them. A roll turned about the tuck's middle carried
        // them 1.2 m off it, because KayKit's head sits the middle well forward of the hips.
        GameObject body = Body();
        Transform root = Bone(body, "root");
        Transform hips = Bone(body, "hips");

        foreach (AnimationClip clip in Clips().Values)
        {
            foreach (float t in Frames(clip))
            {
                clip.SampleAnimation(body, t);

                float off = Vector3.ProjectOnPlane(hips.position - root.position, Vector3.up).magnitude;

                Assert.That(off, Is.LessThan(HipsDrift), $"{clip.name} carries the hips {off:F3} m off the root at {t:F3} s.");
            }
        }
    }

    [Test]
    public void Roll_GoesHeadOverHeels()
    {
        // The head goes under the hips once. On the last frame before it does, it leads them the way
        // the roll is named, which is what tells a forward roll from a backward one sampled the wrong
        // way round. By the end the body is upright again.
        GameObject body = Body();
        Transform head = Bone(body, "head");
        Transform hips = Bone(body, "hips");

        foreach ((string name, AnimationClip clip) in Clips().Select(kv => (kv.Key, kv.Value)))
        {
            float[] frames = Frames(clip).ToArray();
            Vector3 lead = Vector3.zero;
            bool over = false;

            foreach (float t in frames)
            {
                clip.SampleAnimation(body, t);

                if (head.position.y < hips.position.y)
                {
                    over = true;
                    break;
                }

                lead = Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up);
            }

            Assert.That(over, Is.True, $"{name}'s head never goes under its hips.");
            Assert.That(Vector3.Dot(lead, Directions[name]), Is.GreaterThan(Lead),
                $"{name}'s head leads by {lead} before it goes over, not toward {Directions[name]}.");

            clip.SampleAnimation(body, clip.length);

            Assert.That(head.position.y - hips.position.y, Is.GreaterThan(0.3f), $"{name} ends upside down.");
        }
    }

    [Test]
    public void Roll_KeepsTheBodyOnTheFloor()
    {
        // Every skinned mesh baked where the pose puts it, on every frame: the lowest vertex is on the
        // floor, so a roll neither sinks through it nor floats over it.
        GameObject body = Body();
        SkinnedMeshRenderer[] skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(baked);

        Assert.That(skins, Is.Not.Empty);

        foreach (AnimationClip clip in Clips().Values)
        {
            foreach (float t in Frames(clip))
            {
                clip.SampleAnimation(body, t);

                float lowest = float.MaxValue;

                foreach (SkinnedMeshRenderer skin in skins)
                {
                    // Baked with the renderer's scale, in its space: its position and rotation are
                    // what is left to apply.
                    skin.BakeMesh(baked, true);
                    Matrix4x4 place = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);

                    foreach (Vector3 vertex in baked.vertices)
                    {
                        lowest = Mathf.Min(lowest, place.MultiplyPoint3x4(vertex).y);
                    }
                }

                Assert.That(lowest, Is.InRange(-FloorTolerance, FloorTolerance), $"{clip.name} at {t:F3} s: the lowest vertex is at {lowest:F3} m.");
            }
        }
    }

    [Test]
    public void Ranger_DodgeBlendPlaysTheRolls()
    {
        // RS-03d's blend, its four places kept: RangerAnimatorView writes the roll's direction in the
        // body's frame, +X right and +Z forward, and the blend picks the clip there.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimatorState dodge = controller.layers[0].stateMachine.states.Select(s => s.state).Single(s => s.name == "Dodge");
        var tree = (BlendTree)dodge.motion;

        Assert.That(tree.children, Has.Length.EqualTo(Directions.Count));

        foreach (ChildMotion child in tree.children)
        {
            Assert.That(AssetDatabase.GetAssetPath(child.motion), Is.EqualTo(ClipsPath), $"{child.motion.name} is not a roll.");

            Vector3 way = Directions[child.motion.name];

            Assert.That(child.position, Is.EqualTo(new Vector2(way.x, way.z)), $"{child.motion.name} sits at {child.position}.");
        }
    }

    private static Dictionary<string, AnimationClip> Clips() =>
        AssetDatabase.LoadAllAssetsAtPath(ClipsPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name);

    private static IEnumerable<float> Frames(AnimationClip clip)
    {
        int last = Mathf.RoundToInt(clip.length * clip.frameRate);

        for (int frame = 0; frame <= last; frame++)
        {
            yield return frame / clip.frameRate;
        }
    }

    private static Transform Bone(GameObject body, string name) =>
        body.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

    private GameObject Body()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);

        Assert.That(model, Is.Not.Null, BodyPath);

        var instance = (GameObject)Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(instance);

        return instance;
    }
}
