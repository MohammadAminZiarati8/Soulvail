# RS-05b — The Veil, unseen

**Size:** S · **Depends on:** RS-05a (the switch) · **Branch:** `rs-05b-the-veil-unseen`
**Design refs:** GD §10, GD §13.3, GD §16.1 · **Ledger rows:** none

The second half of the owner's *"disable the Veilrot"* of 2026-09-28. RS-05a made the meter a
switch on the mode. This task hides what the screens draw of it and switches the shipped modes off.

## Goal

A run of a mode without Veilrot shows no meter on the HUD and no Cleanse in the Sanctum. The Jungle
and Descent are such modes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HudPresenter.cs` | Game | the meter and its two words hidden (rule 1) |
| `Game/Presentation/SanctumPresenter.cs` | Game | the Cleanse row hidden (rule 2) |
| `Data/Modes/Jungle.asset`, `Descent.asset` | — | `_hasVeilrot: 0` (rule 3) |
| *small edits* | | `Tests/Game/Presentation/VeilrotMeterViewTests.cs`, `SanctumPresenterTests.cs`, `Tests/Game/Authoring/ModeDefinitionTests.cs` (rows, and a fixture flag for the mode) |

## Behaviour

1. **The HUD draws no meter in a run without it.** The meter, its *Veilrot* caption and the
   *Claimed* word are hidden, read off `RunState.HasVeilrot` where the HUD already reads the meter's
   value. The Essence counter stays.
2. **The Sanctum offers no Cleanse in a run without the meter.** The row is hidden rather than
   refused. A refusal names a reason (*"nothing to cleanse"*), and there is no Veilrot in this run to
   name. It is the last of the four rows, so hiding it leaves no hole. Reroll, Banish and Heal are
   unchanged.
3. **Every shipped mode has the meter off.** The Jungle and Descent, which is reached only by a
   Continue.
4. **A run with the meter is drawn as before.**

## Tests

| Test | Given / When / Then |
|---|---|
| `VeilrotMeterViewTests.Hud_HidesTheMeterInARunWithoutIt` | the shipped HUD over a run without the meter / started / meter, caption and *Claimed* hidden; Essence shown (rule 1) |
| `VeilrotMeterViewTests.Hud_ShowsTheMeterInARunWithIt` | the same with the meter / started / meter and caption shown, *Claimed* hidden (rule 4) |
| `SanctumPresenterTests.Sanctum_ARunWithoutTheMeterHasNoCleanseRow` | a run without the meter / the Sanctum opens / Cleanse hidden; Reroll and Heal shown and working (rule 2) |
| `SanctumPresenterTests.Sanctum_ARunWithTheMeterKeepsTheCleanseRow` | a run with the meter / the Sanctum opens / Cleanse shown and buyable (rule 4) |
| `ModeDefinitionTests.EveryShippedMode_HasTheVeilrotMeterOff` | every `ModeDefinition` asset / `ToSpec` / `HasVeilrot` false (rule 3) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → the Ranger → descend. *Expected:* no violet meter down the HUD's right
   edge and no *Veilrot* caption; the Essence counter is still in the corner. No level-up card
   carries a Pact's violet border.
2. **[Editor]** Clear a stage. *Expected:* the Sanctum shows Reroll, Banish and Heal, and no Cleanse.
   RS-05d, asked for during this task, switches the Sanctum off as a whole, so this step is read
   before RS-05d or with its switch on.

## Out of scope

- The Sanctum as a whole (RS-05d). Class select (RS-05c).
- The tree screen and the level-up card's Pact border: with no Pact offered, neither draws one.

## As built

**As specified.** The HUD reads the switch in `Redraw`, where it already reads the meter's value
and the Claiming off `RunState`. `WriteMeter` shows or hides the three objects, touching each only
when its state changes, so the HUD's frame path is unchanged. The Sanctum reads it in `Draw`,
because every redraw passes through there, the first draw and the one after each tap alike.

**Verified.** EditMode 3 422 — 3 421 passed, 0 failed, 1 inconclusive (the animator clock row) —
+5 on RS-05a's 3 417. Console: no errors. Format check clean on the five C# files.
