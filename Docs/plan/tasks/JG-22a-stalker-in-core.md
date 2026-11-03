# JG-22a — The Chameleon stalker in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (Revenant), GD §8.2 (owner, 2026-11-02); AR §18.2, §18.4; CC §3.6

## Goal

`EnemyBehaviourKind.Stalker`: it walks to a spot behind the Ranger, goes hidden while it stands
still — the auto-aim cannot pick it — and strikes along a warned lane when the player stops near it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/StalkerSpec.cs` | Core | **New.** The numbers below, validated |
| `Core/Ai/StalkerBehaviour.cs` | Core | **New.** `StalkerState` and the behaviour |
| `Tests/Core/Ai/StalkerBehaviourTests.cs` | Tests.Core | **New.** Rules 1–9 |
| *small edits* | | `EnemySpec` (kind **appended**, `Stalker` block); `EnemyAgent` (`IsHidden`, dispatch); `EnemySystem` (dispatch; damage reveals); `EnemyTickContext` (`PlayerFacing`); `PlayerCombat.BuildCandidates` (hidden is not a candidate); `Events/EnemyEvents.cs` (`EnemyHiddenChanged`) |
| *ripple* | | `RunSession` and `EnemySystem`'s root-gated context pass `PlayerFacing`; `TargeterTests` gains rule 5's row |

## Public API

```csharp
public sealed class StalkerSpec(float behindDistance, float cloakDelay, float strikeRange,
    float behindAngleDegrees, float strikeLength, float strikeSpeed, float strikeHalfWidth,
    float revealSeconds, float strikeCooldown);
public enum EnemyBehaviourKind { …, Shaman, Stalker } // appended: assets store the ordinal
public enum StalkerState { Prowl, Lurk, Windup, Strike, Recover }
public bool EnemyAgent.IsHidden { get; }
public Vector2 EnemyTickContext.PlayerFacing { get; } // PlayerMotor.Facing on XZ
public readonly struct EnemyHiddenChanged { int Id; bool Hidden; }
```

Authored values (JG-22d): behind 3 m, cloak 0.4 s, strike range 4 m, behind ≥ 110°, lane 5 m at
18 m/s, half-width 0.6 m, reveal 1.5 s, cooldown 3 s. Windup is `EnemySpec.WindupTime` (0.45 s).

## Behaviour

1. **Prowl**: goal = player − `PlayerFacing` × `behindDistance`; walks there, visible. Within 0.5 m
   → `Lurk`.
2. **Lurk**: stands. After `cloakDelay` still → hidden (`EnemyHiddenChanged(true)`). The goal
   drifting > 3 m away → `Prowl`, visible.
3. **Strike when behind**: lurking, off cooldown, player within `strikeRange`, and the angle between
   `PlayerFacing` and (stalker − player) ≥ `behindAngleDegrees` → `Windup`: visible, a lane of
   `strikeLength` armed for the windup toward the player, `EnemyTelegraph`.
4. **Strike** crosses the lane at `strikeSpeed`; the lane does `ContactDamage`. Then `Recover`
   (`RecoverTime`), then `Prowl`. Cooldown counts from the strike's end.
5. **Hidden is not a target**: no auto-aim, skill or focus selects it (`BuildCandidates` marks it
   not vulnerable). It still takes damage from anything that reaches it.
6. **Damage reveals**: any damage shows it for `revealSeconds`; while revealed it cannot hide.
7. **Never strikes from the front**: in front of the player it only prowls.
8. **Visible states**: Prowl, Windup, Strike, Recover are never hidden; death clears hidden silently.
9. **No allocation** in `Tick` at 8 stalkers.

## Tests

| Test | Given / When / Then |
|---|---|
| `Prowl_GoesBehindThePlayer` | facing north / settles 3 m south (1) |
| `Lurk_HidesAfterDelay` | still 0.39 s, 0.41 s / visible, hidden, one event (2) |
| `Lurk_DriftReprowls` | player walks 4 m / visible, `Prowl` (2) |
| `Strike_OnlyFromBehind` | 3 m behind; 3 m in front / windup; none (3, 7) |
| `Windup_ArmsLaneAndShows` | windup / lane 5 m, visible, `EnemyTelegraph` (3) |
| `Strike_LaneDamages` | player stays / contact once (4) |
| `Hidden_IsNotTargeted` | only enemy, hidden / targeter picks none (5) |
| `Hidden_StillTakesDamage` | Volley hits / damaged, revealed 1.5 s (5, 6) |
| `Tick_DoesNotAllocate` | 8 agents (9) |

## Out of scope

- Shimmer, bar, clips (JG-22c); asset (JG-22d). Teleports: this one walks.

## As built

_Filled at merge._
