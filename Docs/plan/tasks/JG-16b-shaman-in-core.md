# JG-16b — The Moss Shaman in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (Choir), GD §8.2; AR §18.2

## Goal

`EnemyBehaviourKind.Shaman`: never attacks; stays behind a shield-bearer at the Ranger's range edge,
pulses heal and haste to allies, flees when the player closes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ShamanSpec.cs` | Core | **New.** The numbers below, validated |
| `Core/Ai/ShamanBehaviour.cs` | Core | **New.** `ShamanState` and the behaviour |
| `Core/Events/ShamanEvents.cs` | Core | **New.** `ShamanCast`, `ShamanPulsed`, `EnemyHealed` |
| `Tests/Core/Ai/ShamanBehaviourTests.cs` | Tests.Core | **New.** Rules 1–8 |
| *small edits* | | `EnemySpec` (kind, `Shaman` block); `EnemyStatus.Hasted`; `CrowdControl` (haste); `EnemySystem.Haste`, dispatch |
| *ripple* | | `EnemyStatusView` ignores `Hasted` until JG-16d |

## Public API

```csharp
public sealed class ShamanSpec(float auraRadius, float pulseInterval, float castSeconds,
    float healFraction, float hasteFraction, float hasteSeconds, float holdDistance,
    float behindDistance, float fleeDistance, float fleeSeconds, float fleeSpeedMultiplier);
public enum ShamanState { Reposition, Cast, Flee }
public sealed class ShamanBehaviour : IEnemyBehaviour { ShamanState State; int AnchorId; } // -1 = none
public readonly struct ShamanCast { int ShamanId; float Seconds; }
public readonly struct ShamanPulsed { int ShamanId; Vector3 Position; float Radius; int Touched; }
public readonly struct EnemyHealed { int EnemyId; float Amount; float HpFraction; }
public bool EnemySystem.Haste(int enemyId, float fraction, float seconds);
```

Authored values (JG-16e): aura 8 m, interval 3 s, cast 0.6 s, heal 0.10, haste 0.25 for 3.5 s,
hold 11 m, behind 2.5 m, flee 4 m for 1.2 s at 1.4×.

## Behaviour

An **ally** is a living agent that is not this one, not a Shaman and not a boss.

1. **Anchor**: re-picked every 0.5 s and when it dies: the nearest ally within 12 m that is a
   Bulwark, else the nearest ally within 12 m, else none.
2. **Reposition**: goal = `behindDistance` past the anchor, away from the player; no anchor =
   `holdDistance + 1` m from the player. Never nearer than `holdDistance`. Stands within 0.4 m;
   faces the player.
3. **Cast**: from 2 s after spawn, every `pulseInterval`, with an ally in the aura: stands
   `castSeconds`, `ShamanCast` at the start. No ally: the timer waits.
4. **Pulse** at the end: each ally in the aura heals `healFraction` of max HP (`EnemyHealed` unless
   full) and gets `Haste`; one `ShamanPulsed` with the count.
5. **Flee**: player within `fleeDistance` ends a cast unpulsed; runs away at the multiplier for
   `fleeSeconds`, then Reposition.
6. **It never damages the player.**
7. **Haste**: `+fraction` move speed, strongest wins, refreshes, ends, publishes
   `EnemyStatusChanged(Hasted)`; stacks with a slow.
8. **No allocation** in `Tick` at 16 agents.

## Tests

| Test | Given / When / Then |
|---|---|
| `Anchor_PrefersBulwark` | Frog 3 m, Bulwark 9 m / Bulwark (1) |
| `Anchor_NonePast12` | ally 13 m / −1 (1) |
| `Moves_BehindAnchor` | settle / 2.5 m past it (2) |
| `Moves_NeverInsideHold` | anchor 6 m off / ≥ 11 m (2) |
| `Moves_AloneAt12` | no ally / 12 ± 0.4 m (2) |
| `Cast_WaitsForAlly` | ally 9 m, 5 s / no cast (3) |
| `Cast_StandsThenPulses` | ally 5 m / still 0.6 s, `ShamanPulsed(1)` (3, 4) |
| `Pulse_HealsAndHastes` | ally 50 % / 60 %, `EnemyHealed`, Hasted 3.5 s (4) |
| `Pulse_SkipsNonAllies` | self, Shaman, boss / untouched (4) |
| `Flee_CancelsCast` | player 3 m mid-cast / no pulse, away at 1.4× (5) |
| `NeverDamagesPlayer` | touching 10 s / HP full (6) |
| `Haste_StrongestWinsEnds` | 0.25, 0.1 / +25 %, then base (7) |
| `Tick_DoesNotAllocate` | 16 agents (8) |

## Out of scope

- JG-16c–e. Allies do not block arrows: hiding is about range, not cover.

## As built

_Filled at merge._
