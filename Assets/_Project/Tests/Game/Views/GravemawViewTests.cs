using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Num = System.Numerics;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-25f rules 1–6: <see cref="GravemawView"/> on the real <c>Gravemaw.prefab</c>.</summary>
[TestFixture]
public sealed class GravemawViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Gravemaw.prefab";
    private const int Id = 25;

    private static readonly ContentId Boss = new("enemy.gravemaw");

    private GameObject _body;
    private GameObject _camera;
    private DomainEventHub _hub;
    private GravemawView _view;
    private EnemyAnimatorView _animatorView;
    private Animator _animator;
    private CameraShake _shake;

    [SetUp]
    public void Rent()
    {
        _body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _animator = _body.GetComponentInChildren<Animator>();
        _animator.Rebind();
        _animator.Update(0f);
        _hub = new DomainEventHub();
        _camera = new GameObject("Shake");
        _shake = _camera.AddComponent<CameraShake>();
        _view = _body.GetComponent<GravemawView>();
        _view.Construct(_hub, _shake);
        _animatorView = _body.GetComponentInChildren<EnemyAnimatorView>();
        _animatorView.Construct(_hub);
        _body.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
    }

    [TearDown]
    public void Return()
    {
        _hub.Dispose();
        Object.DestroyImmediate(_body);
        Object.DestroyImmediate(_camera);
    }

    [Test]
    public void View_Burrowed_HidesModel_PlaysTrail()
    {
        Assert.That(_view.IsBuried, Is.False);
        Assert.That(ModelDrawn(), Is.True);
        Assert.That(Mound().activeSelf, Is.False);

        _hub.Publish(new GravemawBurrowed(Id + 1));
        Assert.That(_view.IsBuried, Is.False, "Another boss's burrow.");

        // A body still up dives first, then is gone.
        _hub.Publish(new GravemawBurrowed(Id));
        Assert.That(_view.IsBuried, Is.True);
        Assert.That(_view.IsTrailPlaying, Is.True);
        Assert.That(Mound().activeSelf, Is.True, "The mound follows the buried body.");
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Dive"), Is.True);
        _view.Step(GravemawView.DiveClipSeconds);
        Assert.That(ModelDrawn(), Is.False, "Hidden once under.");
        Assert.That(_view.IsModelShown, Is.False);

        // Untargetable is core's; no hit flash shows on a model that is not drawn.
        _hub.Publish(new EnemyDamaged(Id, 5f, 0.5f, false));
        Assert.That(ModelDrawn(), Is.False, "No flash from underground.");

        // After the window's dive has played out, the burrow hides at once.
        _hub.Publish(new GravemawErupted(Id, Num.Vector3.Zero, 3f, false));
        _hub.Publish(new GravemawWindow(Id, true, 1f));
        _hub.Publish(new GravemawWindow(Id, false, 0f));
        _view.Step(GravemawView.DiveClipSeconds);
        _hub.Publish(new GravemawBurrowed(Id));
        Assert.That(ModelDrawn(), Is.False);
    }

    [Test]
    public void View_RingArmed_PlacesAtCentre_RemovedOnEruption()
    {
        _body.transform.localScale = Vector3.one * 3f;
        var at = new Num.Vector3(3f, 0f, 4f);

        _hub.Publish(new GravemawRingArmed(Id + 1, at, 3.5f, 1f));
        Assert.That(_view.RingCount, Is.Zero, "Another boss's ring.");

        _hub.Publish(new GravemawRingArmed(Id, at, 3.5f, 1f));
        Assert.That(_view.RingCount, Is.EqualTo(1));
        GameObject ring = _view.Ring(new Vector3(3f, 0f, 4f));
        Assert.That(ring.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(ring.transform.position.z, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(ring.transform.Find("Rim").lossyScale.x, Is.EqualTo(7f).Within(1e-3f), "Radius in world metres.");
        Assert.That(_view.RingFill(new Vector3(3f, 0f, 4f)), Is.Zero);

        // The body tunnels toward it; the centre never moves.
        _body.transform.position = new Vector3(1f, 0f, 2f);
        _view.Step(0.5f);
        Assert.That(ring.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(_view.RingFill(new Vector3(3f, 0f, 4f)), Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(ring.transform.Find("Fill").lossyScale.x, Is.EqualTo(3.5f).Within(1e-3f));

        Color fill = BaseColour(ring.transform.Find("Fill").GetComponent<Renderer>());
        Assert.That(new[] { fill.r, fill.g, fill.b }, Is.EqualTo(new[] { Palette.Danger.r, Palette.Danger.g, Palette.Danger.b }).Within(1e-3f),
            "A telegraph, in Danger, never cyan.");

        _hub.Publish(new GravemawErupted(Id, at, 3.5f, true));
        Assert.That(_view.RingCount, Is.Zero);
        Assert.That(_body.transform.Find("Live").childCount, Is.Zero);
    }

    [Test]
    public void View_Erupted_ShowsModel_StopsTrail()
    {
        _hub.Publish(new GravemawBurrowed(Id));
        _view.Step(GravemawView.DiveClipSeconds);
        Assume.That(ModelDrawn(), Is.False);
        Assume.That(_shake.IsShaking, Is.False);

        _hub.Publish(new GravemawErupted(Id, Num.Vector3.Zero, 3f, false));
        Assert.That(_view.IsBuried, Is.False);
        Assert.That(ModelDrawn(), Is.True);
        Assert.That(_view.IsTrailPlaying, Is.False);
        Assert.That(Mound().activeSelf, Is.False);
        Assert.That(_shake.IsShaking, Is.True, "The rise thuds.");
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Rise"), Is.True, "Rises on a miss too.");

        _hub.Publish(new GravemawBurrowed(Id));
        _view.Step(GravemawView.DiveClipSeconds);
        _hub.Publish(new GravemawErupted(Id, Num.Vector3.Zero, 3f, true));
        Settle();
        Assert.That(ModelDrawn(), Is.True);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Rise"), Is.True, "And on a bite.");
    }

    [Test]
    public void View_WindowOpen_LightsMaw_CloseDives()
    {
        Renderer maw = Model("Maw");
        Renderer body = Model("Body");
        Assert.That(Emission(maw).maxColorComponent, Is.Zero);

        _hub.Publish(new GravemawWindow(Id + 1, true, 2f));
        Assert.That(_view.IsMawLit, Is.False, "Another boss's window.");

        _hub.Publish(new GravemawWindow(Id, true, 2f));
        Assert.That(_view.IsMawLit, Is.True);
        Color lit = Emission(maw);
        Assert.That(lit.maxColorComponent, Is.GreaterThan(1f), "Glows.");
        Color.RGBToHSV(lit, out float hue, out float saturation, out _);
        Assert.That(hue * 360f, Is.LessThan(70f), $"{lit} is warm.");
        Assert.That(hue * 360f >= 175f && hue * 360f <= 200f && saturation > 0.35f, Is.False, "Never cyan.");
        Assert.That(Emission(body).maxColorComponent, Is.GreaterThan(0f).And.LessThan(lit.maxColorComponent), "A faint rim.");

        _view.Step(1.9f);
        Assert.That(_view.IsMawLit, Is.True);
        _view.Step(0.2f);
        Assert.That(_view.IsMawLit, Is.False, "Out after its seconds.");
        Assert.That(Emission(maw), Is.EqualTo(Color.black));
        Assert.That(Emission(body), Is.EqualTo(Color.black));

        _hub.Publish(new GravemawWindow(Id, true, 2f));
        _hub.Publish(new GravemawWindow(Id, false, 0f));
        Assert.That(_view.IsMawLit, Is.False, "Closing puts it out.");
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Dive"), Is.True, "Closing dives.");
    }

    [Test]
    public void View_TelegraphKinds_PlayClips()
    {
        Assert.That(_animator.applyRootMotion, Is.False);
        Assert.That(_animator.runtimeAnimatorController.name, Is.EqualTo("AC_Gravemaw"));
        Assert.That(_animator.runtimeAnimatorController.animationClips.All(c => c.events.Length == 0), Is.True,
            "No clip carries an Animation Event (AR §18.2).");

        (int Move, string State)[] moves =
        {
            (GravemawBehaviour.TremorMove, "Tremor"),
            (GravemawBehaviour.SpitMove, "Spit"),
        };

        foreach ((int move, string state) in moves)
        {
            _hub.Publish(new EnemyTelegraph(Id, 0.8f, move));
            Settle();
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, $"Kind {move} plays {state}.");
            Assert.That(_animator.GetCurrentAnimatorClipInfo(0)[0].clip.name, Is.EqualTo(state));
        }

        _hub.Publish(new EnemyTelegraph(Id + 1, 0.8f, GravemawBehaviour.TremorMove));
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Spit"), Is.True, "Another body's wind-up is not this one's.");

        // The eruption's wind-up arrives underground: consumed, no move clip, no trigger left armed.
        _hub.Publish(new EnemyTelegraph(Id, 1f, GravemawBehaviour.EruptionMove));
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
        Assert.That(_animator.GetBool("Attack"), Is.False);
    }

    [Test]
    public void View_Crack_SpawnsForSeconds()
    {
        _hub.Publish(new GravemawCrack(Id + 1, new Num.Vector3(2f, 0f, -1f), 1.5f));
        Assert.That(_view.CrackCount, Is.Zero, "Another boss's crack.");

        _hub.Publish(new GravemawCrack(Id, new Num.Vector3(2f, 0f, -1f), 1.5f));
        _hub.Publish(new GravemawCrack(Id, new Num.Vector3(-3f, 0f, 2f), 1.5f));
        Assert.That(_view.CrackCount, Is.EqualTo(2));
        GameObject crack = _view.Crack(new Vector3(2f, 0f, -1f));
        Assert.That(crack.transform.position.x, Is.EqualTo(2f).Within(1e-4f));
        Assert.That(crack.transform.position.z, Is.EqualTo(-1f).Within(1e-4f));
        Assert.That(crack.GetComponentInChildren<ParticleSystem>(true), Is.Not.Null, "Dust.");

        _view.Step(1.4f);
        Assert.That(_view.CrackCount, Is.EqualTo(2));
        _view.Step(0.2f);
        Assert.That(_view.CrackCount, Is.Zero, "Gone when the Frog is out.");

        _hub.Publish(new GravemawCrack(Id, new Num.Vector3(1f, 0f, 1f), 1f));
        Assert.That(_view.Crack(new Vector3(1f, 0f, 1f)), Is.SameAs(crack), "Pooled: the same body comes back.");
    }

    [Test]
    public void View_Despawn_ResetsAll()
    {
        _hub.Publish(new GravemawRingArmed(Id, new Num.Vector3(3f, 0f, 4f), 3f, 1f));
        _hub.Publish(new GravemawCrack(Id, new Num.Vector3(2f, 0f, -1f), 2f));
        _hub.Publish(new GravemawBurrowed(Id));
        _view.Step(GravemawView.DiveClipSeconds);
        Assume.That(ModelDrawn(), Is.False);

        _view.OnDespawn();
        _animatorView.Forget();
        _animator.Update(0f);

        Assert.That(_view.IsBuried, Is.False);
        Assert.That(ModelDrawn(), Is.True);
        Assert.That(_view.IsTrailPlaying, Is.False);
        Assert.That(Mound().activeSelf, Is.False);
        Assert.That(_view.IsMawLit, Is.False);
        Assert.That(Emission(Model("Maw")), Is.EqualTo(Color.black));
        Assert.That(_view.RingCount, Is.Zero);
        Assert.That(_view.CrackCount, Is.Zero);
        Assert.That(_body.transform.Find("Live").childCount, Is.Zero, "Nothing left shown.");
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);

        // A window lit, then the boss dies: everything it showed goes with it.
        _hub.Publish(new GravemawErupted(Id, Num.Vector3.Zero, 3f, false));
        _hub.Publish(new GravemawWindow(Id, true, 3f));
        _hub.Publish(new GravemawRingArmed(Id, new Num.Vector3(3f, 0f, 4f), 3f, 1f));
        _hub.Publish(new EnemyDied(Id, Boss, Num.Vector3.Zero));
        Assert.That(_view.IsMawLit, Is.False);
        Assert.That(_view.RingCount, Is.Zero);
    }

    /// <summary>One update starts a transition; the next carries it past its 0.05–0.1 s blend.</summary>
    private void Settle()
    {
        _animator.Update(0f);
        _animator.Update(0.12f);
    }

    private bool ModelDrawn()
    {
        Renderer[] renderers = _body.transform.Find("Model").GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers, Is.Not.Empty);
        bool first = renderers[0].enabled;
        Assert.That(renderers.All(r => r.enabled == first), Is.True, "Body and maw hide together.");
        return first;
    }

    private GameObject Mound() => _body.transform.Find("Trail/Mound").gameObject;

    private Renderer Model(string part) => _body.transform.Find("Model/" + part).GetComponent<Renderer>();

    private static Color Emission(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        return block.GetColor("_EmissionColor");
    }

    private static Color BaseColour(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        return block.GetColor("_BaseColor");
    }
}
