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

Five files as tabled, no sixth. **249 EditMode tests pass in 1.19 s**, up from 232; zero errors, zero new warnings, all six assemblies rebuilt from the source on disk.

**The two places the spec had to be resolved rather than followed:**

- **Rule 4 contradicts itself**: `Alive` holds "only living, registered agents" and then a dead-but-not-despawned agent "is still in `Alive`". The second is what M1-11's death flow needs — the event must be out, and the view must have its frame to start dissolving, before the id stops resolving — so `Alive` and `AliveCount` mean **registered**, and a reader that cares asks `IsAlive`. `Alive_KeepsDeadAgentUntilDespawned` is a row for the clause the table left unpinned.
- **Rules 1 and 8 read as a conflict and are not one.** A new id on a reused object satisfies both. But it makes the Public API block incomplete: `Id` and `Spec` need setters, because a recycled agent may return as a different archetype. Added as `{ get; private set; }` plus one `internal void Initialise(int, EnemySpec, Vector3)` — the single place a spawned agent's state is established, whether it was just constructed or pulled off the free list, so the two paths cannot drift.

**The order inside `Initialise` is load-bearing:** `Health.MaxHp.Base = spec.MaxHp` before `Health.Reset()`, because `Reset` refills `Current` from `MaxHp.Value`. Reversed, a recycled Husk arrives at the previous archetype's hit points. `Spawn_RecycledAgent_IsFullyReinitialised` pins it with a 36 HP agent hurt to 10, despawned, and respawned as a 90 HP one.

**`Health` is reused, not rebuilt**, and not only for the allocation budget: its constructor subscribes to `Stat.Changed` and never unsubscribes, so a fresh one per spawn would allocate *and* leave the old one wired to a stat nobody owns.

**One hazard that could not be closed here.** `Stat` removes modifiers by source reference only — there is no "drop everything" — so a recycled agent inherits any modifier a previous life left on its `MaxHp`. Nothing applies one today, so the reuse is sound as built; depth scaling (M2-03) and Elite affixes (M7-02) are the first that will, and each owes either a removal at despawn or a `Stat` API to clear them. Deferred to the watch list on purpose rather than pre-solved: an API with no caller is one no test can honestly exercise.

**Beyond the Public API block:** the `Initialise` method above, and `EnemySpec` guards `contactDamage` too — the spec's validation list names hp, speed, priority, reach and the times, but leaving one number unguarded among six would let a NaN reach `Health.ApplyDamage`, where `!(amount > 0f)` makes it a silent no-op.

**Beyond the Tests table:** five rows — `Alive_KeepsDeadAgentUntilDespawned` and `Spawn_RecycledAgent_IsFullyReinitialised` above, `Agent_CarriesSpecAndPosition`, and the two guards on the registry's public surface (`Constructor_RejectsNonPositiveCapacity`, `Spawn_RejectsNullSpec`). The internal constructor and `Initialise` carry no guards, per M0-10: `Soulvail.Tests.Core` has no `InternalsVisibleTo`, so a guard there is unreachable from any test that could prove it works.

**One assertion that is deliberately vacuous today:** the recycling row checks `IsVulnerable` is back to `true`, but nothing outside core can lower it, so no test can yet create the state whose reset it checks. The Warden's facing rule (M7-01) is the task that owes that row its teeth.

**Deliberately absent:** GD §8.1's threat cost, which is the director's currency (M2-03, M2-04) rather than the enemy's own property; and `TagSet`, until affixes need it (M7-02).
