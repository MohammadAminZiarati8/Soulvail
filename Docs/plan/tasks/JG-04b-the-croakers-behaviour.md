# JG-04b — Old Croaker's behaviour

**Size:** S · **Depends on:** M4-02 (`WardenBehaviour`, the pattern) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1; AR §18

## Goal

`CroakerBehaviour`: the boss's fighting, in core. It approaches and hop-slams. Each attack's telegraph
now says *which* move it is, so the body can play the right clip.

## Why this shape

- **The hop-slam is built from hazards that already exist.** The landing spot is a `FissureSystem`
  circle armed at take-off and fired on landing. The impact is a `ShockwaveSystem` ring from the landing
  point. Both are telegraphed, tested and drawn already.
- **`EnemyTelegraph` gains `Move`** (int, default 0). Every existing publisher sends 0, so every
  existing body is unchanged. The Croaker sends 1 for `Slam`, 2 for `Lash` (JG-04e) and 3 for `Croak`.

## Files

| Path (under `Assets/_Project/`) | Purpose |
|---|---|
| `Core/Ai/CroakerBehaviour.cs` | **New.** `CroakerState { Idle, Approach, Slam, Croak, Recover }` and the behaviour |
| `Tests/Core/Ai/CroakerBehaviourTests.cs` | **New.** Rules 1–6 |
| *small edits* | `Core/Events/EnemyEvents.cs` (`EnemyTelegraph.Move`) |

## Behaviour

1. **Approach** at the archetype's speed until the player is within `SlamRange` (8 m) and the slam is
   off cooldown (5 s).
2. **Slam** publishes `EnemyTelegraph(id, 0.9 s, Move 1)` and arms a fissure of radius 2.5 m at the
   player's position *now*. The Croaker's prediction is none; prediction is the Strangler's lesson.
   Telegraph ≥ 0.6 s (GD §9.1 rule 1).
3. **On landing** (end of the telegraph) the agent is placed at the landing point, the fissure fires,
   and a shockwave emits from it (speed 7 m/s, max radius 6 m).
4. **Phases chain the hop.** Phase 1 does 1 slam per attack, phase 2 does 2, phase 3 does 3, with
   0.4 s between landings. Each hop gets its own telegraph and fissure.
5. **Croak** happens on entering a phase. It publishes `EnemyTelegraph(id, 1.0 s, Move 3)` once the
   beat ends. The adds themselves are `BossBehaviour`'s summons and are not called again here.
6. **Recover** for 1.2 s after the last landing; it can be hit from every side. The safe answer is to
   step out of the circle, then out of the ring's path (rule 2).
7. All draws come from the `IRandom` passed in (`Misc`), and `Tick` allocates nothing.

## Tests

| Test | Rule |
|---|---|
| `Approach_StopsAtSlamRange` | 1 |
| `Slam_TelegraphsMove1_AndArmsAFissureWhereThePlayerStood` | 2 |
| `Landing_MovesTheBody_FiresTheFissure_EmitsAShockwave` | 3 |
| `Phase2_HopsTwice_Phase3_HopsThrice` | 4 |
| `PhaseEntry_CroaksAfterTheBeat` | 5 |
| `Recover_LastsItsTime_ThenApproaches` | 6 |
| `Tick_AllocatesNothing` | 7 |
| `EnemyTelegraph_MoveDefaultsToZero` | Why this shape |

## Out of scope

Wiring it to a boss id (JG-04c); the tongue lash (JG-04e); any view.

## As built

_2026-10-26._ **Full EditMode suite: 4 423, 4 421 passed / 1 failed / 1 inconclusive**, the two known
(`CharacterPreviewTests.Resize_…`, `PlayerAnimatorViewTests.Animator_AttackSpeed…`); 8 / 8 new rows
green. Zero compile errors or warnings; `dotnet format whitespace` clean on the three files. **Run in a
batch-mode Unity (`-runTests`, TestRunnerApi underneath)**, because the Editor was closed and the MCP
unconnected; the project lock was free.

- **The body is not teleported; the hop carries it.** Core cannot place a body (the snapshot owns
  positions, AR §3), so rule 3's "placed at the landing point" is the move intent: every airborne tick
  sends `(landing − take-off) / 0.9 s` on the ground plane, facing the landing, so a body that follows
  its intents arrives as the slam lands. The ring leaves from the fissure's centre, not from
  `_agent.Position`, so a body stopped short by a wall still slams where the circle is.
- **The phase is read off `agent.Behaviour as BossBehaviour`.** The boss factory builds the inner
  before the `BossBehaviour` exists, and the Files table kept `BossBehaviour` untouched. Driven bare,
  it is phase 0: one hop, no croak. A phase change seen on the first tick after a beat moves it to
  Croak from any state (an interrupted hop's fissure still fires on its own clock); a second crossing
  during a croak restarts the call.
- **Hops = phase index + 1, capped at `MaxHops` 3**, fixed at the first take-off; the 5 s cooldown runs
  from the first take-off too. A chain is one `Slam` state with an airborne flag and the 0.4 s gap.
- **Telegraphs are constants (0.9 / 1.0 s), not `EnemySpec.WindupTime`**, because they are the clip's
  frames. Inside `SlamRange` on cooldown it sits, the Warden's rule. Recover is a constant 1.2 s.
- **The `IRandomStream` is guarded but not held**: nothing draws until JG-04e, and a never-read field
  is a warning. `EnemyTelegraph`'s new `move` parameter defaults to 0, so no call site changed.
