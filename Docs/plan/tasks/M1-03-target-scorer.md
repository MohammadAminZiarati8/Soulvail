# M1-03 — `TargetCandidate`, `TargetingSpec`, `TargetScorer`

**Size:** M · **Depends on:** M1-01 · **Branch:** `m1-03-target-scorer`
**Design refs:** CC §3.1–3.3, §3.6, §7 (targeting); GD §5.4, §8.1 (`targetPriority`)

## Goal

The decision "which enemy should the gun face" is a pure, tested function of plain numbers — priority, distance, elite, finisher, hysteresis — so auto-aim feels intelligent for reasons a designer can tune in the Inspector.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/TargetCandidate.cs` | Core | What the scorer knows about one enemy |
| `Core/Content/TargetingSpec.cs` | Core | Weights + acquire range |
| `Core/Combat/TargetScorer.cs` | Core | Score and select |
| `Tests/Core/Combat/TargetScorerTests.cs` | Tests.Core | Every rule |
| *small edits* | | `CharacterSpec` + `Targeting`; `CharacterDefinition` + fields; `Oathbound.asset` (range 12, weights below); `CharacterDefinitionTests` |

## Public API

```csharp
namespace Soulvail.Core.Combat;

public readonly struct TargetCandidate
{
    public readonly int   Id;
    public readonly float Distance;
    public readonly int   Priority;        // EnemySpec.TargetPriority, 1–8
    public readonly bool  IsElite;
    public readonly bool  IsVulnerable;    // false → never selected
    public readonly float Hp;              // for the finisher bonus
    public TargetCandidate(int id, float distance, int priority, bool isElite, bool isVulnerable, float hp);
}

public sealed class TargetScorer
{
    public TargetScorer(TargetingSpec spec);
    public float Score(in TargetCandidate c, bool isCurrent, float dpsOneSecond);
    /// Highest score among vulnerable candidates within AcquireRange; -1 if none. Ties → lowest Id.
    public int SelectBest(ReadOnlySpan<TargetCandidate> candidates, int currentId, float dpsOneSecond);
    /// Nearest candidate regardless of vulnerability; -1 if empty. Used for the "all blocked" state.
    public int SelectNearest(ReadOnlySpan<TargetCandidate> candidates);
}
```

```csharp
namespace Soulvail.Core.Content;

public sealed class TargetingSpec
{
    public float AcquireRange   { get; }   // 12
    public float DistanceWeight { get; }   // 3.0
    public float EliteBonus     { get; }   // 2.0
    public float FinisherBonus  { get; }   // 1.0
    public float Hysteresis     { get; }   // 1.5
    public float Cadence        { get; }   // 0.1 s
    public TargetingSpec(float acquireRange, float distanceWeight, float eliteBonus, float finisherBonus, float hysteresis, float cadence);
}
```

## Behaviour

1. `Score = Priority + DistanceWeight × (1 − Distance / AcquireRange) + EliteBonus × IsElite + FinisherBonus × (Hp <= dpsOneSecond) + Hysteresis × isCurrent`.
2. `SelectBest` ignores candidates with `!IsVulnerable` or `Distance > AcquireRange`. Among the rest it returns the highest score; exact ties resolve to the lowest `Id` (deterministic).
3. `SelectBest` passes `isCurrent = (c.Id == currentId)` so the incumbent gets the hysteresis bonus — a challenger must beat it by more than `Hysteresis`.
4. `SelectNearest` considers every candidate, vulnerable or not; ties → lowest `Id`.
5. Empty span → −1.
6. No allocation in any call.

Worked example (the design's own): Choir (priority 8) at 11 m vs Husk (priority 1) at 5 m, range 12, no bonuses: Choir 8 + 3 × 0.083 = 8.25; Husk 1 + 3 × 0.583 = 2.75 → Choir.

## Tests

| Test | Given / When / Then |
|---|---|
| `Score_MatchesFormula` | priority 4, dist 6, range 12, elite, hp 5, dps 10, current / Score / 4 + 1.5 + 2 + 1 + 1.5 = 10 |
| `PriorityBeatsDistance` | Choir@11 (p8), Husk@5 (p1) / SelectBest(current −1) / Choir |
| `Hysteresis_KeepsIncumbent` | A score 5 (current), B score 6 / SelectBest(current A) / A (5 + 1.5 > 6) |
| `Hysteresis_ChallengerWinsPastMargin` | A 5 (current), B 7 / — / B |
| `Invulnerable_NeverSelected` | only candidate invulnerable / SelectBest / −1 |
| `OutOfRange_NeverSelected` | candidate at 12.1 / — / −1; at 12.0 / selected |
| `EliteBonus_Applies` | two identical, one elite / — / elite |
| `FinisherBonus_AppliesWhenHpAtOrBelowDps` | hp 10 vs dps 10 → bonus; hp 10.1 → no bonus |
| `Ties_ResolveToLowestId` | identical candidates ids 9, 3 / — / 3 |
| `Empty_ReturnsMinusOne` | empty / SelectBest, SelectNearest / −1 |
| `SelectNearest_IgnoresVulnerability` | invulnerable@2, vulnerable@5 / SelectNearest / the one at 2 |
| `Select_AllocatesNothing` | 40 candidates, warm-up / 10 000 SelectBest / allocated-bytes delta == 0 |
| `Oathbound_ToSpec_HasTargeting` *(Tests.Game)* | asset / ToSpec / range 12, weights 3/2/1/1.5, cadence 0.1 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Building candidates from enemies and the snapshot — M1-08.
- Line-of-sight filtering (CC §3.1 says skip for V1).

## As built

_Filled at merge._
