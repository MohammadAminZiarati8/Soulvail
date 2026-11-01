# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-02, JG-16g body correction built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **JG-16g**: repaired source body/texture in game, source-fitted rig and six clips. |
| **Next task** | **JG-16h**: full-detail source staff, authorised; then 16f awaits owner go. |
| **Verified** | EditMode 5 155: 5 154 pass, 0 fail, 1 known inconclusive. PlayMode boot smoke 3: 2 pass, 1 scene-unload failure (below). Compiler errors 0; no new analyzer warnings; format clean. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother, 15 Warlord.
Stages 1–14 and 16–17 scripted; six enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Warlord: frontal guard and slow turn, warned sweep/charge, stone daze, guard summons,
three ringed spears, phase-2 shatter/speed/combo; painted body, glaive, tells, five stones.
Moss Shaman: never attacks, prefers a Mossguard anchor within 12 m, holds at 11 m,
heals 10% max HP and hastes 25% in 8 m; flees within 4 m. Strongest haste refreshes,
expires and stacks with slow. Repaired source body/texture, fitted KayKit hierarchy,
six clips, seed-only glow, lime pulse ring, healed bars and haste chevrons; pooled cleanly.
Stage 17: Shaman + two guards; Shaman/Frogs/Pitchers; guards/Panthers. XP 32, priority 8.
Ranger: pale arrows, cyan Volley, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run/profile save v5; older saves load.
Clash UI; LocKey strings. All 22 Marksman ranks from level 11, all skills authored/drawn.
Tree preview/choice, rank pips and Learn. Six-circle arc: tap fire on Auto,
0.45 s hold toggles/haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half duration. Marksman forest look, cyan trim,
KayKit quiver, emissive string, brighter arrows; shimmer, silent resume, plain menu/fresh run.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language.

## Known issues

1. ArenaView zero-spawn reload warning; TimeManager reserialises.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Boundary non-binding bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; no reference device.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
8. Warlord's death dissolve shows the unpainted atlas.
9. BootSmoke RootScope_SurvivesSceneLoad fails unloading: destroyed Ranger VFX trails/particles/player; an earlier run hit CoverViews. No Shaman warnings after the prewarm guard.
