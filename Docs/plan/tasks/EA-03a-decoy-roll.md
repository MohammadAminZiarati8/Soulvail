# EA-03a — Decoy Roll in core

**Size:** M · **Depends on:** EA-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Decoy Roll); M5-03 (`LureSystem`, the decoy redirect in `EnemySystem.Perceive`)

## Goal

A roll leaves a shade that every non-boss enemy walks at for a few seconds.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/PlayerCombat.cs` | Core | On roll start: drop a `Decoy` shade when ready; the interval clock |
| `Core/Combat/ShadeSystem.cs` | Core | A `Decoy` spawn drops a lure for its seconds |
| `Core/Ai/EnemySystem.cs` | Core | `Perceive`: a boss is never lured |
| `Core/Effects/PlayerStat.cs` | Core | Append `DecoySeconds` (base 0), `DecoyInterval` (base 8) |
| `Tests/Core/Combat/DecoyRollTests.cs` | Tests.Core | **New** |
| *small edits* | | `CombatantStats`, `RunSession` (attach `ShadeSystem` to `PlayerCombat`; reset on boundary), `LureSystemTests` (boss row), stat coverage tests |

## Public API

```csharp
// PlayerCombat gains:
internal void AttachShades(ShadeSystem shades);
internal void ResetDecoy();
```

## Behaviour

1. With `DecoySeconds` > 0, a roll that **starts** at `now ≥ next decoy time` spawns a `Decoy`
   shade at the roll's start position for `DecoySeconds` (damage 0), and sets the next decoy time
   to `now + DecoyInterval`. A roll before then leaves nothing.
2. A `Decoy` spawn calls `LureSystem.Drop(position, now, seconds)`. A full lure store drops nothing
   more; the shade still stands.
3. **Bosses are never lured:** in `Perceive`, an agent whose `Spec.Behaviour` is
   `EnemyBehaviourKind.Boss` keeps the player as its quarry. Every other living enemy, boss adds
   included, walks at the nearest lure (M5-03's rule, otherwise unchanged). This applies to every
   lure, the Gravecaller's Shroudstep included — a ruling recorded here.
4. A lure outlives its shade only if the shade was replaced early (EA-01a rule 1); it then ends at
   its own time. Accepted, not fixed.
5. **Ranks** (for EA-03c): 1 — `DecoySeconds` Flat +2; 2 — `DecoySeconds` +1, `DecoyInterval` −2;
   3 — `DecoyInterval` −1 (2 s/8 s → 3 s/6 s → 3 s/5 s).
6. A stage boundary, death or run reset readies the next decoy. No allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Roll_Rank1_LeavesDecoyAtStart_TwoSeconds` (1) |
| `Roll_WithinInterval_NoDecoy` / `Roll_AfterInterval_Decoy` (1) |
| `Off_NoDecoy` (1) |
| `Decoy_DropsLure_EnemiesWalkAtIt` (2) |
| `Boss_IgnoresLure` / `BossAdd_IsLured` (3) |
| `ReplacedDecoy_LureEndsOnItsOwnTime` (4) |
| `Rank3_ThreeSeconds_EveryFive` (5) |
| `StageBoundary_ReadiesDecoy` / `Roll_DoesNotAllocate` (6) |

## Out of scope

- The decoy's look and the assets (EA-03c); the burst (EA-03b).

## As built

_Filled at merge._
