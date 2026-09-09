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

- [x] All tests green — 362 EditMode, 11 of them this task's (confirmed by a filtered re-run)
- [x] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified — **owner's, below**
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Hit-stop, screen shake, damage numbers — M8-01.
- Health tint on enemies — M3-13.
- Proper VFX — M7.

## As built

**Files:** three new (`ConeOverlapQuery`, `EnemyHitFeedback`, `ConeOverlapQueryTests`), one new asset (`Materials/M_BoneGrey_Dissolve.mat`), and small edits to `RunTicker`, `RunScope`, `PlayerView`, `EnemyView`, `EnemyViews`, `Enemy.prefab`, `Player.prefab` and `Run.unity`. `TagManager` needed nothing — M1-07's `Enemy` layer (index 8) was already there and was verified rather than added.

**Five deviations, two of them defects in this spec.** The full account is in the PROGRESS entry; in short:

1. **`EnemyView.FromCollider` (rule 1) does not exist and should not.** M1-07 shipped `EnemyViews.TryGetId(Collider, out int)` instead, because a static dictionary is banned mutable static state and — with domain reload disabled on Play — would carry one session's colliders into the next silently. `ConeOverlapQuery`'s constructor therefore takes a third argument, `EnemyViews`, where the Public API block above shows two.
2. **The Public API block is in `UnityEngine` vectors; `ConeHitIntent` is `System.Numerics`.** Converted once per swing through `Num`; `IsInCone` is `UnityEngine`-only.
3. **Rule 4's `OnEnable` subscription is impossible.** `IObjectResolver.Instantiate` runs `Awake`/`OnEnable` *during* the instantiate, before VContainer injects, so the hub would be null on every enemy. Subscribed from `[Inject] Construct`, with `Start` checking that injection happened.
4. **Rule 4's alpha fade needed a material that did not exist.** `M_BoneGrey` is opaque URP/Lit, so a property-block alpha on it is a silent no-op, and it is shared with `GreyBox.prefab` — flipping it would have made the arena see-through. Owner chose a transparent copy, swapped in by `sharedMaterial` only while a corpse fades.
5. **Rule 5's "quad" is a generated wedge mesh**, built in `Awake` from the serialized range and angle so it cannot disagree with them, reusing `M_Reticle.mat`.

**Beyond the table:** `EnemyView.Body` resolves lazily when `Awake` has not run (without it the collider index is empty in EditMode and the query would assert zero — a passing test of a broken feature); `RunScope` gained a serialized `_enemyLayer` and the query's registration; `PlayerView` gained injection, an `Update` and the wedge. Three test rows beyond the Tests table.

**Placeholder to keep honest:** `PlayerView`'s swing range and angle duplicate the Censer's `WeaponSpec` numbers and must be kept equal by hand until M7. `PlayerAttacked` carries only the facing on purpose — the tell starts before the cone that answers it exists.
