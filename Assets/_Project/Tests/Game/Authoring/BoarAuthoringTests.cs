using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using static Soulvail.Tests.Game.Authoring.PackleaderPathTests;
using Num3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>BT-07b: the boar authored at the Packleader's Lv-14 slot, its body and its glob.</summary>
public sealed class BoarAuthoringTests
{
    private static readonly ContentId Packleader = new("path.packleader");
    private static readonly ContentId[] BoarRanks =
    {
        new("skill.packleader.boar"), new("skill.packleader.boar-2"), new("skill.packleader.boar-3"),
    };

    [Test]
    public void Packleader_LevelFourteen_HasBoar_ThreeRanks()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        var row = catalog.Path(Packleader).Skills[2];
        Assert.That(row.UnlockLevel, Is.EqualTo(14));
        Assert.That(row.Ranks.TierCount, Is.EqualTo(3));
        for (int tier = 1; tier <= 3; tier++)
        {
            Assert.That(row.Ranks.Tier(tier).Single(), Is.EqualTo(BoarRanks[tier - 1]));
        }

        PetSpec boar = Load<CharacterDefinition>("Data/Characters/Beastmaster.asset").ToSpec().Boar;
        Assert.That(boar, Is.Not.Null);
        Assert.That(boar.IsRanged, Is.True);
        Assert.That(boar.MaxHp, Is.EqualTo(60f));
        Assert.That(boar.MoveSpeed, Is.EqualTo(3.6f));
        Assert.That(boar.BiteDamage, Is.EqualTo(6f));
        Assert.That(boar.SpitInterval, Is.EqualTo(1.5f));
    }

    [Test]
    public void Packleader_TotalsTwentyRanks()
    {
        using IObjectResolver boot = Boot();
        var rows = boot.Resolve<ContentCatalog>().Path(Packleader).Skills;
        Assert.That(rows.Select(row => row.Ranks.TierCount), Is.EqualTo(new[] { 4, 4, 3, 3, 3, 3 }));
        Assert.That(rows.Sum(row => row.Ranks.TierCount), Is.EqualTo(20));
        Assert.That(rows.Select(row => row.UnlockLevel), Is.EqualTo(new[] { 11, 11, 14, 14, 17, 20 }));
        Assert.That(rows[3].Ranks.Tier(1).Single(), Is.EqualTo(new ContentId("skill.packleader.thick-pelts")));
    }

    [Test]
    public void BoarRanks_ResolveJoinDamageSlow()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        var expected = new[] { (PlayerStat.BoarCount, 1f), (PlayerStat.SpitDamage, 0.3f), (PlayerStat.SpitSlow, 0.25f) };
        ContentId parent = default;
        for (int i = 0; i < 3; i++)
        {
            SkillSpec skill = catalog.Skill(BoarRanks[i]);
            Assert.That(skill.Kind, Is.EqualTo(i == 0 ? SkillKind.Passive : SkillKind.Upgrade));
            Assert.That(skill.ParentId, Is.EqualTo(parent));
            ModifyStat effect = skill.Effects.Cast<ModifyStat>().Single();
            Assert.That((effect.Stat, effect.Value), Is.EqualTo(expected[i]));
            Assert.That(effect.Kind, Is.EqualTo(ModifierKind.Flat));
            Assert.That(effect.Target, Is.EqualTo(StatTarget.Pet));
            parent = skill.Id;
        }

        Assert.That(PetSpit.SlowSeconds, Is.EqualTo(1.5f), "rank 3 slows for 1.5 s.");
    }

    [Test]
    public void PetViews_BoarKind_UsesBoarPrefab()
    {
        var hound = Load<GameObject>("Prefabs/Pets/Hound.prefab").GetComponent<PetView>();
        var boar = Load<GameObject>("Prefabs/Pets/Boar.prefab").GetComponent<PetView>();
        Assert.That(boar, Is.Not.Null, "Boar.prefab carries a PetView.");
        Assert.That(boar.GetComponent<PetHealthBar>(), Is.Not.Null, "and the always-on bar.");
        Assert.That(Load<CharacterDefinition>("Data/Characters/Beastmaster.asset").ToLook().Boar, Is.SameAs(boar));

        var animator = boar.GetComponentInChildren<Animator>(true);
        Assert.That(animator.applyRootMotion, Is.False);
        var controller = (AnimatorController)animator.runtimeAnimatorController;
        Dictionary<string, Motion> states = controller.layers[0].stateMachine.states
            .ToDictionary(state => state.state.name, state => state.state.motion);
        Assert.That(states.Keys, Is.SupersetOf(new[] { "Locomotion", "Bite", "Hit", "Fall", "GetUp", "Land" }));
        Assert.That(states["Bite"].name, Is.EqualTo("Lunge"));
        Assert.That(states["Fall"].name, Is.EqualTo("Death"));
        Assert.That(states["Hit"].name, Is.EqualTo("Hit"));
        Assert.That(controller.parameters.Select(p => p.name), Is.SupersetOf(new[] { "Speed", "Bite", "Roar", "Leap" }));

        // PL-06's war paint reaches the boar by its renderer name, like the hound's.
        var look = Load<PathLook>("Data/Paths/PackleaderLook.asset");
        Assert.That(look.PetMaterials.Count(swap => swap.Renderer == "Boar_Body"), Is.EqualTo(2));

        using var hub = new DomainEventHub();
        using IObjectResolver resolver = new ContainerBuilder().Build();
        var root = new GameObject("Pet bodies");
        try
        {
            using var views = new PetViews(resolver, hound, root.transform, hub, boarPrefab: boar);
            hub.Publish(new PetSpawned(1, default));
            hub.Publish(new PetSpawned(2, default, isLead: false, kind: PetKind.Boar));
            PetView[] bodies = root.GetComponentsInChildren<PetView>();
            PetView boarBody = bodies.Single(body => body.Id == 2);
            Assert.That(boarBody.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "Boar_Body"), Is.True);
            Assert.That(bodies.Single(body => body.Id == 1).GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Any(r => r.name == "Hound_Body"), Is.True);
            Assert.That(boarBody.transform.localScale, Is.EqualTo(boar.transform.localScale));

            // A boar body goes back to the boar pool: the next hound does not wear it.
            hub.Publish(new PetDespawned(2));
            hub.Publish(new PetSpawned(3, default, isLead: false));
            PetView packHound = root.GetComponentsInChildren<PetView>().Single(body => body.Id == 3);
            Assert.That(packHound, Is.Not.SameAs(boarBody));
            Assert.That(packHound.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "Hound_Body"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void GlobView_Colour_GreenCore_CyanTrail()
    {
        AssertColour(PetGlobView.CoreColour, new Color32(0x16, 0xA3, 0x4A, 0xFF));
        Assert.That(PetGlobView.TrailColour, Is.EqualTo(Palette.Player));

        var flight = new PetGlob[PetSpit.Capacity];
        int count = 0;
        using var hub = new DomainEventHub();
        var parent = new GameObject("Globs");
        try
        {
            using var view = new PetGlobView(hub, Load<GameObject>("Prefabs/Vfx/PetGlob.prefab"), parent.transform,
                () => flight.AsSpan(0, count));
            flight[count++] = new PetGlob { Id = 1, Position = new Num3(2f, 0f, 3f) };
            hub.Publish(new PetSpat(9, 1, new Num3(0f, 0f, 0f), 5));
            view.Draw(0.016f);

            GameObject body = view.Bodies.ToArray().Single(b => b.activeSelf);
            Transform core = body.transform.Find("Core");
            Assert.That(core.gameObject.activeSelf, Is.True);
            Assert.That(core.position.x, Is.EqualTo(2f).Within(0.0001f), "the core follows core's glob.");
            var block = new MaterialPropertyBlock();
            core.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            AssertColour(block.GetColor("_BaseColor"), PetGlobView.CoreColour);
            var trail = core.GetComponent<TrailRenderer>();
            AssertColour(trail.startColor, Palette.Player);
            Assert.That(trail.startWidth, Is.LessThanOrEqualTo(0.1f), "a thin trail.");

            // A hit leaves a splat where it landed.
            count = 0;
            hub.Publish(new GlobLanded(1, new Num3(2f, 0f, 3f), true));
            Transform splat = body.transform.Find("Splat");
            Assert.That(core.gameObject.activeSelf, Is.False);
            Assert.That(splat.gameObject.activeInHierarchy, Is.True);
            splat.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            AssertColour(block.GetColor("_BaseColor"), PetGlobView.CoreColour);
            view.Draw(PetGlobView.SplatSeconds + 0.01f);
            Assert.That(splat.gameObject.activeInHierarchy, Is.False);

            // Cover swallows a glob: no splat.
            flight[count++] = new PetGlob { Id = 2 };
            hub.Publish(new PetSpat(9, 2, default, 5));
            hub.Publish(new GlobLanded(2, new Num3(1f, 0f, 1f), false));
            Assert.That(view.Bodies.ToArray().Any(b => b.activeSelf), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(parent);
        }
    }

    [Test]
    public void GlobView_Pooled_NoInstantiateAfterWarmup()
    {
        var flight = new PetGlob[PetSpit.Capacity];
        int count = 0;
        using var hub = new DomainEventHub();
        var parent = new GameObject("Globs");
        try
        {
            using var view = new PetGlobView(hub, Load<GameObject>("Prefabs/Vfx/PetGlob.prefab"), parent.transform,
                () => flight.AsSpan(0, count));
            GameObject[] warm = view.Bodies.ToArray();
            Assert.That(warm.Length, Is.EqualTo(PetSpit.Capacity));
            int objects = parent.GetComponentsInChildren<Transform>(true).Length;

            int id = 1;
            for (int round = 0; round < 20; round++)
            {
                count = 0;
                for (int i = 0; i < PetSpit.Capacity; i++, id++)
                {
                    flight[count++] = new PetGlob { Id = id };
                    hub.Publish(new PetSpat(9, id, default, 5));
                }

                Assert.That(view.ActiveCount, Is.EqualTo(PetSpit.Capacity));
                for (int i = 0; i < PetSpit.Capacity; i++)
                {
                    hub.Publish(new GlobLanded(id - PetSpit.Capacity + i, default, round % 2 == 0));
                }

                view.Draw(PetGlobView.SplatSeconds + 0.01f);
            }

            Assert.That(view.Bodies.ToArray(), Is.EqualTo(warm), "the same bodies all run.");
            Assert.That(parent.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objects), "nothing instantiated after warm-up.");

            // A ninth glob with the pool full flies unseen rather than instantiating.
            count = 0;
            for (int i = 0; i <= PetSpit.Capacity; i++, id++)
            {
                hub.Publish(new PetSpat(9, id, default, 5));
            }

            Assert.That(view.ActiveCount, Is.EqualTo(PetSpit.Capacity));
            Assert.That(parent.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objects));
        }
        finally
        {
            Object.DestroyImmediate(parent);
        }
    }

    private static void AssertColour(Color actual, Color expected)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.002f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.002f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.002f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.002f));
    }
}
