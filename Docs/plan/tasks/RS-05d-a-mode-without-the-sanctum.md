# RS-05d — A mode without the Sanctum

**Size:** M · **Depends on:** RS-05a (the mode's first switch, and the Ordeals it sets aside) · **Branch:** `rs-05d-a-mode-without-the-sanctum`
**Design refs:** GD §7.1, GD §13.3, GD §13.4, GD §15 · **Ledger rows:** none

The owner's message of 2026-09-28, sent while RS-05a to RS-05c were being built: *"after that
disable the thing that after every stage a window comes and player can do some things like heal.
disable the whole feature. just disable so later if we want we can enable it again."*

## Goal

A mode can say it has no Sanctum. A run of it goes from a stage's clear straight to the open door,
draws no Essence, and is dealt no Ordeal that changes Essence. The shipped modes say so. Ticking the
mode's box brings the whole feature back.

## Why these three and no more

- **The Sanctum is the one thing that spends Essence** (`SanctumShop.Buy`, `Banish`). With it off, a
  counter that fills and buys nothing is noise, and so is Famine (Essence ×0.6). Both are hidden or
  skipped, the way RS-05a skipped Hunger.
- **The run still earns Essence and saves it.** The wallet, the income table and the save are
  untouched, so a save written with the Sanctum off resumes correctly after it is switched back on.
  *"Just disable"* is taken at its word.
- **The door already opens at the clear.** `ArenaPool` drops the barrier and shows the door on
  `StageCleared`, a phase before the Sanctum, so skipping the phase needs no view change.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Stage/StageFlow.cs` | Core | Clear → Gate when the mode has no Sanctum (rule 2) |
| `Tests/Core/Stage/StageFlowTests.cs` | Tests.Core | rule 2's rows |
| *small edits* | | `Core/Content/ModeSpec.cs` (`hasSanctum`, `HasSanctum`); `Game/Authoring/ModeDefinition.cs` (`_hasSanctum`); `Core/Run/Ordeals.cs` (rule 4); `Core/Run/RunState.cs` (`HasSanctum`), `RunSession.cs` (passes it); `Game/Presentation/HudPresenter.cs` (rule 3); `Data/Modes/Jungle.asset`, `Descent.asset` (`_hasSanctum: 0`); `Tests/Core/Run/OrdealsTests.cs`, `Tests/Game/Presentation/VeilrotMeterViewTests.cs`, `Tests/Game/Authoring/ModeDefinitionTests.cs` (rows) |

## Public API

```csharp
// ModeSpec: optional and last, after hasVeilrot
public ModeSpec(..., bool hasVeilrot = true, bool hasSanctum = true);
public bool HasSanctum { get; }

// RunState: set by RunSession from the mode
public bool HasSanctum { get; }
```

## Behaviour

1. **The mode says whether a run stops in the Sanctum.** `ModeDefinition._hasSanctum` reaches
   `ModeSpec.HasSanctum`, and a mode that never mentions it has one.
2. **Without it, the clear beat leads to the door.** `StageFlow` goes from `Clear` to `Gate` after
   `ClearTime`. The `Sanctum` phase is never entered: no `SanctumOpened` is published, and
   `LeaveSanctum` has nothing to leave. The door takes the player as before.
3. **Without it, the HUD draws no Essence.** The counter and its word are hidden, read off
   `RunState.HasSanctum` where the HUD reads the rest of the run. The Veilrot meter is RS-05b's.
4. **Without it, no Ordeal that changes Essence is dealt.** It is set aside at construction, like
   RS-05a's Veilrot rule and beside it. With both off, the shipped pool deals Vigil and Swarm.
5. **Every shipped mode has it off.**
6. **A mode with the Sanctum is unchanged.**

## Tests

| Test | Given / When / Then |
|---|---|
| `ModeDefinitionTests.ToSpec_CarriesWhetherTheModeHasASanctum` | a new definition / `ToSpec`, then the flag cleared / on, then off (rule 1) |
| `StageFlowTests.Stage_AModeWithoutTheSanctumOpensTheDoorAfterTheClear` | no Sanctum, a stage cleared / the clear beat / `Gate`, no `SanctumOpened`, `LeaveSanctum` refused; the door takes the player (rule 2) |
| `StageFlowTests.Stage_AModeWithTheSanctumStillStopsInIt` | the Sanctum on / the clear beat / `Sanctum` (rule 6) |
| `VeilrotMeterViewTests.Hud_HidesTheEssenceCounterInARunWithoutTheSanctum` | the shipped HUD over a run without it / started / counter and word hidden, the meter shown (rule 3) |
| `VeilrotMeterViewTests.Hud_ShowsTheEssenceCounterInARunWithTheSanctum` | with it / started / both shown (rule 6) |
| `OrdealsTests.NoSanctum_NeverDealsAnOrdealThatChangesEssence` | the four shipped Ordeals, no Sanctum / stages 2–75 / three dealt, never Famine (rule 4) |
| `OrdealsTests.NoVeilNoSanctum_DealsOnlyTheTwoThatStillMeanSomething` | both off / stages 2–75 / Vigil and Swarm (rule 4) |
| `ModeDefinitionTests.EveryShippedMode_HasTheSanctumOff` | every `ModeDefinition` asset / `ToSpec` / `HasSanctum` false (rule 5) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → the Ranger → descend, and clear stage 1. *Expected:* the barrier
   drops, and after the clear beat no Sanctum window appears. The vine curtain is parted and walking
   into it starts stage 2. No Essence counter in the HUD's top-right corner, and no Veilrot meter
   down its right edge.
2. **[Editor]** In `Data/Modes/Jungle.asset`, tick *Has Sanctum* and play a stage. *Expected:* the
   Sanctum is back, with the Essence the run has earned. Untick it again afterwards.

## Out of scope

- Removing the Sanctum, the shop, Essence or its income: the owner asked for off.
- The level-up screen's reroll, bought only at the Sanctum: with no Sanctum a run never has a charge,
  so the screen never offers one.

## As built

**As specified.** `StageFlow` already held the mode, so rule 2 is one branch in its `Clear` case.
Ordeals now sets aside a pool position for either switch in one loop. RS-05a's Veilrot-only loop
became the pair.

**Verified.** EditMode 3 439 — 3 438 passed, 0 failed, 1 inconclusive (the animator clock row) —
+8 on RS-05c's 3 431. **PlayMode, once over RS-05a to RS-05d:** the first full pass (67) failed two rows —
`Loop_ShootsOnlyStandingStill`, and RS-05c's `Run_ADirectPlayWearsTheFirstClassesBody`, which pinned
the old fallback and was re-pointed in RS-05c's commit. The second full pass failed three rows: two on
the AI Assistant's *"Token refresh timed out"* warning (Known issue 5), and
`Loop_ShootsOnlyStandingStill`. The three fixtures run alone gave 35 of 36. Both token rows passed,
and `Loop_ShootsOnlyStandingStill` failed with the Editor reporting `isApplicationActive` false. That
is Traps §8's row, which fails on `dev` too, in a sandbox loop none of RS-05 touches. Console: no
errors. Format check clean.
