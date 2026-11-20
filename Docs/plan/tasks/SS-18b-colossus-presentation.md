# SS-18b — Colossus prototype and attack warnings

**Size:** M · **Depends on:** SS-18a · **Branch:** `ss-18b-colossus-presentation`

**Design refs:** [GD §9.6](../../GameDesign.md#96-the-sunscar--stage-20-the-sundial-colossus), [GD §16.4](../../GameDesign.md#164-color-language-p1).

## Goal

Make all attacks readable on an articulated stone stand-in with the final boss's silhouette.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Game/Views/ColossusView.cs` | Event subscriptions, pose, warnings, release and audio |
| `Prefabs/Enemies/SundialColossus.prefab` | Pooled enemy shell with articulated stone blockout |
| `Animation/Controllers/AC_SundialColossus.controller` | View-driven pose states with embedded prototype clips |
| `Tests/Game/Views/ColossusViewTests.cs` | Prefab, geometry, pose and pooled lifecycle |

Unity `.meta` companions follow their assets.

## Public API

`ColossusView` binds to its pooled `EnemyView` and consumes SS-18a events, `BossPhaseChanged`
and `EnemyDied`. The animator contract is states `Idle`, `Walk`, `SweepTell`, `SweepStrike`,
`ReturnTell`, `ReturnStrike`, `StompTell`, `StompStrike` and `Recover`. The view addresses
those state names and uses tell progress or impact slot to align the pose.

## Behaviour

1. Author a roughly 4 m sandstone prototype with a wide chest, large articulated right arm and grounded idle. Reuse existing materials and distinct warning audio from Duneclaw's sweep and slam. The prefab has the standard `EnemyView` collider pair, root motion off and zero animation events.
2. Draw all three full ground sectors from the arm event, locked in world space. Use red-orange fill with a bright start mark and directional chevrons. During release, flash the reported sector and fade spent sectors. Reverse changes the start and order using the same geometry. Stomp shows a filled disc with a clear radial edge. Cues and poses start from events, and the arm pins for the reported sweep recovery. Floor visuals have no colliders and add no camera or weather dependencies.
3. Scope events by bound boss ID. Phase entry, death and pooled despawn clear floor visuals, audio and old poses. Fresh bind starts neutral. Footprints may be drawn over Dunes cover and the display follows Core even on a missed strike.

Tests > Behaviour > API > Files. Built code wins. Record deviations.

## Tests

| Test | Given / when / then |
|---|---|
| `Prefab_UsesGroundedStoneBlockout` | Load shell / inspect / right-arm shape, collider pair, animator pose contract and distinct cues |
| `Warnings_UseLockedFootprintAndOrder` | Sweep, return and stomp payloads / drive tells, move body, resolve hits and misses / fixed geometry, order, pose and pinning |
| `Lifecycle_ClearsOnlyForBoundBoss` | Foreign events, own phase/death and pool reuse / dispatch / correct scoping and neutral rebind |

## Manual verification

1. **[Editor]** Drive one of each attack against the Dunes floor. Check the forearm and the flash cross the same sectors and the stomp uses a distinct cue.
2. **[device]** In SS-18c's fight, read the start mark, both directions and the stomp edge in portrait through the sandstorm.

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
