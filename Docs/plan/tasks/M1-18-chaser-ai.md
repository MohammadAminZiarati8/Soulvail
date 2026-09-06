# M1-18 — `ChaserBehaviour` FSM, `EnemyMoveIntent`, strike damage, telegraph event

**Size:** M · **Depends on:** M1-06, M1-08 · **Branch:** `m1-18-chaser-ai`
**Design refs:** AR §3 (core decides, body executes), §5.2, §9; GD §8.1 (Husk), §9.1 rule 1 (telegraphs); ADR-0005

## Goal

The first enemy that fights back — a Husk that chases, winds up, strikes, and recovers — decided entirely in core over its blackboard, with the body only moving where it's told. Something to dodge.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ChaserBehaviour.cs` | Core | `StateMachine<ChaserState>` over the blackboard |
| `Core/Run/EnemyMoveIntent.cs` | Core | Id, velocity, facing |
| `Tests/Core/Ai/ChaserBehaviourTests.cs` | Tests.Core | Every rule |
| *small edits* | | `EnemyAgent` + `Behaviour` (created from `Spec.Behaviour`); `EnemySystem.Tick(dt, now, PlayerCombat player, IIntentSink intents)` ticks behaviours; `IIntentSink` + `EnemyMove(in)`; `IntentBuffer` list; `RecordingIntents`; `EnemyEvents` + `EnemyTelegraph`; `EnemyView` applies `EnemyMove` (gets a `CharacterController`; trigger capsule kept for queries); `Enemy.prefab`; `Husk.asset` → `Behaviour = Chaser`; `RunTicker` forwards `EnemyMove` intents |

## Public API

```csharp
namespace Soulvail.Core.Ai;

public enum ChaserState { Idle, Chase, Windup, Strike, Recover }

public sealed class ChaserBehaviour
{
    public ChaserBehaviour(EnemyAgent agent);
    public ChaserState State { get; }
    public void Tick(float dt, float now, PlayerCombat player, IIntentSink intents);
    public void Reset();
}
```

```csharp
namespace Soulvail.Core.Run;
public readonly struct EnemyMoveIntent { public readonly int Id; public readonly Vector3 Velocity; public readonly Vector2 FacingXZ; }

// EnemyEvents (added)
public readonly struct EnemyTelegraph { public readonly int Id; public readonly float Duration; }
```

## Behaviour

Numbers from `EnemySpec` (Husk: speed 3.5, contact 8, reach 1.2, windup 0.4, recover 0.6). `bb` = the agent's blackboard, filled by `EnemySystem.Ingest` earlier in the tick.

1. **Idle** → **Chase** when `bb.DistanceToPlayer <= 30`.
2. **Chase**: `dir = bb.PathDirectionToPlayer` if non-zero else `bb.DirectionToPlayer`; emit `EnemyMove(id, dir × speed (XZ→3D), dir)`. → **Windup** when `bb.DistanceToPlayer <= reach`.
3. **Windup**: on enter publish `EnemyTelegraph(id, WindupTime)`; emit `EnemyMove(id, zero, DirectionToPlayer)` (face, don't move); `bb.StateTimer` counts; → **Strike** when timer ≥ `WindupTime`; → **Chase** (cancel) if `bb.DistanceToPlayer > reach × 1.5`.
4. **Strike** (one tick): if `bb.DistanceToPlayer <= reach` → `player.ApplyDamage(contact, now)` (blocked by i-frames as usual); → **Recover**.
5. **Recover**: hold facing, no movement, for `RecoverTime` → **Chase**.
6. A dead agent's behaviour is not ticked (EnemySystem skips `!IsAlive`). Death mid-Windup cancels the strike naturally.
7. `EnemyView` applies `EnemyMove`: `controller.Move(velocity × dt)` with gravity like the player; rotates to `FacingXZ`. Enemies now have a `CharacterController` (radius 0.4, height 1.6) so they collide with walls, pillars, the player, and each other; the trigger capsule remains for cone/charge queries.
8. `Tick` allocates nothing.

## Tests

`ChaserBehaviourTests` build an agent + blackboard directly and feed hand-written perception; a fake `PlayerCombat` is unnecessary — use the real one with a `RecordingEvents`.

| Test | Given / When / Then |
|---|---|
| `Idle_ToChase_WithinThirty` | distance 29 / Tick / `Chase`; distance 31 / stays `Idle` |
| `Chase_EmitsMoveTowardPlayer_AtSpeed` | direction (0.6, 0.8), no path / Tick / `EnemyMove` velocity (2.1, 0, 2.8) |
| `Chase_PrefersPathDirection` | path (1, 0), straight (0, 1) / — / velocity (3.5, 0, 0) |
| `Chase_ToWindup_AtReach` | distance 1.2 / Tick / `Windup`, `EnemyTelegraph(id, 0.4)` published, move velocity zero |
| `Windup_ToStrike_AfterWindupTime` | in Windup / Tick to 0.4 / `Strike` then `Recover` in the same or next tick |
| `Windup_CancelsWhenPlayerLeaves` | distance 1.9 (> 1.8) / Tick / `Chase`, no damage |
| `Strike_DamagesPlayerInReach` | distance 1.0 / through Strike / `PlayerDamaged` ToShield 8 |
| `Strike_MissesOutOfReach` | distance 1.3 at strike / — / no `PlayerDamaged` |
| `Strike_BlockedByIFrames` | player in i-frames / — / `PlayerDamaged{Blocked}` |
| `Recover_ThenChase` | after Strike / Tick 0.6 / `Chase` |
| `Dead_NotTicked` | agent killed / `EnemySystem.Tick` / no intents from it |
| `Tick_AllocatesNothing` | 32 chasers, warm-up / 10 000 system ticks / allocated-bytes delta == 0 |

## Manual verification (Editor)

1. Dummies come at you at a walk (3.5 m/s vs your 5.4) — you can always outrun them.
2. When one reaches you it stops, faces you for 0.4 s (telegraph — add a placeholder: scale pulse or a small marker; **not red yet**, that's the danger colour for M7 VFX to own), then strikes.
3. Step away during the windup → no damage. Stand still → 8 to the shield, then HP.
4. Ring refills once you get clear for 4 s. Standing in a crowd kills you — the death flow from M1-17 runs.
5. Enemies bump each other and the pillars; none pass through walls.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Other archetypes — M2-06+. Elite variants — M7-02.
- Real telegraph VFX and audio — M7.

## As built

_Filled at merge._
