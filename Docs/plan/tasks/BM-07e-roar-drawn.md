# BM-07e — Quick Swap's roar drawn

**Size:** S · **Depends on:** BM-07b, BM-07d · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.4 (Quick Swap); readable at first look on a phone

## Goal

When the Quick Swap roar fires, the hound plays a short `Howl` and a cyan 3 m ring shows the slowed
area.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Animation/Controllers/AC_Hound.controller` | — | A `Howl` state (clip exists in `Hound.fbx`) on a `Roar` trigger, back to locomotion |
| `Game/Views/PetView.cs` | Game | `Roar()`: fires the trigger; skipped while leaping or fallen |
| `Game/Views/PetViews.cs` | Game | Subscribes `PetRoared`, routes to the pet's view by id |
| `Game/Views/RoarRingView.cs` | Game | **New.** On `PetRoared`, a ring of `Radius` at `Position` that expands and fades in 0.4 s; pooled |
| `Prefabs/Vfx/RoarRing.prefab` | — | **New.** Unlit, `Palette.Player` |
| `Game/Composition/RunScope.cs` | Game | `[SerializeField]` ring prefab beside `_swapStreakPrefab`; registers the view |
| `Scenes/Run.unity` | — | Assigns the ring prefab on `RunScope` |
| `Tests/Game/Views/RoarRingViewTests.cs` | Tests.Game | **New** |

## Behaviour

1. `PetRoared` makes that pet's view play `Howl`; an unknown id is ignored.
2. A ring appears at `Position` sized to `Radius` (3 m), grows to full over 0.4 s while fading to
   zero, then returns to the pool; a second roar reuses it.
3. Colour `Palette.Player` only.
4. A missing ring prefab on `RunScope` fails loudly at scope build, naming the field.

## Tests

| Test | Given / When / Then |
|---|---|
| `PetRoared_PlaysHowl_UnknownIgnored` (1) |
| `Ring_SizedToRadius_FadesInPointFour_ThenPooled` (2) |
| `Ring_IsPlayerCyan` (3) |
| `RunScope_MissingRingPrefab_Throws` (4) |

## Manual verification (Editor / device)

1. **[Editor]** With Quick Swap learned, swap into frogs: the hound howls on landing and the ring
   covers exactly the slowed frogs.
2. **[device]** The ring reads at a glance and is gone within half a second.

## Out of scope

- Roar sound.

## As built

_Filled at merge._
