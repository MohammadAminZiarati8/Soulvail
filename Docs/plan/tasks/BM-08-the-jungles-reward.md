# BM-08 — The Beastmaster is the Jungle's reward

**Size:** S · **Depends on:** BM-07d · **Branch:** `dev` (direct)
**Design refs:** CH §3.6 (unlock); business model (earnable or buyable, no power for sale); M6 unlocks

## Goal

The Beastmaster is locked until the Apex dies or the player pays for him, and class select says so.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Characters/Beastmaster.asset` | — | Unlock: deed boss `boss.apex`, Shard price 5 000 (a stand-in until the store prices him) |
| `Core/Progression/ClassUnlocks.cs` | Core | Only if needed: a deed boss counts in whichever mode it died |
| `Game/Presentation/HeroGrid.cs` | Game | The locked card names the deed ("Beat the Apex") and the price |
| `Tests/Core/Progression/BeastmasterUnlockTests.cs` | Tests.Core | **New** |
| *small edits* | | `English`/`Pseudo` (deed line); `CharacterLookBook` portrait |

## Behaviour

1. A new profile holds the Beastmaster locked; class select shows his card with a padlock, the
   deed line and the price.
2. Killing `boss.apex` in a Jungle run unlocks him at the run's end, with the unlock toast every
   class uses. The task checks first that `ClassUnlocks.Earned` sees a deed boss killed outside
   Descent; if it does not, it is made to, and *As built* says so.
3. Paying the price unlocks him through `ClassUnlocks.CanBuy` like any class. A real-money purchase
   arrives with the store task and unlocks the same way; nothing he does changes with how he was got.
4. An unlocked Beastmaster stays unlocked across saves (no migration: the id list already persists).

## Tests

| Test | Given / When / Then |
|---|---|
| `NewProfile_BeastmasterLocked` (1) |
| `JungleApexKill_Unlocks` / `OtherBossKill_DoesNot` (2) |
| `ShardPurchase_Unlocks` (3) |
| `Unlock_SurvivesSaveRoundTrip` (4) |

## Manual verification (Editor / device)

1. **[Editor]** A fresh profile shows him locked with "Beat the Apex"; DT-01 at stage 30, kill the
   Apex: he unlocks.

## Out of scope

- The store and real-money purchase; Packleader (PL).

## As built

_Filled at merge._
