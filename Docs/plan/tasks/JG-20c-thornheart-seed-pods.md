# JG-20c — Thornheart's seed pods: a kill race that grows Rootlings

**Size:** M · **Depends on:** JG-20b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 4; GD §8.1 (target priority); `IBossAddOwner` (JG-15c); boss summons pay no XP; unlike the Mother's pods, these must be *killed* to stop them

## Goal

Thornheart plants pods on open ground; a pod still alive when it ripens bursts into Rootlings, so
the player walks toward the pod to make the auto-aim pick it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ThornheartBehaviour.cs` | Core | Pods move, ripening, roll-call; `IBossAddOwner` |
| `Core/Events/ThornheartEvents.cs` | Core | Gains `PodPlanted` |
| `Tests/Core/Ai/ThornheartPodTests.cs` | Tests.Core | **New** |
| *small edits* | | ctor gains `podId`, `rootlingId`; `RunSession` row passes `enemy.thorn_pod`, `enemy.rootling` |

## Public API

```csharp
// ThornheartMove gains Pods = 3 (EnemyTelegraph kind 3)
// consts: PodTelegraphSeconds 0.8f, PodRipenSeconds 6f, PodClearance 1.5f; ints PodsPerCast 2,
//   RootlingsPerPod 3, MaxAdds 8
// ThornheartBehaviour : IBossAddOwner;  int AddCount { get; }  int PodCount { get; }
// ctor: (EnemyAgent, ThornSystem, IRandomStream, ContentId podId, ContentId rootlingId)
// ThornheartEvents gains PodPlanted(BossId, PodId, At, RipenSeconds)
```

## Behaviour

1. **Rotation** becomes Roots → Pods → Roots → Slam.
2. **Plant:** after `PodTelegraphSeconds`, up to `PodsPerCast` points from `OpenGroundPicker`
   (around the player, its clearances) that `ThornSystem.Blocks(point, PodClearance)` rejects not;
   each `SpawnSummon(podId, at)` and `PodPlanted`. A point the picker can't find is skipped.
3. **Ripen:** a pod alive `PodRipenSeconds` after planting despawns and `SpawnSummon`s
   `RootlingsPerPod` Rootlings evenly on a 1 m circle round it; 0 XP, still kills. A killed pod
   grows nothing. A registry without room skips the Rootling.
4. **Cap:** a pod counts `RootlingsPerPod` toward `AddCount`; a pod is planted only while
   `AddCount + RootlingsPerPod ≤ MaxAdds`.
5. **Roll-call:** pod and Rootling ids tracked; a dead one leaves that tick. `ClearAdds` (the beat)
   despawns all, pods included.
6. **Reset** empties the roll-call without despawning. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rotation_RootsPodsRootsSlam` (1) |
| `Pods_TwoPlanted_OnOpenGround_NotOnThorns` / `Pods_PickerFails_Skipped` (2) |
| `Pod_Survives_RipensIntoThreeRootlings_PayNoXp` / `Pod_Killed_GrowsNothing` / `Ripen_NoRoom_Skipped` (3) |
| `Cap_FullRoster_NoPod` (4) |
| `RollCall_DeadLeaves` / `ClearAdds_DespawnsPodsAndRootlings` (5) |
| `Reset_EmptiesRollCall` / `Tick_DoesNotAllocate` (6) |

## Out of scope

- The pod's data, priority and body (20f, 20h); phase changes (20d).

## As built

_Filled at merge._
