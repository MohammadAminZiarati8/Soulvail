using System;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Direction = System.Numerics.Vector2;
using Point = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class StormRunViewTests
{
    private DomainEventHub _hub;
    private StormRunView _view;
    private ParticleSystem _aura;
    private ParticleSystem _trail;
    private ParticleSystem _burst;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        var prefab = AssetDatabase.LoadAssetAtPath<StormRunView>("Assets/_Project/Prefabs/Vfx/VFX_StormRun.prefab");
        Assert.That(prefab, Is.Not.Null);
        _view = Object.Instantiate(prefab);
        _view.Construct(_hub);
        _aura = _view.transform.Find("Body Wind").GetComponent<ParticleSystem>();
        _trail = _view.transform.Find("Roll Streaks").GetComponent<ParticleSystem>();
        _burst = _view.transform.Find("Volley Ring").GetComponent<ParticleSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_view != null)
        {
            Object.DestroyImmediate(_view.gameObject);
        }

        _hub.Dispose();
    }

    [Test]
    public void View_AuraFollowsState()
    {
        Assert.That(_aura.isPlaying, Is.False);
        Assert.That(_trail.isPlaying, Is.False);
        _hub.Publish(new StormRunChanged(true, 6f));
        Assert.That(_aura.isPlaying, Is.True);
        Assert.That(_aura.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
        Assert.That(_trail.isPlaying, Is.False);
        _hub.Publish(new ChargeStarted(Direction.UnitX));
        Assert.That(_trail.isPlaying, Is.True);
        Assert.That(_trail.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
        Assert.That(Vector3.Dot(_trail.transform.forward, Vector3.left), Is.GreaterThan(0.99f));
        _hub.Publish(new StormRunChanged(false, 6f));
        Assert.That(_aura.isEmitting, Is.False);
        Assert.That(_trail.isEmitting, Is.False);
    }

    [Test]
    public void View_BurstOnRollEnd_OnlyWhileActive()
    {
        _hub.Publish(new ChargeEnded());
        Assert.That(_burst.particleCount, Is.Zero);

        var feet = new Vector3(4f, 0f, -1f);
        _view.transform.position = feet;
        _hub.Publish(new StormRunChanged(true, 6f));
        _hub.Publish(new ChargeStarted(Direction.UnitX));
        Assert.That(_burst.particleCount, Is.Zero);
        _hub.Publish(new ChargeEnded());

        var particles = new ParticleSystem.Particle[_burst.main.maxParticles];
        int count = _burst.GetParticles(particles);
        Assert.That(count, Is.GreaterThan(16));
        Assert.That(_burst.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
        Assert.That(_trail.isEmitting, Is.False);
        Vector3 centre = feet + Vector3.up * 0.06f;
        for (int i = 0; i < count; i++)
        {
            ParticleSystem.Particle particle = particles[i];
            Vector3 offset = particle.position - centre;
            Assert.That(offset.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(offset.magnitude, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(Vector3.Dot(offset, particle.velocity), Is.GreaterThan(0f));
            Assert.That(particle.startLifetime, Is.EqualTo(0.2f));
        }

        _view.transform.position += Vector3.right;
        _burst.Simulate(0.1f, withChildren: false, restart: false, fixedTimeStep: false);
        Assert.That(_burst.GetParticles(particles), Is.EqualTo(count));
        Assert.That((particles[0].position - centre).magnitude, Is.GreaterThan(0.45f));
        _burst.Simulate(0.15f, withChildren: false, restart: false, fixedTimeStep: false);
        Assert.That(_burst.particleCount, Is.Zero);
        _hub.Publish(new StormRunChanged(false, 6f));
        _hub.Publish(new ChargeEnded());
        Assert.That(_burst.particleCount, Is.Zero);
    }

    [Test]
    public void View_Pooled_ClearsOnBoundary()
    {
        Action[] boundaries =
        {
            () => _hub.Publish(new StageCleared(1, Point.Zero, default)),
            () => _hub.Publish(new StageTransitionStarted(2, 1f)),
            () => _hub.Publish(new PlayerDied(1f)),
        };
        ParticleSystem[] pool = _view.GetComponentsInChildren<ParticleSystem>();
        foreach (Action boundary in boundaries)
        {
            _hub.Publish(new StormRunChanged(true, 6f));
            _hub.Publish(new ChargeStarted(Direction.UnitX));
            _hub.Publish(new ChargeEnded());
            Assert.That(_burst.particleCount, Is.GreaterThan(0));

            boundary();
            _hub.Publish(new ChargeEnded());
            Assert.That(_view.GetComponentsInChildren<ParticleSystem>(), Is.EqualTo(pool));
            foreach (ParticleSystem system in pool)
            {
                Assert.That(system.isPlaying, Is.False, system.name);
                Assert.That(system.particleCount, Is.Zero, system.name);
            }
        }
    }
}
