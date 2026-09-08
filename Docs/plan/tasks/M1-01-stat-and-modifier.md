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

Three files as specified, and 15 tests, not 13. 168 EditMode tests pass in 0.77 s (153 before), zero errors, zero warnings.

1. **Non-finite values are refused at every door** — `Modifier.Value`, `Stat(baseValue)` and `Base` all throw `ArgumentOutOfRangeException` for NaN or infinity. Rule 6's "nothing guards against negative results" stands as written; this is a different animal. `Changed` decides whether to fire by comparing the old and new values, every comparison against NaN is false, so a NaN-valued stat reports itself unchanged *forever*: the failure mode is a HUD that quietly stops updating, not one showing a silly number. The comparison itself is spelled `!(MathF.Abs(diff) <= tolerance)` for the same reason, so a value that goes non-finite by some other route still counts as a change.
2. **An undefined `ModifierKind` is refused too**, same exception. An enum is not a closed set at runtime — a cast makes any int one — and a kind the arithmetic does not handle would be counted by `ModifierCount` and listed by `Describe` while changing nothing. The recompute's `switch` keeps an unreachable `default` that throws, so a fourth kind added without teaching it fails loudly rather than being silently dropped.
3. **`Add` guards a sourceless modifier** (`ArgumentException`), which only `default(Modifier)` can produce. Same shape as `default(ContentId)` in M0-08: a struct always has a zeroed form, so the constructor's guard is not the last word, and a modifier no source can remove is a permanent change with nothing to point at. `RemoveAll`, `CopyModifiersTo` and `Describe` throw `ArgumentNullException` on a null argument.
4. **Two tests beyond the Tests table** — `NonFiniteValue_Throws` and `UndefinedKind_Throws` — for decisions 1–2. `NullSource_Throws` also asserts the other doors in decision 3, and `Describe_Format` picked up two asserts the table does not ask for: the format under a comma-decimal culture, and the empty-stack shape.
5. **`CopyModifiersTo` appends rather than clearing**, so a debug panel can gather several stats into one buffer it owns and reuses. `Describe` appends too.
6. **`Describe` always writes the same shape**, so a stat with no modifiers reads `13.00 = (13.00 + 0.00) × 1.00`. The alternative — omitting factors that are 1 — would make a reader parse a variable format to find the number they came for. Formatted with `InvariantCulture`: this is a diagnostic, and a number in a bug report should read the same wherever it was captured.
7. **The `×` is written as `(char)0x00D7`, not the glyph.** With the glyph in both files, a source read as anything but UTF-8 would garble the expectation and the output identically, and `Describe_Format` would pass while the panel showed mojibake.
8. **The cache is lazy only while nothing is subscribed to `Changed`.** Rules 2 and 5 look independent and are not: deciding whether the value changed means computing it at mutation time. `ValueBeforeMutation` returns without computing when there are no subscribers, and for a watched stat the recompute was going to happen anyway.
9. **`RemoveAll` compacts the list in place** — one pass, a write index, then a single `RemoveRange`. No allocation, no O(n²) shuffle, and survivors keep the order they were added, which is the order `Describe` and `CopyModifiersTo` read out.
10. **`ValueRead_AllocatesNothing` invalidates inside the measured body.** Ten thousand reads of a valid cache exercise one field read; the walk over the modifier list — where a rewrite to LINQ would allocate — only runs on the first. Its thousand-iteration half sets `Base` every time.
11. **Two files outside the Files table were corrected, on the owner's go-ahead.** AR §11.1's code sketch showed `IReadOnlyList<Modifier> Modifiers` and a `void RemoveAll`; it now matches what was built, with a line naming the two members that moved and why. `MovementSpec`'s class comment claimed M1-01 would replace `Speed` with a `Stat`; it now names M1-08 and says why the spec stays a plain float — the stat belongs to the live character, not to authored data.
