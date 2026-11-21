using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Heading = System.Numerics.Vector2;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class MatriarchViewTests
{
    private const int Id = 7;
    private static readonly Point Origin = new(1f, 0f, 2f);
    private static readonly Point Target = new(7f, 0f, 2f);
    private DomainEventHub _events;
    private GameObject _instance;
    private GameObject _camera;
    private CameraShake _shake;
    private MatriarchView _view;
    private EnemyView _body;
    private Transform _model;
    private MeshFilter _warning;
    private MeshFilter _fill;
    private AudioSource _audio;
    private Renderer _shadow;
    private Renderer[] _bodyRenderers;
    private Canvas _healthBar;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/CarrionMatriarch.prefab");
        Assert.That(prefab, Is.Not.Null);
        _instance = Object.Instantiate(prefab);
        _camera = new GameObject("Matriarch shake", typeof(CameraShake));
        _shake = _camera.GetComponent<CameraShake>();
        _events = new DomainEventHub();
        _view = _instance.GetComponent<MatriarchView>();
        _body = _instance.GetComponent<EnemyView>();
        _model = _instance.transform.Find("Model");
        _bodyRenderers = _model.GetComponentsInChildren<Renderer>(true);
        _healthBar = _instance.transform.Find("HealthBar").GetComponent<Canvas>();
        _warning = _instance.transform.Find("Locked Footprint").GetComponent<MeshFilter>();
        _fill = _instance.transform.Find("Dive Fill").GetComponent<MeshFilter>();
        _shadow = _instance.transform.Find("FX_Vulture_Shadow").GetComponent<Renderer>();
        _audio = _instance.GetComponent<AudioSource>();
        _view.Construct(_events);
        _body.Bind(Id, new Vector3(Origin.X, Origin.Y, Origin.Z));
        _view.Bind(Id, _shake);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_instance);
        Object.DestroyImmediate(_camera);
        _events?.Dispose();
    }

    [Test]
    public void Warnings_MatchLockedFootprintsAndDiveFill()
    {
        var cues = new HashSet<AudioClip>();
        foreach (MatriarchMove move in new[] { MatriarchMove.RuinShove, MatriarchMove.FeatherSpear })
        {
            Arm(move);
            Assert.That(cues.Add(_audio.clip), Is.True);
            Assert.That(_warning.gameObject.activeSelf, Is.True);
            Vector3 start = _warning.transform.TransformPoint(new Vector3(0f, 0f, 0f));
            Vector3 end = _warning.transform.TransformPoint(new Vector3(0f, 0f, 1f));
            Assert.That(start.x, Is.EqualTo(Origin.X).Within(0.001f));
            Assert.That(end.x, Is.EqualTo(Target.X).Within(0.001f));
            Assert.That(end.z, Is.EqualTo(Target.Z).Within(0.001f));
            Assert.That(_warning.transform.lossyScale.x, Is.EqualTo(2f).Within(0.001f));
            _instance.transform.SetPositionAndRotation(new Vector3(2f, 0f, -5f), Quaternion.Euler(0f, 40f, 0f));
            _view.Step(0.1f);
            Assert.That(Vector3.Distance(_warning.transform.position, start), Is.LessThan(0.001f));
            Assert.That(Vector3.Angle(_warning.transform.forward, Vector3.right), Is.LessThan(0.01f));
        }

        Arm(MatriarchMove.CarrionPounce);
        Assert.That(cues.Add(_audio.clip), Is.True);
        Assert.That(_warning.sharedMesh.bounds.size.x, Is.EqualTo(2f).Within(0.001f));
        Assert.That(_warning.transform.position.x, Is.EqualTo(Target.X).Within(0.001f));
        Assert.That(_warning.transform.position.z, Is.EqualTo(Target.Z).Within(0.001f));
        Assert.That(_warning.transform.lossyScale.x, Is.EqualTo(3f).Within(0.001f));
        Assert.That(_fill.gameObject.activeSelf, Is.False);

        Arm(MatriarchMove.CarrionDive);
        Assert.That(cues.Add(_audio.clip), Is.True);
        Assert.That(cues, Does.Not.Contain(null));
        Assert.That(_warning.gameObject.activeSelf, Is.True);
        Assert.That(_fill.gameObject.activeSelf, Is.False);
        Progress(-0.5f);
        Assert.That(_fill.gameObject.activeSelf, Is.False);
        Progress(0.5f);
        Assert.That(_fill.gameObject.activeSelf, Is.True);
        Assert.That(_fill.transform.position.x, Is.EqualTo(Target.X).Within(0.001f));
        Assert.That(_fill.transform.position.z, Is.EqualTo(Target.Z).Within(0.001f));
        Assert.That(_fill.transform.lossyScale.x, Is.EqualTo(1.5f).Within(0.001f));
        Progress(2f);
        Assert.That(_fill.transform.lossyScale.x, Is.EqualTo(3f).Within(0.001f));
        Assert.That(_warning.transform.lossyScale.x, Is.EqualTo(3f).Within(0.001f));
    }

    [Test]
    public void Flight_ShadowTracksHeadingAndLockedDiveUntilBodyReturns()
    {
        ApplyFacing(15f);
        Arm(MatriarchMove.RuinShove);
        AssertFacing(15f);
        Impact(MatriarchMove.RuinShove);
        AssertFacing(15f);
        Assert.That(_instance.transform.Find("FX_DuneclawSlab").gameObject.activeSelf, Is.True);

        Arm(MatriarchMove.CarrionPounce);
        Progress(0.5f);
        Assert.That(_model.position.y, Is.GreaterThan(0f));
        AssertBodyVisible(true);
        Assert.That(_shadow.enabled, Is.True);
        AssertFacing(15f);
        Impact(MatriarchMove.CarrionPounce);
        AssertFacing(15f);
        ApplyFacing(25f);
        AssertFacing(25f);

        _events.Publish(new MatriarchFlightChanged(Id, true));
        AssertBodyVisible(false);
        Assert.That(_shadow.enabled, Is.True);
        Arm(MatriarchMove.FeatherSpear);
        var flightPosition = new Point(2f, 0f, 4f);
        ApplyFacing(60f, flightPosition);
        AssertShadowAt(flightPosition);
        Assert.That(Quaternion.Angle(_shadow.transform.rotation, Quaternion.Euler(0f, 60f, 0f)), Is.LessThan(0.05f));
        AssertBodyVisible(false);
        _events.Publish(new MatriarchTellCleared(Id));
        Assert.That(_shadow.enabled, Is.True);

        Arm(MatriarchMove.CarrionDive, flightPosition);
        Progress(0.5f);
        AssertShadowAt(Point.Lerp(flightPosition, Target, 0.5f));
        Progress(1f);
        AssertShadowAt(Target);
        Assert.That(_shadow.enabled, Is.True);
        AssertBodyVisible(false);
        _events.Publish(new MatriarchFlightChanged(Id, false));
        Impact(MatriarchMove.CarrionDive);
        Assert.That(Vector3.Distance(_model.position, Target.ToUnity()), Is.LessThan(0.001f));
        ApplyFacing(60f, Target);
        AssertBodyVisible(true);
        Assert.That(_model.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(_shadow.enabled, Is.False);
        AssertFacing(60f);
        ApplyFacing(30f);
        AssertFacing(30f);
    }

    [Test]
    public void Landings_UseGravemawSlamOnHitOrMiss()
    {
        Arm(MatriarchMove.CarrionPounce);
        Impact(MatriarchMove.CarrionPounce, Id + 1);
        _events.Publish(new MatriarchTellCleared(Id));
        Impact(MatriarchMove.RuinShove);
        Impact(MatriarchMove.FeatherSpear);
        Assert.That(_shake.IsShaking, Is.False);

        // Core emits the same impact on a hit and on a dodge. No damage event is needed.
        foreach (MatriarchMove move in new[] { MatriarchMove.CarrionPounce, MatriarchMove.CarrionDive })
        {
            Arm(move);
            Impact(move);
            Assert.That(_shake.Offset.y, Is.EqualTo(-CameraShake.SlamStrength));
            _shake.Step(CameraShake.Seconds * 0.25f);
            Vector3 offset = _shake.Offset;
            _events.Publish(new MatriarchTellCleared(Id));
            Assert.That(_shake.Offset, Is.EqualTo(offset));
            _shake.Step(CameraShake.Seconds);
        }
    }

    [Test]
    public void Pool_ClearsVisualsAndSubscriptions()
    {
        ApplyFacing(35f);
        Arm(MatriarchMove.CarrionPounce);
        Progress(0.5f);
        _events.Publish(new MatriarchTellCleared(Id));
        AssertGrounded();
        _events.Publish(new MatriarchFlightChanged(Id, true));
        _view.Step(0.35f);
        Arm(MatriarchMove.CarrionDive);
        Progress(0.4f);
        Assert.That(_fill.gameObject.activeSelf, Is.True);

        _view.Unbind();
        AssertGrounded();
        Assert.That(_events.SubscriberCount<MatriarchImpact>(), Is.Zero);
        Impact(MatriarchMove.CarrionDive);
        Assert.That(_shake.IsShaking, Is.False);
        _body.Bind(Id + 1, Vector3.zero);
        _view.Bind(Id + 1, _shake);
        _view.Bind(Id + 1, _shake);
        AssertGrounded();
        AssertFacing(35f);
        Arm(MatriarchMove.RuinShove);
        Assert.That(_warning.gameObject.activeSelf, Is.False);
        Assert.That(_events.SubscriberCount<MatriarchArmed>(), Is.EqualTo(1));
        Assert.That(_events.SubscriberCount<MatriarchImpact>(), Is.EqualTo(1));
    }

    private void AssertGrounded()
    {
        AssertBodyVisible(true);
        Assert.That(_model.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(_shadow.enabled, Is.False);
        Assert.That(_warning.gameObject.activeSelf, Is.False);
        Assert.That(_fill.gameObject.activeSelf, Is.False);
        Assert.That(_audio.clip, Is.Null);
    }

    private void AssertBodyVisible(bool visible)
    {
        Assert.That(_healthBar.enabled, Is.EqualTo(visible));
        Assert.That(_bodyRenderers, Is.Not.Empty);
        foreach (Renderer renderer in _bodyRenderers)
        {
            Assert.That(renderer.forceRenderingOff, Is.EqualTo(!visible), renderer.name);
        }
    }

    private void AssertShadowAt(Point ground)
    {
        Assert.That(Vector3.Distance(_shadow.transform.position, ground.ToUnity() + Vector3.up * 0.03f),
            Is.LessThan(0.001f));
    }

    private void ApplyFacing(float yaw, Point? position = null)
    {
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        _body.Apply(new EnemyMoveIntent(Id, Point.Zero, new Heading(forward.x, forward.z), position ?? Origin), 0.02f);
        _view.Step(0.02f);
    }

    private void AssertFacing(float yaw)
    {
        Assert.That(Quaternion.Angle(_model.rotation, Quaternion.Euler(0f, yaw, 0f)), Is.LessThan(0.05f));
    }

    private void Arm(MatriarchMove move, Point? origin = null) =>
        _events.Publish(new MatriarchArmed(Id, move, origin ?? Origin, Target, 2f, 3f, 1.5f));

    private void Progress(float fraction) => _events.Publish(new MatriarchTellProgress(Id, fraction));

    private void Impact(MatriarchMove move, int id = Id) => _events.Publish(new MatriarchImpact(id, move, Target));
}
