# JG-15d — The Warlord's phases: spear lobs at 66 %, the shield shatters at 33 %

**Size:** M · **Depends on:** JG-15c · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 3, 5; AR §18.3 (modifiers), §18.4

## Goal

At 66 % the Warlord adds ringed spear lobs; at 33 % its shield shatters — no front guard, faster,
quicker to turn, and its sweep becomes a two-hit combo.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/WarlordBehaviour.cs` | Core | Lob move; shattered state; combo |
| `Core/Events/WarlordEvents.cs` | Core | Gains `WarlordShattered` |
| `Tests/Core/Ai/WarlordPhaseTests.cs` | Tests.Core | **New** |

## Public API

```csharp
public const float LobTelegraphSeconds = 0.8f, LobCooldownSeconds = 5f, LobMinRange = 5f,
  LobSpeed = 9f, LobRadius = 1.4f, LobSpread = 2.5f;
public const int LobSpears = 3;
public const float ShatteredSpeedBonus = 0.4f, ShatteredTurnRateDeg = 120f,
  ShatteredRecoverSeconds = 0.5f, ShatteredAttackGapSeconds = 0.9f, ComboSecondWindupSeconds = 0.6f;
public int Phase { get; }          // as last entered: 0, 1, 2
public bool IsShattered { get; }
public readonly struct WarlordShattered { int BossId; }
```

## Behaviour

1. **Lob** (phase ≥ 1): off cooldown and the player ≥ `LobMinRange` away → ranks after war cry and
   before charge. Stand, publish `EnemyTelegraph(id, 0.8, 4)`; at 0.8 s fire `LobSpears` projectiles
   with the boss's own spec id (so its `MarksLanding` rings each landing): one at the player's
   position, two at ±`LobSpread` across the line to the player. Damage `ContactDamage`. One
   `IRandomStream` draw per volley at most. Cooldown from the release.
2. **Shatter** on entering phase 2: `IsShattered`, `GuardHalfArcDeg` 0 for the rest of the fight (lock
   and daze no longer change it), `MoveSpeed` gains a `PercentMult` +0.4 from this source,
   turn rate 120°/s, recover 0.5 s, gap 0.9 s. `WarlordShattered` publishes once.
3. **Combo** (shattered): the sweep becomes `Combo`: wind-up 0.8 s (`EnemyTelegraph(id, 0.8, 5)`),
   hit as 15a rule 5; then it re-aims at the player and winds a second 0.6 s
   (`EnemyTelegraph(id, 0.6, 6)`, its own cone) and hits again. Each hit has its own cone and
   `SprayFired`. Every telegraph ≥ 0.6 s.
4. **Charge and war cry** continue in every phase; a shattered daze is still 2 s.
5. **Beats** (GD rule 3) come from `BossBehaviour` at 0.66 and 0.33; the beat's add clear is 15c's.
   A phase entry drops the armed move (15a rule 7) before the phase's changes apply.
6. **Reset** removes its modifier and un-shatters. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Lob_PhaseZero_Never` / `Lob_PhaseOne_FarPlayer_TelegraphsPointEight` / `Lob_NearPlayer_NotChosen` (1) |
| `Lob_ThreeSpears_PlayerAndBothSides_OwnSpecId` / `Lob_Cooldown_FromRelease` (1) |
| `Shatter_PhaseTwo_GuardZero_PublishesOnce` / `Shatter_LockNoLongerWidens` (2) |
| `Shatter_Faster_TurnsFaster_ShorterGaps` (2) |
| `Combo_TwoTelegraphs_TwoHits_SecondReaims` / `Combo_SecondWindupIsPointSix` (3) |
| `Shattered_StillCharges_StillWarCries` (4) |
| `Phase_DropsMove_ThenApplies` — session at 0.66 and 0.33 with a real `BossSpec` (5) |
| `Reset_RemovesBonus_Unshatters` / `Tick_DoesNotAllocate` (6) |

## Out of scope

- Fight length and HP (15g's authored numbers); the shatter's look (15f).

## As built

_Filled at merge._
