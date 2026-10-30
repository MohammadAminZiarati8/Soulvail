# JG-14a — The shell turns to face you

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (the Warden), GD §5.4, GD §9.1 rule 1

## Goal

The brain of stage 11's Warden-role **Mossguard**. The Warden of Ash has no front shield
to reuse; the boss beat's pair is reused: `Health.SetExternalInvulnerable` blocks, `IsVulnerable`
false keeps aim off and shows the blocked glyph (GD §5.4). Walking round it at 3 m (61°/s) loses to
75°/s; a roll, or walking round in its recover, wins.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BulwarkSpec.cs` | Core | **New.** |
| `Core/Ai/BulwarkBehaviour.cs` | Core | **New.** Approach → Windup → Bash → Recover |
| `Tests/Core/Ai/BulwarkBehaviourTests.cs` | Tests.Core | **New.** |
| *small edits* | | `Core/Content/EnemySpec.cs` (`Bulwark` appended; optional `BulwarkSpec bulwark`); `Core/Ai/EnemyAgent.cs` (dispatch); `Core/Ai/EnemySystem.cs` (`Tick` switch; `ApplyDamage` passes `result.Blocked`); `Core/Events/EnemyEvents.cs` (`EnemyDamaged.Blocked`, optional trailing) |

## Public API

```csharp
public enum EnemyBehaviourKind { /* … */ Lunger, Bulwark } // appended
public sealed class BulwarkSpec
{
    public BulwarkSpec(float shieldHalfArcDegrees, float turnRateDegrees, float walkConeDegrees);
    public float ShieldHalfArcDegrees { get; } // (0, 180)
    public float TurnRateDegrees { get; }
    public float WalkConeDegrees { get; }      // (0, 90]
}
public sealed class BulwarkBehaviour : IEnemyBehaviour
{
    public BulwarkBehaviour(EnemyAgent agent);
    public void Tick(in EnemyTickContext ctx);
    public void Reset();
}
```

Numbers (JG-14d): arc ±60°, turn 75°/s, cone 45°, move 1.6, reach 1.6, wind-up 0.7 s,
recover 1.5 s, damage 16.

## Behaviour

1. **Shielded** while the player's XZ bearing is within the half-arc of its facing: invulnerable,
   `IsVulnerable` false; else both off. Set every tick, after facing moves.
2. **A shielded hit is turned away**: HP unchanged, `EnemyDamaged.Blocked` true. Others land.
3. **Approach.** Facing turns toward the path (else straight) by ≤ `TurnRate × dt`; it walks along
   its facing at `MoveSpeed` only while the path is in the walk cone, else stands and turns.
4. **`Reset`**: approaching, unshielded, no cooldown; its first tick faces the player outright.
5. **Wind-up.** In `Reach`, player in the arc, off cooldown: stands, `EnemyTelegraph(id, WindupTime)`.
   Facing freezes until Recover ends.
6. **Bash.** At the wind-up's end, a player in `Reach` and inside the arc takes `ContactDamage` once.
7. **Recover** `RecoverTime` on its frozen front, then approach; `NextAttackAt` = bash + recover.
8. **One intent a tick** in every state; standing sends zero velocity.
9. **Refused at build:** no `BulwarkSpec`, or wind-up < `WardenBehaviour.MinTelegraphSeconds`.
10. **`Tick` allocates nothing.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Bulwark_PlayerInFront_IsShielded` | 30° off / both flags raised (1) |
| `Bulwark_PlayerBeside_IsOpen` | 90° off / lowered (1) |
| `Bulwark_ShieldedHit_IsBlocked` | `ApplyDamage` 20 / HP full, `Blocked` true (2) |
| `Bulwark_HitFromBehind_Lands` | `ApplyDamage` 20 / HP −20, `Blocked` false (2) |
| `Bulwark_TurnsAtItsRate` | player behind, 0.5 s / +37.5° ± 1 tick (3) |
| `Bulwark_PathOutsideCone_Stands` | path 90° off / halted (3) |
| `Bulwark_Reset_FacesThePlayer` | mid-recover / `Reset`, tick / approaching, shielded (4) |
| `Bulwark_InReach_Telegraphs` | 1.2 m / 0.7 s telegraph, halted (5) |
| `Bulwark_Bash_HitsOnlyInFront` | in front; beside / 16 once; none (6) |
| `Bulwark_Recover_DoesNotTurn` | player circles / fixed 1.5 s (7) |
| `Bulwark_OneIntentATick` | each state / one intent (8) |
| `Spec_Bulwark_Refuses` | no spec; 0.5 s / throws (9) |
| `Bulwark_Tick_AllocatesNothing` | every state / none (10) |

## Out of scope

- JG-14b–d; the Warden of Ash's shield; per-hit direction.

## As built

Built 2026-10-31. `BulwarkSpec`, appended kind and pooled dispatch; Approach turns along the path,
walks within its cone, and updates both shield flags after steering. First tick after reset faces
the player outright, even when the path differs. Wind-up freezes the front; Bash resolves once at
its end, rechecking reach/arc and refusing decoy damage. Recover counts from that bash instant,
including the Bash state's following tick; `NextAttackAt` is bash + recover. Blocked damage rides
the optional trailing `EnemyDamaged.Blocked`. No authored content or placeholder-name dependency.

Ten test methods map one to one to Behaviour 1–10, with 29 cases, all passing (including allocation).
Full EditMode: 4 749 total / 4 748 passed / 0 failed / 1 known animator-clock inconclusive;
UTC start 2026-10-30 23:02:31, after submission. Zero compiler errors or new analyzer warnings;
only existing tests' expected fault logs. `dotnet format` clean on all seven C# files.
Files table only, plus Unity `.meta` companions and protocol docs.

Manual: none for this core-only task. After JG-14d authors the body, check front blocking,
rolling round its frozen bash/recover, and the 75°/s turn against walking at 3 m.
