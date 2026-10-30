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

Built 2026-10-30 on `dev`.

- `CoverViews` builds one instance per authored look beneath an inactive holding root; constructor
  subscriptions place it at the owner's spawn (identity rotation), lift it on that owner's despawn,
  and sweep it on arrival. Dispose unsubscribes and destroys the instances. No per-frame work.
- `EnemyLook.Cover` is optional and last; `EnemyDefinition._cover` carries it. `RunScope` registers
  the listener and resolves it through RunTicker's existing `bossViews` parameter factory, before
  resolving `BossViews`, keeping all implementation edits inside the Files table.
- Prefab made through the Unity Editor MCP with four nested KayKit Forest Nature
  `Rock_1_A_Color1.fbx` instances at scale 3.4, radius 6 m and the four specified diagonal angles.
  Each stone is approximately 1.97 × 1.84 × 2.07 m, grounded, with a matching box collider and
  carving box obstacle. Models and colliders use `Cover`; the imported Forest Nature material
  remains assigned. Tests pin the eye-height ray and actual `LineOfSightSense` answer, floor,
  edge clearances, nested model/material links and averaged sampled albedo per material.
- **Scope exception:** `Data/Enemies/Husk.asset` gains only `_cover: {fileID: 0}`. The existing
  `EnemyDefinitionTests.Husk_EveryYamlKeyBindsToAField` requires serialized text to survive an Editor
  reserialize unchanged; adding the field otherwise fails it. Existing code/test wins over Files.
- **Rule resolution:** Tests includes rule 6 although the Files purpose says “Rules 1–5”; Tests
  wins, so `MotherStones_WearNoReservedColour` is included. Nine named tests map to the Tests table.
- **Verified:** JG-12g 9/9. Final full EditMode: 4 653 passed, 0 failed (failing names: none),
  1 known animator-clock inconclusive, 4 654 total. Project compiler errors 0; new analyzer warnings
  0 (both affected assemblies also compiled against Unity's response files with its analyzers).
  `dotnet format whitespace --folder --verify-no-changes --include` checked all five changed C#
  files: clean. The initial full run failed only the Husk serialization row described above;
  retaining its null cover field made the full rerun green.
- **Manual steps:** no prefab creation remains. At JG-12h assign `Cover_MotherStones.prefab` to
  MotherPitcher's Cover field (that task owns the asset), then verify stage 10 in the Editor:
  four grey stones ring her, Frogs path around them, and the stones read clearly on the dark floor.
  That live fight/device check remains deferred until JG-12h.
