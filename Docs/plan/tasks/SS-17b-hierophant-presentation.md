# SS-17b — Hierophant prototype presentation

**Size:** M · **Depends on:** SS-17a · **Branch:** `ss-17b-hierophant-presentation`

**Design ref:** [GD §9.5](../../GameDesign.md#95-the-sunscar--stage-15-the-sunscar-hierophant).

## Goal

Present the fight on the existing jackal rig and staff with distinct, phase-safe tells.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Game/Views/HierophantView.cs` | Event-driven boss tells, audio, animation and seal lifetime |
| `Prefabs/Enemies/Hierophant.prefab` | Boss shell, Jackal Priest body, existing staff and seal-display references |
| `Prefabs/Arenas/Sunscar/HierophantSeal.prefab` | Reusable flush stand-in with separate light renderer |
| `Animation/Controllers/AC_Hierophant.controller` | Walk, staff sweep, staff raise and recovery using existing clips |
| `Tests/Game/Views/HierophantViewTests.cs` | Event parity, timing, binding, cleanup and asset checks |

Unity `.meta` companions follow their assets.

## Public API

`HierophantView` is a Game `MonoBehaviour` bound to the pooled `EnemyView`, using the existing Duneclaw/Shardback subscription pattern. Consume SS-17a's four events and existing scoped boss phase and death events. Use the seal prefab reference for the three cosmetic instances.

## Behaviour

1. Create three visual-only seals at the emitted world centres. They stay still while the boss moves and never obstruct the hero, navmesh or projectiles. Use a simple sandstone plaque stand-in. Clone the priest's rig and staff references into a separate boss prefab. Disable root motion. Use existing Duneclaw sweep and Shardback fan warning clips for the distinct cues.
2. On the matching arm event show the complete stationary red-orange footprint and the corresponding pose. Light only the selected seal. Stronger fill follows Core's progress. The sector's arc and rectangle match the armed payload including its dimensions. Keep geometry readable through the Dunes weather and independent of the moving body.
3. Replace the warning with a brief release on the matching impact event. Reset all warning, release, audio and pose state on phase change, death and pooled despawn. Keep neutral seals through phase changes and retire them on death/despawn. Rebind starts clean, and other enemy IDs have no effect.

Tests > Behaviour > API > Files. Built code wins; record deviations.

## Tests

| Test | Given / when / then |
|---|---|
| `Prefab_BindsPrototypeAssets` | Boss prefab / load / rig and staff assigned, root motion off, seal nonblocking, warning clips distinct |
| `Tell_MatchesCoreAndStaysInPlace` | Both attack payloads and progress / move the boss / same geometry, correct seal and fill, release at impact |
| `Lifecycle_ClearsAndRebinds` | Other ID, phase, death and pooled reuse / publish / scoped reset, no stale tell or duplicate seal |

## Manual verification

1. **[Editor]** Drive the view events on the Dunes. Check footprint edges against the boss and seals, impact timing and the two audible cues.
2. **[device]** In SS-17c's live fight, check that the safe edge stays visible at portrait scale through the sandstorm.

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
