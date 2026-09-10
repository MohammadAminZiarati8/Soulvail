# M1-19 — `ViewPool`, `RespawnPolicy` (keep 12 alive), NavMesh bake + path sense

**Size:** M · **Depends on:** M1-12, M1-18 · **Branch:** `m1-19-pooling-respawn-navmesh`
**Design refs:** AR §3 (pathfinding is a sense), §5.5, §14; GD §12.4 (spawn safety), §7.3; ADR-0011 (first use of a random stream)

## Goal

Enemies keep coming without allocating, they walk *around* pillars instead of into them, and the arena never spawns something on top of you — the last pieces before the feel test.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Pooling/ViewPool.cs` | Game | Generic pool over `IObjectResolver.Instantiate`; `IPoolable` |
| `Core/Run/RespawnPolicy.cs` | Core | Keep N alive, delay, spawn safety, position choice via `Spawn` stream |
| `Game/Adapters/NavPathSense.cs` | Game | Per-enemy NavMesh path direction, round-robin at 10 Hz |
| `Tests/Core/Run/RespawnPolicyTests.cs` | Tests.Core | Policy rules with `FixedRandom` |
| `Tests/Game/Pooling/ViewPoolTests.cs` | Tests.Game | Reuse and callbacks |
| *small edits* | | `SpawnPlan` + `Respawn` (policy data); `EnemySystem` takes `IRandom`, applies the policy in `Tick`; `EnemyViews` uses `ViewPool` (Instantiate/Destroy stopgap removed); `SnapshotBuilder` fills `PathDirectionToPlayer` from `NavPathSense`; `GreyBox.prefab` + `NavMeshSurface` (baked, agent radius 0.4); `Soulvail.Game.asmdef` + `Unity.AI.Navigation`; `RunScope` plan → keep 12, delay 2 s |

## Public API

```csharp
namespace Soulvail.Core.Run;

public sealed class RespawnPolicy
{
    public int   KeepAlive      { get; }   // 12
    public float RespawnDelay   { get; }   // 2 s after the last death
    public float MinPlayerDistance { get; } // 6 m (GD §12.4 spawn safety)
    public IReadOnlyList<Vector3> Positions { get; }
    public ContentId SpecId { get; }
}

// EnemySystem (added)
public void ApplyRespawn(RespawnPolicy policy, Vector3 playerPosition, float now, IRandomStream spawnStream);
```

```csharp
namespace Soulvail.Game.Pooling;

public interface IPoolable { void OnSpawn(); void OnDespawn(); }

public sealed class ViewPool<T> : IDisposable where T : Component, IPoolable
{
    public ViewPool(IObjectResolver resolver, T prefab, Transform parent, int prewarm);
    public T   Get();
    public void Release(T instance);
    public int CountActive { get; }
    public int CountInactive { get; }
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class NavPathSense
{
    public NavPathSense(int capacity, float refreshHz = 10f);
    /// Returns the cached unit XZ direction along the path from `from` toward `to`; zero if no path yet.
    public Vector2 DirectionFor(int enemyId, Vector3 from, Vector3 to, float time);
}
```

## Behaviour

**RespawnPolicy** (applied in `EnemySystem.Tick` after behaviours)
1. When `AliveCount (living only) < KeepAlive` and `now − lastDeathAt >= RespawnDelay` (or no death yet) → spawn **one** enemy of `SpecId` this tick (one per tick, so a wipe refills over a few frames, not instantly).
2. Position: draw `spawnStream.NextInt(0, Positions.Count)`; if that position is within `MinPlayerDistance` of the player, try the next index (wrapping) up to `Count` times; if none qualifies, skip this tick. Deterministic given the stream.
3. Uses `random.Spawn` only — never another stream (ADR-0011).

**ViewPool**
4. `Get` returns an inactive instance or instantiates through the resolver (so `[Inject]` runs once, on creation); calls `OnSpawn`, activates. `Release` calls `OnDespawn`, deactivates, returns to the pool. `prewarm` instances are created at construction (the run's loading moment, not mid-wave).
5. `EnemyView` implements `IPoolable`: `OnDespawn` resets scale/alpha/collider from a dissolve, unbinds. `EnemyViews` releases on `EnemyDespawned` instead of destroying.

**NavPathSense**
6. Each call returns the cached direction for `enemyId`. Refresh runs round-robin: at most `ceil(capacity / (refreshHz × 60))`… simpler: each enemy's path is recomputed when `time − lastComputed >= 1 / refreshHz`, and at most **4 enemies per frame** recompute (so 40 enemies refresh in 10 frames ≈ the 0.1 s cadence). `NavMesh.CalculatePath` with a preallocated `NavMeshPath`; direction = `normalize(corners[1] − corners[0]).XZ`; if `PathPartial/Invalid` or fewer than 2 corners → straight-line direction.
7. `SnapshotBuilder` writes it into `EnemySense.PathDirectionToPlayer`. `ChaserBehaviour` already prefers it (M1-18 rule 2).
8. No allocation in `Get`/`Release` after prewarm, nor in `DirectionFor` (path object reused; corners read via `GetCornersNonAlloc`).

## Tests

| Test | Given / When / Then |
|---|---|
| `Respawn_WhenBelowKeepAlive_AfterDelay` | 11 alive, last death at 0 / ApplyRespawn at 1.9 / none; at 2.1 / one spawned |
| `Respawn_OnePerTick` | 5 alive, delay elapsed / ApplyRespawn ×3 / 8 alive |
| `Respawn_SkipsPositionsNearPlayer` | positions [near, far], FixedRandom picks 0 / — / spawned at far |
| `Respawn_NoSafePosition_Skips` | all positions near / — / nothing spawned |
| `Respawn_UsesSpawnStream` | FixedRandom.SetStream("Spawn", 0.99), others 0 / — / picked last index |
| `Respawn_NotAboveKeepAlive` | 12 alive / — / nothing |
| `Pool_ReusesReleasedInstance` | Get, Release, Get / — / same instance; `CountInactive` 0 after |
| `Pool_CallsSpawnAndDespawn` | — / Get, Release / `OnSpawn` then `OnDespawn` once each |
| `Pool_PrewarmCreatesInactive` | prewarm 5 / — / `CountInactive 5`, all inactive |

`NavPathSense` is exercised on device (NavMesh needs the baked arena); its straight-line fallback is covered by `ChaserBehaviourTests`.

## Manual verification (Editor, then device)

1. Kill dummies steadily: after a 2 s lull they trickle back in, never closer than 6 m, never on top of you.
2. Put a pillar between you and a chaser — it walks around it.
3. Profiler: zero `Instantiate` calls after the first second; no GC.Alloc spikes from spawning or pathing.
4. Overlay: `enemies` hovers at 12.

## Acceptance

- [x] All tests green — 440 EditMode (428 + 12 new), 3 PlayMode
- [x] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified; Profiler shows no per-spawn allocation — **owner's, steps 1, 3 and 4**. Step 2 (a chaser walks around a pillar) is verified in the Editor: a path from (13, 0, 13) to the origin comes back `PathComplete` with 4 corners, turning at (7.6, 0, 10.0) — the north face of `Pillar_NE` plus the 0.4 m agent radius
- [x] `PROGRESS.md` entry appended (stopgap from M1-07 closed); Current State updated; ROADMAP box ticked

## Out of scope

- Threat budget / waves / director — M2. This policy is a stand-in for the feel test and is replaced, not extended.
- Line of sight — still skipped (CC §3.1).

## As built

Built as specified, with five differences worth naming.

- **`FixedRandom.SetSpawn`, not `SetStream("Spawn", …)`.** The test table's spelling never existed; M0-04's fake gives each stream its own setter. `Respawn_UsesSpawnStream` scripts the shared stream with `0f` and `Spawn` with `0.99f`, so a draw from the wrong stream lands on the first position instead of the last and the row fails for the right reason.
- **`EnemySystem` gained three pieces of state, not one.** The policy itself (adopted by `SpawnAll` from `SpawnPlan.Respawn`), `_lastDeathAt` (stamped in `ApplyDamage` beside `agent.DiedAt`), and `_playerPosition` (recorded in `Ingest`). The third is the one the spec did not anticipate: `Tick` is handed a `PlayerCombat`, which owns health and targeting and deliberately not a position, so the spawn-safety check had nowhere else to read it from. `ApplyRespawn` still takes the position as a parameter, which is what lets a test state the geometry instead of building a snapshot to imply it. All three are cleared in `Clear`.
- **`NavPathSense` answers with the straight line rather than zero.** Rule 6 says so for a partial or invalid path; the same answer is now given before an enemy's first refresh, where the API sketch said zero. The two are indistinguishable downstream — `ChaserBehaviour` falls back to the straight line on a zero — but it keeps the sense honest: what it returns is always a direction towards the player, never an absence. The per-frame budget resets when the `time` argument changes, so there is no begin-frame call for a caller to forget.
- **`refreshHz` is passed explicitly in `RunScope`.** VContainer resolves every constructor parameter from the container or a `WithParameter` and never falls back to a C# default value, so the default in the signature would have failed to compose the run. Hence `NavPathSense.DefaultRefreshHz`.
- **`EnemyView.OnDespawn` reaches into `EnemyHitFeedback.ResetVisuals`.** Rule 5 asks the view to reset "scale/alpha/collider from a dissolve", and all three belong to the feedback component. The alternative — the census resetting each component in turn — spreads "what a rental has to forget" across two files, and the file that would not have it is the one holding the pooled object.

**Prewarm is `max(dressed, keepAlive) + 1`,** 13 for the Run scene. The spare covers the overlap every kill has: a corpse holds its body for `CorpseTime`'s 0.6 s while its replacement is already being rented, and without it a steady fight would instantiate once per death.

**Two things outside the Files table.** `ProjectSettings/NavMeshAreas.asset` — the Humanoid agent's radius, 0.5 → 0.4, which is where agent radius lives and the only way to bake at the number this spec asks for; approved by the owner before the edit. And `Assets/_Project/Prefabs/Arenas/NavMeshGreyBox.asset`, the baked data, which a bake necessarily produces.
