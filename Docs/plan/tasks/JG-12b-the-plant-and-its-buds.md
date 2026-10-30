# JG-12b — The plant and its buds

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (Choir), GD §9.1 rules 3–4, GD §9.2 (Choirmother)

## Goal

`MotherPitcherBehaviour` stands rooted facing the player and takes a fifth of every hit while any
Pitcher bud lives. A boss's opening phase now summons its adds.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/MotherPitcherBehaviour.cs` | Core | **New.** Rooted stand; the bud shield |
| `Tests/Core/Ai/MotherPitcherBehaviourTests.cs` | Tests.Core | **New.** Rules 1–7 |
| *small edits* | | `Core/Ai/EnemyAgent.cs` (`DamageTaken`; `Initialise` clears it); `Core/Ai/EnemySystem.cs` (`ApplyDamage` scales by it); `Core/Ai/BossBehaviour.cs` (opening summons); `Core/Run/RunSession.cs` (`_bossBehaviours` row); `Docs/Invariants.md` (§18.1 *Enemies*: the scale is applied before `Health`) |
| *ripple* | | `Tests/Core/Run/BossDispatchTests.cs` (rule 8) |

## Public API

```csharp
// EnemyAgent
public Stat DamageTaken { get; }                    // base 1; multiplies every hit
public sealed class MotherPitcherBehaviour : IEnemyBehaviour
{
    public const float ShieldedDamageTaken = 0.2f;  // PercentMult −0.8
    public MotherPitcherBehaviour(EnemyAgent agent, ProjectileSystem projectiles,
        IRandomStream random, ContentId budId, ContentId frogId);
    public bool IsShielded { get; }
    public void Tick(in EnemyTickContext ctx);
    public void Reset();
}
```

## Behaviour

1. **`DamageTaken` scales every hit.** `EnemySystem.ApplyDamage` hands `Health` `amount ×
   DamageTaken.Value`; `EnemyDamaged` reports what was applied. `Initialise` removes its modifiers.
2. **The opening phase summons.** On a boss's first tick `BossBehaviour` calls in phase 0's summons,
   once per life (`Reset` re-arms). The Warden's and Croaker's phase 0 summon none: unchanged.
3. **Rooted.** Every tick one intent: zero velocity, facing `DirectionToPlayer`.
4. **Buds shield her.** While any live enemy wears `budId`, `DamageTaken` carries exactly one
   `PercentMult` −0.8 from this behaviour; with none, none. A registry walk each tick.
5. **The shield follows the phases.** Buds a crossing summons raise it again after the beat.
6. **`Reset`** removes the modifier.
7. **`Tick` allocates nothing.**
8. **`RunSession` drives `boss.mother_pitcher`** with this behaviour, the Misc stream,
   `enemy.pitcher_bud` and `enemy.frog`.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `DamageTaken_ScalesTheHit` | ×0.2 / 50 applied / 10 off `Health`, `EnemyDamaged` 10 (rule 1) |
| `DamageTaken_ClearedOnInitialise` | modifier, respawn / read / 1 (rule 1) |
| `Boss_OpeningPhase_SummonsOnItsFirstTick` | phase 0 summons 3 / two ticks / 3 adds once; after `Reset`, again (rule 2) |
| `Mother_StandsRootedFacingThePlayer` | player circles / ticks / one intent each, zero velocity, facing the player (rule 3) |
| `Mother_BudAlive_TakesAFifth` | a bud live / 100 applied / 20 (rule 4) |
| `Mother_LastBudDies_TakesFull` | every bud killed / tick, 100 applied / 100 (rule 4) |
| `Mother_Crossing66_RegrowsTheShield` | buds dead, phase 1 summons buds / cross 66 %, beat ends / shielded (rule 5) |
| `Mother_Reset_DropsTheShield` | shielded / `Reset` / no modifier (rule 6) |
| `Mother_Tick_AllocatesNothing` | buds live / ticked / `AllocationAssert.None` (rule 7) |
| `Dispatch_MotherPitcher_IsDriven` | the table / `boss.mother_pitcher` / a `MotherPitcherBehaviour` (rule 8) |

## Manual verification (Editor / device)

None until JG-12h.

## Out of scope

- The attacks and seed pods (JG-12c). The bud's archetype and the phases' data (JG-12h).
- A drawn shield: auto-aim on the buds is the read.

## As built

Built 2026-10-30. Rules 1-7 are pinned by the nine named rows in
`MotherPitcherBehaviourTests`; rule 8 by `BossDispatchTests.Dispatch_MotherPitcher_IsDriven`.
`DamageTaken` is a base-1 stat, cleared on rental and applied before `Health`; events retain the
applied result. The rooted behaviour owns one -0.8 `PercentMult` while any live bud remains,
removes it on reset, and resumes its registry scan after a phase beat. Opening summons run once
per life, re-armed by `BossBehaviour.Reset`. The session row passes the current run's projectile
system, Misc stream, bud id and Frog id; the attack dependencies stay unused until JG-12c.
AR section 18.1 records the damage ordering. No spec/code conflicts or rule resolutions were needed.

Verification: full EditMode rerun 4 600 pass, 0 fail, 0 skipped, 1 known inconclusive
(`PlayerAnimatorViewTests.Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns`: Editor clock
stands at zero). Failing names: none. Compiler errors 0; new analyzer warnings 0;
`dotnet format whitespace --folder --verify-no-changes --include Assets/_Project` clean.
All ten new rows pass. The first full run's sole failure,
`LocalJsonSaveStoreTests.Store_RoundTripsClaimed`, was an I/O error replacing its temporary save;
it passed on the full rerun.
Manual Editor/device verification remains deferred to JG-12h as specified.
