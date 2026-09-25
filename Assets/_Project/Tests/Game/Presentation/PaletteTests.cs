using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// GD §16.4's five reserved colours, the sets derived from them, and the rule that makes the file
/// worth having: <c>#FF4A1F</c> is danger and nothing else, ever.
/// </summary>
/// <remarks>
/// <para>
/// <b>This fixture is the enforcement.</b> A palette that merely exists is a list of numbers in one
/// place, which is what nine files each had already; what makes GD §16.4's <em>"one rule, enforced
/// globally"</em> true is that <see cref="Danger_IsUsedByNothingElse"/> can fail. The reserved
/// values are therefore written out here as literals rather than read from the file under test —
/// reading them from <c>Palette</c> would make every claim true by construction — while the derived
/// sets are asserted against the palette, because they answer to no design document and the claim
/// about them is that there is exactly one of each.
/// </para>
/// <para>
/// <b>Eight bits per channel is the resolution every comparison here uses</b>, because that is the
/// resolution a screen has and the one GD §16.4's hex is written at. <c>#22D3EE</c> has shipped in
/// this project as both <c>(0.133, 0.827, 0.933)</c> and <c>(34/255, 211/255, 238/255)</c> — the
/// first is the second rounded to three places, they are the same colour, and a row that compared
/// floats exactly would call that a difference.
/// </para>
/// <para>
/// <b>The reflection rows are what keep ledger row 6 closed rather than merely closing it.</b>
/// <see cref="Views_CarryNoSerializedColour"/> walks the nine readers for a serialized
/// <see cref="Color"/> and finds none, so the next screen that wants a colour has to add a member
/// here rather than a field of its own; <see cref="Views_KeepTheirTimingFields"/> is the other half,
/// because this task took colours and was not allowed to take anything else.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5). Nothing here needs them:
/// <see cref="HpBar_DrawsThePaletteColours"/> dresses its two graphics by reflection and drives
/// <c>Set</c> directly, which is the path the presenter takes anyway.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PaletteTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const BindingFlags Everything =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
    private const string ReticlePrefabPath = "Assets/_Project/Prefabs/UI/Reticle.prefab";
    private const string PalettePath = "Assets/_Project/Game/Presentation/Palette.cs";

    /// <summary>
    /// The nine files ledger row 6 collected: every type that held a colour of the palette's before
    /// M3-13a, and the ninth that nothing had ever counted.
    /// </summary>
    /// <remarks>
    /// <b>Nine and not twelve, and the difference is a ruling rather than an oversight.</b>
    /// <c>ReticleView</c>, <c>FocusGlowView</c> and <c>PlayerView</c> each hold a serialized cyan
    /// too — but it is <c>#4CE6FF</c>, not GD §16.4's <c>#22D3EE</c>, so moving them here would be a
    /// visible recolour of three world-space views rather than the relocation this task is.
    /// <see cref="Palette_IsNotTheOnlyCyanInTheBuild"/> is where that is recorded and pinned.
    /// </remarks>
    private static readonly Type[] Readers =
    {
        typeof(ThreatArrows),
        typeof(TelegraphRings),
        typeof(HpBarView),
        typeof(OfferCard),
        typeof(TreeNodeView),
        typeof(AutoCastRow),
        typeof(ZoneView),
        typeof(BulwarkView),
        typeof(EnemyHitFeedback),
    };

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void DestroyWorld()
    {
        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();
    }

    // ---- GD §16.4's five reserved meanings (rule 1) ----------------------------------------------

    [Test]
    public void Reserved_PlayerIsTheAuthoredCyan()
    {
        AssertHex(Palette.Player, 0x22, 0xD3, 0xEE, nameof(Palette.Player));

        // The two spellings already in the build, both of which are this colour. HpBarView and
        // ThreatArrows shipped the rounded one from M1-17 and M2-12a; the two VFX prefabs were
        // dressed with the exact one at M3-11c. Both are asserted, because "the four readers land on
        // the same byte they always did" is the whole of manual step 2's no-visible-change claim.
        AssertSameColour(Palette.Player, new Color(0.133f, 0.827f, 0.933f, 1f), "the M1-17 spelling");
        AssertSameColour(
            Palette.Player,
            new Color(0.13333334f, 0.827451f, 0.93333334f, 1f),
            "the value VFX_ConsecrateZone and VFX_Bulwark are dressed with");
    }

    [Test]
    public void Reserved_DangerIsTheAuthoredRedOrange()
    {
        AssertHex(Palette.Danger, 0xFF, 0x4A, 0x1F, nameof(Palette.Danger));

        // **And it is the value ThreatArrows shipped from M2-12a**, which is the claim rule 4 turns
        // on: the number moved file without moving. Written out rather than read from anywhere,
        // because the field it came from no longer exists — that is what "nothing forwards" means.
        AssertSameColour(
            Palette.Danger,
            new Color(1f, 0.290f, 0.122f, 1f),
            "the value ThreatArrows.Danger held from M2-12a to M3-13a");
    }

    [Test]
    public void Reserved_VeilrotEssenceAndNeutral()
    {
        AssertHex(Palette.Veilrot, 0xA8, 0x55, 0xF7, nameof(Palette.Veilrot));
        AssertHex(Palette.Essence, 0xFB, 0xBF, 0x24, nameof(Palette.Essence));

        // GD §16.4 gives Neutral a description rather than a hex — "desaturated bone" — so it is
        // asserted as a property rather than as a number. 0.2 is the band that admits a bone grey and
        // refuses every one of the other four.
        Color.RGBToHSV(Palette.Neutral, out _, out float saturation, out float value);

        Assert.That(
            saturation,
            Is.LessThan(0.2f),
            "Palette.Neutral is not desaturated. GD §16.4 puts everything that is not the player, "
                + "danger, Veilrot or Essence in a bone band, which is what stops the arena reading "
                + "as a second meaning.");

        Assert.That(value, Is.GreaterThan(0.2f).And.LessThan(0.8f), "bone is neither black nor white.");

        foreach (Color reserved in new[] { Palette.Player, Palette.Danger, Palette.Veilrot, Palette.Essence })
        {
            Color.RGBToHSV(reserved, out _, out float other, out _);

            Assert.That(
                other,
                Is.GreaterThan(0.2f),
                "a reserved colour is inside the neutral band, so the band no longer tells anything "
                    + "apart.");
        }
    }

    // ---- The rule the file exists for (rule 3) ---------------------------------------------------

    [Test]
    public void Danger_IsUsedByNothingElse()
    {
        FieldInfo[] members = ColourMembers();

        Assert.That(members.Length, Is.GreaterThan(1), "reflection found no palette members at all.");

        string[] danger = members
            .Where(f => Palette.IsDanger(Value(f)))
            .Select(f => f.Name)
            .ToArray();

        // **Ledger row 6's teeth.** M3-11c rule 10 stated this rule and could only test two prefabs'
        // serialized fields; this is the whole set, and it is the row the next colour has to pass.
        // M3-13b is the tempting caller by name: GD §16.2 wants a dying enemy to shift "toward red"
        // and GD §16.4 reserves this one for nothing else, ever.
        Assert.That(
            danger,
            Is.EqualTo(new[] { nameof(Palette.Danger) }),
            "more than one palette member is GD §16.4's reserved red-orange, or the reserved one is "
                + "not. #FF4A1F means a telegraph and may mean nothing else — a healing disc or a "
                + "health bar in it is the worst readability bug this project can ship.");
    }

    [Test]
    public void IsDanger_IgnoresAlpha()
    {
        var faded = new Color(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b, 0.4f);

        Assert.That(
            Palette.IsDanger(faded),
            Is.True,
            "a view that faded a telegraph ring to 40 % was told it had stopped using the reserved "
                + "colour. Every view in the game multiplies its own alpha (rule 8), so a rule that "
                + "could be stepped around by doing so would be a rule about a float.");

        Assert.That(Palette.IsDanger(Palette.Player), Is.False);
        Assert.That(Palette.IsDanger(Color.clear), Is.False, "transparent black is not danger.");
    }

    /// <summary>The non-finite door every float argument in this project owes.</summary>
    /// <remarks>
    /// False rather than a throw: this is a question a view asks about a colour it is already
    /// holding, so there is nothing to refuse and refusing would take a run down over a rendering
    /// mistake. The branch exists because <c>Mathf.Clamp01</c> does not catch a NaN — two
    /// comparisons, and every comparison against NaN is false (AR §18.3).
    /// </remarks>
    [Test]
    public void IsDanger_IsFalseForANonFiniteColour()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.That(Palette.IsDanger(new Color(bad, bad, bad, 1f)), Is.False, $"{bad} read as danger.");

            Assert.That(
                Palette.IsDanger(new Color(bad, Palette.Danger.g, Palette.Danger.b, 1f)),
                Is.False,
                $"a colour with {bad} in one channel and danger in the other two read as danger.");
        }
    }

    // ---- The derived sets (rules 1, 3) ------------------------------------------------------------

    [Test]
    public void Kinds_AreFourDistinctColours()
    {
        var kinds = new[]
        {
            Palette.KindPassive, Palette.KindActive, Palette.KindUpgrade, Palette.KindKeystone,
        };

        Assert.That(
            kinds.Distinct().Count(),
            Is.EqualTo(4),
            "two of CH §4's four kinds are the same colour, so the stripe on a card, a tree cell and "
                + "an auto-cast cell all say nothing.");

        Assert.That(kinds.Any(c => Palette.IsDanger(c)), Is.False, "a node kind is drawn in the danger colour.");
    }

    [Test]
    public void States_AreThreeDistinctColours()
    {
        var states = new[] { Palette.NodeLocked, Palette.NodeAvailable, Palette.NodeTaken };

        Assert.That(
            states.Distinct().Count(),
            Is.EqualTo(3),
            "two of M3-09d's three node states share a frame colour, and the frame is what CH §5.1's "
                + "\"the player's path highlighted\" is made of.");

        Assert.That(states.Any(c => Palette.IsDanger(c)), Is.False, "a node state is drawn in the danger colour.");
    }

    [Test]
    public void Heal_IsInThePlayersFamily()
    {
        Color.RGBToHSV(Palette.Heal, out float heal, out _, out _);
        Color.RGBToHSV(Palette.Player, out float player, out _, out _);

        float degrees = Mathf.Abs(Mathf.DeltaAngle(heal * 360f, player * 360f));

        // GD §16.4 puts the player and the things that keep them safe in one cyan, and a patch of
        // healing ground is exactly that. Today the two are the same value — VFX_ConsecrateZone was
        // dressed #22D3EE at M3-11c — and Heal is a member of its own rather than a second name for
        // Player, because the day the owner wants healing ground a shade off is one line here.
        Assert.That(
            degrees,
            Is.LessThan(40f),
            "Palette.Heal has left the player's hue family, so a Consecrate no longer reads as a "
                + "safe place by the same rule the health bar does.");

        Assert.That(Palette.IsDanger(Palette.Heal), Is.False, "healing ground is drawn in #FF4A1F.");
    }

    [Test]
    public void HeldFocus_IsDimmerThanDanger()
    {
        Color.RGBToHSV(Palette.HeldFocus, out _, out _, out float held);
        Color.RGBToHSV(Palette.Danger, out _, out _, out float danger);

        Assert.That(held, Is.LessThan(danger));

        Assert.That(
            Palette.IsDanger(Palette.HeldFocus),
            Is.False,
            "the arrow for an enemy the player deliberately tapped is drawn in the colour reserved "
                + "for a thing about to hit them.");
    }

    /// <summary>
    /// The correction the spec's own API comment needed: <c>HeldFocus</c> is not a dimmer cyan, it is
    /// <see cref="Palette.Player"/>.
    /// </summary>
    /// <remarks>
    /// <b>The two were already byte-identical before this task</b> — <c>ThreatArrows.HeldFocus</c>
    /// and <c>HpBarView._fillColour</c> both held <c>(0.133, 0.827, 0.933, 1)</c> — and
    /// <see cref="HeldFocus_IsDimmerThanDanger"/> passed anyway, on 0.933 against 1.0, which is a row
    /// going green for a reason that has nothing to do with what it is named after. What makes a held
    /// focus look dimmer on screen is <c>ThreatArrows.Place</c> ramping alpha by distance, and an
    /// out-of-range focus is far away by definition. Changing the value to make the comment true
    /// would have been a visible change, which manual step 2 forbids; the comment was wrong.
    /// </remarks>
    [Test]
    public void HeldFocus_IsThePlayersOwnColour()
    {
        Assert.That(
            Palette.HeldFocus,
            Is.EqualTo(Palette.Player),
            "HeldFocus is no longer the player's own cyan. GD §16.4 gives the enemy the player "
                + "*picked* the player's colour, because \"the thing you picked is over there\" is "
                + "the more specific statement — if this is now a colour of its own, ThreatArrows' "
                + "two meanings need re-ruling rather than re-tinting.");

        // And the dimming is the view's, which is where the spec's word belonged.
        Assert.That(
            typeof(ThreatArrows).GetField("FarAlpha", BindingFlags.Static | BindingFlags.NonPublic),
            Is.Not.Null,
            "ThreatArrows lost its distance alpha ramp, which is the only thing that made a held "
                + "focus read dimmer than a threat. If the ramp is gone, HeldFocus has to become a "
                + "colour of its own or the two arrows are indistinguishable.");
    }

    // ---- The seam rule 4 closes -------------------------------------------------------------------

    [Test]
    public void ThreatArrows_HoldsNoColourOfItsOwn()
    {
        Assert.That(
            typeof(ThreatArrows).GetFields(Everything).Where(f => f.FieldType == typeof(Color)),
            Is.Empty,
            "ThreatArrows holds a Color again. It held Danger and HeldFocus as public statics from "
                + "M2-12a and TelegraphRings read the first across the Views/Presentation seam, "
                + "which was ledger row 6's original complaint.");

        // **And no forwarder**, which is the half that matters: a forwarding property would be two
        // names for one colour, which is the state the row describes with an extra hop.
        foreach (string gone in new[] { "Danger", "HeldFocus" })
        {
            Assert.That(
                typeof(ThreatArrows).GetMember(gone, Everything),
                Is.Empty,
                $"ThreatArrows.{gone} is back. One place for the number was the point — if a "
                    + "forwarder is genuinely wanted, it has to argue against rule 4 rather than "
                    + "appear.");
        }
    }

    [Test]
    public void TelegraphRings_ReadsThePalette()
    {
        FieldInfo danger = typeof(Palette).GetField(nameof(Palette.Danger));

        Assert.That(danger, Is.Not.Null);

        Assert.That(
            Reads(typeof(TelegraphRings), danger),
            Is.True,
            "TelegraphRings no longer reads Palette.Danger, so the colour a telegraph ring is drawn "
                + "in has moved somewhere this file cannot see.");

        // The dependency direction rule 4 fixes: Views and Presentation stop being mutually
        // dependent and both read one file that reads nobody. Palette itself is what must stay a
        // leaf — a colour that had to ask a view a question would be the seam back again.
        Assert.That(
            typeof(Palette).GetFields(Everything).Select(f => f.FieldType).Distinct(),
            Is.EqualTo(new[] { typeof(Color) }),
            "Palette holds something that is not a Color, so it has stopped being a leaf.");
    }

    // ---- The fields that went, and the ones that stayed (rule 5) ---------------------------------

    [Test]
    public void Views_CarryNoSerializedColour()
    {
        foreach (Type reader in Readers)
        {
            FieldInfo[] serialized = reader.GetFields(Everything)
                .Where(IsColourish)
                .Where(IsSerialized)
                .ToArray();

            Assert.That(
                serialized.Select(f => f.Name),
                Is.Empty,
                $"{reader.Name} carries a serialized Color. Every colour in these nine files is "
                    + "Palette's as of M3-13a — a field defaulted from the palette and then dressed "
                    + "differently on a prefab is the placeholder problem with an extra step, and "
                    + "worse: a read-back row would agree with the dressed value whatever it was "
                    + "(Traps §7). A tenth colour is a member of Palette and a row here.");
        }

        // **The three this row is deliberately not told about**, so it cannot go red on views it was
        // never scoped to. See Palette_IsNotTheOnlyCyanInTheBuild for the ruling and the reason.
        foreach (Type outside in new[] { typeof(ReticleView), typeof(FocusGlowView), typeof(PlayerView) })
        {
            Assert.That(
                Readers,
                Has.No.Member(outside),
                $"{outside.Name} was added to the nine readers without the ruling that keeps its "
                    + "#4CE6FF out of the palette being revisited.");
        }
    }

    /// <summary>
    /// Rule 5's other half: this task took colours and was allowed to take nothing else.
    /// </summary>
    /// <remarks>
    /// <b>The spec's list named <c>_secondsShown</c> and no reader has one.</b> That field is on
    /// <c>FirstActiveHint</c> and <c>OverflowToast</c>, neither of which ever held a colour, so the
    /// list here is the timing and feel fields that actually live on the nine — including the two
    /// alphas, which are feel numbers rather than colour and are exactly what rule 8 says a view
    /// keeps.
    /// </remarks>
    [Test]
    public void Views_KeepTheirTimingFields()
    {
        var kept = new (Type Type, string Field)[]
        {
            (typeof(EnemyHitFeedback), "_flashSeconds"),
            (typeof(EnemyHitFeedback), "_dissolveSeconds"),
            (typeof(HpBarView), "_blockedSeconds"),
            (typeof(BulwarkView), "_fadeSeconds"),
            (typeof(BulwarkView), "_maxAlpha"),
            (typeof(ZoneView), "_pulseSeconds"),
            (typeof(ZoneView), "_baseAlpha"),
            (typeof(ZoneView), "_pulseAlpha"),
        };

        foreach ((Type type, string field) in kept)
        {
            FieldInfo info = type.GetField(field, Private);

            Assert.That(
                info,
                Is.Not.Null,
                $"{type.Name}.{field} is gone. M3-13a removed colours and nothing else — a timing or "
                    + "an alpha leaving with them is this task taking a feel decision it was not "
                    + "asked for (M8-01 owns those).");

            Assert.That(IsSerialized(info), Is.True, $"{type.Name}.{field} stopped being serialized.");
        }
    }

    [Test]
    public void HpBar_DrawsThePaletteColours()
    {
        var root = new GameObject(nameof(HpBarView));

        _spawned.Add(root);

        var bar = root.AddComponent<HpBarView>();

        Image fill = Graphic(root, "Fill");
        Image ghost = Graphic(root, "Ghost");

        Set(bar, "_fill", fill);
        Set(bar, "_ghost", ghost);

        bar.Set(0.5f);

        Assert.That(
            fill.color,
            Is.EqualTo(Palette.Player),
            "the health bar is not the player's cyan. It held _fillColour as a serialized field from "
                + "M1-17, dressed on Hud.prefab at the same value; the field is gone and this is the "
                + "one place the number lives.");

        bar.FlashBlocked();

        Assert.That(
            fill.color,
            Is.EqualTo(Palette.PlayerBlocked),
            "a turned-away hit no longer flashes the brightened cyan. CC §7 gives the Oathbound half "
                + "a second of i-frames after every hit, and a player who cannot see them spent "
                + "concludes the game dropped a hit.");

        Assert.That(
            Palette.IsDanger(Palette.PlayerBlocked),
            Is.False,
            "the blocked flash is #FF4A1F, which GD §16.4 forbids on the health bar by name.");
    }

    /// <summary>
    /// GD §16.3's white hit flash — the ninth reader, and the one no ledger row had ever counted.
    /// </summary>
    /// <remarks>
    /// <b>Asserted through the IL rather than through a live flash</b>, and that is the shape rather
    /// than a shortcut: <c>EnemyHitFeedback.SetColour</c> writes into a <c>MaterialPropertyBlock</c>
    /// built in <c>Awake</c>, which never runs in EditMode (Traps §5), so a behavioural row would
    /// have to build a renderer, a material and an <c>EnemyView</c> to assert one constant. What the
    /// row is actually about is that the constant is white and that the flash reads it, and both are
    /// checked here exactly.
    /// </remarks>
    [Test]
    public void HitFlash_IsWhite()
    {
        Assert.That(Palette.HitFlash, Is.EqualTo(Color.white));

        AssertHex(Palette.HitFlash, 0xFF, 0xFF, 0xFF, nameof(Palette.HitFlash));

        FieldInfo flash = typeof(Palette).GetField(nameof(Palette.HitFlash));

        Assert.That(
            Reads(typeof(EnemyHitFeedback), flash),
            Is.True,
            "EnemyHitFeedback stopped reading Palette.HitFlash, so the white a hit flashes has moved "
                + "back out of the palette. It held _flashColour as a serialized field from M1-12 "
                + "and nothing had ever counted it as a reader of anything.");

        // And the archetype tint it returns to is content, which is what keeps this the only colour
        // in that file the palette owns (the spec's Out of scope, and EnemyLook's own remarks).
        Assert.That(
            typeof(EnemyHitFeedback).GetMethod("SetArchetypeLook"),
            Is.Not.Null,
            "EnemyHitFeedback lost SetArchetypeLook, so a Bloater's rust has nowhere to arrive from "
                + "and the palette would be the only colour an enemy could be.");
    }

    // ---- What kind of thing the file is (rules 2, 7, 8) -------------------------------------------

    [Test]
    public void Palette_HoldsNoMutableState()
    {
        Assert.That(typeof(Palette).IsAbstract && typeof(Palette).IsSealed, Is.True, "Palette is not static.");

        foreach (FieldInfo field in typeof(Palette).GetFields(Everything))
        {
            Assert.That(field.IsStatic, Is.True, $"Palette.{field.Name} is an instance field.");
            Assert.That(field.IsInitOnly, Is.True, $"Palette.{field.Name} is not readonly.");

            // A value type is what makes readonly mean something: a readonly reference to a mutable
            // object is a handle every caller could write through, which is the static mutable state
            // AR §7 actually bans.
            Assert.That(
                field.FieldType.IsValueType,
                Is.True,
                $"Palette.{field.Name} is a reference type, so `readonly` protects the handle and "
                    + "not the thing — which is the static the ban is about.");
        }

        foreach (PropertyInfo property in typeof(Palette).GetProperties(Everything))
        {
            Assert.That(
                property.CanWrite,
                Is.False,
                $"Palette.{property.Name} has a setter, so the one sanctioned static is mutable "
                    + "after all and AR §7's row no longer describes it.");
        }
    }

    [Test]
    public void Palette_IsEntirelyOpaque()
    {
        foreach (FieldInfo field in ColourMembers())
        {
            Assert.That(
                Value(field).a,
                Is.EqualTo(1f),
                $"Palette.{field.Name} is not opaque. Rule 8: a view that wants transparency "
                    + "multiplies its own — ThreatArrows ramps by distance, ZoneView carries a "
                    + "resting and a pulse alpha, BulwarkView a maximum, and PlayerView._swingColour "
                    + "is authored at 0.25. A palette that grew a curve would be a style system, and "
                    + "GD §16.4 is five rows.");
        }
    }

    /// <summary>
    /// M6-03b rule 6: <c>Palette_HasTheColourNobodyReadsYet</c>, retired — the meter is its reader.
    /// </summary>
    /// <remarks>
    /// That row was narrowed at M6-03a to the one colour it was still true of, and there is nothing
    /// left for it to say. Replaced rather than narrowed a second time (ledger row 8).
    /// </remarks>
    [Test]
    public void Palette_VeilrotIsTheMetersColour()
    {
        FieldInfo member = typeof(Palette).GetField(nameof(Palette.Veilrot));

        Assert.That(Sweep(member), Does.Contain(typeof(VeilrotMeterView)), "the meter does not read Palette.Veilrot.");

        AssertHex(Palette.Veilrot, 0xA8, 0x55, 0xF7, nameof(Palette.Veilrot));

        Assert.That(
            Palette.IsDanger(Palette.Veilrot),
            Is.False,
            "Palette.Veilrot is the danger colour, so the one readout up for a whole run breaks GD §16.4.");
    }

    /// <summary>
    /// M6-05b rule 8: the Pact frame reads <see cref="Palette.Veilrot"/>, and the palette gained no
    /// member for it.
    /// </summary>
    /// <remarks>
    /// GD §16.4 makes one violet mean Veilrot, Pacts and corruption. Nineteen is the count as
    /// M6-05b found it; a twentieth colour is a decision, and this row is where it has to be argued.
    /// </remarks>
    [Test]
    public void Palette_ThePactFrameIsNotATenthColour()
    {
        Assert.That(
            Sweep(typeof(Palette).GetField(nameof(Palette.Veilrot))),
            Does.Contain(typeof(OfferCard)),
            "the Pact frame does not read Palette.Veilrot.");

        Assert.That(
            typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static).Count(f => f.FieldType == typeof(Color)),
            Is.EqualTo(19),
            "Palette gained a member. A Pact is GD §16.4's violet, not a second one.");
    }

    /// <summary>
    /// Ledger row 8, closed: both reserved colours that shipped with no reader have one, and neither
    /// summary still says otherwise.
    /// </summary>
    [Test]
    public void Palette_BothReservedColoursHaveReaders()
    {
        string[] source = System.IO.File.ReadAllLines(PalettePath);

        foreach (string name in new[] { nameof(Palette.Veilrot), nameof(Palette.Essence) })
        {
            Assert.That(Sweep(typeof(Palette).GetField(name)), Is.Not.Empty, $"Palette.{name} has no reader.");

            int field = Array.FindIndex(source, line => line.Contains($"public static readonly Color {name} "));

            Assert.That(field, Is.GreaterThan(4), $"Palette.{name} is not declared in {PalettePath}.");

            // The summary above the field, however many lines it runs to.
            int start = Array.FindLastIndex(source, field, line => line.Contains("<summary>"));
            string summary = string.Join(" ", source.Skip(start).Take(field - start));

            Assert.That(summary, Does.Not.Contain("No reader yet"), $"Palette.{name}'s summary still says it has no reader.");
        }
    }

    [Test]
    public void Palette_EssenceHasTwoReadersAndItsSummarySaysSo()
    {
        FieldInfo member = typeof(Palette).GetField(nameof(Palette.Essence));

        Type[] readers = Sweep(member);

        // GD §16.4's reward gold on the run-end payout (M4-06) and on the Sanctum's balance (M6-03a).
        // ServiceRow reads it too, for the prices, and is the Sanctum's own control; HudPresenter
        // joined at M6-03b for GD §16.1's corner counter; ClassSelectPresenter and ClassCard at
        // M6-09b, for the Shard balance and a price that can be paid.
        Assert.That(readers, Does.Contain(typeof(RunEndPresenter)));
        Assert.That(readers, Does.Contain(typeof(SanctumPresenter)));
        Assert.That(
            readers,
            Is.EquivalentTo(new[]
            {
                typeof(RunEndPresenter), typeof(SanctumPresenter), typeof(ServiceRow), typeof(HudPresenter),
                typeof(ClassSelectPresenter), typeof(ClassCard),
            }));

        // And the summary says so. XML docs do not exist at run time, so the source is read: the
        // three lines above the field are its summary.
        string[] source = System.IO.File.ReadAllLines(PalettePath);
        int field = Array.FindIndex(source, line => line.Contains("public static readonly Color Essence"));

        Assert.That(field, Is.GreaterThan(3), $"Palette.Essence is not declared in {PalettePath}.");

        string summary = string.Join(" ", source.Skip(field - 4).Take(4));

        Assert.That(summary, Does.Not.Contain("No reader yet"), "rule 10: the summary was wrong since M4-06.");
        Assert.That(summary, Does.Contain("run-end payout").And.Contain("Sanctum"));
    }

    /// <summary>
    /// The parking-lot line's own diagnosis, asserted: the nine-type list could not see M4-06's
    /// reader, and the sweep can.
    /// </summary>
    [Test]
    public void Palette_TheSweepWouldHaveCaughtM4_06()
    {
        FieldInfo member = typeof(Palette).GetField(nameof(Palette.Essence));

        Type[] withoutTheSanctum = Sweep(member)
            .Where(type => type != typeof(SanctumPresenter) && type != typeof(ServiceRow))
            .ToArray();

        Assert.That(withoutTheSanctum, Does.Contain(typeof(RunEndPresenter)), "the sweep finds M4-06's reader.");
        Assert.That(Readers, Has.No.Member(typeof(RunEndPresenter)), "and the hand-kept list never did.");
    }

    /// <summary>
    /// The finding this task made and did not act on: there are <em>two</em> player cyans in the
    /// shipped build, and only one of them is GD §16.4's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReticleView._colour</c>, <c>FocusGlowView._colour</c> and <c>PlayerView._swingColour</c>
    /// all hold <c>#4CE6FF</c> — a lighter cyan that predates GD §16.4's hex — while
    /// <c>HpBarView</c>, <c>ThreatArrows</c> and both M3-11c prefabs hold <c>#22D3EE</c>. That is
    /// exactly the drift ledger row 6 exists to catch, arrived at <em>before</em> the palette rather
    /// than after it, and neither the row nor M3-13a's spec had counted it.
    /// </para>
    /// <para>
    /// <b>They are out of scope on purpose.</b> Moving them onto <see cref="Palette.Player"/> would
    /// recolour the reticle, the focus glow and the swing arc on every frame of every run, which
    /// manual step 2 forbids and the spec's <em>Out of scope</em> — <em>"any new colour; this task
    /// moves what exists"</em> — rules out. Which cyan the player's own world-space views should be
    /// is the owner's call, and it is a parking-lot line rather than a ledger row because no task
    /// owns it. This row is what stops it being quietly tidied either way.
    /// </para>
    /// </remarks>
    [Test]
    public void Palette_IsNotTheOnlyCyanInTheBuild()
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var reticle = AssetDatabase.LoadAssetAtPath<GameObject>(ReticlePrefabPath);

        Assert.That(player, Is.Not.Null, $"No prefab at {PlayerPrefabPath}.");
        Assert.That(reticle, Is.Not.Null, $"No prefab at {ReticlePrefabPath}.");

        var dressed = new (string Name, Color Colour)[]
        {
            ("PlayerView._swingColour", Field<Color>(player.GetComponentInChildren<PlayerView>(true), "_swingColour")),
            ("FocusGlowView._colour", Field<Color>(player.GetComponentInChildren<FocusGlowView>(true), "_colour")),
            ("ReticleView._colour", Field<Color>(reticle.GetComponentInChildren<ReticleView>(true), "_colour")),
        };

        // Asserted as the floats the three actually ship rather than as a hex, deliberately: 0.3 and
        // 0.9 sit within half an eight-bit step of two different bytes, so naming a hex here would
        // make the row's failure message an argument about rounding rather than about a colour. What
        // matters is that all three hold one value, that it is not the palette's, and that nobody has
        // moved it.
        var lighter = new Color(0.3f, 0.9f, 1f, 1f);

        foreach ((string name, Color colour) in dressed)
        {
            Assert.That(colour.r, Is.EqualTo(lighter.r).Within(1e-4f), $"{name} red moved.");
            Assert.That(colour.g, Is.EqualTo(lighter.g).Within(1e-4f), $"{name} green moved.");
            Assert.That(colour.b, Is.EqualTo(lighter.b).Within(1e-4f), $"{name} blue moved.");

            Assert.That(
                SameColour(colour, Palette.Player),
                Is.False,
                $"{name} is now GD §16.4's #22D3EE. If that was deliberate it is a visible recolour "
                    + "and belongs in a task's As built with a playtest behind it; if it was not, "
                    + "three world-space views have quietly changed colour.");

            Assert.That(Palette.IsDanger(colour), Is.False, $"{name} is drawn in the danger colour.");
        }

        // The one that carries an alpha, which is why it could not have moved into the palette as-is
        // even if the hue had matched: rule 8 says every member here is opaque and a view multiplies
        // its own.
        Assert.That(dressed[0].Colour.a, Is.EqualTo(0.25f).Within(1e-4f));
    }

    [Test]
    public void Neutral_AgreesWithTheEnemyDefault()
    {
        // **They agree and they are still two things.** EnemyLook.BoneGrey is M_BoneGrey's own base
        // colour, written down twice on purpose so an archetype nobody authored a tint for is drawn
        // exactly as the untinted prefab was, and EnemyLookTests' Husk row pins it against the
        // shipped material. Folding it into the palette would mean retuning Neutral silently
        // recoloured every unauthored archetype and reddened a row about a material. This is what
        // says so the day either one moves.
        Assert.That(
            SameColour(Palette.Neutral, EnemyLook.Default.Tint),
            Is.True,
            "Palette.Neutral and EnemyLook.Default.Tint have diverged. They are deliberately two "
                + "values of one colour — the palette's is GD §16.4's \"desaturated bone\" and the "
                + "look book's is M_BoneGrey's own base colour — so a difference means one of the "
                + "two was retuned without the other, and an unauthored enemy is now a different "
                + "grey from the rest of the arena.");

        Assert.That(
            typeof(EnemyLook).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Any(f => f.FieldType == typeof(Color)),
            Is.True,
            "EnemyLook stopped holding its own bone grey. If it now reads Palette.Neutral, the "
                + "material-agreement row in EnemyLookTests is asserting the palette against itself.");
    }

    /// <summary>
    /// The four rows this task rewrote are still there, still tests, and still assert what they
    /// asserted — which is what distinguishes M3-13a from a task that quietly broke four suites.
    /// </summary>
    /// <remarks>
    /// <b>Written so a deleted row fails it, not merely a renamed one.</b> Each entry names the
    /// fixture, the method and the palette member the rewritten body must read, so a row that is
    /// removed goes red on the lookup, a row that is renamed goes red on the same lookup, and a row
    /// that is gutted into a tautology goes red on the IL. <b>Five rather than the spec's four</b>:
    /// <c>Tree_TintsByKind</c> compared <c>TreeNodeView</c>'s four serialized tints against
    /// <c>OfferCard</c>'s field by field and could not survive the fields going either.
    /// </remarks>
    [Test]
    public void RewrittenRows_StillAssertWhatTheyAsserted()
    {
        var rows = new (string Fixture, string Row, string Member)[]
        {
            ("Soulvail.Tests.Game.Views.SkillViewTests", "Views_UseNoDangerColour", nameof(Palette.Player)),
            ("Soulvail.Tests.Game.Presentation.LevelUpPresenterTests", "Card_TintsByKind", nameof(Palette.KindPassive)),
            ("Soulvail.Tests.Game.Presentation.TreeViewPresenterTests", "Tree_TintsByKind", nameof(Palette.KindKeystone)),
            ("Soulvail.Tests.Game.Presentation.TreeViewPresenterTests", "Tree_FrameColoursByState", nameof(Palette.NodeLocked)),
            ("Soulvail.Tests.Game.Presentation.AutoCastRowTests", "Row_TintsByKind", nameof(Palette.KindActive)),
        };

        foreach ((string fixture, string row, string member) in rows)
        {
            Type type = typeof(PaletteTests).Assembly.GetType(fixture);

            Assert.That(type, Is.Not.Null, $"{fixture} is gone.");

            MethodInfo method = type.GetMethod(row, BindingFlags.Instance | BindingFlags.Public);

            Assert.That(
                method,
                Is.Not.Null,
                $"{fixture}.{row} was deleted or renamed. M3-13a rewrote it rather than removing it, "
                    + "because a task that silently dropped rows from four other tasks' suites would "
                    + "be indistinguishable from one that broke them.");

            Assert.That(
                method.GetCustomAttribute<TestAttribute>(),
                Is.Not.Null,
                $"{fixture}.{row} is no longer a [Test], so it is present and never runs.");

            Assert.That(
                Reads(method, typeof(Palette).GetField(member)),
                Is.True,
                $"{fixture}.{row} no longer reads Palette.{member}. It used to assert against a "
                    + "serialized field; the rewrite moved the claim onto the palette, and a body "
                    + "that reads neither is asserting nothing.");
        }
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>Every public <see cref="Color"/> the palette exposes, in declaration order.</summary>
    private static FieldInfo[] ColourMembers() =>
        typeof(Palette).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(Color))
            .ToArray();

    private static Color Value(FieldInfo field) => (Color)field.GetValue(null);

    /// <summary>Whether <paramref name="field"/> is a colour Unity would write into an asset.</summary>
    private static bool IsColourish(FieldInfo field) =>
        field.FieldType == typeof(Color) || field.FieldType == typeof(Color[]);

    /// <summary>
    /// Unity's own serialization rule, as far as a colour needs it: public unless opted out, or
    /// private with <c>[SerializeField]</c>. A static field is never serialized.
    /// </summary>
    private static bool IsSerialized(FieldInfo field) =>
        !field.IsStatic
        && (field.GetCustomAttribute<SerializeField>() is not null
            || (field.IsPublic && field.GetCustomAttribute<NonSerializedAttribute>() is null));

    /// <summary>Whether <paramref name="type"/>, or anything nested in it, loads <paramref name="field"/>.</summary>
    /// <remarks>
    /// <c>SkillBarPresenterTests.Calls</c>' technique, for fields rather than methods: every
    /// <c>ldsfld</c> in the type's own bodies is resolved back through the module that holds it,
    /// which is the only way to say "this class reads that number" without a behavioural path to
    /// drive. A token that resolves to nothing is skipped rather than believed — a four-byte window
    /// after an opcode byte that happened to fall inside an operand is not a field load.
    /// </remarks>
    private static bool Reads(Type type, FieldInfo field)
    {
        var types = new List<Type> { type };

        types.AddRange(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        return types.Any(candidate =>
            candidate.GetMethods(Everything).Cast<MethodBase>()
                .Concat(candidate.GetConstructors(Everything))
                .Any(method => Reads(method, field)));
    }

    /// <summary>
    /// Every top-level type in <c>Soulvail.Game</c> that loads <paramref name="field"/>, the palette
    /// itself excepted (M6-03a rule 11).
    /// </summary>
    /// <remarks>
    /// Top-level only, because <see cref="Reads(Type, FieldInfo)"/> already walks a type's nested
    /// ones — a closure's <c>ldsfld</c> is credited to the class that wrote the lambda.
    /// </remarks>
    private static Type[] Sweep(FieldInfo field) =>
        typeof(Palette).Assembly.GetTypes()
            .Where(type => type.DeclaringType is null && type != typeof(Palette))
            .Where(type => Reads(type, field))
            .ToArray();

    /// <inheritdoc cref="Reads(Type, FieldInfo)" />
    private static bool Reads(MethodBase method, FieldInfo field)
    {
        Assert.That(field, Is.Not.Null, "the palette member this row names is gone.");

        byte[] il = method.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            return false;
        }

        for (int i = 0; i + 4 < il.Length; i++)
        {
            // ldsfld (0x7E) and ldsflda (0x7F). Nothing else reaches a static field's value.
            if (il[i] != 0x7E && il[i] != 0x7F)
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveField(BitConverter.ToInt32(il, i + 1)) == field)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Not a token; the window fell inside somebody else's operand.
            }
        }

        return false;
    }

    /// <summary>
    /// Asserts <paramref name="colour"/> is the named hex to eight bits per channel, which is the
    /// resolution a screen has and the one GD §16.4 writes its colours at.
    /// </summary>
    private static void AssertHex(Color colour, int r, int g, int b, string name)
    {
        Assert.That(Byte(colour.r), Is.EqualTo(r), $"{name} red is #{Byte(colour.r):X2}, not #{r:X2}.");
        Assert.That(Byte(colour.g), Is.EqualTo(g), $"{name} green is #{Byte(colour.g):X2}, not #{g:X2}.");
        Assert.That(Byte(colour.b), Is.EqualTo(b), $"{name} blue is #{Byte(colour.b):X2}, not #{b:X2}.");

        Assert.That(colour.a, Is.EqualTo(1f), $"{name} is not opaque (rule 8).");
    }

    private static void AssertSameColour(Color actual, Color expected, string what) =>
        Assert.That(
            SameColour(actual, expected),
            Is.True,
            $"the palette no longer agrees with {what}, so this task moved a value rather than "
                + "relocating one — which manual step 2's no-visible-change claim rests on.");

    private static bool SameColour(Color a, Color b) =>
        Byte(a.r) == Byte(b.r) && Byte(a.g) == Byte(b.g) && Byte(a.b) == Byte(b.b);

    private static int Byte(float channel) => Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f);

    /// <summary>One <see cref="Image"/> on a child of <paramref name="root"/>, named for a message.</summary>
    private static Image Graphic(GameObject root, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));

        child.transform.SetParent(root.transform, worldPositionStays: false);

        return child.AddComponent<Image>();
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
}
