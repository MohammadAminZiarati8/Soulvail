# Soulvail — Progress

History: `git log`. Checks: [OWED](OWED.md).

_2026-11-07, Hound in the run._

## State

| | |
|---|---|
| **Milestone** | None planned. M0–M6 tagged. |
| **Last merged** | **BM-03c** — The Hound moves, bites and carries a cyan health bar. |
| **Next task** | BM-04 on a go; playtests owed. |
| **Verified** | EditMode 6054 passed, 0 failed, 1 inconclusive. Format clean. |

## What works

Portrait (landscape owed); Heroes/Places; Jungle 30 stages. Ranger, Oathbound, Beastmaster; Veilrot/Sanctum/disc off.
Waves → door; bosses: Warden/Croaker, 10 Mother, 15 Warlord, 20 Thornheart.
Scripted 1–29 except bosses; 30 Apex; 11 bodied enemies. Boss adds: no XP, die with it.
Thornheart (20): spokes, pods, slam, heart ×2; warned vines.
Mother: shields, lobs, spray, pod. Leap: crouch, ring, 1.2 m arc, bite. Chain: lit bombers, red link, 8 Hz fuse.
From 21 four cousins (Bullfrog, Thorn pitcher, Chain bomber, panther) replace.
Stalker (22+): lurks, hides, hit reveals, strikes; shimmer, flash.
Thornweaver (24+): holds 10 m, ≤ 2 walls past you, die with it; crone, lime crown.
Gravemaw (25): burrow; ring, bite+throw; window tremor|rocks; 50 %: 2 rings, crack Frogs;
past cover, up in the open; trail, rise/dive, lit maw; 986 HP, 2.8 m/s, 4 pillars.
Apex (30): eye tell/feint, streak, +15% window; 1580 HP. Four stones; 2 panthers at 66%, cleared at 33%; win.
Stone-stops dash/rake. Pack: 3 s cd. Hunt: +30% speed, 3 lanes, double pounce; 0.9 s window/0.6 s gap.
Warlord: frontal guard, slow turn, warned sweep/charge, stone daze, adds,
ringed spears, phase-2 shatter/speed/combo; painted body, glaive, 5 stones.
Shaman: guards ≤12 m, holds 11 m; heals 10%, hastes 25% in 8 m;
flees <4 m. Strongest haste refreshes, stacks with slow. Lime staff/glow/ring.
17–19: healer/guards, wall, gate; XP 32, priority 8.
Per level +3 % HP (heals)/+2 % dmg; cap 30 at stage 25, MAX strip. Banked points; paused free-pick tree, rank pips, Learn; 20 Marksman ranks (Steady Aim 4, Binding Shot 3). Death pays Shards.
Save v5; Clash UI/LocKeys. Six-circle arc: tap fire on Auto,
0.45 s hold toggles; 0.5 s gap; mirrored. Legacy saves expand to six.
Status/poison icons; pooled CC, bosses immune. Marksman/cyan trim,
Quiver, lit arrows, Volley, roll/flinch; silent resume, plain menu.
Killshot: 3 m, 4x arrow, wind/trail; pierces guards, not phases.
Wind-up blocks hits and debuffs, with an Immune pop on blocked hits.
Skirmisher shoots on the run. Ricochet 11, Second Wind 14 (roll guard and pips), Whirlwind 17, Storm Run 20. Silver-cyan look and hip quiver.

## Missing

1. Balance: Gravecaller over, Emberwright under; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. No audio, Elites, feel pass or second language.

## Known issues

1. ArenaView zero-spawn reload warning; TimeManager reserialises.
2. ContactDamage ruling pending. Invalid tree saves strand Continue.
3. Boundary non-binding bolts leave bodies; pooled Bind/Move snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; bosses unprofiled.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
8. Warlord's death dissolve shows the unpainted atlas.
9. PlayMode unload: destroyed cover/VFX/player; teardown errors.
