# EA-01b — Echo Arrow in core

**Size:** S · **Depends on:** EA-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Echo Arrow); RS-03b (the shot queue)

## Goal

Every Nth basic arrow is loosed again by a shadow 0.15 s later, at the same target.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/PlayerCombat.cs` | Core | Count basic arrows in `QueueShot`; a fixed queue of pending echoes |
| `Core/Effects/PlayerStat.cs` | Core | Append `EchoEvery` (base 0), `EchoDamage` (base 0) |
| `Tests/Core/Combat/EchoArrowTests.cs` | Tests.Core | **New** |
| *small edits* | | `ShotOrigin` (append `Echo`), `CombatantStats`, stat coverage tests |

## Public API

```csharp
public enum ShotOrigin { Ranger, Shade, Echo }
// PlayerCombat gains:
public const float EchoDelay = 0.15f;
public const int MaxPendingEchoes = 2;
```

## Behaviour

1. With `EchoEvery` ≥ 1, every basic arrow (not Volley, not a skill's arrow, not an echo) adds 1
   to a counter. When it reaches `round(EchoEvery)`, it resets to 0 and an echo is scheduled for
   `now + EchoDelay`. `EchoEvery` < 1 → off, and the counter stays 0.
2. The echo remembers the original's target id and damage (crit included). When due, if that enemy
   is alive, it is queued as a shot from the player's current position, led at the target, for
   `damage × EchoDamage`, `Origin = Echo`; if it died, the echo is dropped.
3. The echo fires whether the Ranger is standing, moving or rolling. It counts as a player arrow
   hit (kills, XP), and it never feeds Steady Aim or the echo counter.
4. More than `MaxPendingEchoes` pending → the new one is dropped.
5. **Ranks** (for EA-01c): 1 — `EchoEvery` Flat +4, `EchoDamage` +0.6; 2 — `EchoEvery` −1;
   3 — `EchoDamage` +0.2; 4 — `EchoEvery` −1.
6. A stage boundary, death or run reset clears the counter and the pending echoes. No allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rank1_FourthArrowEchoes` | 8 basic arrows / 2 echoes, after arrows 4 and 8 (1, 5) |
| `Off_NoEcho` / `Volley_NotCounted` (1) |
| `Echo_AfterDelay_SameTarget_SixtyPercent` / `Echo_TargetDead_Dropped` (2) |
| `Echo_FiresWhileMoving` / `Echo_NotCountedForSteadyAim` (3) |
| `Pending_OverCap_Dropped` (4) |
| `Rank4_EveryOther_EightyPercent` (5) |
| `StageBoundary_ClearsPending` / `Tick_DoesNotAllocate` (6) |

## Out of scope

- Assets (EA-01c); the echo arrow's tint (EA-02).

## As built

_Filled at merge._
