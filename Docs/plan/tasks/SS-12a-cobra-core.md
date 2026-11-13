# SS-12a — The cobra in core: a locked spit cone and Poison on the hero

**Size:** M · **Depends on:** — · **Branch:** `ss-12a-cobra-core`
**Design refs:** GD §8.12, §16.5, ST-03a (enemy `Poison`), JG-12 (the Mother's spray test)

## Goal

A new `CobraBehaviour` holds 8 m, closes to 4 m, rears 0.7 s over a locked cone and spits. A hit poisons the hero: capped stacks that pulse without i-frames, show in the status row and clear on any Space move.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SpitConeSpec.cs` | Core | `BurrowSpec`'s shape and validation |
| `Core/Ai/CobraBehaviour.cs` | Core | hold, close, rear, spit, recover |
| `Core/Events/CobraEvents.cs` | Core | the three events and `CobraPhase` |
| `Tests/Core/Ai/CobraBehaviourTests.cs` | Tests.Core | |
| `Tests/Core/Combat/HeroPoisonTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec` (`spitCone = null` last, property, guard); `EnemyBehaviourKind.Cobra` appended; `EnemyAgent`'s factory case; `Poison.Apply` gains `int maxStacks = MaxStacks`; `Health.ApplyPulse`; `PlayerCombat` (a `Poison`, the members below, the pulse in `Tick`, the clear); `PlayerDamaged` (`bool isPulse = false` appended); `RunState.PlayerPoisonStacks` |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class SpitConeSpec // finite, > 0; Close < Hold; HalfAngle < 90; fraction ≤ 1; stacks ≥ 1
{
    public SpitConeSpec(float holdDistance, float closeDistance, float rearSeconds, float halfAngleDegrees,
        float length, float spitDamage, float poisonFractionPerSecond, float poisonSeconds,
        int poisonMaxStacks, float recoverSeconds, float cooldownSeconds);
        // 8, 4, 0.7, 30, 5, 8, 0.02, 4, 3, 0.5, 3 — a get-only property each
}
// EnemySpec.SpitCone — required for Cobra, refused on any other kind.
namespace Soulvail.Core.Events;
public enum CobraPhase { Hold, Close, Rear, Recover }
public readonly struct CobraPhaseChanged { int Id; CobraPhase Phase; float Seconds; }
public readonly struct CobraConeArmed { int Id; Vector3 Apex; Vector2 Direction; float HalfAngleDegrees; float Length; float Seconds; }
public readonly struct CobraSpat { int Id; bool Hit; }
// CobraBehaviour.State, .Cooldown
// PlayerCombat: bool ApplyPoison(float maxHpFractionPerStackPerSecond, float seconds, int maxStacks);
//   DamageResult ApplyPulse(float amount, float now); int PoisonStacks { get; }
// Health.ApplyPulse(float amount, float now); PlayerDamaged.IsPulse; RunState.PlayerPoisonStacks
```

## Behaviour

1. **Hold** (recharging): beyond `HoldDistance` it slithers in on `PathDirectionToPlayer` at `MoveSpeed`; inside `HoldDistance × SpitterBehaviour.RetreatFraction` it backs off on `−DirectionToPlayer`; between, it stands facing the hero.
2. **Close.** Off cooldown with `HasLineOfSight`, it slithers in to `CloseDistance`. Losing sight returns it to rule 1.
3. **Rear.** There it stands, locks apex (itself) and direction (to the hero), publishes one `CobraConeArmed` and waits `RearSeconds`. Nothing turns or cancels it.
4. **Spit.** The hero is hit when in sight, within `Length` of the apex and `HalfAngleDegrees` of the direction on XZ (the spray test): `ApplyDamage(SpitDamage)`, then `ApplyPoison` unless blocked. One `CobraSpat`. Pets are untouched.
5. **Recover** `RecoverSeconds`, then Hold. The cooldown starts at the spit.
6. **Hero Poison.** A stack per landed spit, capped at `maxStacks`; the timer refreshes to the longer; per stack, the fraction × `MaxHp` at apply, strongest wins; pulses every `Poison.PulseSeconds` through `ApplyPulse`.
7. **`ApplyPulse`** is refused while externally invulnerable (roll, wind-up), never checks or starts hit i-frames, is absorbed by shields, can kill, and publishes `PlayerDamaged` with `IsPulse`.
8. **Status** `status.poisoned`: a debuff on the hero's `StatusBoard` with the stacks and remaining time, reposted per stack, cleared at the end. A board refusing debuffs refuses the stack.
9. **Cleared**, stacks and status, by any started Space move (every `MovementSkillKind`), death and `PlayerCombat`'s reset.
10. Enemy poison is unchanged (default cap 5). `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_Guards` | no spec on Cobra; spec on a Chaser; bad numbers / construct / throws |
| `Hold_KeepsBand` | on cooldown, hero at 12 m, then 5 m / tick / moves in, then away |
| `Close_ReadyRearsAt4` | ready, in sight, 8 m / tick / `Rear` at 4 m; one armed event, 30°, 5 m, 0.7 s |
| `Close_NoSightNoRear` | ready, no sight, 3 m / 2 s / never `Rear` |
| `Rear_DirectionLocked` | hero circles during the rear / spit / armed direction unchanged |
| `Spit_HitsInCone` | hero still at 4 m / rear ends / 8 damage, 1 stack, `CobraSpat` hit |
| `Spit_Misses` | 1.2 m back; 35° off; rolling / rear ends / no damage or no stack |
| `Cooldown_FromTheSpit` | spit at t / tick / next rear ≥ t + 3.7 |
| `Poison_StacksToThree` | four spits, each inside 4 s / read / 3 |
| `Poison_PulsesNoIFrames` | 1 stack, 90 HP, just hit / 1 s / 1.8 lost in 2 pulses; i-frames untouched |
| `Poison_RefusedWhileImmune` | wind-up / pulse, apply / nothing lost, 0 stacks |
| `Poison_RefreshThenEnd` | stacks at 0 and 3 s / tick 6.5, 7.1 s / poisoned, then 0 and no status |
| `Poison_StatusShowsStacks` | 2 stacks / board / one debuff, 2 stacks |
| `Poison_SpaceMoveClears` | 3 stacks, each class / start its move / 0, status gone |
| `Poison_PulseFlagAndKill` | 1 HP / pulse / `IsPulse` true, death once; a spit's false |
| `Reset_Clears` | mid-rear, poisoned / both resets / Hold, 0 stacks |
| `Tick_Cycle_NoAlloc` | two spits, poisoned / ticks / `AllocationAssert.None` |
| `Poison*`, `PriestMend*`, `Antlion*` | unchanged / run / green |

No count or coverage test reads `EnemyBehaviourKind`'s length or the status ids (grep, 2026-11-13); one that breaks moves by one and *As built* names it.

## Out of scope

- Model, body, cone, icon (SS-12b–c). Data, roster (SS-12d). Poison from anything else. Elites.

## As built

_Commit body._
