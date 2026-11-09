using NUnit.Framework;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Arena;

[TestFixture]
public sealed class SunscarSandstormTests
{
    private const string Root = "Assets/_Project/";
    private const string Arenas = Root + "Prefabs/Arenas";
    private const string Dunes = "arena.sunscar.dunes";

    [Test]
    public void Sandstorm_Arena_OnlyDunes()
    {
        int storms = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Arenas }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (!prefab.TryGetComponent(out ArenaView arena))
            {
                continue;
            }

            var views = prefab.GetComponentsInChildren<SunscarSandstormView>(true);
            Assert.That(views, Has.Length.EqualTo(arena.Id.Value == Dunes ? 1 : 0), prefab.name);
            storms += views.Length;
        }

        Assert.That(storms, Is.EqualTo(1));
    }

    [Test]
    public void Sandstorm_Tint_PaleGoldenTan()
    {
        var parent = new GameObject("Sandstorm test");
        parent.SetActive(false);
        try
        {
            SunscarSandstormView view = BuildStorm(parent.transform);
            var material = view.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.That(material.mainTexture, Is.Not.Null);
            Assert.That(material.color, Is.EqualTo(view.SandTint));
            Color.RGBToHSV(material.color, out float hue, out float saturation, out float value);
            Assert.That(hue * 360f, Is.InRange(35f, 55f));
            Assert.That(saturation, Is.InRange(0.15f, 0.45f));
            Assert.That(value, Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(material.renderQueue, Is.LessThan((int)UnityEngine.Rendering.RenderQueue.Transparent));
        }
        finally
        {
            DestroyStorm(parent);
        }
    }

    [Test]
    public void Sandstorm_ClearRadius_CoversRangerWeapon()
    {
        var ranger = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Ranger.asset");
        var view = DunesPrefab().GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(float.IsFinite(view.ClearRadius), Is.True);
        Assert.That(view.ClearRadius, Is.GreaterThanOrEqualTo(ranger.ToSpec().Weapon.Range));
    }

    [Test]
    public void Sandstorm_View_OnlyVisualComponents()
    {
        var parent = new GameObject("Sandstorm test");
        parent.SetActive(false);
        try
        {
            SunscarSandstormView view = BuildStorm(parent.transform);
            foreach (Transform child in view.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(child.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
            }

            Assert.That(view.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Rigidbody2D>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Light>(true), Is.Empty);
            var systems = view.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(systems, Has.Length.EqualTo(1));
            Assert.That(systems[0].main.maxParticles, Is.InRange(1, 128));
            Assert.That(systems[0].collision.enabled || systems[0].trigger.enabled, Is.False);
        }
        finally
        {
            DestroyStorm(parent);
        }
    }

    private static void DestroyStorm(GameObject parent)
    {
        var renderer = parent.GetComponentInChildren<ParticleSystemRenderer>(true);
        if (renderer != null)
        {
            Object.DestroyImmediate(renderer.sharedMaterial);
        }

        Object.DestroyImmediate(parent);
    }

    private static GameObject DunesPrefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Arenas + "/Sunscar/Arena_Dunes.prefab");

    private static SunscarSandstormView BuildStorm(Transform parent)
    {
        var arena = Object.Instantiate(DunesPrefab(), parent).GetComponent<ArenaView>();
        var view = arena.GetComponentInChildren<SunscarSandstormView>(true);
        view.Bind(arena, parent);
        return view;
    }
}
