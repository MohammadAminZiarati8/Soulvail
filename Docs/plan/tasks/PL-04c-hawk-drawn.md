# PL-04c — Hawk drawn and authored

**Size:** M · **Depends on:** PL-04a, PL-04b · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Hawk); MK-07b

## Goal

The Packleader's Lv-17 active in the arc: a hawk circling overhead, a ground shadow, dives that
strike marked enemies.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/HawkView.cs` | Game | **New.** Placed from `HawkSystem.Position`; `Glide`/`Flap` circling, `Dive` → `Strike` → `Climb` |
| `Animation/Controllers/AC_Hawk.controller` | — | **New** |
| `Game/Authoring/CallHawkDefinition.cs` | Game | **New.** Damage share, stun |
| `Data/Skills/Packleader/Hawk*.asset` | — | **New.** Three ranks |
| `Data/Effects/Packleader/Hawk*.asset` | — | **New** |
| `Data/Paths/Packleader.asset` | — | Hawk in the Lv-17 row |
| `Tests/Game/Views/HawkViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Hawk.prefab` gains the view and a soft ground shadow; `RunScope` registers the view and a narrow read of `HawkSystem`; `English`/`Pseudo`; `SkillIconBook` |

## Behaviour

1. On `HawkArrived` the hawk swoops in from off-screen to its circle; on `HawkLeft` it flies off.
2. While circling, a soft dark shadow on the ground under it marks where it is (P1).
3. On `HawkDove` it dives along core's line; on `HawkStruck` it plays `Strike` with a small cyan
   burst, then `Climb`.
4. Its position never runs ahead of core's.
5. Hawk is a circle in the arc, auto-cast on by default.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arrived_Shows_Left_Hides` (1) |
| `Shadow_UnderHawk` (2) |
| `Dove_PlaysDive_Struck_PlaysStrike` (3) |
| `View_FollowsCorePosition` (4) |
| `Hawk_ThreeRanks_Numbers_Trigger` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Learn Hawk at 17: it circles, dives on marked frogs, stuns them briefly, leaves after 8 s.
2. **[device]** The hawk and its shadow read at phone zoom.

## Out of scope

- Stampede (PL-05).

## As built

_Filled at merge._
