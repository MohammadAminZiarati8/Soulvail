# M0-04 — `IRandom` with named streams, `SeededRandom`, `FixedRandom`

**Size:** M · **Depends on:** M0-01 · **Branch:** `m0-04-random-streams`
**Design refs:** AR §6, §11.4; ADR-0011

## Goal

Core draws randomness only through a port with independent named streams, so a seeded run reproduces exactly and adding a consumer in one stream never shifts another.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/IRandom.cs` | Core | `IRandom` + `IRandomStream` (two interfaces, one file — they are one contract) |
| `Game/Adapters/SeededRandom.cs` | Game | PCG32 streams derived from one seed |
| `Tests/Core/Fakes/FixedRandom.cs` | Tests.Core | Scripted values for deterministic tests |
| `Tests/Game/Adapters/SeededRandomTests.cs` | Tests.Game | Determinism and independence tests |

## Public API

```csharp
namespace Soulvail.Core.Ports;

public interface IRandomStream
{
    float NextFloat();                                  // [0, 1)
    int   NextInt(int minInclusive, int maxExclusive);  // throws if max <= min
    float Range(float minInclusive, float maxInclusive);
    bool  Chance(float probability);                    // p <= 0 → always false; p >= 1 → always true
}

public interface IRandom
{
    int Seed { get; }
    IRandomStream Spawn   { get; }
    IRandomStream Offers  { get; }
    IRandomStream Affixes { get; }
    IRandomStream Drops   { get; }
    IRandomStream Misc    { get; }
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class SeededRandom : IRandom
{
    public SeededRandom(int seed);
}
```

```csharp
namespace Soulvail.Tests.Core.Fakes;

public sealed class FixedRandom : IRandom          // every stream is the same scripted stream unless set individually
{
    public FixedRandom(params float[] floats);     // NextFloat returns these in order, then 0.5f forever
    public FixedRandom SetStream(string name, params float[] floats);   // "Spawn", "Offers", …
}
```

## Behaviour

1. Each stream's seed is derived from `(seed, streamIndex)` via SplitMix64, then drives an independent PCG32 generator. Stream indices are fixed: Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4.
2. Two `SeededRandom` with the same seed produce identical sequences on every stream.
3. Drawing any number of values from one stream does not change the sequence of any other stream.
4. `NextFloat` ∈ [0, 1). `NextInt(min, max)` ∈ [min, max). `Range(a, b)` ∈ [a, b].
5. `NextInt` with `max <= min` throws `ArgumentOutOfRangeException`.
6. `Chance(p)` returns `NextFloat() < p`, so `p <= 0` is always false and `p >= 1` always true; it consumes one draw either way.
7. `Seed` returns the constructor seed.
8. No allocation per draw.
9. `FixedRandom.NextInt(min, max)` maps the scripted float `f` to `min + (int)(f * (max - min))`, clamped into range.

## Tests

| Test | Given / When / Then |
|---|---|
| `SameSeed_SameSequence_EveryStream` | two SeededRandom(42) / 100 NextFloat on each stream / sequences equal |
| `DifferentSeed_DifferentSequence` | seeds 1 and 2 / 100 NextFloat on Spawn / sequences differ |
| `Streams_AreIndependent` | r1, r2 same seed; draw 50 from r1.Spawn only / then 20 from Offers on both / Offers sequences equal |
| `NextFloat_InUnitInterval` | 100 000 draws / all in [0, 1) |
| `NextInt_WithinBounds_AndHitsBothEnds` | 100 000 draws of NextInt(3, 6) / all in {3,4,5}; 3 and 5 both seen |
| `NextInt_InvalidRange_Throws` | NextInt(5, 5) / `ArgumentOutOfRangeException` |
| `Range_WithinBounds` | 10 000 draws Range(-2, 2) / all in [-2, 2] |
| `Chance_ZeroNeverOneAlways` | 1 000 draws each / Chance(0) never true; Chance(1) always true |
| `Chance_ConsumesOneDraw` | fresh stream, Chance(0.5) then NextFloat / equals second value of a fresh stream |
| `Seed_Roundtrips` | SeededRandom(7) / Seed / 7 |
| `Draw_AllocatesNothing` | warm-up / 10 000 NextFloat / allocated-bytes delta == 0 |
| `FixedRandom_ReturnsScriptedThenDefault` | FixedRandom(0.1, 0.9) / three NextFloat / 0.1, 0.9, 0.5 |
| `FixedRandom_NextInt_MapsFloat` | FixedRandom(0.99) / NextInt(0, 10) / 9 |

## Acceptance

- [x] All tests green — 42 passed, 0 failed, via `TestRunnerApi`
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Where the seed comes from at runtime (M0-12 `PendingRun`).
- Any consumer of randomness. Nothing in M0 draws a random number; the port exists so `RunSession` can take it.
- Weighted picks, shuffles — add to `IRandomStream` when the director (M2) needs them.

## As built

**Files:** 5, not 4 — `Tests/Core/Fakes/FixedRandomTests.cs` was added, because two Tests rows exercise a `Soulvail.Tests.Core` fake and the only test file in the table lives in `Soulvail.Tests.Game`. Still size M. Approved before implementation.

**API change:** `FixedRandom.SetStream(string name, params float[])` shipped as five named setters instead — `SetSpawn`, `SetOffers`, `SetAffixes`, `SetDrops`, `SetMisc`, each returning `this` so they chain. Compile-time checked, no new type, and free to change now because nothing in the project draws a random number yet. Approved before implementation.

**Also:** `FixedRandom.Seed` returns 0 (`IRandom` requires the property; the fake has no seed constructor). `Draw_AllocatesNothing` is measured with `AllocationAssert` — the row's "allocated-bytes delta == 0" describes the BCL probe M0-02 proved inert on this runtime — and covers all four draw methods, since rule 8 is about draws in general. One test beyond the table, `FixedRandom_SetStream_OverridesOnlyThatStream`, covers the Public API's "same scripted stream unless set individually", which had no row.

**Rule 9 is the fake's mapping only.** `SeededRandom.NextInt` uses Lemire multiply-shift on the raw 32 bits rather than scaling a float, so bounded draws keep full resolution and stay branchless.

**Verified:** 42 EditMode tests green (28 before, 14 new), zero errors, zero warnings in the Unity Console.

**Carry forward:** the stream indices (Spawn 0 … Misc 4) are part of what a seed means — a sixth stream takes index 5, and nothing moves. On the PROGRESS watch list.
