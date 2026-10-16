using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-11d rules 4 and 5: a finished run's end screen says the place is cleared, and says what it
/// opened. A death keeps today's title.
/// </summary>
/// <remarks>
/// The screen is the shipped prefab and the words are the shipped English table, as in
/// <c>RunEndPresenterTests</c>, for the same reason: a row pinned against a table this file wrote
/// would go green over an <c>English.asset</c> that said anything at all.
/// </remarks>
[TestFixture]
public sealed class RunEndFinishTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/RunEnd.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly ContentId Jungle = new ContentId("mode.jungle");
    private static readonly ContentId Descent = new ContentId("mode.descent");

    private DomainEventHub _hub;
    private GameObject _screen;
    private RunEndPresenter _presenter;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        _hub?.Dispose();

        if (_screen != null)
        {
            Object.DestroyImmediate(_screen);
        }
    }

    [Test]
    public void RunEnd_Finished_ShowsClearedTitle()
    {
        Build(Catalog(opens: false));

        Finish(Jungle);

        Assert.That(Title().text, Is.EqualTo("Jungle cleared!"));
        Assert.That(Title().color, Is.EqualTo(Palette.HudText), "the gold is the ribbon's; gold text on it cannot be read (UI-09c rule 3).");
    }

    [Test]
    public void RunEnd_Death_KeepsTitle()
    {
        Build(Catalog(opens: true));

        _hub.Publish(new ShardsAwarded(10, 12, 2, null, Jungle));

        Assert.That(Title().text, Is.EqualTo("You died"));
        Assert.That(Title().color, Is.EqualTo(Palette.HudText));
        Assert.That(Opened().gameObject.activeSelf, Is.False);
    }

    [Test]
    public void RunEnd_FinishOpensPlace_ShowsLine()
    {
        Build(Catalog(opens: true));

        Finish(Jungle);

        Assert.That(Opened().gameObject.activeSelf, Is.True);
        Assert.That(Opened().text, Is.EqualTo("New place: Descent"));
    }

    [Test]
    public void RunEnd_NothingOpened_NoLine()
    {
        Build(Catalog(opens: false));

        Finish(Jungle);

        Assert.That(Opened().gameObject.activeSelf, Is.False, "the Jungle alone opens nothing.");
    }

    [Test]
    public void RunEnd_Death_DrawsTheDefeatPair()
    {
        Build(Catalog(opens: true));

        Finish(Jungle);
        _hub.Publish(new ShardsAwarded(10, 12, 2, null, Jungle));

        Assert.That(Picture("_emblem").sprite.name, Is.EqualTo("RunEnd_EmblemDefeat"), "a death after a finish is dressed as a death.");
        Assert.That(Picture("_ribbon").sprite.name, Is.EqualTo("RunEnd_RibbonDefeat"));
        Assert.That(Title().color, Is.EqualTo(Palette.HudText));
    }

    [Test]
    public void RunEnd_Finish_SwapsInTheVictoryPair()
    {
        Build(Catalog(opens: false));

        _hub.Publish(new ShardsAwarded(10, 30, 6, null, Jungle));

        Assert.That(Picture("_emblem").sprite.name, Is.EqualTo("RunEnd_EmblemDefeat"));

        _hub.Publish(new ModeFinished(Jungle, 30));

        Assert.That(Picture("_emblem").sprite.name, Is.EqualTo("RunEnd_EmblemVictory"));
        Assert.That(Picture("_ribbon").sprite.name, Is.EqualTo("RunEnd_RibbonVictory"));
        Assert.That(Title().text, Is.EqualTo("Jungle cleared!"), "on the same call that rewrites the title.");
        Assert.That(Title().color, Is.EqualTo(Palette.HudText), "gold text on a gold ribbon cannot be read.");
    }

    private void Finish(ContentId mode)
    {
        _hub.Publish(new ShardsAwarded(10, 30, 6, null, mode));
        _hub.Publish(new ModeFinished(mode, 30));
    }

    private void Build(ContentCatalog catalog)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _presenter = _screen.GetComponent<RunEndPresenter>();

        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        _presenter.Construct(_hub, new RecordingLoader(), new TableLocalizer(table), catalog);
        _presenter.GetType().GetMethod("Start", Private).Invoke(_presenter, null);
    }

    private TMP_Text Title() => Get("_title");

    private TMP_Text Opened() => Get("_opened");

    private UnityEngine.UI.Image Picture(string field) =>
        (UnityEngine.UI.Image)_presenter.GetType().GetField(field, Private).GetValue(_presenter);

    private TMP_Text Get(string field) =>
        (TMP_Text)_presenter.GetType().GetField(field, Private).GetValue(_presenter);

    /// <summary>The Jungle, and, when asked, a listed place the Jungle opens.</summary>
    private static ContentCatalog Catalog(bool opens) => new ContentCatalog(
        Array.Empty<CharacterSpec>(),
        modes: opens
            ? new[] { Mode(Jungle, default), Mode(Descent, Jungle) }
            : new[] { Mode(Jungle, default) });

    private static ModeSpec Mode(ContentId id, ContentId unlockedBy) => new ModeSpec(
        id,
        new LocKey(id.Value + ".name"),
        startingStage: 1,
        isEndless: false,
        finalStage: 30,
        new ScalingSpec(
            new BudgetCurve(40f, 12f, 0.9f),
            new WaveCurve(2, 5, 2, 5),
            new ConcurrencyCurve(10, 2),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        new XpCurve(20f, 12f, 1.4f),
        Array.Empty<RosterEntry>(),
        unlockedBy: unlockedBy,
        isListed: true);

    private sealed class RecordingLoader : SceneLoader
    {
        public override Task LoadAsync(string sceneName) => Task.CompletedTask;
    }
}
