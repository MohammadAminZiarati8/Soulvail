# Soulvail — Progress

History: `git log`. [OWED](OWED.md).

_2026-11-09._

## State

| | |
|---|---|
| **Milestone** | M0–M6 tagged. |
| **Last merged** | **LS-03** — all shipped arenas wide, west to east. |
| **Next task** | Awaiting owner direction. |
| **Verified** | EditMode 6251 passed, 0 failed, 1 inconclusive, 91 skipped. Format clean. |

## What works

Heroes/Places; Jungle 30 stages. Ranger, Oathbound, Beastmaster; Veilrot/Sanctum/disc off.
Waves → door; bosses: Warden/Croaker, 10 Mother, 15 Warlord, 20 Thornheart.
Scripted 1–29 except bosses; 30 Apex; 11 bodied enemies. Boss adds: no XP, die with it.
Thornheart: spokes, pods, slam, heart ×2. Mother: shields, lobs, spray, pod.
From 21 four cousins (Bullfrog, pitcher, bomber, panther).
Stalker (22+) lurks. Thornweaver (24+): holds 10 m, ≤ 2 walls past you, die with it.
Gravemaw (25): burrow; ring, bite+throw; tremor|rocks; 50 %: 2 rings, crack Frogs;
past cover, up in the open; trail, rise/dive, lit maw; 986 HP, 4 pillars.
Apex (30): eye tell/feint, streak, +15% window; 1580 HP. Four stones; 2 panthers at 66%, cleared at 33%; win.
Stone-stops dash/rake. Hunt: +30% speed, 3 lanes, double pounce.
Warlord: guard, sweep/charge, stone daze, adds, phase-2 shatter/combo; glaive, 5 stones.
Shaman: guards ≤12 m, holds 11 m; heals 10%, hastes 25% in 8 m; flees <4 m; lime staff.
17–19: healer/guards, wall, gate; XP 32, priority 8.
Per level +3 % HP (heals)/+2 % dmg; cap 30 at stage 25, MAX strip. Banked points; paused free-pick tree, rank pips, Learn; 20 Marksman ranks (Steady Aim 4, Binding Shot 3). Death pays Shards.
Save v8; Clash UI/LocKeys. Six-circle arc: tap fires on Auto,
hold toggles; mirrored.
Status icons; pooled CC, bosses immune. Marksman trim,
Quiver, lit arrows, Volley, roll/flinch; silent resume.
Killshot: 3 m, 4x arrow; pierces guards, not phases.
Wind-up blocks hits/debuffs (Immune).
Skirmisher fires running. Ricochet 11, Second Wind 14 (roll guard, pips), Whirlwind 17, Storm Run 20.

Beastmaster: axe pair per 1.2 s, standing, closes on target, pace = atk speed; cover turns axes; Swift Hands;
Pets hunt arena-wide, last mark first; idle in slots; hawk climbs; Auto Pounce, exact Quick Swap. Packleader: Hounds/Howl 11, Boar/Pelts 14, Hawk 17, Frenzy 20.
Frenzy: +30% bite/speed/dmg 8 s; 10 s, +40%/24 s.
Boar: holds 5 m, green globs (cyan trail, splat), cover stops; +30% dmg, 25% slow.
Bond Barrier: swap shields 15% HP, landing+3 s, pale bubble, pops; Quick Swap 25%, 2 m shove. Swap: 0 dmg.
Guards judge pet hits by source: back/side land, hawk always.

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
