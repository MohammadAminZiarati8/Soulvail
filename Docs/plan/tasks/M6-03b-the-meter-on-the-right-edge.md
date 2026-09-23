# M6-03b — The meter on the right edge, the counter in the corner, and the node that is gone

**Size:** S · **Depends on:** M6-03a · **Branch:** `m6-03b-veilrot-meter-and-essence`
**Design refs:** GD §10.1, §10.2, §10.3, §16.1, §16.4; CH §5.1; AR §7, §8, §18.2 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m6) — two device rows; [3](../ROADMAP.md#carry-forward-into-m6) — touched and not moved; [7](../ROADMAP.md#carry-forward-into-m6) — three strings

## Goal

The two readouts GD §16.1 asks for and the build has never had — Essence in the top-right corner and
Veilrot down the right edge, with its four thresholds and the Claiming on it — and a node Banish took
stops drawing as one the player could still reach.

## Why these three are one task

They are the same sentence three times: **a state a merged mechanic creates that no screen says.**
[M6-04](M6-04-veilrot-thresholds-and-the-claiming.md) ships a meter, four thresholds and a latch and
draws none of it (*"Drawing any of it"* is its first *Out of scope* line);
[M6-02b](M6-02b-four-things-essence-buys.md) gives a node a fourth state and leaves it drawing as
`Locked`; [M6-01a](M6-01a-essence-wallet-and-drops.md) ships a wallet the HUD cannot see. All three
are read-only, all three are one value each, and all three are the kind of thing a playtest reports
as *"nothing happened"* rather than as a bug. **GD §10 is the document's own most emphatic section**
— *"If we cut one thing from this document, it should not be this"* — and until this task the
signature system is invisible for the whole run.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/VeilrotMeterView.cs` | Game | GD §16.1's vertical meter: a fill, four ticks, and the Claiming |
| `Game/Presentation/HudPresenter.cs` | Game | **Substantial.** The meter's place, the Essence counter, and the four subscriptions |
| `Tests/Game/Presentation/VeilrotMeterViewTests.cs` | Tests.Game | The fill, the ticks, the latch, and the colour rule |
| *small edits* | Game | `Game/Presentation/Palette.cs` — `NodeBanished`, and `Veilrot`'s summary; `Game/Controls/TreeNodeView.cs` — `NodeState.Banished` and its `Frame` line (rule 7); `Game/Presentation/TreeViewPresenter.cs` — the fourth state; `Prefabs/UI/Hud.prefab` — the meter and the counter; `Data/Localisation/English.asset` — three rows |
| *ripple* | Tests.Game | `HudPresenterTests` gains the counter and the meter; `PaletteTests` retires one row and gains two (rules 6, 10); `TreeViewPresenterTests` gains the fourth state |

**Nothing in `Soulvail.Core` changes.** The thresholds door rule 3 reads is
[M6-04's, amended at M6-00b](M6-04-veilrot-thresholds-and-the-claiming.md) so it ships right the
first time; this task is the reader that found it.

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Game/Presentation/VeilrotMeterView.cs — block namespace (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §10's meter, on screen: a vertical fill down the right edge, four marks where the
    /// thresholds are, and one more mark once the Claiming has begun.
    /// </summary>
    public sealed class VeilrotMeterView : MonoBehaviour
    {
        /// <summary>The meter, 0–1. What a test reads instead of a pixel.</summary>
        public float Fill { get; }

        /// <summary>Whether the Claiming mark is up — <c>RunState.IsClaimed</c>, drawn.</summary>
        public bool IsClaimed { get; }

        /// <summary>
        /// Draws <paramref name="value"/> of <c>Veilrot.Max</c>, with or without the Claiming.
        /// </summary>
        /// <remarks>
        /// A non-finite or negative value leaves the meter where it was, <c>HpBarView.Set</c>'s
        /// bargain: a NaN reaching a <c>fillAmount</c> is a graphic that never draws again.
        /// </remarks>
        public void Set(float value, bool isClaimed);

        /// <summary>Lays the four marks out from core's thresholds — rule 3.</summary>
        public void PlaceThresholds(float pixelsPerDp);
    }
}
```

```csharp
// Core/Run/Veilrot.cs — NOT changed here. Read for context: this is the door rule 3 argued for and
// M6-04's Public API now carries, amended at M6-00b before that task was built.
//     public static int ThresholdCount { get; }
//     public static float Threshold(int index);
```

## Behaviour

1. **`VeilrotMeterView` is a view and subscribes to nothing.** `HudPresenter` holds the
   subscriptions and calls `Set`; this class owns a fill, four marks and one flag.
   `HpBarView`/`XpBarView`'s bargain exactly, and the reason is theirs: a pooled or prefab-dressed
   view that reached for the hub would need injecting individually, and `HudPresenter` is the one
   thing `RunScope` registers.
2. **`HudPresenter` drives both readouts off events and reads the run for its first frame.**
   `EssenceChanged` writes the counter, `VeilrotChanged` writes the meter, `ClaimingBegan` raises
   the mark, and `RunStarted` seeds all three from `RunState.Essence`, `RunState.Veilrot` and
   `RunState.IsClaimed` — because a resumed run's wallet and meter are restored **silently**
   ([M6-01a](M6-01a-essence-wallet-and-drops.md) rule 8,
   [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md) rule 9), so there is no event to hear and a
   screen that only listened would draw a resumed run at zero on both.
3. **The thresholds are asked for one at a time, and M6-04's drafted array does not ship.** That
   spec's Public API reads `public static readonly float[] Thresholds = { 25f, 50f, 75f, 100f };`
   with the note *"Public because a HUD draws the ticks"* — this is that HUD, and the array is
   **static mutable state**: `readonly` protects the handle and not the four floats, so any caller in
   the process can write `Thresholds[3] = 5f` and every later comparison in the meter is wrong for
   the rest of the session. That is the exact thing AR §7 bans, and it is `PaletteTests`'
   `Palette_IsTheOneSanctionedStatic` written out — *"a readonly reference to a mutable object is a
   handle every caller could write through."* **Grepped: `Soulvail.Core` today has no
   `public static readonly` field at all**, so this would be the first and nothing would catch it.
   `ThresholdCount` and `Threshold(int)` hand out floats by value and cost the meter four calls once.
   **[M6-04's Public API was amended at M6-00b](M6-04-veilrot-thresholds-and-the-claiming.md) rather
   than corrected here**, because M6-04 merges first and undoing a shipped array costs that task's
   fixture as well as this one's. So this rule describes a door that already exists, and
   `Meter_ReadsTheThresholdsThroughTheDoor` is what objects if it does not.
4. **The fill is `Value` and the Claiming is a mark, because at 100 the two stop agreeing.** M6-04
   rule 6 ships one state combination that looks like a bug and is not: a run that reaches 100 and
   then buys Cleanse sits at `Value` 40 with `IsClaimed` still true, the three buffs on and the 75
   and 25 states off. A meter that drew the Claiming *as* a full bar would show 100 for a player
   whose meter is at 40, and a meter that dropped the mark with the value would tell them the gamble
   was refundable. So the fill follows `Value` down and the mark does not come off.
5. **The fill is `Palette.Veilrot` and may not be `Palette.Danger`, and that is a ruling rather than
   a default.** The Claiming is the most dangerous state the game has, which is precisely why the
   reserved colour is tempting here; GD §16.4 says *"for nothing else, ever"*, and `BossBarView`
   made the identical call at M4-04 rule 6 for a band that is up for a whole boss fight. This meter
   is up for a whole **run**. What the Claiming gets instead is the mark and `ui.hud.claimed`.
6. **`Palette.Veilrot` gets its first reader, so `Palette_HasTheColoursNobodyReadsYet` is retired
   rather than narrowed again.** [M6-03a](M6-03a-the-sanctum-screen.md) rule 11 turned it into a
   sweep and cut its claim down to this one colour; there is now nothing left for it to say, so it
   is deleted and replaced by `Palette_VeilrotIsTheMetersColour`, which asserts the fill is
   `#A855F7` and that `Palette.IsDanger(Palette.Veilrot)` is false. The two edits are one each and
   for different reasons, which is what stops this being the churn the
   [parking-lot line](../ROADMAP.md#parking-lot) warned about. `Palette.Veilrot`'s summary loses
   *"No reader yet (M6)"*.
7. **`NodeState.Banished` and its colour ship in the same file edit, because `TreeNodeView` refuses
   the alternative.** `TreeNodeView.Frame` throws for a member with no line — its own remarks say a
   new state without a colour *"would draw as locked, which is a node the player owns reading as one
   they cannot reach, silently"* — so the enum member, the `Frame` case and `Palette.NodeBanished`
   are one change. That class's remarks name **M6-02** as the task that would add it and
   [M6-02b](M6-02b-four-things-essence-buys.md) handed it on; this is where it lands, and the comment
   is corrected to say so.
8. **`NodeBanished` is `NodeLocked` darkened and nothing else, and the honest limit is stated.** A
   banished node is *gone* where a locked one is merely out of reach, so it reads dimmer than the
   dimmest thing on the screen. **Colour alone is a weak signal on a 24 dp cell that holds neither an
   icon nor a word**, and that is [ledger row 3](../ROADMAP.md#carry-forward-into-m6) — this task
   touches one of the two files that row is about and moves none of it, which is said here so nobody
   reads the diff and concludes otherwise. No hue shift toward violet: Banish is bought with Essence
   and has nothing to do with the Veil, and GD §16.4's violet means corruption.
9. **The four states are disjoint, so the presenter's order of checks cannot matter.**
   `SkillTree.Banish` refuses a taken node and `Check` returns closed for a banished ordinal
   (M6-02b rules 4, 5), so no node is ever two of `Taken`, `Available`, `Banished`. The presenter
   reads `RunState.BanishedNodeIds` — [M6-01b](M6-01b-save-format-v4.md) rule 8's read, given its
   writer at M6-02b — which is a walk of at most the taken-and-banished count per cell, 144
   comparisons for this build's twelve-node trees, on a screen drawn once when it opens.
10. **Both readouts are placed in dp at runtime and neither is authored into position.**
    `HudPresenter.Place`'s two reasons, unchanged: a Scale-With-Screen-Size canvas measures in
    reference pixels, and a meter authored at 200 of those is a different physical height on every
    phone. A non-finite or non-positive dp field leaves the prefab's layout alone rather than
    writing a NaN into a `sizeDelta`.
11. **Nothing here allocates on a frame.** Two `SetText` calls on events, one `fillAmount` write, and
    four marks placed once at `Start`. `HudPresenter` gains no `Update` work.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Meter_SubscribesToNothing` | `typeof(VeilrotMeterView)` / reflection over fields and methods / no `DomainEventHub`, no `IDisposable` subscription, no `[Inject]` — rule 1 |
| `Meter_DrawsTheValue` | `Set(42, false)` / — / `Fill` 0.42 |
| `Meter_ClampsAtBothEnds` | `Set(-5, false)`, `Set(140, false)` / — / 0 and 1, no throw |
| `Meter_IgnoresANonFiniteValue` | `Set(60, false)` then `Set(NaN, false)` / — / `Fill` still 0.6 — rule 1 |
| `Meter_DrawsFourMarks` | a dressed meter / `PlaceThresholds` / four marks at 25 %, 50 %, 75 % and 100 % of its height — rule 3 |
| `Meter_ReadsTheThresholdsThroughTheDoor` | `typeof(Veilrot)` / reflection / no `public static` **field**, and `ThresholdCount` is 4 — rule 3 |
| `Meter_TheClaimingIsAMarkAndNotAFullBar` | `Set(40, isClaimed: true)` / — / `Fill` 0.4 **and** `IsClaimed` — rule 4, M6-04 rule 6's odd state drawn |
| `Meter_TheMarkDoesNotComeOff` | claimed at 100, then `Set(40, true)` / — / still claimed |
| `Meter_IsViolet` | a drawn meter / — / the fill is `Palette.Veilrot`, `#A855F7` to eight bits — rule 5 |
| `Meter_IsNeverTheDangerColour` | every graphic on the meter / — / `Palette.IsDanger` false for each — rule 5 |
| `Hud_DrawsTheEssenceCounter` | `EssenceChanged(84, +24)` / — / the counter reads 84 in `Palette.Essence` |
| `Hud_SeedsBothFromTheRunOnStart` | a **resumed** run at 317 Essence and 78 Veilrot / `RunStarted` / counter 317, fill 0.78, and **no event was published** — rule 2 |
| `Hud_RaisesTheClaimingOnItsEvent` | `ClaimingBegan(200)` / — / `IsClaimed`, and `ui.hud.claimed` is drawn |
| `Hud_PlacesBothInDp` | two canvas scales / `Start` / the meter's height and the counter's inset differ by the scale — rule 10 |
| `Hud_ANonFiniteDpFieldIsIgnored` | `_meterSizeDp` NaN / `Start` / the prefab's layout is unmoved, no throw — rule 10 |
| `Hud_AllocatesNothingPerFrame` | 10 000 frames with the meter up / `AllocationAssert.None` / zero — rule 11 |
| `Node_BanishedDrawsItsOwnFrame` | a banished node / `Show(spec, NodeState.Banished, loc)` / the frame is `Palette.NodeBanished` — rule 7 |
| `Node_BanishedIsDimmerThanLocked` | both colours / — / `NodeBanished`'s value is below `NodeLocked`'s, and neither is `Danger` — rule 8 |
| `Node_AStateWithNoColourStillThrows` | a fifth member by reflection / `Frame` / `ArgumentOutOfRangeException` — `TreeNodeView`'s rule survives the fourth |
| `Tree_DrawsABanishedNodeAsBanished` | a run with one node banished and one taken / the tree screen opened / one `Banished` cell, one `Taken`, the rest `Locked`/`Available` — rule 9 |
| `Tree_TheFourStatesAreDisjoint` | every node of a run mid-banish / — / no id is in two of taken, available and banished — rule 9 |
| `Palette_VeilrotIsTheMetersColour` | *(replaces `Palette_HasTheColoursNobodyReadsYet`)* the sweep / — / `VeilrotMeterView` reads it and `IsDanger` is false — rule 6 |
| `Palette_BothReservedColoursHaveReaders` | `Veilrot` and `Essence` / the sweep over `Soulvail.Game` / each has at least one, and neither summary says *"no reader yet"* — rule 6 |
| `Strings_EveryKeyThisTaskDrawsHasARow` | the three keys / `English.asset` / each resolves — [ledger row 7](../ROADMAP.md#carry-forward-into-m6) |
| `Prefab_IsDressed` | `Hud.prefab` / loaded / a `VeilrotMeterView` with four marks and an Essence label, on top of everything M4-04 left |

**Guard rows are implied, not listed:** nulls to `Construct`, a missing meter or counter left silent
rather than throwing (`SkillBarPresenter`'s bargain — an undressed readout is a silent readout, not
a stopped run), and `Threshold(-1)` / `Threshold(4)`.

## Manual verification (Editor / device)

1. **[Editor]** Play a run with a debug command that grants Veilrot. The meter fills in steps down
   the right edge, each of the four marks passes under the fill as it is crossed, and at 100 the
   Claiming mark appears.
2. **[Editor]** Buy a Cleanse from 100. The fill drops to 85 and the Claiming mark **stays** — rule
   4's odd state, looked at rather than reasoned about.
3. **[Editor]** Clear three stages. The top-right counter reads 24, 52, 84, and falls when something
   is bought in the Sanctum.
4. **[Editor]** Banish a node and open the tree. It draws in its own frame rather than as *Locked*.
5. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**, two rows: whether
   `#A855F7` is distinguishable from the arena's bone at phone brightness (GD §16.4), and whether a
   meter drained at 1 %/s under the Claiming reads as *a clock running out* rather than as a bug at
   six inches. Neither is answerable in an Editor whose `Screen.dpi` reads 120
   ([Traps §9](../../Traps.md)).

## Out of scope

- **GD §16.1's *"grows visually more organic and invasive as it fills"*, and GD §10.2's fraying
  screen edges.** Both are art and post-process; the meter ships as a fill and a mark. **M7**'s art
  pass and **M8-01**'s game-feel pass.
- **HUD opacity** (GD §16.1's 40 % floor). **M8-02**'s options screen.
- **The tree screen's redesign.** [Ledger row 3](../ROADMAP.md#carry-forward-into-m6), unmoved and
  said so in rule 8.
- **Anything that changes a number.** Every value here is read; `Veilrot`'s only edit is rule 3's
  door, which moves no arithmetic.
- **A second reader for `Palette.NodeBanished`.** `AutoCastRow` draws Actives the player owns, and a
  banished node was never owned.

## As built

**Built to the table's shape.** `VeilrotMeterView` (track, vertical fill, four marks cloned from one
template, a Claiming cap); `HudPresenter` drives it and a top-right counter off `EssenceChanged`,
`VeilrotChanged` and `ClaimingBegan`, seeded from `RunState` on `RunStarted`; `NodeState.Banished` +
`Palette.NodeBanished` `(0.15, 0.16, 0.19)`; `TreeViewPresenter.StateOf` scans `BanishedNodeIds`.
EditMode 2 709 → **2 736, +27**; PlayMode **26**, unchanged.

### Deviations

1. **The HUD reads words again — this changes M4-06 rule 2's decision.** `ui.hud.claimed` is on the
   Tests table, and a word needs an `ILocalizer`, so `HudPresenter.Construct` is three parameters.
   `RunEndPresenterTests.Hud_NoLongerOwnsTheDeathOverlay` now pins `(hub, session, localizer)`, keeps
   `SceneLoader`/`InputAdapter` out, and asserts the class's only `LocKey`s are this task's three.
   The three words are written once at `Start`, never per event.
2. **The three keys are `ui.hud.essence`, `ui.hud.veilrot`, `ui.hud.claimed`.** The spec named one.
   The counter is a bare number (`{0:0}`, TMP's non-allocating overload) with a caption under it;
   the meter has a caption under it; the Claiming's name sits left of the meter's top, hidden until
   claimed. All three are violet or gold, never `Danger`.
3. **No `HudPresenterTests` exists** — the HUD's rows have always lived with their view. The
   `Hud_*` rows are in `VeilrotMeterViewTests`.
4. **Five test files outside the table.** `BossBarViewTests`, `GrantedShieldTests`,
   `XpBarViewTests` — the new `Construct` argument; `RunEndPresenterTests` — deviation 1;
   `SkillBarPresenterTests.Hud_NothingElseMoved` — **M4-07's legibility guard caught four unmeasured
   text elements on its first run.** The captions shipped at 24 pt (≈ 10.6 dp, under Android's
   12 sp); they are 28 pt (12.36 dp, the slot labels' size) and the row now measures all four
   against the floor. `Palette_EssenceHasTwoReadersAndItsSummarySaysSo`'s exact set gains
   `HudPresenter`, the corner counter's reader.
5. **A negative value clamps to 0 rather than being ignored** — the Public API's remark and
   `Meter_ClampsAtBothEnds` disagreed and the table wins. Non-finite is still ignored.
6. **The view latches `IsClaimed` itself**, so `Set(40, false)` after a Claiming leaves the mark up.
   Stronger than rule 4 asked; `RunState.IsClaimed` never goes false, so no correct caller notices.
7. **Placement, which the spec left open.** `PausePresenter` owns the top-right corner (44 dp at a
   16 dp margin), so the counter's default inset is **68 dp** in, beside the icon, and the meter's is
   **76 dp** down, under it: 12 × 160 dp. Serialized, so the owner can move either on a device.
8. **Public reads beyond the API:** `MarkCount` and `MarkFraction(int)`, `BossBarView`'s shape.
   **Rows beyond the table:** `Construct_RefusesNulls`, `Hud_AnUndressedReadoutIsSilent`,
   `Hud_DropsItsEconomySubscriptions`. The `Node_*` rows are in `TreeViewPresenterTests`.

### Findings

- **Manual steps 1 and 2 cannot be run in this build.** Nothing gains Veilrot until M6-05b and
  M6-03a removed the debug overlay's F-keys, so there is no grant to press. Steps 3 and 4 can.
- **`FrameOrderTests.Ticker_SnapshotPrecedesTheTick` failed once** — *"the body walked"*, 0.24 m
  against a 0.5 m sanity floor on the first frame. Not known issue 1's row; a fake core and a bare
  body, nothing this task touched. The next two PlayMode runs were clean. One sighting, not a rate.
- **Ledger row 8 DISCHARGED**; ledger rows 1 and 3 touched and not moved, as rule 8 says.
