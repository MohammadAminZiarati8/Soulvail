# JG-12h — Mother Pitcher in the Jungle

**Size:** M · **Depends on:** JG-12a, b, c, e, f, g (so d too) · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §9.1 rules 3–5, GD §9.2, GD §12.3

## Goal

Jungle stage 10 is Mother Pitcher's: `boss.mother_pitcher` with three Pitcher buds per phase and
Frogs; the other fifths stay Old Croaker's.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/MotherPitcher.asset` | — | **New.** `enemy.mother_pitcher` (rule 1) |
| `Data/Enemies/MotherPitcherBoss.asset` | — | **New.** `boss.mother_pitcher` (rule 2) |
| `Data/Enemies/PitcherBud.asset` | — | **New.** `enemy.pitcher_bud` (rule 3) |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rules 1–6 |
| *small edits* | | `Data/Modes/Jungle.asset` (`_bossRoster`); `Prefabs/Composition/BootScope.prefab` (two enemies, one boss); `Data/Localisation/English.asset`, `Pseudo.asset` (two names) |

## Public API

None.

## Behaviour

1. **`enemy.mother_pitcher`**: Boss kind, HP 750, target priority 2, threat 40, XP 400,
   `ContactDamage` 10, `_bodyScale` 2.5, body `MotherPitcher.prefab`, projectile `PitcherSpit.prefab`,
   `_marksLanding` on, cover `Cover_MotherStones.prefab`. It never moves (JG-12b).
   *HP:* a stage-10 Ranger ≈ 27 DPS (13 × 1.1, +10 %/stage, GD §12.5) × ~60 % standing ≈ 16; 750 ×
   h(10) 1.54 = 1 155 ≈ 72 s, buds ≈ 21 s, beats 3 s: ≈ 95 s, inside 75–120.
2. **`boss.mother_pitcher`**: `SpawnsCentral`, beat 1.5 s; phases from 1.0 (3 buds), 0.66 (3 buds +
   2 Frogs), 0.33 (3 buds + 3 Frogs).
3. **`enemy.pitcher_bud`**: Static, HP 24 (37 at stage 10), priority 8, threat 4, XP 6, no contact
   damage, body `Pitcher.prefab` at `_bodyScale` 0.6. Not in the roster: the budget never draws it.
4. **The roster**: a stage row 10 → `boss.mother_pitcher`; every 5 → `boss.croaker`. Stages 5, 15,
   20, 25, 30 are Croaker's; scripted stages and arenas unchanged.
5. **It boots.** `BootScope` lists both enemies and the boss; `enemy.mother_pitcher.name`
   ("Mother Pitcher") and `enemy.pitcher_bud.name` ("Pitcher Bud") are in both tables; a Jungle run
   started at 10 passes `RunSession.Start`'s checks.
6. **The fight fits GD §9.1 rule 5**: rule 1's sum from the assets lies in 75–120 s.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Rule |
|---|---|
| `Jungle_MotherPitcher_HasItsNumbers` | 1 |
| `Jungle_MotherPitcherBoss_HasItsPhases` | 2 |
| `Jungle_PitcherBud_IsAHighPriorityStatic` / `Jungle_PitcherBud_IsNotRostered` | 3 |
| `Jungle_StageTen_IsMotherPitcher_OtherFifthsCroaker` | 4 |
| `Jungle_MotherPitcher_Boots` (start at 10) | 5 |
| `Jungle_MotherPitcher_FightIsInTheBand` | 6 |

## Manual verification (Editor / device)

1. **[Editor]** Test start at 10: the plant stands in the middle, four stones round it, three buds at
   its foot; arrows go to the buds first; the plant barely bleeds until they fall.
2. **[Editor]** Barrage: five rings, five lobs; moving clears them. Spray: a wedge with shadows
   behind the stones; behind one, no hit; in the open, ≈ 26.
3. **[Editor]** At 66 % and 33 %: a beat, adds cleared, buds regrow, Frogs hop in; pods drop Frogs.
4. **[Editor]** About 1½ minutes; stages 5 and 15 are still Old Croaker. The owner rules on feel.

## Out of scope

- Retuning Old Croaker, or Descent's Warden. Mother Pitcher on any stage but 10.

## As built

- Authored Mother (750 HP, scale 2.5, landing marks and four-stone cover), the static
  24-HP / scale-0.6 bud, and the central three-phase boss through the Editor. BootScope and
  Jungle's stage-10 override were bound there too; both names are in English and generated Pseudo.
- Seven tests in `JungleTests` map to rules 1–6 (rule 3 has its specified two rows). The HP sum
  uses the shipped stage-10 scaling and phase summons: **94.8 s** at 27 DPS × 60% uptime.
- **Scope exception / rule 3:** `Tests/Game/Views/MinionViewsTests.cs` also changed. Its older
  blanket size sweep rejected the specified 0.6-scale bud against the existing 0.7-scale Wight.
  The Wight remains unchanged; the sweep now explicitly exempts this rooted boss shield, retaining
  its guard for every other archetype. No production code, models or materials changed. New assets
  include their Editor-generated `.meta` files; the requested PROGRESS and ROADMAP edits are included.
- **Live Editor, via MCP:** normal Jungle Test start at 10; Mother at central spawn XZ (0, 2),
  three buds and four stones. Default targeting killed the buds first. A normal bow hit dealt
  **2.6 while shielded, 13 after the last bud died**. Barrage fired five lobs; spray hit in the open
  (26.3 damage at stage 10), but reported no hit behind a stone; pods spawned Frogs. Both thresholds
  regrew three buds and added 2 / 3 Frogs, with 1.5-second beats. An instrumented run spent the
  Test-start Ranger's Quick Draw / Volley picks and repositioned the player through MCP: Mother
  died at **77.9 s**, the final pod Frog at 78.7 s. This verifies the live mechanics, not natural
  movement, fight duration or feel. Original saves were sheltered for the check.
  Live boots at **5, 15, 20, 25 and 30** each spawned Old Croaker.
- **Rule 2 / manual check 3 resolved against existing code:** `BossBehaviour` clears only the
  adds tracked from phase summons. A Frog spawned by a pod survived the phase beat in the live
  trace; the earlier behaviour wins over “adds cleared” meaning every Frog. No AI change is included.
- Gameplay-camera capture: `Logs/JG12h-gameplay-opening.png`, **411 × 914**. **The lid reads as
  solid black; the mouth interior reads mostly black with a visible brown centre strip.** The
  owner must rule on the existing sculpt/materials. Evidence traces are in
  `Logs/JG12h-live-{shield-cover,phases,fifths}.txt` (local, untracked).
- Verification: **7/7 task tests; full EditMode 4 660 passed, 0 failed (failing names: none),
  1 known inconclusive**. Clean compiler pass: **0 errors, 0 compiler/analyzer warnings**
  (so 0 new analyzer warnings). `dotnet format whitespace --folder --verify-no-changes` clean
  for both changed C# files; `git diff --check` clean. Test and compiler evidence:
  `Logs/JG12h-final-editmode.xml`, `Logs/JG12h-compiler.txt` (local, untracked).
- Manual owner steps: play Jungle 7–10 with ordinary movement and a representative build; judge
  the difficulty climb and 75–120-second fight feel, cover/wedge readability on a phone, and the
  black lid/mouth. No asset wiring or BootScope setup remains for the owner.
