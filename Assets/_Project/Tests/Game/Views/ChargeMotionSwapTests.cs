using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>BT-05: a Wild Swap is stopped by nothing and lands on the hound's spot.</summary>
/// <remarks>
/// <c>Awake</c> does not run in EditMode (Traps §7), so the fixture calls it. The sweep resolves
/// through an empty <see cref="EnemyViews"/>, so it reports nobody and the session is never called.
/// </remarks>
[TestFixture]
public sealed class ChargeMotionSwapTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Step = 0.0625f;
    private const float Duration = 0.25f;
    private const float Distance = 3f;

    private readonly List<GameObject> _spawned = new();
    private GameObject _player;
    private ChargeMotion _motion;
    private CharacterController _controller;
    private RunSession _session;
    private int _enemyLayer;

    [SetUp]
    public void SetUp()
    {
        _enemyLayer = LayerMask.NameToLayer("Enemy");
        Assume.That(_enemyLayer, Is.GreaterThanOrEqualTo(0), "The project has no Enemy layer.");

        _player = new GameObject("SwapPlayer");
        _motion = _player.AddComponent<ChargeMotion>();
        _controller = _player.GetComponent<CharacterController>();
        typeof(ChargeMotion).GetMethod("Awake", Private)!.Invoke(_motion, null);

        var views = (EnemyViews)FormatterServices.GetUninitializedObject(typeof(EnemyViews));
        typeof(EnemyViews).GetField("_idByColliderInstance", Private)!.SetValue(views, new Dictionary<int, int>());
        typeof(ChargeMotion).GetField("_views", Private)!.SetValue(_motion, views);
        typeof(ChargeMotion).GetField("_enemyLayer", Private)!.SetValue(_motion, (LayerMask)(1 << _enemyLayer));
        _session = (RunSession)FormatterServices.GetUninitializedObject(typeof(RunSession));
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_player);
        foreach (GameObject spawned in _spawned)
        {
            Object.DestroyImmediate(spawned);
        }

        _spawned.Clear();
    }

    [Test]
    public void Swap_EnemyOnTheLine_LandsOnDestination()
    {
        Block(new Vector3(1.5f, 0f, 0f), _enemyLayer);
        _motion.Begin(Swap(), _session);
        Walk(Duration);
        Assert.That(_motion.IsActive, Is.False);
        Assert.That(Vector2.Distance(Flat(_player.transform.position), new Vector2(Distance, 0f)), Is.LessThan(0.05f));
    }

    [Test]
    public void Swap_PetBodyAtDestination_LandsOnIt()
    {
        var hound = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Pets/Hound.prefab"));
        _spawned.Add(hound);
        hound.transform.position = new Vector3(Distance, 0f, 0f);
        Physics.SyncTransforms();
        _motion.Begin(Swap(), _session);
        Walk(Duration);
        Assert.That(Vector2.Distance(Flat(_player.transform.position), Flat(hound.transform.position)), Is.LessThan(0.05f));
    }

    [Test]
    public void Swap_Ends_LayersRestored()
    {
        int before = 1 << 4;
        _controller.excludeLayers = before;
        Block(new Vector3(1.5f, 0f, 0f), _enemyLayer);
        _motion.Begin(Swap(), _session);
        Walk(Duration);
        Assert.That(_controller.excludeLayers.value, Is.EqualTo(before));
        Assert.That(_controller.enabled, Is.True);
    }

    [Test]
    public void Dash_EnemyOnTheLine_StillStopped()
    {
        Block(new Vector3(1.5f, 0f, 0f), _enemyLayer);
        _motion.Begin(new ChargeIntent(new System.Numerics.Vector2(1f, 0f), Distance, Duration), _session);
        Walk(Duration);
        Assert.That(_motion.IsActive, Is.False);
        Assert.That(_player.transform.position.x, Is.LessThan(1f - 0.4f), "The dash went through the enemy.");
    }

    private static ChargeIntent Swap() =>
        new(new System.Numerics.Vector2(1f, 0f), Distance, Duration, passesCover: true);

    /// <summary>A solid box on <paramref name="layer"/>, the stand-in for an enemy body that blocks the controller.</summary>
    private void Block(Vector3 position, int layer)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _spawned.Add(box);
        box.layer = layer;
        box.transform.position = position;
        box.transform.localScale = new Vector3(1f, 4f, 4f);
        Physics.SyncTransforms();
    }

    private void Walk(float seconds)
    {
        int steps = Mathf.RoundToInt(seconds / Step);
        for (int i = 0; i < steps; i++)
        {
            _motion.Step(Step);
        }
    }

    private static Vector2 Flat(Vector3 v) => new(v.x, v.z);
}
