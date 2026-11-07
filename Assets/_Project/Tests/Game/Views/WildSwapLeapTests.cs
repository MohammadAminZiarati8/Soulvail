using System;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Num2 = System.Numerics.Vector2;
using Num3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

public sealed class WildSwapLeapTests
{
    private const string Root = "Assets/_Project/";

    [Test]
    public void Beastmaster_MovementIsSwap_Numbers()
    {
        MovementSkillSpec movement = Character("Beastmaster").ToSpec().MovementSkill;

        Assert.That(movement.Kind, Is.EqualTo(MovementSkillKind.Swap));
        Assert.That(movement.Duration, Is.EqualTo(0.25f));
        Assert.That(movement.Cooldown, Is.EqualTo(6f));
        Assert.That(movement.IFrameTrail, Is.EqualTo(0.2f));
    }

    [Test]
    public void ChargeStarted_Swap_PlaysJumpFullShort()
    {
        var body = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Characters/Beastmaster.prefab"));
        using var events = new DomainEventHub();
        try
        {
            CharacterSpec charge = Character("Oathbound").ToSpec();
            CharacterSpec swap = Character("Beastmaster").ToSpec();
            var catalog = new ContentCatalog(new[] { charge, swap });
            body.GetComponent<BeastmasterAnimatorView>().Construct(events, catalog);
            Animator animator = body.GetComponentInChildren<Animator>();
            animator.Rebind();
            animator.Update(0f);
            int jump = Animator.StringToHash("Jump_Full_Short");

            events.Publish(new RunStarted(charge.Id, 1));
            events.Publish(new ChargeStarted(Num2.UnitX));
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.Not.EqualTo(jump));

            events.Publish(new RunStarted(swap.Id, 1));
            events.Publish(new ChargeStarted(Num2.UnitX));
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(jump));
            Assert.That(animator.GetCurrentAnimatorClipInfo(0)[0].clip.name, Is.EqualTo("Jump_Full_Short"));
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    [Test]
    public void PetLeap_RoutesToItsPet_UnknownIgnored()
    {
        var parent = new GameObject("Swap routing");
        try
        {
            using var events = new DomainEventHub();
            using IObjectResolver resolver = new ContainerBuilder().Build();
            using var views = new PetViews(resolver, Hound(), parent.transform, events);
            events.Publish(new PetSpawned(1, Num3.Zero));
            events.Publish(new PetSpawned(2, Num3.One));

            views.ApplyLeaps(new[]
            {
                new PetLeapIntent(99, Num3.UnitZ, 0.25f),
                new PetLeapIntent(2, 5f * Num3.UnitX, 0.25f)
            });
            views.ApplyMoves(Array.Empty<EnemyMoveIntent>(), 0.125f);

            PetView[] pets = parent.GetComponentsInChildren<PetView>();
            Assert.That(pets.Single(pet => pet.Id == 1).transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(pets.Single(pet => pet.Id == 2).transform.position.x, Is.EqualTo(3f));
        }
        finally
        {
            Object.DestroyImmediate(parent);
        }
    }

    [Test]
    public void PetLeap_ArcsAndLands()
    {
        PetView pet = Object.Instantiate(Hound());
        try
        {
            pet.Bind(1, Vector3.zero, 1f, null);
            Animator animator = pet.GetComponentInChildren<Animator>();
            animator.Rebind();
            animator.Update(0f);
            var to = new Vector3(4f, 0f, 2f);

            pet.Leap(to, 0.25f);
            animator.Update(0.02f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                Is.EqualTo(Animator.StringToHash("Pounce")));

            pet.StepLeap(0.125f);
            Assert.That(pet.transform.position.y, Is.EqualTo(0.6f).Within(0.01f));
            Assert.That(pet.transform.position.x, Is.EqualTo(2f));
            Vector3 midpoint = pet.transform.position;
            pet.Apply(new EnemyMoveIntent(1, 20f * Num3.UnitZ, Num2.UnitY), 0.125f);
            Assert.That(pet.transform.position, Is.EqualTo(midpoint));

            pet.StepLeap(0.125f);
            animator.Update(0f);
            Assert.That(pet.transform.position, Is.EqualTo(to));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                Is.EqualTo(Animator.StringToHash("Land")));
        }
        finally
        {
            Object.DestroyImmediate(pet.gameObject);
        }
    }

    [Test]
    public void Swap_WithoutPet_Refused()
    {
        CharacterDefinition definition = Object.Instantiate(Character("Oathbound"));
        try
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_movementSkillKind").intValue = (int)MovementSkillKind.Swap;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var error = Assert.Throws<ArgumentException>(() => definition.ToSpec());
            Assert.That(error.Message, Does.Contain(definition.name).And.Contain("Swap requires a pet"));
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }

    private static CharacterDefinition Character(string name) =>
        AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/" + name + ".asset");

    private static PetView Hound() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Pets/Hound.prefab").GetComponent<PetView>();
}
