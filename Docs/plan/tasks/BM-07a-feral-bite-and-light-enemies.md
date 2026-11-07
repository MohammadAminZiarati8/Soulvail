# BM-07a — Feral Bite and light enemies

**Size:** M · **Depends on:** BM-03a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.4 (Light; Feral Bite); JG-26a (bosses ignore CC)

## Goal

A per-enemy "light" flag, and the hound's every-Nth-bite stun that reads it. Pounce (BM-07c),
Hawk and Stampede (PL) read the same flag.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/EnemySpec.cs` | Core | `IsLight` (refused with `IsElite`) |
| `Game/Authoring/EnemyDefinition.cs` | Game | `_isLight` → `IsLight` |
| `Core/Ai/PetSystem.cs` | Core | Counts bites per pet; every `PetBiteStunEvery`-th on a light enemy stuns it |
| `Core/Effects/PlayerStat.cs` | Core | Append `PetBiteStunEvery` (base 0 = off), `PetBiteStunSeconds` (base 0.5) |
| `Core/Effects/PetStats.cs` | Core | Answers both |
| `Tests/Core/Ai/FeralBiteTests.cs` | Tests.Core | **New** |
| `Tests/Game/Authoring/LightEnemyTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Enemies/` `Frog`, `Rootling`, `Puffball`, `Spitter`, `Pitcher`, `Panther`, `ShadowPanther`, `ThornPitcher`, `ChainBomber` set `_isLight`; stat coverage tests |

## Public API

```csharp
public sealed class EnemySpec { public bool IsLight { get; } }
public sealed class EnemySystem { public bool IsLight(int enemyId); }   // false for bosses and the dead
```

## Behaviour

1. `IsLight` is authored per enemy; an elite or a boss cannot be light (validation throws, naming it).
2. The nine listed are light; Bullfrog, Mossguard, Shaman, Stalker, Thornweaver, every boss are not.
3. **Feral Bite:** with `PetBiteStunEvery` = N > 0, every Nth bite by a pet stuns its target for
   `PetBiteStunSeconds` if that target is light. The count is per pet and runs on every bite, light
   or not.
4. `PetBiteStunEvery` is read as a whole number, clamped 0–10; 0 is off.
5. The stun goes through `EnemySystem.Stun`, so it shows as stun stars and a bar icon.

## Tests

| Test | Given / When / Then |
|---|---|
| `EliteOrBoss_Light_Refused` (1) |
| `Assets_LightSet_IsTheNine` (2) |
| `EveryThirdBite_StunsLight` / `ThirdBiteOnBullfrog_NoStun` (3) |
| `CountRunsOnEveryBite` | bites 1–2 on a Bullfrog, 3 on a Frog / the Frog is stunned (3) |
| `StunEveryZero_Off` / `StunEveryFractional_Floors` (4) |
| `FeralStun_ShowsAsStun` (5) |

## Out of scope

- The node asset (BM-07d).

## As built

_Filled at merge._
