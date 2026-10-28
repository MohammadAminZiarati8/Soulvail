using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// JG-09 rules 2 and 3: every enemy body with a flinch layer gives the body back after a flinch, and
/// the layer rests at weight 0 in its controller.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two runs per body, one with a hit, compared bone by bone</b> — RS-09a's
/// <c>RangerFlinchPoseTests</c> shape. Each body idles; the hit is the <c>Hit</c> trigger set
/// directly, since the trigger's guards are <c>EnemyAnimatorViewTests</c>' and this row is about what
/// happens after it. The view's <see cref="EnemyAnimatorView.Step"/> runs every frame before the
/// Animator, as it does in play, so the weight it drives is the one under test.
/// </para>
/// <para>
/// <b>Culling off</b> for <c>RangerFlinchPoseTests</c>' reason, and each row guards itself with a
/// during-the-flinch check. Before JG-09 the bodies measured 1–44° off from 0.3 s after the flinch.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EnemyFlinchPoseTests
{
    private const float Dt = 1f / 30f;
    private const int Frames = 30 * 4;
    private const int HitFrame = 30;

    /// <summary>0.3 s after the flinch leaves <c>Hit</c>: its fade, and margin.</summary>
    private const int SettleFrames = 9;

    private static readonly int HitStateId = Animator.StringToHash("Hit");

    /// <summary>The body, and how far its flinch must move a bone to prove the row live.</summary>
    private static readonly object[] Bodies =
    {
        new object[] { "Croaker", 5f },
        new object[] { "Frog", 5f },
        new object[] { "Panther", 5f },
        new object[] { "Pitcher", 5f },

        // The Puffball's flinch is a small cap wobble, 6° at its peak.
        new object[] { "Puffball", 1f },
        new object[] { "Rootling", 5f },
    };

    private static readonly string[] Controllers =
    {
        "AC_Croaker", "AC_Frog", "AC_Panther", "AC_Pitcher", "AC_Puffball", "AC_Rootling",
    };

    [TestCaseSource(nameof(Bodies))]
    public void Flinch_LetsGoOfTheBody(string body, float live)
    {
        List<Quaternion[]> control = Run(body, false, out _);
        List<Quaternion[]> hurt = Run(body, true, out int lastHitFrame);

        Assert.That(lastHitFrame, Is.GreaterThan(HitFrame), $"{body}'s flinch never played.");

        float during = 0f;

        for (int f = HitFrame; f <= lastHitFrame; f++)
        {
            during = Mathf.Max(during, Worst(control[f], hurt[f]));
        }

        Assert.That(during, Is.GreaterThan(live), $"{body}'s flinch moved nothing, so the row below proves nothing.");

        for (int f = lastHitFrame + SettleFrames; f < Frames; f++)
        {
            Assert.That(Worst(control[f], hurt[f]), Is.LessThan(1f),
                $"{body} at {f * Dt:F2} s, {(f - lastHitFrame) * Dt:F2} s after the flinch, is still off a run with no hit.");
        }
    }

    [TestCaseSource(nameof(Controllers))]
    public void FlinchLayer_RestsAtZero(string controller)
    {
        string path = $"Assets/_Project/Animation/Controllers/{controller}.controller";
        var asset = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

        Assert.That(asset, Is.Not.Null, path);

        AnimatorControllerLayer layer = System.Array.Find(asset.layers, l => l.name == FlinchLayer.Name);

        Assert.That(layer, Is.Not.Null, $"{controller} has no {FlinchLayer.Name} layer.");
        Assert.That(layer.defaultWeight, Is.Zero, $"{controller}'s flinch layer at rest would hold the flinch's last pose.");
    }

    private static List<Quaternion[]> Run(string body, bool hit, out int lastHitFrame)
    {
        string path = $"Assets/_Project/Prefabs/Enemies/{body}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, path);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var poses = new List<Quaternion[]>(Frames);

        lastHitFrame = -1;

        try
        {
            EnemyAnimatorView view = instance.GetComponentInChildren<EnemyAnimatorView>();
            Animator animator = instance.GetComponentInChildren<Animator>();

            Assert.That(view, Is.Not.Null, $"{body} has no EnemyAnimatorView.");

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();

            Transform[] bones = animator.GetComponentsInChildren<Transform>();
            int flinch = animator.GetLayerIndex(FlinchLayer.Name);

            for (int f = 0; f < Frames; f++)
            {
                if (hit && f == HitFrame)
                {
                    animator.SetTrigger("Hit");
                }

                view.Step(Dt);
                animator.Update(Dt);

                bool inHit = animator.GetCurrentAnimatorStateInfo(flinch).shortNameHash == HitStateId
                    || (animator.IsInTransition(flinch) && animator.GetNextAnimatorStateInfo(flinch).shortNameHash == HitStateId);

                if (inHit)
                {
                    lastHitFrame = f;
                }

                var pose = new Quaternion[bones.Length];

                for (int i = 0; i < bones.Length; i++)
                {
                    pose[i] = bones[i].localRotation;
                }

                poses.Add(pose);
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        return poses;
    }

    private static float Worst(Quaternion[] a, Quaternion[] b)
    {
        float worst = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            worst = Mathf.Max(worst, Quaternion.Angle(a[i], b[i]));
        }

        return worst;
    }
}
