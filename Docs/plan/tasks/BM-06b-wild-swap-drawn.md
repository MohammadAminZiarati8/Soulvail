# BM-06b — Wild Swap drawn and authored

**Size:** M · **Depends on:** BM-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.3; EA ruling (a teleport on a phone loses the hero)

## Goal

Space on the Beastmaster: both bodies leap past each other with a cyan streak between, readable
on a phone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Characters/Beastmaster.asset` | — | Movement `Swap`, 0.25 s, cooldown 6, after-window 0.2 |
| `Game/Authoring/CharacterDefinition.cs` | Game | Accepts `Swap`; refuses it on a character without a pet |
| `Animation/Controllers/AC_Beastmaster.controller` | — | `Jump_Full_Short` on the swap's start |
| `Game/Views/BeastmasterAnimatorView.cs` | Game | Plays the leap on `ChargeStarted` while the kind is `Swap` |
| `Game/Views/PetView.cs` | Game | Moves along a 0.6 m-high arc on `PetLeap`, plays `Pounce` → `Land` |
| `Game/Views/SwapStreakView.cs` | Game | **New.** A cyan streak joining the two points, 0.3 s |
| `Prefabs/Vfx/SwapStreak.prefab` | — | **New** |
| `Tests/Game/Views/SwapStreakViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `SkillIconBook` swap icon for the movement button; `RunScope` registers the streak; `English`/`Pseudo` |

## Behaviour

1. Space on the Beastmaster swaps; the movement button shows the swap icon and its 6 s sweep.
2. He plays `Jump_Full_Short`; the hound arcs to his start point; both stay visible throughout;
   the camera follows him with no cut.
3. On `Swapped`, a cyan streak joins `PlayerFrom` and `PlayerTo` for 0.3 s, then fades.
4. With the hound fallen, only he leaps; the rescue ring jumps to half.
5. `CharacterDefinition` refuses `Swap` without a pet, naming the character.
6. Colours: `Palette.Player` only.

## Tests

| Test | Given / When / Then |
|---|---|
| `Beastmaster_MovementIsSwap_Numbers` (1) |
| `Swapped_StreakJoinsBothPoints_FadesInPointThree` (3) |
| `PetLeap_ArcsAndLands` (2) |
| `Swap_WithoutPet_Refused` (5) |
| `Streak_IsPlayerCyan` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Swap with the hound inside a frog pair: he lands in the fight, the hound lands
   behind, the frogs come back at him.
2. **[Editor]** Swap to a fallen hound: he lands on it, the ring is half full.
3. **[device]** The hero is never lost during a swap.

## Out of scope

- Quick Swap (BM-07b).

## As built

_Filled at merge._
