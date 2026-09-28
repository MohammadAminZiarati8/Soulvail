# M7-05i — A Jungle run

**Size:** S · **Depends on:** M7-05d (spans), M7-05e (the Hollow), M7-05h (the Rootling's body) · **Branch:** `m7-05i-a-jungle-run`
**Design refs:** GD §4.5, GD §8.1, GD §8.2 · **Ledger rows:** none

The last task cut from the owner's go of 2026-09-27: *"decide, and report the reasoning: whether the
Rootling is a new EnemyDefinition (`enemy.rootling`, Husk numbers) or the Husk wearing the Jungle's
skin, and how a Jungle run gets it, given no Jungle ModeDefinition exists yet."*

## Goal

A run from class select is a Jungle run. It opens in the Hollow, moves to the Clearing from stage 5,
and meets Rootlings from its first wave.

## The two rulings, and why

- **The Rootling is its own archetype, `enemy.rootling`, with the Husk's numbers.** Content identity
  in this project is a `ContentId` (CLAUDE.md, rule 5). A look is keyed by that id, and a place's
  enemies are its mode's roster. The profile's met archetypes, which `ShardPayout` pays a first
  meeting from, also name that id. So *"the Jungle's Husk"* is a roster row naming a different id,
  and needs no new concept. A profile that has met the Husk meets the Rootling as new, once.
  The rejected alternative, a skin keyed by mode and archetype, invents a second identity beside the
  id, which every later place would have to use. It would show the Husk's name on a Rootling, and it
  would tie the Jungle's tuning to the Ashen Reach's. **The cost** is that the Husk's numbers are
  written twice. `Rootling_PlaysTheHusksRole` pins them equal, so they part only when someone edits
  that row, which is the moment to decide they should.
- **The Jungle gets its own `ModeDefinition`, `mode.jungle`, listed first in `BootScope`.** A place is
  a mode (the 2026-09-26 direction). A run plays the first mode the catalog holds, and there is no
  place-select screen yet. So the Jungle listed first is what makes class select start a Jungle
  run, and Descent listed second is what keeps a saved Descent run resuming.
  **What that changes for a player:** until the place-select screen, a new run is a Jungle run, and
  the grey boxes and the Ashen courtyard are reached only by a Continue. Moving Descent back above
  the Jungle in `BootScope.prefab`'s *Modes* list undoes it, with no code.
- **The Jungle is Descent's curves, economy, Sanctum, Ordeals and boss, with its own rooms and its
  Husk.** Only what makes it the Jungle differs: the Rootling at stage 1, the Hollow for stages 1–4
  and the Clearing from 5. The Spitter and the Bloater keep their grey capsules until their Jungle
  bodies exist. Descent keeps the Clearing too, until the Ashen Reach has a mode of its own.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Rootling.asset` | — | **New.** `enemy.rootling`, the Husk's numbers, `Rootling.prefab` as its body (rule 1) |
| `Data/Modes/Jungle.asset` | — | **New.** `mode.jungle` (rules 2, 3) |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | **New.** Rules 1–4 |
| *small edits* | | `Prefabs/Composition/BootScope.prefab` (+ Rootling in *Enemies*, + Jungle first in *Modes*); `Data/Localisation/English.asset` and `Pseudo.asset` (+ `enemy.rootling.name`, `mode.jungle.name`) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **`enemy.rootling` plays the Husk's role.** Every number on its spec equals the Husk's: behaviour,
   hit points, speed, priority, cost, experience, contact damage, reach, wind-up, recovery and
   aggro range. It is not an Elite. Its name is its own, and its look names `Rootling.prefab` with a
   white tint at scale 1, so the atlas reads as authored.
2. **`mode.jungle` is Descent's numbers with the Jungle's content.** Its scaling, levelling curve,
   Overflow, Essence, Sanctum, Ordeal schedule and pool, and boss roster equal Descent's. Its roster
   is the Rootling at 1, the Spitter at 2 and the Bloater at 4.
3. **A Jungle run opens in a small room.** Its arenas are the Hollow for stages 1–4 and the Clearing
   from stage 5. Every room open at stage 1 has a floor no wider than 26 m, and every room it
   rosters is shipped.
4. **A new run is a Jungle run, and a Descent run still resumes.** `BootScope`'s *Modes* list is the
   Jungle and then Descent. Its *Enemies* list carries the Rootling.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rootling_PlaysTheHusksRole` | both assets / `ToSpec()` / every number equal; ids and names differ (rule 1) |
| `Rootling_WearsItsOwnBody` | the asset / `ToLook()` / `Rootling.prefab`, white, scale 1 (rule 1) |
| `Jungle_IsDescentsNumbers` | both modes / `ToSpec()` / scaling, curve, Overflow, Essence, Sanctum, Ordeals and bosses equal (rule 2) |
| `Jungle_RostersItsOwnHusk` | the Jungle / its roster / Rootling 1, Spitter 2, Bloater 4 (rule 2) |
| `Jungle_OpensInASmallRoom` | the Jungle / its arenas and their prefabs / Hollow 1–4, Clearing from 5; each room open at stage 1 has a floor collider ≤ 26 m (rule 3) |
| `Jungle_EveryArenaItRostersIsShipped` | the Jungle / its arenas / each is an `ArenaView` prefab (rule 3) |
| `Boot_TheJungleIsTheFirstPlace` | `BootScope.prefab` / its *Modes* and *Enemies* / Jungle then Descent; the Rootling listed (rule 4) |
| *(unchanged)* `AllModeDefinitions_ValidUniqueIds`, `AllModeDefinitions_RosterIdsAreAuthored`, `EveryShippedMode_*`, `EveryLocKey_*`, `EveryFileName_MatchesTheLastSegmentOfItsId`, `Pseudo_*` | the new assets / the sweeps / pass (rules 1, 2) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → a class → descend. *Expected:* stage 1 is the Hollow, and wave 1 is
   Rootlings. They crouch-walk at the player in packs, rear up and glow red-orange before each blow,
   flash white when hit, and fall and fade when killed. Stages 1–4 are the Hollow, stage 5 is the
   Warden in the Clearing, and later stages are the Clearing.
2. **[Editor]** Its colours: the Rootlings pale against the green floor, with nothing cyan, violet
   or gold on them (GD §16.4).
3. **[device]** Frame time in the Hollow at stage 4 with 12 animated Rootlings. Deferred with every
   device row.

## Out of scope

- A place-select screen, which makes the *Modes* order stop mattering.
- The Jungle's own Spitter and Bloater, and its other rooms (M7-05).
- Descent giving up the Clearing, which waits for the Ashen Reach's own mode.

## As built

**As specified**, with one note. *(1)* **`English.asset` was restored and appended by hand.**
Saving it from the Editor re-serialised three untouched rows: a re-wrapped line, an escaped
em-dash, and a trailing space after `_locale:`. It is the same text, but three rows of noise, so the
file is `HEAD`'s with the two new rows added. `Pseudo.asset` is the generator's output
(*Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale*), two rows longer.

**What a player gets.** A new run from class select is a Jungle run: the Hollow for stages 1–4,
the Warden in the Clearing at stage 5, and the Clearing after. Rootlings come from the first wave,
and grey-capsule Spitters from stage 2 and Bloaters from 4. A saved Descent run still resumes into
Descent. Moving Descent back above the Jungle in `BootScope.prefab`'s *Modes* list undoes the
default, with no code. A profile that has met the Husk meets the Rootling as new once, and
`ShardPayout` pays its first meeting.

**Red-checked.** With one budget number changed in `Jungle.asset` (40 → 41), `JungleTests` ran
6 / 1. The failure was exactly `Jungle_IsDescentsNumbers`, which named the field and compared 21
values in the scaling block alone, so the comparison is not vacuous.

**Verified.** `JungleTests` 7 / 7. The content sweeps, `ModeDefinitionTests`, the localisation
sweeps and `InstallerTests` passed with the new assets: 148 / 148. The full suite ran with all six
tasks in the tree: EditMode 3 406, of which 3 405 passed, 0 failed, 1 inconclusive (the animator
clock row), and PlayMode 67 / 67, whose boot paths now start the Jungle. The manual steps are the
owner's to play.
