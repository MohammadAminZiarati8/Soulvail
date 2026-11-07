# PL-05a — Stampede in core

**Size:** M · **Depends on:** BM-07a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Stampede); MK-10a/c (`KillshotLine`, frontal guards, phase shields)

## Goal

The Packleader's Lv-20 active: a herd of wild boars runs down the most crowded line, hitting
everything once per boar and knocking light enemies down.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/StampedeSystem.cs` | Core | **New.** The herd on its line: boars, their sweep, hit sets |
| `Core/Effects/Stampede.cs` | Core | **New.** `IEffect`, handler, cast guard |
| `Core/Events/StampedeEvents.cs` | Core | **New.** `StampedeStarted(From, Direction, Length, Boars, HalfWidth)`, `BoarHit(EnemyId, Amount)`, `StampedeEnded` |
| `Core/Effects/PlayerStat.cs` | Core | Append `StampedeBoars` (6), `StampedeDamage` (1), `StampedeHalfWidth` (1.5) |
| `Tests/Core/Combat/StampedeSystemTests.cs` | Tests.Core | **New** |
| *small edits* | | `EffectRegistry`; `RunSession` (construct, tick, clear); stat coverage tests |

## Public API

```csharp
public sealed class Stampede : IEffect { public Stampede(float knockdownSeconds); }   // 0.8
public sealed class StampedeSystem {
  public const float BoarSpeed = 14f, Spacing = 0.15f, StartBehind = 2f, Length = 20f;
  public StampedeSystem(IDomainEvents events, PlayerStats stats);
  public bool IsRunning { get; }
  public bool Start(Vector3 player, ReadOnlySpan<EnemyPoint> enemies, float now, float knockdownSeconds);
  public void Tick(float now, EnemySystem enemies, PlayerCombat combat);
  public void Clear(); }
```

## Behaviour

1. **Line:** `KillshotLine.TryPick` from the player with `StampedeHalfWidth`; none → `CanCast`
   false, cooldown kept. The herd starts `StartBehind` behind the player on that line and runs
   `Length` along it.
2. `StampedeBoars` boars (whole, clamped 1–12) leave `Spacing` s apart at `BoarSpeed`.
3. **Hits:** each boar hits each living enemy whose XZ distance to the line ≤ `StampedeHalfWidth +
   BodyRadius` once as it passes: `StampedeDamage × WeaponDamage`, half on a boss, through the damage
   door; a light enemy is also stunned `knockdownSeconds`. `BoarHit` per hit.
4. Boars pass frontal guards and stop at nothing; a boss's phase shield still blocks (Killshot's rule).
5. Boars cannot be hit or hunted and are in no registry. `StampedeEnded` when the last boar's run ends.
6. Trigger `EnemiesInAcquireRange ≥ 1`; cooldown 30 s. **Ranks** (PL-05c): 1 — active;
   2 — `StampedeBoars` +3; 3 — `StampedeDamage` +0.3 and `StampedeHalfWidth` +0.5.
7. Kills pay XP as the player's. `Clear` ends the run silently. No allocation per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Start_PicksCrowdedLine` / `NoLine_Refused_CooldownKept` (1) |
| `SixBoars_SpacedAtInterval` (2) |
| `EachBoar_HitsEachEnemyOnce` / `Boss_HalfDamage` / `Light_KnockedDown` (3) |
| `Guard_Passed_PhaseShield_Blocks` (4) |
| `Ended_AfterLastBoar` (5) |
| `Ranks_Numbers` (6) |
| `BoarKill_PaysXp` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- Model (PL-05b); drawn and authored (PL-05c).

## As built

_Filled at merge._
