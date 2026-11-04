# JG-30a — The Apex in core: hunt, dash lane, rake

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 6; GD §9.2 (stage 30); Warlord charge (`WarlordBehaviour`, `LaneSystem`, `ILaneStopListener`)

## Goal

A fast, always-visible boss that runs you down, slashes along warned lanes that stop at stones, and
rakes you if you stay close.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ApexBehaviour.cs` | Core | **New.** Inner boss behaviour: hunt, dash, rake |
| `Core/Ai/ApexStats.cs` | Core | **New.** Every number below, as `Stat`s |
| `Tests/Core/Ai/ApexBehaviourTests.cs` | Tests.Core | **New.** Hunt, pick, rake |
| `Tests/Core/Ai/ApexDashTests.cs` | Tests.Core | **New.** Lane, probe, stone |
| *small edits* | | `RunSession` boss factory gains `boss.apex` → `ApexBehaviour(agent, new ApexStats(), _random.Misc, _lanes)` |

## Public API

```csharp
public enum ApexMove { None, Dash = 1, Rake = 2, Pounce = 3, Feint = 4 }   // 3, 4 are JG-30b's
public sealed class ApexBehaviour : IEnemyBehaviour, IBossPhaseListener, ILaneStopListener
{
    public ApexBehaviour(EnemyAgent agent, ApexStats stats, IRandomStream random, LaneSystem lanes);
    public ApexMove Move { get; }
    public bool IsRecovering { get; }
    public void OnLaneStop(bool hit, float distance);
    public void OnPhaseEntered(int phase);   // drops an armed move; JG-30c adds the rest
}
// ApexStats: RunSpeed 5.5, MoveGapSeconds 1.0,
//   DashTelegraphSeconds 0.7, DashMinRange 4, DashLength 10, DashHalfWidth 0.9, DashSpeed 22,
//   DashDamageScale 1.5, DashRecoverSeconds 0.8, DashCooldownSeconds 3.5,
//   RakeRange 3, RakeWindupSeconds 0.6, RakeSecondSeconds 0.4, RakeReach 2.6, RakeHalfAngleDeg 50,
//   RakeDamageScale 1, RakeRecoverSeconds 0.6
```

## Behaviour

1. **Hunt.** With no move armed and not recovering, she runs straight at the player at `RunSpeed`,
   facing them. She is never hidden and never leaves auto-aim.
2. **Pick.** After `MoveGapSeconds` since the last move ended: within `RakeRange`, rake; else at
   `DashMinRange` or more with the dash off cooldown, dash; else keep hunting. One move at a time.
3. **Dash lane.** She stops, aims at the player's position and sends a `LaneProbeIntent` of
   `DashLength`. The answer, or two ticks without one, opens a `LaneSystem` lane of that length
   (shortened to a stone), half-width `DashHalfWidth`, firing at `DashTelegraphSeconds`, damage
   `ContactDamage × DashDamageScale`. Then she dashes along it at `DashSpeed` and recovers.
4. **Stones stop her.** A probe that hits stops lane and dash at the stone. No daze, no stun: she
   recovers as normal. Behind a stone is a safe answer.
5. **Rake.** She stands and publishes `EnemyTelegraph(id, 1.0, 2)`, covering both swipes. At
   `RakeWindupSeconds` and again `RakeSecondSeconds` later, a `ConeHitIntent` of `RakeReach`,
   ±`RakeHalfAngleDeg`, damage `ContactDamage × RakeDamageScale`. Stepping back or to the side
   answers it.
6. **Phase entry drops an armed move** (lane already opened stays to fire; she doesn't dash it).
7. **Every telegraph ≥ 0.6 s.** No allocation per tick. `Reset` clears every timer.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hunt_RunsAtPlayer_AtRunSpeed_NeverHidden` (1) |
| `Pick_Close_Rakes` / `Pick_Far_DashOffCooldown_Dashes` / `Pick_DashOnCooldown_Hunts` / `Pick_WaitsMoveGap` (2) |
| `Dash_ProbesThenOpensLane_FiresAtPointSeven_ThenDashes` / `Dash_NoProbeAnswer_OpensAfterTwoTicks` (3) |
| `Dash_ProbeHitsStone_LaneAndDashStopAtStone_NoDaze` (4) |
| `Rake_TelegraphsOneSecond_TwoConesPointFourApart` / `Rake_PlayerOutsideCone_Unhurt` (5) |
| `PhaseEntry_DropsArmedMove` (6) |
| `Telegraphs_AtLeastPointSix` / `Tick_DoesNotAllocate` / `Reset_ClearsTimers` (7) |

## Out of scope

- Pounce and feint (30b); phases (30c); the look (30e); data and stage (30f).
