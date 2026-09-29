using UnityEngine;
using UnityEngine.UI;

// File-scoped, like Palette beside it: this type derives from nothing, least of all
// UnityEngine.Object, so Unity's script importer never has to find it and Traps §5's block-namespace
// rule does not apply.
namespace Soulvail.Game.Presentation;

/// <summary>
/// The top band's shared arithmetic, in dp (UI-01b): the numbers six components agree on, and the two
/// widths that depend on how wide the safe area turns out to be.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because the band is placed by six components on two canvases.</b>
/// <c>HudPresenter</c> lays out the star, the ring, the bar and its numbers; <c>XpBarView</c> the
/// strip; <c>AutoCastRow</c> its cells; <c>BossBarView</c> its band; <c>PausePresenter</c> the icon,
/// on <c>Pause.prefab</c>; and UI-01d's chips hang beside it. Each keeps its own Inspector fields, the
/// doors ledger row 4 wants tunable on a device, and each field's default is a constant here. So they
/// agree by construction rather than by copied literals, which is what
/// <c>BossBarView.TopDp</c> defaulting to <c>XpBarView.HeightDp</c> did for two of them since M4-04.
/// </para>
/// <para>
/// <b>Static, and pure.</b> Constants and three functions of their arguments — <c>StickShaper</c>'s
/// shape, which AR §7's ban on static <em>mutable</em> state does not reach. Nothing here reads the
/// screen: a caller measures its safe area and passes the width in, which is what lets
/// <c>HudLayoutTests</c> lay the band out at 20:9 and at 9:20 in one Editor.
/// </para>
/// <para>
/// <b>Landscape today, portrait next</b> (the owner's ruling of 2026-09-29). Every number is
/// anchored to the safe area's top edge, and the one piece whose width cannot be fixed — the health
/// bar, which shares a row with the star on the left and the pause button and the stage chip on the
/// right — takes what the row leaves. At 914 dp that is its full 240; at 9:20's 411 it is 201, which
/// is why the owner's mockup, drawn in portrait, shows it at 213.
/// </para>
/// </remarks>
public static class HudLayout
{
    /// <summary>From the safe area's left and right edges to the first piece, in dp.</summary>
    public const float MarginDp = 12f;

    /// <summary>The XP strip's track, across the very top of the safe area. Its fill is GD §16.1's 4 dp.</summary>
    public const float StripHeightDp = 8f;

    /// <summary>
    /// The top of the row under the strip: the star's, the pause button's and the stage chip's. Five dp
    /// clear of the strip.
    /// </summary>
    public const float RowTopDp = 13f;

    /// <summary>The level star, square. The row's centre line is half of it below <see cref="RowTopDp"/>.</summary>
    public const float StarSizeDp = 44f;

    /// <summary>The gap between two pieces of the row, in dp.</summary>
    public const float GapDp = 4f;

    /// <summary>The health bar's height, centred on the star's centre line.</summary>
    public const float BarHeightDp = 22f;

    /// <summary>The health bar's left edge: one gap past the star. The skill row's left edge too.</summary>
    public const float BarLeftDp = MarginDp + StarSizeDp + GapDp;

    /// <summary>
    /// The skill row's top: 8 dp under the bar, which sits on the star's centre line (UI-01b rule 12).
    /// </summary>
    /// <remarks>
    /// Eight rather than UI-01b's six since UI-01d: the stage chip's bottom edge is 53 dp down, and at
    /// 9:20 a long row reaches the chip's column, so the row starts clear of it.
    /// </remarks>
    public const float SkillRowTopDp = RowTopDp + ((StarSizeDp - BarHeightDp) * 0.5f) + BarHeightDp + 8f;

    /// <summary>The widest the health bar is ever drawn — the owner's starting size.</summary>
    public const float BarMaxWidthDp = 240f;

    /// <summary>The narrowest, on a safe area too small for the row, where it gives way to the chips.</summary>
    public const float BarMinWidthDp = 120f;

    /// <summary>
    /// What the right-hand end of the row keeps for itself, in dp from the safe area's right edge: the
    /// 12 dp margin, the 44 dp pause button, a 6 dp gap, UI-01d's 80 dp stage chip and an 8 dp gap
    /// before the bar.
    /// </summary>
    public const float RightReserveDp = 150f;

    /// <summary>The pause button, square, <see cref="MarginDp"/> in and <see cref="RowTopDp"/> down (UI-01d).</summary>
    public const float PauseSizeDp = 44f;

    /// <summary>The stage chip's width: room for <em>Wave 5/5</em> at 14 dp.</summary>
    public const float StageChipWidthDp = 80f;

    /// <summary>The stage chip's height: two lines, on the star's centre line.</summary>
    public const float StageChipHeightDp = 36f;

    /// <summary>The stage chip's right edge, in from the safe area's: past the pause button and a 6 dp gap.</summary>
    public const float StageChipRightDp = MarginDp + PauseSizeDp + 6f;

    /// <summary>The stage chip's top, which puts its centre on the row's centre line.</summary>
    public const float StageChipTopDp = RowTopDp + ((StarSizeDp - StageChipHeightDp) * 0.5f);

    /// <summary>The enemies-left chip's width.</summary>
    public const float EnemiesChipWidthDp = 48f;

    /// <summary>The enemies-left chip's height.</summary>
    public const float EnemiesChipHeightDp = 22f;

    /// <summary>The enemies-left chip's right edge, in from the safe area's: centred under the pause button.</summary>
    public const float EnemiesChipRightDp = MarginDp + ((PauseSizeDp - EnemiesChipWidthDp) * 0.5f);

    /// <summary>The enemies-left chip's top: one gap under the pause button.</summary>
    public const float EnemiesChipTopDp = RowTopDp + PauseSizeDp + GapDp;

    /// <summary>
    /// Where the right-hand column's switched-off readouts hang, the Essence counter and the Veilrot
    /// meter: under the boss band's row, where nothing else is (UI-01d rule 8).
    /// </summary>
    public const float EconomyTopDp = 124f;

    /// <summary>
    /// The top band's floor. Nothing in the band reaches lower: the skill row's pips, the lowest piece,
    /// end at 93.
    /// </summary>
    public const float BandBottomDp = 96f;

    /// <summary>Where the boss band hangs: one gap under the top band.</summary>
    public const float BossTopDp = BandBottomDp + GapDp;

    /// <summary>The widest the boss band is ever drawn — the owner's starting size.</summary>
    public const float BossMaxWidthDp = 400f;

    /// <summary>
    /// How wide the health bar is, given the safe area: the room between its left edge and the right
    /// cluster, between <see cref="BarMinWidthDp"/> and <paramref name="maxDp"/>.
    /// </summary>
    /// <param name="safeWidthDp">The safe area's width.</param>
    /// <param name="leftDp">Where the bar's left edge is, from the safe area's left edge.</param>
    /// <param name="maxDp">The widest it may be — the Inspector's width.</param>
    /// <param name="rightReserveDp">What the right-hand end of the row keeps.</param>
    /// <returns>
    /// The width in dp. A <paramref name="maxDp"/> that is not a usable width is read as
    /// <see cref="BarMaxWidthDp"/>, and a non-finite measurement — the safe area, the left edge or the
    /// reserve — answers that ceiling outright: a bar at its authored width is a better failure than a
    /// NaN rect, which never renders again (AR §18.3).
    /// </returns>
    public static float BarWidth(float safeWidthDp, float leftDp, float maxDp, float rightReserveDp)
    {
        float max = float.IsFinite(maxDp) && maxDp > 0f ? maxDp : BarMaxWidthDp;

        if (!float.IsFinite(safeWidthDp) || !float.IsFinite(leftDp) || !float.IsFinite(rightReserveDp))
        {
            return max;
        }

        float room = safeWidthDp - leftDp - rightReserveDp;

        return Mathf.Clamp(room, Mathf.Min(BarMinWidthDp, max), max);
    }

    /// <summary>
    /// How wide the boss band is, given the safe area: at most <paramref name="maxDp"/>, and inside the
    /// two margins.
    /// </summary>
    /// <returns>The width in dp, never below 0. A non-finite argument answers 0.</returns>
    public static float BossWidth(float safeWidthDp, float maxDp)
    {
        if (!float.IsFinite(safeWidthDp) || !float.IsFinite(maxDp))
        {
            return 0f;
        }

        return Mathf.Max(0f, Mathf.Min(maxDp, safeWidthDp - (2f * MarginDp)));
    }

    /// <summary>
    /// Scales a sliced image's borders with its rect's height, so a rounded end stays round at any size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The art is about twice the size it is drawn at, and not by a fixed amount.</b> A sliced
    /// image draws its borders at <c>border ÷ (sprite ppu ÷ canvas reference ppu × multiplier)</c>
    /// canvas units, so a 259 px frame whose ends are 130 px would draw 130-unit ends on a 22-unit bar.
    /// Setting the multiplier to <c>sprite height ÷ rect height</c> draws the ends at the bar's own
    /// scale, which is the scale <c>UI_v2/mockup_hud.py</c> composed the approved mockup at.
    /// </para>
    /// <para>
    /// A missing sprite, a rect with no height yet, or a non-finite answer leaves the image alone.
    /// </para>
    /// </remarks>
    public static void FitSlices(Image image)
    {
        if (image == null || image.sprite == null || image.transform is not RectTransform rect)
        {
            return;
        }

        float height = rect.rect.height;
        float multiplier = height > 0f ? image.sprite.rect.height / height : 0f;

        if (!float.IsFinite(multiplier) || multiplier <= 0f || Mathf.Approximately(image.pixelsPerUnitMultiplier, multiplier))
        {
            return;
        }

        image.pixelsPerUnitMultiplier = multiplier;
    }
}
