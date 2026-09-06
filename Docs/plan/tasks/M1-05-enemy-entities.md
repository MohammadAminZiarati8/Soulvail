# M1-05 — `EnemySpec`, `EnemyAgent`, `EnemyBlackboard`, `EnemyRegistry`

**Size:** M · **Depends on:** M1-02 · **Branch:** `m1-05-enemy-entities`
**Design refs:** AR §3, §5 (`Ai`), §9; ADR-0005; GD §8.1 (Husk numbers)

## Goal

Enemies exist in core as first-class entities — spec, health, blackboard, identity — owned by a registry that hands out stable ids and iterates the living, so the director, targeting, and AI all have one place to look.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/EnemySpec.cs` | Core | Immutable enemy definition |
| `Core/Ai/EnemyBlackboard.cs` | Core | Per-agent perception + working memory |
| `Core/Ai/EnemyAgent.cs` | Core | One living enemy |
| `Core/Ai/EnemyRegistry.cs` | Core | Ids, spawn/despawn, iteration, lookup |
| `Tests/Core/Ai/EnemyRegistryTests.cs` | Tests.Core | Registry + agent construction (blackboard is data) |

## Public API

```csharp
namespace Soulvail.Core.Content;

public enum EnemyBehaviourKind { Static, Chaser }

public sealed class EnemySpec
{
    public ContentId Id             { get; }
    public LocKey    NameKey        { get; }
    public float     MaxHp          { get; }   // Husk 36
    public float     MoveSpeed      { get; }   // 3.5
    public int       TargetPriority { get; }   // 1
    public bool      IsElite        { get; }
    public float     ContactDamage  { get; }   // 8
    public float     Reach          { get; }   // 1.2 m
    public float     WindupTime     { get; }   // 0.4 s
    public float     RecoverTime    { get; }   // 0.6 s
    public EnemyBehaviourKind Behaviour { get; }
    // constructor validates: hp > 0, speed >= 0, priority 1..8, reach > 0, times >= 0
}
```

```csharp
namespace Soulvail.Core.Ai;
using System.Numerics;

public sealed class EnemyBlackboard
{
    // Perception — written by EnemySystem ingestion each tick
    public Vector3 SelfPosition;
    public Vector3 SelfVelocity;
    public Vector3 PlayerPosition;
    public float   DistanceToPlayer;
    public Vector2 DirectionToPlayer;        // unit XZ, straight line
    public Vector2 PathDirectionToPlayer;    // unit XZ along NavMesh; zero if unknown → behaviours fall back to DirectionToPlayer
    public bool    HasLineOfSight;
    public int     AlliesNearby;             // within 6 m

    // Working memory — written by the agent's own behaviour
    public float   StateTimer;
    public Vector2 LungeDirection;
    public float   NextAttackAt;

    public void Reset();
}

public sealed class EnemyAgent
{
    public int            Id        { get; }
    public EnemySpec      Spec      { get; }
    public Health         Health    { get; }        // maxHp Stat from spec, no shield, 0 i-frames
    public EnemyBlackboard Blackboard { get; }
    public Vector3        Position  { get; internal set; }   // last ingested
    public Vector3        Velocity  { get; internal set; }
    public bool           IsAlive   => !Health.IsDead;
    public bool           IsVulnerable { get; internal set; } = true;   // Warden facing rule lands in M7-01
    internal EnemyAgent(int id, EnemySpec spec, Vector3 position);
}

public sealed class EnemyRegistry
{
    public EnemyRegistry(int capacity);                       // matches snapshot capacity

    public int  AliveCount { get; }
    public int  Capacity   { get; }
    public EnemyAgent Spawn(EnemySpec spec, Vector3 position);   // InvalidOperationException when full
    public bool Despawn(int id);                                 // false if unknown
    public bool TryGet(int id, out EnemyAgent agent);
    public ReadOnlySpan<EnemyAgent> Alive { get; }               // stable order: spawn order; compacted on despawn
    public void Clear();
}
```

## Behaviour

1. Ids start at 1 and increase monotonically for the life of the registry; ids are never reused within a run (`Clear` resets to 1 — used only between runs).
2. `Spawn` when `AliveCount == Capacity` throws `InvalidOperationException` — the caller (spawn policy) checks `AliveCount` first.
3. `Despawn` removes the agent from `Alive` (swap-with-last is **not** allowed — order must stay spawn order for determinism; use a list `RemoveAt`, which is O(n) with n ≤ 64).
4. `Alive` exposes only living, registered agents. A dead-but-not-despawned agent (`Health.IsDead`) is still in `Alive` until `EnemySystem` despawns it after publishing `EnemyDied` (M1-11) — readers must check `IsAlive` where it matters.
5. `TryGet` is O(1) (dictionary or dense array by id).
6. `EnemyAgent.Health` is `new Health(new Stat(spec.MaxHp), null, 0)`.
7. `EnemyBlackboard.Reset` zeroes everything.
8. No allocation in `Spawn` after the first `capacity` spawns (agent objects are recycled: despawned agents go to a free list and are re-initialised with a new id on the next spawn).

## Tests

| Test | Given / When / Then |
|---|---|
| `Spawn_AssignsIncreasingIds` | registry(8) / Spawn ×3 / ids 1, 2, 3 |
| `Spawn_WhenFull_Throws` | registry(2), two spawned / Spawn / `InvalidOperationException` |
| `Despawn_RemovesFromAlive_KeepsOrder` | ids 1,2,3 / Despawn(2) / Alive ids [1, 3], `AliveCount 2` |
| `Despawn_Unknown_ReturnsFalse` | — / Despawn(99) / false |
| `Ids_NeverReusedWithinRun` | spawn 1, despawn 1 / Spawn / id 2 |
| `TryGet_FindsAlive_NotDespawned` | spawned 1 / TryGet(1) true; Despawn, TryGet(1) false |
| `Agent_HasHealthFromSpec` | Husk spec / Spawn / `Health.MaxHp.Value == 36`, `Current == 36`, no shield |
| `Agent_DefaultsVulnerable` | — / Spawn / `IsVulnerable` |
| `Clear_ResetsIdsAndAlive` | spawned / Clear / `AliveCount 0`; next id 1 |
| `Spawn_AfterWarmup_AllocatesNothing` | registry(64), spawn/despawn 64 once / 10 000 × (Spawn, Despawn) / allocated-bytes delta == 0 |
| `Spec_Validation` | priority 0 / 9, hp 0, reach 0 / new / `ArgumentOutOfRangeException` |
| `Blackboard_Reset_Zeroes` | filled / Reset / all default |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Ticking behaviours, spawning from a plan, ingesting the snapshot — M1-06.
- `TagSet` on specs — first needed by affixes (M7-02); added then.
- `EnemyDefinition` ScriptableObject — M1-07.

## As built

_Filled at merge._
