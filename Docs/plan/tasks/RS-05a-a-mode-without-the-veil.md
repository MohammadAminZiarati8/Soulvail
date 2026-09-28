# RS-05a — A mode without the Veil

**Size:** M · **Depends on:** M6-04 (the meter), M6-05b (Pacts), M6-06b (Ordeals) · **Branch:** `rs-05a-a-mode-without-the-veil`
**Design refs:** GD §10, GD §13.2, GD §13.4 · **Ledger rows:** none

The owner's go of 2026-09-28: *"disable the Veilrot, and disable all characters except the Ranger"*,
after *"is it easy to disable Veilrot?"* was answered with three depths. The owner chose off, not
removed. So the meter becomes a switch that is off, and the code, the saves and the three classes'
relationships stay as they are. RS-05b hides what the screens draw of it and switches the shipped
modes off. RS-05c is the class-select half.

## Goal

A mode can say it has no Veilrot. A run of it is offered no Pact, starts at zero whatever its class,
never fills, comes back at zero from a save, and is dealt no Ordeal that multiplies the meter.

## Why the switch is on the mode, and read by the meter

- **A place is a mode** (the 2026-09-26 direction), and the mode already carries a run's other
  rules: Overflow, the Sanctum, the Ordeals. "The Jungle has no Veil" is one Inspector tick.
- **One question, asked of one object.** Only one thing ever *gains* Veilrot: taking a Pact card
  (`LevelUpFlow.Choose`). A class *starts* with it (the Gravecaller at 15), and a save *restores*
  it. The meter answers `IsOn`, and those three writers ask it, so nothing else needs the mode.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/Veilrot.cs` | Core | `isOn`, `IsOn`; `Gain` and `Restore` inert when off; the class start skipped (rule 2) |
| `Core/Run/Ordeals.cs` | Core | a mode without the meter sets aside Ordeals that multiply it (rule 4) |
| `Tests/Core/Run/VeilrotTests.cs` | Tests.Core | rows for rules 2 and 5 |
| *small edits* | | `Core/Content/ModeSpec.cs` (`hasVeilrot`, `HasVeilrot`); `Game/Authoring/ModeDefinition.cs` (`_hasVeilrot`); `Core/Progression/LevelUpFlow.cs` (rule 3); `Core/Run/RunSession.cs` (passes the mode's word); `Core/Run/RunState.cs` (`HasVeilrot`); `Data/Modes/Descent.asset`, `Jungle.asset` (the key, **on**); `Tests/Core/Progression/PactOfferTests.cs`, `Tests/Core/Run/OrdealsTests.cs`, `Tests/Game/Authoring/ModeDefinitionTests.cs` (rows) |

## Public API

```csharp
// ModeSpec: optional and last, for Essence's reason (sixty-odd fixtures construct one).
public ModeSpec(..., IReadOnlyList<OrdealSpec> ordeals = null, bool hasVeilrot = true);
public bool HasVeilrot { get; }

// Veilrot
public Veilrot(PlayerStats stats, PlayerCombat combat, CombatBlackboard blackboard, IDomainEvents events,
               Ordeals ordeals = null, VeilrotSpec relationship = null, bool isOn = true);
public bool IsOn { get; }

// RunState: a narrow read, AR §18.2
public bool HasVeilrot => Rot.IsOn;
```

## Behaviour

1. **The mode says whether a run has the meter.** `ModeDefinition._hasVeilrot` reaches
   `ModeSpec.HasVeilrot`, and a mode that never mentions it has the meter.
2. **With the meter off, it reads zero for the whole run.** The class's start is not applied,
   `Gain` changes nothing, and a `Restore` from a save leaves it at zero and unclaimed. So no
   threshold, no enemy speed bonus and no Claiming can happen. `RunSession` passes the mode's word,
   and `RunState.HasVeilrot` reports it.
3. **With the meter off, no level-up card is a Pact.** The roll still spends its two draws on the
   Offers stream (M6-05b rule 1), so a seed deals the same cards with the meter on or off. Only the
   Pact is dropped.
4. **A mode without the meter never deals an Ordeal that multiplies it.** GD §13.4's Hunger (×1.5)
   would be a card that changes nothing. It is set aside at construction: it is not counted in
   `Remaining`, not drawn, and not applied by a `Restore`.
5. **A mode with the meter is unchanged.** Every rule above is behind the switch.

## Tests

| Test | Given / When / Then |
|---|---|
| `ModeDefinitionTests.ToSpec_CarriesWhetherTheModeHasVeilrot` | a new definition / `ToSpec`, then the flag cleared / on, then off (rule 1) |
| `VeilrotTests.Off_TheMeterIsOnUnlessTheModeSaysSo` | the fixture's meter / read / on (rules 1, 5) |
| `VeilrotTests.Off_ReadsZeroAndIgnoresTheClassStart` | the Gravecaller's relationship, meter off / built / zero, no event (rule 2) |
| `VeilrotTests.Off_AGainDoesNotMoveIt` | meter off / a gain of 100 / zero, unclaimed, maximum untouched (rule 2) |
| `VeilrotTests.Off_ARestoreDoesNotMoveIt` | meter off / restored at 100 and Claimed, a second ticked / zero, unclaimed, no drain, blackboard zero (rule 2) |
| `VeilrotTests.Off_ARunResumedIntoAModeWithoutItComesBackAtZero` | a save at 100 and Claimed / resumed into a mode without the meter / `HasVeilrot` false, zero, unclaimed, full maximum (rule 2) |
| `VeilrotTests.Off_ARunOfAModeWithTheMeterHasIt` | the same save, a mode with the meter / resumed / `HasVeilrot` true, 100 (rule 5) |
| `PactOfferTests.Flow_OffersNoPactWithTheMeterOff` | the roll that puts a Pact on card 1, meter off / offered, card 1 taken / no Pact index, five draws, a clean take, zero (rule 3) |
| `OrdealsTests.NoVeil_NeverDealsAnOrdealThatMultipliesIt` | the four shipped Ordeals, no meter / stages 2–75 / three dealt, never Hunger, multiplier 1 (rule 4) |
| `OrdealsTests.NoVeil_ARestoreSkipsItToo` | a save holding Hunger and Famine, no meter / restored / Famine alone (rule 4) |
| `OrdealsTests.WithTheVeil_HungerIsStillDealt` | the four, a mode with the meter / stages 2–55 / Hunger among them (rule 5) |
| *(unchanged)* `ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField` | `Descent.asset` carries `_hasVeilrot` / reserialised / unchanged |

## Manual verification (Editor / device)

None of its own: both shipped modes keep the meter on here. RS-05b switches them off and lists what a
player sees.

## Out of scope

- Hiding the HUD meter and the Sanctum's Cleanse, and switching the shipped modes off (RS-05b).
- Class select (RS-05c), and the Sanctum as a whole (RS-05d, asked for during this task).
- Removing Veilrot: the owner asked for off, so it can come back on.

## As built

**As specified**, with one note on order. The first suite pass failed exactly one row,
`Descent_EveryYamlKeyBindsToAField`: it reserialises `Descent.asset`, and the new field added a key.
The row's own reserialise wrote `_hasVeilrot: 1` into the asset. `Jungle.asset` got the same line
by hand, so both assets say **on** here and RS-05b is the one that turns them off. Each commit stays
green on its own.

**The switch is read by the meter, not by each caller.** `LevelUpFlow` asks `_veilrot.IsOn`.
`Ordeals` reads `ModeSpec.HasVeilrot` at construction, because it is built before the meter. The
Sanctum and the HUD are RS-05b's, through `RunState.HasVeilrot`.

**Verified.** EditMode 3 417 — 3 416 passed, 0 failed, 1 inconclusive (the animator clock row,
by construction) — +11 on M7-05i's 3 406: VeilrotTests 6, OrdealsTests 3, PactOfferTests 1,
ModeDefinitionTests 1. Console: no errors, and no warning but the five reload-time "0 spawn
point(s)" lines (Known issue 2). PlayMode is run once over RS-05a to RS-05d.
