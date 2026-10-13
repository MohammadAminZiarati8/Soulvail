using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-01b: the player's health bar in the owner's art — the fill and the ghost as widths in the well,
/// the gloss, the blocked flash, and the beat below a quarter.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first rows ever to drive the ghost's clock.</b> M1-17's 0.4 s hold and 1.5-a-second drain
/// shipped with no row watching them, because <c>Update</c> read <c>Time.deltaTime</c> and an EditMode
/// fixture cannot make time pass. UI-01b split the body into a private <c>Tick</c>, which is
/// <c>OverflowToast</c>'s and <c>FirstActiveHint</c>'s bargain, so these rows hand it a delta.
/// </para>
/// <para>
/// <b>Off the shipped prefab, with no presenter and no run</b> — <c>GrantedShieldTests.BareBar</c>'s
/// reason: the claims are about the asset the game draws, and a run would be scaffolding for a rect.
/// The bar is sized to the band's 240 × 22 and fitted, so the well has the geometry the game gives it.
/// The one row that needs a run's numbers, <c>Numbers_AreInsideTheBar</c>, lives in
/// <c>GrantedShieldTests</c>, which already builds one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HpBarViewTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private HpBarView _bar;

    [SetUp]
    public void BuildBar()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _spawned.Add(hud);

        hud.GetComponent<Canvas>().scaleFactor = 1f;

        _bar = hud.GetComponentInChildren<HpBarView>(true);

        Assert.That(_bar, Is.Not.Null, "HpBarView did not load off Hud.prefab (Traps §5).");

        // The band's size, in canvas units at a scale of 1, so the well and the gloss have the
        // proportions the game gives them.
        var rect = (RectTransform)_bar.transform;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(240f, 22f);

        _bar.Fit();
    }

    [TearDown]
    public void DestroyBar()
    {
        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();
        _bar = null;
    }

    // ---- The fill and the ghost (rule 5) ---------------------------------------------------------

    [Test]
    public void Fill_IsTheFractionOfTheWell()
    {
        _bar.Set(0.6f);

        var fill = (RectTransform)Fill().transform;

        Assert.That(_bar.Fraction, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(fill.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(fill.anchorMax.x, Is.EqualTo(0.6f).Within(1e-5f),
            "a sliced fill's width is its right-hand anchor; fillAmount is not drawn.");
        Assert.That(fill.anchorMax.y, Is.EqualTo(1f));
        Assert.That(fill.parent, Is.SameAs(Field<RectTransform>(_bar, "_well")), "the fill is not in the well.");
        Assert.That(Fill().color, Is.EqualTo(Palette.Player));
    }

    [Test]
    public void Fill_HidesAtZero()
    {
        _bar.Set(0f);

        // Switched off rather than drawn at no width: a sliced pill with none draws its two end caps
        // as a dot, which on a health bar says the player has health they do not.
        Assert.That(Fill().enabled, Is.False);
        Assert.That(Ghost().enabled, Is.False);
    }

    [Test]
    public void Ghost_HoldsForFourTenthsThenDrains()
    {
        _bar.Set(1f);
        _bar.Set(0.5f);

        Tick(0.3f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(1f).Within(0.01f), "the ghost moved inside its hold.");

        // The hold runs out inside this tick, and the drain takes the tick's whole 0.1 s at 1.5 a second.
        Tick(0.1f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.85f).Within(0.01f));

        Tick(0.1f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.7f).Within(0.01f));

        Tick(1f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.5f).Within(0.01f), "the ghost drained past the fill.");
        Assert.That(((RectTransform)Ghost().transform).anchorMax.x, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void Ghost_AHitRestartsTheHold()
    {
        _bar.Set(1f);
        _bar.Set(0.8f);

        Tick(0.3f);

        // A second hit inside the hold starts it again from the top, so a flurry leaves one trail
        // from the health before the first of them rather than a bar that stutters.
        _bar.Set(0.6f);

        Tick(0.3f);

        Assert.That(_bar.GhostFraction, Is.EqualTo(1f).Within(0.01f));
    }

    [Test]
    public void Ghost_AHealSnapsIt()
    {
        _bar.Set(1f);
        _bar.Set(0.5f);

        Tick(2f);

        Assert.That(_bar.GhostFraction, Is.EqualTo(0.5f).Within(0.01f), "the fixture's premise: drained.");

        _bar.Set(0.8f);

        // A ghost that lagged upward would draw a band over health the player already has.
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.8f).Within(1e-5f));
    }

    [Test]
    public void Ghost_TheFirstValueSnapsIt()
    {
        // A run that begins on less than full health must not open with a ghost describing damage
        // that never happened.
        _bar.Set(0.4f);

        Assert.That(_bar.GhostFraction, Is.EqualTo(0.4f).Within(1e-5f));
    }

    [Test]
    public void Ghost_IsPaleCyanNotGold()
    {
        _bar.Set(1f);
        _bar.Set(0.5f);

        Assert.That(Ghost().color, Is.EqualTo(Palette.PlayerGhost),
            "the ghost is not Palette.PlayerGhost. It was the prefab's 50 % gold from M1-17, and gold "
                + "means rewards (GD §16.4).");
        Assert.That(Ghost().transform.GetSiblingIndex(), Is.LessThan(Fill().transform.GetSiblingIndex()),
            "the ghost is not behind the fill.");
    }

    // ---- The gloss (rule 6) ----------------------------------------------------------------------

    [Test]
    public void Gloss_FollowsTheFill()
    {
        Image gloss = Field<Image>(_bar, "_gloss");

        Assert.That(gloss, Is.Not.Null, "the bar has no gloss.");

        _bar.Set(0.7f);

        var rect = (RectTransform)gloss.transform;

        Assert.That(gloss.enabled, Is.True);
        Assert.That(rect.anchorMax.x, Is.EqualTo(0.7f).Within(1e-5f));
        Assert.That(rect.anchorMin.y, Is.EqualTo(0.5f), "the gloss is not the fill's top half.");

        // Narrower than the well is tall: a shine on a sliver would be wider than the sliver.
        _bar.Set(0.02f);

        Assert.That(gloss.enabled, Is.False);
    }

    // ---- The blocked flash (rule 7) --------------------------------------------------------------

    [Test]
    public void Blocked_FlashesAndMovesNothing()
    {
        _bar.Set(0.8f);
        _bar.FlashBlocked();

        Assert.That(Fill().color, Is.EqualTo(Palette.PlayerBlocked));
        Assert.That(_bar.Fraction, Is.EqualTo(0.8f).Within(1e-5f), "a turned-away hit moved the bar.");

        Tick(0.07f);

        Assert.That(Fill().color, Is.EqualTo(Palette.Player), "the flash outlived its 0.06 s.");
    }

    // ---- The low state (rule 8) ------------------------------------------------------------------

    [Test]
    public void Low_PulsesInThePlayersFamily()
    {
        _bar.Set(0.2f);

        Assert.That(_bar.IsLow, Is.True);

        var seen = new HashSet<Color32>();

        for (int i = 0; i < 25; i++)
        {
            Tick(0.02f);

            Color colour = Fill().color;

            seen.Add(colour);

            Assert.That(Palette.IsDanger(colour), Is.False, $"the low bar drew the danger colour at tick {i}.");

            // Every channel between the two cyans, which is what "never red-orange" means for a mix.
            Assert.That(Between(colour.r, Palette.Player.r, Palette.PlayerLow.r), Is.True, $"red left the family at tick {i}.");
            Assert.That(Between(colour.g, Palette.Player.g, Palette.PlayerLow.g), Is.True, $"green left the family at tick {i}.");
            Assert.That(Between(colour.b, Palette.Player.b, Palette.PlayerLow.b), Is.True, $"blue left the family at tick {i}.");
        }

        Assert.That(seen.Count, Is.GreaterThanOrEqualTo(2), "the low bar did not beat.");
    }

    [Test]
    public void Low_TheBlockedFlashWins()
    {
        _bar.Set(0.2f);

        Tick(0.1f);

        _bar.FlashBlocked();

        Assert.That(Fill().color, Is.EqualTo(Palette.PlayerBlocked));
    }

    [Test]
    public void Low_NotAtAQuarterNorAtZero()
    {
        _bar.Set(HpBarView.LowFraction);

        Tick(0.1f);

        Assert.That(_bar.IsLow, Is.False, "a quarter is not below a quarter.");
        Assert.That(Fill().color, Is.EqualTo(Palette.Player));

        _bar.Set(0f);

        Tick(0.1f);

        Assert.That(_bar.IsLow, Is.False, "a dead player's bar beats.");
    }

    // ---- The segment (rule 10) -------------------------------------------------------------------

    [Test]
    public void Segment_LivesInTheWell()
    {
        _bar.Set(0.5f);
        _bar.SetGrantedShield(0.25f);

        Image segment = Field<Image>(_bar, "_shieldSegment");

        Assert.That(segment.transform.parent, Is.SameAs(Field<RectTransform>(_bar, "_well")));
        Assert.That(segment.color, Is.EqualTo(Palette.PlayerBlocked));
        Assert.That(segment.transform.GetSiblingIndex(), Is.GreaterThan(Fill().transform.GetSiblingIndex()),
            "the segment is not ahead of the fill.");
    }

    // ---- The asset -------------------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var bar = prefab.GetComponentInChildren<HpBarView>(true);
        var serialized = new SerializedObject(bar);

        Image Wired(string field)
        {
            var image = serialized.FindProperty(field).objectReferenceValue as Image;

            Assert.That(image, Is.Not.Null, $"HpBarView.{field} is not wired.");

            return image;
        }

        Assert.That(Wired("_frame").sprite.name, Is.EqualTo("HealthBar_Frame"));
        Assert.That(Wired("_frame").type, Is.EqualTo(Image.Type.Sliced));

        foreach (string field in new[] { "_fill", "_ghost" })
        {
            Assert.That(Wired(field).sprite.name, Is.EqualTo("Fill_Bar"), field);
            Assert.That(Wired(field).type, Is.EqualTo(Image.Type.Sliced), field);
        }

        Assert.That(Wired("_gloss").sprite.name, Is.EqualTo("Gloss_Streak"));
        Assert.That(serialized.FindProperty("_well").objectReferenceValue, Is.Not.Null, "the bar has no well.");
        Assert.That(serialized.FindProperty("_lowPulseHz").floatValue, Is.EqualTo(2f));

        var presenter = new SerializedObject(prefab.GetComponent<HudPresenter>());
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/UI/Fonts/LilitaOne_SDF.asset");

        foreach (string field in new[] { "_hpText", "_levelText" })
        {
            var text = presenter.FindProperty(field).objectReferenceValue as TMP_Text;

            Assert.That(text, Is.Not.Null, field);
            Assert.That(text.font, Is.SameAs(font), $"{field} is not in Lilita One.");
        }

        var star = presenter.FindProperty("_levelStar").objectReferenceValue as Image;

        Assert.That(star, Is.Not.Null, "HudPresenter has no star.");
        Assert.That(star.sprite.name, Is.EqualTo("LevelStar"));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private Image Fill() => Field<Image>(_bar, "_fill");

    private Image Ghost() => Field<Image>(_bar, "_ghost");

    private void Tick(float dt) =>
        typeof(HpBarView).GetMethod("Tick", Private).Invoke(_bar, new object[] { dt });

    private static bool Between(float value, float a, float b) =>
        value >= Mathf.Min(a, b) - 1e-4f && value <= Mathf.Max(a, b) + 1e-4f;

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);
}
