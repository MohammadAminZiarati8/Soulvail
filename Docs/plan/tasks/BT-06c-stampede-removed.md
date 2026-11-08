# BT-06c — Stampede's code removed

**Size:** S · **Depends on:** BT-06b · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Frenzy replaced Stampede)

## Goal

Nothing of Stampede runs or ships once Frenzy holds its slot; the boar model and its rig stay.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/StampedeSystem.cs` | Core | **Deleted** |
| `Core/Effects/Stampede.cs` | Core | **Deleted** |
| `Core/Events/StampedeEvents.cs` | Core | **Deleted** |
| `Game/Authoring/StampedeDefinition.cs` | Game | **Deleted** |
| `Game/Views/StampedeView.cs` | Game | **Deleted** |
| `Prefabs/Vfx/StampedeDust.prefab` | asset | **Deleted** (with its material if nothing else uses it) |
| *small edits* | | `RunSession` (field, construction, handler) · `RunScope` (the Stampede root) · Stampede tests deleted · `SkillIconBook` remarks |

## Behaviour

1. No type, registration, view or prefab named Stampede remains; every reference is gone, and the
   project compiles with zero new warnings.
2. `PlayerStat.StampedeBoars/Damage/HalfWidth` stay (ordinals are stored), documented "retired, unread".
3. The boar's model, materials and `Rig_Panther` skin from PL-05b are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoStampedeTypes_InCoreOrGame` (1) | reflection over both assemblies |
| `PlayerStat_Ordinals_Unchanged` (2) | the existing guard, still green |
| `BoarModel_StillImports` (3) | Tests.Game: the PL-05b model asset loads |

## Manual verification (Editor / device)

1. **[Editor]** Packleader run to Lv 20: Frenzy only; no console errors, no missing-script warnings.

## Out of scope

- Using the boar (BT-07a, BT-07b).

## As built

_Filled at merge._
