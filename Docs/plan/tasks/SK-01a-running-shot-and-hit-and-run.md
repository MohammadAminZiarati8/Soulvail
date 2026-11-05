# SK-01a — Running Shot and Hit and Run in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Skirmisher); RS-03a (`FireWhileMoving`); ADR-0008 (damage pool)

## Goal

Both Skirmisher passives in core: firing on the move, a speed burst on hit, damage from distance.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/HitAndRun.cs` | Core | **New.** Burst timer + a 2 s distance ring (20 × 0.1 s buckets) |
| `Core/Combat/PlayerCombat.cs` | Core | Moving fire rate; aim-facing on the move; feeds `HitAndRun`; Momentum in `ShotDamage` |
| `Core/Effects/PlayerStat.cs` | Core | Append `MovingFireRate`, `HitSpeedBurst`, `MomentumPerMetre` |
| `Tests/Core/Combat/RunningShotTests.cs` | Tests.Core | **New** |
| `Tests/Core/Combat/HitAndRunTests.cs` | Tests.Core | **New** |
| *small edits* | | `CombatantStats`, `RunSession` (construct, attach the `StatusBoard`), `StatBlockTests`/`PlayerStatCoverageTests`/`ModifyStatTests` (new members) |

## Public API

```csharp
public sealed class HitAndRun {
  public const float BurstSeconds = 1.5f, WindowSeconds = 2f, MaxMetres = 8f, SnapMetres = 3f;
  public void AttachStatus(StatusBoard status);
  public void OnMoved(float metres, float now);   // XZ distance this tick
  public void OnHit(float now);
  public float SpeedBonus(float now);             // 0 or HitSpeedBurst
  public float DamageMultiplier(float now);       // 1 + min(metres, MaxMetres) × MomentumPerMetre
  public void Reset(); }
```

## Behaviour

1. **`MovingFireRate`** has base **1** for every class. While the player moves and
   `FireWhileMoving` > 0.5, the weapon fires at `FireRate × MovingFireRate`; standing, at
   `FireRate`. The rate is read when each shot's interval starts. Classes without Running Shot are
   unchanged.
2. While moving and firing, `FaceDirection` is the aim direction, as when standing; moving with no
   target, it stays the run direction.
3. **Running Shot ranks** (for SK-01b): 1 — `FireWhileMoving` Flat +1 and `MovingFireRate` Flat
   −0.60; 2, 3, 4 — `MovingFireRate` +0.15 each (0.40 / 0.55 / 0.70 / 0.85).
4. **Burst:** with `HitSpeedBurst` > 0, every player arrow that damages an enemy (basic, Volley and
   later skills' arrows) sets the burst to end 1.5 s later — it refreshes, never stacks. While it
   lasts, move speed is ×(1 + `HitSpeedBurst`), as one modifier added at start and removed at end.
5. The burst posts `status.hit-and-run` (buff) on the `StatusBoard` with its seconds left, and
   re-posts on refresh.
6. **Momentum:** with `MomentumPerMetre` > 0, every player arrow's damage is multiplied by
   `DamageMultiplier` at the moment it is loosed. Distance counts walking and rolling over the last
   2 s, up to 8 m. A single tick moving more than 3 m (a spawn or stage snap) counts 0.
7. **Hit and Run ranks:** 1 — `HitSpeedBurst` +0.15; 2 — +0.05 and `MomentumPerMetre` +0.02;
   3 — +0.05 and `MomentumPerMetre` +0.01.
8. A stage boundary or run reset clears the burst, its status and the distance ring.
9. Nothing allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Moving_WithoutRunningShot_HoldsFire` | Ranger, no rank / moves / no shot (1) |
| `Moving_Rank1_Fires_AtFortyPercent` / `Rank4_EightyFive` | interval = base / 0.40, / 0.85 (1, 3) |
| `Standing_FullRate` | rank 4, stands / base interval (1) |
| `Moving_FacesTarget` / `Moving_NoTarget_FacesRun` (2) |
| `Hit_StartsBurst_SpeedRises` / `Hit_Refreshes_NoStack` (4) |
| `Burst_Ends_SpeedRestored` | 1.5 s after the last hit / base speed (4) |
| `Burst_PostsAndRepostsStatus` (5) |
| `Momentum_FourMetres_Rank2_Plus8Percent` / `Momentum_CapsAtEight` (6, 7) |
| `Momentum_SnapIgnored` / `Momentum_ForgetsAfterTwoSeconds` (6) |
| `StageBoundary_ClearsAll` (8) |
| `Tick_DoesNotAllocate` (9) |

## Out of scope

- Assets and the path (SK-01b); the run-and-shoot animation (SK-02).

## As built

_Filled at merge._
