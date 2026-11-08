using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEngine;
using static Soulvail.Tests.Game.Authoring.PackleaderPathTests;

namespace Soulvail.Tests.Game.Views;

public sealed class StampedeViewTests
{
    private DomainEventHub _hub;
    private StampedeView _view;
    private float _now;
    private LineRenderer _strip;
    private Transform[] _boars;

    [SetUp]
    public void SetUp()
    {
        _now = 0f;
        _hub = new DomainEventHub();
        _view = new GameObject("Stampede").AddComponent<StampedeView>();
        _view.Bind(_hub, Load<GameObject>("Prefabs/Pets/Boar.prefab"),
            Load<GameObject>("Prefabs/Vfx/StampedeDust.prefab").GetComponent<ParticleSystem>(),
            Load<Material>("Materials/M_Killshot.mat"), () => _now, _ => Vector3.right, null);
        _strip = _view.GetComponentInChildren<LineRenderer>();
        _boars = _view.transform.Find("Boar pool").Cast<Transform>().ToArray();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_view.gameObject);
        _hub.Dispose();
    }

    [Test]
    public void Started_ShowsCyanStrip_PointThree()
    {
        Start();
        Assert.That(_strip.enabled, Is.True);
        Color color = _strip.startColor;
        Assert.That((color.r, color.g, color.b), Is.EqualTo((Palette.Player.r, Palette.Player.g, Palette.Player.b)));
        Assert.That(_strip.widthMultiplier, Is.EqualTo(3f));
        Assert.That(_strip.GetPosition(1) - _strip.GetPosition(0), Is.EqualTo(Vector3.forward * StampedeSystem.Length));
        _view.Step(0.299f);
        Assert.That(_strip.enabled, Is.True);
        _view.Step(0.3f);
        Assert.That(_strip.enabled, Is.False);
    }

    [Test]
    public void Boars_SpacedAndOnLine()
    {
        _now = 10f;
        Start();
        Assert.That(_boars.Length, Is.EqualTo(12));
        Assert.That(ActiveBoars(), Is.EqualTo(1));
        Assert.That(_boars[0].position, Is.EqualTo(Vector3.right));
        _view.Step(_now + StampedeSystem.Spacing - 0.01f);
        Assert.That(ActiveBoars(), Is.EqualTo(1));
        _view.Step(_now + StampedeSystem.Spacing + 0.05f);
        Assert.That(ActiveBoars(), Is.EqualTo(2));
        Assert.That(_boars[1].position.x, Is.EqualTo(1f));
        Assert.That(_boars[1].position.z, Is.EqualTo(0.05f * StampedeSystem.BoarSpeed).Within(0.001f));
        _view.Step(_now + StampedeSystem.Length / StampedeSystem.BoarSpeed + 0.01f);
        Assert.That(_boars[0].gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Hit_DustBurst()
    {
        Start();
        var dust = _view.GetComponentInChildren<ParticleSystem>();
        int before = dust.particleCount;
        _hub.Publish(new BoarHit(1, 1f));
        Assert.That(dust.particleCount, Is.GreaterThan(before));
        Color color = dust.main.startColor.color;
        Assert.That((color.r, color.g, color.b), Is.EqualTo((Palette.Player.r, Palette.Player.g, Palette.Player.b)));
    }

    [Test]
    public void Ended_ReturnsAll()
    {
        Start();
        _view.Step(0.5f);
        Assert.That(ActiveBoars(), Is.GreaterThan(1));
        _hub.Publish(new StampedeEnded());
        Assert.That(ActiveBoars(), Is.Zero);

        _now = 1f;
        Start();
        _hub.Publish(new StageArrived(2, default));
        Assert.That(ActiveBoars(), Is.Zero);
        Assert.That(_strip.enabled, Is.False);
        Assert.That(_view.GetComponentInChildren<ParticleSystem>().particleCount, Is.Zero);
    }

    private int ActiveBoars() => _boars.Count(boar => boar.gameObject.activeSelf);

    private void Start() => _hub.Publish(new StampedeStarted(Vector3.right.ToNum(),
        Vector3.forward.ToNum(), StampedeSystem.Length, 6, 1.5f));
}
