# PL-01b — The Packleader authored

**Size:** M · **Depends on:** PL-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Packleader); CH §3.5 (path rules); MK-02b, EA-01c

## Goal

From level 11 the Beastmaster can choose the Packleader, and Pack Hounds is its first skill, with
pack hounds on the floor.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Paths/Packleader.asset` | — | **New.** `path.packleader`: one skill row, Pack Hounds at Lv 11. `PathDefinition` refuses a row with no ranks, so PL-02b to PL-05c append their rows (11/14/14/17/20) |
| `Data/Skills/Packleader/PackHounds*.asset` | — | **New.** Four ranks |
| `Data/Effects/Packleader/PackHounds*.asset` | — | **New.** Their `ModifyStat`s |
| `Data/Characters/Beastmaster.asset` | — | First path slot → Packleader; `_pathOpensAtLevel` 11 |
| `Game/Views/PetViews.cs` | Game | A non-lead body at 0.8 scale with its own smaller bar; `PetDespawned` returns that body and bar (PL-01a publishes it) |
| `Tests/Game/Authoring/PackleaderPathTests.cs` | Tests.Game | **New** |
| *small edits* | | `English`/`Pseudo` (path name, card line, skill lines); Pack Hounds' icon is `Icon_Placeholder`. No `PathLook` yet (PL-06); if a null look is refused, an empty `Data/Paths/PackleaderLook.asset`, named in *As built* |

## Behaviour

1. At level 11 the Beastmaster's path area shows the Packleader card; choosing it locks it for
   the run (MK-01b). The task checks the tree screen with one path slot filled of three; if the
   card row needs three, the empty slots show as "coming" cards. Name it in *As built*.
2. **Pack Hounds ranks:** 1 — `PackHounds` +1 (share 0.5); 2 — `PackShare` +0.15; 3 — `PackHounds`
   +1; 4 — `PackShare` +0.15.
3. Pack hounds are the Hound's body at 0.8 scale with its cyan collar, each with a smaller cyan bar.
4. The Pack Hounds row shows its unlock level, 11.
5. The path holds one skill (4 ranks) now; PL-02b to PL-05c append the other five rows.

## Tests

| Test | Given / When / Then |
|---|---|
| `Beastmaster_FirstPathIsPackleader_OpensAt11` (1) |
| `PackHounds_FourRanks_Numbers` (2) |
| `PackHoundBody_SmallerScale_OwnBar` (3) |
| `Path_OneRow_PackHoundsAt11` (4, 5) |
| `Skills_HaveLocKeys` (2) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at stage 12 with points: choose Packleader, learn Pack Hounds: a smaller hound
   joins; rank 3 brings a second; both heel and fall like the Hound.

## Out of scope

- Howl, Pack Tactics, Thick Pelts, Hawk, Stampede (PL-02 to PL-05).

## As built

_Filled at merge._
