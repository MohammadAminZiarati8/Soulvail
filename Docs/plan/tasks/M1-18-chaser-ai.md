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

**Built ahead of [M1-17](M1-17-hud.md), which the ROADMAP ordered first.** M1-17's required test (`PlayerDied_EndsRun`) and three of its four manual steps need something that damages the player, and until this task there was nothing: `RunState.Combat` is `internal`, `IDomainEvents` is publish-only, `IPlayerCommands` has no damage verb and `Soulvail.Tests.Core` has no `InternalsVisibleTo`. This task damages the player from *inside* core (rule 4 calls `player.ApplyDamage` directly), needs no new port to do it, and depends only on M1-06 and M1-08 — so swapping the two costs nothing and removes the problem instead of working around it. M1-17 now lists 18 as a dependency. The only casualty is this spec's manual step 4, which mentions M1-17's death flow: that half is verified when the HUD lands, one PR later.

**Six deviations, all of shape rather than behaviour:**

1. **`ChaserBehaviour.Tick` takes a fifth argument, `IDomainEvents events`.** The spec's Public API block omits it while rule 3 requires publishing `EnemyTelegraph`, so the two could not both be satisfied. Events ride in per tick beside `player` and `intents` rather than being held from the constructor, because `EnemySystem` already owns the reference and an agent-held one would have to be threaded through `EnemyRegistry.Spawn`. `EnemySystem.Tick`'s signature is the spec's, unchanged.
2. **An `EnemyMoveIntent` is emitted in every state, including `Idle`.** The spec only asks for one in Chase and Windup. `EnemyView` applies gravity inside the same `CharacterController.Move` that carries the walk, so a tick with no intent is a tick an enemy is not pinned to the floor by — and an unconditional cadence is the same promise `RunSession.TickBody` makes for the player: one instruction per tick, never none.
3. **`Strike` is entered on the tick the windup completes and left on the next**, so it is observable for exactly one tick rather than being an invisible edge. The damage lands in `OnEnter`, which is the frame the player was watching.
4. **The windup cancel is tested before the timer.** Rule 3 lists them the other way. A player who leaves on the same tick the windup completes has beaten it; the other order lets a swing they had already escaped resolve and then miss on rule 4's reach test anyway — the same outcome by a route that looks like a bug.
5. **`EnemyView.Knockback` was converted from a transform move to a controller move.** M1-15 left a note in that method saying this task would do it, and it is what stops a shoved Husk sliding through a wall now that there is a controller to resolve against. A shove outranks a walk: `Apply` returns without moving while one is playing, so the body is never pushed by two things in one frame.
6. **`EnemyHitFeedback` gained the telegraph swell** — a sixth touched file, beyond the spec's list. Manual step 2 asks for a placeholder telegraph, and that component is the enemy's cosmetic listener; putting a pulse in `EnemyView` would mix the dumb body with feedback that already has an owner. A shape change and not a colour one, because saturated red-orange is reserved for danger (GD §16.4) and that palette is M7's.

**Also touched by consequence, not by design:** `EnemySystemTests` (the `Tick` signature gained two arguments), `ChargeIntegrationTests` and `ConeHitsToDamageTests` (their private `SilentIntents` fakes had to implement the new port member), and `EnemyDefinitionTests` (its `Husk_ToSpec_MatchesGameDesign` row asserted `Static`, which this task's asset edit flips to `Chaser`).

**Verified:** 427 EditMode green (414 before, so 13 this task) and 3 PlayMode green, zero errors, zero new analyzer warnings — the five Console warnings are the pre-existing `Debug.LogWarning`s the content-validation and installer fixtures raise on purpose, unchanged in number and origin. Measured in play mode in the Run scene: a Husk closes at **3.50 m/s exactly**, stops at **1.13 m** (reach 1.2, one tick inside), swells over **0.4 s** to ~1.15 and snaps back in a single frame, then repeats on a **1.00 s** cycle (0.4 windup + 1 tick + 0.6 recovery). All eight dummies were killed by the player's own Censer within 8 s, and the bodies stayed grounded (`y` 0.02–0.04) throughout.

**Manual step 5 confirmed by the owner:** the Husks bump into each other rather than passing through. That was the one claim in this task no probe could settle — the `CharacterController` added beside the trigger capsule is doing its job — and it is what makes the crowd of M1-19 and M2 a crowd rather than a stack. Steps 1–3 are the measurements above. Step 4's second half waits for M1-17, which is the only thing that will put a number on screen.
