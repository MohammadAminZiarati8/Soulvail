using UnityEngine;

// File-scoped, unlike HpBarView and AutoCastRow beside it: this type derives from nothing, least of
// all UnityEngine.Object, so Unity's script importer never has to find it and Traps §5's
// block-namespace rule does not apply. The same split EnemyLook and EnemyDefinition make.
namespace Soulvail.Game.Presentation;

/// <summary>
/// GD §16.4's colour language, in the one place the rule can be enforced from: five reserved
/// meanings, and every tint derived from them.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because the numbers had started being copied.</b> GD §16.4 says <em>"one rule,
/// enforced globally"</em>, and before this file the player's cyan was written down in four places,
/// CH §4's four kind tints in three, and <c>#FF4A1F</c> in one place that two others read across a
/// namespace seam. Ledger row 6 predicted exactly that shape — <em>"the number being copied on the
/// third use, which is where palettes stop being one"</em> — and this is the file that ends it. The
/// enforcement is not the existence of the file: it is <c>PaletteTests</c>, which can fail.
/// </para>
/// <para>
/// <b>Static, and the precedent was shipped rather than invented here.</b> <c>ThreatArrows.Danger</c>
/// has been a <c>public static readonly Color</c> since M2-12a and <c>TelegraphRings</c> read it,
/// which is the whole of ledger row 6's original complaint — and the complaint was about the
/// <em>seam</em>, not the storage. AR §7's ban is on static <b>mutable</b> state and on service
/// location; a <c>readonly Color</c> is neither, holds nothing a run owns, and is inert under a
/// disabled domain reload because there is nothing to reset. Two alternatives were weighed and
/// rejected: an <b>injected instance</b>, which <c>TreeNodeView</c> and <c>ZoneView</c> cannot have —
/// they are pooled templates instantiated from a prefab field and nothing injects them individually
/// — and a <b><c>ScriptableObject</c> referenced per prefab</b>, which is five more dressing steps
/// and treats a design law as a tuning knob, when the thing that makes <c>#FF4A1F</c> load-bearing is
/// precisely that nobody may retune it. Recorded as the one sanctioned static in <b>AR §7</b>, beside
/// the ban itself, so the next static has to argue against this rather than against nothing.
/// </para>
/// <para>
/// <b>Every value is opaque, and a view that wants transparency multiplies its own.</b> Alpha is 1 on
/// every member by construction — <see cref="Rgb"/> is the only way a colour is made here — and
/// <see cref="Palette"/> has no ramp, no gradient and no per-tier variant, because a palette that
/// grew a curve would be a style system and GD §16.4 is five rows. The views that fade already own
/// their own number and keep it: <c>ThreatArrows</c> ramps alpha by distance, <c>ZoneView</c> carries
/// a resting and a pulse alpha, <c>BulwarkView</c> a maximum, <c>EnemyHitFeedback</c> drives one down
/// over a dissolve, and <c>PlayerView._swingColour</c> is authored at 0.25. That last one is the shape
/// a future caller takes: it multiplies, it does not ask the palette for a faded colour.
/// </para>
/// <para>
/// <b>The tenth colour is a field here, which is how ledger row 6 closes rather than pauses.</b>
/// M3-13b's damage tint, M4-04's boss segments, M6-04's Veilrot meter and M6-05's Pact frames each
/// add a member and a test row instead of a placeholder. <see cref="Veilrot"/> and
/// <see cref="Essence"/> both shipped with no reader at all, which is the cheapest possible statement
/// of that rule; Essence has had readers since M4-06 and Veilrot since M6-03b's meter, and
/// <see cref="IsDanger"/> is the door every new member has to pass.
/// </para>
/// <para>
/// <b>It is <c>Soulvail.Game</c> and it could not be anything else.</b> A colour is presentation:
/// <c>Soulvail.Core</c> is <c>noEngineReferences</c> (ADR-0001), so <see cref="Color"/> cannot cross
/// the boundary. Core keeps owning identity — a node's <c>Kind</c>, a <c>LocKey</c> — and Game keeps
/// owning what identity looks like.
/// </para>
/// </remarks>
public static class Palette
{
    // ---- GD §16.4's five reserved meanings. Design law, not tuning. -----------------------------

    /// <summary>
    /// <c>#22D3EE</c> — the player, and the things that keep them safe. GD §16.4.
    /// </summary>
    /// <remarks>
    /// Read by the health bar, the threat arrows' held focus, the Bulwark shell and the Consecrate
    /// ground. <b>It is not the only cyan in the build</b>, and that is recorded rather than tidied:
    /// <c>ReticleView</c>, <c>FocusGlowView</c> and <c>PlayerView._swingColour</c> hold
    /// <c>#4CE6FF</c>, a lighter cyan that predates GD §16.4's hex, and moving them here would be a
    /// visible recolour of three world-space views rather than the refactor this file is —
    /// <c>Palette_IsNotTheOnlyCyanInTheBuild</c> is what keeps that from being forgotten.
    /// </remarks>
    public static readonly Color Player = Rgb(0x22, 0xD3, 0xEE);

    /// <summary>
    /// <c>#FF4A1F</c> — GD §16.4's saturated red-orange. <em>Danger, and nothing else, ever.</em>
    /// </summary>
    /// <remarks>
    /// The one value in this file with a rule attached to it rather than a meaning, which is what
    /// <see cref="IsDanger"/> exists to make testable. <c>ThreatArrows</c> shipped it as
    /// <c>(1, 0.290, 0.122)</c> from M2-12a to M3-13a; that is the same colour to eight bits, and the
    /// exact hex lives here because this is the one place it is written down.
    /// </remarks>
    public static readonly Color Danger = Rgb(0xFF, 0x4A, 0x1F);

    /// <summary>
    /// <c>#A855F7</c> — corruption, Veilrot, Pacts. GD §16.4. Read by the HUD's Veilrot meter and the
    /// Claiming's name beside it.
    /// </summary>
    public static readonly Color Veilrot = Rgb(0xA8, 0x55, 0xF7);

    /// <summary>
    /// <c>#FBBF24</c> — rewards, Essence, Gates. GD §16.4. Read by the run-end payout and the
    /// Sanctum's prices.
    /// </summary>
    public static readonly Color Essence = Rgb(0xFB, 0xBF, 0x24);

    /// <summary>
    /// <c>#6E6A63</c> — GD §16.4's desaturated bone: everything else.
    /// </summary>
    /// <remarks>
    /// <b>It agrees with <c>EnemyLook.Default</c>'s tint bit for bit, and the two are still two
    /// things.</b> That field is <c>M_BoneGrey</c>'s own base colour, written down twice on purpose
    /// so an archetype nobody authored a tint for is drawn exactly as the untinted prefab was, and
    /// <c>EnemyLookTests</c>' Husk row pins it against the shipped material. Folding it in here would
    /// mean retuning the palette silently recoloured every unauthored archetype and broke a row about
    /// a material. <c>Neutral_AgreesWithTheEnemyDefault</c> is what says so the day either moves.
    /// </remarks>
    public static readonly Color Neutral = Rgb(0x6E, 0x6A, 0x63);

    // ---- Derived: CH §4's four node kinds, read by three files. ---------------------------------

    /// <summary>Always on: a stat or a rule change, about 45 % of a tree. CH §4.</summary>
    /// <remarks>
    /// The four kind tints carry the exact values <c>OfferCard</c>, <c>TreeNodeView</c> and
    /// <c>AutoCastRow</c> each shipped, to the float rather than to the byte, because unlike the five
    /// above they answer to no authored hex — they are placeholders three files agreed on, and moving
    /// them by even a rounding would be this task retuning rather than relocating. What changes is
    /// that there is now one of them.
    /// </remarks>
    public static readonly Color KindPassive = new Color(0.62f, 0.66f, 0.72f, 1f);

    /// <summary>Grants a skill with a cooldown. CH §4.2.</summary>
    /// <inheritdoc cref="KindPassive" />
    public static readonly Color KindActive = new Color(0.36f, 0.72f, 0.85f, 1f);

    /// <summary>Improves a skill already owned. CH §4.</summary>
    /// <inheritdoc cref="KindPassive" />
    public static readonly Color KindUpgrade = new Color(0.45f, 0.78f, 0.55f, 1f);

    /// <summary>Build-defining, end of a branch, three per class. CH §4.</summary>
    /// <inheritdoc cref="KindPassive" />
    public static readonly Color KindKeystone = new Color(0.85f, 0.72f, 0.32f, 1f);

    // ---- Derived: M3-09d's three node states, and M6-03b's fourth. -----------------------------

    /// <summary>
    /// Neither owned nor takeable: dim, because M3-09d rule 9's honest failure mode is small and
    /// quiet rather than clipped.
    /// </summary>
    /// <remarks><c>TreeNodeView</c>'s shipped value, to the float — <see cref="KindPassive"/>'s reason.</remarks>
    public static readonly Color NodeLocked = new Color(0.28f, 0.30f, 0.34f, 1f);

    /// <summary>Takeable on the next pick — the only thing on the tree screen the player can act on.</summary>
    /// <inheritdoc cref="NodeLocked" />
    public static readonly Color NodeAvailable = new Color(0.92f, 0.86f, 0.55f, 1f);

    /// <summary>Already owned. CH §5.1's <em>"the player's path highlighted"</em> is this and nothing else.</summary>
    /// <inheritdoc cref="NodeLocked" />
    public static readonly Color NodeTaken = new Color(0.36f, 0.82f, 0.62f, 1f);

    /// <summary>
    /// Taken out of this run's offer pool by a Sanctum Banish (M6-02b). <see cref="NodeLocked"/>
    /// darkened and nothing else: a banished node is <em>gone</em> where a locked one is merely out of
    /// reach, so it reads dimmer than the dimmest thing on the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No hue shift toward violet</b> (M6-03b rule 8). Banish is bought with Essence and has
    /// nothing to do with the Veil, and GD §16.4's violet means corruption.
    /// </para>
    /// <para>
    /// <b>The honest limit, stated:</b> colour alone is a weak signal on a 24 dp cell that holds
    /// neither an icon nor a word. That is ledger row 3's redesign, which this member does not move.
    /// </para>
    /// </remarks>
    public static readonly Color NodeBanished = new Color(0.15f, 0.16f, 0.19f, 1f);

    // ---- Derived: the player's own readouts and feedback. ----------------------------------------

    /// <summary>
    /// The health bar's brightened cyan, flashed when a hit is turned away.
    /// </summary>
    /// <remarks>
    /// A brightened <see cref="Player"/> rather than a new hue: the bar is already the player's
    /// colour, so the only thing left to say <em>"that one did not land"</em> with is brightness.
    /// <see cref="Danger"/> is reserved and may never be used here (GD §16.4).
    /// </remarks>
    public static readonly Color PlayerBlocked = new Color(0.78f, 0.98f, 1f, 1f);

    /// <summary>
    /// M3-11b's healing ground. A safe thing, so <see cref="Player"/>'s family — and today its exact
    /// value, which is what <c>VFX_ConsecrateZone.prefab</c> was dressed with.
    /// </summary>
    /// <remarks>
    /// A member of its own rather than a second name for <see cref="Player"/>, because rule 7 is
    /// about <em>meanings</em>: the day the owner wants healing ground a shade off the player's cyan,
    /// it is one line here rather than an argument about which readers meant which.
    /// </remarks>
    public static readonly Color Heal = Rgb(0x22, 0xD3, 0xEE);

    /// <summary>GD §16.3's white hit flash, 80 ms. It reads as <em>hit</em> on every body colour the game will have.</summary>
    public static readonly Color HitFlash = Rgb(0xFF, 0xFF, 0xFF);

    /// <summary>
    /// <c>#3B2422</c> — where an enemy's body is heading as its hit points run out (M3-13b). A dark,
    /// low-saturation maroon, and deliberately <em>not</em> <see cref="Danger"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>GD §16.2 and GD §16.4 disagree about this and §16.4 wins.</b> §16.2 says a dying body
    /// <em>"darkens and shifts toward red"</em>; §16.4 reserves saturated red-orange for danger
    /// <em>"for nothing else, ever."</em> A Husk at 20 % HP drawn in the telegraph colour would be the
    /// worst readability bug this project could ship, and <c>EnemyHitFeedback</c> has already made
    /// this call once — its telegraph is a <em>shape</em> change for exactly this reason. So the body
    /// darkens toward red without ever arriving at the reserved one. <b>The contradiction is flagged
    /// for the owner rather than edited</b> (M3-02a rule 7's precedent); M3-15 resolves it.
    /// </para>
    /// <para>
    /// <b>Red is the whole of the margin, and that is a fact about the shipped archetypes rather than
    /// about this value.</b> The Bloater ships at <c>(0.639, 0.341, 0.220)</c>, whose green is 0.051
    /// from <see cref="Danger"/>'s and whose blue is 0.098 from it — <em>before</em> any tint is
    /// applied. As hp falls its green crosses <see cref="Danger"/>'s exactly and its blue converges to
    /// 0.012 away, so neither channel can carry a distance claim. Red can: it lerps monotonically
    /// between each archetype's red and the 0.231 here, and the worst case over all three archetypes
    /// at every step is the Bloater at full health, 0.361 away from <see cref="Danger"/>'s 1.0.
    /// <c>Tint_NeverApproachesDanger</c> is therefore written as <em>not all three channels within
    /// 0.15</em>, which is a claim about a colour being the danger colour; <em>no channel within
    /// 0.15</em> would be a claim about coincidence, and it is red on a Bloater standing at full
    /// health in the build that exists.
    /// </para>
    /// </remarks>
    public static readonly Color EnemyDying = Rgb(0x3B, 0x24, 0x22);

    /// <summary>
    /// <c>#CCC4B7</c> — the boss's own bar across the top of the screen (M4-04). <see cref="Neutral"/>
    /// brightened: the same bone, at the value a full-width band needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is the tenth colour the class remarks promised, and the argument is by elimination.</b>
    /// GD §16.4 has five meanings and a boss is only one of them. It is not the player and not
    /// something that keeps them safe; it is not Veilrot and not a reward; and it may <em>not</em> be
    /// <see cref="Danger"/> — M4-04 rule 6 forbids it outright, because a permanent red-orange band
    /// across the top of the screen is the most visible breach of <em>"for nothing else, ever"</em>
    /// this project could ship, and a boss bar is up for the whole of the two minutes GD §9.1 rule 5
    /// gives a fight. So a boss is <em>everything else</em>, and the only freedom §16.4 leaves inside
    /// that band is value.
    /// </para>
    /// <para>
    /// <b>Which is why this is a brightened <see cref="Neutral"/> rather than a new hue</b> — exactly
    /// <see cref="PlayerBlocked"/>'s move, one band over. Each channel is <see cref="Neutral"/>'s
    /// times 1.85, so the two are the same bone and one of them is loud; <c>Palette_IsEntirelyOpaque</c>
    /// and <c>Danger_IsUsedByNothingElse</c> sweep it with the rest by reflection, so it needed no row
    /// of its own in <c>PaletteTests</c> to be held to the file's two rules.
    /// </para>
    /// <para>
    /// <b>A separate member was necessary rather than tidy, and that is a fact about what is already
    /// taken.</b> <see cref="Neutral"/> itself is spoken for on an enemy <em>body</em> twice over —
    /// M3-13b's health-bar fill and M4-03's invulnerable-beat shell — and <see cref="EnemyDying"/> is
    /// the third thing on one. A boss's bar drawn in any of those would say <em>"an ordinary
    /// enemy"</em> in the one place GD §16.2 wants <em>"the fight you are in"</em>. <c>BossBarView</c>
    /// reads this for the fill and <see cref="Neutral"/> for the seams between segments, which is the
    /// contrast that makes a mark legible on a filled bar.
    /// </para>
    /// </remarks>
    public static readonly Color Boss = Rgb(0xCC, 0xC4, 0xB7);

    /// <summary>
    /// The enemy the player tapped, when it is off screen: <see cref="Player"/>'s colour, and
    /// deliberately not <see cref="Danger"/>.
    /// </summary>
    /// <remarks>
    /// <b>It is <see cref="Player"/> exactly, and the spec's word for it — <em>"dimmer"</em> — was
    /// about the wrong thing.</b> An enemy that is both a threat and the held focus gets the cyan
    /// arrow, because <em>"the thing you picked is over there"</em> is the more specific statement;
    /// what makes it read dimmer on screen is <c>ThreatArrows.Place</c> ramping alpha by distance, and
    /// a held focus out of range is by definition far away. The dimness is the view's, not the
    /// colour's — which is this file's alpha rule (see the class remarks) seen from the other side.
    /// </remarks>
    public static readonly Color HeldFocus = Rgb(0x22, 0xD3, 0xEE);

    /// <summary>
    /// True when <paramref name="colour"/> is GD §16.4's reserved danger colour. How the
    /// <em>"for nothing else, ever"</em> half of the rule is tested rather than promised.
    /// </summary>
    /// <param name="colour">Any colour. Alpha is ignored — see the remarks.</param>
    /// <remarks>
    /// <para>
    /// <b>Alpha is ignored on purpose.</b> A view that faded a telegraph ring to a third has not
    /// stopped using the reserved colour, and a rule that could be stepped around by multiplying an
    /// alpha would be a rule about a float rather than about a meaning.
    /// </para>
    /// <para>
    /// <b>Compared at eight bits per channel rather than exactly.</b> That is the resolution a screen
    /// has and the resolution GD §16.4's hex is written at, and it is what makes the answer survive
    /// the difference between <c>0.290</c> and <c>74 / 255</c> — the two spellings <c>#FF4A1F</c> has
    /// had in this project. A non-finite channel answers false rather than throwing: this is a
    /// question a view asks about a colour it already holds, so there is nothing to refuse.
    /// </para>
    /// </remarks>
    public static bool IsDanger(in Color colour) =>
        Quantise(colour.r) == Quantise(Danger.r)
        && Quantise(colour.g) == Quantise(Danger.g)
        && Quantise(colour.b) == Quantise(Danger.b);

    /// <summary>
    /// One opaque colour from its eight-bit channels, so every value above reads as the hex GD §16.4
    /// writes it as.
    /// </summary>
    /// <remarks>
    /// <b>This is what makes the alpha rule structural rather than merely asserted.</b> There is no
    /// way to spell a translucent reserved colour in this file, which is the point:
    /// <c>Palette_IsEntirelyOpaque</c> then pins the derived members, which are authored as floats
    /// because they answer to no hex.
    /// </remarks>
    private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

    /// <summary>A channel at the resolution a screen has, or −1 for one that is not a number.</summary>
    /// <remarks>
    /// <b>The non-finite branch is checked rather than left to <c>Mathf.Clamp01</c>, which does not
    /// catch it.</b> That method is two comparisons and every comparison against NaN is false, so a
    /// NaN passes straight through both bounds and reaches <c>RoundToInt</c>, whose answer for one is
    /// not defined anywhere. AR §18.3's rule about guarding before the arithmetic that launders it,
    /// one layer down: −1 is a value no finite channel can produce, so a colour with a NaN in it
    /// matches nothing and <see cref="IsDanger"/> answers false.
    /// </remarks>
    private static int Quantise(float channel) =>
        float.IsFinite(channel) ? Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f) : -1;
}
