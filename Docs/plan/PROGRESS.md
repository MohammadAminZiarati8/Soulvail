# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 6 000 bytes.**

_Updated 2026-10-31, after JG-14f._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; `m6` owner to tag. |
| **Last merged** | **JG-14f**: spear, shield-up stab, 2 m reach. |
| **Next task** | None specified; owner playtest. |
| **Verified** | EditMode 4 786: 4 785 pass, 0 fail, 1 known clock inconclusive; JG-14f 9/9. Compiler errors 0; new analyzer warnings 0; format clean. |

## What works

Portrait; Heroes/Places. Jungle 30 stages; Veilrot, Sanctum, disc off; Ranger alone.
Threat-budgeted waves → door; fifth-stage bosses: Descent Warden, Jungle Croaker except 10 Mother.
Mother: rooted, three facing shield buds/phase, five-lob barrage, stone-blocked pulsing spray/goo,
ringed pods swell 1 s → Frogs, 66/33% beats. Scripts: 1 = 3 Frogs; 2 = 2 Pitchers;
3 = both; 4 = 2 Puffballs; 6 = 2 Panthers; 7–9 mixed waves. Stage 11: Mossguard, then one + four
Frogs; shield blocks, spear stabs (2 m), hit feedback. Stages 12–14: shield/spit, pincer, exam; 76 → 110 → 128
threat. All five have bodies; Pitcher lobs ringed; Descent capsules.
Ranger: pale trails, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. `TestStart._on` hides test start.
Boss bar one line, none overhead; boss adds pay no XP. Saves run v4/profile v5, older load.
Clash UI; `LocKey` strings.

## Owed to the owner (Editor checks)

1. Pause popup: ribbon, Resume, View Tree (points), Quit.
2. Run end: death screen; Jungle clear via debug jump to 30.
3. Telegraph rings are round discs with an edge; player cyan matches the health bar.
4. Heroes tab / hero page: avatar fills frame; page scrolls all ranks; drag turns model.
5. JG-01/02: stage 1 = 3 Frogs, third kill → level 2; stage 2 Pitchers lob dodgeable goo, no stray trail.
6. RS-07a/b: bigger arrows, short cyan streak; Volley fan with glowing heads and bigger burst.
7. JG-03: stage 4 Puffballs swell and burst in spores, at range too.
8. JG-04: `Croaker.fbx`; stage 5 Croaker hops, slams, dodgeable tongue, ~1 min; Descent 5 still Warden.
9. JG-05: walled Pitcher takes every arrow; Frogs hop-sit-hop; Croaker never walks; two steps clear a Puffball.
10. JG-06: tongue down the lane; landings jolt the camera, burst dust; ground-wave earth ridge.
11. Jungle: open floors, no flakes, enemies path round edges.
12. JG-07b/12h: judge Panther/Mother sculpts (green, chartreuse rim, no black).
13. JG-07d: stage 6 Panthers warn, crouch, bound (no tongue), 14 damage; cat reads on dark floor; appear from 7.
14. RS-08a: still 5 s near a Frog — one beat; run, stop — arrow in ~¼ s.
15. RS-08b: stage 1 sixth shot sends three arrows, one per Frog; a late wave sends one per enemy in range.
16. `Soulvail/Capture Look`: every screen at 411 × 914 and 360 × 780.
17. DT-01: Start stage 12 → tree open with points; `_on` off → panel hidden, Play/Continue same.
18. RS-09/09a: Frog bite while shooting — one flinch, then normal draw; roll hits never flinch.
19. JG-09: shoot a Frog (stage 1) and a Panther (stage 6) without killing — one jerk per arrow, then walk and idle as before.
20. Arrow fix: mid-hop Frogs — visible contact hurts, empty ground not.
21. JG-10: stage 1 Frog bar: fill shortens, white chunk holds then drains, gone after 2 s; stage 6 Panther bar reads.
22. JG-11: Test start 7–9: judge mixed-wave climb/fairness, stage 9's door after wave 3.
23. JG-12h: stage 10 duration and feel.
24. JG-13a: stage 10 wedge grows 2.5 s; stone shadow is safe; stream after the blow at 1x.
25. JG-13b/c/d: stage 10 buds face her, lean, pulse; buds/phase Frogs clear stones, floor edges and player.
26. JG-13e/f: stage 10 pod arcs, ring fills, lands clear, swells ~1 s, hatches Frog; landing hurts.
27. Fixes: stage 10 bar one line; rings/lanes/waves show on stone; Pitcher lobs ringed.
28. JG-13g: stage 10 fan reads on grass/stone, beats faster; goo splats floor/stone, < 1 s; kill mid wind-up, fan goes; no bar over her.
29. Stage 5/10: boss adds and pod Frogs give no XP.
30. JG-14b-f: stage 11 shield/reticle, roll round, 2 m stab/shield up, sparks/jolt; wave-two feel.
31. JG-14e: start 12–14; check mixes, wave-3 door at 14; judge climb 11→15.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4's band; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past stage ~26 late waves are all Bloaters/Puffballs (`WaveComposer.Upgrade`).
4. No audio, no Elites, three bosses; no feel pass, no second language.

## Known issues

1. `ArenaView.OnValidate` warns "0 spawn point(s)" on reload; harmless.
2. `PlayerStat.ContactDamage` refused by `PlayerStats.Resolve` — awaiting a ruling.
3. `TimeManager.asset` re-serialises on Editor open; revert before every commit.
4. A save the tree refuses (`SkillTree.Restore` throws) strands the player; Continue offers the dead run.
5. Save with huge XP (`1e38`) hangs `LevelTracker`.
6. A bolt in the air at a stage boundary leaves its body (`ProjectileViews`, no `StageArrived` teardown).
7. A pooled body teleported by `Bind` can snap back on first `Move` (`autoSyncTransforms` 0).
8. `SanctumOpened` publishes before `IsSanctumOpen`; presenter draws a frame late.
9. Nothing damages a Wight. The Knight's Charge root curve snaps 0.22 m.
10. HUD ~15 draw calls (no atlas). No PlayMode row taps Continue. Disk near full.
11. Mother phase beats clear phase summons only; pod Frogs can survive (existing code; JG-12h As built).
## Device

Editor only (BlueStacks Vulkan crash). DPI 120 vs phone ~400 ([Traps §9](../Traps.md#9-rendering-canvas-and-tmp)). Deferred: multi-touch, kill-from-recents,
haptics, latency, frame time at depth, thermal, screen-open cost, hero-sheet scroll feel.
Store: `com.soulvail.dev`, `Soulvail` (placeholders); business model unruled.
