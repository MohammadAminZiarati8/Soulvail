# Soulvail — Progress

[OWED](OWED.md). History in Git.

_2026-11-13._

## State

| | |
|---|---|
| **Milestone** | M0–M6 tagged. |
| **Last merged** | **SS-11d** — jackal priest data, Sunscar roster from 14. |
| **Next task** | None specced. The jackal priest block is done; owner picks the next Sunscar enemy or boss. |
| **Verified** | EditMode 6686/6778, 0 failed, 1 inconclusive, 91 skipped. C# whitespace clean. |

## What works

Jungle rotates 3 arenas. Sunscar waves open the portal. Scorpions at 1, beetles 2, broods 4, vipers 6, slingers 8, glassbacks 9, antlions 11, priests 14. A pale sac pulses before three scorplings drop. Antlions tunnel, warn with a ring and erupt, then sit exposed for 2.5 s. DT-01 starts any place.
Ranger, Oathbound, Beastmaster. Veilrot/Sanctum/disc off.
Waves → cyan portal, idle hint. Bosses: Warden/Croaker, 10 Mother, 15 Warlord, 20 Thornheart.
Scripted 1–29 except bosses. 11 enemies. Boss adds die with boss, 0 XP.
Thornheart: spokes/pods/slam/heart ×2. Mother: shields/lobs/spray/pod.
Four cousins from 21.
Stalker (22+) lurks. Thornweaver (24+): holds 10 m, ≤ 2 walls past you, die with it.
Gravemaw (25): burrow past cover, rise in open. Ring, bite+throw, tremor/rocks. At 50%, 2 rings and crack Frogs. 986 HP, 4 pillars.
Apex (30): eye tell/feint, streak, +15% window, 1580 HP, 4 stones, panthers 66–33%, win.
Stones stop dash/rake. Hunt: +30% speed, 3 lanes, 2 pounces.
Warlord: guard, sweep/charge, stone daze, adds, shatter/combo in P2.
Shaman (17–19): guard ≤12 m, hold 11 m, flee <4 m. Heal 10%, haste 25% in 8 m. XP 32, priority 8.
+3% HP (heals)/+2% dmg per level. MAX at 30/stage 25. Banked points, paused free-pick tree, pips, Learn. 20 Marksman ranks. Death pays Shards.
Save v8. Clash UI/LocKeys. Mirrored six-circle arc: tap fires on Auto, hold toggles.
Status icons, pooled CC, immune bosses. Marksman trim, Quiver, lit arrows, Volley, roll/flinch, silent resume.
Killshot: 3 m, 4x arrow; pierces guards, not phases.
Wind-up blocks hits/debuffs (Immune).
Skirmisher fires running. Ricochet 11, Second Wind 14 (roll guard, pips), Whirlwind 17, Storm Run 20.

Beastmaster: paired axes/1.2 s, standing, closes, atk speed sets pace. Cover turns axes. Swift Hands.
Pets hunt arena-wide, last mark first, idle in slots. Hawk climbs. Auto Pounce, Quick Swap. Packleader: Hounds/Howl 11, Boar/Pelts 14, Hawk 17, Frenzy 20.
Frenzy: +30% bite/speed/dmg 8 s; 10 s, +40%/24 s.
Boar holds 5 m. Globs stop on cover, +30% dmg, 25% slow.
Bond Barrier: swap shields 15% HP, landing+3 s. Quick Swap 25%, 2 m shove. Swap 0 dmg.
Guards judge pet hits by source: back/side land, hawk always.

## Missing

1. Gravecaller over, Emberwright under. Warden >120 s near 33. No natural run past 19.
2. Sanctum plays as zero; Pact ~1 in 62.
3. Missing audio, Elites, feel pass and 2nd language.

## Known issues

1. ArenaView warns on zero-spawn reload. TimeManager rewrites.
2. ContactDamage pending. Invalid tree saves strand Continue.
3. Non-binding boundary bolts leave bodies. Pooled Bind/Move snap without autoSyncTransforms.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draws. No PlayMode Continue test. Low disk. Bosses unprofiled.
6. Mother pod Frogs survive phases (JG-12h). Unload can restore destroyed cover (MK-02b).
7. RangerSandbox lacks IRunSession and EnemyViews.
8. Warlord death dissolve shows unpainted atlas.
9. PlayMode unload can destroy cover/VFX/player and log errors.
