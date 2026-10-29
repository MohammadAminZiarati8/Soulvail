# JG-12g — Stones to hide behind

**Size:** M · **Depends on:** JG-12f (both widen `EnemyLook`) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 6, GD §12.4 (cover guarantee), GD §16.4; AR §18.2, AR §18.4

## Goal

An archetype can bring cover with it. Mother Pitcher's is four great stones ringed round her, on the
`Cover` layer, so standing behind one blocks her spray (`HasLineOfSight` false).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/CoverViews.cs` | Game | **New.** Places and lifts an archetype's cover |
| `Prefabs/Arenas/Jungle/Cover_MotherStones.prefab` | — | **New.** Four KayKit Forest Nature rocks |
| `Tests/Game/Views/CoverViewsTests.cs` | Tests.Game | **New.** Rules 1–5 |
| *small edits* | | `Game/Authoring/EnemyLook.cs` (`Cover`, optional last); `Game/Authoring/EnemyDefinition.cs` (`_cover`); `Game/Composition/RunScope.cs` (registers `CoverViews` on `RunTicker`'s chain) |

## Public API

```csharp
// EnemyLook: `GameObject cover = null` appended; `public GameObject Cover { get; }`
namespace Soulvail.Game.Views
{
    public sealed class CoverViews : IDisposable
    {
        public int LiveCount { get; }
    }
}
```

## Behaviour

1. **Built before it is shown.** At construction, one instance per look with a `Cover`, under a
   deactivated root (AR §18.2), subscribed from the constructor (AR §18.1).
2. **It stands where its archetype spawns.** On `EnemySpawned` for a spec whose look has `Cover`, that
   cover moves to the spawn position (yaw 0) and activates.
3. **It goes with its archetype.** That enemy's `EnemyDespawned` deactivates it, and `StageArrived`
   deactivates any left standing.
4. **Four stones that block a sight line.** Each sits 5.5–6.5 m from the prefab's root, at 45°, 135°,
   225° and 315°; each is ≥ 1.6 m tall (over `LineOfSightSense.EyeHeight`'s 1.1) and ≥ 1.6 m wide; on
   `Cover`, with a collider and a carving `NavMeshObstacle`. A ray at `EyeHeight` from the root to 3 m
   behind a stone hits it.
5. **The room still plays.** Placed on `Arena_Clearing`'s middle spawn point (`Spawn_06`), no stone is
   within 2 m of a spawn point or `PlayerStart`, every stone lies on the floor, the nearest stone edge
   is ≥ 4 m from the root (a strafe lane round her), and adjacent stones are ≥ 4 m apart.
6. **Desaturated stone.** The rocks wear their Forest Nature material, no reserved colour (GD §16.4).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Rule |
|---|---|
| `Cover_BuiltInactive_UnderADeactivatedRoot` | 1 |
| `Cover_SpawnOfItsArchetype_StandsThere` / `Cover_OtherSpawns_PlaceNothing` | 2 |
| `Cover_Despawn_LiftsIt` / `Cover_StageArrived_LiftsAny` | 3 |
| `MotherStones_AreFourSightBlockers` (with the ray) | 4 |
| `MotherStones_InTheClearing_LeaveTheRoomPlayable` | 5 |
| `Prefab_IsDressed` (Traps §5) | 4 |
| `MotherStones_WearNoReservedColour` (each material's albedo, averaged) | 6 |

## Manual verification (Editor / device)

1. **[Editor]** At JG-12h, stage 10: four grey stones ring the plant, Frogs path round them, and the
   stones read as stone on the dark floor, not as a wall.

## Out of scope

- Stones on any other stage, or in the arena prefab itself (the Jungle keeps open floors).
- Stones that break, or a boss that moves them.

## As built

_Filled at merge._
