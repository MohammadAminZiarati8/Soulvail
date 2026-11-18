# SS-15d — Shatter the sandstone slabs with a tail slam

**Size:** M · **Depends on:** SS-15a, SS-15c · **Branch:** `dev` (direct)
**Design refs:** [GD §9.3](../../GameDesign.md#93-the-sunscar--stage-5-the-duneclaw), `OpenGroundSense`, Invariants §§18.2, 18.4

## Goal

A slam aimed at a cracked floor slab shatters it and buys one extra second to attack.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Core/Ai/DuneclawBehaviour.cs` | Own slab selection, single-use state and extended recovery |
| `Core/Ai/DuneclawStats.cs` | Slab placement, aim tolerance and recovery stats |
| `Core/Events/DuneclawEvents.cs` | Slab lifecycle facts |
| `Game/Views/DuneclawView.cs` | Render slab facts and clean up pooled visuals |
| `Prefabs/Vfx/FX_DuneclawSlab.prefab` | Flush cracked stone and shatter dust, no collision |
| `Prefabs/Enemies/Duneclaw.prefab` | Two prebuilt visual slots rooted at the arena level while active |
| `Tests/Core/Ai/DuneclawSlabTests.cs` | Placement, aim, consumption and reset |
| `Tests/Game/Views/DuneclawViewTests.cs` | Slab event and cleanup tests |

## Public API

```csharp
// DuneclawSlabPlaced: BossId, Slot (int), Position (Vector3)
// DuneclawSlabShattered: BossId, Slot (int)
```

## Behaviour

1. On the first tick with measured `EnemyTickContext.OpenGround`, take the nearest valid XZ
   spot to each of two desired positions 3.5 m to either side of the boss, relative to its
   initial facing to the player. Placement is once per spawn, with list order breaking ties.
   Require each accepted spot within 1.5 m of its desired position and at least 3 m from the
   other slab, the boss's spawn and the initial player position. Keep fewer if space is limited.
2. A resolved slam consumes one intact slab if its locked centre is within 0.8 m XZ of the
   slab centre. Publish shattered and a 3.0 s impact recovery; otherwise publish the 2.0 s
   recovery. Use the same damage test either way. The player stepping clear after lock still
   breaks the slab. Phase transitions preserve slab state; behaviour reset clears it.
3. Show the two slabs flush on the floor with readable dark cracks and a diameter matching
   the 0.8 m aim radius. On shatter, remove the intact tile and play local dust at that spot.
   Keep them stationary while the boss moves. Slabs have no collider or NavMesh modifier.
   Despawn, pooling and disposal clear both tiles and their effects. No per-tick allocations.

## Tests

| Test | Given / When / Then |
|---|---|
| `Slabs_UseOpenGroundOnce_WithSpacingAndStableTies` | Crowded and open spot lists / spawn / legal fixed positions, up to two (1) |
| `Slam_LockedCentreOnSlab_BreaksOnceAndPinsThreeSeconds` | Intact tile / arm then walk clear, slam twice / 3 s then 2 s (2) |
| `Slam_EdgeOverlapOrPhaseCancel_PreservesSlab` | Off-centre or cancelled attack / expiry or new phase / tile intact (2) |
| `Reset_ClearsSlabsAndPlacementState` | One consumed / reset / clean new placement (2) |
| `SlabView_StationaryAndNonblocking_ShatterAndDespawnCleanUp` | Spawn and move boss / shatter and pool / visuals follow facts (3) |

## Manual verification (Editor / device)

1. **[Editor]** In stage 5 after SS-15e, bait a slam onto each slab, walk out of the circle and see the longer pin. Slam the same patch again and see the ordinary pin.
2. **[device]** Confirm the cracks and danger circle are legible together.

## Out of scope

General-purpose destructible cover and persistent terrain damage.

## As built

_Written at merge in the commit body._
