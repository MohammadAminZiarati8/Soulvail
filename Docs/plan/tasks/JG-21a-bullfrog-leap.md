# JG-21a — The Bullfrog's leap in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 (owner, 2026-11-02); AR §18.2

## Goal

A Chaser with a `LeapSpec` closes the gap: it crouches, a landing spot is fixed and announced, it
flies there and bites everything in a ring where it lands.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/LeapSpec.cs` | Core | **New.** The numbers below, validated |
| `Tests/Core/Ai/BullfrogLeapTests.cs` | Tests.Core | **New.** Rules 1–8 |
| *small edits* | | `EnemySpec` (`Leap`, optional, honoured by Chaser only); `ChaserBehaviour` (`Crouch`, `Air`, `Land` states); `Events/EnemyEvents.cs` (`EnemyLeapAimed`) |

## Public API

```csharp
public sealed class LeapSpec(float minRange, float maxRange, float crouchSeconds,
    float airSeconds, float radius, float damageMultiplier, float cooldown);
public enum ChaserState { Idle, Chase, Windup, Strike, Recover, Crouch, Air, Land } // appended
public readonly struct EnemyLeapAimed { int Id; Vector3 From; Vector3 Target; float Radius;
    float CrouchSeconds; float AirSeconds; }
public LeapSpec EnemySpec.Leap { get; } // null = no leap
```

Authored values (JG-21f): min 4 m, max 9 m, crouch 0.6 s, air 0.5 s, radius 1.6 m, ×1.5, cooldown 4 s.

## Behaviour

1. **When**: in `Chase`, leap off cooldown, player between `minRange` and `maxRange` → `Crouch`.
   Cooldown counts from the landing; the first leap is ready 1 s after the chase starts.
2. **Crouch**: stands facing the player for `crouchSeconds`. The target is the player's position at
   the crouch's start, never updated. `EnemyLeapAimed` once, at the start; `EnemyTelegraph`
   (move 1) for the crouch.
3. **Air**: moves in a straight line to the target over exactly `airSeconds` (velocity = distance ÷
   air time), facing it. No contact damage in the air.
4. **Land**: once, on arrival: the player within `radius` (XZ) takes `ContactDamage × multiplier`.
   Then `Recover` (`RecoverTime`), then `Chase`.
5. **The dodge is leaving the ring**: a player who walks out during crouch or air takes nothing.
6. **A stun or root mid-air** lands it where it stopped; the bite still resolves there.
7. **No `LeapSpec`** = the Chaser exactly as before (Frog and Husk tests unchanged).
8. **No allocation** in `Tick` at 16 Bullfrogs.

`LeapSpec` refuses: non-positive values (cooldown may be 0), `minRange > maxRange`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Leap_StartsOnlyInBand` | player 3 m, 6 m, 12 m / crouch only at 6 m (1) |
| `Leap_WaitsForCooldown` | lands, player at 6 m / no crouch for 4 s (1) |
| `Crouch_FixesTargetAndAnnounces` | player moves mid-crouch / target unchanged, one `EnemyLeapAimed` (2) |
| `Air_ArrivesInAirSeconds` | 7 m leap / at target after 0.5 s ± one tick (3) |
| `Land_BitesInsideRing` | player stays / takes 1.5× contact once (4) |
| `Land_MissesOutsideRing` | player steps 2 m away / no damage (5) |
| `Air_StunLandsWhereStopped` | stun at half air / bite resolves at the stop (6) |
| `NoLeapSpec_IsTheOldChaser` | Frog spec / never crouches (7) |
| `Tick_DoesNotAllocate` | 16 agents (8) |

## Out of scope

- The ring, the clip and the arc (JG-21e); the asset (JG-21f). Leaping over thorn walls: walls
  block only the player.

## As built

_Filled at merge._
