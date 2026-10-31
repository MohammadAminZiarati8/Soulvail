# MK-06a — Enemies can be stunned, rooted and slowed

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Rain, Binding Shot, Snare); AR §18.1 (`EnemySystem` order), §18.4

## Goal

Core can stun, root and slow an enemy for a time, every behaviour obeys it without knowing why, and
views hear about it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/CrowdControl.cs` | Core | **New.** One agent's stun / root / slow timers |
| `Core/Events/EnemyStatusChanged.cs` | Core | **New.** `readonly struct EnemyStatusChanged(int EnemyId, EnemyStatus Status, float Seconds)` (`Seconds` 0 = ended) |
| `Core/Ai/EnemySystem.cs` | Core | `Stun`, `Root`, `Slow`; the dispatch obeys them |
| `Tests/Core/Ai/CrowdControlTests.cs` | Tests.Core | **New** |
| *small edits* | | `EnemyAgent` (owns a `CrowdControl`; reset in `Initialise`), `Docs/Invariants.md` §18.4 (a line for rules 2–3) |

## Public API

```csharp
public enum EnemyStatus { Stunned, Rooted, Slowed }
// EnemySystem:
bool Stun(int enemyId, float seconds);                    // false: dead/unknown
bool Root(int enemyId, float seconds);
bool Slow(int enemyId, float fraction, float seconds);    // fraction in (0, 0.9]
bool Has(int enemyId, EnemyStatus status);
float Remaining(int enemyId, EnemyStatus status);
```

## Behaviour

1. **Bosses take half** of every duration (an agent driven by a `BossSpec`). Elites take full.
2. **Stunned:** the behaviour is **not ticked** — its timers freeze, a wind-up in progress resumes
   where it stopped — and no move or attack intent is produced; contact damage is off. Telegraphs
   already promised still resolve (AR §18.4 "a telegraph is a promise").
3. **Rooted:** the behaviour ticks and may attack; every move it asks for is zeroed, including a
   dash, hop or lunge (the agent stays put while the move plays out).
4. **Slowed:** `MoveSpeed` gains a `PercentMult` of `1 − fraction` for the time; the strongest
   active slow wins; ending removes exactly that modifier.
5. A new duration **refreshes to the longer** of the remaining and the new one; never stacks.
6. Timers count down on `snapshot.Dt` inside `EnemySystem.Tick`, before behaviours.
7. `EnemyStatusChanged` publishes on start, on a refresh that lengthens, and on end (`Seconds` 0);
   death ends all three silently (no event after `EnemyDied`).
8. A stage boundary and a despawn clear everything. Nothing allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Boss_TakesHalf` | Stun 2 s on a boss / 1 s (1) |
| `Stun_FreezesTheBehaviour` | Chaser mid wind-up, stun 1 s / wind-up timer unchanged, no intents; after, resumes (2) |
| `Stun_NoContactDamage` (2) |
| `Root_AllowsAttack_ZeroesMove` | Spitter roots / still fires, no move (3) |
| `Root_StopsALungersDash` (3) |
| `Slow_StrongestWins_EndsCleanly` | 0.3 then 0.5 / ×0.5; after both end, base speed (4) |
| `Refresh_LongerWins` (5) |
| `Events_StartRefreshEnd` / `Death_NoEventAfterDied` (7) |
| `Boundary_Clears` / `Tick_DoesNotAllocate` (8) |

## Out of scope

- Any skill that applies these (MK-07a–MK-09a). Drawing them (MK-06b). Statuses on the player.

## As built

_Filled at merge._
