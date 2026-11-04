# Soulvail — Progress

Overwritten, never appended; history: `git log`. **Cap 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-04, JG-25f._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **JG-25f**: Gravemaw drawn. |
| **Next task** | **JG-25g**; revise spec first. |
| **Verified** | EditMode 5 509: 5 508 pass, 1 inconclusive. No PlayMode. 0 errors/new warnings; formatted. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother, 15 Warlord, 20 Thornheart.
Stages 1–14, 16–19, 21–24 scripted; 11 bodied enemies; ringed lobs. Boss adds: no XP.
Thornheart (20): spokes, pods, slam, heart ×2; pods die to 3 arrows; 66 % ring bites outside, 4 spokes; 33 % faster, 3 pods.
Drawn: clips, amber heart, filling pod rings, band 14→11 m; walls warn, rise as vines.
Mother: shields, lobs, spray, pods.
Leap: crouch, ring, 1.2 m arc, bite. Chain: lit bombers, red link, 8 Hz fuse.
From 21 four cousins (Bullfrog, Thorn pitcher, Chain bomber, panther) replace.
Stalker (22+): lurks behind, hides still, hit reveals, strikes from behind; shimmer, warn flash.
Thornweaver (24+): holds 10 m, ≤ 2 walls past the player, die with it; crone, lime crown.
Gravemaw (core): burrow, ring, bite+throw; window: tremor|rocks; 50 %: 2 rings, crack Frogs.
Gravemaw drawn: trail, filling ring, rise/dive, lit maw, cracks; unspawned.
Warlord: frontal guard, slow turn, warned sweep/charge, stone daze, adds,
ringed spears, phase-2 shatter/speed/combo; painted body, glaive, 5 stones.
Moss Shaman: never attacks; anchors on a Mossguard ≤ 12 m, holds 11 m,
heals 10%, hastes 25% in 8 m; flees < 4 m. Strongest haste refreshes,
expires and stacks with slow. Fitted body/staff, glow, lime ring.
17–19: healer/guards, healer's wall, gate. XP 32, priority 8.
Ranger: pale arrows, cyan Volley, roll, flinch; 1st arrow ~¼ s on stop.
Levels bank points; paused tree picks. Death pays Shards. Save v5; older saves load.
Clash UI; LocKeys. 22 Marksman ranks.
Tree preview/choice, rank pips, Learn. Six-circle arc: tap fire on Auto,
0.45 s hold toggles; 0.5 s auto gap; mirrored. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half; poison bubbles, drop icon. Marksman look, cyan trim,
KayKit quiver, glowing string, bright arrows; silent resume, plain menu.

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
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; bosses unprofiled.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
8. Warlord's death dissolve shows the unpainted atlas.
9. PlayMode unload hits destroyed cover/VFX/player; teardown errors.
