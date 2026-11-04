# Soulvail — Progress

Overwritten, never appended; history: `git log`. **Cap 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_2026-11-05, JG-30c._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **JG-30c**: Apex pack and hunt phases in core. |
| **Next task** | JG-30d–f. |
| **Verified** | EditMode 5598: 5597 pass, 0 failed, 1 inconclusive. No PlayMode. 0 compile errors/new warnings; formatted. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum, disc off; Ranger alone.
Budgeted waves → door; 5th-stage bosses: Warden/Croaker, stage 10 Mother, 15 Warlord, 20 Thornheart.
Stages 1–14, 16–19, 21–24, 26–29 scripted; 30 Croaker; 11 bodied enemies; ringed lob. Boss adds: no XP, die with it.
Thornheart (20): spokes, pods, slam, heart ×2; pods die to 3 arrows; 66 % ring bites outside, 4 spokes; 33 % faster, 3 pods.
Art: amber heart, pod rings, 14→11 m band; warned vines.
Mother: shields, lobs, spray, pod. Leap: crouch, ring, 1.2 m arc, bite. Chain: lit bombers, red link, 8 Hz fuse.
From 21 four cousins (Bullfrog, Thorn pitcher, Chain bomber, panther) replace.
Stalker (22+): lurks behind, hides still, hit reveals, strikes; shimmer, warn flash.
Thornweaver (24+): holds 10 m, ≤ 2 walls past you, die with it; crone, lime crown.
Gravemaw (25): burrow; ring, bite+throw; window tremor|rocks; 50 %: 2 rings, crack Frogs;
past cover, up in the open; trail, rise/dive, lit maw; 632 HP, 2.8 m/s, 4 pillars.
Apex (unspawned): stone-stopped dash, rake, pounce/feint; roll blocks bites; air finishes beats.
Pack: 3 s cooldown. Hunt: +30% speed, 3 lanes, double pounce, 0.9 s window/0.6 s gap.
Warlord: frontal guard, slow turn, warned sweep/charge, stone daze, adds,
ringed spears, phase-2 shatter/speed/combo; painted body, glaive, 5 stones.
Shaman: noncombat; Mossguard anchor ≤ 12 m, holds 11 m; heals 10%, hastes 25% in 8 m;
flees < 4 m. Strongest haste refreshes, expires, stacks with slow. Body/staff, lime glow/ring.
17–19: healer/guards, wall, gate. XP 32, priority 8.
Banked points; paused free-pick tree, rank pips, Learn; 22 Marksman ranks. Death pays Shards.
Save v5; older saves load. Clash UI; LocKeys. Six-circle arc: tap fire on Auto,
0.45 s hold toggles; 0.5 s auto gap; mirrored. Legacy saves expand to six.
Status icons; pooled enemy CC, bosses immune; poison bubbles/drop icon. Marksman, cyan trim,
Quiver, lit arrows (~¼ s first), Volley, roll/flinch; silent resume, plain menu.

## What does not work yet

1. Balance: Gravecaller over, Emberwright under; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. No audio, Elites, feel pass or second language.

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
