# BM-05b — Heel, fall and rescue drawn

**Size:** M · **Depends on:** BM-05a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.1; GD §16.4; P1 readable at first look

## Goal

The player can see the hound hurt, retreating, fallen and being rescued, at phone zoom.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/PetView.cs` | Game | `Hit` on `PetHurt`; `Fall` held on `PetFell`; `GetUp` on `PetRose`; heel run plays `Run` |
| `Game/Views/RescueRingView.cs` | Game | **New.** A cyan ring at the fallen body; its fill follows `RescueProgressed` |
| `Prefabs/Vfx/RescueRing.prefab` | — | **New.** Ground ring, 2.5 m radius, cyan, a fill arc |
| `Animation/Controllers/AC_Hound.controller` | — | `Hit`, `Fall` (holds), `GetUp` states |
| `Animation/Controllers/AC_Beastmaster.controller` | — | `Interact` kneel while a rescue fills near him |
| `Game/Views/BeastmasterAnimatorView.cs` | Game | Kneel on `RescueProgressed` while standing still; stops on `PetRose` |
| `Tests/Game/Views/RescueRingViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunScope` registers the ring; `PetHealthBar` hides while fallen |

## Behaviour

1. `Hit` plays on each `PetHurt`; the cyan bar drops.
2. Below the heel line the bar pulses (cyan, never red-orange) while the hound runs to heel.
3. **Fallen:** `Fall` plays and holds; the bar hides; the ring appears under the body, its radius
   the rescue radius, empty.
4. The ring's arc fills with `RescueProgressed`, stays where it got to when the player leaves.
5. `PetRose`: `GetUp`, the ring fades in 0.3 s, the bar returns at its new HP.
6. The Beastmaster kneels while a rescue fills and he is not moving; moving cancels the kneel only.
7. Stage change clears the ring.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fell_ShowsEmptyRing_HidesBar` (3) |
| `Progressed_FillsArc_KeptWhenAway` (4) |
| `Rose_FadesRing_ShowsBar` (5) |
| `Ring_IsPlayerCyan_RadiusMatchesSpec` (3) |
| `StageChange_ClearsRing` (7) |

## Manual verification (Editor / device)

1. **[Editor]** Let the hound fight three frogs alone: it runs back below a third, heals at heel,
   goes back in. Let it fall: ring, kneel 2 s beside it, it gets up.
2. **[device]** The fallen ring and the pulsing bar read at phone zoom in a crowd.

## Out of scope

- Wild Swap (BM-06).

## As built

_Filled at merge._
