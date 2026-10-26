# JG-04c — Old Croaker in the Jungle

**Size:** M · **Depends on:** JG-04b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1, GD §9.2; M4-01b rule 1

## Goal

A second boss can exist. `RunSession` picks a boss's behaviour by its id, and the Jungle's boss stages
are Old Croaker.

## Why this shape

- **A map, not a switch.** `RunSession` builds a `Dictionary<ContentId, Func<EnemyAgent, IEnemyBehaviour>>`
  once per run: `boss.warden → WardenBehaviour` and `boss.croaker → CroakerBehaviour`. The factory
  `EnemySystem` takes looks the id up. An id with no entry is refused at `RunSession.Start`, alongside
  the existing check that the catalog holds the boss. This is the dispatch `RunSession`'s own comment
  says the second boss would need.
- **The Jungle's boss roster is Croaker every 5th, for now.** `TryGetBossFor` is modulo-based, so it
  can't say "stage 5 only". Until the second Jungle boss exists, all six Jungle boss stages are Croaker,
  which already beats the off-theme Warden. Stage-specific entries come with that second boss.
- **Health.** Old Croaker uses `enemy.warden`'s HP and contact damage. GD §9.1 rule 5's 75–120 s is the
  owner's playtest to judge; tuning is a later task.

## Files

| Path (under `Assets/_Project/`) | Purpose |
|---|---|
| `Data/Enemies/Croaker.asset` | **New.** `enemy.croaker`: Warden's numbers, the Croaker prefab (a capsule until JG-04d) |
| `Data/Enemies/CroakerBoss.asset` | **New.** `boss.croaker`: phases 1 / 0.66 / 0.33, summoning 3 then 4 `enemy.frog` |
| `Tests/Core/Run/BossDispatchTests.cs` | **New.** Rules 1–3 |
| *small edits* | `Core/Run/RunSession.cs` (the map); `Data/Modes/Jungle.asset` (`_bossRoster` → `boss.croaker`, 5); wherever `WardenBoss.asset` is registered in the catalog (the two new assets) |
| *ripple* | `Tests/Game/Content/ContentValidationTests` rows that list bosses |

## Behaviour

1. A boss spawned as `boss.croaker` fights with `CroakerBehaviour`, and `boss.warden` still with `WardenBehaviour`.
2. `RunSession.Start` refuses a mode whose roster names a boss with no behaviour, naming the id.
3. A Jungle run's stage 5 spawns Old Croaker. Descent's stage 5 is still the Warden.
4. Phase 2 calls in 3 Frogs and phase 3 calls in 4.

## Tests

| Test | Rule |
|---|---|
| `Croaker_FightsWithCroakerBehaviour` / `Warden_StillTheWarden` | 1 |
| `Start_RefusesABossWithNoBehaviour` | 2 |
| `Jungle_Stage5_IsTheCroaker` / `Descent_Stage5_IsTheWarden` | 3 |
| `CroakerBoss_PhasesSummonFrogs` (Tests.Game, asset row) | 4 |

## Manual verification

1. **[Editor]** Jump to Jungle stage 5. A big capsule hop-slams: a red-orange circle where you stood,
   then a ring outward from it. At 66 % the arena clears and 3 Frogs come.

## Out of scope

The body (JG-04d); the tongue lash (JG-04e); HP tuning; the mud hazard.

## As built

_2026-10-26._ **Full EditMode suite: 4 429, 4 427 passed / 1 failed / 1 inconclusive**, the two known
(`CharacterPreviewTests.Resize_…`, `PlayerAnimatorViewTests.Animator_AttackSpeed…`); 6 / 6 new rows
green. Zero compile errors or warnings; `dotnet format whitespace` clean on the four changed `.cs`
files. **Run in a batch-mode Unity (`-runTests`)**: the Editor was closed and the MCP had no tools.

- **The map is built once per session, in the constructor, not once per run.** Each entry reads
  `_shockwaves`, `_fissures` and `_random.Misc` when a boss stands up, so it always gets the live
  run's; building it in `Start` would allocate the same two closures every run for nothing. The
  factory is `(agent, boss) => _bossBehaviours[boss.Id](agent)`.
- **Rule 2 sits in `RequireAuthored`**, after the catalog and body checks for the same roster entry,
  so a stranger boss is refused before `RunStarted`, naming the id (`KeyNotFoundException`).
- **Rule 1's rows run a whole `RunSession`** (the map is private and Tests.Core has no
  `InternalsVisibleTo`) and read the boss's first `EnemyTelegraph`: `Move 1`, 0.9 s for the Croaker,
  `Move 0` for the Warden. The player stands 1.5 m from where the boss stood up, since the snapshot
  carries no enemy positions.
- **Rule 3's rows are asset rows in `ContentValidationTests`, not `BossDispatchTests`**, because the
  rule is about `Jungle.asset` and `Descent.asset` and Tests.Core cannot load assets.
  `CroakerBoss_PhasesSummonFrogs` sits beside them. `ShippedBosses` 1 → 2.
- **Outside the Files table, said here:** `enemy.croaker.name` → "Old Croaker" in `English.asset`
  and `Pseudo.asset`, so the boss bar shows no raw key; and `JungleTests.SharedBlocks` loses
  `_bossRoster`, since the Jungle's roster now differs from Descent's on purpose.
- `Croaker.asset` is `Warden.asset` with its own id, name key and a moss tint; `_body` empty (the
  capsule). `CroakerBoss.asset` is `WardenBoss.asset` with `enemy.frog` adds.
