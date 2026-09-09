# M1-10 — `WeaponSpec`, `Weapon` cadence + damage frame, `ConeHitIntent`

**Size:** M · **Depends on:** M1-08 · **Branch:** `m1-10-weapon-and-cone-intent`
**Design refs:** CC §4.1–4.2, §7 (attack); AR §4.1; ADR-0003, ADR-0008

## Goal

The Censer swings itself: core decides *when* to swing and when the damage frame lands, and asks the body to resolve a cone — the first intent that requests a physical fact.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/WeaponSpec.cs` | Core | Cone numbers |
| `Core/Combat/Weapon.cs` | Core | Cadence, swing state, damage frame |
| `Core/Run/ConeHitIntent.cs` | Core | "Resolve this cone and tell me who's in it" |
| `Tests/Core/Combat/WeaponTests.cs` | Tests.Core | Every rule |
| *small edits* | | `IIntentSink` + `ConeHit(in ConeHitIntent)`; `IntentBuffer` + cone list + `Clear`; `RecordingIntents`; `CharacterSpec`/`CharacterDefinition`/`Oathbound.asset` + `Weapon` (13 / 3.0 / 8 / 60° / 0.4); `PlayerCombat` owns a `Weapon`, takes `IIntentSink`, sets `DpsOneSecond`; `CombatEvents` + `PlayerAttacked` |

## Public API

```csharp
namespace Soulvail.Core.Content;

public enum WeaponKind { Cone }   // Projectile arrives in M5-01

public sealed class WeaponSpec
{
    public WeaponKind Kind         { get; }
    public float Damage            { get; }   // 13
    public float SwingsPerSecond   { get; }   // 3.0
    public float Range             { get; }   // 8
    public float ConeAngleDeg      { get; }   // 60 (full angle)
    public float DamageFrame       { get; }   // 0.4 of the swing interval
}
```

```csharp
namespace Soulvail.Core.Combat;

public readonly struct WeaponTick { public readonly bool SwingStarted; public readonly bool DamageFrame; }

public sealed class Weapon
{
    public Weapon(WeaponSpec spec);
    public Stat  Damage   { get; }            // base spec.Damage
    public Stat  FireRate { get; }            // base spec.SwingsPerSecond
    public float Range        => …;
    public float ConeAngleDeg => …;
    public bool  IsSwinging   { get; }
    public float DpsOneSecond => Damage.Value * FireRate.Value;
    public WeaponTick Tick(float dt, float now, bool targetInRange);
    public void Reset();
}
```

```csharp
namespace Soulvail.Core.Run;

public readonly struct ConeHitIntent
{
    public readonly int     RequestId;   // monotonically increasing per run
    public readonly Vector3 Origin;      // player position
    public readonly Vector2 FacingXZ;    // unit
    public readonly float   Range;
    public readonly float   AngleDeg;
}

public readonly struct PlayerAttacked { public readonly Vector2 FacingXZ; }   // in CombatEvents
```

## Behaviour

1. Interval `= 1 / FireRate.Value`, sampled **at swing start** and held for that swing (a mid-swing modifier changes the *next* swing).
2. A swing starts when `!IsSwinging && targetInRange && now >= nextSwingAt`. On start: `SwingStarted = true`, `damageFrameAt = now + DamageFrame × interval`, `swingEndsAt = now + interval`, `nextSwingAt = swingEndsAt`.
3. `DamageFrame = true` on the first tick where `now >= damageFrameAt`, exactly once per swing — even if the target left range or died meanwhile (the cone resolves against whatever is there).
4. `IsSwinging` clears when `now >= swingEndsAt`. If `targetInRange` is still true at that tick, the next swing starts on the same tick (no dead frame between swings).
5. `targetInRange` is supplied by `PlayerCombat`: `CurrentTargetId >= 0 && !IsTargetBlocked && distance(player, target) <= Range`.
6. On `SwingStarted`, `PlayerCombat` publishes `PlayerAttacked(facing)`. On `DamageFrame`, it emits `intents.ConeHit(new ConeHitIntent(++requestId, playerPos, motorFacingXZ, Range, ConeAngleDeg))` and remembers the request as pending (consumed in M1-11).
7. `PlayerCombat.DpsOneSecond = Weapon.DpsOneSecond` every tick (feeds the finisher bonus).
8. `Tick` allocates nothing.

TTK sanity: 13 damage × 3 swings = 39 ≥ 36 → a Husk dies on the third damage frame, ~0.87 s after the first swing starts.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoTarget_NeverSwings` | targetInRange false / Tick 2 s / no `SwingStarted` |
| `Target_SwingsImmediately` | ready / Tick(0.016, true) / `SwingStarted`, `IsSwinging` |
| `DamageFrame_AtFortyPercent` | started at 0 / ticks of 0.01 / `DamageFrame` at t ∈ [0.133, 0.143), exactly once |
| `ThreeSwingsPerSecond` | target always in range / 1.0 s of 0.01 ticks / 3 damage frames, 3 swing starts |
| `FireRateModifier_ChangesNextSwing` | +30 % PercentAdd on `FireRate` before start / 1.0 s / ≈ 3.9 frames (3 or 4; assert interval ≈ 0.256) |
| `ModifierMidSwing_DoesNotShortenCurrentSwing` | start, then +100 % / — / current swing ends at the original time |
| `TargetLeavesMidSwing_FrameStillFires` | start, targetInRange false after 0.05 / — / damage frame still fires |
| `NoDeadFrameBetweenSwings` | continuous target / tick landing exactly on swingEndsAt / next `SwingStarted` same tick |
| `PlayerCombat_EmitsConeHit_OnDamageFrame` | session with enemy at 5 m / tick to damage frame / one `ConeHit` intent: origin = player pos, facing = motor facing, range 8, angle 60, `RequestId 1` |
| `PlayerCombat_PublishesPlayerAttacked_OnSwingStart` | — / — / one `PlayerAttacked` per swing |
| `PlayerCombat_NoSwing_WhenTargetBlocked` | only invulnerable enemy / 1 s / no intents |
| `PlayerCombat_NoSwing_WhenTargetBeyondRange` | enemy at 9 m / 1 s / no intents |
| `DpsOneSecond_Is39` | Oathbound weapon / — / 39 |
| `Tick_AllocatesNothing` | warm-up / 10 000 ticks / allocated-bytes delta == 0 |

## Acceptance

- [x] All tests green — 341 EditMode, 3 PlayMode, 0 failed
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Turning hits into damage — M1-11. The physics query — M1-12.
- Projectile weapons — M5-01.

## As built

Built as specified. All fourteen rows of the Tests table exist under those names and pass; the eight behaviour rules are implemented as written. Three departures, each argued in the code:

1. **`PlayerCombat.Tick` gained a `bodyFacing` parameter** — `Tick(dt, now, snapshot, enemies, bodyFacing)`. Rule 6 says the cone carries the motor's facing, and `PlayerCombat` holds no motor: `RunState.Motor` is `internal` so that no one can advance it twice, and injecting it here would rebuild that hazard inside core. A snapshot field was the other option and was rejected as duplicated state — `PlayerView` sets the transform straight from `intent.Facing` with no smoothing, so core already knows the rendered facing exactly. It is **last tick's facing**, because the run turns the motor after combat decides where to look: up to 12° behind the drawn pose at 60 fps, inside a 60° arc, only while turning. Cost: fifteen mechanical call-site updates in `PlayerCombatTests`.
2. **A `FireRate` of zero or below refuses to start a swing.** `Stat` clamps nothing, so a −100 % `PercentMult` would otherwise schedule a swing ending at infinity and leave `IsSwinging` true for the rest of the run. Extra row: `FireRateAtZero_DoesNotSwing`.
3. **A dead player still swings.** Rule 5's three conditions are implemented exactly; there is nothing to stop until M1-17 wires the death flow, and a fourth condition here would be a second opinion on when a run is over. Named in `PlayerCombat`'s remarks.

Additions the table implies rather than lists: `PlayerCombat.PendingConeRequestId` (rule 6's "remembers the request as pending", exposed so M1-11 can match a fact against it rather than sitting as a dead field), and `Weapon.Reset` joining `PlayerCombat.Reset`.

Four extra rows in `WeaponTests` (`Reset_ReturnsToRest`, `FireRateAtZero_DoesNotSwing`, `Spec_InvalidNumbers_Throw`, `Ctor_NullSpec_Throws`) and edits to six existing test files, all of which guard a listed small edit — see the PROGRESS entry for the list and the reasons.

**Manual verification:** none possible or needed. There is nothing on screen yet — M1-12 draws the swing and resolves the cone. `Tick_AllocatesNothing` covers the per-frame budget; the TTK invariant is pinned twice, in `ThreeSwingsPerSecond` and in `CharacterDefinitionTests.Oathbound_ToSpec_HasWeapon`.
