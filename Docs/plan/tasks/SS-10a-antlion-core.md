# SS-10a — The antlion in core: tunnel behind, a ring under the hero, a slow dig

**Size:** M · **Depends on:** — · **Branch:** `ss-10a-antlion-core`
**Design refs:** GD §8.10, §8.1, JG-25a/25h (Gravemaw's burrow and open-ground aim)

## Goal

A new `AntlionBehaviour` runs Dig → Tunnel → Arm → Surfaced → Dig. It cannot be hurt or targeted underground, locks a ring on the hero, erupts, then sits exposed for 2.5 s.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BurrowSpec.cs` | Core | `ShineSpec`'s shape and validation |
| `Core/Ai/AntlionBehaviour.cs` | Core | the cycle |
| `Core/Events/AntlionEvents.cs` | Core | `AntlionPhaseChanged`, `AntlionPhase`, `AntlionRingArmed` |
| `Tests/Core/Ai/AntlionBehaviourTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec`: `BurrowSpec burrow = null` last + property + guard; `EnemyBehaviourKind.Antlion` appended; the behaviour factory's case; Gravemaw's cover-to-open-ground ring move (JG-25h) lifted into a helper both call, if it is not one already (Gravemaw's tests unchanged) |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class BurrowSpec   // finite, > 0; MinTunnelSeconds < MaxTunnelSeconds
{
    public BurrowSpec(float tunnelSpeed, float behindDistance, float lockDistance, float minTunnelSeconds,
        float maxTunnelSeconds, float ringRadius, float armSeconds, float eruptionDamage,
        float surfacedSeconds, float digSeconds);
        // 4.5, 2, 1.2, 1.5, 4, 1.6, 0.8, 16, 2.5, 0.6
    // one get-only property per parameter, same names
}
// EnemySpec.Burrow { get; } — required for Antlion, refused on any other kind.
namespace Soulvail.Core.Events;
public enum AntlionPhase { Dig, Tunnel, Arm, Surfaced }
public readonly struct AntlionPhaseChanged { int Id; AntlionPhase Phase; float Seconds; }
public readonly struct AntlionRingArmed { int Id; Vector3 Centre; float Radius; float Seconds; }
// AntlionBehaviour.State { get; }  AntlionBehaviour.Goal { get; } (the behind point, for tests)
```

## Behaviour

1. **Spawn.** It starts in `Dig` on the surface: stands for `DigSeconds`, can be hit. Then `Tunnel`.
2. **Underground** (`Tunnel`, `Arm`): `IsVulnerable` false and `Health.SetExternalInvulnerable(true)`, written every tick as Gravemaw's guard. Auto-aim, skills and focus skip it. CC and poison do not apply. On leaving `Arm` both are restored.
3. **Goal.** The hero's position minus `BehindDistance` along the hero's movement direction (the position estimate against `ctx.Now`, as Gravemaw's), or along `ctx.PlayerFacing` when the hero moves < 0.5 m/s. It is recomputed every tick until the ring locks.
4. **Tunnel.** It moves at `TunnelSpeed` straight at the goal, ignoring cover and other enemies (the move intent as built). It **locks** when it is within `LockDistance` of the goal after `MinTunnelSeconds`, or at `MaxTunnelSeconds`.
5. **Arm.** The ring's centre is the hero's position at lock. If the ring would overlap cover, it moves to the nearest open ground (the JG-25h rule). One `AntlionRingArmed` event. It tunnels to the centre over `ArmSeconds`, which never ends early.
6. **Eruption.** At the end of `Arm` it stands at the centre. The hero takes `EruptionDamage` only if inside `RingRadius` XZ at that tick. A roll's i-frames and Bond Barrier apply as built. Pets and minions are untouched.
7. **Surfaced.** `SurfacedSeconds`: vulnerable, turns toward the hero at the shaman's turn rate, no attack. A stun or root pauses the clock, and a slow does not. Then `Dig` and rule 1 again, then `Tunnel`.
8. One `AntlionPhaseChanged` on each entry, with that phase's seconds (Tunnel's is `MaxTunnelSeconds`).
9. Death on the surface is normal. It cannot die underground (rule 2). `Reset()` returns to rule 1 with the guard off.
10. Elites: no special case. Its threat cost, XP and priority are data (SS-10d).
11. `Tick` allocates nothing in any state.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_Guards` | Antlion with no burrow; burrow on a Chaser; bad numbers / construct / throws |
| `Spawn_DigsThenTunnels` | new / tick 0.6 s / `Dig` → `Tunnel`; hittable during Dig |
| `Underground_IsUntouchable` | in `Tunnel` / damage, stun, target query / blocked, none applied, not a candidate |
| `Goal_BehindMovingHero` | hero moving +X at 3 m/s / read Goal / 2 m on −X from the hero |
| `Goal_BehindStillHerosFacing` | hero still, facing +Z / read / 2 m on −Z |
| `Tunnel_NoLockBeforeMin` | goal reached at 0.5 s / tick to 1.4 s / still `Tunnel` |
| `Tunnel_LocksNearGoal` | within 1.2 m after 1.5 s / tick / `Arm`, one `AntlionRingArmed` on the hero, r 1.6, 0.8 s |
| `Tunnel_LocksAtMax` | hero outrunning / 4 s / `Arm` |
| `Arm_RingAvoidsCover` | hero against a rock / lock / centre on open ground, ring clear of cover |
| `Eruption_HitsInsideRing` | hero still / end of Arm / 16 damage once |
| `Eruption_MissesOutside` | hero walks 2 m in the arm / end / no damage |
| `Eruption_IgnoresPets` | hound in the ring / erupt / hound untouched |
| `Surfaced_ExposedThenDigs` | erupted / 2.5 s / hittable, no attack; then `Dig` 0.6 s, then `Tunnel` |
| `Surfaced_StunHoldsClock` | 1 s stun at 1 s / tick / surfaced ends at 3.5 s |
| `Events_OnePerPhase` | full cycle / record / Dig, Tunnel, Arm, Surfaced, Dig in order, with seconds |
| `Reset_Underground_Cleans` | in `Tunnel` / `Reset` / `Dig`, vulnerable |
| `Tick_Cycle_NoAlloc` | two cycles / ticks / `AllocationAssert.None` |
| `Gravemaw*` | unchanged / run / green |

## Out of scope

- Model, body, ripple, ring drawing (SS-10b–c). Data and roster (SS-10d).
- Attacks on pets. A surfaced bite. Elites.

## As built

_Commit body._
