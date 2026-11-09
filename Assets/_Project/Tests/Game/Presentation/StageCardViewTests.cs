using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Presentation;

public sealed class StageCardViewTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private GameObject _hud;
    private DomainEventHub _hub;
    private StageCardView _card;
    private TMP_Text _text;

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_hud);
        _hub?.Dispose();
    }

    [Test]
    public void Card_Arrived_ShowsStageNumber()
    {
        Build();

        _hub.Publish(new StageArrived(1, default));
        Assert.That(_card.Showing, Is.True);
        Assert.That(_text.text, Is.EqualTo("STAGE 1"));

        _hub.Publish(new StageArrived(7, default));
        Assert.That(_card.Showing, Is.True);
        Assert.That(_text.text, Is.EqualTo("STAGE 7"));
    }

    [Test]
    public void Card_Timeline_RestartsOnArrival()
    {
        Build();
        _hub.Publish(new StageArrived(7, default));

        _card.Advance(0.3f);
        Assert.That(_card.Alpha, Is.Zero);
        _card.Advance(0.25f);
        Assert.That(_card.Alpha, Is.EqualTo(0.5f).Within(0.001f));
        _card.Advance(0.45f);
        Assert.That(_card.Alpha, Is.EqualTo(1f));

        _hub.Publish(new StageArrived(8, default));
        Assert.That(_card.Alpha, Is.Zero);
        _card.Advance(0.3f);
        Assert.That(_card.Alpha, Is.Zero);
        Assert.That(_text.text, Is.EqualTo("STAGE 8"));

        _card.Advance(1.7f);
        Assert.That(_card.Alpha, Is.EqualTo(1f));
        _card.Advance(0.3f);
        Assert.That(_card.Alpha, Is.GreaterThan(0f).And.LessThan(1f));
        _card.Advance(0.3f);
        Assert.That(_card.Alpha, Is.Zero);
        Assert.That(_card.Showing, Is.False);
        Assert.That(_text.enabled, Is.False);
    }

    [Test]
    public void Card_RunEnded_Hides()
    {
        Build();
        _hub.Publish(new StageArrived(7, default));
        _card.Advance(1f);

        _hub.Publish(new RunEnded(1f));

        Assert.That(_card.Showing, Is.False);
        Assert.That(_card.Alpha, Is.Zero);
        Assert.That(_text.enabled, Is.False);
    }

    [Test]
    public void Card_NotRaycastTarget()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var card = prefab.GetComponentInChildren<StageCardView>(true);
        Assert.That(card, Is.Not.Null);

        var texts = card.GetComponentsInChildren<TMP_Text>(true);
        Assert.That(texts, Has.Length.EqualTo(1));
        Assert.That(texts[0].alignment, Is.EqualTo(TextAlignmentOptions.Center));
        Assert.That(texts[0].color, Is.EqualTo(Color.white));
        Assert.That(card.transform.parent.name, Is.EqualTo("SafeArea"));
        Assert.That(((RectTransform)card.transform).anchorMin.y, Is.EqualTo(2f / 3f).Within(0.0001f));
        foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true))
        {
            Assert.That(graphic.raycastTarget, Is.False, graphic.name);
        }
    }

    private void Build()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        _hud = Object.Instantiate(prefab);
        _card = _hud.GetComponentInChildren<StageCardView>(true);
        Assert.That(_card, Is.Not.Null);

        _text = _card.GetComponent<TMP_Text>();
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);
        _hub = new DomainEventHub();
        _card.Construct(_hub, new TableLocalizer(table));
    }
}
