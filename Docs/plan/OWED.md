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

## MK-05 — Status row (2026-10-31)

- [ ] **[Editor]** Start stage 12 as Marksman with Steady Aim: stand still — a cyan-rimmed placeholder icon appears under the HP bar, left-aligned with it, counting 2…5 bottom-right (1 shows no number); move — it vanishes. With rank 5 (Guard), a second icon joins at full stacks and leaves on moving. Clear a stage: the row empties.
- [ ] **[Editor]** Device Simulator at 360 x 780: the 24 dp row clears the stage chip and the threat arrows. Placeholder art; the HUD is to be redone.

## MK-06a — Stun, root and slow (2026-10-31)

- [ ] **[Editor]** Regression only — nothing applies these yet (MK-07a+) and nothing draws them (MK-06b). Play stages 1–14 and a boss: Frogs, Pitchers, Panthers, Mossguard and Warden/Croaker/Mother move, wind up and strike exactly as before.

## MK-06b — Enemy crowd-control visuals (2026-10-31)

- [ ] **[Editor]** Start a Jungle run. Use the dev DebugOverlay's **Stun 3s**, **Root 3s** and **Slow 3s** buttons on the nearest living enemy. Confirm three pale sparks circle above a stunned head, cyan-lit coils fit a rooted body's feet while its legs keep moving, and faint pale-blue frost reads on the dark floor with slower animation. A stunned enemy still flinches on hits. Full-health basic bars appear during a status; 14 dp icons stay ordered stun → root → slow and their circular sweeps count down. Effects/icons clear at the end event, death and pooled reuse. Boss durations are 1.5 s and their ordinary world bars stay suppressed. Skills that apply these remain future work.

## MK-07b — Rain of Arrows drawn and authored (2026-11-01)

- [ ] **[Editor]** Stage 12, Ranger / Marksman, Rain learned: with ≥ 3 enemies in acquire range, check the upward bow release and cyan sky streak while moving; a fixed cyan-rimmed circle appears on the largest group, ordinary arrows fall inside it for 2 s with small landing dust, and enemies inside lose health. At rank 3 the circle widens, the faint fill turns pale blue and enemies visibly slow. Pause/resume and cross a stage boundary: no lingering arrows, dust or ring.

## MK-08b — Binding Shot in Unity

- [ ] **[Editor]** Stage 12 at level 14: shoot an enemy standing in front of a stone — chain to the stone, long stun; two enemies in a line — both chained; open ground — short stun. Check the quick draw/release, cyan rope tail and simple readable chain/clasps; chains should follow bodies and disappear with the stun or stage change.

## MK-09b — Hunter's Snare drawn and authored (2026-11-01)

- [ ] **[Editor]** Ranger / Marksman at level 17 with Hunter's Snare learned: let a Frog hop within 3 m. The Ranger stoops briefly (legs keep running); a small rope-and-stakes snare drops at their feet with a dust puff and, after ~0.3 s, a faint cyan ring pulses slowly on it. When the Frog steps on it the loop jerks up and cinches round its feet, the prop vanishes, root coils show and the Frog loses health. An untouched snare sinks and fades after 10 s; a third cast fades the oldest. Crossing a stage clears every snare.

## MK-10a — deferred until MK-10b supplies the assets (2026-11-01)

- At level 20 with Killshot learned, playcheck the kneel/held movement and roll, no basic arrows during the draw, locked arena-crossing shot through cover, falloff, and normal XP/drop feedback for non-boss executes. Check damage does not cancel the draw, while player death and stage arrival do. Core behaviour and allocation checks are automated and green; this is the integrated presentation check after MK-10b.

## MK-10b — Killshot drawn and authored (2026-11-01)

- [ ] **[Editor]** Level 20 on a crowded late stage: learn Killshot as the sixth Marksman skill. Check the locked facing, crouched bow draw, growing cyan-white tip and faint aim line for 0.7 s; the full-arena streak, brief stronger camera jolt, sparks on each hit and bright bursts on executed enemies. The Ranger should stand within 0.25 s and move as soon as core releases the hold. Confirm it reads as the strongest skill in the kit, without a polish pass.
- [ ] **[Editor]** Die or cross a stage boundary during the draw: no stuck crouch, glow, aim line or delayed streak. Check the three ranks at 15/20/25% execution thresholds, rank-two damage and rank-three cooldown, plus the existing MK-10a level-20 playcheck.

## MK-11 — The Marksman's look (2026-11-01)

- [ ] **[Editor]** Choose Marksman at level 11: notice the 0.4 s cyan shimmer, then compare against the plain Ranger at the game camera height. Hood/cloak should be deep forest green with cyan trim, the KayKit quiver visible on the back, the bowstring faintly glowing cyan and ordinary arrow trails a little brighter. Judge readability on the dark floor; no polish pass is included.
- [ ] **[Editor]** Continue a saved Marksman with a purchased path rank: the look should already be applied without a shimmer. Return to Heroes/class select and start a fresh run: plain Ranger, no path prop or glow.

## JG-15e — The Warlord's model (2026-11-01)

- [ ] **[Editor]** Scene view: `Mossguard_Warlord.prefab` at scale 2.03 beside `Mossguard.prefab` at 1.27, then at the game camera height. He should read as the same kind, clearly bigger and war-painted: darker olive skin, umber cloth, bone-white stripes on skirt, tail and shield. Glaive (axe head on the spear haft) and the 1.35× shield should be readable at phone zoom. Judge the KayKit axe's slate-blue neck wrap (hue 203°, 3° outside the cyan band): if it reads as player cyan, say so and 15f/15g can swap the head's material.

## JG-15f — The Warlord's body (2026-11-01)

- [ ] **[Editor]** After JG-15g, DT-01 start at stage 15: each move reads before it lands — two-handed slice (sweep and combo first), chop (combo second), throw (lob), taunt (war cry), shield-braced charge with running legs. The flat grey wedge at his feet shows the guarded front and widens when a guard Mossguard stands within 3.5 m. Judge whether the charge's running legs (Running_A at up to 4×) blur.
- [ ] **[Editor]** Charge him into a stone: Hit_B then a slumped Idle_B, a dust puff, three bone-grey motes circling over the head for 2 s, guard pose dropped. At 33 %: the shield vanishes in a burst of wood/moss shards and dust, and the guard pose never returns.
- [ ] **[device]** All of the above at phone size (GD §9.1 rule 7); the wedge must stay readable but quieter than the danger telegraphs.

## JG-15g — Stage 15 is the Warlord's (2026-11-01)

- [ ] **[Editor]** DT-01 start at stage 15: the Warlord spawns central among five stones (uneven ring ~6.5 m out). Roll behind him and shoot; bait a charge into a stone — 2 s daze; Binding Shot him to a stone; Snare his lane. His spears fly tip first and land on their rings.
- [ ] **[Editor]** Fight lands in 75–120 s with a Marksman build (authored 825 HP aims at ~95 s); war-cry guards vanish at 66 % and 33 %.
- [ ] **[Editor]** Known, not in 15g's table: his death dissolve still shows the unpainted atlas (M_Enemy_Dissolve).

## JG-16a — Stage 16 is a breather (2026-11-01)

- [ ] **[Editor]** DT-01 start at stage 16: 6 Frogs + 2 Pitchers; then 2 Mossguards with 2 Panthers; then 1 Mossguard, 2 Puffballs and a Pitcher; the door after wave 3. Judge it as a breather after the Warlord (121 threat vs stage 14's 128).

- JG-16b (2026-11-01): no standalone visual check; judge the healer's range-edge hiding, pulse and flee with its body in JG-16e.

- JG-16c (2026-11-01): judge the Shaman silhouette and source likeness; repaired front/back/side previews under Temp/ShamanReview and Temp/ShamanStaffReview, six-clip pose sheet at Temp/ShamanReview/poses-staff-upright.png. Agent inspected closure and poses; owner art ruling remains.

- JG-16d (2026-11-01): with the stage-17 encounter, judge lantern glow, pale-lime support ring, healed bars and haste chevrons from the game camera; approach to cancel its cast and see it flee. Automated view/pool checks passed.

- JG-16e (2026-11-02): playtest Jungle stage 17 with DT-01's test start. Judge the Shaman's likeness, range-edge hiding, reticle priority, heal/haste pulse and fleeing. Unity cast preview: Temp/ShamanReview/UnityShaman.png. EditMode 5154/5153/0/1; boot smoke 3/2/1/0, RootScope_SurvivesSceneLoad fails in unrelated scene teardown (Ranger VFX/player; earlier CoverViews). Shaman prewarm warning fixed. Stages 18–19 await JG-16f.

- JG-16g (2026-11-02): full-detail repaired source body and original texture are now in the game prefab. No decimation, atlas repaint or proportion warp; retargeted clips on source-fitted KayKit joints. Staff source correction follows as JG-16h before handover. Full-detail mobile cost unmeasured.

- JG-16h (2026-11-02): repaired full-detail body and staff now used in game, with original UVs/albedo, source-fitted rig and six retargeted clips. Judge appearance/motion at Jungle 17. Actual game-asset Blender review: Temp/ShamanInGame/Shaman_Corrected_Game.blend; Unity preview: Temp/ShamanInGame/CompleteUnity.png. EditMode 5156/5155/0/1; PlayMode run-start 1/1 pass. Full-detail mobile performance unmeasured.

- JG-16h verification clarification: the run-start test reports 1/1 passed, but errors can arrive afterward during scene teardown (CoverViews and Ranger Killshot VFX). The earlier commit body's claim that the post-smoke Console was clean was incorrect; these cleanup errors remain outside the model correction.

- JG-16f (2026-11-02): Jungle stages 18–19 scripted (178, 208 threat). Test start at 18: Shaman behind two Mossguards with Pitchers; the open crowd; a second Shaman wall with Panthers. Test start at 19: the crowd; two Shamans behind three Mossguards; the last mix; the door. Rule whether 16–19 (121 → 144 → 178 → 208) climb fairly into stage 20.

- JG-16i (2026-11-02): Shaman gait. Test start at 17 or 18 and watch the Shaman: legs step with the body (no glide), it turns to face where it walks or flees (no sideways slide or backwards run), turns back to face you when it stops or casts, and the staff stays in hand without whipping. Its Hit, Cast and Death clips still carry the old retarget's 170°+ single-frame flips (not touched here).

- JG-16j (2026-11-02): Shaman Hit, Cast and Death rebuilt (supersedes the "still carry flips" note above). At Jungle 17, hit the Shaman, let it cast near a Mossguard, and kill it: the staff should never jump out of the hand at the start of any of the three, and the cast should raise the seed above the head smoothly.

- ST-01 (2026-11-02): enemy statuses are now ids. In a run, press the debug Stun, Root and Slow buttons on an enemy and let the Shaman haste an ally: sparks, vines, frost and the haste chevron should show and clear as before, and a stunned enemy should free its binding chain.

- ST-02 (2026-11-02): the enemy bar's status row. Stage 17: debug-stun a hasted Mossguard: one stun icon over the bar, and when the stun ends the new lime hasted chevron returns. Debug-slow a Frog: frost on the body and one slow icon draining over 3 s. Check the hasted icon reads at phone size.

- ST-03a (2026-11-02): poison in core. In a run, press the new debug Poison button on an enemy: it should lose health in pulses every half second for 4 s (three stacks of 4 damage a second), show no icon yet (ST-03b), and a low-health enemy should die from it with XP paid. Pressing it again adds stacks up to five.

- JG-20f (2026-11-02): Thornheart's model. Open `Prefabs/Enemies/Thornheart.prefab` and `ThornPod.prefab` in a scene beside a Mossguard and a Pitcher bud: the tree huge with its gold heart readable from the game camera at phone zoom, the pod not mistaken for a bud. Preview the seven `A_Thornheart_*` clips on the Model (Animation window): roots dig, Root_0 slams, the crown opens on Exposed. **On a real phone:** profile stage 19 (two full-detail Shamans) and, once 20h lands, stage 20 (Thornheart ~182k tris plus ~97k-tri pods) with the Unity Profiler over USB.

- ST-03b (2026-11-03): poison drawn. In a run, debug-Poison a Mossguard: green bubbles rise from its middle, the bar shows the green drop with "3" draining over 4 s, and HP drops in steps; at death the bubbles vanish at once. Poison then debug-Stun the same enemy: the stun icon takes the bar while the bubbles keep rising. Check the drop reads at phone size beside the Shaman's lime haste chevron.

- JG-20e (2026-11-03): thorn walls in Unity. Nothing opens a wall until JG-20h, so check then at stage 20: a red strip fills along the floor before each wall, the moss-green bramble (M_SeedPod, lit; was white M_Leaf until 2026-11-03) rises out of it, the Ranger and the roll stop at a standing wall, a Rootling walks straight through, and a wall that rose on top of you lets you walk out before it turns solid. Withered walls sink in ~0.3 s. The bramble is a placeholder moss-green box; rule whether it needs a real mesh.

- JG-20g (2026-11-03): Thornheart's body. Check at stage 20 once JG-20h lands: roots dig on a spoke tell, the slam on its strip, the plant on a pod cast; the heart glows amber only after a slam rises and goes dark when it closes (never cyan); every pod gets a red ring that fills over ~6 s and vanishes when the pod is shot or bursts; from 66 % a red-edged olive band closes from 17 m to 11 m and the bite starts exactly at its red inner edge. Judge whether the slam's blow lands with the damage (clip blows were estimated at 0.4/0.5/0.2 s) and whether the band and pod rings read at phone size.

- JG-20h (2026-11-03): stage 20 is Thornheart's. DT-01 test start at stage 20: the tree stands up alone in the centre, no wave beside it, no stones. Spokes rise where warned and stop you, never a Rootling; stand on a strip and get bitten, roll through one and don't. The slam lights the heart and damage numbers double; an unshot pod grows three Rootlings, a shot one (three arrows) nothing, and the Ranger takes pods before the tree. At 66 % the ring closes and the lane inside it stays clear of spokes; at 33 % it all speeds up. Time the fight: 75–120 s (aimed at ~95 s). The door opens only once the tree and every pod and Rootling are down. Also run JG-20e's and JG-20g's stage-20 checks above.

- JG-20i (2026-11-03): the thorn wall's vine. DT-01 test start at stage 20: each spoke and the slam rise out of their red strip as a gnarled dark green-brown vine with pale thorns, laid in segments along the whole strip (no gap at either end, no stretched thorns on the 14 m slam). Judge at phone size from the game camera whether the vines read against the jungle floor and whether the segment repeat is too obvious. A risen vine stops the Ranger and the roll, a Rootling walks through, and a withered one sinks back into the floor in ~0.3 s. **On a real phone:** with the ring's four spokes and a slam standing (up to ~25 segments, 970 tris each), check the frame time holds.

- JG-20j (2026-11-03): the ring reads. DT-01 test start at stage 20, take Thornheart under 66 %: as the ring closes, everything outside its red edge gets a faint red tint, and each time it bites you (every 0.5 s while you stand out there) the tint flashes brighter and fades in ~0.35 s. Step inside the edge and the bites stop. Judge whether the tint is too strong or too faint on the jungle floor at phone size, and whether it ever hides a red spoke strip or pod ring.

- JG-20j fix (2026-11-03): the ring fits the arena. DT-01 test start at stage 20, take Thornheart under 66 %: the ring's red edge appears on the floor well inside the walls (14 m out, was 17 m on the wall line) and closes to 11 m; the red tint stops at the walls and nothing of the band or tint is drawn behind them, wherever the tree stands. Walk the tree towards a wall: the clipped side follows it. A first-time player should see where to stand at a glance — judge that at phone size.

- JG-20j scale fix (2026-11-03): the ring was drawn 3x too big (the 3x boss body scaled it), so it showed ~33 m while the bite was at 11 m. Retake the JG-20j fix check above: the red edge now sits inside the walls and the HP starts dropping exactly when you step past it, not before; pod rings are pod-sized (~2.4 m across), not ~7 m.

- JG-21e (2026-11-03): the cousins drawn. Nothing spawns a Bullfrog or a Chain bomber until JG-21f, so check then at stage 21 (DT-01). A Bullfrog crouches as a red ring fills where you stood, plays the Frog rig's Leap clip, arcs about 1.2 m up and lands on the ring as it fills; the body's shadow/collider stays on the floor. Judge whether the 1 s Leap clip lines up with the ~1.1 s crouch-and-flight. Shoot one of four bunched Chain bombers: a red-orange line runs from each blast to each bomber it lights, ~0.3 m off the floor, fading in 0.3 s; each lit bomber blinks red-orange fast (8 Hz) until it pops in turn. Judge at phone size whether the 0.15 m lines read against the jungle floor.

- JG-21f (2026-11-03): the cousins in the jungle. DT-01 test start at stage 21: wave 1 is six Bullfrogs (bigger, olive Frogs that crouch and leap onto a filling ring); wave 2 brings three Thorn pitchers (paler, larger Pitchers throwing three ringed lobs in a line) with three Bullfrogs; wave 3 four Chain bombers (pale yellow-green Puffballs; one blast lights its neighbours) with two Bullfrogs; wave 4 two Shadow panthers (near-black Panthers that pair up and lunge from two sides) with a Mossguard and two Thorn pitchers. Each should read at a glance as its parent's harder kin, never as the parent. Rule the four tints and whether 21 (threat 194, below 19's 208) is a fair step after Thornheart. Also run JG-21e's stage-21 check above. Past 21, budgeted stages draw only cousins, Mossguards and Shamans — no plain Frog, Pitcher, Puffball or Panther.

- JG-22b (2026-11-03): the stalker's model. Make the pose sheet: `PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" -b --factory-startup -P Tools/Blender/stalker.py -- --pose-sheet Temp/stalker-sheet.png`. Rows: Idle_B, Sneaking (the prowl), Crouching (the lurk), the punch (the strike), Hit_A, Death_A, front and three-quarter back; the last row is the Mossguard, Stalker and Shaman in black. Check it sneaks, crouches, lunges, flinches and dies without tearing, the curled tail follows the hips, and in black it reads as neither the Mossguard nor the Shaman. The leaf-green skin and dark olive bands are atlas swatches (the Frog's green, the Panther's coat); the scan's orange eyes are now cream with a dark pupil. **Rule whether the sculpt (TRELLIS seed 42) stays.**

- JG-22c (2026-11-03): the stalker's body. Nothing spawns a stalker until JG-22d (no asset yet), so check then at stage 22 (DT-01), in a phone-sized Game view. Stand still and let one sneak behind the Ranger: it crouches and fades in 0.3 s to a near-clear body (10 % alpha) with a pale leaf-green rim that keeps climbing in bands while it holds still; its health bar and status icons vanish with it, and the auto-aim ignores it. Stop beside it: it snaps back to full colour, flashes white for 0.1 s, the red-orange lane arms and it punches down it. Shoot it while hidden: it drops the cloak at once. **Rule whether the shimmer is too easy or too hard to spot** (dials on `M_StalkerCloak`: Rim Alpha Floor 0.35, Body Alpha Floor 0.1, Shimmer Speed 1.5). Also judge whether the punch lands at the end of the lane's fill.

- JG-22d (2026-11-04): the stalker in the jungle. DT-01 test start at stage 22: wave 1 is one Chameleon stalker with six Bullfrogs — the stalker should be the only new thing. Stop to shoot Bullfrogs and it should creep behind you, cloak and punch down a warned lane from behind; keep moving and it should never land a hit from the front. Wave 2 is two Shadow panthers, two Thorn pitchers and four Chain bombers; wave 3 two stalkers, two Mossguards and a Shaman (threat 218, after 21's 194). **Rule whether two stalkers behind a Mossguard wall in wave 3 is fair**, and whether its 0.45 s windup is long enough to dodge — it is under the 0.6 s floor GD §9.1 gives Lungers and Bulwarks. Also run JG-22c's stage-22 check above.

- JG-23 (2026-11-04): stage 23, "the tangle". DT-01 test start at stage 23: wave 1 is four Chain bombers with two Shadow panthers and four Bullfrogs — bombers should chain into the panthers; wave 2 is two Mossguards walling a Shaman with three Thorn pitchers firing over them; wave 3 is two Chameleon stalkers among two Shadow panthers and two Chain bombers. Nothing new should appear. Clear it and the door opens. Then rule whether 23 is a fair step up from 22 (242 threat vs 218).

- JG-24b (2026-11-04): the Thornweaver's model. Seed 42 of your three TRELLIS outputs was picked (clearest crown, most moss; 43 is darker, 44 near-black). Make the pose sheet: `PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" -b --factory-startup -P Tools/Blender/thornweaver.py -- --pose-sheet Temp/thornweaver-sheet.png`. Rows: Idle_B, Walking_B, Ranged_Magic_Spellcasting (the cast), Hit_A, Death_A, front and three-quarter back; the last row is the Mossguard, Shaman, Thornweaver and Stalker in black. Check it walks, casts, flinches and dies without tearing, the pale-green thorn crown rings the head (not the face), and in black it reads as none of the other three. **Rule whether the sculpt stays** (or name another seed).

- JG-24c (2026-11-04): the Thornweaver's body. Nothing spawns a weaver until JG-24d, so check then (DT-01, phone-sized Game view). A weaver about 10 m away raises its arms while its thorn crown glows lime; when the warned strip appears beyond you, a moss-green root crawls from its feet to the strip's middle in about a quarter second and is gone as the wall rises. With two weavers you can tell whose wall is whose. The weaver's body no longer glows red on the cast (the strip is the warning); say if you want that back. Kill one mid-cast: the glow stops, and a root already drawn stays until its wall rises or withers.

- JG-24d (2026-11-04): the Thornweaver in stage 24. DT-01 test start at stage 24, phone-sized Game view. Wave 1: one Thornweaver holds about 10 m off and plants thorn walls just beyond you, cutting off the retreat, while eight Bullfrogs leap in. Wave 2: two Chameleon stalkers, four Chain bombers, two Thorn pitchers, two Bullfrogs (no weaver). Wave 3: two Thornweavers pen you toward two Mossguards with two Shadow panthers and a Shaman; kill a weaver and its walls drop. Its name reads "Thornweaver". Then the JG-24c look checks above (crown, root, whose wall is whose). **Rule whether 21–24 climb fairly into Gravemaw** (194, 218, 242, 269 threat). Its priority is 7, the Thorn pod's: they never meet, so say if you want it lower.

- JG-25b (2026-11-04): the eruption's throw. Nothing spawns Gravemaw until JG-25g, so check then (stage 25, phone-sized Game view): stand in the ring when it bites — you are shoved straight out to about 1 m past its edge in a quarter second, still firing; stand with a wall or stone between you and the edge — you stop at it. Rolling through the bite: no damage, no shove.

- JG-25c (2026-11-04): the tremor and the spit. Nothing spawns Gravemaw until JG-25g, so check then (stage 25, phone-sized Game view). Each window holds one move, alternating: the first, about 0.4 s after the eruption, is a 0.8 s windup then a ring expanding from the eruption spot to 9 m — roll through the band and take nothing, stand in it and take a hit; the next window instead lobs three rocks (at you and 2.5 m either side), each with a landing ring once JG-25f sets it. Note any move that reads late or hard to see.

- JG-25d (2026-11-04): Gravemaw's phase 2. Nothing spawns Gravemaw until JG-25g, so check then (stage 25, phone-sized Game view). Bring it under half health: a 1.5 s pause, then it chases for 1.5 s instead of 2.5. Each cycle now erupts twice — the first bites and shoves you out, it stays under, and 0.6 s later a second ring (0.8 s warning) lands on where you went; only then does the window open with its one move. Each eruption opens two cracks on open ground away from you; a Frog climbs out of each a second later (no XP), at most four at once. The cracks have no look until JG-25f. **Rule whether the 0.8 s second ring is readable after the shove**, and whether four Frogs is too many.

- JG-25e (2026-11-04): Gravemaw's body. Open `Prefabs/Enemies/Gravemaw.prefab` beside a Mossguard in a scene, set Gravemaw's scale to 3 (25g's `BodyScale`), and look from the game camera at phone zoom: clearly huge; the round maw with its amber throat reads at the front; the brown, mossed hide stands off the Jungle floor. Scrub the seven clips in the Animation window (Idle, Rise, Dive, Spit, Tremor, Hit, Death): Rise comes up out of the ground maw first, Dive goes back under. **Rule whether 42's red flecks on the flanks may stay**, and whether the 11 270-triangle boss budget (crowd: 2 800) is acceptable until a phone profile.

- JG-25f (2026-11-04): Gravemaw drawn. Nothing spawns Gravemaw until JG-25g, so check then (stage 25, phone-sized Game view). Underground it is a dirt mound throwing clods that leads the eye to you; the ring lands in Danger at your predicted spot and fills over its one second; it comes up out of the ring (Rise, a dust burst, a camera thud) whether or not it bit; in its window the maw glows amber and the body warms faintly, and only then; it dives when the window shuts. Phase 2's first eruption pops up and straight back down. Cracks show a filling Danger disc with dust for the second before a Frog climbs out. **Rule whether the dirt clods (square, placeholder) and the mound read**, and whether a crack in Danger reads as "something comes out here" rather than "this hurts".
