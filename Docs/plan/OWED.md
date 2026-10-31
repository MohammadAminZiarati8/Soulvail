# Soulvail — Owed to the owner

Editor checks the owner still owes, and device facts. **Implementation sessions never read this
file:** a task appends its manual steps to the end of the list (`cat >> Docs/plan/OWED.md`); the
owner deletes items as they pass.

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

## Device

Editor only (BlueStacks Vulkan crash). DPI 120 vs phone ~400 ([Traps §9](../Traps.md#9-rendering-canvas-and-tmp)). Deferred: multi-touch, kill-from-recents,
haptics, latency, frame time at depth, thermal, screen-open cost, hero-sheet scroll feel.
Store: `com.soulvail.dev`, `Soulvail` (placeholders); business model unruled.

## MK-02b — Marksman authored (2026-10-31)

- [ ] [Editor] Confirm boot and a Jungle run look unchanged; no path selection UI is expected until MK-03. Automated boot/run smoke checks are reported in the commit body.
- [ ] [Editor] Recheck run-to-boot teardown: MK-02b's smoke batch hit MissingReferenceException in CoverViews.Dispose/Lift after Jungle start. Boot and run-start checks passed; teardown code is unchanged.
  Isolated RootScope_SurvivesSceneLoad rerun: 1/1 pass with a clean console.

## MK-03 — Tree path screen (2026-10-31)

- [ ] **Editor:** DT-01 test start at stage **12+** (stage 12 for the first check) to reach the path. Open the tree: Ranger's Quick Draw, Volley and Fleet Foot share the bottom row, with rank pips; above are three path cards, two Coming soon. Preview Marksman, then Choose; cards disappear, Steady Aim is learnable at Lv 11, Eagle Eye stays locked until Lv 14. Learn repeatedly: the next rank's text and pips advance, spending one point; MAX disables Learn. Check cyan edges on Volley/actives, the points pill, pause and Close.
- [ ] **Editor:** Soulvail/Capture Look at **411 x 914** and **360 x 780**, before and after choosing Marksman: no clipped icons, labels or detail text; no overlap. Final art and device legibility remain owner checks.

## MK-04 — Skill arc (2026-10-31)

- [ ] **[Editor]** Start stage 12 with Marksman: roll sits in from the corner, Volley is above it, passives below/right; untaken Eagle Eye shows a lock and Lv 14. The path actives are authored in later tasks.
- [ ] **[Editor]** Charge Volley with ordinary arrows. Hold its circle at least 0.45 s: A toggles once; releasing does not cast. Tap while ready, including with A on: Volley fires; an uncharged tap does nothing. Check pips, ready cyan border and passive locks.
- [ ] **[device]** Check thumb reach, circle separation and left-stick coexistence. A hold gives one small buzz with haptics enabled and none when disabled. If using the serialized left-handed mirror, verify roll, arc and passives mirror together.
