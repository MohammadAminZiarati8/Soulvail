# M7-05m — Frogs instead of Rootlings

**Size:** S · **Depends on:** M7-05l (the Frog's body), M7-05i (a Jungle run) · **Branch:** `m7-05m-frogs-instead-of-rootlings`
**Design refs:** GD §8.1, GD §8.2 · **Ledger rows:** none

The second half of the owner's go of 2026-09-29: *"now use this frog as enemies for the first stage
… I mean use the frog instead of what we have now."*

## Goal

A Jungle run meets Frogs from its first wave, where it met Rootlings.

## Why this shape

- **The Frog plays the Husk's role, as the Rootling did.** The first stage's archetype is the Husk
  (GD §8.2), and the owner asked for the Frog in the Rootling's place, not in the Lunger's. So
  `enemy.frog` is M7-05i's ruling applied again: its own `ContentId` with the Husk's numbers,
  pinned equal by the same test row.
- **The Rootling leaves the catalog, not the repository.** Every bodied archetype `BootScope` lists
  is prewarmed to a pool of 29 at every Run load, rostered or not (M7-05g). A Rootling no mode
  rosters would cost 29 idle bodies a run. Its asset, prefab, model and tests stay, so rostering it
  again is two lines.
- **Nothing saved names it in a way that breaks.** A run's save holds no enemies. A profile's met
  archetypes may hold `enemy.rootling`, which no longer matches anything and only means the Frog is
  met as new, once.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Frog.asset` | — | **New.** `enemy.frog`, the Husk's numbers, `Frog.prefab` as its body (rule 1) |
| *small edits* | | `Data/Modes/Jungle.asset` (the roster's first row); `Prefabs/Composition/BootScope.prefab` (*Enemies*: the Frog where the Rootling was); `Data/Localisation/English.asset` and `Pseudo.asset` (+ `enemy.frog.name`); `Tests/Game/Authoring/JungleTests.cs` (rules 1–3) |

## Behaviour

1. **`enemy.frog` plays the Husk's role.** Every number on its spec equals the Husk's: behaviour,
   hit points, speed, priority, cost, experience, contact damage, reach, wind-up, recovery and aggro
   range. Its name is its own, "Frog". Its look names `Frog.prefab`, white, at scale 1.
2. **The Jungle rosters the Frog at stage 1.** Its roster is the Frog at 1, the Spitter at 2 and the
   Bloater at 4. Everything else about the Jungle is as M7-05i left it.
3. **`BootScope` lists the Frog and not the Rootling.** The Rootling stays authored, with its Husk's
   numbers and its own body.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `JunglesHusk_PlaysTheHusksRole` (Frog, Rootling) | each asset and the Husk / `ToSpec()` / every number equal; its own id and name (rules 1, 3) |
| `JunglesHusk_WearsItsOwnBody` (Frog, Rootling) | each asset / `ToLook()` / its own prefab, white, scale 1 (rules 1, 3) |
| `Jungle_RostersItsOwnHusk` | the Jungle / its roster / Frog 1, Spitter 2, Bloater 4 (rule 2) |
| `Boot_TheJungleIsTheFirstPlace` | `BootScope.prefab` / its *Modes* and *Enemies* / Jungle then Descent; the Frog listed, the Rootling not (rule 3) |
| *(unchanged)* the content, mode and localisation sweeps | the new asset and rows / swept / pass (rule 1) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → Descend. *Expected:* stage 1 is the Hollow, and wave 1 is Frogs. They
   hop at the player, glow red-orange over the wind-up and lash the tongue at the blow, flash white
   and flinch when hit, and flip belly up and fade when killed. Spitters join at 2, Bloaters at 4.
2. **[device]** Frame time in the Hollow at stage 4 with 12 animated Frogs. Deferred with every
   device row.

## Out of scope

- A place-select screen, and the Rootling in another place or a later stage of this one.
- The Frog as the Lunger (M7-01a), whose `Leap` it already carries.

## As built

**As specified.** The data edits are one line each: the roster's first `_specId`, `BootScope`'s
*Enemies* entry swapped in place, and two English rows appended by hand, as M7-05i did.
`Frog.asset` is a copy of `Rootling.asset` with its id, name key and body changed. `Pseudo.asset` is
the generator's output, two rows longer. `JungleTests`' two Rootling rows became `JunglesHusk_*`,
each run for both the Frog and the Rootling.

**What a player gets.** A new run, and a resumed Jungle run from its next wave, meets Frogs where it
met Rootlings. A profile that has met the Rootling meets the Frog as new once, and `ShardPayout` pays
that first meeting. Putting the Rootling's row back in `Jungle.asset` and its asset back in
`BootScope`'s *Enemies* undoes it, with no code.

**Verified.** With M7-05j to M7-05l in the tree: EditMode 3 459, of which 3 458 passed, 0 failed and
1 was inconclusive (the animator clock row, by construction); PlayMode 67 / 67, whose boot paths
start Jungle runs and so build, inject and spawn the Frog's pool. The format check is clean on every
changed C# file. `ProjectSettings/TimeManager.asset`, rewritten by the Editor, was reverted.
