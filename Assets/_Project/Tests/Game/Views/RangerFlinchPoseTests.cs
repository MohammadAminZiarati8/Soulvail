using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// RS-09a rule 2: on the real Ranger body, a flinch gives the arms back. The draw and release after a
/// hit pose exactly as they would have with no hit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two runs, one with a hit, compared bone by bone.</b> Both shoot on the same schedule, so any
/// difference is the flinch. RS-09 left the arms 76–128° off for the rest of the fight; its wiring
/// tests passed, which is why this row reads poses.
/// </para>
/// <para>
/// <b>Culling off.</b> The body's Animator culls with <c>CullUpdateTransforms</c>, and nothing sees an
/// object in an EditMode fixture, so without this every bone would read as never moving and the row
/// would pass for the wrong reason. The during-the-flinch assertion guards that.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RangerFlinchPoseTests
{
    private const string BodyPath = "Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab";

    private const float Dt = 1f / 30f;
    private const int Frames = 30 * 6;
    private const int HitFrame = 60;
    private const int ShotPeriod = 27;
    private const int ReleaseAfter = 15;

    /// <summary>0.3 s after the flinch leaves <c>Hit</c>: its 0.15 s fade, and margin.</summary>
    private const int SettleFrames = 9;

    private static readonly int HitStateId = Animator.StringToHash("Hit");

    [Test]
    public void Flinch_LetsGoOfTheArms()
    {
        List<Quaternion[]> control = Run(false, out _);
        List<Quaternion[]> hurt = Run(true, out int lastHitFrame);

        Assert.That(lastHitFrame, Is.GreaterThan(HitFrame), "The flinch never played.");

        float during = 0f;

        for (int f = HitFrame; f <= lastHitFrame; f++)
        {
            during = Mathf.Max(during, Worst(control[f], hurt[f]));
        }

        Assert.That(during, Is.GreaterThan(10f), "The flinch moved nothing, so the row below proves nothing.");

        for (int f = lastHitFrame + SettleFrames; f < Frames; f++)
        {
            Assert.That(Worst(control[f], hurt[f]), Is.LessThan(1f),
                $"At {f * Dt:F2} s, {(f - lastHitFrame) * Dt:F2} s after the flinch, the arms are still off a run with no hit.");
        }
    }

    private static List<Quaternion[]> Run(bool hit, out int lastHitFrame)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);

        Assert.That(prefab, Is.Not.Null, BodyPath);

        var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var hub = new DomainEventHub();
        var poses = new List<Quaternion[]>(Frames);

        lastHitFrame = -1;

        try
        {
            RangerAnimatorView view = body.GetComponentInChildren<RangerAnimatorView>();
            Animator animator = body.GetComponentInChildren<Animator>();

            Assert.That(view, Is.Not.Null, "Bodies/Ranger has no RangerAnimatorView.");

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();

            Transform[] bones = animator.GetComponentsInChildren<Transform>();
            int flinch = animator.GetLayerIndex(FlinchLayer.Name);

            view.Construct(hub);
            animator.SetBool("Aiming", true);
            animator.SetFloat("ShotSpeed", 1.5f);

            for (int f = 0; f < Frames; f++)
            {
                int phase = f % ShotPeriod;

                if (f >= ReleaseAfter && phase == 0)
                {
                    animator.SetTrigger("Shoot");
                }

                if (f >= ReleaseAfter && phase == ReleaseAfter)
                {
                    animator.ResetTrigger("Shoot");
                    animator.SetTrigger("Release");
                }

                if (hit && f == HitFrame)
                {
                    hub.Publish(new PlayerDamaged(0f, 6f, 0.94f, 0f, false));
                }

                // The frame's order in play: the view's Update, then the Animator's evaluation.
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
            hub.Dispose();
            Object.DestroyImmediate(body);
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
