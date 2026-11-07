using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

public sealed class BeastmasterBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string BodyPath = Root + "Prefabs/Characters/Beastmaster.prefab";
    private const string AxePath = "Assets/ThirdParty/KayKit/Adventurers/Props/axe_1handed.fbx";

    [Test]
    public void Beastmaster_InCatalogAndClassSelect()
    {
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Composition/BootScope.prefab");
        var scope = new SerializedObject(boot.GetComponentInChildren<BootScope>(true));

        foreach (string list in new[] { "_characters", "_classSelect" })
        {
            SerializedProperty entries = scope.FindProperty(list);
            Assert.That(Enumerable.Range(0, entries.arraySize)
                .Select(i => entries.GetArrayElementAtIndex(i).objectReferenceValue),
                Does.Contain(Definition()), list);
        }

        Assert.That(Definition().ToLook().Body, Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath)));
    }

    [Test]
    public void Beastmaster_AssetNumbers_MatchCh36()
    {
        CharacterSpec spec = Definition().ToSpec();
        Assert.That(spec.Id, Is.EqualTo(new ContentId("character.beastmaster")));
        Assert.That(spec.MaxHp, Is.EqualTo(100f));
        Assert.That(spec.Movement.Speed, Is.EqualTo(3.1f));
        Assert.That(spec.Weapon.Kind, Is.EqualTo(WeaponKind.ReturningAxe));
        Assert.That(spec.Weapon.Damage, Is.EqualTo(6f));
        Assert.That(spec.Weapon.SwingsPerSecond, Is.EqualTo(2f));
        Assert.That(spec.Weapon.Range, Is.EqualTo(8f));
        Assert.That(spec.Weapon.AxeSpeed, Is.EqualTo(14f));
        Assert.That(spec.Weapon.AxeCarry, Is.EqualTo(2));
        Assert.That(spec.Weapon.AxeHitsPerPass, Is.EqualTo(3));
        Assert.That(spec.Weapon.ShotRadius, Is.EqualTo(0.5f));
        Assert.That(spec.Weapon.MarkSeconds, Is.EqualTo(4f));
        Assert.That(spec.Weapon.FiresWhileMoving, Is.True);
        Assert.That(spec.Unlock, Is.Null);
        Assert.That(spec.Minions, Is.Null);
    }

    [Test]
    public void Paint_NoDanger_NoGold_NoLime()
    {
        const string path = Root + "Art/Characters/Textures/T_Beastmaster_Albedo.png";
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Characters/M_Beastmaster.mat");
        Assert.That(material.mainTexture, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(path)));

        var atlas = new Texture2D(2, 2);
        try
        {
            Assert.That(atlas.LoadImage(File.ReadAllBytes(path)), Is.True);
            Color32 cyan = Palette.Player;
            Color32[] colours = atlas.GetPixels32().Distinct().ToArray();
            Assert.That(colours, Does.Contain(cyan));
            Vector3 danger = Lab(Palette.Danger);
            foreach (Color32 texel in colours)
            {
                Color.RGBToHSV(texel, out float hue, out float saturation, out float value);
                float degrees = hue * 360f;
                Assert.That(Vector3.Distance(Lab(texel), danger), Is.GreaterThanOrEqualTo(20f), texel.ToString());
                Assert.That(degrees >= 40f && degrees <= 55f && saturation > 0.5f, Is.False, texel.ToString());
                Assert.That(degrees >= 55f && degrees <= 125f && saturation > 0.35f && value > 0.35f,
                    Is.False, texel.ToString());
            }
        }
        finally
        {
            Object.DestroyImmediate(atlas);
        }
    }

    [Test]
    public void Prefab_AxeOnEachHand_NoShield()
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
        Assert.That(PrefabUtility.GetPrefabAssetType(body), Is.EqualTo(PrefabAssetType.Variant));
        var source = PrefabUtility.GetCorrespondingObjectFromSource(body);
        Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(Root + "Prefabs/Characters/Barbarian.prefab"));

        foreach (string slot in new[] { "handslot.l", "handslot.r" })
        {
            Transform hand = Hand(body, slot);
            Assert.That(hand.childCount, Is.EqualTo(1));
            GameObject axe = hand.GetChild(0).gameObject;
            Assert.That(axe.activeSelf, Is.True);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(axe), Is.EqualTo(AxePath));
        }

        Assert.That(body.GetComponentsInChildren<Renderer>().Any(renderer => renderer.name.Contains("shield")),
            Is.False);
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Characters/M_Beastmaster.mat");
        Assert.That(body.GetComponentsInChildren<Renderer>().All(renderer => renderer.sharedMaterial == material),
            Is.True);
    }

    [Test]
    public void View_ThrowHidesAxe_CatchShowsIt()
    {
        var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath));
        using var events = new DomainEventHub();
        try
        {
            body.GetComponent<BeastmasterAnimatorView>().Construct(events);
            GameObject left = Hand(body, "handslot.l").GetChild(0).gameObject;
            GameObject right = Hand(body, "handslot.r").GetChild(0).gameObject;

            events.Publish(new AxeThrown(1, default, default));
            Assert.That(left.activeSelf, Is.False);
            Assert.That(right.activeSelf, Is.True);

            events.Publish(new AxeThrown(2, default, default));
            events.Publish(new AxeThrown(3, default, default));
            Assert.That(right.activeSelf, Is.False);

            events.Publish(new AxeCaught(3));
            Assert.That(left.activeSelf, Is.False);
            Assert.That(right.activeSelf, Is.False);

            events.Publish(new AxeCaught(2));
            Assert.That(right.activeSelf, Is.True);
            Assert.That(left.activeSelf, Is.False);
            events.Publish(new AxeCaught(1));
            Assert.That(left.activeSelf, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    [Test]
    public void Controller_HasThrowLayer_AndRigMediumClips()
    {
        var animator = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath).GetComponent<Animator>();
        Assert.That(animator.avatar.isValid, Is.True);
        Assert.That(animator.avatar.isHuman, Is.False);
        Assert.That(animator.applyRootMotion, Is.False);

        var controller = (AnimatorController)animator.runtimeAnimatorController;
        AnimatorControllerLayer upper = controller.layers.Single(layer => layer.name == "Throw");
        AvatarMask mask = upper.avatarMask;
        int[] paths = Enumerable.Range(0, mask.transformCount).ToArray();
        Assert.That(paths.Any(i => mask.GetTransformPath(i).EndsWith("/spine") && mask.GetTransformActive(i)), Is.True);
        Assert.That(paths.Where(i => mask.GetTransformPath(i).Contains("/upperleg"))
            .Any(mask.GetTransformActive), Is.False);
        Assert.That(upper.stateMachine.states.Single(state => state.state.name == "Throw").state.motion.name,
            Is.EqualTo("Throw"));
        Assert.That(controller.animationClips.Select(clip => clip.name),
            Is.EquivalentTo(new[] { "Idle_A", "Running_A", "Hit_A", "Death_A", "PickUp", "Throw", "Jump_Full_Short" }));
        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Does.StartWith("Assets/ThirdParty/KayKit/Animations/Rig_Medium/"));
            Assert.That(clip.events, Is.Empty);
        }
    }

    private static CharacterDefinition Definition() =>
        AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Beastmaster.asset");

    private static Transform Hand(GameObject body, string slot) =>
        body.GetComponentsInChildren<Transform>(true).Single(transform => transform.name == slot);

    // CIE 1976 L*a*b*, sRGB D65, as in ApexModelTests.
    private static Vector3 Lab(Color colour)
    {
        float r = Mathf.GammaToLinearSpace(colour.r), g = Mathf.GammaToLinearSpace(colour.g), b = Mathf.GammaToLinearSpace(colour.b);
        float x = F(((0.4124f * r) + (0.3576f * g) + (0.1805f * b)) / 0.95047f);
        float y = F((0.2126f * r) + (0.7152f * g) + (0.0722f * b));
        float z = F(((0.0193f * r) + (0.1192f * g) + (0.9505f * b)) / 1.08883f);
        return new Vector3((116 * y) - 16, 500 * (x - y), 200 * (y - z));

        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : (7.787f * t) + (16f / 116f);
    }
}
