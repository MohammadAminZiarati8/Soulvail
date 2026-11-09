# SF-03 — The Grove: a third Jungle arena, so no stage repeats the last

**Size:** M · **Depends on:** SF-02 · **Branch:** `sf-03-jungle-third-arena`
**Design refs:** GD §7.1–7.2

## Goal

No two Jungle stages in a row share an arena. Today the roster opens one room per span (Hollow 1–4, Clearing 5+), so `ArenaFor`'s no-repeat rule never gets to act.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Arenas/Jungle/Arena_Grove.prefab` | — | new; rules 2–3 |
| `Prefabs/Arenas/Jungle/NavMesh-Arena_Grove.asset` | — | new; baked |
| `Data/Modes/Jungle.asset` | — | rule 1 |
| `Tests/Game/Content/JungleArenaRotationTests.cs` | Tests.Game | new |
| *small edits* | | `RunScope._arenaPrefabs` in `Run.unity` gains the Grove; `ArenaViewTests` shipped-arena lists gain it |

Allows `.meta` files and test count/coverage updates.

## Public API

None — content only (`arena.jungle.grove`).

## Behaviour

1. Roster: Hollow 1–4, Grove from 2, Clearing from 5, all open-ended past their first stage except Hollow.
2. The Grove is 36 × 36 like the Clearing (`_halfSize` 18, 18), so every boss's stones and Thornheart's 14 m ring fit. Square, run south to north, start, gate and entry (SF-02) placed as in the Clearing; open (no cover pillars), GD §7.2.
3. It must look like another place at first glance: KayKit Jungle dressing (a different ground tint and canopy edge from the Clearing), and no new models.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_NoStageRepeatsTheLast` | Jungle asset, seeds 1–200 / `ArenaFor(s)` vs `ArenaFor(s−1)`, s 2–30 / never equal |
| `Jungle_EveryRosteredArenaIsShipped` | roster / `_arenaPrefabs` / each id found |
| `Grove_IsValid` | prefab / `DescribeFaults` / empty, `_halfSize` 18, 18 |

## Manual verification (Editor / device)

1. **[Editor]** Stages 1–6 with DT-01: the arena changes every stage.
2. **[Editor]** Stages 10, 15, 20, 25, 30 in the Grove (pick a seed that lands there): stones and Thornheart's ring sit inside the walls.

## Out of scope

- More Jungle rooms (GD §7.2 targets 8–12 per biome); a cross-run repeat rule.
