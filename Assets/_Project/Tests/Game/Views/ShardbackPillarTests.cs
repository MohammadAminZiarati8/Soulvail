using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

public sealed class ShardbackPillarTests
{
    private const int Boss = 7;
    private readonly ContentId _spec = new("enemy.shardback");
    private readonly Vector3 _origin = new(3000f, 0f, 3000f);
    private GameObject _parent;
    private GameObject _source;
    private DomainEventHub _hub;
    private CoverViews _views;
    private Guid _lifetime;
    private int _coverLayer;

    [SetUp]
    public void SetUp()
    {
        _parent = new GameObject("Cover test");
        _source = new GameObject("Synthetic Shardback cover");
        _source.SetActive(false);
        _hub = new DomainEventHub();
        _lifetime = Guid.NewGuid();
        _coverLayer = LayerMask.NameToLayer("Cover");
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        _views = null;
        _hub.Dispose();
        Object.DestroyImmediate(_source);
        Object.DestroyImmediate(_parent);
        Physics.SyncTransforms();
    }

    [TestCase(-1, 1, 2, "")]
    [TestCase(0, 0, 2, "")]
    [TestCase(0, 1, 3, "")]
    [TestCase(0, 1, 2, "missing")]
    [TestCase(0, 1, 2, "disabled")]
    [TestCase(0, 1, 2, "inactive")]
    [TestCase(0, 1, 2, "trigger")]
    [TestCase(0, 1, 2, "layer")]
    [TestCase(0, 1, 2, "nested")]
    public void Slots_Invalid_Refused(int first, int second, int third, string defect)
    {
        ShardbackPillar a = AddPillar(first);
        ShardbackPillar b = AddPillar(second);
        if (defect != "missing")
        {
            ShardbackPillar c = AddPillar(third);
            Collider blocker = c.GetComponent<Collider>();
            switch (defect)
            {
                case "disabled":
                    blocker.enabled = false;
                    break;
                case "inactive":
                    c.gameObject.SetActive(false);
                    break;
                case "trigger":
                    blocker.isTrigger = true;
                    break;
                case "layer":
                    c.gameObject.layer = 0;
                    break;
                case "nested":
                    c.transform.SetParent(b.transform, false);
                    break;
            }
        }

        Assert.Throws<InvalidOperationException>(() => Build());
        Assert.That(_parent.transform.childCount, Is.Zero);
        Assert.That(_hub.SubscriberCount<EnemySpawned>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<ShardbackFightStarted>(), Is.Zero);
        Assert.That(a.GetComponent<Collider>().enabled, Is.True);
    }

    [Test]
    public void Fight_DuplicateAndNewLifetime_RestoreOnce()
    {
        ShardbackPillar[] slots = Prepare();
        Break(_lifetime, 1);
        Start(_lifetime);
        _hub.Publish(new BossPhaseChanged(Boss, 1, 3));
        Assert.That(_views.Version, Is.EqualTo(3));
        Assert.That(slots[1].IsBroken, Is.True);
        Assert.That(slots[1].GetComponent<Renderer>().enabled, Is.False);

        Guid next = Guid.NewGuid();
        Start(next);
        Start(next);
        Assert.That(_views.Version, Is.EqualTo(4));
        foreach (ShardbackPillar slot in slots)
        {
            Assert.That(slot.IsBroken, Is.False);
            Assert.That(slot.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(slot.BossId, Is.EqualTo(Boss));
            Assert.That(slot.Lifetime, Is.EqualTo(next));
        }
    }

    [Test]
    public void Break_AtImpact_RemovesBothObstructions()
    {
        ShardbackPillar[] slots = Prepare();
        using IObjectResolver container = new ContainerBuilder().Build();
        using var arenas = new ArenaPool(container, Array.Empty<ArenaView>(), null, _hub, _parent.AddComponent<PlayerView>());
        var probe = new ShardbackProbeAdapter(1 << _coverLayer, arenas);
        var lane = new LaneProbeIntent(Boss, _origin.ToNum(), System.Numerics.Vector3.UnitZ,
            8f, 1f, new ShardbackProbeKey(_lifetime, 1), 0);
        Rect bounds = new(_origin.x - 20f, _origin.z - 20f, 40f, 40f);
        ShardbackStop stop = probe.Probe(lane, bounds);
        Assert.That(stop.Kind, Is.EqualTo(ShardbackStopKind.Pillar));
        Assert.That(stop.PillarSlot, Is.EqualTo(1));
        Assert.That(_views.Version, Is.EqualTo(2));
        Assert.That(slots[1].IsBroken, Is.False);
        Assert.That(Shot(0, 8f), Is.True);

        Break(_lifetime, 1);
        Break(_lifetime, 1);
        Assert.That(probe.Probe(lane, bounds).Kind, Is.EqualTo(ShardbackStopKind.None));
        Assert.That(Shot(0, 8f), Is.False);
        Assert.That(slots[1].IsBroken, Is.True);
        Assert.That(_views.Version, Is.EqualTo(3));
        Assert.That(_views.LiveCount, Is.EqualTo(1));
    }

    [Test]
    public void Lifecycle_StaleEventsAndUnload_Safe()
    {
        ShardbackPillar[] slots = Prepare();
        Break(_lifetime, 1);
        _hub.Publish(new EnemyDespawned(Boss));
        AssertLifted(slots[1]);
        Start(_lifetime);
        Break(_lifetime, 1);
        Assert.That(_views.Version, Is.EqualTo(4));
        Spawn();
        Break(_lifetime, 0);
        Assert.That(slots[0].IsBroken, Is.False);
        Guid next = Guid.NewGuid();
        Start(next);
        Break(_lifetime, 0);
        Break(next, 0, Boss + 1);
        Break(next, -1);
        Break(next, 3);
        Start(Guid.Empty);
        Assert.That(slots[0].IsBroken, Is.False);
        Assert.That(_views.Version, Is.EqualTo(6));

        _hub.Publish(new StageArrived(11, default));
        AssertLifted(slots[1]);
        Spawn(Boss + 1);
        Guid latest = Guid.NewGuid();
        Start(latest, Boss + 1);
        Start(next);
        Break(next, 1);
        Assert.That(slots[1].Lifetime, Is.EqualTo(latest));
        Assert.That(slots[1].IsBroken, Is.False);

        Object.DestroyImmediate(slots[0].transform.parent.gameObject);
        Assert.DoesNotThrow(() => Break(latest, 0, Boss + 1));
        Assert.DoesNotThrow(() => Start(Guid.NewGuid(), Boss + 1));
        Assert.DoesNotThrow(() => _hub.Publish(new EnemyDespawned(Boss + 1)));
        Object.DestroyImmediate(_parent);
        Assert.DoesNotThrow(() => _views.Dispose());
        Assert.That(_hub.SubscriberCount<ShardbackFightStarted>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<ShardbackPillarBroken>(), Is.Zero);
    }

    [Test]
    public void AllSpent_FreshFight_RestoresThree()
    {
        ShardbackPillar[] slots = Prepare(withWall: true);
        Transform inactive = slots[0].transform.GetChild(0);
        Collider disabled = inactive.GetComponent<Collider>();
        Renderer hidden = inactive.GetComponent<Renderer>();
        Renderer visible = inactive.GetChild(0).GetComponent<Renderer>();
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.That(Shot(X(i), 8f), Is.True);
            Break(_lifetime, i);
        }

        _hub.Publish(new BossPhaseChanged(Boss, 2, 3));
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.That(Shot(X(i), 8f), Is.False);
            Assert.That(Move(X(i), 8f), Is.False);
        }
        Assert.That(Shot(0, 12f), Is.True);
        Assert.That(Move(0, 12f), Is.True);
        Assert.That(visible.enabled, Is.False);

        using IObjectResolver container = new ContainerBuilder().Build();
        using var arenas = new ArenaPool(container, Array.Empty<ArenaView>(), null, _hub, _parent.AddComponent<PlayerView>());
        var probe = new ShardbackProbeAdapter(1 << _coverLayer, arenas);
        var lane = new LaneProbeIntent(Boss, _origin.ToNum(), System.Numerics.Vector3.UnitZ,
            12f, 1f, new ShardbackProbeKey(_lifetime, 2), 0);
        Assert.That(probe.Probe(lane, new Rect(_origin.x - 20f, _origin.z - 20f, 40f, 40f)).Kind,
            Is.EqualTo(ShardbackStopKind.Cover));
        Assert.That(probe.Probe(lane, new Rect(_origin.x - 20f, _origin.z - 20f, 40f, 28f)).Kind,
            Is.EqualTo(ShardbackStopKind.Boundary));

        Start(Guid.NewGuid());
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.That(Shot(X(i), 8f), Is.True);
            Assert.That(Move(X(i), 8f), Is.True);
        }
        Assert.That(inactive.gameObject.activeSelf, Is.False);
        Assert.That(disabled.enabled, Is.False);
        Assert.That(hidden.enabled, Is.False);
        Assert.That(visible.enabled, Is.True);
    }

    [Test]
    public void BreakRestore_Warm_AllocationFree()
    {
        ShardbackPillar[] slots = Prepare();
        Guid next = Guid.NewGuid();
        AllocationAssert.None(() =>
        {
            Break(_lifetime, 0);
            Start(next);
            Break(next, 0);
            Start(_lifetime);
        }, 100);
        Assert.That(slots[0].IsBroken, Is.False);
        Assert.That(slots[0].Lifetime, Is.EqualTo(_lifetime));
    }

    private ShardbackPillar[] Prepare(bool withWall = false)
    {
        ShardbackPillar first = AddPillar(0);
        AddPillar(2);
        AddPillar(1);
        GameObject inactive = Solid("Inactive child", first.transform, Vector3.zero);
        inactive.SetActive(false);
        inactive.GetComponent<Collider>().enabled = false;
        inactive.AddComponent<MeshRenderer>().enabled = false;
        var visible = new GameObject("Enabled beneath inactive child");
        visible.transform.SetParent(inactive.transform, false);
        visible.AddComponent<MeshRenderer>();
        if (withWall)
        {
            Solid("Generic wall", _source.transform, new Vector3(0, 1, 10));
        }

        Build();
        Spawn();
        Start(_lifetime);
        Physics.SyncTransforms();
        var slots = new ShardbackPillar[3];
        foreach (ShardbackPillar pillar in _parent.GetComponentsInChildren<ShardbackPillar>())
        {
            slots[pillar.Slot] = pillar;
        }

        return slots;
    }

    private void Build()
    {
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>
        {
            [_spec] = new EnemyLook(Color.white, 1f, cover: _source),
        });
        _views = new CoverViews(looks, _hub, _parent.transform);
    }

    private ShardbackPillar AddPillar(int slot)
    {
        GameObject pillar = Solid($"Pillar {slot}", _source.transform, new Vector3(X(slot), 1, 5));
        pillar.AddComponent<MeshRenderer>();
        var marker = pillar.AddComponent<ShardbackPillar>();
        var serialized = new SerializedObject(marker);
        serialized.FindProperty("_slot").intValue = slot;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return marker;
    }

    private GameObject Solid(string name, Transform parent, Vector3 position)
    {
        var solid = new GameObject(name);
        solid.layer = _coverLayer;
        solid.transform.SetParent(parent, false);
        solid.transform.localPosition = position;
        solid.AddComponent<BoxCollider>().size = new Vector3(1, 2, 1);
        return solid;
    }

    private void Spawn(int boss = Boss) => _hub.Publish(new EnemySpawned(boss, _spec, _origin.ToNum()));
    private void Start(Guid lifetime, int boss = Boss) => _hub.Publish(new ShardbackFightStarted(boss, lifetime));
    private void Break(Guid lifetime, int slot, int boss = Boss) => _hub.Publish(new ShardbackPillarBroken(boss, lifetime, slot));
    private static float X(int slot) => (slot - 1) * 4f;

    private bool Shot(float x, float reach) => Physics.Raycast(_origin + new Vector3(x, 1, 0),
        Vector3.forward, reach, 1 << _coverLayer, QueryTriggerInteraction.Ignore);

    private bool Move(float x, float reach) => Physics.SphereCast(_origin + new Vector3(x, 1, 0), 1f,
        Vector3.forward, out _, reach, 1 << _coverLayer, QueryTriggerInteraction.Ignore);

    private static void AssertLifted(ShardbackPillar pillar)
    {
        Assert.That(pillar.gameObject.activeInHierarchy, Is.False);
        Assert.That(pillar.BossId, Is.Zero);
        Assert.That(pillar.Lifetime, Is.EqualTo(Guid.Empty));
    }
}
