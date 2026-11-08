# Soulvail — Progress

History: `git log`. [OWED](OWED.md).

_2026-11-09._

## State

| | |
|---|---|
| **Milestone** | M0–M6 tagged. |
| **Last merged** | **BT-06b**. |
| **Next task** | **BT-06c** (→ BT-07b specced). |
| **Verified** | EditMode 6188 passed, 0 failed, 1 inconclusive, 91 skipped. Format clean. |

## What works

Heroes/Places; Jungle 30 stages. Ranger, Oathbound, Beastmaster; Veilrot/Sanctum/disc off.
Waves → door; bosses: Warden/Croaker, 10 Mother, 15 Warlord, 20 Thornheart.
Scripted 1–29 except bosses; 30 Apex; 11 bodied enemies. Boss adds: no XP, die with it.
Thornheart (20): spokes, pods, slam, heart ×2, vines.
Mother: shields, lobs, spray, pod. Leap: crouch, ring, arc, bite. Chain: lit bombers, link, fuse.
From 21 four cousins (Bullfrog, pitcher, bomber, panther).
Stalker (22+) lurks. Thornweaver (24+): holds 10 m, ≤ 2 walls past you, die with it; crone, lime crown.
Gravemaw (25): burrow; ring, bite+throw; tremor|rocks; 50 %: 2 rings, crack Frogs;
past cover, up in the open; trail, rise/dive, lit maw; 986 HP, 2.8 m/s, 4 pillars.
Apex (30): eye tell/feint, streak, +15% window; 1580 HP. Four stones; 2 panthers at 66%, cleared at 33%; win.
Stone-stops dash/rake. Pack: 3 s cd. Hunt: +30% speed, 3 lanes, double pounce; 0.9 s window/0.6 s gap.
Warlord: frontal guard, slow turn, warned sweep/charge, stone daze, adds,
ringed spears, phase-2 shatter/speed/combo; painted body, glaive, 5 stones.
Shaman: guards ≤12 m, holds 11 m; heals 10%, hastes 25% in 8 m; flees <4 m; lime staff/glow.
17–19: healer/guards, wall, gate; XP 32, priority 8.
Per level +3 % HP (heals)/+2 % dmg; cap 30 at stage 25, MAX strip. Banked points; paused free-pick tree, rank pips, Learn; 20 Marksman ranks (Steady Aim 4, Binding Shot 3). Death pays Shards.
Save v7; Clash UI/LocKeys. Six-circle arc: tap fires on Auto,
0.45 s hold toggles; mirrored; legacy saves expand.
Status icons; pooled CC, bosses immune. Marksman trim,
Quiver, lit arrows, Volley, roll/flinch; silent resume, plain menu.
Killshot: 3 m, 4x arrow; pierces guards, not phases.
Wind-up blocks hits/debuffs (Immune).
Skirmisher shoots on the run. Ricochet 11, Second Wind 14 (roll guard and pips), Whirlwind 17, Storm Run 20. Silver-cyan, hip quiver.

Beastmaster: axe pair per 1.2 s, standing, pace = attack speed; cover turns axes; Swift Hands;
Pets hunt arena-wide, last mark first; idle in slots; hawk climbs; Auto Pounce, Quick Swap; exact swap. Packleader: Hounds/Howl (heal) 11, Tactics/Pelts 14, Hawk 17, Frenzy 20; war-paint.
Frenzy: +30% bite/speed/dmg 8 s; 10 s, +40%/24 s; glows.

## Missing

1. Balance: Gravecaller over, Emberwright under; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; Pact ~1 in 62.
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
