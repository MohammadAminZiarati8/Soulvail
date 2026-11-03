# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-03, JG-21a._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **JG-21a**: Bullfrog leap in core (no body yet). |
| **Next task** | **JG-21b**; then JG-21c–24d. |
| **Verified** | EditMode 5 312: 5 311 pass, 1 inconclusive. PlayMode not rerun. 0 errors, no new warnings, format clean. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother, 15 Warlord, 20 Thornheart.
Stages 1–14, 16–19 scripted; six enemies have bodies; Pitcher lobs ringed. Boss adds: no XP.
Thornheart (stage 20): spokes, pods, slam, heart ×2; pods die to 3 arrows; 66 % ring bites outside (tint flashes), 4 spokes; 33 % faster, 3 pods.
Drawn: clips per move, amber heart while exposed, pod rings fill, band 14→11 m, clipped to the walls; walls warn, rise as vines, block.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs.
Leap (core): LeapSpec Chasers crouch 0.6 s, fix a ring, fly 0.5 s, bite ×1.5.
Warlord: frontal guard and slow turn, warned sweep/charge, stone daze, guard summons,
three ringed spears, phase-2 shatter/speed/combo; painted body, glaive, tells, 5 stones.
Moss Shaman: never attacks; anchors on a Mossguard ≤ 12 m, holds at 11 m,
heals 10% max HP and hastes 25% in 8 m; flees within 4 m. Strongest haste refreshes,
expires and stacks with slow. Faces its path, the player when still; walks under 1.2 m/s, runs above.
Fitted body/staff, clips, seed glow, lime ring, haste chevrons.
Stage 17: healer/guards; 18: the healer's wall; 19: the gate, two Shamans. XP 32, priority 8.
Ranger: pale arrows, cyan Volley, Space roll, flinch; first arrow ~¼ s on a stop.
Levels bank points; paused tree picks. Death pays Shards. Run/profile save v5; older saves load.
Clash UI; LocKeys. All 22 Marksman ranks from lvl 11, authored/drawn.
Tree preview/choice, rank pips and Learn. Six-circle arc: tap fire on Auto,
0.45 s hold toggles/haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half duration; poison bubbles, drop icon. Marksman forest look, cyan trim,
KayKit quiver, emissive string, brighter arrows; shimmer, silent resume, plain menu.

## What does not work yet

1. Balance: Gravecaller over, Emberwright under; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language.

## Known issues

1. ArenaView zero-spawn reload warning; TimeManager reserialises.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Boundary non-binding bolts leave bodies; pooled Bind/Move snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; Shaman/Thornheart unprofiled.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
8. Warlord's death dissolve shows the unpainted atlas.
9. PlayMode unload hits destroyed cover/VFX/player; teardown errors.
