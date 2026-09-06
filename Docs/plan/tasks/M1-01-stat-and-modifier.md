# M1-01 — `Stat` and `Modifier`

**Size:** S · **Depends on:** M0 · **Branch:** `m1-01-stat-and-modifier`
**Design refs:** AR §11.1, ADR-0008 (non-negotiable)

## Goal

Every gameplay number from here on is a `Stat` with a modifier stack, so ten future sources of "+damage" plug in without touching combat code — and a designer can always ask a `Stat` where its value came from.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/Modifier.cs` | Core | Kind + value + source |
| `Core/Combat/Stat.cs` | Core | Base + ordered modifier stack, cached value |
| `Tests/Core/Combat/StatTests.cs` | Tests.Core | Every rule below |

## Public API

```csharp
namespace Soulvail.Core.Combat;

public enum ModifierKind { Flat, PercentAdd, PercentMult }

public readonly struct Modifier
{
    public readonly ModifierKind Kind;
    public readonly float Value;          // Flat: units · PercentAdd: 0.15 = +15 % · PercentMult: 0.2 = ×1.2
    public readonly object Source;        // never null; identity used for removal and description
    public Modifier(ModifierKind kind, float value, object source);
}

public sealed class Stat
{
    public Stat(float baseValue);

    public float Base  { get; set; }      // setting invalidates the cache
    public float Value { get; }           // (Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult_i)
    public int   ModifierCount { get; }

    public void Add(in Modifier modifier);
    public int  RemoveAll(object source);           // number removed
    public void CopyModifiersTo(List<Modifier> destination);
    public void Describe(StringBuilder sb);          // "28.80 = (13.00 + 2.00) × 1.60 × 1.20"

    public event Action<Stat> Changed;               // fires only when Value actually changes
}
```

## Behaviour

1. Order is fixed: sum all `Flat`, add to `Base`; multiply by `(1 + ΣPercentAdd)`; multiply by each `(1 + PercentMult)` in turn.
2. `Value` is cached; recomputed lazily on the first read after `Base` set, `Add`, or a `RemoveAll` that removed something. Reading `Value` allocates nothing.
3. `Modifier.Source == null` → `ArgumentNullException`. Sources are compared by reference (`ReferenceEquals`).
4. The same source may add several modifiers; `RemoveAll(source)` removes all of them and returns the count. Unknown source → 0, no `Changed`.
5. `Changed` fires after `Base` set / `Add` / `RemoveAll` **only if** the new `Value` differs from the old (compare with a 1e-6 tolerance).
6. A `PercentMult` of −1 yields `Value == 0`; nothing guards against negative results — callers clamp where it matters (e.g. cooldown floor in M3-06).
7. `Describe` writes the base, the flat sum, each percent factor, and the result in the format above — allocation is acceptable here (debug only).
8. `Add` may grow an internal list (amortised allocation). Hot paths add/remove rarely; reads are free.

## Tests

| Test | Given / When / Then |
|---|---|
| `BaseOnly_ValueIsBase` | Stat(13) / Value / 13 |
| `Flat_AddsToBase` | +2 Flat / Value / 15 |
| `PercentAdd_SumsAdditively` | +15 %, +45 % PercentAdd on base 10 / Value / 16 |
| `PercentMult_MultipliesEach` | ×1.2, ×1.5 PercentMult on base 10 / Value / 18 |
| `Order_FlatThenAddThenMult` | base 13, +2 Flat, +15 % Add, +45 % Add, 0.2 Mult / Value / 28.8 ± 1e-4 |
| `SetBase_Invalidates` | base 10 + 50 % Add, read / Base = 20 / Value 30 |
| `RemoveAll_RemovesEverythingFromSource` | src A: Flat + Add; src B: Flat / RemoveAll(A) / returns 2, only B remains |
| `RemoveAll_UnknownSource_ReturnsZero_NoEvent` | — / RemoveAll(new object()) / 0; `Changed` not fired |
| `NullSource_Throws` | Modifier(Flat, 1, null) / — / `ArgumentNullException` |
| `Changed_FiresOnlyWhenValueChanges` | subscribe / Add(Flat 0) / not fired; Add(Flat 1) / fired once |
| `PercentMultMinusOne_YieldsZero` | Mult −1 / Value / 0 |
| `Describe_Format` | the Order test's stat / Describe / `"28.80 = (13.00 + 2.00) × 1.60 × 1.20"` |
| `ValueRead_AllocatesNothing` | warm-up / 10 000 reads with cache valid, and 1 000 reads after invalidation / allocated-bytes delta == 0 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Converting `MovementSpec.Speed` to a `Stat` — M1-08 gives the player a `Stat` for speed when `PlayerCombat` owns the motor's inputs; the spec stays a plain float (it's data).
- Timed modifiers — callers own timing and call `RemoveAll`.

## As built

_Filled at merge._
