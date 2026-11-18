# SS-16b3 — Charge travel and clipped fans in core

**Size:** M (6 counted) · **Depends on:** SS-16b2 · **Branch:** `ss-16b3-charge-and-fan`
**Design refs:** [GD §9.4](../../GameDesign.md#94-the-sunscar--stage-10-the-shardback).

## Goal

Core charge/fans/pillars; owner go needed.

## Files

Under `Assets/_Project/`; new `.meta` included.

| Path | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ShardbackBehaviour.cs` | Core | State machine |
| `Core/Ai/ShardbackStats.cs` | Core | Stats |
| `Core/Events/ShardbackEvents.cs` | Core | Events |
| `Tests/Core/Ai/ShardbackBehaviourTests.cs` | Tests.Core | Regression |
| `Tests/Core/Ai/ShardbackChargeTests.cs` | Tests.Core | Mechanics |
| `Tests/Core/Run/ShardbackProbeRoutingTests.cs` | Tests.Core | Routing |

Listed files.

## Public API

```csharp
// Append ShardbackMove.Charge; preserve existing values.
void ShardbackBehaviour.OnShardbackStop(in ShardbackStop report); // implements listener
// Stat getters, defaults:
Stat ChargeLength, ChargeHalfWidth, ChargeWindupSeconds, ChargeSpeed,
    ChargeDamage, ChargeRecoverySeconds, PillarRecoverySeconds,
    ChargeStallSeconds; // 12, 1, 1.2, 10, 18, 1.5, 3, 0.25
// Retain constructors; add trailing Key overload to Armed/Impact.
// Events: readonly structs/fields, PascalCase args.
ShardbackFightStarted(int bossId, Guid lifetime);
ShardbackPillarBroken(int bossId, Guid lifetime, int slot);
ShardbackClipped(int bossId, ShardbackProbeKey key, float centre, float plus, float minus);
```

## Behaviour

1. Phase 0: fan/charge; phase 1+: fan/charge/burst. Phase 2 skips fan recovery only: reaim charge/full tell/recovery, full burst. Retain SS-16a otherwise; discard transition overflow.
2. First tick of each fight publishes FightStarted. Guid identifies fight; casts increase. Arm emits keyed Armed/probes: charge 0, fan 1–3; snapshot origin/aim/geometry/tuning. Burst: no probe. After all facts, next tick publish Clipped (charge: stop/0/0; fan: centre/plus/minus) for tells. Wait stationary after tell for facts; missing never means clear.
3. First valid/current boss/lifetime/cast/lane only. Reject duplicates, wrong lanes/casts, default/nonfinite/out-of-range facts, spent-pillar answers, reports after impact/phase/reset. None distance equals reach. Phase cancels work, preserves spent mask; Reset clears mask/rotates lifetime even on reused ID. Reports never damage/publish.
4. Fan resolves with three answers after tell. Clip each strip independently; blocked endpoint excluded, clear endpoint included. Union hits once, committed damage; ignore decoys.
5. Charge velocity 10, capped by remaining distance/dt. Damage sweeps previous to observed forward SelfPosition progress, clamped [0,stop], half-width 1; never requested displacement/full tell. Apply committed damage once via PlayerCombat; ignore decoys. Blocked endpoint/beyond safe; no extra contact hit. At stop publish Impact; intact first pillar: mark spent, publish Broken once, recover 3 s; otherwise 1.5 s. Zero stop breaks without moving. Stalled short 0.25 s: normal recovery/no break. Only charge spends slots.
6. Ticks/reports allocate nothing. All-spent cycles allow sidesteps/fan gaps/burst retreat; damageable outside beats.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rotation_AllPhases_FullTells` | 0/1/2 / cycles+large dt / rule 1 |
| `Reports_MissingStaleRecycled_Reject` | Rule 2/3 variants / report/beat/reset/session / no stale effects; Clipped once |
| `Fan_ThreeStops_UnionOnce` | Unequal clips, edge hero, decoy / impact / rule 4 |
| `Charge_ObservedSweep_StopsDamage` | dt/sidestep/stall; Cover/Boundary/None/Unknown / ticks / rule 5 + i-frames |
| `Pillars_FirstOnly_PersistAndReset` | Zero stop/two pillars in line / impact+beats/reset / one break/slot, recovery (3/5) |
| `Fight_AllSpent_CompletesCycles` | Three charge breaks / beats+cycles / rule 6; AllocationAssert.None |

Precedence/guards: `_TEMPLATE.md`; record resolutions.

## Out of scope

No assets/stages/presentation.

## As built

Implementation: ≤1,500-byte body; delete/tick/update PROGRESS; fresh EditMode/compiler/format checks.
