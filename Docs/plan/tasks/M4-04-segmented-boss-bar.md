# M4-04 — The segmented boss bar: a phase you can see coming

**Size:** S · **Depends on:** M4-01b, M3-13a, M3-13b · **Branch:** `m4-04-segmented-boss-bar`
**Design refs:** GD §16.2, §16.4, §9.1 (rule 3); AR §11.5 · **Ledger rows:** [M4 row 1](../ROADMAP.md#carry-forward-into-m4) — **this task is the first to land a new readout on the HUD M3-15 found unreadable, and it is required to say so**

## Goal

GD §16.2's last unbuilt row: *"a big segmented bar across the top of the screen, one segment per phase, so the
player can see a phase transition coming."* Three of the four rows in that table shipped at M3-13b; this is the
fourth, and it has waited since M2-15 opened M4's titles.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/BossBarView.cs` | Game | The bar, its segments, and the fill across them |
| `Tests/Game/Presentation/BossBarViewTests.cs` | Tests.Game | Segment count, fill mapping, the phase seam, the palette |
| *small edits* | Game | `Hud.prefab` gains the bar; `HudPresenter` binds it; `RunScope` registers nothing new |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **The segment count comes from `BossPhaseChanged.OfPhases`, not from a constant and not from a flag.**
   M4-01b rule 7 put it on the event precisely so this view could learn it — so a four-phase Archon at M7 needs
   no edit here, and **a bar drawn before any event arrives is not drawn at all** rather than guessing three.
2. **The bar appears when a boss spawns and leaves when it dies**, and nothing else puts it on screen. There is
   no *"is a boss alive"* query in core for it to poll; it is event-driven like every other view.
3. **Fill is the boss's total health fraction across the whole bar, and the segments are *marks on it*, not
   separate bars.** A player reading it should see one quantity with lines showing where the beats are — which
   is what *"see a transition coming"* means. Segments that empty one at a time would hide how close the next
   threshold is, which is the only thing the bar is for.
4. **The segment boundaries are the phases' own `EntersBelow` values**, so a bar for phases at 1.0 / 0.66 /
   0.33 has its marks at exactly two-thirds and one-third and **not at even spacing**. If the design ever
   authors uneven phases, the bar is already right. A test drives uneven thresholds to prove the mapping is
   read rather than assumed.
5. **During a beat the bar says so.** The beat is the moment GD §9.1 rule 3 exists for, and a bar that looks
   identical while the boss is invulnerable is telling the player their hits are landing when they are not.
6. **Every colour is `Palette`'s and no `Color` is serialized** (M3-13a). **The bar must not be
   `Palette.Danger`** — a permanent red-orange band across the top of the screen would break GD §16.4's
   reservation in the most visible place in the game.
7. **Every string is a `LocKey` through `ILocalizer`** (M3-14a, AR §11.5) — including the boss's name, if it
   shows one. A new prefab drawing a raw word would undo the thing M3-14c just finished.
8. **This task is required to record how it reads, not only that it works — and it is the first task after
   M3-15's verdict to add a readout at all.** [M4 row 1](../ROADMAP.md#carry-forward-into-m4) says the HUD is
   already too cramped to read; this adds a full-width band across the top of it, **directly above the 4 dp XP
   strip M3-10b put on the very top edge**. Whether those two collide, and whether the safe area holds both
   beside a landscape notch, is a **[device]** question and a real one. **The task may not tick a readability
   row; it states the arrangement it shipped and hands the judgement to M4-07.**

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Bar_IsHiddenUntilABossExists` | a run with no boss / ticked / the bar is not drawn and takes no touches (rule 2) |
| `Bar_TakesItsSegmentCountFromTheEvent` | `BossPhaseChanged(ofPhases: 4)` / bound / four segments, not three (rule 1) |
| `Bar_DrawsNothingBeforeAnyEvent` | a boss spawned, no phase event yet / ticked / no bar, rather than a guessed three (rule 1) |
| `Bar_FillIsTotalHealthNotPerSegment` | hp 0.5 of a three-phase boss / drawn / the fill is half the **whole** bar (rule 3) |
| `Bar_MarksSitAtTheAuthoredThresholds` | phases at 1.0 / 0.8 / 0.25 / drawn / marks at 0.8 and 0.25, not at thirds (rule 4) |
| `Bar_SaysSoDuringABeat` | `BossBeatStarted` / drawn / visibly different from the same fill outside a beat; `BossBeatEnded` restores it (rule 5) |
| `Bar_CarriesNoSerializedColour` | the view / reflected / no `[SerializeField] Color` (rule 6) |
| `Bar_IsNotTheDangerColour` | every colour the bar draws / compared / none satisfies `Palette.IsDanger` (rule 6) |
| `Bar_DrawsNoRawString` | the prefab and the view / swept / every user-facing string is a `LocKey` resolved through `ILocalizer` (rule 7) |
| `Bar_IgnoresANonFiniteFraction` | NaN / applied / the last good fill is kept and nothing throws |
| `Bar_LeavesOnDeath` | the boss dies / ticked / the bar is gone |
| `Hud_StripAndBarDoNotOverlap` | `Hud.prefab` / both rects read / they do not intersect — rule 8's collision, asserted in the Editor even though *legibility* cannot be |

**Guard rows are implied, not listed:** a null hub, a segment count below 1, an out-of-order threshold list.

## Manual verification (Editor / device)

1. **[Editor]** Reach stage 5. *Expected: a bar across the top, three segments, full.*
2. **[Editor]** Damage the boss. *Expected: one fill draining across the whole bar, passing the first mark at
   two-thirds.*
3. **[Editor]** Cross a threshold. *Expected: the bar shows the beat, and stops showing it when the beat ends.*
4. **[Editor]** Check the XP strip is still visible with the bar up. *Expected: both readable, neither covering
   the other.* **If they collide, that is this task's to fix by moving a number** — both are serialized dp.
5. **[device]** Whether a full-width band plus a 4 dp strip plus a notch coexist in a landscape safe area, and
   whether the segments read as *"a transition is coming"* at phone size. [M4 row 3](../ROADMAP.md#carry-forward-into-m4).

## Out of scope

- **Fixing the HUD.** Rule 8 — this task **inherits** M4 row 1 and must not pretend to answer it. It adds one
  readout to a HUD already known to be unreadable, and says so.
- **The boss's name, portrait or intro card.** GD §16.2 asks for a bar. A name needs a `LocKey` and a design
  decision about whether bosses are announced at all; M7.
- **Elite bars** — shipped at M3-13b, three milestones early, and unchanged here.
- **Anything about phases themselves** — M4-01b. This view reads events and owns no rules.

## As built

_Filled at merge._
