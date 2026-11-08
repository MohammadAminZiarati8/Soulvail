# BM-08 — The Beastmaster is the Jungle's reward

**Size:** S · **Depends on:** BM-07d · **Branch:** `dev` (direct)
**Design refs:** CH §3.6 (unlock); business model (earnable or buyable, no power for sale); M6 unlocks

## Goal

The Beastmaster is locked until the Apex dies or the player pays for him, and class select says so.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Characters/Beastmaster.asset` | — | Unlock: deed boss `boss.apex`, Shard price 5 000 (a stand-in until the store prices him) |
| `Core/Run/RunSession.cs` | Core | `PublishPayout`: a finished mode's depth is `StageIndex + 1` (its last stage was left), so the last boss counts |
| `Game/Presentation/HeroGrid.cs` | Game | The locked card names the deed and the price: a boss deed reads `ui.classselect.locked.deedBoss` with the boss's name ("or beat the Apex"); a stage deed keeps `…locked.deed` |
| `Tests/Core/Progression/BeastmasterUnlockTests.cs` | Tests.Core | **New** |
| *small edits* | | `English`/`Pseudo` (`ui.classselect.locked.deedBoss`); `CharacterLookBook` portrait if missing |

## Behaviour

1. A new profile holds the Beastmaster locked; class select shows his card with a padlock, the
   deed line and the price.
2. Killing `boss.apex` in a Jungle run unlocks him at the run's end, with the unlock toast every
   class uses. **Found 2026-11-08:** on a won mode `StageFlow` keeps `Stage` at the last stage, and
   `ClassUnlocks.DeedDone` and `ShardPayout.BossesKilled` count only stages *below* the depth, so
   the Apex never counted. Fix at the source: `PublishPayout` reports `StageIndex + 1` when
   `IsModeComplete`. A won run therefore also pays its last stage and last boss (correct: it left
   them). A death keeps `StageIndex`. `ClassUnlocks` and `ShardPayout` are unchanged.
3. Paying the price unlocks him through `ClassUnlocks.CanBuy` like any class. A real-money purchase
   arrives with the store task and unlocks the same way; nothing he does changes with how he was got.
4. An unlocked Beastmaster stays unlocked across saves (no migration: the id list already persists).

## Tests

| Test | Given / When / Then |
|---|---|
| `NewProfile_BeastmasterLocked` (1) |
| `JungleApexKill_Unlocks` / `OtherBossKill_DoesNot` / `DeathAtApex_DoesNot` (2) |
| `ModeWon_PayoutDepthIsPastLastStage` / `Death_PayoutDepthUnchanged` (2) |
| `LockedCard_BossDeed_NamesBoss` (1) |
| `ShardPurchase_Unlocks` (3) |
| `Unlock_SurvivesSaveRoundTrip` (4) |

## Manual verification (Editor / device)

1. **[Editor]** A fresh profile shows him locked with "Beat the Apex"; DT-01 at stage 30, kill the
   Apex: he unlocks.

## Out of scope

- The store and real-money purchase; Packleader (PL).

## As built

_Filled at merge._
