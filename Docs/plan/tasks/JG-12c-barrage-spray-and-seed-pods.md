# JG-12c — Barrage, spray and seed pods

**Size:** M · **Depends on:** JG-12b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 4, 6; GD §12.4

## Goal

Mother Pitcher loops a barrage of five spit lobs (keep moving), a wide spray that cover
blocks (get behind a stone), and a seed pod that drops a Frog.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/MotherPitcherBehaviour.cs` | Core | The loop, three moves |
| `Core/Ai/IBossPhaseListener.cs` | Core | **New.** An inner told of a crossing |
| `Tests/Core/Ai/MotherPitcherAttackTests.cs` | Tests.Core | **New.** Rules 1–8 |
| *small edits* | | `Core/Events/BossEvents.cs` (three spray events); `Core/Ai/BossBehaviour.cs` (rule 6) |

## Public API

```csharp
public interface IBossPhaseListener { void OnPhaseEntered(int phase); }
// MotherPitcherBehaviour : IEnemyBehaviour, IBossPhaseListener
public const int BarrageMove = 1, SprayMove = 2, PodMove = 3;  // EnemyTelegraph.Move
public const float AttackGapSeconds = 2f, RecoverSeconds = 1f;
public const float BarrageTelegraphSeconds = 0.8f, BarrageSpread = 3.5f;
public const int BarrageLobs = 5;
public const float LobSpeed = 7f, LobRadius = 1.6f;             // the Pitcher's spit
public const float SprayTelegraphSeconds = 1f, SprayHalfAngleDeg = 40f;
public const float SprayRange = 12f, SprayDamageScale = 2f;
public const float PodTelegraphSeconds = 0.8f, PodDistance = 3f;
public const int MaxPodFrogs = 3;
public readonly struct SprayArmed { Id, Origin, Direction, HalfAngleDeg, Range, Seconds }
public readonly struct SprayFired { Id, Hit }
public readonly struct SprayCleared { Id }
```

## Behaviour

Stage 10 (10 × `d` 1.315): a lob 13, the spray 26, 29 % of 90 HP (boss cap 50 %).

1. **The loop.** After `AttackGapSeconds` standing: barrage, spray, pod, barrage… A pod while
   `MaxPodFrogs` Frogs live becomes a barrage.
2. **Barrage.** `EnemyTelegraph(id, 0.8, BarrageMove)`, then five lobs: one at the player's position
   then, four `BarrageSpread` round it 90° apart, turned by one Misc draw; each a `Projectile` of
   the agent's spec id, `LobSpeed`, `LobRadius`, `ContactDamage`.
3. **Spray.** `EnemyTelegraph(id, 1.0, SprayMove)` and `SprayArmed` along the aim caught at the start;
   the facing holds it. Then one bite of `ContactDamage × SprayDamageScale` if the player is
   within `SprayRange` and `SprayHalfAngleDeg` of the aim (XZ) **and** `HasLineOfSight`; then
   `SprayFired(id, hit)` and `RecoverSeconds` standing.
4. **Pod.** `EnemyTelegraph(id, 0.8, PodMove)`, then a Frog `PodDistance` out, away from the player.
5. **Every wind-up is at least 0.6 s.**
6. **A crossing drops the move in hand.** `BossBehaviour` calls `OnPhaseEntered` on an inner that
   implements it: an armed spray publishes `SprayCleared`; she restarts from the gap. Lobs in
   flight land. The Warden and Croaker do not implement it.
7. **Rooted:** every intent has zero velocity.
8. **`Tick` allocates nothing.**

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Rule |
|---|---|
| `Mother_Loop_BarrageSprayPod` / `Mother_Pod_SkippedAt3Frogs` | 1 |
| `Mother_Barrage_TelegraphsThenLobsFive` / `Mother_Barrage_LobsAreSpit` | 2 |
| `Mother_Spray_HoldsItsAim` / `Mother_Spray_InTheWedgeInSight_Hits` | 3 |
| `Mother_Spray_BehindCover_Misses` / `Mother_Spray_OutsideTheWedge_Misses` | 3 |
| `Mother_Pod_DropsAFrogAway` | 4 |
| `Mother_EveryWindup_AtLeastPointSix` | 5 |
| `Mother_Crossing_ClearsAnArmedSpray` / `Boss_Crossing_TellsAListeningInner` | 6 |
| `Mother_NeverMoves` | 7 |
| `Mother_Attacks_AllocateNothing` | 8 |

## Manual verification (Editor / device)

None until JG-12h.

## Out of scope

- Drawing the wedge or lob marks (JG-12f), the stones (JG-12g), the assets (JG-12h).

## As built

Built 2026-10-30.

- Rules 1–4: rooted barrage, spray, pod loop; a capped pod takes the barrage slot.
  Barrage reads the release-time player position and rotates four outer lobs with one Misc draw.
  Spray keeps its starting origin/aim, tests XZ range/angle plus sight, then applies one scaled bite.
  Pods spawn through EnemySystem, away from the release-time player direction; only living Frogs
  count. Recovery stands for 1 s, followed by the 2 s gap.
- Rules 5–7: one timer transition per tick preserves the whole announced wind-up on long frames.
  BossBehaviour calls IBossPhaseListener before the crossing beat; Mother clears an armed spray
  immediately, abandons the move and restarts at the barrage gap. Airborne lobs remain untouched.
  Warden/Croaker remain non-listeners. Every Mother intent has zero velocity; JG-12b shield stays.
- Rule 8: AllocationAssert.None exercises all three attacks, including a warmed pooled Frog spawn
  and the spray damage call, using a silent event publisher.
- Tests: all 14 specified test names, 19 cases, mapped by rule in MotherPitcherAttackTests.
- Resolution (rules 2–3 / damage paragraph): keep the existing full-precision ContactDamage and
  DepthScaling arithmetic; the prose's stage-10 13/26 are rounded descriptions, not a new rounding
  operation or a hard-coded damage value.
- Resolution (rule 8): allocation checks use the project's warmed steady-state convention.
  Existing EnemyRegistry.Spawn creates an agent when its free pool is empty; a cold pod spawn
  retains that earlier code's allocation. No registry/pool rewrite outside this task's Files.
- Files outside the table: this footer, PROGRESS/ROADMAP as requested, and Unity .meta sidecars
  for the two new scripts. No views, assets or other gameplay files changed.
- Verification: JG-12c 19/19 pass. Full EditMode: 4,627 pass, 0 fail, 0 skipped,
  1 known inconclusive (includes the 8 concurrent JG-12d model tests). Failing names: none.
  The inconclusive is Animator_AttackSpeedIsUnreachableFromAnEditorClock. Compiler errors 0,
  new analyzer warnings 0; dotnet format whitespace clean across Assets/_Project.
- Manual verification: none until JG-12h, as specified.
