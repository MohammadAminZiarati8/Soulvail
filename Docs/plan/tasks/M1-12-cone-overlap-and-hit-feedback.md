# M1-12 — `ConeOverlapQuery`, `RunTicker` fact loop, hit flash, dissolve death

**Size:** M · **Depends on:** M1-07, M1-11 · **Branch:** `m1-12-cone-overlap-and-hit-feedback`
**Design refs:** AR §4.3 (step 5), §14; CC §4.2 (hit registration); GD §16.3 (feel checklist)

## Goal

The body answers the cone question with a real physics query and renders the consequences: enemies flash white when hit and dissolve upward when they die. Swinging is visible.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/ConeOverlapQuery.cs` | Game | Sphere overlap + angle test → enemy ids, allocation-free |
| `Game/Views/EnemyHitFeedback.cs` | Game | Flash on `EnemyDamaged`, dissolve on `EnemyDied` (component on the enemy prefab) |
| `Tests/Game/Adapters/ConeOverlapQueryTests.cs` | Tests.Game | The pure angle/range test |
| *small edits* | | `RunTicker.Tick` + step 5: for each `ConeHitIntent` in the buffer → query → `session.ReportConeHits(ids)`; `PlayerView` + swing visual on `PlayerAttacked`; `Enemy.prefab` + `EnemyHitFeedback`; `Player.prefab` + swing-cone quad child; `TagManager` (layer `Enemy` from M1-07 verified) |

## Public API

```csharp
namespace Soulvail.Game.Adapters;

public sealed class ConeOverlapQuery
{
    public ConeOverlapQuery(int capacity, LayerMask enemyLayer);
    /// Fills ids with unique enemy ids inside the cone. Returns count. Never allocates.
    public int Query(in ConeHitIntent cone, Span<int> ids);
    /// Pure: point inside the horizontal cone? Range and half-angle on XZ.
    public static bool IsInCone(Vector3 origin, Vector2 facingXZ, Vector3 point, float range, float angleDeg);
}
```

## Behaviour

1. `Query`: `Physics.OverlapSphereNonAlloc(origin, range, colliders, enemyLayer, QueryTriggerInteraction.Collide)` → for each collider, `EnemyView.FromCollider` (M1-07) → if `IsInCone(origin, facing, view.Position, range, angle)` and id not already added → append. Uses preallocated `Collider[capacity]`.
2. `IsInCone`: XZ distance ≤ range **and** angle between `facingXZ` and `(point − origin).XZ` ≤ `angleDeg / 2`. A point at the origin counts as inside (a dummy standing on the player).
3. `RunTicker.Tick` step 5 runs **after** views apply intents: for each cone intent → `Query` into a reusable `int[]` → `session.ReportConeHits(ids[..count])`. Reports are made even when count is 0 (consumes the pending request; core must not wait forever).
4. `EnemyHitFeedback` (subscribes in `OnEnable`, filters by its view's id): `EnemyDamaged` → set emission/albedo to white for 80 ms via `MaterialPropertyBlock` (no material instances); `EnemyDied` → disable the trigger collider, over 0.5 s scale Y ×1.4 and fade alpha to 0 (the "dissolve upward"); `EnemyDespawned` (from `EnemyViews`) removes the object as before.
5. `PlayerView` on `PlayerAttacked`: enable the swing-cone quad (60°, 8 m, cyan, 25 % alpha) for 0.1 s, oriented to the event's facing. Placeholder VFX; enough to *see* the cadence.
6. No per-swing allocation anywhere in this path.

## Tests

| Test | Given / When / Then |
|---|---|
| `IsInCone_Inside` | origin 0, facing +Z, point (0,0,5), range 8, 60° / — / true |
| `IsInCone_AtEdgeAngle` | point at 29.9° / true; at 30.1° / false |
| `IsInCone_BeyondRange` | (0,0,8.1) / — / false; (0,0,8.0) / true |
| `IsInCone_Behind` | (0,0,−3) / — / false |
| `IsInCone_IgnoresY` | (0,5,4) / — / true |
| `IsInCone_AtOrigin` | point == origin / — / true |
| `Query_ReturnsUniqueIds_InCone` | EditMode scene: 3 enemy views with trigger colliders (two in cone, one behind), `Physics.SyncTransforms()` / Query / count 2, the right ids |
| `Query_AllocatesNothing` | warm-up / 1 000 queries / allocated-bytes delta == 0 |

## Manual verification (Editor)

1. Stand near a dummy: the swing quad pulses 3×/s; each damage frame flashes the dummy white.
2. Three flashes → the dummy stretches up and fades over half a second, then disappears.
3. Two dummies overlapping in the cone both flash on one swing; one behind you never does.
4. Overlay: `enemies` count drops after each death.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Hit-stop, screen shake, damage numbers — M8-01.
- Health tint on enemies — M3-13.
- Proper VFX — M7.

## As built

_Filled at merge._
