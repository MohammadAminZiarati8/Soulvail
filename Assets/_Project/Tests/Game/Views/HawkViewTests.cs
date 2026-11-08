using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using static Soulvail.Tests.Game.Authoring.PackleaderPathTests;

namespace Soulvail.Tests.Game.Views;

public sealed class HawkViewTests
{
    private DomainEventHub _hub;
    private Camera _camera;
    private HawkView _view;
    private Animator _animator;
    private Vector3 _position;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _camera = new GameObject("Hawk test camera").AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.transform.SetPositionAndRotation(new Vector3(0f, 12f, -8f), Quaternion.Euler(55f, 0f, 0f));
        _view = Object.Instantiate(Load<GameObject>("Prefabs/Pets/Hawk.prefab")).GetComponent<HawkView>();
        _animator = _view.GetComponentInChildren<Animator>(true);
        _position = new Vector3(3f, 4f, 2f);
        _view.Bind(_hub, () => _position, _camera);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_view.gameObject);
        Object.DestroyImmediate(_camera.gameObject);
        _hub.Dispose();
    }

    [Test]
    public void Arrived_Shows_Left_Hides()
    {
        Assert.That(_view.gameObject.activeSelf, Is.False);
        _hub.Publish(new HawkArrived());
        _view.Step(0f);
        Assert.That(_view.gameObject.activeSelf, Is.True);
        Assert.That(_camera.WorldToViewportPoint(_animator.transform.position).x, Is.LessThan(0f));

        _view.Step(0.5f);
        _view.Step(0f);
        Assert.That(_animator.transform.position, Is.EqualTo(_position));

        _hub.Publish(new HawkLeft());
        _view.Step(0.4f);
        Assert.That(_view.gameObject.activeSelf, Is.True);
        Assert.That(_camera.WorldToViewportPoint(_animator.transform.position).x, Is.GreaterThan(1f));
        _view.Step(0.1f);
        Assert.That(_view.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Shadow_UnderHawk()
    {
        Circle();
        SpriteRenderer shadow = _view.GetComponentsInChildren<SpriteRenderer>().Single(sprite => sprite.name == "Ground Shadow");
        Assert.That(shadow.enabled && shadow.gameObject.activeInHierarchy, Is.True);
        Assert.That(shadow.sprite, Is.Not.Null);
        Assert.That(shadow.transform.position.x, Is.EqualTo(_position.x).Within(0.0001f));
        Assert.That(shadow.transform.position.z, Is.EqualTo(_position.z).Within(0.0001f));
        Assert.That(shadow.transform.position.y, Is.InRange(0f, 0.1f));
        Assert.That(shadow.color.maxColorComponent, Is.LessThan(0.15f));
        Assert.That(shadow.color.a, Is.InRange(0.1f, 0.6f));
    }

    [Test]
    public void Dove_PlaysDive_Struck_PlaysStrike()
    {
        Circle();
        var target = new System.Numerics.Vector3(2f, 0f, 1f);
        _hub.Publish(new HawkDove(1, _position.ToNum(), target));
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Dive"), Is.True);

        _hub.Publish(new HawkStruck(1, 2f));
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Strike"), Is.True);
        SpriteRenderer burst = _view.GetComponentsInChildren<SpriteRenderer>().Single(sprite => sprite.name == "Strike Burst");
        Assert.That(burst.enabled, Is.True);
        Assert.That(burst.color, Is.EqualTo(new Color(Palette.Player.r, Palette.Player.g, Palette.Player.b, 1f)));
        Assert.That(Vector3.Distance(burst.transform.position, target.ToUnity() + Vector3.up * 0.25f), Is.LessThan(0.0001f));

        _animator.Update(0.6f);
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Climb"), Is.True);
        _view.Step(0.3f);
        Assert.That(burst.enabled, Is.False);
    }

    [Test]
    public void View_FollowsCorePosition()
    {
        Circle();
        _hub.Publish(new HawkDove(1, _position.ToNum(), new System.Numerics.Vector3(2f, 0f, 1f)));
        _position = new Vector3(2.5f, 2f, 1.5f);
        _view.Step(0.3f);
        Assert.That(_view.transform.position, Is.EqualTo(_position));
        Assert.That(_animator.transform.position, Is.EqualTo(_position));
        _view.Step(0.4f);
        Assert.That(_animator.transform.position, Is.EqualTo(_position));
    }

    [Test]
    public void Hawk_ThreeRanks_Numbers_Trigger()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        var path = new ContentId("path.packleader");
        PathSkillSpec row = catalog.Path(path).Skills[4];
        Assert.That(row.UnlockLevel, Is.EqualTo(17));
        Assert.That(row.Ranks.TierCount, Is.EqualTo(3));

        var skills = Enumerable.Range(1, 3).Select(i => catalog.Skill(row.Ranks.Tier(i).Single())).ToArray();
        var icons = boot.Resolve<SkillIconBook>();
        var placeholder = Load<Sprite>("Art/UI/Icons/Icon_Placeholder.png");
        Assert.That(skills.Select(skill => skill.Kind), Is.EqualTo(new[] { SkillKind.Active, SkillKind.Upgrade, SkillKind.Upgrade }));
        Assert.That(skills.Select(skill => skill.ParentId), Is.EqualTo(new[] { default(ContentId), skills[0].Id, skills[1].Id }));
        Assert.That(skills.Select(skill => icons.For(skill.Id)), Is.All.SameAs(placeholder));
        Assert.That(skills[0].Active.Cooldown, Is.EqualTo(16f));
        var call = (CallHawk)skills[0].Active.OnCast.Single();
        Assert.That((call.DamageOfWeapon, call.StunSeconds), Is.EqualTo((2f, 0.4f)));
        TriggerClause clause = skills[0].Active.Trigger.Clauses.Single();
        Assert.That((clause.Field, clause.Comparison, clause.Threshold),
            Is.EqualTo((TriggerField.MarkedEnemies, TriggerComparison.AtLeast, 1f)));
        AssertModifier((ModifyStat)skills[1].Effects.Single(), PlayerStat.HawkSeconds, 2f);
        AssertModifier(skills[2].Effects.OfType<ModifyStat>().Single(), PlayerStat.HawkDiveInterval, -0.5f);
        var cooldown = skills[2].Effects.OfType<ModifySkillCooldown>().Single();
        Assert.That((cooldown.SkillId, cooldown.Kind, cooldown.Value),
            Is.EqualTo((skills[0].Id, ModifierKind.Flat, -4f)));

        var invalid = ScriptableObject.CreateInstance<CallHawkDefinition>();
        try
        {
            invalid.name = "Invalid Hawk";
            var serialized = new SerializedObject(invalid);
            serialized.FindProperty("_damageOfWeapon").floatValue = float.NaN;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(() => invalid.ToEffect(), Throws.ArgumentException.With.Message.Contains(invalid.name));
            serialized.FindProperty("_damageOfWeapon").floatValue = 2f;
            serialized.FindProperty("_stunSeconds").floatValue = -1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(() => invalid.ToEffect(), Throws.ArgumentException.With.Message.Contains(invalid.name));
        }
        finally
        {
            Object.DestroyImmediate(invalid);
        }

        RunSession session = StartRun(catalog, _hub, Load<CharacterDefinition>("Data/Characters/Beastmaster.asset"), level: 17);
        session.ChoosePath(path);
        GameObject hud = Object.Instantiate(Load<GameObject>("Prefabs/UI/Hud.prefab"));
        try
        {
            var arc = hud.GetComponentInChildren<SkillArc>(true);
            arc.Construct(session, catalog, icons, boot.Resolve<CharacterLookBook>(),
                new SkillArcInput(session, session), boot.Resolve<ILocalizer>());
            SkillArcButton circle = arc.Actives.Single(button => button.Id == skills[0].Id);
            Assert.That(circle.IsLocked, Is.True);
            session.TakeNode(skills[0].Id);
            arc.Refresh();
            Assert.That(circle.IsLocked, Is.False);
            Assert.That(circle.AutoBadge, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(hud);
        }
    }

    private void Circle()
    {
        _hub.Publish(new HawkArrived());
        _view.Step(0.5f);
        _view.Step(0f);
    }

    private static void AssertModifier(ModifyStat effect, PlayerStat stat, float value) =>
        Assert.That((effect.Stat, effect.Kind, effect.Value, effect.Target),
            Is.EqualTo((stat, ModifierKind.Flat, value, StatTarget.Player)));
}
