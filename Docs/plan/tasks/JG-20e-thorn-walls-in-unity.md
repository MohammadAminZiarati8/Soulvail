# JG-20e — Thorn walls in Unity: a warned strip, a rising wall that blocks only you

**Size:** M · **Depends on:** JG-20a · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 7; AR §18.2 (core holds no walls; build-now-show-later under an inactive root); colour law (`Palette.Danger` for warnings, never cyan)

## Goal

What `ThornSystem` decides, made physical: the warning strip, the wall rising out of the floor, a
collider that stops the player and their roll but lets enemies through, and the wither.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ThornWallViews.cs` | Game | **New.** Subscribes to the three thorn events; rents walls |
| `Game/Views/ThornWallView.cs` | Game | **New.** `MonoBehaviour`, `IPoolable`: strip, rise, collider, wither |
| `Prefabs/Vfx/FX_ThornWall.prefab` | — | **New.** Strip quad, bramble mesh, `BoxCollider` |
| `Tests/Game/Views/ThornWallViewsTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunScope`: `[SerializeField] _thornWallPrefab`, registration and dispose; `RunScope` prefab wiring |

## Public API

```csharp
namespace Soulvail.Game.Views {
  public sealed class ThornWallViews : IDisposable {
    public ThornWallViews(GameObject wallPrefab, DomainEventHub hub, Transform parent, PlayerView player);
    public int LiveCount { get; } }
  public sealed class ThornWallView : MonoBehaviour, IPoolable {
    public bool IsSolid { get; } } }
```

## Behaviour

1. **Pool:** `ThornSystem.Capacity` walls built under an inactive root at construction (AR §18.2);
   a 13th rent is refused and logged once.
2. **Armed:** the wall is placed and sized to the event's exact rectangle; the strip fills in
   `Palette.Danger` over `ArmSeconds`; no collider yet.
3. **Rose:** the bramble rises over 0.15 s; the collider turns on the first frame the player's
   `CharacterController` bounds are clear of the box, so a bitten player is never trapped inside.
4. **Collider:** layer `Cover`, `excludeLayers` = `Enemy`: the player and `ChargeMotion` stop at it;
   enemies walk through; `LineOfSightSense` sees it as cover. No `NavMesh` change.
5. **Withered:** collider off at once, the bramble sinks over 0.3 s, then returns to the pool.
6. **StageArrived** returns every wall. Nothing allocates per frame.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pool_PrewarmedInactive_ThirteenthRefused` (1) |
| `Armed_PlacedAndSized_StripDanger_NoCollider` (2) |
| `Rose_PlayerClear_Solid` / `Rose_PlayerInside_SolidOnceClear` (3) |
| `Collider_CoverLayer_ExcludesEnemy` (4) |
| `Withered_ColliderOff_ReturnsToPool` (5) |
| `StageArrived_ReturnsAll` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Played in JG-20h (nothing opens a wall before it): the strip warns, the wall rises,
   the Ranger and the roll stop at it, a Rootling walks through.

## Out of scope

- Thornheart's body, pods and ring (20f–g).

## As built

_Filled at merge._
