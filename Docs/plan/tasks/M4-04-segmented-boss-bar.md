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

**The band ships and every behaviour rule is met, and rule 4 could not have been met without one Core
addition the Files table does not list.** `2 145 / 0 / 0`, twice consecutively on the final code, against
M4-03's `2 124` — **+21**, and all 21 are `BossBarViewTests`' plus two assertions added to an existing
Core row.

### The deviation, and why it was the only honest way to satisfy the Tests table

**`BossPhaseChanged` gained a fourth field: `IReadOnlyList<float> EntersBelow`.** Rule 4 requires the
marks to sit at *"the phases' own `EntersBelow` values"*, and **`BossPhaseChanged` carried only the
count, so nothing in `Soulvail.Game` could have known where a mark goes.** The alternatives were all
worse:

- **Even spacing.** Refused by rule 4 by name.
- **Learn each threshold as it is crossed.** Defeats the element's purpose — a mark the player only sees
  *after* passing it is the opposite of *"see a transition coming"*.
- **A `ContentCatalog` on the HUD, reverse-looking-up the boss whose `EnemySpec` an `EnemySpawned`
  named.** This is the seam `EnemySpawned.IsElite`'s own remarks refuse (*"a view needs the fact and
  must not hold a handle to the thing that knows it"*), it is ambiguous the day two bosses share a body,
  and it would rely on the two events being published adjacently — right by coincidence.

So the fact rides the event, which is `ShieldGranted.Total`'s, `EnemyDamaged.HpFraction`'s,
`EnemyTelegraph.Duration`'s and `BossBeatStarted.Seconds`' own argument. **It is the same sentence
`OfPhases` is, one step further**, and the ROADMAP's sizing rule waves it through explicitly: *"small
additive edits to existing files (a new field on a spec, a registration, **an event struct**) … don't
count"*. Three files carry it — `BossEvents.cs` (the field, defaulted to `null` on
`EnemySpawned.IsElite`'s terms so no existing construction moved), `BossPhases.cs` (an
`EntersBelow` view over the array it has held since M4-01b, `Array.AsReadOnly`'d **once at
construction**, so publishing it allocates nothing and `BossPhases`' allocation row is untouched), and
`BossBehaviour.cs` (both publish sites). **This is the first event in the project to carry a
collection**, and the reason it is safe is that nothing builds one per publish.

**Two deviations in all.** The second is **two assertions appended to
`BossPhasesTests.Phase_IsAnnouncedWhenTheBossStandsUp`** — that the announced list is not null and *is*
`{1, 0.66, 0.33}`. Without it the view's mapping would be proved against an array the Game fixture
typed, and nothing would say the wire carries the *asset's* numbers. It is the only row in the project
that asserts that, and it sits in the fixture that already stands a real boss up.

**Nothing else outside the table changed.** `RunScope` registers nothing new — see *the band is dumb*
below. `PaletteTests` needed no row: `ColourMembers()` reflects over every public static `Color`, so
`Danger_IsUsedByNothingElse` and `Palette_IsEntirelyOpaque` swept the new member the moment it existed,
and rule 6's own rows live in this task's fixture.

### Rule 3 against rule 4, settled — and the shipped boss could not have told them apart

**They do not conflict; rule 4 is where rule 3's marks go.** One `Filled` image across the whole band
(`Bar_FillIsTotalHealthNotPerSegment` asserts there is *exactly one*, so nobody can quietly make it N
bars), and `N − 1` opaque seams anchored *fractionally* over it at the thresholds — the outermost phase
enters at `BossSpec.FirstPhaseEntersBelow`, which is 1 and is therefore the band's own end rather than a
line drawn on it. **Anchored rather than positioned**: `anchorMin.x = 0.8` is 80 % of whatever the safe
area turns out to be, on both rotations and every device, where arithmetic against a measured width
would be a second copy of what the anchors already know.

**And the answer the owner asked for: no.** `WardenBoss.asset` authors 1.0 / 0.66 / 0.33; even spacing on
three segments puts the marks at 0.6667 and 0.3333. The differences are **0.0067 and 0.0033 of the
band's width** — on a 1080-wide landscape safe area, **7 px and 4 px**, which is three seam-widths and
one. **The only boss that exists could not have distinguished the rule from the thing the rule forbids**,
which is exactly why `Bar_MarksSitAtTheAuthoredThresholds` drives the spec's own 1.0 / 0.8 / 0.25 and
asserts *both* that the marks are at 0.8 and 0.25 **and** that they are `Is.Not.EqualTo(2/3).Within(1e-2)`
— so a band that spaced evenly fails outright rather than by a rounding. **`Bar_LearnsItsMarksFromARealFight`
is what makes that mapping a fact rather than a fixture agreeing with itself**: it drives the band from a
real `BossPhases` over a real `BossSpec`.

Measured on the saved asset through a live canvas: a band 2 560 units wide put its seams at **2 047.3**
and **639.3** — 0.8 and 0.25 exactly, centred, drawn *over* the fill (sibling indices 2 and 3 against the
fill's 0, because uGUI draws in hierarchy order and a seam behind the fill would vanish on a full bar).

### The band is dumb, and that is why `RunScope` registers nothing new

`BossBarView` is handed fractions and flags exactly as `HpBarView` and `ShieldRingView` are; nothing is
injected into it and no event reaches it. **The five subscriptions are `HudPresenter`'s** —
`BossPhaseChanged`, `BossBeatStarted`, `BossBeatEnded`, `EnemyDamaged`, `EnemyDied` — and the reason they
belong there rather than on the view is rule 8: that class already owns every rect on this prefab that
has to agree with another about where it is, and *"do the band and the strip collide"* is exactly that
question. `Construct`'s signature is **unchanged at five parameters**, which `Construct_RefusesANullHub`
pins.

**All five of the task's named inheritances landed as stated.**

1. **There is no boss-spawn signal, and none was invented.** `BossPhaseChanged` at phase 0 *is* the
   arrival; `EnemyDied` is the departure. `Bar_DrawsNothingBeforeAnyEvent` publishes a real `EnemySpawned`
   for `enemy.warden` and asserts the band stays down — the row that would go red the day somebody added
   the `IsBoss` flag M4-00a refused.
2. **The fill is `EnemyDamaged.HpFraction` and there is no second source.** No `RunState` read, no
   blackboard: a corpse's fraction is frozen rather than zeroed (M4-01a). `Bar_IgnoresEveryOtherEnemy` is
   the row that matters here — a boss phase summons Husks by the handful, so an unfiltered band would be
   driven by whichever add was hit last and taken down by the first one to die.
3. **`Palette.Boss` — `#CCC4B7`, the tenth colour, argued by elimination.** A boss is not the player, not
   a reward, not corruption, and **may not be `Danger`** (rule 6: a permanent red-orange band across the
   top of the screen, up for the whole 75–120 s GD §9.1 rule 5 gives a fight, is the most visible possible
   breach of *"for nothing else, ever"*). So it is GD §16.4's *everything else*, and the only freedom left
   inside that band is **value** — so it is `Palette.Neutral` brightened ×1.85 per channel, which is
   `PlayerBlocked`'s move one band over. A separate member was **necessary rather than tidy**: `Neutral`
   is already an enemy's bar (M3-13b) *and* M4-03's beat shell, and `EnemyDying` is the dying tint, so a
   boss's band in any of them would say *"an ordinary enemy"*. The seams are `Palette.Neutral`, which is
   the contrast that makes one legible on a brightened bone; the track is `Palette.Boss` at the view's own
   `_trackAlpha`, because the palette is opaque and *"a view that wants transparency multiplies its own"*.
4. **Rule 8: no readability row is ticked, and the arrangement is stated as numbers.** **0–4 dp** the XP
   strip, **5–15 dp** this band, **16 dp** down the HP row — two 1 dp gaps.
   `Hud_StripAndBarDoNotOverlap` asserts all four edges, and **it also asserts the collision the spec did
   not name**: the HP row, which a full-width element was always going to reach first and which sits
   16 dp down with a 24 dp bar. **Manual step 4 was therefore answered before it was written** — nothing
   collides, and no number had to move. Whether two 1 dp gaps and a 10 dp band are *legible* on a HUD
   M3-15 already found too cramped to read is **M4-07's and the device's**, and it is three serialized dp
   fields when the answer comes back.
5. **Rule 7: the band draws no string at all, and that is stated rather than waived.** GD §16.2 asks for
   a bar; the name, the portrait and the intro card are out of scope, and a name needs a `LocKey` *and* a
   design answer about whether bosses are announced (M7). **So no `ILocalizer` was wired** — one taken for
   a view with no words is a dependency nothing uses, which is worse than none. `Bar_DrawsNoRawString`
   asserts the *absence*: no `string`, `LocKey`, `TMP_Text` or `ILocalizer` field on the class, and no
   label anywhere under the band **on the asset**, so nobody can type a word onto it without the row going
   red.

**And the one thing not chased was correctly not ours:** `M_TelegraphRing.mat`'s missing
`_ALPHAPREMULTIPLY_ON`. The band is a canvas `Image` on the HUD, shares no material with any world-space
decal, and the probe above read its colours back at the alphas it wrote (`#CCC4B7FF` fill, `#CCC4B738`
track).

### Three decisions worth arguing with

1. **The beat is a dim rather than a second colour.** `Bar_SaysSoDuringABeat` asserts the band is visibly
   different *at the same fill* and restored by `BossBeatEnded`, and what changes is the `CanvasGroup`
   alpha to `_beatAlpha` (0.4). That is the palette's own rule rather than a shortcut — every member is
   opaque and a view multiplies its own, the way `ThreatArrows`, `ZoneView` and `BulwarkView` do — and the
   alternative was an eleventh palette member for a state lasting 1.5 s, when the two nearby colours are
   both already taken on an enemy body. **Whether a dim reads as *"you cannot hurt it"* rather than as
   *"the HUD glitched"* is a device question**, and it is one field.
2. **Seams are cloned on demand and never destroyed.** The first fight clones as many as it needs — two,
   for the Warden — and every later boss reuses them; nothing is instantiated again unless a deeper boss
   has more phases than any before it. A boss spawn is already the frame a body, a look and a behaviour
   are all being built. `MaxSegments` is **12**, and it is a *refusal ceiling* rather than a layout number:
   nothing caps how many phases a `BossSpec` may hold, so a count that arrived wrong would otherwise be
   answered by creating that many `GameObject`s on the HUD. A count above it draws twelve and the fight
   plays on.
3. **A broken threshold list draws *fewer* seams, never evenly spaced ones.** `BossSpec` refuses an empty
   list and an out-of-order one, so every case in `Bar_SurvivesANonsenseThresholdList` is unreachable from
   an asset — but a NaN anchor is a rect that never renders again (AR §18.3), and **a mark invented in the
   wrong place is worse than a missing one, because the player would plan around it**. Out of order is
   drawn *where the list says* rather than sorted, which is *"read rather than assumed"* from the other
   side: a view that quietly sorted one would be hiding the fault the catalog already reports. Likewise
   `SetFraction` keeps the **last good** fill on a non-finite reading, which is the deliberate opposite of
   `EnemyHealthBar`'s *"read as full"* — that bar is up for 2.25 s over a body about to die, and this one
   is up for two minutes, where snapping to full would read as the boss healing.

### One finding about a file this task did not write

**Unity 6.3 rewrites `ProjectSettings/TimeManager.asset` into a new serialization format as soon as the
Editor touches the project**, turning `Fixed Timestep: 0.02` into a `serializedVersion: 2` count/rate
pair worth exactly 0.02. It appeared in the working tree during this task, is a **no-op re-serialisation**,
and was reverted — `git diff m3 HEAD -- ProjectSettings/` is still empty. It will come back for whoever
opens the Editor next; it is a one-line `git checkout`, and it is worth knowing before it looks like a
change somebody made.
