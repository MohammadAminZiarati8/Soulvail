# M1-15 — `ChargeIntent`, `IPlayerCommands.MovementSkill`, `ReportChargeHits`, knockback intent

**Size:** M · **Depends on:** M1-11, M1-14 · **Branch:** `m1-15-charge-integration`
**Design refs:** CC §5; AR §4.1; ADR-0003

## Goal

Charge goes through the whole architecture: a command in, an intent out, i-frames on the player's health, a fact back about who was passed through, damage decided by core, and a knockback intent for the body to execute.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/ChargeIntent.cs` | Core | Direction, distance, duration |
| `Core/Run/EnemyKnockbackIntent.cs` | Core | Id, direction, distance |
| `Tests/Core/Combat/ChargeIntegrationTests.cs` | Tests.Core | Through `RunSession` |
| *small edits* | | `IIntentSink` + `Charge(in)`, `EnemyKnockback(in)`; `IntentBuffer` + storage; `RecordingIntents`; `IPlayerCommands` + `MovementSkill()`; `IRunSession` + `ReportChargeHits(ReadOnlySpan<int>)`; `RunSession` forwards both, suppresses `PlayerMove` while charging; `PlayerCombat` owns `ChargeSkill`; `PlayerMotor.Stop()` used at charge end; `CombatEvents` + `ChargeStarted`, `ChargeEnded`; `FocusTracker` treats charging as moving |

## Public API

```csharp
namespace Soulvail.Core.Run;

public readonly struct ChargeIntent        { public readonly Vector2 DirectionXZ; public readonly float Distance; public readonly float Duration; }
public readonly struct EnemyKnockbackIntent{ public readonly int Id; public readonly Vector2 DirectionXZ; public readonly float Distance; }

// CombatEvents (added)
public readonly struct ChargeStarted { public readonly Vector2 DirectionXZ; }
public readonly struct ChargeEnded   { }
```

## Behaviour

1. `IPlayerCommands.MovementSkill()` → `charge.Request(State.Time)`. Throws when not running.
2. `PlayerCombat.Tick`: `charge.Tick(dt, now, snapshot.MoveInput, motorFacingXZ)`; on start → `Health.SetExternalInvulnerable(true)`, `intents.Charge(new(Direction, Distance, Duration))`, publish `ChargeStarted`, open a hit window `until activeUntil + 0.1 s`.
3. While `charge.IsInvulnerable` was true and becomes false → `Health.SetExternalInvulnerable(false)`, publish `ChargeEnded`.
4. While `charge.IsActive`, `RunSession` does **not** emit `PlayerMove`; the body executes the `ChargeIntent`. On the first tick after `IsActive` clears, `Motor.Stop()` (velocity zero — no carry-over), then normal `PlayerMove` resumes. Position comes back via the snapshot as always.
5. `ReportChargeHits(ids)`: ignored outside the hit window; deduplicated; for each living id → `enemies.ApplyDamage(id, spec.Damage, now)` and `intents.EnemyKnockback(id, Direction, spec.Knockback)`. Multiple reports within one window are allowed (the body reports per frame during the sweep); each id is damaged **once per charge** (bitset cleared at start).
6. Charging counts as "moving" for `FocusTracker` (level drops to 0 on start).
7. No allocation in any of these paths.

## Tests

| Test | Given / When / Then |
|---|---|
| `MovementSkill_EmitsChargeIntent_AndEvent` | running, facing +Z / MovementSkill, Tick / one `Charge` intent (dir (0,1), 10, 0.22); one `ChargeStarted` |
| `PlayerMove_SuppressedWhileActive` | started / Tick ×10 (0.16 s) / no `PlayerMove` intents |
| `PlayerMove_ResumesAfter_WithZeroVelocity` | active until 0.22 / Tick past / first `PlayerMove` has zero velocity, then accelerates |
| `Health_InvulnerableDuringWindow` | started at 0; enemy damage at 0.1 and at 0.26 / — / both Blocked; at 0.3 / applied |
| `ChargeEnded_PublishedOnce` | — / run through / one `ChargeEnded` at ≈ 0.27 |
| `ReportChargeHits_DamagesAndKnocksBack` | enemy id 1 alive / ReportChargeHits([1]) during window / `EnemyDamaged` 20; `EnemyKnockback` intent (1, dir, 5) |
| `ReportChargeHits_OncePerEnemyPerCharge` | reports [1], [1], [1,2] / — / id 1 damaged once, id 2 once |
| `ReportChargeHits_OutsideWindow_Ignored` | after activeUntil + 0.1 / report / nothing |
| `Charge_KillsLowHpEnemy` | enemy hp 15 / report / `EnemyDied` |
| `Focus_DropsOnCharge` | focus level 1 / MovementSkill, Tick / level 0 |
| `Commands_WhenNotRunning_Throw` | — / MovementSkill / `InvalidOperationException` |
| `ChargePath_AllocatesNothing` | warm-up / 1 000 full charges with reports / allocated-bytes delta == 0 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Executing the motion, sweeping for hits, the button — M1-16.
- Wall collision during a charge — the body stops at walls naturally (`CharacterController`); core doesn't need to know.

## As built

Built to the Files table. Twelve rows, all green; 409 EditMode + 3 PlayMode, zero errors, zero new warnings.

**Three places the code says something the spec did not.**

1. **The dedupe is a list of ids, not a bitset** (rule 5 says "bitset cleared at start"). Enemy ids rise for the whole run and are never reused — `EnemyRegistry` rule 1 — so by the hundredth spawn they are far past any bit index a capacity-sized set could offer. `_chargeHitIds` is the same shape `ResolveConeHits` already uses and was documented as such in M1-11, scanned linearly at most 64 × 64 times per dash. What is genuinely new is its *lifetime*: it is cleared when a dash starts and not when a report is answered, which is what makes "once per Charge" survive a sweep the body reports frame by frame.
2. **Aliveness is asked of the registry rather than inferred from the damage.** `ResolveConeHits` treats "nothing landed" as "nobody was there", which is true for a weapon that always damages. A Charge always damages too — but `MovementSkillSpec.Damage` is documented as legally zero (M5-03's Shroudstep), and reading aliveness off the damage would leave a zero-damage movement skill knocking nothing back. One `Registry.TryGet` per id, no allocation.
3. **The motor is not ticked at all while a dash is active, and the tick it ends on moves nowhere.** Rule 4 asks for `Motor.Stop()` at the end, which is only load-bearing if the motor kept its pre-dash velocity throughout — so `RunSession.TickBody` skips `Motor.Tick` for the length of the dash, and on the first tick after `IsActive` clears it stops the motor and emits that zero rather than accelerating within the same tick. One frame at rest, 16 ms, against a carry-over at running speed. `PlayerMove_ResumesAfter_WithZeroVelocity` is the row that pins it.

**Two moments, not one.** `ChargeEnded` is tied to the i-frames lapsing (0.27 s), while the motor is handed back when the movement ends (0.22 s). Rule 3 and rule 4 say exactly this; it is worth restating because the names suggest a single event.

**Beyond the Files table:** `SilentIntents` in `ConeHitsToDamageTests` gained two no-op methods — widening `IIntentSink` forces every implementer, and that private fake is the third. Two lines, no behaviour.

**Not covered by a test:** `IntentBuffer`'s new `HasCharge` / `Knockbacks` storage has no row in `IntentBufferTests` — the spec's Tests table has none and the core fixture proves the intents core writes, not the buffer that receives them. M1-16 is the first thing that reads them and is where they earn coverage.
