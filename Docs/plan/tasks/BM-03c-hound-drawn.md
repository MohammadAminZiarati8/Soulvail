# BM-03c — The Hound drawn and in the run

**Size:** M · **Depends on:** BM-03a, BM-03b · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.2; GD §16.2; `MinionViews`

## Goal

The Beastmaster's run has a hound on the floor: moving where core says, biting, with an always-on
cyan bar.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/PetViews.cs` | Game | **New.** Spawns on `PetSpawned`; feeds `WorldSnapshot.Pets`; clears at stage changes |
| `Game/Views/PetView.cs` | Game | **New.** Moves on `PetMove` intents (as `MinionView` does), faces its travel, plays `Bite` on `PetBit` |
| `Game/Views/PetHealthBar.cs` | Game | **New.** Small cyan bar over the body, always shown |
| `Animation/Controllers/AC_Hound.controller` | — | **New.** `Idle` / `Trot` / `Run` by speed; `Bite` trigger |
| `Game/Authoring/CharacterDefinition.cs` | Game | Pet fields → `PetSpec`; a pet prefab slot |
| `Data/Characters/Beastmaster.asset` | — | Pet: `pet.hound`, 70 HP, 4.0 m/s, bite 7 / 1.0 s, reach 1.4, leash 8, return 10, heel 1.5; `Hound.prefab` |
| `Tests/Game/Views/PetViewsTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunScope` registers `PetViews` and `PetSystem`; `MinionViews` fills `Minions` itself, so `PetViews` fills `Pets` the same way; `Hound.prefab` gains `PetView` and the bar |

## Behaviour

1. One body per `PetSpawned`, at its position; its transform is reported each frame in `Pets`.
2. Moves at the intent's speed; animator speed parameter from the body's velocity.
3. `Bite` plays on each `PetBit` from that pet.
4. **Bar:** cyan (`Palette.Player`), above the body, always visible, filling from `Health`; it
   never takes an enemy bar's colour.
5. Stage transitions and run end remove the body; `StageArrived` brings it back through rule 1.
6. A character without a pet spawns nothing and registers no bar.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spawned_OneBody_ReportsPosition` (1) |
| `MoveIntent_MovesBody` (2) |
| `Bit_PlaysBite` (3) |
| `Bar_AlwaysShown_PlayerCyan` (4) |
| `StageTransition_RemovesBody` (5) |
| `NoPet_SpawnsNothing` (6) |
| `Beastmaster_PetNumbers_MatchCh362` | asset / converted / numbers as CH §3.6.2 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 3: the hound trots at heel, runs at the first marked frog, bites
   about once a second, comes back when the wave is clear.

## Out of scope

- Being hurt (BM-04); heel run, fall and rescue (BM-05a, BM-05b).

## As built

_Filled at merge._
