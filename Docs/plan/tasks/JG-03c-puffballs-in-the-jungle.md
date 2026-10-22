# JG-03c — Puffballs in the Jungle

**Size:** S · **Depends on:** JG-03b (the Puffball's body), JG-01 (scripted stages), M7-05q (the pattern) · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §8.2

The last step of the owner's go of 2026-10-22: *"Stage 4 of the Jungle: one wave, 2 Puffballs, nothing
else (Scripted Stages on Jungle.asset). Stages 5+ stay on the budget, with the Puffball in the roster
where the Bloater was (stage 4)."*

## Goal

A Jungle run meets Puffballs from stage 4, where it met rust-coloured Bloater capsules, and stage 4 is
exactly two of them.

## The ruling, and why

- **The Puffball is its own archetype, `enemy.puffball`, with the Bloater's numbers**: the ruling M7-05q
  made for the Pitcher, a fourth time. `JunglesBloater_PlaysTheBloatersRole` pins every number equal,
  the explosion's radius included, so the two part only when someone edits that row.
- **The Bloater stays listed.** Descent still rosters its capsule, so `BootScope` lists both. The
  Puffball's pool is the cost: every bodied archetype listed is prewarmed at every Run load (M7-05g).
- **Its look is white at scale 1**, as every Jungle body's is: the model carries its colour and the
  prefab its size. The Bloater's capsule wore rust at 1.35.
- **Stage 4 is the Jungle's fourth scripted stage**: two Puffballs, one wave. Stage 4 is no boss stage
  (every fifth is), so `ModeSpec` takes it.
- **Nothing saved breaks.** A run's save holds no enemies; a profile meets the Puffball as new, once.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Puffball.asset` | — | **New.** `enemy.puffball`, the Bloater's numbers, `Puffball.prefab` as its body (rule 1) |
| *small edits* | | `Data/Modes/Jungle.asset` (the roster's third row; stage 4 scripted); `Prefabs/Composition/BootScope.prefab` (*Enemies*: + the Puffball); `Data/Localisation/English.asset` and `Pseudo.asset` (+ `enemy.puffball.name`); `Tests/Game/Authoring/JungleTests.cs` (rules 1–4) |

## Behaviour

1. **`enemy.puffball` plays the Bloater's role.** Every number on its spec equals the Bloater's:
   behaviour, hit points, speed, priority, cost, experience, elite flag, contact damage, reach, wind-up,
   recovery, aggro range and explosion radius, and neither has a projectile. Its name is its own,
   "Puffball". Its look names `Puffball.prefab`, white, at scale 1.
2. **The Jungle rosters the Puffball at stage 4.** Its roster is the Frog at 1, the Pitcher at 2 and
   the Puffball at 4.
3. **The Jungle scripts stage 4 as `enemy.puffball × 2`, one wave.** Stages 1–3 are as JG-01 and
   2026-10-22 left them; stage 5 on is the budget's.
4. **`BootScope` lists the Puffball, and still lists the Bloater.**

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `JunglesBloater_PlaysTheBloatersRole` | both assets / `ToSpec()` / every number equal, the explosion included; its own id and name (rule 1) |
| `JunglesEnemy_WearsItsOwnBody` (+ Puffball) | each asset / `ToLook()` / its own prefab, white, scale 1 (rule 1) |
| `Jungle_RostersItsOwnEnemies` | the Jungle / its roster / Frog 1, Pitcher 2, Puffball 4 (rule 2) |
| `Jungle_ScriptsItsFirstFourStages` | the Jungle / scripted stages / 1–3 as before, 4 is Puffball × 2 in wave 1, 5 not scripted (rule 3; was `Jungle_ScriptsItsFirstThreeStages`) |
| `Boot_TheJungleIsTheFirstPlace` | `BootScope.prefab` / *Enemies* / the Puffball and the Bloater listed, as well as before (rule 4) |
| *(unchanged)* the content, mode and localisation sweeps | the new asset and rows / swept / pass (rule 1) |

## Manual verification (Editor / device)

1. **[Editor]** A new Jungle run, to stage 4 (the debug jump, or play through): exactly two Puffballs
   telegraph in and nothing else. They waddle at the player, and close in swell with the cap glowing
   cyan-white, then burst in a cream puff of spores inside the red-orange blast ring. A hit squashes the
   cap; one killed at range collapses and bursts the same way.
2. **[Editor]** Stage 5 and on: the budget's waves, with Puffballs where Bloaters were.
3. **[device]** Frame time with several bursts at once. Deferred with every device row.

## Out of scope

- The Puffball in `EnemyShowcase.unity` (M7-05r's scene) — not asked for.
- Its hit volume: a Jungle body is the Husk's colliders at scale 1, as the Frog and the Pitcher are,
  where the Bloater's capsule was the Husk's at 1.35.

## As built

**As specified**, in one change. `Puffball.asset` is a copy of `Bloater.asset` with its id, name key,
look and body changed and nothing else, so every number is the Bloater's by construction and
`JunglesBloater_PlaysTheBloatersRole` keeps it so. The roster row and the stage-4 script were written
through `SerializedObject`, the `BootScope` list through the prefab's contents. The name is "Puffball";
its pseudo row `[Pûffbáll ···]` follows the table's rule (accented vowels, one dot per three letters
plus one).

**Findings.** *(1)* **Stage 4 pays 48 XP** (two Puffballs at the Bloater's 24), where the budget's stage 4
was a mix. *(2)* **`WaveComposer.Upgrade` now fills a capped late Jungle wave with Puffballs** (PROGRESS,
*What does not work yet* 4): the dearest archetype in the Jungle's roster is the Puffball, at the
Bloater's cost of 8.

**Deviations.** None.

**Verified.** EditMode suite through `TestRunnerApi`: **4 411 — 4 409 passed / 1 failed / 1
inconclusive**, the two known rows (`CharacterPreviewTests.Resize`, and the inconclusive
`PlayerAnimatorViewTests` row). `JungleTests` 18 / 18, two of them new: the Puffball's role row and its
body case.
