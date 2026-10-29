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
/// <b>Except JG-10's enemy track, every value is opaque, and a view that wants transparency multiplies its own.</b> Alpha is 1 on
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
/// M3-13b's damage tint, M4-04's boss segments and M6-04's Veilrot meter each add a member and a
/// test row instead of a placeholder. <b>M6-05b's Pact frame adds no member</b>, and that is the
/// rule working rather than an exception to it: GD §16.4 makes one violet mean Veilrot, Pacts and
/// corruption, so the frame reads <see cref="Veilrot"/>, and a second violet would be the drift
/// ledger row 6 exists to catch arriving from inside this file. <see cref="Veilrot"/> and
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
    /// Read by the health bar, the threat arrows' held focus, the Bulwark shell, the Consecrate
    /// ground, and, since UI-10b, the reticle, the focus glow and the swing arc. <b>It is the only
    /// player cyan in the build</b>: <c>Palette_IsTheOnlyPlayerCyanInTheBuild</c> fails a serialized
    /// copy of it, or of the older <c>#4CE6FF</c>, under <c>Prefabs/</c> or <c>Scenes/</c>.
    /// </remarks>
    public static readonly Color Player = Rgb(0x22, 0xD3, 0xEE);

    /// <summary>Near-white, cool wind on an ordinary Ranger arrow (RS-08c).</summary>
    public static readonly Color Wind = Rgb(0xE5, 0xF5, 0xFA);

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
    /// <c>#A855F7</c> — corruption, Veilrot, Pacts. GD §16.4. Read by the HUD's Veilrot meter, the
    /// Claiming's name beside it, and a Pact offer card's frame and price.
    /// </summary>
    public static readonly Color Veilrot = Rgb(0xA8, 0x55, 0xF7);

    /// <summary>
    /// <c>#FBBF24</c> — rewards, Essence, Gates. GD §16.4. Read by the run-end payout, the
    /// Sanctum's prices, and the class-select screen's Shard balance and prices (M6-09b).
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
    /// <c>#A5F3FC</c> — the health bar's ghost: the health that was there a moment ago (UI-01b).
    /// </summary>
    /// <remarks>
    /// <b>Pale cyan, and not the gold it was dressed in from M1-17 to UI-01b</b>, because GD §16.4's
    /// gold means rewards and a band of it across the player's own bar said <em>"you gained
    /// something"</em> at the moment they lost it. Its own member rather than <see cref="PlayerBlocked"/>
    /// again: the ghost and the blocked flash can be on the bar at once, and the flash has to be the
    /// brighter of the two to be seen.
    /// </remarks>
    public static readonly Color PlayerGhost = Rgb(0xA5, 0xF3, 0xFC);

    /// <summary>
    /// <c>#0E7490</c> — the deep end of the health bar's low-health beat, below a quarter (UI-01b).
    /// </summary>
    /// <remarks>
    /// <b>A darker cyan, never a warmer one.</b> The owner's word for the low state is that it
    /// <em>"must never turn red-orange"</em>, and GD §16.4 would say the same without being asked: a
    /// health bar in <see cref="Danger"/> would read as a telegraph on the player's own row. So the
    /// bar beats between <see cref="Player"/> and this, and a colour it passes through is a mix of two
    /// cyans.
    /// </remarks>
    public static readonly Color PlayerLow = Rgb(0x0E, 0x74, 0x90);

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
    /// <c>#D4F8FF</c> — the Puffball's wind-up glow, cyan-white, where every other body's is
    /// <see cref="Danger"/> (JG-03b).
    /// </summary>
    /// <remarks>
    /// <b>The one telegraph not in danger's colour, by the owner's ruling of 2026-10-22</b>: the cap
    /// glows cyan-white as it swells. GD §16.4 gives telegraphs red-orange, and the blast ring the burst
    /// draws still is; this is the body's own glow. A member here rather than a colour on the prefab, so
    /// the exception is named once and <c>EnemyHitFeedback._sporeTelegraph</c> is a switch, not a value.
    /// </remarks>
    public static readonly Color SporeGlow = Rgb(0xD4, 0xF8, 0xFF);

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
    /// M3-13b's health-bar fill and M4-03's invulnerable-beat shell. A boss's bar drawn in either
    /// would say <em>"an ordinary enemy"</em> in the one place GD §16.2 wants <em>"the fight you
    /// are in"</em>. <c>BossBarView</c>
    /// reads this for the fill. Its seams were <see cref="Neutral"/> until UI-01f, when they became the
    /// art's own dark pills, drawn untinted — the same contrast, drawn in.
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

    // ---- The HUD's art (UI-01). -----------------------------------------------------------------

    /// <summary>
    /// <c>#0F1419</c> — the outline on every HUD word and number, and the art's own ink (UI-01a).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one colour the new art and the code have to agree on.</b> Every piece Gemini drew for
    /// the HUD is outlined in a near-black, and <c>UI_v2/compose.py</c> strokes the mockup's words in
    /// exactly this value. <c>LilitaOne_SDF</c>'s material carries it as both its outline and its
    /// shadow, so a number drawn over a frame is outlined in the frame's own ink — which is why it
    /// is a member rather than a colour typed into a material, and why <c>HudArtTests</c> reads the
    /// material back against it.
    /// </para>
    /// <para>
    /// <b>It is <em>everything else</em> in GD §16.4's sense</b>: not the player, not a reward, not
    /// the Veil, and nowhere near <see cref="Danger"/>. A view that wants it translucent — the skill
    /// row's cooldown shade — multiplies its own alpha, the class remarks' rule.
    /// </para>
    /// </remarks>
    public static readonly Color Ink = Rgb(0x0F, 0x14, 0x19);

    /// <summary>Light enemy health fill (JG-10).</summary>
    public static readonly Color EnemyBar = Rgb(0xE8, 0xE2, 0xD6);

    /// <summary>The health just lost (JG-10).</summary>
    public static readonly Color EnemyBarGhost = Rgb(0xFF, 0xFF, 0xFF);

    /// <summary>Ink at 85% opacity behind enemy health (JG-10).</summary>
    public static readonly Color EnemyBarTrack = new Color(Ink.r, Ink.g, Ink.b, 0.85f);

    /// <summary>
    /// <c>#FFFFFF</c> — every number on the top band: the health, the level, the wave (UI-01b).
    /// </summary>
    /// <remarks>
    /// <b>The same white as <see cref="HitFlash"/>, and a member of its own</b> — <see cref="Heal"/>'s
    /// argument: a meaning, not a value. The day the owner wants the band's numbers a shade warmer,
    /// the hit flash must not warm with them.
    /// </remarks>
    public static readonly Color HudText = Rgb(0xFF, 0xFF, 0xFF);

    /// <summary>
    /// <c>#CCC4B7</c> — a caption on the band: the <em>STAGE</em> over the wave (UI-01d).
    /// </summary>
    /// <remarks>
    /// GD §16.4's <em>everything else</em>, at the brightness a word on a dark pill needs, which is
    /// <see cref="Boss"/>'s value and <see cref="Heal"/>'s argument for a member of its own: a
    /// caption and a boss's bar are two meanings, and the day one moves the other must not.
    /// </remarks>
    public static readonly Color HudCaption = Rgb(0xCC, 0xC4, 0xB7);

    /// <summary>
    /// <c>#4ADE80</c> — a point to spend: the green count on the level star (UI-03b).
    /// </summary>
    /// <remarks>
    /// <b>A green, and never the red-orange.</b> GD §16.4 gives a banked point no colour of its own, so it
    /// is <em>everything else</em> in the one hue the band does not use: not the player's cyan, not the
    /// reward gold, not the Veil's violet, and nowhere near <see cref="Danger"/>, which means a
    /// telegraph and nothing else. A member of its own, <see cref="Heal"/>'s argument: a meaning,
    /// not a value.
    /// </remarks>
    public static readonly Color Available = Rgb(0x4A, 0xDE, 0x80);

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
