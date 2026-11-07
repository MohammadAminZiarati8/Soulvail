using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class AxeViewsTests
{
    private const string Root = "Assets/_Project/";
    private GameObject _parent;
    private DomainEventHub _hub;
    private AxeSystem _axes;
    private AxeViews _views;

    [SetUp]
    public void SetUp()
    {
        _parent = new GameObject("Axe views test");
        _hub = new DomainEventHub();
        var character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Beastmaster.asset").ToSpec();
        var combat = new PlayerCombat(character, _hub, new IntentBuffer(), 1);
        var motor = new PlayerMotor(character.Movement, Point.UnitZ);
        var stats = new PlayerStats(combat, motor, new LevelTracker(new XpCurve(1f, 1f, 1f), _hub));
        _axes = new AxeSystem(_hub, stats, character.Weapon);
        var prefab = AssetDatabase.LoadAssetAtPath<AxeView>(Root + "Prefabs/Projectiles/Axe.prefab");
        Assert.That(prefab, Is.Not.Null);
        _views = new AxeViews(_hub, prefab, _parent.transform, () => _axes.InFlight);
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        Object.DestroyImmediate(_parent);
    }

    [Test]
    public void Thrown_SpawnsOneView_AtCorePosition()
    {
        AxeView view = Throw();
        _axes.Tick(0.1f, 0.1f, Point.Zero, null, null);
        _views.Draw(0.1f);
        Assert.That(view.transform.position, Is.EqualTo(_axes.InFlight[0].Position.ToUnity()));
        Assert.That(_parent.GetComponentsInChildren<AxeView>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void Caught_ReturnsToPool()
    {
        AxeView view = Throw();
        _axes.Tick(1f, 1f, Point.Zero, null, null);
        _axes.Tick(1f, 2f, new Point(1f, 0f, 1f), null, null);
        Assert.That(view.gameObject.activeSelf, Is.False);
        Assert.That(Throw(), Is.SameAs(view));
    }

    [Test]
    public void StageChange_ClearsAll()
    {
        Throw();
        _hub.Publish(new StageTransitionStarted(1, 0.5f));
        Assert.That(_parent.GetComponentsInChildren<AxeView>(), Is.Empty);
        Assert.That(_parent.GetComponentsInChildren<AxeView>(true), Has.Length.EqualTo(AxeSystem.Capacity));
    }

    [Test]
    public void Trail_IsPlayerCyan()
    {
        AxeView view = Throw();
        var trail = view.GetComponent<TrailRenderer>();
        Assert.That(trail.startColor, Is.EqualTo(Palette.Player));
        _hub.Publish(new AxeTurned(view.Id, _axes.InFlight[0].Position));
        Assert.That(trail.startColor, Is.EqualTo(Palette.Wind));
        _views.Draw(0.11f);
        Assert.That(trail.startColor, Is.EqualTo(Palette.Player));
    }

    [Test]
    public void Marked_ShowsGlyph_Unmarked_Hides()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<SpriteRenderer>(Root + "Prefabs/Vfx/MarkGlyph.prefab");
        Assert.That(prefab, Is.Not.Null);
        var body = new GameObject("Enemy");
        body.transform.SetParent(_parent.transform);
        var collider = body.AddComponent<CapsuleCollider>();
        var enemy = body.AddComponent<EnemyView>();
        var statuses = new EnemyStatuses();
        using var glyphs = new MarkGlyphViews(_hub, prefab, _parent.transform, _parent.transform,
            id => id == 1 ? enemy : null, prewarm: 1);

        _hub.Publish(new EnemyStatusChanged(1, statuses.Marked, 4f, 1));
        _hub.Publish(new EnemyStatusChanged(1, statuses.Marked, 4f, 1));
        _hub.Publish(new EnemyStatusChanged(1, statuses.Stunned, 1f, 1));
        SpriteRenderer[] visible = _parent.GetComponentsInChildren<SpriteRenderer>();
        Assert.That(visible, Has.Length.EqualTo(1));
        Assert.That(visible[0].color, Is.EqualTo(Palette.Player));
        Assert.That(visible[0].transform.position.y, Is.GreaterThan(collider.bounds.max.y));

        _hub.Publish(new EnemyStatusChanged(1, statuses.Marked, 0f, 1));
        Assert.That(_parent.GetComponentsInChildren<SpriteRenderer>(), Is.Empty);
    }

    [Test]
    public void Icon_MarkedInStatusIconBook()
    {
        var book = AssetDatabase.LoadAssetAtPath<StatusIconBook>(Root + "Data/StatusIconBook.asset");
        Sprite icon = book.For(new ContentId(EnemyStatuses.MarkedKey));
        Assert.That(icon, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(icon), Is.EqualTo(Root + "Art/UI/Icons/Status_Marked.png"));
    }

    private AxeView Throw()
    {
        // Zero damage isolates presentation from enemy hit resolution while core flies and catches.
        Assert.That(_axes.TryThrow(new Point(1f, 0f, 1f), Point.UnitZ, 0f, 0f), Is.True);
        AxeView[] active = _parent.GetComponentsInChildren<AxeView>();
        Assert.That(active, Has.Length.EqualTo(1));
        Assert.That(active[0].transform.position, Is.EqualTo(_axes.InFlight[0].Position.ToUnity()));
        return active[0];
    }
}
