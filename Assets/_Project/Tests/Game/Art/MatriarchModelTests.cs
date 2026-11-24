using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class MatriarchModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string Prefabs = "Assets/_Project/Prefabs/Enemies/";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_CarrionMatriarch.mat";

    [Test]
    public void Model_UsesExistingRigAndSculptMaterial()
    {
        var sculpt = Load<GameObject>(Art + "CarrionMatriarch.fbx").GetComponentInChildren<SkinnedMeshRenderer>();
        var vulture = Load<GameObject>(Art + "Vulture.fbx").GetComponentInChildren<SkinnedMeshRenderer>();
        var model = Load<GameObject>(Prefabs + "CarrionMatriarch.prefab");
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var material = Load<Material>(MaterialPath);

        Assert.That(sculpt.bones.Select(bone => bone.name), Is.EqualTo(vulture.bones.Select(bone => bone.name)));
        Assert.That(sculpt.sharedMesh.bindposes, Is.EqualTo(vulture.sharedMesh.bindposes));
        Assert.That(sculpt.sharedMesh.triangles.Length / 3, Is.InRange(9500, 10500));
        Assert.That(sculpt.sharedMesh.GetBonesPerVertex().ToArray(), Has.All.InRange(1, 4));
        Assert.That(sculpt.sharedMaterial, Is.SameAs(material));
        Assert.That(skin.sharedMesh, Is.SameAs(sculpt.sharedMesh));
        Assert.That(skin.sharedMaterial, Is.SameAs(material));
        Assert.That(vulture.sharedMaterial, Is.Not.SameAs(material));
        Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(Load<Texture2D>(Art + "T_CarrionMatriarch_Albedo.png")));

        var instance = Object.Instantiate(model);
        var baked = new Mesh();
        try
        {
            var animator = instance.GetComponentInChildren<Animator>();
            Assert.That(animator.applyRootMotion, Is.False);
            animator.transform.parent.localScale = Vector3.one;
            var body = animator.GetComponentInChildren<SkinnedMeshRenderer>();
            Bounds grounded = Sample("Idle_Ground", 0f);
            Bounds flight = Sample("Glide", 0.5f);
            Bounds pounce = Sample("Flap", 0.5f);
            Bounds dive = Sample("Dive", 0.5f);
            Bounds landed = Sample("Land", 0.99f);
            Assert.That(grounded.size.x, Is.LessThan(flight.size.x * 0.65f));
            Assert.That(pounce.size.x, Is.GreaterThan(grounded.size.x));
            Assert.That(dive.size.z, Is.GreaterThan(0.5f));
            Assert.That(grounded.min.y, Is.EqualTo(0f).Within(0.06f));
            Assert.That(landed.min.y, Is.EqualTo(grounded.min.y).Within(0.03f));

            Bounds Sample(string clipName, float normalizedTime)
            {
                AnimationClip clip = animator.runtimeAnimatorController.animationClips.Single(item => item.name == clipName);
                clip.SampleAnimation(animator.gameObject, clip.length * normalizedTime);
                body.BakeMesh(baked);
                foreach (Vector3 point in baked.vertices)
                {
                    Assert.That(body.localBounds.Contains(point), Is.True, $"Out of renderer bounds in {clipName}: {point}");
                }
                return baked.bounds;
            }
        }
        finally
        {
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void Prefab_PreservesGameplayAndWarningAnchors()
    {
        var boss = Load<GameObject>(Prefabs + "CarrionMatriarch.prefab");
        Assert.That(boss.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(boss.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(boss.transform.localScale, Is.EqualTo(Vector3.one));

        var trigger = boss.GetComponent<CapsuleCollider>();
        Assert.That(trigger.isTrigger && trigger.enabled, Is.True);
        Assert.That(trigger.radius, Is.EqualTo(0.6235f).Within(0.00001f));
        Assert.That(trigger.height, Is.EqualTo(0.89f));
        Assert.That(trigger.center, Is.EqualTo(new Vector3(0f, 0.43f, 0f)));

        var controller = boss.GetComponent<CharacterController>();
        Assert.That(controller.radius, Is.EqualTo(0.513f));
        Assert.That(controller.height, Is.EqualTo(0.82f));
        Assert.That(controller.center, Is.EqualTo(new Vector3(0f, 0.41f, 0f)));

        Transform model = boss.transform.Find("Model");
        Assert.That(model.localScale, Is.EqualTo(Vector3.one * 3.3f));
        var view = new SerializedObject(boss.GetComponent<MatriarchView>());
        Assert.That(view.FindProperty("_model").objectReferenceValue, Is.SameAs(model));
        Assert.That(view.FindProperty("_animator").objectReferenceValue, Is.SameAs(model.GetComponentInChildren<Animator>()));
        AssertAnchor("_shadow", "FX_Vulture_Shadow", 3.3f);
        AssertAnchor("_warning", "Locked Footprint", 1f);
        AssertAnchor("_fill", "Dive Fill", 1f);

        var feedback = new SerializedObject(boss.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue,
            Is.SameAs(model.GetComponentInChildren<SkinnedMeshRenderer>()));

        void AssertAnchor(string field, string name, float scale)
        {
            Transform anchor = boss.transform.Find(name);
            Assert.That(anchor, Is.Not.Null, name);
            Assert.That(anchor.localScale, Is.EqualTo(Vector3.one * scale), name);
            var component = (Component)view.FindProperty(field).objectReferenceValue;
            Assert.That(component.transform, Is.SameAs(anchor), name);
        }
    }

    private static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
