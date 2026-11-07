# BM-06b — Wild Swap drawn: both leap

**Size:** M · **Depends on:** BM-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.3; EA ruling (a teleport on a phone loses the hero)

## Goal

Space on the Beastmaster swaps him with the Hound: both bodies leap past each other, readable on a
phone. The streak and the button icon are BM-06c.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Characters/Beastmaster.asset` | — | Movement `Swap`, 0.25 s, cooldown 6, after-window 0.2 |
| `Game/Authoring/CharacterDefinition.cs` | Game | Accepts `Swap`; refuses it on a character without a pet, naming the character |
| `Animation/Controllers/AC_Beastmaster.controller` | — | `Jump_Full_Short` on the swap's start |
| `Game/Views/BeastmasterAnimatorView.cs` | Game | Plays the leap on `ChargeStarted` while the kind is `Swap` |
| `Animation/Controllers/AC_Hound.controller` | — | `Pounce` and `Land` states (clips exist in `Hound.fbx`), a `Leap` trigger |
| `Game/Views/PetView.cs` | Game | `Leap(to, seconds)`: a 0.6 m-high arc, `Pounce` on start → `Land` on arrival |
| `Game/Views/PetViews.cs` | Game | `ApplyLeaps(IReadOnlyList<PetLeapIntent>)` routes each to its pet's view by id |
| `Tests/Game/Views/WildSwapLeapTests.cs` | Tests.Game | **New** |
| *small edit* | | `RunTicker`: one line beside `ApplyMoves`, `_petViews?.ApplyLeaps(_intents.PetLeaps)` |

## Behaviour

1. The Beastmaster's movement is `Swap`: 0.25 s, cooldown 6 s, after-window 0.2 s.
2. He plays `Jump_Full_Short` on the swap's `ChargeStarted`; other kinds keep their own clip.
3. A `PetLeap` intent reaches the matching pet's view the same frame; an unknown id is ignored.
4. The hound arcs to `To` over `Seconds`, peaking 0.6 m at the midpoint, landing exactly on `To`;
   `Pounce` plays on start, `Land` on arrival. While leaping, `ApplyMoves` does not move it.
5. Both bodies stay visible throughout; the camera follows him with no cut (no code: the camera
   already follows the player transform; manual check).
6. `CharacterDefinition` refuses `Swap` on a character without a pet, naming the character.

## Tests

| Test | Given / When / Then |
|---|---|
| `Beastmaster_MovementIsSwap_Numbers` (1) | Load the asset → kind `Swap`, 0.25 / 6 / 0.2 |
| `ChargeStarted_Swap_PlaysJumpFullShort` (2) | Swap kind → leap clip; `Charge` kind → not |
| `PetLeap_RoutesToItsPet_UnknownIgnored` (3) | Two ids → only the matching view leaps; unknown id → no throw |
| `PetLeap_ArcsAndLands` (4) | Midpoint height 0.6 m ± 0.01; end == `To`; Pounce then Land |
| `PetLeap_MovesIgnoredWhileLeaping` (4) | A move during the leap → position follows the arc |
| `Swap_WithoutPet_Refused` (6) | Swap on a pet-less definition → error names it |

## Manual verification (Editor / device)

1. **[Editor]** Swap with the hound inside a frog pair: he lands in the fight, the hound lands
   behind, the frogs come back at him.
2. **[Editor]** Swap to a fallen hound: only he leaps, he lands on it, the rescue ring is half full.
3. **[device]** The hero is never lost during a swap.

## Out of scope

- The cyan streak and the swap icon on the movement button (BM-06c). Quick Swap (BM-07b).

## As built

_Filled at merge._
