# Soulvail — Progress

**Where the project stands, today.** Read this first, every session. It is overwritten, never
appended: each line states the present. History is `git log`; a merged task's account is its spec's
*As built* until the spec is deleted, then the commit. **Capped at 10 000 bytes** —
`wc -c Docs/plan/PROGRESS.md` before a handover; over the cap, rewrite the line that grew.

_Updated 2026-10-27, after JG-07c and d._

## Current State

| | |
|---|---|
| **Milestone** | **None planned.** The owner is setting the next direction; the old M7/M8 plan, the RS and UI tracks and every unbuilt spec were removed on 2026-10-22. M0–M5 are tagged `m0`–`m5`; `m6` is the owner's to tag. |
| **Last merged task** | **JG-07c–d**. **c:** `Panther.prefab` (1.52 m long, 0.92 m at the shoulder) prowls, crouches across the 0.9 s wind-up and bounds through the dash; a lane shoots a tongue only if armed with one. **d:** `enemy.panther`, a Lunger, rostered in the Jungle from stage 6; stage 6 is 2 Panthers. |
| **Next task** | **None named.** The owner plays JG-07d's stage 6 first. |
| **Verified** | **JG-07d: full EditMode suite 4 515: 4 512 passed / 2 failed / 1 inconclusive**, the known pair ([Traps §7](../Traps.md#7-tests-nunit-allocation)) and a Husk reserialise row, fixed (114 of 114), in batch mode. |

## What works

A **portrait** top-down run. **Home** with Heroes and Places tabs; places unlock in order, and the
**Jungle** is the first place and ends at stage 30. **Focus build:** Veilrot, the Sanctum and the
Focus disc are switched off (mode flags), and class select lists the **Ranger** alone (BootScope
list); the Oathbound, Gravecaller and Emberwright still exist behind the switch. A stage is an arena
sealed by threat-budgeted, telegraphed waves, then a door; a boss every fifth (the Warden; in the Jungle, Old Croaker — a giant old frog that hop-slams, lashes its tongue down a lane, and stays the same all fight). A mode can
script a stage's enemies by hand (`Scripted Stages` on the mode asset): the Jungle's stage 1 is
3 Frogs (18 XP each, enough for level 2), stage 2 is 2 Pitchers, stage 3 is 2 Pitchers + 3 Frogs,
stage 4 is 2 Puffballs, stage 6 is 2 Panthers. An enemy asset can name its own
shot prefab; the Pitcher lobs red-orange goo (`PitcherSpit`, 7 m/s) that splats on a hit. Jungle
enemies: the Frog (hops; its own `Rig_Frog`), the pitcher plant (`enemy.pitcher`) and the puffball
(`enemy.puffball`, the Bloater's role: swells glowing cyan-white, bursts in spores) and the panther
(`enemy.panther`, a Lunger: warns with a lane, crouches, dashes 12 m down it); Descent keeps the
Husk, Spitter and Bloater capsules. **The Ranger:** its own model on `Rig_Medium`, a 1.33 m arrow per shot with a
short cyan trail, holds fire on the move, Volley (its fan flies a bigger glowing arrow with its own
burst), a real roll on Space. **Levelling:** a kill is XP; a level banks a skill point; the tree
screen pauses and takes a free pick. **Death** pays Shards into the profile. **Saves:** `run.json`
v4, `profile.json` v5, all older versions load; Continue offers the run last written. **UI** in the
Clash-style art: HUD, fixed move stick, Roll and skill buttons, level-up banner, tree,
hero page (baked avatar, stats, every skill rank), Pause, Run end. Every string is a `LocKey`.

## Owed to the owner (manual checks, in the Editor)

1. **Pause** a Jungle run: stone popup, "Paused" ribbon, green Resume, blue View Tree (point count), grey Quit.
2. **Run end**: die (cracked emblem, "You died", three rows, gold-glowing shards with no box edge,
   green Home); finish the Jungle via the debug jump to 30 ("Jungle cleared!").
3. **Telegraph rings** in a Jungle stage are round discs with an edge.
4. **Player cyan**: reticle, focus glow and swing arc match the health bar.
5. **Heroes tab and hero page**: the Ranger's avatar fills its frame; the page scrolls to Basic
   attack, Special move and three branches of nine ranks; dragging the model turns it, the
   sheet only scrolls.
6. **JG-01**: a new Jungle run spawns exactly 3 Frogs on stage 1, and the third kill raises
   level 2; stage 2 spawns exactly 2 Pitchers; `Jungle.asset` › Scripted Stages edits in the
   Inspector.
7. **JG-02**: Jungle stage 2's Pitchers lob red-orange goo with a short trail, slow enough to step
   out of; a hit splats red-orange; no trail streaks from where the last spit landed.
8. **RS-07a**: a Ranger's arrows are visibly larger and draw a thin cyan streak that dies within a
   body length; no streak runs from where the last arrow landed back to the bow.
9. **RS-07b**: with Volley taken, every Nth shot is a fan of bigger arrows with a glowing cyan head
   and a wider cyan trail; a volley hit plays a bigger white-and-cyan burst; ordinary shots unchanged.
10. **JG-03**: Jungle stage 4 is exactly 2 Puffballs. They waddle in, swell glowing cyan-white, burst in
   cream spores inside the red-orange ring; a hit squashes the cap; one killed at range bursts too.
11. **JG-04a**: open `Art/Enemies/Croaker.fbx` in the Inspector's preview: a huge, darker, mossy frog
   with drooping lids, warts and three wine-capped mushrooms.
12. **JG-04c/d**: Jungle stage 5: Old Croaker hops in and lands on the red-orange circle as the ring goes
   out; one hop per slam, no croak, no Frogs; one band on the boss bar; dies onto its back. At 411 × 914
   circle, ring and frog fit on screen. Descent's stage 5 is still the Warden.
13. **JG-04e**: on stage 5, 3–9 m out after a slam: a red-orange rectangle runs toward you and brightens,
   the tongue lashes as it flashes, one sidestep avoids it. Old Croaker dies in about a minute.
14. **JG-05**: a Pitcher backed into a wall takes every arrow; Frogs hop, sit, hop; Old Croaker never
   walks or lashes mid-air; two steps back clear a Puffball's burst.
15. **JG-06a**: on stage 5, 6–9 m out after a slam: as the strip flashes, a pink tongue shoots from the
   frog's mouth to its end and snaps back.
16. **JG-06b**: every Old Croaker landing thuds: the view jolts and settles in under half a second, earth
   and dust burst from its feet. The Warden's slam too. Pausing mid-shake holds it.
17. **JG-06c**: after a landing a metre-wide red band runs out with an earth ridge on its front, the floor
   clear inside; it sinks at its reach. The Warden's slam too.
18. **Open Jungle arenas**: every Jungle stage, both rooms, has an open floor — no trunks, pillar or
   broken wall inside — and no green flakes falling; enemies still path to you round the edges.
19. **JG-07b**: preview `Art/Enemies/Panther.fbx`: a smooth dark cat, tail straight back. **Rule whether the sculpt stays.**
20. **JG-07d**: jump to Jungle stage 6. Two panthers prowl in; one stops, a red-orange strip
   runs toward you and it crouches, tail lashing; as the strip flashes it bounds down it, with no tongue.
   One sidestep avoids it, standing in it costs 14; it stands about a second, then prowls again.
21. **JG-07d**: from the game camera it reads as a long, low cat, distinct from the Frog, Pitcher and
   Puffball, and not lost on the dark floor.
22. **JG-07d**: stage 7 and on, panthers turn up among the others.
23. `Soulvail/Capture Look` at **411 × 914** and **360 × 780** of: every portrait screen (HUD, menu,
   home, tree, Pause, Run end, hero page).

## What does not work yet

1. **Balance**: hits-to-kill leave GD §12.4's band for the Gravecaller (over, from stage 9) and the
   Emberwright (under, to 9); the Warden's fight passes GD §9.1's 120 s near stage 33. No natural run
   has passed stage 19.
2. **The Sanctum plays as zero** — nothing bought in two natural runs (switched off now).
3. **A Pact is about one offer in 62.**
4. **Past stage ~26 the late waves are all Bloaters** (Puffballs in the Jungle): `WaveComposer.Upgrade` swaps a capped wave's
   bodies for the dearest archetype.
5. **No audio, no Elites, two bosses.** The feel pass and a second language have not happened.

## Known issues and loose ends

1. `ArenaView.OnValidate` warns "0 spawn point(s)" on every domain reload — reload-time references
   are fake-null; harmless.
2. `PlayerStat` carries `ContactDamage`, which `PlayerStats.Resolve` refuses — awaiting a ruling.
3. `ProjectSettings/TimeManager.asset` re-serialises whenever the Editor opens; revert it before
   every commit ([Traps §5](../Traps.md#5-unity-editor-and-the-asset-pipeline)).
4. **A saved run the tree refuses strands the player**: `SkillTree.Restore` throws through
   `RunTicker.Start`, the Run scene sits idle, and Continue offers the dead run until Descend.
5. A hand-edited save with huge XP (`1e38`) hangs `LevelTracker`'s level loop.
6. A bolt in the air at a stage boundary leaves its body standing (`ProjectileViews` has no
   `StageArrived` teardown).
7. A pooled body teleported by `Bind` can be snapped back by its first `Move`
   (`autoSyncTransforms` is 0).
8. `SanctumOpened` publishes before `IsSanctumOpen` is true; `SanctumPresenter` draws a frame later.
9. Nothing ever damages a Wight; its `MaxHp` is unread.
10. The Knight's Charge plays `Dodge_Forward`, whose root curve snaps the mesh 0.22 m.
11. The HUD is ~15 draw calls (no sprite atlas; the packer is a `ProjectSettings/` change).
12. No PlayMode row taps Continue end to end.
13. The disk sits near full; an IL2CPP build needs several GB.

## Device

**No reference device; nothing has run outside the Editor.** BlueStacks installs the APK and
crashes in its Vulkan driver. `Screen.dpi` reads 120 in the Editor against a phone's ~400, so the
Editor can't judge legibility ([Traps §9](../Traps.md#9-rendering-canvas-and-tmp)). Deferred to the
first phone: multi-touch, kill-from-recents, haptics, latency, frame time at depth, thermal, the
~50 ms frame a screen costs to open, and scroll feel on the hero sheet.

## Placeholders that become permanent at store upload

Application identifier `com.soulvail.dev` and company name `Soulvail`. The business model
(GD §21.1) is unruled.
