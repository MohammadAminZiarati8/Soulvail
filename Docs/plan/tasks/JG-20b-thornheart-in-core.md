# JG-20b — Thornheart in core: rooted, spokes of thorn, the slam that bares its heart

**Size:** M · **Depends on:** JG-20a · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 6; GD §9.2; `MotherPitcherBehaviour` (rooted, `DamageTaken`)

## Goal

A boss that never moves and can always be shot grows thorn spokes and slams a root down a line,
baring its heart to double damage.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ThornheartBehaviour.cs` | Core | **New.** Rotation, spokes, slam, exposed heart |
| `Core/Events/ThornheartEvents.cs` | Core | **New.** `HeartExposed` |
| `Tests/Core/Ai/ThornheartBehaviourTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession._bossBehaviours` row `boss.thornheart` passing `_thorns`, `_random.Misc`; `BossDispatchTests` row |

## Public API

```csharp
public enum ThornheartMove { None, Roots = 1, Slam = 2 }   // EnemyTelegraph kinds 1, 2
public sealed class ThornheartBehaviour : IEnemyBehaviour, IBossPhaseListener {
  public const float AttackGapSeconds = 1.6f, RecoverSeconds = 0.6f;
  public const float RootTelegraphSeconds = 0.9f, RootInner = 2f, RootOuter = 12f,
                     RootHalfWidth = 0.6f, RootStandSeconds = 12f, RootDamageScale = 1f,
                     RootAimJitterDeg = 20f;
  public const int RootSpokes = 3;
  public const float SlamTelegraphSeconds = 1f, SlamLength = 14f, SlamHalfWidth = 1.2f,
                     SlamDamageScale = 1.5f, SlamStandSeconds = 3f,
                     ExposedSeconds = 3f, ExposedDamageTaken = 2f;
  public ThornheartBehaviour(EnemyAgent agent, ThornSystem thorns, IRandomStream random);
  public ThornheartMove Move { get; }  public bool IsExposed { get; }
}
// ThornheartEvents.cs: HeartExposed(BossId, Open, Seconds)
```

## Behaviour

1. **Rooted and open:** every tick a stand-still move; `IsVulnerable` always true, never externally
   invulnerable.
2. **Rotation:** Roots → Slam → Roots → Slam…, each move's telegraph, then `RecoverSeconds`, then
   `AttackGapSeconds`. Each telegraph publishes `EnemyTelegraph(id, seconds, kind)`.
3. **Roots:** spoke 0 points at the player's bearing plus one draw in ±`RootAimJitterDeg`; the rest
   are spaced evenly round 360°. Each opens a wall from `RootInner` to `RootOuter` out of the boss,
   arm = `RootTelegraphSeconds`, damage `ContactDamage × RootDamageScale`.
4. **Slam:** the bearing is fixed at telegraph start; one wall `SlamLength` long, arm =
   `SlamTelegraphSeconds`, damage `× SlamDamageScale`, standing `SlamStandSeconds`.
5. **Exposed:** on the slam's `ThornRose` (hit or miss) `DamageTaken` gains a `PercentMult` of
   `ExposedDamageTaken − 1` owned by the behaviour for `ExposedSeconds`; `HeartExposed` open, then
   closed.
6. **A refused wall** (`Open` returns 0) skips that spoke; the move still runs its clock.
7. **CC:** a stunned boss isn't ticked, so the rotation and exposed clocks pause; an armed wall still
   rises on its own clock (a telegraph is a promise).
8. **Phase entry** drops the move, closes the heart and calls `ThornSystem.WitherAll`. **Reset**
   removes the modifier and returns to the rotation's start.
9. Registered for `boss.thornheart`. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rooted_NeverMoves_AlwaysVulnerable` (1) |
| `Rotation_RootsSlamAlternate_WithGaps` / `Telegraphs_PublishKinds` (2) |
| `Roots_ThreeSpokes_FirstAtPlayerWithinJitter_EvenlySpaced` / `Roots_SpanInnerToOuter` (3) |
| `Slam_BearingFixedAtTelegraph_LengthAndDamage` (4) |
| `Exposed_OnRise_DoubleDamage_ForThreeSeconds` / `Exposed_Miss_StillOpens` (5) |
| `Roots_SystemFull_SkipsSpoke` (6) |
| `Stun_PausesRotationAndExposed` — session (7) |
| `Phase_WithersAll_ClosesHeart` / `Reset_RemovesModifier` (8) |
| `Dispatch_ThornheartBossId_Builds` / `Tick_DoesNotAllocate` (9) |

## Out of scope

- Pods (20c); phases' changes (20d); everything Unity (20e–h).

## As built

_Filled at merge._
