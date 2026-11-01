using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Num = System.Numerics;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-15f rules 2–6: <see cref="WarlordView"/> on the real <c>Mossguard_Warlord.prefab</c>.</summary>
[TestFixture]
public sealed class WarlordViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Mossguard_Warlord.prefab";
    private const int Id = 15;

    private GameObject _body;
    private DomainEventHub _hub;
    private WarlordView _view;
    private Animator _animator;

    [SetUp]
    public void Rent()
    {
        _body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _animator = _body.GetComponentInChildren<Animator>();
        _animator.Rebind();
        _animator.Update(0f);
        _hub = new DomainEventHub();
        _view = _body.GetComponent<WarlordView>();
        _view.Construct(_hub);
        _body.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
    }

    [TearDown]
    public void Return()
    {
        _hub.Dispose();
        Object.DestroyImmediate(_body);
    }

    [Test]
    public void WarlordView_WedgeFollowsGuardArc()
    {
        MeshFilter wedge = Wedge();
        Assert.That(wedge.transform.parent, Is.SameAs(_body.transform), "A child of the root turns with core's facing.");
        Assert.That(wedge.transform.localRotation, Is.EqualTo(Quaternion.identity));

        _hub.Publish(new WarlordGuardArc(Id, 60f));
        _view.Step(WarlordView.WedgeEaseSeconds * 0.5f);
        Assert.That(_view.WedgeHalfArcDeg, Is.EqualTo(30f).Within(0.01f), "Eased, not snapped.");
        _view.Step(WarlordView.WedgeEaseSeconds);
        Assert.That(_view.WedgeHalfArcDeg, Is.EqualTo(60f));
        Assert.That(wedge.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(EdgeDeg(wedge.sharedMesh), Is.EqualTo(60f).Within(0.1f));

        _hub.Publish(new WarlordGuardArc(Id, 90f));
        _view.Step(WarlordView.WedgeEaseSeconds + 0.01f);
        Assert.That(_view.WedgeHalfArcDeg, Is.EqualTo(90f), "A guard close by widens it.");
        Assert.That(EdgeDeg(wedge.sharedMesh), Is.EqualTo(90f).Within(0.1f));
    }

    [Test]
    public void WarlordView_ZeroHidesWedge()
    {
        _hub.Publish(new WarlordGuardArc(Id, 60f));
        _view.Step(0.2f);
        _hub.Publish(new WarlordGuardArc(Id, 0f));
        _view.Step(WarlordView.WedgeEaseSeconds + 0.01f);
        Assert.That(_view.WedgeHalfArcDeg, Is.Zero);
        Assert.That(Wedge().GetComponent<MeshRenderer>().enabled, Is.False);
    }

    [Test]
    public void WarlordView_DazePlaysAndEnds()
    {
        Transform motes = _body.transform.Find("DazeMotes");
        Assert.That(motes.childCount, Is.EqualTo(WarlordView.MoteCount));
        Assert.That(motes.gameObject.activeSelf, Is.False);

        _hub.Publish(new WarlordDazed(Id, new Num.Vector3(3f, 0f, 4f), 2f));
        Assert.That(_view.IsDazed, Is.True);
        Assert.That(_animator.GetBool("Dazed"), Is.True);
        Assert.That(motes.gameObject.activeSelf, Is.True);
        Assert.That(motes.position.y, Is.GreaterThan(1.5f), "Over the head.");
        Transform dust = _body.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Dust");
        Assert.That(new Vector2(dust.position.x, dust.position.z), Is.EqualTo(new Vector2(3f, 4f)));

        Quaternion before = motes.localRotation;
        _view.Step(1f);
        Assert.That(_view.IsDazed, Is.True);
        Assert.That(motes.localRotation, Is.Not.EqualTo(before), "The motes circle.");
        Assert.That(_animator.GetLayerWeight(1), Is.Zero, "The guard drops through a daze.");

        _view.Step(1.01f);
        Assert.That(_view.IsDazed, Is.False);
        Assert.That(_animator.GetBool("Dazed"), Is.False);
        Assert.That(motes.gameObject.activeSelf, Is.False);
        Assert.That(_animator.GetLayerWeight(1), Is.EqualTo(1f));
    }

    [Test]
    public void WarlordView_ShatterHidesShieldOnce_PlaysVfx()
    {
        GameObject shield = Shield();
        ParticleSystem shatter = Shatter();
        Assert.That(shatter.main.playOnAwake, Is.False);
        Assert.That(shatter.main.loop, Is.False);
        GameObject nested = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.Find("VFX_ShieldShatter").gameObject;
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(nested),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vfx/VFX_ShieldShatter.prefab")));

        Vector3 at = shield.transform.position;
        _hub.Publish(new WarlordShattered(Id));
        Assert.That(_view.IsShattered, Is.True);
        Assert.That(shield.activeSelf, Is.False);
        Assert.That(shatter.isPlaying, Is.True);
        Assert.That(shatter.transform.position, Is.EqualTo(at));
        _view.Step(0.1f);
        Assert.That(_animator.GetLayerWeight(1), Is.Zero, "No guard for the rest of the life.");

        shatter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _hub.Publish(new WarlordShattered(Id));
        Assert.That(shatter.isPlaying, Is.False, "Once.");
        _view.Step(5f);
        Assert.That(_animator.GetLayerWeight(1), Is.Zero);
    }

    [Test]
    public void WarlordView_NoCyan()
    {
        Color wedge = Palette.Vertex(Palette.Neutral);
        AssertNotCyan(wedge, "wedge");
        _hub.Publish(new WarlordGuardArc(Id, 60f));
        _view.Step(0.2f);
        foreach (Color colour in Wedge().sharedMesh.colors)
        {
            Assert.That(colour.a, Is.EqualTo(WarlordView.WedgeAlpha).Within(1e-4f));
            Assert.That((Vector3)(Vector4)colour, Is.EqualTo((Vector3)(Vector4)wedge));
        }

        foreach (ParticleSystem system in _body.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MinMaxGradient start = system.main.startColor;
            if (start.mode == ParticleSystemGradientMode.TwoColors)
            {
                AssertNotCyan(start.colorMin, system.name);
                AssertNotCyan(start.colorMax, system.name);
            }
            else
            {
                Assert.That(start.mode, Is.EqualTo(ParticleSystemGradientMode.Color), system.name);
                AssertNotCyan(start.color, system.name);
            }
        }

        foreach (Renderer mote in _body.transform.Find("DazeMotes").GetComponentsInChildren<Renderer>(true))
        {
            AssertNotCyan(mote.sharedMaterial.GetColor("_BaseColor"), mote.name);
        }
    }

    [Test]
    public void WarlordView_DespawnRestores()
    {
        _hub.Publish(new WarlordGuardArc(Id, 60f));
        _view.Step(0.2f);
        _hub.Publish(new WarlordDazed(Id, Num.Vector3.Zero, 2f));
        _hub.Publish(new WarlordShattered(Id));

        _view.OnDespawn();

        Assert.That(_view.IsShattered, Is.False);
        Assert.That(_view.IsDazed, Is.False);
        Assert.That(_view.WedgeHalfArcDeg, Is.Zero);
        Assert.That(Wedge().GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(Shield().activeSelf, Is.True);
        Assert.That(_body.transform.Find("DazeMotes").gameObject.activeSelf, Is.False);
        Assert.That(Shatter().isPlaying, Is.False);
        Assert.That(_animator.GetBool("Dazed"), Is.False);
        Assert.That(_animator.GetLayerWeight(1), Is.EqualTo(1f));
    }

    [Test]
    public void WarlordView_OtherIdIgnored()
    {
        _hub.Publish(new WarlordGuardArc(Id + 1, 60f));
        _hub.Publish(new WarlordDazed(Id + 1, Num.Vector3.Zero, 2f));
        _hub.Publish(new WarlordShattered(Id + 1));
        _view.Step(0.2f);

        Assert.That(_view.WedgeHalfArcDeg, Is.Zero);
        Assert.That(_view.IsDazed, Is.False);
        Assert.That(_view.IsShattered, Is.False);
        Assert.That(Shield().activeSelf, Is.True);
        Assert.That(_animator.GetLayerWeight(1), Is.EqualTo(1f));
    }

    private static void AssertNotCyan(Color colour, string what)
    {
        Color.RGBToHSV(colour, out float hue, out float saturation, out _);
        Assert.That(hue * 360f >= 175f && hue * 360f <= 200f && saturation > 0.35f, Is.False, $"{what}: {colour} is the player's cyan.");
    }

    /// <summary>The widest ray's angle off the body's forward, in degrees.</summary>
    private static float EdgeDeg(Mesh mesh) =>
        mesh.vertices.Skip(1).Max(v => Mathf.Abs(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg));

    private MeshFilter Wedge() => _body.transform.Find("GuardWedge").GetComponent<MeshFilter>();

    private GameObject Shield() => _body.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Shield").gameObject;

    private ParticleSystem Shatter() => _body.transform.Find("VFX_ShieldShatter").GetComponent<ParticleSystem>();
}
