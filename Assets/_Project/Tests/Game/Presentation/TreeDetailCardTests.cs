using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-04d: a tap selects, the card under the block describes the node, and Learn spends the point.
/// </summary>
/// <remarks>
/// A third file of <see cref="TreeScreenTests"/> for <see cref="TreeFrameTests"/>' reason.
/// </remarks>
public sealed partial class TreeScreenTests
{
    private const float LearnMinDp = 48f;

    [Test]
    public void Tap_Selects_SendsNothing()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        ContentId tier1 = _tree.Branches[0].Tier(1)[0];

        Assert.That(CellFor(tier1).State, Is.EqualTo(NodeState.Available), "the fixture's premise.");

        Tap(CellFor(tier1));

        Assert.That(_screen.Selected, Is.EqualTo(tier1));
        Assert.That(CellFor(tier1).IsSelected, Is.True, "the ring is not shown.");
        Assert.That(_session.State.SkillPoints, Is.EqualTo(1));
        Assert.That(_session.State.TakenNodeIds, Has.No.Member(tier1), "a tap took the node.");
    }

    [Test]
    public void Tap_Another_MovesRing()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        ContentId first = _tree.Branches[0].Tier(1)[0];
        ContentId locked = _tree.Branches[1].Tier(3)[0];

        Tap(CellFor(first));
        Tap(CellFor(locked));

        Assert.That(_screen.Selected, Is.EqualTo(locked), "a locked cell is selectable.");
        Assert.That(Shown().Where(c => c.IsSelected).Select(c => c.Node), Is.EqualTo(new[] { locked }));
    }

    [Test]
    public void Open_SelectsNothing_ShowsHint()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());

        _screen.Open();

        TreeDetailCard card = Card();

        Assert.That(_screen.Selected, Is.EqualTo(default(ContentId)));
        Assert.That(Shown().Any(c => c.IsSelected), Is.False);
        Assert.That(card.IsHint, Is.True);
        Assert.That(card.HintText, Is.EqualTo("Tap a skill to see it"));
        Assert.That(card.LearnShown, Is.False);
    }

    [Test]
    public void Close_ClearsSelection()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();
        Tap(CellFor(_tree.Branches[0].Tier(1)[0]));
        TapClose();

        Assert.That(_screen.Selected, Is.EqualTo(default(ContentId)));

        _screen.Open();

        Assert.That(Shown().Any(c => c.IsSelected), Is.False, "the ring survived a close.");
        Assert.That(Card().IsHint, Is.True);
    }

    [Test]
    public void Card_DescribesSelected()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build(new MapLocalizer(
            ("skill.test.b2a.name", "Fleet Foot"),
            ("skill.test.b2a.desc", "Move faster."),
            ("ui.tree.level", "Level {0:0} of {1:0}")));

        _screen.Open();
        Tap(CellFor(_tree.Branches[1].Tier(2)[0]));

        TreeDetailCard card = Card();

        Assert.That(card.IsHint, Is.False);
        Assert.That(card.TitleText, Is.EqualTo("Fleet Foot II"));
        Assert.That(card.LevelText, Is.EqualTo("Level 2 of 3"));
        Assert.That(card.DescriptionText, Is.EqualTo("Move faster."));
        Assert.That(card.TileSprite.name, Is.EqualTo("TreeTile_Locked"));
    }

    [Test]
    public void Learn_EnabledOnlyAvailableWithPoint()
    {
        ContentId first = TreeFirst(Ranger());

        // Available with a point: green.
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());
        _screen.Open();
        Tap(CellFor(first));

        Assert.That(Card().LearnInteractable, Is.True);
        Assert.That(Card().LearnSprite.name, Is.EqualTo("Button_Green"));
        Assert.That(Card().LearnText, Is.EqualTo("Learn"));

        // Available without one: grey.
        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 0);
        Build(Shipped());
        _screen.Open();
        Tap(CellFor(first));

        Assert.That(Card().LearnInteractable, Is.False);
        Assert.That(Card().LearnSprite.name, Is.EqualTo("Button_Grey"));

        // Locked, with a point: grey.
        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());
        _screen.Open();
        Tap(CellFor(LockedNode()));

        Assert.That(Card().LearnInteractable, Is.False);
        Assert.That(Card().LearnSprite.name, Is.EqualTo("Button_Grey"));

        // Taken: grey, and says so.
        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 1, taken: new[] { first });
        Build(Shipped());
        _screen.Open();
        Tap(CellFor(first));

        Assert.That(Card().LearnInteractable, Is.False);
        Assert.That(Card().LearnSprite.name, Is.EqualTo("Button_Grey"));
        Assert.That(Card().LearnText, Is.EqualTo("Learned"));
    }

    [Test]
    public void Learn_TakesOnce_KeepsSelection()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());

        _screen.Open();

        ContentId first = _tree.Branches[0].Tier(1)[0];

        Take(CellFor(first));
        Field<Button>(Card(), "_learn").onClick.Invoke();

        Assert.That(_session.State.TakenNodeIds, Has.Member(first));
        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(1), "Learn took twice.");
        Assert.That(_session.State.SkillPoints, Is.Zero);
        Assert.That(_screen.Selected, Is.EqualTo(first));
        Assert.That(CellFor(first).State, Is.EqualTo(NodeState.Taken));
        Assert.That(Card().LearnInteractable, Is.False);
        Assert.That(Card().LearnSprite.name, Is.EqualTo("Button_Grey"));
    }

    [Test]
    public void DrawingRun_NoLearn()
    {
        StartRun(Ranger(), free: false, pending: 1);
        Build();

        _screen.Open(null);
        Tap(CellFor(_tree.Branches[0].Tier(1)[0]));

        Assert.That(Card().IsHint, Is.False, "a drawing run shows the card.");
        Assert.That(Card().LearnShown, Is.False);
    }

    [Test]
    public void Card_FitsPortrait()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        OpenAt(Portrait);
        Tap(CellFor(_tree.Branches[0].Tier(1)[0]));
        Canvas.ForceUpdateCanvases();

        float px = StickShaper.PixelsPerDp(Screen.dpi);
        TreeDetailCard card = Card();
        Rect frame = WorldRect(Piece("_frame"));
        Rect cardRect = WorldRect(card);
        Rect block = WorldRect(Field<TreeBlockView>(_screen, "_block"));
        Rect learn = WorldRect((RectTransform)Field<Button>(card, "_learn").transform);

        Assert.That(
            cardRect.xMin >= frame.xMin - 0.5f && cardRect.xMax <= frame.xMax + 0.5f
                && cardRect.yMin >= frame.yMin - 0.5f && cardRect.yMax <= frame.yMax + 0.5f,
            Is.True,
            $"the card {cardRect} leaves the frame {frame}.");
        Assert.That(cardRect.Overlaps(block), Is.False, "the card overlaps the block.");
        Assert.That(
            learn.xMin >= cardRect.xMin - 0.5f && learn.xMax <= cardRect.xMax + 0.5f
                && learn.yMin >= cardRect.yMin - 0.5f && learn.yMax <= cardRect.yMax + 0.5f,
            Is.True,
            "Learn leaves the card.");
        Assert.That(learn.height, Is.GreaterThanOrEqualTo((LearnMinDp * px) - 0.5f));

        foreach (TMP_Text text in card.GetComponentsInChildren<TMP_Text>(false))
        {
            Assert.That(text.fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), $"{text.name} is under the floor.");
        }
    }

    private TreeDetailCard Card() => Field<TreeDetailCard>(_screen, "_card");

    /// <summary>A localizer over a handful of rows; anything else reads as its own key.</summary>
    private sealed class MapLocalizer : ILocalizer
    {
        private readonly Dictionary<string, string> _rows;

        public MapLocalizer(params (string Key, string Text)[] rows) =>
            _rows = rows.ToDictionary(r => r.Key, r => r.Text);

        public string Get(LocKey key) => _rows.TryGetValue(key.Key, out string text) ? text : key.Key;

        public string Format(LocKey key, params object[] args) => string.Format(Get(key), args);
    }
}
