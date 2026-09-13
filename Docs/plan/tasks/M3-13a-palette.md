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
7. **The tenth colour is a field here.** That is how the row closes rather than pauses: M3-13b's damage tint, M4-04's boss segments, M6-05's Pact frames and M6-04's Veilrot meter each add a member and a test row instead of a placeholder. `Veilrot` and `Essence` are in the file already with no reader at all, which is the cheapest possible statement of that rule.
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

_Filled at merge._
