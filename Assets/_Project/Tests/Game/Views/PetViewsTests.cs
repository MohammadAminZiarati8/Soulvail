using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Num2 = System.Numerics.Vector2;
using Num3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class PetViewsTests
{
    private const string Characters = "Assets/_Project/Data/Characters/";
    private DomainEventHub _hub;
    private IObjectResolver _resolver;
    private PetView _prefab;
    private GameObject _parent;
    private PetViews _views;
    private WorldSnapshot _snapshot;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _resolver = new ContainerBuilder().Build();
        _parent = new GameObject("Pet test bodies");
        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Pets/Hound.prefab").GetComponent<PetView>();
        _snapshot = new WorldSnapshot(1);
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        _views = null;
        _hub.Dispose();
        _resolver.Dispose();
        Object.DestroyImmediate(_parent);
    }

    [Test]
    public void Spawned_OneBody_ReportsPosition()
    {
        CreateViews(_prefab);
        var spawn = new PetSpawned(1, new Num3(2f, 0f, 3f));
        _hub.Publish(spawn);
        _hub.Publish(spawn);

        _views.CopyInto(_snapshot);

        Assert.That(_parent.GetComponentsInChildren<PetView>(), Has.Length.EqualTo(1));
        Assert.That(_snapshot.PetCount, Is.EqualTo(1));
        Assert.That(_snapshot.Pets[0].Id, Is.EqualTo(1));
        Assert.That(_snapshot.Pets[0].Position, Is.EqualTo(spawn.Position));
    }

    [Test]
    public void MoveIntent_MovesBody()
    {
        CreateViews(_prefab);
        _hub.Publish(new PetSpawned(1, Num3.Zero));

        _views.ApplyMoves(new[] { new EnemyMoveIntent(1, 4f * Num3.UnitX, Num2.UnitX) }, 0.25f);
        _views.CopyInto(_snapshot);

        Assert.That(_snapshot.Pets[0].Position, Is.EqualTo(Num3.UnitX));
        Assert.That(_snapshot.Pets[0].Velocity, Is.EqualTo(4f * Num3.UnitX));
        PetView body = _parent.GetComponentInChildren<PetView>();
        Assert.That(Vector3.Dot(body.transform.forward, Vector3.right), Is.EqualTo(1f).Within(0.001f));
        Assert.That(body.GetComponentInChildren<Animator>().GetFloat("Speed"), Is.EqualTo(4f));
    }

    [Test]
    public void Bit_PlaysBite()
    {
        CreateViews(_prefab);
        _hub.Publish(new PetSpawned(1, Num3.Zero));
        Animator animator = _parent.GetComponentInChildren<Animator>();
        int bite = Animator.StringToHash("Bite");
        animator.Rebind();
        animator.Update(0f);

        _hub.Publish(new PetBit(2, 9, 7f));
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.Not.EqualTo(bite));

        _hub.Publish(new PetBit(1, 9, 7f));
        animator.Update(0.02f);
        animator.Update(0.05f);
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(bite));
    }

    [Test]
    public void Bar_AlwaysShown_PlayerCyan()
    {
        CreateViews(_prefab);
        _hub.Publish(new PetSpawned(1, Num3.Zero, healthFraction: 0.6f));
        Image fill = _parent.transform.GetComponentInChildren<PetHealthBar>()
            .transform.Find("Pet Health/Fill").GetComponent<Image>();

        Assert.That(fill.isActiveAndEnabled, Is.True);
        Assert.That(fill.color, Is.EqualTo(Palette.Player));
        Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(0.6f));
        Assert.That(fill.rectTransform.parent.localPosition.y, Is.GreaterThan(0f));
    }

    [Test]
    public void StageTransition_RemovesBody()
    {
        CreateViews(_prefab);
        _hub.Publish(new PetSpawned(1, Num3.Zero));

        _hub.Publish(new StageArrived(2, default));
        _views.CopyInto(_snapshot);

        Assert.That(_parent.GetComponentsInChildren<PetView>(), Is.Empty);
        Assert.That(_snapshot.PetCount, Is.Zero);

        _hub.Publish(new PetSpawned(2, Num3.One));
        _views.CopyInto(_snapshot);
        Assert.That(_snapshot.PetCount, Is.EqualTo(1));
        Assert.That(_snapshot.Pets[0].Id, Is.EqualTo(2));

        _hub.Publish(new RunEnded(1f));
        Assert.That(_parent.GetComponentsInChildren<PetView>(), Is.Empty);
    }

    [Test]
    public void ArenaArrival_SpawnsAtTheTeleportedPlayerOffset()
    {
        ArenaView arena = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Arenas/Arena_Pillars.prefab").GetComponent<ArenaView>();
        var playerObject = new GameObject("Pet test player");
        playerObject.transform.SetParent(_parent.transform);
        var player = playerObject.AddComponent<PlayerView>();
        using var arenas = new ArenaPool(_resolver, new[] { arena }, _parent.transform, _hub, player);
        _views = new PetViews(_resolver, _prefab, _parent.transform, _hub, player, arenas);
        var heelOffset = new Num3(0f, 0f, -1.5f);

        for (int stage = 1; stage <= 2; stage++)
        {
            var beforeArrival = new Num3(stage * 4f, 0f, 8f);
            playerObject.transform.position = beforeArrival.ToUnity();
            _hub.Publish(new StageArrived(stage, arena.Id));
            _hub.Publish(new PetSpawned(stage, beforeArrival + heelOffset, beforeArrival));
            _snapshot.Clear();
            _views.CopyInto(_snapshot);

            Assert.That(_snapshot.PetCount, Is.EqualTo(1));
            Assert.That(_snapshot.Pets[0].Id, Is.EqualTo(stage));
            Assert.That(_snapshot.Pets[0].Position, Is.EqualTo(player.Position.ToNum() + heelOffset));
        }
    }

    [Test]
    public void NoPet_SpawnsNothing()
    {
        CharacterDefinition ranger = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Characters + "Ranger.asset");
        Assert.That(ranger.ToSpec().Pet, Is.Null);
        Assert.That(ranger.ToLook().Pet, Is.Null);
        CreateViews(ranger.ToLook().Pet);
        _hub.Publish(new PetSpawned(1, Num3.Zero));

        Assert.That(_parent.GetComponentsInChildren<PetHealthBar>(true), Is.Empty);
        _views.CopyInto(_snapshot);
        Assert.That(_snapshot.PetCount, Is.Zero);
    }

    [Test]
    public void Beastmaster_PetNumbers_MatchCh362()
    {
        CharacterDefinition beastmaster = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Characters + "Beastmaster.asset");
        PetSpec pet = beastmaster.ToSpec().Pet;

        Assert.That(beastmaster.ToLook().Pet, Is.EqualTo(_prefab));
        Assert.That(pet.Id.Value, Is.EqualTo("pet.hound"));
        Assert.That(pet.MaxHp, Is.EqualTo(70f));
        Assert.That(pet.MoveSpeed, Is.EqualTo(4f));
        Assert.That(pet.BiteDamage, Is.EqualTo(7f));
        Assert.That(pet.BiteInterval, Is.EqualTo(1f));
        Assert.That(pet.Reach, Is.EqualTo(1.4f));
        Assert.That(pet.HeelDistance, Is.EqualTo(1.5f));
    }

    private void CreateViews(PetView prefab) => _views = new PetViews(_resolver, prefab, _parent.transform, _hub);
}
