# JG-25d — Gravemaw's phase 2 at 50 %: a double surface and adds from the cracks

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references.

**Size:** M · **Depends on:** JG-20c · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 3, 4; owner 2026-11-02 (phase 2 near 50 %); `IBossAddOwner` (JG-15c); boss summons pay no XP

## Goal

Below half health every cycle erupts twice before the window opens, and Frogs crawl out of cracks.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/GravemawBehaviour.cs` | Core | Double surface, cracks, roll-call; `IBossAddOwner` |
| `Core/Ai/GravemawStats.cs` | Core | Gains the stats below |
| `Core/Events/GravemawEvents.cs` | Core | Gains `GravemawCrack` |
| `Tests/Core/Ai/GravemawPhaseTests.cs` | Tests.Core | **New** |

## Public API

```csharp
// GravemawStats gains: DoubleGapSeconds /*0.6*/, SecondWarnSeconds /*0.8*/, PhaseTwoChaseSeconds /*1.5*/,
//   CrackSeconds /*1*/, MaxAdds /*4*/, AddsPerSurface /*2*/, CrackSpacing /*5 m*/
// GravemawBehaviour : IBossAddOwner;  int Phase { get; }  int AddCount { get; }
// GravemawEvents: GravemawCrack(BossId, At, Seconds)
```

## Behaviour

1. **Phase 2** is `BossSpec` phases 1 / 0.5 (owner's ruling over GD §9.1's 66 / 33). The beat is
   `BossBehaviour`'s: invulnerable 1.5 s, adds cleared (rule 6), then Chase.
2. **Double surface:** after eruption A (damage and throw as 20a–b) it does **not** open the
   window: it burrows `DoubleGapSeconds`, predicts afresh from the thrown player, rings B with
   `SecondWarnSeconds` (≥ 0.6, telegraphed) and erupts. The window, tremor and spit follow B.
3. **Chase** is `PhaseTwoChaseSeconds` long in phase 2.
4. **Cracks:** at each eruption, while `AddCount + AddsPerSurface ≤ MaxAdds`, open `AddsPerSurface`
   cracks at `ctx.OpenGround` points ≥ `OpenGroundPicker.PlayerClearance` from the player and
   ≥ `CrackSpacing` from each other, farthest from the boss first, ties to the lower index;
   publish `GravemawCrack(id, at, CrackSeconds)`.
5. **Crawl out:** `CrackSeconds` later `SpawnSummon("enemy.frog", at)` — 0 XP, still a kill. A
   registry without room skips the add.
6. **Roll-call:** ids tracked; a dead guard leaves the count that tick. `ClearAdds` (the beat)
   despawns every tracked add and cancels unopened cracks.
7. **Reset** empties the roll-call without despawning; phase 0 behaves as 20a–c exactly.
   No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `PhaseTwo_BeatClearsAdds_ThenChases` — real `BossSpec` 1 / 0.5 (1) |
| `Double_FirstEruptionHurtsAndThrows_NoWindow` / `Double_SecondRingFromThrownPlayer_AtPointEight` / `Double_WindowAfterSecond` (2) |
| `PhaseTwo_ChaseIsShorter` / `PhaseOne_SingleSurface` (3) |
| `Cracks_TwoOpen_SpacedAndClear` / `Cracks_FullRoster_NoneOpen` (4) |
| `Crack_Summons_Frog_PaysNoXp` / `Crack_NoRoom_Skipped` (5) |
| `RollCall_DeadLeaves` / `Beat_DespawnsAdds_CancelsCracks` (6) |
| `Reset_EmptiesRollCall` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- The crack's look (20f); more phases; Frog stats.

## As built

_Filled at merge._
