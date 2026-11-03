using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Game.Views;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-25b rule 6: the body walks an enemy's throw like a dash, stops at walls, sweeps nobody.</summary>
/// <remarks>
/// <c>Awake</c> does not run in EditMode (Traps §7), so the fixture calls it. The walk is measured
/// on the ground plane: with no floor, the motion's grounding pin sinks the capsule a little.
/// </remarks>
[TestFixture]
public sealed class ChargeMotionThrowTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    /// <summary>Exact in binary, so four steps make the 0.25 s throw to the bit.</summary>
    private const float Step = 0.0625f;

    private GameObject _player;
    private GameObject _wall;
    private ChargeMotion _motion;

    [SetUp]
    public void SetUp()
    {
        _player = new GameObject("ThrowPlayer");
        _motion = _player.AddComponent<ChargeMotion>();
        typeof(ChargeMotion).GetMethod("Awake", Private)!.Invoke(_motion, null);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_player);
        if (_wall != null)
        {
            Object.DestroyImmediate(_wall);
        }
    }

    [Test]
    public void Throw_MovesDistanceInDuration()
    {
        _motion.Throw(new PlayerThrowIntent(new System.Numerics.Vector2(1f, 0f), 2f, 0.25f));
        Assert.That(_motion.IsActive, Is.True);
        Walk(0.25f);
        Assert.That(_motion.IsActive, Is.False);
        Assert.That(Vector2.Distance(Flat(_player.transform.position), new Vector2(2f, 0f)), Is.LessThan(0.02f));
        Walk(0.25f);
        Assert.That(Flat(_player.transform.position).x, Is.EqualTo(2f).Within(0.02f), "It ran past its duration.");
    }

    [Test]
    public void Throw_StopsAtWall()
    {
        _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _wall.transform.position = new Vector3(1.5f, 0f, 0f);
        _wall.transform.localScale = new Vector3(1f, 4f, 4f);
        Physics.SyncTransforms();
        _motion.Throw(new PlayerThrowIntent(new System.Numerics.Vector2(1f, 0f), 3f, 0.25f));
        Walk(0.25f);
        Assert.That(_motion.IsActive, Is.False);
        Assert.That(_player.transform.position.x, Is.LessThan(1f - 0.4f), "It went through the wall.");
        Assert.That(_player.transform.position.x, Is.GreaterThan(0.2f), "It never moved.");
    }

    [Test]
    public void Throw_DoesNotTurnTheCharacter()
    {
        _player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        _motion.Throw(new PlayerThrowIntent(new System.Numerics.Vector2(0f, -1f), 1f, 0.25f));
        Walk(0.25f);
        Assert.That(Quaternion.Angle(_player.transform.rotation, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(0.01f));
    }

    /// <remarks>
    /// The sweep is made live and fatal: every layer, and no <c>EnemyViews</c> to resolve the
    /// capsule's own collider through, so a sweep throws. The dash is the control that it does.
    /// </remarks>
    [Test]
    public void Throw_SweepsNobody()
    {
        typeof(ChargeMotion).GetField("_enemyLayer", Private)!.SetValue(_motion, (LayerMask)~0);
        var session = (RunSession)FormatterServices.GetUninitializedObject(typeof(RunSession));

        _motion.Begin(new ChargeIntent(new System.Numerics.Vector2(1f, 0f), 1f, 0.25f), session);
        Assert.Throws<NullReferenceException>(() => _motion.Step(Step), "The control's sweep never ran.");

        _motion.Throw(new PlayerThrowIntent(new System.Numerics.Vector2(1f, 0f), 1f, 0.25f));
        Assert.DoesNotThrow(() => Walk(0.25f));
    }

    [Test]
    public void Throw_ReplacedByCharge()
    {
        _motion.Throw(new PlayerThrowIntent(new System.Numerics.Vector2(1f, 0f), 2f, 0.25f));
        Walk(Step);
        var session = (RunSession)FormatterServices.GetUninitializedObject(typeof(RunSession));
        _motion.Begin(new ChargeIntent(new System.Numerics.Vector2(0f, 1f), 1f, 0.25f), session);
        Vector3 from = _player.transform.position;
        Walk(0.25f);
        Vector3 moved = _player.transform.position - from;
        Assert.That(moved.x, Is.EqualTo(0f).Within(0.02f));
        Assert.That(moved.z, Is.EqualTo(1f).Within(0.02f));
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
