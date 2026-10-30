# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 6 000 bytes.**

_Updated 2026-10-30, after JG-13f._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; `m6` owner to tag. |
| **Last merged** | **JG-13f**: green pods fly, mark landings, swell and hatch. |
| **Next task** | None planned; owner to set direction. |
| **Verified** | EditMode total 4 704: 4 703 pass, 0 fail (names: none), 1 known inconclusive. Compiler errors 0; analyzer warnings 0; dotnet format clean. |

## What works

Portrait. Home: Heroes, Places. **Jungle** first, 30 stages. **Focus build:**
Veilrot, Sanctum, Focus disc off (mode flags); class select = Ranger alone (BootScope list). Stages:
threat-budgeted telegraphed waves, then a door; boss every fifth (Descent: Warden; Jungle: Old Croaker,
except 10 = Mother Pitcher). Mother: central/rooted, three shielding buds per phase (facing her), five-lob barrage,
stone-blocked spray, green pods with landing rings that swell for 1 s then hatch Frogs, 66/33% beats. Scripted Jungle stages: 1 = 3 Frogs, 2 = 2 Pitchers, 3 = 2 Pitchers + 3 Frogs,
4 = 2 Puffballs, 6 = 2 Panthers; 7–9 = mixed waves of all four. Frog, Pitcher (`PitcherSpit` lobs),
Puffball and Panther have their own bodies; Descent keeps capsules. **Ranger:** pale arrow trails,
cyan Volley per enemy, Space roll, upper-body flinch; first arrow ~¼ s after stopping. Levelling:
points, paused tree picks. Death pays Shards. **DT-01:** menu Test start; `TestStart._on` hides it.
Saves: run v4, profile v5; older load. Clash-style UI; all strings `LocKey`.

## Owed to the owner (Editor checks)

1. Pause popup: ribbon, Resume, View Tree (points), Quit.
2. Run end: death screen; Jungle clear via debug jump to 30.
3. Telegraph rings are round discs with an edge; player cyan matches the health bar.
4. Heroes tab / hero page: avatar fills frame; page scrolls all ranks; drag turns model.
5. JG-01/02: stage 1 = 3 Frogs, third kill → level 2; stage 2 Pitchers lob dodgeable goo, no stray trail.
6. RS-07a/b: bigger arrows, short cyan streak; Volley fan with glowing heads and bigger burst.
7. JG-03: stage 4 Puffballs swell, burst in spores inside the ring; killed at range bursts too.
8. JG-04: `Croaker.fbx`; stage 5 Croaker hops, slams, dodgeable tongue, ~1 min; Descent 5 still Warden.
9. JG-05: walled Pitcher takes every arrow; Frogs hop-sit-hop; Croaker never walks; two steps clear a Puffball.
10. JG-06: tongue shoots down the lane; every landing jolts the camera and bursts dust; ground-wave band with earth ridge (Warden too).
11. Jungle: open floors, no flakes, enemies path round edges.
12. JG-07b/12h: judge Panther/Mother sculpts. Mother green, chartreuse rim/teeth, no black; stage 10.
13. JG-07d: stage 6 Panthers warn, crouch, bound (no tongue), 14 damage; cat reads on dark floor; appear from 7.
14. RS-08a: stand still 5 s near a Frog — one steady beat; run, stop — fast draw, arrow in ~¼ s.
15. RS-08b: stage 1 sixth shot sends three arrows, one per Frog; a late wave sends one per enemy in range.
16. `Soulvail/Capture Look`: every screen at 411 × 914 and 360 × 780.
17. DT-01: Start stage 12 → tree open with points; `_on` off → panel hidden, Play/Continue unchanged.
18. RS-09/09a: stage 1 Frog bites while shooting — one flinch, then normal draw/release; running legs unchanged; roll hits never flinch.
19. JG-09: shoot a Frog (stage 1) and a Panther (stage 6) without killing — one jerk per arrow, then walk and idle as before.
20. Arrow fix: stage 1/3 mid-hop Frogs — visible arrow contact hurts; empty ground does not.
21. JG-10: stage 1 Frog - light fill shortens, white chunk holds then drains over a dark track, bar gone after 2 s; stage 6 Panther - readable shortening bar.
22. JG-11: Test start 7–9: judge the mixed-wave climb/fairness and stage 9's door after wave 3.
23. JG-12h: stage 10 duration/feel, phone wedge/stone readability.
24. JG-13a: stage 10 wedge grows 2.5 s; stone shadow is safe; stream after the blow at 1x.
25. JG-13b/c/d: stage 10 buds face her, lean, pulse; buds/phase Frogs clear stones, floor edges and player. Stage 2 Pitchers and boss rings unchanged.
26. JG-13e/f: stage 10 pod arcs with filling ring, lands clear, swells ~1 s, hatches Frog; landing hurts. Render checked; live/device pending.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4's band; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past stage ~26 late waves are all Bloaters/Puffballs (`WaveComposer.Upgrade`).
4. No audio, no Elites, three bosses; no feel pass, no second language.

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
11. Mother phase beats clear phase summons only; pod Frogs can survive (existing code; JG-12h As built).
## Device

Editor only (BlueStacks Vulkan crash). DPI 120 vs phone ~400 ([Traps §9](../Traps.md#9-rendering-canvas-and-tmp)). Deferred: multi-touch, kill-from-recents,
haptics, latency, frame time at depth, thermal, screen-open cost, hero-sheet scroll feel.
Store: `com.soulvail.dev`, `Soulvail` (placeholders); business model unruled.
