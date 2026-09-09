# M1-11 — `ReportConeHits` fact → damage → `EnemyDamaged` / `EnemyDied`, despawn

**Size:** S · **Depends on:** M1-10 · **Branch:** `m1-11-cone-hits-to-damage`
**Design refs:** AR §1 (facts in, outcomes decided, consequences rendered), §4.1; CC §4.1 (TTK)

## Goal

The first fact closes the loop: the body reports who was in the cone, core turns that into damage, death, and events, and keeps the corpse around just long enough for a dissolve.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Combat/ConeHitsToDamageTests.cs` | Tests.Core | End-to-end through `RunSession` |
| *small edits* | | `IRunSession` + `ReportConeHits(ReadOnlySpan<int> enemyIds)`; `RunSession` forwards; `PlayerCombat` + `ResolveConeHits(ids, now)` with pending-request check and per-swing dedupe; `EnemySystem` + `ApplyDamage(id, amount, now)`, corpse timer, `CorpseTime = 0.6`; `EnemyEvents` + `EnemyDamaged`, `EnemyDied` |

## Public API

```csharp
// IRunSession (added)
void ReportConeHits(ReadOnlySpan<int> enemyIds);

// EnemySystem (added)
public const float CorpseTime = 0.6f;
public DamageResult ApplyDamage(int enemyId, float amount, float now);   // None if unknown/dead

// EnemyEvents (added)
public readonly struct EnemyDamaged { public readonly int Id; public readonly float Amount; public readonly float HpFraction; public readonly bool Killed; }
public readonly struct EnemyDied    { public readonly int Id; public readonly ContentId SpecId; public readonly Vector3 Position; }
```

## Behaviour

1. `ReportConeHits` with no pending cone request is ignored (a stale or duplicate report). One pending request is consumed per report; requests don't accumulate beyond one (a second damage frame before a report replaces the pending id).
2. For each id in the report, deduplicated (bitset sized to enemy capacity): `enemies.ApplyDamage(id, weapon.Damage.Value, now)`.
3. `EnemySystem.ApplyDamage`: unknown id or already dead → `None`. Otherwise `agent.Health.ApplyDamage` → publish `EnemyDamaged(id, applied, fraction, killed)`; if killed → publish `EnemyDied(id, specId, position)` and mark `diedAt = now`.
4. `EnemySystem.Tick` despawns agents with `IsDead && now - diedAt >= CorpseTime` (publishing `EnemyDespawned` as usual). Dead-but-not-despawned agents stay in `Registry.Alive` (M1-05 rule 4); `PlayerCombat` already treats them as not vulnerable, so they're never targeted.
5. A target that dies triggers the targeter's immediate retarget on the next tick (M1-04 rule 1) → `TargetChanged`.
6. `ReportConeHits` when not running throws `InvalidOperationException`.
7. No allocation in the report path.

## Tests

| Test | Given / When / Then |
|---|---|
| `ThreeReports_KillHusk` | Husk at 5 m, session ticking to each damage frame / `ReportConeHits([id])` after each / after the 3rd: `EnemyDied` once, `EnemyDamaged` ×3 with fractions 0.64, 0.28, 0 |
| `Report_WithoutPending_Ignored` | no swing yet / ReportConeHits([id]) / no damage, no events |
| `Report_ConsumesPending` | one frame, two reports / — / second ignored |
| `Report_DedupesIds` | pending / ReportConeHits([id, id, id]) / one `EnemyDamaged`, hp 36 − 13 |
| `Report_IgnoresUnknownAndDead` | ids [99, deadId] / — / no events |
| `Corpse_DespawnsAfterCorpseTime` | killed at t / Tick to t + 0.59 / not despawned; Tick to t + 0.61 / `EnemyDespawned` |
| `DeadTarget_RetargetsNextTick` | current target killed / Tick / `TargetChanged` to another id or −1 |
| `Report_WhenNotRunning_Throws` | — / ReportConeHits / `InvalidOperationException` |
| `ReportPath_AllocatesNothing` | warm-up / 10 000 × (tick to frame, report) / allocated-bytes delta == 0 |

## Acceptance

- [x] All tests green
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- The physics query and any visuals — M1-12.
- XP on kill — M3-01. Essence — M6-01.

## As built

Built as specified, with three departures — see the PROGRESS entry for the full argument.

1. **`ResolveConeHits(ids, now, enemies)`** takes the `EnemySystem` as a parameter. `PlayerCombat` owns no registry by design (M1-08); holding one would let the player's combat brain spawn and despawn.
2. **The dedupe is a capacity-sized `int[]`, scanned linearly — not a bitset.** *The spec's phrasing cannot be implemented:* enemy ids are monotonic for a whole run and never reused (`EnemyRegistry` rule 1), so they outgrow any capacity-sized bit index within a hundred spawns. The array keeps what the rule was asking for — a fixed structure sized to the world, not to the report — and cannot overflow, because an entry is written only for an id that damage actually reached.
3. **`EnemySystem.ApplyDamage` publishes nothing when nothing landed**, matching `PlayerCombat.ApplyDamage` rather than rule 3's literal text. A `Stat` can be driven to zero (ADR-0008) and `Health` refuses NaN, and an `EnemyDamaged` of zero would flash a hit that never arrived. One test row beyond the table covers it.

Beyond the Files table: `EnemyAgent` gained an `internal float DiedAt` (the corpse timer had to live with the agent, or a recycled one would inherit the previous life's stamp), and `RunSession.RequireRunning`'s message widened to cover facts as well as commands.

All nine rows of the Tests table pass, plus `ApplyDamage_NothingLanded_PublishesNothing`. 351 EditMode and 3 PlayMode green; zero errors and zero warnings on a clean recompile.

**Not observable in the Editor.** No physics query answers the `ConeHitIntent` until M1-12, so nothing here has ever hit a real enemy outside the suite.
