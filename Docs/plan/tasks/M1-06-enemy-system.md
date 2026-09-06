# M1-06 — `EnemySystem`: spawn plan, snapshot ingestion, perception, enemy events

**Size:** M · **Depends on:** M1-05 · **Branch:** `m1-06-enemy-system`
**Design refs:** AR §3, §4.1–4.3, §9; ADR-0003

## Goal

Core owns enemies end to end: it decides they exist (from a spawn plan), learns where they are (from the snapshot), fills their perception (blackboards), and tells the body about spawns and despawns through events. Static dummies only — no behaviour yet.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/SpawnPlan.cs` | Core | Initial spawns for a run (data) |
| `Core/Ai/EnemySystem.cs` | Core | Registry owner; ingest, perceive, tick, publish |
| `Core/Events/EnemyEvents.cs` | Core | `EnemySpawned`, `EnemyDespawned` (grouped) |
| `Tests/Core/Ai/EnemySystemTests.cs` | Tests.Core | Every rule |
| *small edits* | | `RunConfig` + `SpawnPlan`; `RunSession` creates `EnemySystem`, calls `Ingest`/`Tick`; `RunState` + `Enemies`; `ContentCatalog` + `Enemies` list + `Enemy(id)` |

## Public API

```csharp
namespace Soulvail.Core.Run;

public sealed class SpawnPlan
{
    public readonly struct Entry { public readonly ContentId SpecId; public readonly Vector3 Position; }
    public IReadOnlyList<Entry> Initial { get; }
    public static SpawnPlan Empty { get; }
    public SpawnPlan(IReadOnlyList<Entry> initial);
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemySystem
{
    public EnemySystem(ContentCatalog catalog, IDomainEvents events, int capacity);

    public EnemyRegistry Registry { get; }

    public EnemyAgent Spawn(ContentId specId, Vector3 position);   // registers + publishes EnemySpawned
    public void SpawnAll(SpawnPlan plan);
    public bool Despawn(int id);                                    // unregisters + publishes EnemyDespawned

    /// Copies positions/velocities from the snapshot into agents by Id and fills perception on every blackboard.
    public void Ingest(WorldSnapshot snapshot);
    public void Tick(float dt, float now);                          // no-op for Static behaviours in this task
    public void Clear();
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct EnemySpawned   { public readonly int Id; public readonly ContentId SpecId; public readonly Vector3 Position; }
public readonly struct EnemyDespawned { public readonly int Id; }
```

## Behaviour

1. `Spawn` resolves `catalog.Enemy(specId)`, calls `Registry.Spawn`, then publishes `EnemySpawned`. Unknown spec → the catalog's `KeyNotFoundException`; nothing spawned.
2. `SpawnAll` spawns in plan order.
3. `Despawn` publishes `EnemyDespawned` **after** removing from the registry; unknown id → false, no event.
4. `Ingest`: for each `snapshot.Enemies[i]` with an `Id` known to the registry, set `agent.Position/Velocity`. Unknown ids are ignored (a view can lag a frame behind a despawn). Agents *absent* from the snapshot keep their last position — their views may not have reported yet.
5. Perception, per living agent, after positions are updated: `SelfPosition`, `SelfVelocity`, `PlayerPosition = snapshot.PlayerPosition`, `DistanceToPlayer` (XZ), `DirectionToPlayer` (unit XZ; zero if distance < 1e-4), `PathDirectionToPlayer` (copied from the sense, may be zero), `HasLineOfSight` (copied), `AlliesNearby` = count of other living agents within 6 m (O(n²) with n ≤ 64 — acceptable; revisit if profiling says so).
6. `RunSession.Tick` order becomes: `State.Time += dt` → `Enemies.Ingest(snapshot)` → `Enemies.Tick(dt, Time)` → player motor → intents. (Player combat slots in between in M1-08.)
7. `RunSession.Start` calls `SpawnAll(config.SpawnPlan)` **after** `RunStarted` is published, so view subscribers exist... no — views subscribe at scope build, before `Start`. Order: `RunStarted`, then spawns. Tests assert this order.
8. `RunSession.End` → `Enemies.Clear()` (no despawn events; the scope is going away).
9. `Ingest` and `Tick` allocate nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spawn_RegistersAndPublishes` | catalog{husk} / Spawn(husk, (2,0,3)) / `Registry.AliveCount 1`; `Single<EnemySpawned>()` id 1, spec husk, pos (2,0,3) |
| `Spawn_UnknownSpec_Throws_NoSpawn` | — / Spawn("x.y") / throws; alive 0; no events |
| `SpawnAll_InPlanOrder` | plan A, B, C / SpawnAll / ids 1,2,3 with matching specs; 3 events in order |
| `Despawn_PublishesAfterRemoval` | subscriber checks `AliveCount` inside handler / Despawn(1) / handler saw 0 |
| `Despawn_Unknown_NoEvent` | — / Despawn(9) / false, no events |
| `Ingest_CopiesPositionsById` | ids 1,2; snapshot has 2 then 1 / Ingest / positions match by id, not index |
| `Ingest_IgnoresUnknownIds` | snapshot has id 7 / Ingest / no throw |
| `Ingest_KeepsPositionWhenAbsent` | id 1 at (5,0,5); snapshot omits it / Ingest / still (5,0,5) |
| `Perception_DistanceAndDirection` | player (0,0,0), enemy (3,0,4) / Ingest / Distance 5, Direction (0.6, 0.8) |
| `Perception_DirectionZeroWhenCoincident` | enemy at player / — / Direction zero |
| `Perception_PathDirectionCopied` | sense path (1,0) / — / blackboard path (1,0) |
| `Perception_AlliesNearby` | three enemies: two within 6 m of each other, one far / — / counts 1, 1, 0 |
| `RunSession_Start_PublishesRunStartedBeforeSpawns` | config with plan / Start / events order [RunStarted, EnemySpawned…] |
| `RunSession_End_ClearsEnemies_NoDespawnEvents` | spawned / End / `AliveCount 0`, no `EnemyDespawned` |
| `IngestAndTick_AllocateNothing` | 32 enemies, warm-up / 10 000 × (Ingest, Tick) / allocated-bytes delta == 0 |
| `Catalog_Enemy_LookupAndDuplicates` | — / — / mirrors the character tests for `Enemy(id)` |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Behaviours (`Tick` is a no-op for `Static`) — M1-18.
- Respawn policy, threat budget — M1-19, M2-03.
- Damage and death events — M1-11.

## As built

_Filled at merge._
