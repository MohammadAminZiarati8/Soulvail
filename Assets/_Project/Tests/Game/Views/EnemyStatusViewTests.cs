using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>MK-06b: shipped body effects, animation, timed icons and pool lifecycle.</summary>
[TestFixture]
public sealed class EnemyStatusViewTests
{
    private const int Id = 1;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ContentId Rootling = new("enemy.frog");
    private DomainEventHub _hub;
    private IObjectResolver _container;
    private EnemyViews _views;
    private GameObject _parent;
    private EnemyView _body;
    private EnemyStatusView _status;
    private EnemyHealthBar _bar;
    private EnemyAnimatorView _animation;
    private Animator _animator;
    private RunSession _session;
    private RunRecorder _recorder;

    [SetUp]
    public void BuildPool()
    {
        _hub = new DomainEventHub();
        _session = StartedSession();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        builder.RegisterInstance<IRunSession>(_session);
        _container = builder.Build();
        _parent = new GameObject("Status pool test");
        _parent.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<EnemyView>("Assets/_Project/Prefabs/Enemies/Rootling.prefab");
        _views = new EnemyViews(_container, prefab, _parent.transform, _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()), prewarm: 1);
        Spawn(Id);
    }

    [TearDown]
    public void DestroyPool()
    {
        _views?.Dispose();
        _container?.Dispose();
        _hub?.Dispose();
        Object.DestroyImmediate(_parent);
    }

    [Test]
    public void Stun_ShowsSparks_HoldsAnimator()
    {
        Change(new ContentId(EnemyStatuses.StunnedKey), 3f);
        GameObject sparks = _status.Effect(new ContentId(EnemyStatuses.StunnedKey));
        Assert.That(sparks.activeSelf, Is.True);
        Assert.That(sparks.transform.childCount, Is.EqualTo(3));
        Quaternion before = sparks.transform.localRotation;
        _status.Step(0.2f);
        Assert.That(Quaternion.Angle(before, sparks.transform.localRotation), Is.GreaterThan(20f));
        Assert.That(_animator.speed, Is.EqualTo(0.1f));
    }

    [Test]
    public void Stun_End_Resumes()
    {
        Change(new ContentId(EnemyStatuses.StunnedKey), 3f);
        Change(new ContentId(EnemyStatuses.StunnedKey), 0f);
        Assert.That(_animator.speed, Is.EqualTo(1f));
        Assert.That(_status.Effect(new ContentId(EnemyStatuses.StunnedKey)).activeSelf, Is.False);
    }

    [Test]
    public void Root_ShowsCoils_ScaledToRadius()
    {
        _body.GetComponent<CapsuleCollider>().radius = 0.8f;
        _status.Bind(Id);
        Change(new ContentId(EnemyStatuses.RootedKey), 3f);
        GameObject coils = _status.Effect(new ContentId(EnemyStatuses.RootedKey));
        Assert.That(coils.activeSelf, Is.True);
        Assert.That(coils.transform.localScale.x, Is.EqualTo(1.6f));
        Assert.That(coils.GetComponent<LineRenderer>().positionCount, Is.GreaterThan(30));
        _animation.Step(0.1f);
        Assert.That(_animator.GetFloat("Speed"), Is.GreaterThan(0f), "Rooted legs keep animating.");
        Assert.That(_animator.speed, Is.EqualTo(1f));
        Change(new ContentId(EnemyStatuses.RootedKey), 0f);
        Assert.That(coils.activeSelf, Is.False);
    }

    [Test]
    public void Slow_ScalesAnimator()
    {
        Assert.That(_session.DebugEnemyStatus(Id, new ContentId(EnemyStatuses.SlowedKey)), Is.True);
        Assert.That(_status.Effect(new ContentId(EnemyStatuses.SlowedKey)).activeSelf, Is.True);
        Assert.That(_animator.speed, Is.EqualTo(0.5f));
        Change(new ContentId(EnemyStatuses.StunnedKey), 2f);
        Assert.That(_animator.speed, Is.EqualTo(0.1f));
        Change(new ContentId(EnemyStatuses.StunnedKey), 0f);
        Assert.That(_animator.speed, Is.EqualTo(0.5f));
        Change(new ContentId(EnemyStatuses.SlowedKey), 0f);
        Assert.That(_animator.speed, Is.EqualTo(1f));
        Assert.That(_status.Effect(new ContentId(EnemyStatuses.SlowedKey)).activeSelf, Is.False);
    }

    [Test]
    public void Bar_IconsInOrderWithSweep()
    {
        Change(new ContentId(EnemyStatuses.SlowedKey), 6f);
        Change(new ContentId(EnemyStatuses.RootedKey), 6f);
        Change(new ContentId(EnemyStatuses.StunnedKey), 6f);
        TickBar(1.5f);
        float lastX = float.NegativeInfinity;
        foreach (ContentId kind in new[] { new ContentId(EnemyStatuses.StunnedKey), new ContentId(EnemyStatuses.RootedKey), new ContentId(EnemyStatuses.SlowedKey) })
        {
            Image icon = _bar.StatusIcon(kind);
            Image sweep = _bar.StatusSweep(kind);
            Assert.That(icon.gameObject.activeSelf, Is.True);
            Assert.That(icon.sprite, Is.Not.Null);
            Assert.That(icon.rectTransform.anchoredPosition.x, Is.GreaterThan(lastX));
            lastX = icon.rectTransform.anchoredPosition.x;
            Assert.That(sweep.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(sweep.sprite, Is.Not.Null);
            Assert.That(sweep.fillAmount, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(icon.rectTransform.sizeDelta.x,
                Is.EqualTo(14f * Soulvail.Game.Controls.StickShaper.PixelsPerDp(Screen.dpi)).Within(0.01f));
        }
        Change(new ContentId(EnemyStatuses.RootedKey), 0f);
        Assert.That(_bar.StatusIcon(new ContentId(EnemyStatuses.RootedKey)).gameObject.activeSelf, Is.False);
        Assert.That(_bar.StatusIcon(new ContentId(EnemyStatuses.StunnedKey)).rectTransform.anchoredPosition.x,
            Is.LessThan(_bar.StatusIcon(new ContentId(EnemyStatuses.SlowedKey)).rectTransform.anchoredPosition.x));
    }

    [Test]
    public void Bar_AppearsWhileStatused()
    {
        Assert.That(_bar.IsShown, Is.False);
        Change(new ContentId(EnemyStatuses.RootedKey), 3f);
        Assert.That(_bar.IsShown, Is.True);
        TickBar(4f);
        Assert.That(_bar.IsShown, Is.True, "Only core's end event hides a status.");
        Assert.That(_status.Effect(new ContentId(EnemyStatuses.RootedKey)).activeSelf, Is.True);
        Change(new ContentId(EnemyStatuses.RootedKey), 0f);
        Assert.That(_bar.IsShown, Is.False);
    }

    [Test]
    public void Death_ClearsEverything()
    {
        AllOn();
        _hub.Publish(new EnemyDied(Id, Rootling, System.Numerics.Vector3.Zero));
        AssertClean();
        Change(new ContentId(EnemyStatuses.StunnedKey), 2f);
        AssertClean();
    }

    [Test]
    public void Pooled_ComesBackClean()
    {
        AllOn();
        EnemyView before = _body;
        _hub.Publish(new EnemyDespawned(Id));
        Spawn(2);
        Assert.That(_body, Is.SameAs(before));
        AssertClean();
        Change(new ContentId(EnemyStatuses.RootedKey), 3f);
        AssertClean();
        _hub.Publish(new EnemyStatusChanged(2, new EnemyStatuses().Rooted, 3f, 1));
        Assert.That(_status.Effect(new ContentId(EnemyStatuses.RootedKey)).activeSelf, Is.True);
    }

    [Test]
    public void Effects_ArePooled()
    {
        var effects = new[] { _status.Effect(new ContentId(EnemyStatuses.StunnedKey)), _status.Effect(new ContentId(EnemyStatuses.RootedKey)), _status.Effect(new ContentId(EnemyStatuses.SlowedKey)) };
        var icons = new[] { _bar.StatusIcon(new ContentId(EnemyStatuses.StunnedKey)), _bar.StatusIcon(new ContentId(EnemyStatuses.RootedKey)), _bar.StatusIcon(new ContentId(EnemyStatuses.SlowedKey)) };
        int count = _parent.GetComponentsInChildren<Transform>(true).Length;
        for (int n = 0; n < 5; n++)
        {
            AllOn();
            var kinds = new[] { new ContentId(EnemyStatuses.StunnedKey), new ContentId(EnemyStatuses.RootedKey), new ContentId(EnemyStatuses.SlowedKey) };
            for (int k = 0; k < kinds.Length; k++)
            {
                Change(kinds[k], 0f);
                Assert.That(_status.Effect(kinds[k]), Is.SameAs(effects[k]));
                Assert.That(_bar.StatusIcon(kinds[k]), Is.SameAs(icons[k]));
            }
        }
        Assert.That(_parent.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
    }

    [Test]
    public void Stunned_HitStillFlinches()
    {
        _parent.SetActive(true);
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Change(new ContentId(EnemyStatuses.StunnedKey), 3f);
        _hub.Publish(new EnemyDamaged(Id, 1f, 0.9f, false));
        int layer = _animator.GetLayerIndex(FlinchLayer.Name);
        bool hit = false;
        for (int i = 0; i < 60; i++)
        {
            _animation.Step(0.05f);
            _animator.Update(0.05f);
            hit |= _animator.GetCurrentAnimatorStateInfo(layer).IsName("Hit");
        }
        Assert.That(hit, Is.True, "Hit must reach the flinch layer even at the held animator rate.");
    }

    [TestCase("Enemy")]
    [TestCase("Croaker")]
    [TestCase("Frog")]
    [TestCase("Mossguard")]
    [TestCase("MotherPitcher")]
    [TestCase("Panther")]
    [TestCase("Pitcher")]
    [TestCase("Pitcher_Bud")]
    [TestCase("Puffball")]
    [TestCase("Rootling")]
    public void Prefab_HasOneStatusViewWithAllEffects(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + name + ".prefab");
        Assert.That(prefab.GetComponents<EnemyStatusView>().Length, Is.EqualTo(1));
        var so = new SerializedObject(prefab.GetComponent<EnemyStatusView>());
        foreach (string field in new[] { "_stunSparks", "_rootVines", "_slowFrost", "_icons", "_sweepSprite" })
        {
            Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null, name + ": " + field);
        }
    }

    private void Spawn(int id)
    {
        _hub.Publish(new EnemySpawned(id, Rootling, System.Numerics.Vector3.Zero));
        Assert.That(_views.TryGet(id, out _body), Is.True);
        _status = _body.GetComponent<EnemyStatusView>();
        _bar = _body.GetComponent<EnemyHealthBar>();
        _animation = _body.GetComponentInChildren<EnemyAnimatorView>(true);
        _animator = _body.GetComponentInChildren<Animator>(true);
        // Awake is not run for EditMode instances. Initialise the existing hit-feedback sibling,
        // and evaluate the controller before reading its live parameters.
        typeof(EnemyHitFeedback).GetMethod("Awake", Private).Invoke(_body.Feedback, null);
        _parent.SetActive(true);
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.Update(0f);
    }

    private void Change(ContentId kind, float seconds)
    {
        new EnemyStatuses().TryGet(kind, out StatusRule rule);
        _hub.Publish(new EnemyStatusChanged(Id, rule, seconds, 1));
    }
    private void TickBar(float dt) => typeof(EnemyHealthBar).GetMethod("Tick", Private).Invoke(_bar, new object[] { dt });
    private void AllOn()
    {
        foreach (ContentId kind in new[] { new ContentId(EnemyStatuses.StunnedKey), new ContentId(EnemyStatuses.RootedKey), new ContentId(EnemyStatuses.SlowedKey) })
        {
            Change(kind, 3f);
        }
    }

    private void AssertClean()
    {
        foreach (ContentId kind in new[] { new ContentId(EnemyStatuses.StunnedKey), new ContentId(EnemyStatuses.RootedKey), new ContentId(EnemyStatuses.SlowedKey) })
        {
            Assert.That(_status.Effect(kind).activeSelf, Is.False);
            Assert.That(_bar.StatusIcon(kind).gameObject.activeSelf, Is.False);
        }
        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_animator.speed, Is.EqualTo(1f));
    }

    private RunSession StartedSession()
    {
        var boot = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
        var catalog = new ContentCatalog(
            Specs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            Specs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            Specs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            Specs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            Specs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            Specs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        var random = new FixedRandom(7);
        _recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L)), _hub);
        var session = new RunSession(catalog, random, _hub, new RecordingIntents(), _recorder, 64, 28, 64);
        session.Start(new RunConfig(new ContentId("mode.jungle"), new ContentId("character.ranger"), 7, 1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(Rootling, new System.Numerics.Vector3(0f, 0f, 5f)) }), null));
        return session;
    }

    private static TSpec[] Specs<T, TSpec>(SerializedObject boot, string field, Func<T, TSpec> read) where T : Object
    {
        SerializedProperty array = boot.FindProperty(field);
        var specs = new TSpec[array.arraySize];
        for (int i = 0; i < specs.Length; i++)
        {
            specs[i] = read((T)array.GetArrayElementAtIndex(i).objectReferenceValue);
        }
        return specs;
    }
}
