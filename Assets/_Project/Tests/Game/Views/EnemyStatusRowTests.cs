using System;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>ST-02: the status row's order and eviction, and what the enemy bar draws from it.</summary>
[TestFixture]
public sealed class EnemyStatusRowTests
{
    private const int Id = 3;
    private static readonly EnemyStatuses Statuses = new();

    private EnemyStatusRow _row;
    private GameObject _body;
    private DomainEventHub _hub;
    private EnemyHealthBar _bar;
    private StatusIconBook _book;
    private Sprite _fallback;
    private Texture2D _texture;

    [SetUp]
    public void Build()
    {
        _row = new EnemyStatusRow();
        _hub = new DomainEventHub();
        _body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Rootling.prefab"));
        _bar = _body.GetComponent<EnemyHealthBar>();
        _bar.Construct(_hub);
        _book = AssetDatabase.LoadAssetAtPath<StatusIconBook>("Assets/_Project/Data/StatusIconBook.asset");
        _bar.ConfigureStatuses(_book, null);
        _bar.Bind(Id, false);
    }

    [TearDown]
    public void Free()
    {
        Object.DestroyImmediate(_body);
        _hub.Dispose();
        if (_fallback != null)
        {
            Object.DestroyImmediate(_fallback);
            Object.DestroyImmediate(_texture);
        }
    }

    [Test]
    public void Order_DebuffsBeforeBuffs()
    {
        Apply(Statuses.Hasted, 3f);
        Apply(Statuses.Slowed, 3f);

        Assert.That(_row.RuleAt(0).Id.Value, Is.EqualTo(EnemyStatuses.SlowedKey));
        Assert.That(_row.RuleAt(1).Id.Value, Is.EqualTo(EnemyStatuses.HastedKey));
    }

    [Test]
    public void Order_PriorityWithinSide()
    {
        Apply(Statuses.Slowed, 3f);
        Apply(Statuses.Stunned, 3f);
        Apply(Statuses.Rooted, 3f);

        Assert.That(_row.RuleAt(0).Id.Value, Is.EqualTo(EnemyStatuses.StunnedKey));
        Assert.That(_row.RuleAt(1).Id.Value, Is.EqualTo(EnemyStatuses.RootedKey));
        Assert.That(_row.RuleAt(2).Id.Value, Is.EqualTo(EnemyStatuses.SlowedKey));

        var row = new EnemyStatusRow();
        row.Apply(Change(Debuff("status.test.b", 5), 3f));
        row.Apply(Change(Debuff("status.test.a", 5), 3f));
        Assert.That(row.RuleAt(0).Id.Value, Is.EqualTo("status.test.b"), "Equal priorities keep arrival order.");
    }

    [Test]
    public void Apply_ZeroSeconds_Removes()
    {
        Apply(Statuses.Stunned, 3f);
        Apply(Statuses.Stunned, 0f);

        Assert.That(_row.Count, Is.Zero);
    }

    [Test]
    public void Apply_Ninth_EvictsLowest()
    {
        FillEight(10);
        Apply(Debuff("status.test.ninth", 50), 3f);

        Assert.That(_row.Count, Is.EqualTo(EnemyStatusRow.Tracked));
        Assert.That(_row.RuleAt(0).Id.Value, Is.EqualTo("status.test.ninth"));
        Assert.That(_row.RuleAt(7).Id.Value, Is.EqualTo("status.test.6"), "The last arrival of the tie was evicted.");
    }

    [Test]
    public void Apply_Ninth_BelowAll_Dropped()
    {
        FillEight(10);
        Apply(Debuff("status.test.ninth", 10), 3f);

        Assert.That(_row.Count, Is.EqualTo(EnemyStatusRow.Tracked));
        for (int rank = 0; rank < EnemyStatusRow.Tracked; rank++)
        {
            Assert.That(_row.RuleAt(rank).Id.Value, Is.EqualTo("status.test." + rank));
        }
    }

    [Test]
    public void Shown_BasicOne_EliteFour()
    {
        for (int i = 0; i < 5; i++)
        {
            Apply(Debuff("status.test." + i, 10), 3f);
        }

        Assert.That(_row.Shown(false), Is.EqualTo(1));
        Assert.That(_row.Shown(true), Is.EqualTo(4));
    }

    [Test]
    public void Fill_DrainsAndRestarts()
    {
        Apply(Statuses.Slowed, 2f);
        _row.Step(1f);
        Assert.That(_row.FillAt(0), Is.EqualTo(0.5f).Within(1e-5f));

        Apply(Statuses.Slowed, 2f);
        Assert.That(_row.FillAt(0), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void Stacks_CountOnlyAboveOne()
    {
        Publish(Statuses.Slowed, 3f, 1);
        Assert.That(_bar.StatusStacks(0).gameObject.activeSelf, Is.False);

        Publish(Statuses.Slowed, 3f, 3);
        Assert.That(_bar.StatusStacks(0).gameObject.activeSelf, Is.True);
        Assert.That(_bar.StatusStacks(0).text, Is.EqualTo("3"));
    }

    [Test]
    public void Bar_BasicEnemy_DrawsOneIcon()
    {
        Publish(Statuses.Slowed, 3f, 1);
        Publish(Statuses.Stunned, 3f, 1);

        Assert.That(_bar.StatusIcon(0).gameObject.activeSelf, Is.True);
        Assert.That(_bar.StatusIcon(0).sprite, Is.SameAs(_book.For(Statuses.Stunned.Id)));
        for (int rank = 1; rank < 4; rank++)
        {
            Assert.That(_bar.StatusIcon(rank).gameObject.activeSelf, Is.False);
        }
    }

    [Test]
    public void Bar_Hasted_UsesBookSprite()
    {
        Publish(Statuses.Hasted, 3f, 1);

        Sprite sprite = _bar.StatusIcon(0).sprite;
        Assert.That(sprite, Is.Not.Null);
        Assert.That(sprite, Is.SameAs(_book.For(Statuses.Hasted.Id)));
        Assert.That(sprite.name, Is.EqualTo("T_Status_Hasted"));
    }

    [Test]
    public void Bar_UnknownId_Fallback()
    {
        _texture = new Texture2D(4, 4);
        _fallback = Sprite.Create(_texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        var book = ScriptableObject.CreateInstance<StatusIconBook>();
        book.Set(new string[0], new Sprite[0], _fallback);
        var body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Rootling.prefab"));
        try
        {
            var bar = body.GetComponent<EnemyHealthBar>();
            bar.Construct(_hub);
            bar.ConfigureStatuses(book, null);
            bar.Bind(Id + 1, false);
            _hub.Publish(new EnemyStatusChanged(Id + 1, Debuff("status.test.unmapped", 5), 3f, 1));

            Assert.That(bar.StatusIcon(0).sprite, Is.SameAs(_fallback));
        }
        finally
        {
            Object.DestroyImmediate(body);
            Object.DestroyImmediate(book);
        }
    }

    [Test]
    public void Bar_Draw_NoAllocation()
    {
        MethodInfo tick = typeof(EnemyHealthBar).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
        var step = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), _bar, tick);
        var on = new EnemyStatusChanged(Id, Statuses.Stunned, 2f, 3);
        var more = new EnemyStatusChanged(Id, Statuses.Slowed, 3f, 1);
        var buff = new EnemyStatusChanged(Id, Statuses.Hasted, 3f, 1);
        var off = new EnemyStatusChanged(Id, Statuses.Stunned, 0f, 1);

        AllocationAssert.None(() =>
        {
            _hub.Publish(on);
            _hub.Publish(more);
            _hub.Publish(buff);
            step(0.1f);
            _hub.Publish(off);
            step(0.1f);
        }, 100);
    }

    private void FillEight(int priority)
    {
        for (int i = 0; i < EnemyStatusRow.Tracked; i++)
        {
            Apply(Debuff("status.test." + i, priority), 3f);
        }
    }

    private void Apply(StatusRule rule, float seconds) => _row.Apply(Change(rule, seconds));

    private void Publish(StatusRule rule, float seconds, int stacks) =>
        _hub.Publish(new EnemyStatusChanged(Id, rule, seconds, stacks));

    private static EnemyStatusChanged Change(StatusRule rule, float seconds) => new(Id, rule, seconds, 1);

    private static StatusRule Debuff(string id, int priority) =>
        new(new ContentId(id), StatusSide.Debuff, priority, 1f);
}
