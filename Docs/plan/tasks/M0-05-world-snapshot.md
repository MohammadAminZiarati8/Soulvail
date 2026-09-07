# M0-05 — `WorldSnapshot`, `EnemySense`, `Num` vector conversions

**Size:** M · **Depends on:** M0-01 · **Branch:** `m0-05-world-snapshot`
**Design refs:** AR §4.2, ADR-0003

## Goal

The only thing core receives about "where things are" has a shape: a preallocated, allocation-free snapshot, plus the conversion between Unity vectors and the `System.Numerics` vectors core uses.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/WorldSnapshot.cs` | Core | Reused per-frame container |
| `Core/Run/EnemySense.cs` | Core | Per-enemy spatial facts |
| `Game/Adapters/Num.cs` | Game | `UnityEngine` ↔ `System.Numerics` extension conversions |
| `Tests/Core/Run/WorldSnapshotTests.cs` | Tests.Core | Capacity, Clear, no-alloc |
| `Tests/Game/Adapters/NumTests.cs` | Tests.Game | Round-trip conversions |

## Public API

```csharp
namespace Soulvail.Core.Run;
using System.Numerics;

public sealed class WorldSnapshot
{
    public WorldSnapshot(int enemyCapacity);       // > 0

    public float   Dt;
    public Vector2 MoveInput;                      // already shaped by the input layer; |v| <= 1
    public Vector3 PlayerPosition;
    public Vector3 PlayerVelocity;

    public int EnemyCount;                          // valid entries in Enemies[0..EnemyCount)
    public readonly EnemySense[] Enemies;           // length == enemyCapacity, never reallocated
    public int EnemyCapacity { get; }

    public ref EnemySense AddEnemy();               // EnemyCount++, returns the slot; throws if full
    public void Clear();                            // Dt = 0, inputs/positions zero, EnemyCount = 0
}

public struct EnemySense
{
    public int     Id;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector2 PathDirectionToPlayer;           // unit XZ direction along the NavMesh path; zero if none
    public bool    HasLineOfSight;
}
```

```csharp
namespace Soulvail.Game.Adapters;

public static class Num
{
    public static System.Numerics.Vector3 ToNum(this UnityEngine.Vector3 v);
    public static UnityEngine.Vector3     ToUnity(this System.Numerics.Vector3 v);
    public static System.Numerics.Vector2 ToNum(this UnityEngine.Vector2 v);
    public static UnityEngine.Vector2     ToUnity(this System.Numerics.Vector2 v);
    public static System.Numerics.Vector2 ToNumXZ(this UnityEngine.Vector3 v);   // (x, z) — ground-plane direction
}
```

## Behaviour

1. `Enemies` is allocated once in the constructor with length `enemyCapacity` and never replaced.
2. `AddEnemy()` returns a `ref` to slot `EnemyCount` and increments `EnemyCount`. When `EnemyCount == EnemyCapacity` it throws `InvalidOperationException` — the caller (SnapshotBuilder) must respect the device-tier cap.
3. `Clear()` zeroes `Dt`, `MoveInput`, `PlayerPosition`, `PlayerVelocity`, and sets `EnemyCount = 0`. It does **not** zero the `Enemies` array contents (stale data past `EnemyCount` is by design; readers never look past it).
4. `Clear()` and `AddEnemy()` allocate nothing.
5. Conversions are component-wise; `ToNumXZ` drops Y.
6. `enemyCapacity <= 0` throws `ArgumentOutOfRangeException`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Ctor_AllocatesArrayOfCapacity` | new(32) / — / `Enemies.Length == 32`, `EnemyCapacity == 32`, `EnemyCount == 0` |
| `Ctor_ZeroCapacity_Throws` | new(0) / — / `ArgumentOutOfRangeException` |
| `AddEnemy_ReturnsSlotAndIncrements` | new(4) / `ref var e = ref AddEnemy(); e.Id = 7` / `EnemyCount == 1`, `Enemies[0].Id == 7` |
| `AddEnemy_WhenFull_Throws` | new(1), one AddEnemy / AddEnemy / `InvalidOperationException` |
| `Clear_ResetsScalarsAndCount_KeepsArrayInstance` | filled snapshot / Clear / Dt 0, MoveInput zero, EnemyCount 0, same `Enemies` reference |
| `ClearAndAdd_AllocateNothing` | warm-up / 10 000 × (Clear, 8 × AddEnemy) / allocated-bytes delta == 0 |
| `Num_Vector3_RoundTrips` | (1.5, -2, 3) / ToNum().ToUnity() / equal |
| `Num_Vector2_RoundTrips` | (0.25, -1) / ToNum().ToUnity() / equal |
| `Num_ToNumXZ_DropsY` | (1, 99, 2) / ToNumXZ / (1, 2) |

## Acceptance

- [x] All tests green — 51 passed, 0 failed, via `TestRunnerApi`
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Filling the snapshot from a scene — M0-16 `SnapshotBuilder`.
- Any enemy fields beyond spatial facts (priority, elite, vulnerable, hp) — M1-06 extends `EnemySense`.
- The device-tier capacity value — a constant `64` in M0-12; a `TuningConfig` field later.

## As built

**Files:** 5, exactly the table. No new asmdef, so no new `csc.rsp`. `Docs/Architecture.md` was also edited — see below; a doc, so it does not count against size M.

**`System.Numerics` needed no reference.** It ships in the .NET Standard profile the project targets (`apiCompatibilityLevel: 6`) and is not a `UnityEngine*` assembly, so `Soulvail.Core.asmdef` is unchanged and `AssemblyPurityTests` stays green. First core dependency beyond `System` and `System.Collections.Generic`.

**AR §4.2 was stale and is corrected in this change** (owner's direction). It sketched `WorldSnapshot` as a struct with no `AddEnemy`/`Clear`/`EnemyCapacity`; this spec's Public API says `sealed class`, and it has to be — `AddEnemy` returns `ref EnemySense` and advances `EnemyCount`, which a by-value copy would throw away. Not a deviation from the spec, and no superseding ADR: ADR-0003 decides that the snapshot is the tick's payload, not its storage kind, so the decision stood and only its illustration was wrong. §4.2 now carries the built shape plus the two rules the sketch could not express — the array outlives every frame, and every reader stops at `EnemyCount` — and §12's adapters list gained `Num`.

**Also:** `ClearAndAdd_AllocateNothing` is measured with `AllocationAssert` — the row's "allocated-bytes delta == 0" describes the BCL probe M0-02 proved inert on this runtime. Two rows carry one extra assertion each, covering behaviour the row already owns rather than growing the table: `Ctor_ZeroCapacity_Throws` also asserts a negative capacity (rule 6 is `<= 0`, the row names only 0), and `Clear_ResetsScalarsAndCount_KeepsArrayInstance` also asserts a stale slot survives (rule 3's "does **not** zero the contents" had no row).

**The ref-return trap.** `var e = snapshot.AddEnemy()` binds a copy and compiles; every assertion about `e` then passes while the snapshot stays empty. `ref` is needed on both sides, and the tests assert through `snapshot.Enemies[…]` rather than through the local so that a copy fails them. M0-06's intent buffer is the next place this applies.

**Public mutable fields are deliberate** on both types, against the project's general ban, which targets Unity components. One writer (`SnapshotBuilder`), one reader (core); properties would cost a call per field per enemy per frame. Documented in the type remarks. No analyzer objects.

**Verified:** 51 EditMode tests green (42 before, 9 new), zero errors, zero warnings in the Unity Console.

**Carry forward:** `Clear()` does not zero `Enemies` — readers must stop at `EnemyCount` or they read last frame's enemies. On the PROGRESS watch list, and now stated in AR §4.2.
