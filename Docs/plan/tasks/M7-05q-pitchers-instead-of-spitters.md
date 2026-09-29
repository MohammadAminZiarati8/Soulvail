# M7-05q — Pitchers instead of Spitters

**Size:** S · **Depends on:** M7-05p (the Pitcher's body), M7-05m (the Jungle's Frog, the pattern) · **Branch:** `m7-05q-pitchers-instead-of-spitters`
**Design refs:** GD §8.1, GD §8.2 · **Ledger rows:** none

The last step of the owner's go of 2026-09-29: *"The Jungle's Spitter is currently the capsule
enemy.spitter; decide from M7-05i/M7-05m's precedent whether the plant gets its own id with the
Spitter's numbers."*

## Goal

A Jungle run meets Pitchers from stage 2, where it met grey-capsule Spitters.

## The ruling, and why

- **The Pitcher is its own archetype, `enemy.pitcher`, with the Spitter's numbers**: M7-05i's ruling
  for the Rootling and M7-05m's for the Frog, a third time. Identity is a `ContentId`, a look is keyed
  by it, and a place's enemies are its mode's roster, so the Jungle's Spitter is one roster row naming
  a different id. `JunglesSpitter_PlaysTheSpittersRole` pins every number equal, the wind-up and the
  projectile block included, so the two part only when someone edits that row. The rejected
  alternative, a skin keyed by mode and archetype, is the second identity M7-05i already refused.
- **The Spitter stays listed.** Unlike the Rootling at M7-05m, `enemy.spitter` is still rostered, by
  Descent, as a capsule, so `BootScope` lists both. The cost is the Pitcher's pool: every bodied
  archetype `BootScope` lists is prewarmed to 29 bodies at every Run load (M7-05g), and the Pitcher is
  one more. The Spitter's capsule has no body of its own and adds none.
- **Nothing saved breaks.** A run's save holds no enemies. A profile that has met the Spitter meets
  the Pitcher as new, once, and `ShardPayout` pays that first meeting.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Pitcher.asset` | — | **New.** `enemy.pitcher`, the Spitter's numbers, `Pitcher.prefab` as its body (rule 1) |
| *small edits* | | `Data/Modes/Jungle.asset` (the roster's second row); `Prefabs/Composition/BootScope.prefab` (*Enemies*: + the Pitcher); `Data/Localisation/English.asset` and `Pseudo.asset` (+ `enemy.pitcher.name`); `Tests/Game/Authoring/JungleTests.cs` (rules 1–3) |

## Behaviour

1. **`enemy.pitcher` plays the Spitter's role.** Every number on its spec equals the Spitter's:
   behaviour, hit points, speed, priority, cost, experience, contact damage, reach, wind-up, recovery,
   aggro range, and the projectile's standoff, speed and radius. Its name is its own, "Pitcher". Its
   look names `Pitcher.prefab`, white, at scale 1.
2. **The Jungle rosters the Pitcher at stage 2.** Its roster is the Frog at 1, the Pitcher at 2 and
   the Bloater at 4. Everything else about the Jungle is as M7-05m left it.
3. **`BootScope` lists the Pitcher, and still lists the Spitter.**

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `JunglesSpitter_PlaysTheSpittersRole` | both assets / `ToSpec()` / every number equal, the projectile block included; its own id and name (rule 1) |
| `JunglesEnemy_WearsItsOwnBody` (Frog, Rootling, Pitcher) | each asset / `ToLook()` / its own prefab, white, scale 1 (rule 1; was `JunglesHusk_WearsItsOwnBody`) |
| `Jungle_RostersItsOwnEnemies` | the Jungle / its roster / Frog 1, Pitcher 2, Bloater 4 (rule 2; was `Jungle_RostersItsOwnHusk`) |
| `Boot_TheJungleIsTheFirstPlace` | `BootScope.prefab` / its *Modes* and *Enemies* / Jungle then Descent; the Frog, the Pitcher and the Spitter listed, the Rootling not (rule 3) |
| *(unchanged)* the content, mode and localisation sweeps | the new asset and rows / swept / pass (rule 1) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → Descend, to stage 2. *Expected:* Pitchers shuffle in and stop at range,
   lean back with the lid flipping open and glowing red-orange, snap forward as the glob leaves,
   flash and flinch when hit, and topple onto their side and fade when killed. Frogs are still stage
   1's, Bloaters join at 4.
2. **[device]** Frame time in the Hollow at stage 4 with Frogs and Pitchers animated together.
   Deferred with every device row.

## Out of scope

- The glob leaving the lid rather than the plant's feet (M7-05p finding 2).
- The Pitcher in `EnemyShowcase.unity`, whose tests pin the Frog's row alone.
- Descent's Spitter, which keeps its capsule until the Ashen Reach gives it a body.

## As built

**As specified.** The data edits are one line each, as at M7-05m: the roster's second `_specId`, one
row appended to `BootScope`'s *Enemies*, and two English rows appended by hand. `Pitcher.asset` is a
copy of `Spitter.asset` with its id, name key, tint, scale and body changed. `Pseudo.asset` is the
generator's output (*Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale*), two rows longer. Two
`JungleTests` rows were renamed for a Jungle with two bodies of its own; the name "Pitcher" is one
English row, if the owner wants another.

**What a player gets.** A new run, and a resumed Jungle run from its next wave, meets Pitchers where
it met grey capsules at stage 2. Putting `enemy.spitter` back in `Jungle.asset`'s second row undoes
it, with no code.

**Verified.** With M7-05o to M7-05q in the tree: EditMode 3,508, of which 3,507 passed, 0 failed and
1 was inconclusive (the animator clock row, by construction), 24 over M7-05n's 3,484: RS-06d's 7 and
these tasks' 17. PlayMode 66 / 67 on the full pass, queued while Unity was not the active
application: `RangerSandboxTests.Loop_ShootsOnlyStandingStill` failed, Traps §8's known sandbox row,
and passed alone with the Editor raised. The boot paths start Jungle runs and so build, inject and
prewarm the Pitcher's pool. Console: no errors and no warnings. The format check is clean on every
changed C# file. `ProjectSettings/TimeManager.asset` and `ProjectSettings.asset`, rewritten by the
Editor, were reverted.
