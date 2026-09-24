# M3-13a — `Palette`: GD §16.4 in one file, and the nine readers that have been waiting for it

**Size:** S (two code files and one doc row; the rest is a wide, mechanical ripple — rule 5) · **Depends on:** M3-08b, M3-09d, M3-10b, M3-11c (the five files whose placeholders it absorbs) · **Branch:** `m3-13a-palette`
**Design refs:** GD §16.2, §16.3, §16.4; CH §4; AR §7, §18.3; ADR-0001 · **Ledger rows:** **6 — closed by this task**, and closed in a way that makes the tenth colour cheap rather than the tenth argument

## Goal

GD §16.4's five reserved colours and every tint derived from them live in one file, so *"one rule, enforced globally"* stops being a sentence in a design doc and becomes something a test can fail.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/Palette.cs` | Game | the colours: GD §16.4's five reserved meanings, and the derived sets the readers need |
| `Tests/Game/Presentation/PaletteTests.cs` | Tests.Game | the five hex values, the danger rule, and that no reader carries a colour of its own |
| *small edits* | | `Docs/Architecture.md` — **AR §18.3** gains the sanctioned-static row (rule 2); `Game/Presentation/ThreatArrows.cs` — `Danger` and `HeldFocus` move out and nothing forwards (rule 4); `Game/Views/TelegraphRings.cs` — reads `Palette.Danger`; `Game/Presentation/HpBarView.cs`, `Game/Views/EnemyHitFeedback.cs`, `Game/Controls/OfferCard.cs`, `Game/Controls/TreeNodeView.cs`, `Game/Presentation/AutoCastRow.cs`, `Game/Views/ZoneView.cs`, `Game/Views/BulwarkView.cs` — their serialized `Color` fields go (rule 5) |
| *ripple* | | the prefabs that carried those fields — `Hud.prefab`, `Enemy.prefab`, `LevelUp.prefab`, `TreeView.prefab`, and the two VFX prefabs — lose them on the next serialize; the four spec test rows that asserted a *serialized* colour are rewritten to assert the palette's (rule 6) |

Only these files change. Anything else is a deviation: say so in *As built*.

## The count, recounted

**Ledger row 6 says "eight readers across five files"; the true shape is nine readers across nine files, and eight of the nine are one line each.** The row's number grew by counting M3's own screens (4 → 5 → 6 → 8 across M3-08b, M3-09d, M3-10b and M3-11c) on top of the three that already existed, and it never named the ninth:

| # | File | What it holds | Named by |
|---|---|---|---|
| 1 | `ThreatArrows` | `Danger` `#FF4A1F`, `HeldFocus` — **already `public static readonly`** | M2-12a |
| 2 | `TelegraphRings` | reads `ThreatArrows.Danger` across the `Views`/`Presentation` seam | M2-12b, the row's original complaint |
| 3 | `HpBarView` | the player's cyan and its brightened blocked variant | M1-17 |
| 4 | `OfferCard` | CH §4's four kind tints | M3-08b rule 8 |
| 5 | `TreeNodeView` | the same four kinds, **plus** three node states | M3-09d rule 8 |
| 6 | `AutoCastRow` | the same four kinds again | M3-10b rule 9 |
| 7 | `ZoneView` | the heal colour | M3-11c rule 10 |
| 8 | `BulwarkView` | the player's cyan again | M3-11c rule 10 |
| 9 | `EnemyHitFeedback` | the white hit flash (GD §16.3) | **nothing — M3-13a found it** |

Three files hold the player's cyan and three hold CH §4's four kinds. That is the state row 6 predicted — *"the number being copied on the third use, which is where palettes stop being one"* — arrived at exactly on schedule.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    /// <summary>GD §16.4's colour language, in the one place the rule can be enforced from.</summary>
    public static class Palette
    {
        // GD §16.4's five reserved meanings. These are design law, not tuning.
        public static readonly Color Player;        // #22D3EE — the player, and safe things
        public static readonly Color Danger;        // #FF4A1F — "for nothing else, ever"
        public static readonly Color Veilrot;       // #A855F7 — corruption, Pacts (M6)
        public static readonly Color Essence;       // #FBBF24 — rewards, Gates (M6)
        public static readonly Color Neutral;       // desaturated bone — everything else

        // Derived: CH §4's four node kinds, read by three files today.
        public static readonly Color KindPassive, KindActive, KindUpgrade, KindKeystone;

        // Derived: M3-09d's three node states.
        public static readonly Color NodeLocked, NodeAvailable, NodeTaken;

        // Derived: the player's own readouts and feedback.
        public static readonly Color PlayerBlocked; // HpBarView's brightened cyan
        public static readonly Color Heal;          // M3-11b's zone — a safe thing, so Player's family
        public static readonly Color HitFlash;      // GD §16.3's white, 80 ms
        public static readonly Color HeldFocus;     // a focus out of range: dimmer, and not Danger

        /// <summary>True when this is GD §16.4's reserved danger colour. How rule 3 is tested.</summary>
        public static bool IsDanger(in Color colour);
    }
}
```

## Behaviour

1. **Five reserved colours at the hex values GD §16.4 names, one test row each.** A colour language *"enforced globally"* is one whose numbers exist once; the row exists because `#FF4A1F` was two tasks from being typed a third time and the player's cyan already had been.
2. **Static, and the precedent is already shipped rather than invented here.** `ThreatArrows.Danger` has been `public static readonly Color` since M2-12a and `TelegraphRings` reads it — which is the *whole of* ledger row 6's original complaint, and the complaint was about the **seam**, not the storage. AR §7's ban is on static **mutable** state and on service location; a `readonly Color` is neither, holds nothing a run owns, and is inert under a disabled domain reload because there is nothing to reset (CLAUDE.md's `SubsystemRegistration` rule has no work to do). Two alternatives were weighed and rejected: an **injected instance**, which `TreeNodeView` and `ZoneView` cannot have — they are pooled templates instantiated from a prefab field and nothing injects them individually; and a **`ScriptableObject` referenced per prefab**, which is five more dressing steps and treats a design law as a tuning knob, when the thing that makes `#FF4A1F` load-bearing is precisely that nobody may retune it. **Recorded as an AR §18.3 row** — the one sanctioned static — so the next one has to argue against this rather than against nothing.
3. **Nothing but danger is `#FF4A1F`, and `IsDanger` is what stops that being a promise.** M3-11c rule 10 states the rule and could only test two prefabs' fields; this file can test the whole set, and does: every other member is asserted not to be `Danger`. The member most likely to break it is not in this task — M3-13b's damage tint darkens an enemy *"toward red"* (GD §16.2) — which is why the door exists before the tempting caller arrives.
4. **`Danger` and `HeldFocus` move, and `ThreatArrows` keeps no forwarding property.** One place for the number was the point, and a forwarder is two names for one colour — the state the row describes, preserved with an extra hop. The move also **fixes the dependency direction**: `Views` and `Presentation` stop being mutually dependent (legal inside one assembly, and the untidiness row 6 names) and both read one file that reads nobody. `HeldFocus` travels with `Danger` because it is *defined* as "not danger" and belongs beside the rule it is an exception to.
5. **The serialized `Color` fields go rather than defaulting from the palette.** A field initialised from `Palette` and then dressed differently on a prefab is the placeholder problem with an extra step, and worse: Traps §5's read-back test would pass on the wrong value, because a dressed field *is* what the prefab says. Removing the field makes the prefab's stored value unreachable rather than contradictory. **`_flashSeconds`, `_blockedSeconds`, `_fadeSeconds` and every other timing field stay** — this task takes colours and nothing else.
6. **Four existing test rows change meaning and are rewritten, not deleted.** M3-11c's `Views_UseNoDangerColour` reads *"the two prefabs' serialized colours"* and there are none left; it becomes a palette assertion. M3-08b's `Card_TintsByKind`, M3-09d's `Tree_FrameColoursByState` and M3-10b's `Row_TintsByKind` assert that the tints *differ*, which the palette satisfies unchanged — they keep their names and lose their fixture setup. Named because a task that silently deleted four rows from four other tasks' suites would be indistinguishable from a task that broke them.
7. **The tenth colour is a field here.** That is how the row closes rather than pauses: M3-13b's damage tint, M4-04's boss segments, M6-05b's Pact frames and M6-04's Veilrot meter each add a member and a test row instead of a placeholder. `Veilrot` and `Essence` are in the file already with no reader at all, which is the cheapest possible statement of that rule.
8. **No alpha ramps, no gradients, no per-tier variants.** Every value is **opaque** — alpha 1, asserted — and a view that wants transparency multiplies its own, which `EnemyHitFeedback`'s dissolve already does. A palette that grew a curve would be a style system, and GD §16.4 is five rows.
9. **It is `Game`, and it could not be anything else.** A colour is presentation: `Soulvail.Core` has no `UnityEngine` reference and never will (ADR-0001, `noEngineReferences`), so `Color` cannot cross the boundary — `AssemblyPurityTests` is the standing assertion and this task adds nothing to it. Core keeps owning identity — a node's `Kind`, a `LocKey` — and Game keeps owning what identity looks like.

## Tests

| Test | Given / When / Then |
|---|---|
| `Reserved_PlayerIsTheAuthoredCyan` | — / `Player` / `#22D3EE` to eight bits per channel (rule 1) |
| `Reserved_DangerIsTheAuthoredRedOrange` | — / `Danger` / `#FF4A1F`, and equal to the value `ThreatArrows` shipped since M2-12a (rules 1, 4) |
| `Reserved_VeilrotEssenceAndNeutral` | — / the three / `#A855F7`, `#FBBF24`, and a bone value with saturation below 0.2 (rule 1) |
| `Danger_IsUsedByNothingElse` | reflection over every `Color` member / — / exactly one is `Danger`, and `IsDanger` is true for it alone — **ledger row 6's teeth** (rule 3) |
| `IsDanger_IgnoresAlpha` | `Danger` with alpha 0.4 / `IsDanger` / true — a view that faded a telegraph has not stopped using the reserved colour (rule 3) |
| `Kinds_AreFourDistinctColours` | — / the four / four distinct values, none `Danger` (rules 1, 3) |
| `States_AreThreeDistinctColours` | — / the three / three distinct values, none `Danger` (rules 1, 3) |
| `Heal_IsInThePlayersFamily` | — / `Heal`, `Player` / hues within 40°, and `Heal` is not `Danger` — GD §16.4 puts safe things in cyan (rules 1, 3) |
| `HeldFocus_IsDimmerThanDanger` | — / the two / `HeldFocus` has lower value and is not `Danger` (rule 4) |
| `ThreatArrows_HoldsNoColourOfItsOwn` | reflection over `ThreatArrows` / — / no `Color` field, static or serialized, and no member named `Danger` (rule 4) |
| `TelegraphRings_ReadsThePalette` | a bound ring / — / its colour is `Palette.Danger`; the type has no reference to `ThreatArrows` (rule 4) |
| `Views_CarryNoSerializedColour` | reflection over the nine files' types / — / no `Color` field carries `[SerializeField]` — **the row that keeps this row closed** (rule 5) |
| `Views_KeepTheirTimingFields` | the same nine / — / `_flashSeconds`, `_blockedSeconds`, `_dissolveSeconds`, `_fadeSeconds` and `_secondsShown` all still serialized (rule 5) |
| `HpBar_DrawsThePaletteColours` | a bar / `Set(0.5)`, then `FlashBlocked` / `Palette.Player`, then `Palette.PlayerBlocked` (rule 5) |
| `HitFlash_IsWhite` | a body / `EnemyDamaged` / the property block's colour is `Palette.HitFlash`, and it is white — GD §16.3, the ninth reader (rules 1, 5) |
| `Palette_HoldsNoMutableState` | reflection over the type / — / every field is `static readonly`, no property has a setter, and nothing is a reference type a caller could write through (rule 2) |
| `Palette_IsEntirelyOpaque` | every `Color` member / — / alpha is exactly 1; a view fades by multiplying its own (rule 8) |
| `Palette_HasTheColoursNobodyReadsYet` | — / `Veilrot`, `Essence` / present and distinct — the statement of rule 7, asserted so it is not tidied away |
| `RewrittenRows_StillAssertWhatTheyAsserted` | M3-08b's `Card_TintsByKind`, M3-09d's `Tree_FrameColoursByState`, M3-10b's `Row_TintsByKind` and M3-11c's `Views_UseNoDangerColour` / the suite / all four present, all four green, none deleted — the row that distinguishes this task from one that broke four other suites (rule 6) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Open `Hud.prefab`, `LevelUp.prefab`, `TreeView.prefab` and `Enemy.prefab`. **No colour swatches remain** on any of the components this task touched — the fields are gone, not merely unused (rule 5).
2. **[Editor]** Descend. The health bar, the Aegis ring, the hit flash, the threat arrows and the telegraph rings all look exactly as they did before this branch. **A no-visible-change PR is the intended outcome**; anything that looks different is a value that was dressed away from its own default and is a finding for *As built*.
3. **[Editor]** Level up. The four kind tints on the cards, the tree cells and the auto-cast row are now the *same* four colours in all three places, which they were not before (rule 1).
4. **[device]** Ledger row 4, and the one question a palette exists to answer: whether a cyan healing disc and a red-orange telegraph ring overlapping on one floor are tellable apart under screen glare (M3-11c's deferred step, now with one number behind both).

## Out of scope

- **Any new colour.** The damage tint is M3-13b's and adds its own member by rule 7; the boss bar's segments are M4-04's. This task moves what exists.
- **Health-bar treatment** (GD §16.2's tint, elite bars, the granted shield) — M3-13b. Row 6 and the §16.2 table are two arguments and this is the colour one.
- **Skill icons.** M3-10a rule 9 and M3-10b rule 8 both point at M3-13 for them; they are art, not colour, and they are M7's pass. Neither row is closed by this task and both are re-stated at M3-15.
- **A theme, a dark mode, or HUD opacity** (GD §16.1's *"user-adjustable down to 40 %"*) — M8-03, a settings field and a profile bump by M3-09c rule 3's rule.
- **Making the palette authored.** Rule 2 rejects it, and the day the owner wants to retune a kind tint on a phone is the day this becomes a `ScriptableObject` with one dressing step per prefab. The values live in one file until then.
- **`EnemyHitFeedback`'s archetype tints** (`EnemyLook.Tint`, authored per archetype in `Data/Enemies/`). Those are content, not palette: a Bloater's rust is a `ScriptableObject` field the owner tunes, and GD §16.4 puts *"everything else"* in the desaturated band rather than naming each body.

## As built

**Two new code files, nine production edits, five rewritten test rows across four files, two more test files repointed, one `Architecture.md` row. Two assemblies, no third. No prefab and no scene moved — see the ripple ruling below.** `1 856 / 0 / 0` EditMode twice consecutively against M3-12c's `1 833`, **+23 rows and the reconciliation is exact**: the spec's Tests table is 19 and four more are owed by the rulings below (`IsDanger_IsFalseForANonFiniteColour`, `HeldFocus_IsThePlayersOwnColour`, `Palette_IsNotTheOnlyCyanInTheBuild`, `Neutral_AgreesWithTheEnemyDefault`). **The five rewritten rows changed the count by nothing**, which is what makes them rewrites.

### The four rulings owed before a line was written

**1 — It does not split, and the ruling is on the ROADMAP's own criterion rather than on the file count.** The review is sixteen files and the ROADMAP counts *"new code files and substantial rewrites"*: there are **two** new code files and **zero** substantial rewrites. Each of the nine production edits is one field deleted and one expression repointed, with no line of behaviour changed; the four test files lose a fixture; the doc is one row. The counter-argument — that a *subtractive* edit is not the *additive* one the ROADMAP's examples name — is true and cuts the other way: a class that ends with fewer members and no new behaviour is a smaller edit than one that gains a field, not a larger one. **What decided it is that every available seam is disqualifying.** The proposed one (`Palette` + `ThreatArrows` + `TelegraphRings` + the AR row, then the seven views) leaves `Palette` shipping **eleven of its fifteen members with no reader**, and leaves `Views_CarryNoSerializedColour` — the row ledger row 6 actually closes on — unwritable until the second half. Row 6 would then close on the *second* PR whichever way the files were cut, and a task whose whole point is *"the number exists once"* would ship an intermediate state in which it exists twice. Ruled out loud because the spec carries no tripwire and M3-11a is the precedent for splitting on one.

**2 — The three unnamed views are out of scope, and the reason is manual step 2 rather than tidiness.** `ReticleView._colour`, `FocusGlowView._colour` and `PlayerView._swingColour` each hold a serialized cyan the spec's recount never counted, so the honest reader count is **twelve files, not nine**. But theirs is **`#4CE6FF`**, not GD §16.4's `#22D3EE` — measured off `Player.prefab` and `Reticle.prefab`, not off the initialisers — so **there have been two player cyans in the shipped build since M1-17**, which is precisely the drift row 6 exists to catch, arrived at *before* the palette. Moving them onto `Palette.Player` would recolour the reticle, the focus glow and the swing arc on every frame of every run: a visible change, which manual step 2 forbids and the spec's *Out of scope* (*"any new colour; this task moves what exists"*) rules out. **`Views_CarryNoSerializedColour` is therefore scoped to the nine by name and asserts that the three are *not* in that list**, so it cannot go red on views it was never told about; `Palette_IsNotTheOnlyCyanInTheBuild` reads all three off their shipped prefabs and pins `#4CE6FF`, the 0.25 alpha on the swing arc, and the fact that none of them is the palette's. **Which cyan the player's own world-space views should be is the owner's call and no task owns it, so it is a parking-lot line rather than a ledger row.**

**3 — The ripple is *nothing*, and that is a measured correction to both the spec and the expectation.** The spec names six prefabs; the expectation added `Run.unity`. Neither moved. `Run.unity` holds `BulwarkView` and `AutoCastRow` only as **stripped** prefab-instance references (`--- !u!114 &1333328736 stripped`), which carry `m_Script` and nothing else — a grep for every removed field name over the whole scene returns nothing, so there was never a colour in it to lose. And the six prefabs did not move either: Unity 6.3 **keeps** a removed field's YAML line rather than dropping it, and `AssetDatabase.ForceReserializeAssets` on `VFX_ConsecrateZone.prefab` produced a **zero-byte diff** with `_colour:` still on line 107. The lines are now dead text. **Manual step 1 still passes today**, which was checked rather than assumed: a `SerializedObject` walk of all seven components reports `(none)` for colour properties, so the Inspector shows no swatch. The working tree is twenty entries, all of them `.cs` or `.md`.

**4 — The white initialisers were read from the prefab YAML, and they are `#22D3EE`.** `ZoneView._colour` and `BulwarkView._colour` both initialised to `Color.white` in C# and both were dressed `(0.13333334, 0.827451, 0.93333334, 1)` on their prefabs — M3-11c's *As built* said so and Traps §7 is the general case. **`Palette.Heal` and the Bulwark shell are therefore `#22D3EE` and not white**, and the probe printed both beside what the two assets store. Rule 5's argument depends on exactly this: removing the field makes the prefab's stored value *unreachable* rather than *contradictory*, which is only an improvement if you know what you made unreachable.

### Six corrections the spec needed

- **`HeldFocus` is `Player`, byte for byte, and the spec's *"dimmer"* was about the wrong thing.** `ThreatArrows.HeldFocus` and `HpBarView._fillColour` were already the identical `(0.133, 0.827, 0.933, 1)`; `HeldFocus_IsDimmerThanDanger` passed on 0.933 against Danger's 1.0, which is a row going green for a reason unrelated to its name. **What makes a held focus read dimmer is `ThreatArrows.Place` ramping alpha by distance**, and a focus held out of range is far away by definition. Changing the value would have been a visible change, so **the comment was corrected and the value was not**; `HeldFocus_IsThePlayersOwnColour` is the new row, and it also pins the alpha ramp, because if the ramp ever goes the two arrows become indistinguishable. **No *"every member is distinct"* row was written**, deliberately: `Player`, `Heal` and `HeldFocus` are all `#22D3EE` on purpose and such a row would go red on a triple that is a design decision.
- **`EnemyLook.BoneGrey` stays content and `Palette.Neutral` takes its value.** It is `#6E6A63`, its saturation is 0.1, and GD §16.4's *"desaturated bone"* is exactly that — but its documented job is to be **`M_BoneGrey`'s own number, written down twice on purpose**, pinned by `EnemyLookTests`' Husk row against the shipped material. Folding it in would mean retuning the palette silently recoloured every unauthored archetype and reddened a row about a material. **`EnemyLook.cs` was not edited**; `Neutral_AgreesWithTheEnemyDefault` asserts the two agree and that the look book still holds its own, so the day either moves the other is told.
- **The AR row is `§7`, not `§18.3`, and it sanctions a *surface* rather than a field.** **A first draft of that bullet said *"exactly one sanctioned static"* and was false on the facts**: six types already hold a `private static readonly` constant — `EnemyLook.BoneGrey`, both `FlatRotation`s, `FirstActiveHint.HintKey`, `OverflowToast.ToastKey`, `SaveDtos.EmptySlots` — several carrying their own note saying why that is legal. The row now sanctions the one static **other files read**, because a shared static is what can become a service locator by accretion and a private constant cannot. As for the section: §18.3 is *"Numbers and identity"* — NaN guards, `ContentId`'s grammar, seed consumption, budget arithmetic — and a static-policy row is a poor fit for it. §18's own preamble is *"rules the code currently depends on"*, and this is a rule about what may be **written**. §7 is where the ban on statics actually is, which is the text a person reads while deciding to write the next one. One bullet, immediately under the ban, naming the two rejected alternatives and the argument the next static has to beat.
- **Two more test files break than the spec knew.** `ThreatArrowsTests` (five sites) and `TelegraphRingsTests` (five sites) both read `ThreatArrows.Danger` / `.HeldFocus`, which rule 4 deletes with no forwarder. Both are pure `ThreatArrows.` → `Palette.` substitutions and **no row in either changed meaning**, so they are listed as a ripple rather than as rewrites.
- **The rewritten rows are five, in four files across two folders.** The spec's four are `Views_UseNoDangerColour` (in `Tests/Game/**Views**/SkillViewTests.cs`, not `Presentation/`), `Card_TintsByKind`, `Tree_FrameColoursByState` and `Row_TintsByKind` — and **`Tree_TintsByKind` is a fifth**, because it compared `TreeNodeView`'s four serialized tints against `OfferCard`'s field by field and could not survive the fields going. `RewrittenRows_StillAssertWhatTheyAsserted` names all five as (fixture, row, palette member) and asserts the method exists, carries `[Test]`, **and loads that member in its IL** — so a deleted row, a renamed row and a row gutted into a tautology all go red, which is what distinguishes this task from one that broke four other suites.
- **`Views_KeepTheirTimingFields` could not use the spec's list.** `_secondsShown` is on `FirstActiveHint` and `OverflowToast`, neither of which ever held a colour. The row asserts the timing and feel fields that actually live on the nine — `_flashSeconds`, `_dissolveSeconds`, `_blockedSeconds`, `_fadeSeconds`, `_pulseSeconds` — **plus `_maxAlpha`, `_baseAlpha` and `_pulseAlpha`**, which is rule 8's bargain made checkable: the palette is opaque and a view keeps its own alpha.

### What moved, to the byte

**Rule 8 is structural rather than asserted.** `Palette.Rgb(int, int, int)` is the only way a reserved colour is spelled in the file, so there is no way to write a translucent one; the derived members are authored as floats and `Palette_IsEntirelyOpaque` pins them. **The five reserved colours carry the exact hex GD §16.4 names; the derived sets carry the exact floats the build shipped** — they answer to no authored hex, so moving them by even a rounding would be retuning. Four values therefore moved and nothing else did, each by less than half of one 255th and each landing on the byte it already had: `HpBarView._fillColour` and `ThreatArrows.HeldFocus` `(0.133, 0.827, 0.933)` → `34/255, 211/255, 238/255`, and `ThreatArrows.Danger` `(1, 0.290, 0.122)` → `255/255, 74/255, 31/255`. The two VFX prefabs were already exact, so their delta is zero. **`IsDanger` compares at eight bits** for that reason, and answers `true` for both spellings of `#FF4A1F` — probed.

`IsDanger`'s non-finite branch is checked rather than left to `Mathf.Clamp01`, which does not catch it: that method is two comparisons, every comparison against NaN is false, and `RoundToInt(NaN)` is undefined. AR §18.3's *"guard before the arithmetic that launders it"*, one layer down.

### Probed rather than trusted

All fifteen members printed as hex with `IsDanger` against each: `#22D3EE` `#FF4A1F` `#A855F7` `#FBBF24` `#6E6A63`; kinds `#9EA8B8` `#5CB8D9` `#73C78C` `#D9B852`; states `#474C57` `#EBDB8C` `#5CD19E`; `PlayerBlocked #C7FAFF`, `Heal #22D3EE`, `HitFlash #FFFFFF`, `HeldFocus #22D3EE`. **Exactly one is `IsDanger`.** The doors: danger at alpha 0.4 → `true`, the M2-12a literal → `true`, NaN → `false`, `+Infinity` → `false`, `Color.clear` → `false`. `Heal == Player` and `HeldFocus == Player` both `true`; `Palette.Neutral` and `EnemyLook.Default.Tint` both `#6E6A63`. `ZoneView.Colour` off the shipped `VFX_ConsecrateZone` reads `#22D3EE`, which is what the prefab stored. All seven touched components report **no serialized colour property at all**; `PlayerView._swingColour` reads `#4CE6FF a=0.25`, `FocusGlowView._colour` and `ReticleView._colour` both `#4CE6FF a=1`.
