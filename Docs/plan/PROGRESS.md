# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 6 000 bytes.**

_Updated 2026-10-30, after JG-12g._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; `m6` owner to tag. |
| **Last merged** | **JG-12g**: prebuilt archetype cover; four Editor-built KayKit stones, sight and room tests. |
| **Next task** | **JG-12h**: Mother assets, stage 10, cover binding and live fight check; playtest 7–9 due. |
| **Verified** | JG-12g: 9/9; EditMode 4 653 pass, 0 fail (names: none), 1 known inconclusive. Compiler errors 0; new analyzer warnings 0; format clean. |

## What works

Portrait run. Home (Heroes, Places); the **Jungle** is the first place, 30 stages. **Focus build:**
Veilrot, Sanctum, Focus disc off (mode flags); class select = Ranger alone (BootScope list). Stages:
threat-budgeted telegraphed waves, then a door; boss every fifth (Warden; Jungle: Old Croaker — hop-slam,
lane tongue). Scripted Jungle stages: 1 = 3 Frogs, 2 = 2 Pitchers, 3 = 2 Pitchers + 3 Frogs,
4 = 2 Puffballs, 6 = 2 Panthers; 7–9 = mixed waves of all four. Jungle enemies: Frog (`Rig_Frog`), Pitcher (lobs `PitcherSpit`),
Puffball (Bloater role), Panther (Lunger). Descent keeps the capsules. **Ranger:** own model, pale arrow trails, cyan Volley per enemy, Space roll, upper-body flinch; steady rate, first arrow ~¼ s after stopping. **Levelling:** points, paused tree picks.
Death pays Shards. **DT-01:** dev-only Test start, menu top-left; Menu `TestStart._on` hides it.
Saves: run v4, profile v5; older load. Clash-style UI; all strings `LocKey`.

## Owed to the owner (Editor checks)

1. Pause popup: ribbon, Resume, View Tree (points), Quit.
2. Run end: death screen; Jungle clear via debug jump to 30.
3. Telegraph rings are round discs with an edge; player cyan matches the health bar.
4. Heroes tab / hero page: avatar fills frame; page scrolls all ranks; drag turns model.
5. JG-01/02: stage 1 = 3 Frogs, third kill → level 2; stage 2 Pitchers lob dodgeable goo, no stray trail.
6. RS-07a/b: bigger arrows, short cyan streak; Volley fan with glowing heads and bigger burst.
7. JG-03: stage 4 Puffballs swell, burst in spores inside the ring; killed at range bursts too.
8. JG-04: `Croaker.fbx` preview; stage 5 Old Croaker hops in, slams, lane tongue dodgeable, ~1 min fight; Descent stage 5 still the Warden.
9. JG-05: walled Pitcher takes every arrow; Frogs hop-sit-hop; Croaker never walks; two steps clear a Puffball.
10. JG-06: tongue shoots down the lane; every landing jolts the camera and bursts dust; ground-wave band with earth ridge (Warden too).
11. Jungle arenas have open floors, no falling flakes; enemies path round the edges.
12. JG-07b/JG-12e: rule on Panther/Mother sculpts. `Logs/JG12e-gameplay-*.png`: black lid, brown mouth/black center. Live run due JG-12h.
13. JG-07d: stage 6 Panthers warn, crouch, bound down the strip (no tongue), 14 damage; read as a cat on the dark floor; appear from stage 7 on.
14. RS-08a: stand still 5 s near a Frog — one steady beat; run, stop — fast draw, arrow in ~¼ s.
15. RS-08b: stage 1 sixth shot sends three arrows, one per Frog; a late wave sends one per enemy in range.
16. `Soulvail/Capture Look` at 411 × 914 and 360 × 780 of every portrait screen.
17. DT-01: Start stage 12 → tree open with points; `_on` off → panel hidden, Play/Continue unchanged.
18. RS-09/09a: stage 1 Frog bites while shooting — one flinch, then normal draw/release; running legs unchanged; roll hits never flinch.
19. JG-09: shoot a Frog (stage 1) and a Panther (stage 6) without killing — one jerk per arrow, then walk and idle as before.
20. Arrow fix: stage 1/3 mid-hop Frogs — visible arrow contact hurts; empty ground does not.
21. JG-10: stage 1 Frog - light fill shortens, white chunk holds then drains over a dark track, bar gone after 2 s; stage 6 Panther - readable shortening bar.
22. JG-11 Test start: 7 = 4 Frogs → Panther + 3 Frogs; 8 = 2 Pitchers + 3 Frogs → 2 Panthers + 3 Frogs; 9 = 3 Frogs + 2 Puffballs → 2 Pitchers + Panther → 2 Panthers + 2 Frogs; door after wave 3. Judge climb/fairness.

23. JG-12f/g at JG-12h: bind Mother's Cover to `Cover_MotherStones`; 411 × 914 — grey stones read on the floor, Frogs path around them, safe spray shadows and lob rings.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4's band; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past stage ~26 late waves are all Bloaters/Puffballs (`WaveComposer.Upgrade`).
4. No audio, no Elites, two bosses; no feel pass, no second language.

## Known issues

1. `ArenaView.OnValidate` warns "0 spawn point(s)" on reload — harmless.
2. `PlayerStat.ContactDamage` refused by `PlayerStats.Resolve` — awaiting a ruling.
3. `TimeManager.asset` re-serialises on Editor open; revert before every commit.
4. A save the tree refuses (`SkillTree.Restore` throws) strands the player; Continue offers the dead run.
5. Save with huge XP (`1e38`) hangs `LevelTracker`.
6. A bolt in the air at a stage boundary leaves its body (`ProjectileViews`, no `StageArrived` teardown).
7. A pooled body teleported by `Bind` can snap back on first `Move` (`autoSyncTransforms` 0).
8. `SanctumOpened` publishes before `IsSanctumOpen`; presenter draws a frame late.
9. Nothing damages a Wight. The Knight's Charge root curve snaps 0.22 m.
10. HUD ~15 draw calls (no sprite atlas). No PlayMode row taps Continue. Disk near full.
## Device

Editor only (BlueStacks crashes in Vulkan). `Screen.dpi` 120 vs phone ~400 ([Traps §9](../Traps.md#9-rendering-canvas-and-tmp)). Deferred: multi-touch, kill-from-recents,
haptics, latency, frame time at depth, thermal, screen-open cost, hero-sheet scroll feel.
Store placeholders: `com.soulvail.dev`, company `Soulvail`; business model unruled.
