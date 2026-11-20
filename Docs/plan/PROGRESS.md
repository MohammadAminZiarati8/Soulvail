# Soulvail — Progress

[OWED](OWED.md).

_2026-11-20._

## State

| | |
|---|---|
| **Milestone** | M0–M6 tagged. |
| **Last merged** | **SS-17e** — Larger Hierophant and aimed Sun Seals. |
| **Next task** | Awaiting owner's go for Sunscar's next task. |
| **Verified** | EditMode 7113/7205, 0 failed, 1 inconclusive, 91 skipped. Format clean. |

## What works

Jungle clear unlocks Sunscar. DT-01 starts any place.
Ranger, Oathbound, Beastmaster. Veilrot/Sanctum/disc off.
Cyan portals. Jungle: Warden/Croaker, Mother 10/Warlord 15/Thornheart 20.
Scripted 1–29. Boss adds die with boss, 0 XP.
Sunscar: 24 regular stages, 2–5 waves, 5–35 enemies. Tune XP/rewards for L30 after 24.
Thornheart: spokes/pods/slam/heart ×2. Mother: shields/lobs/spray/pod.
Stalker 22+ lurks. Thornweaver 24+: 10 m, ≤2 walls past you; die with it.
Gravemaw 25: burrow past cover/rise open; ring, bite/throw, tremor/rocks. 50%: 2 rings/crack Frogs. 986 HP/4 pillars.
Apex 30: eye/feint, streak, +15% window, 1580 HP, 4 stones, panthers 66–33%.
Stones stop dash/rake. Hunt: +30% speed, 3 lanes, 2 pounces.
Warlord: guard, sweep/charge, stone daze, adds, shatter/combo in P2.
Shaman 17–19: guard ≤12 m/hold 11/flee <4. Heal 10%/haste 25% in 8 m. XP 32/priority 8.
+3% HP (heals)/+2% dmg per level. MAX 30/stage 25. Banked points, paused free tree/pips/Learn. Marksman 20 ranks. Death: Shards.
Save v8. Clash UI/LocKeys. Six-circle mirrored arc: tap Auto fire/hold toggle.
Icons, pooled CC, immune bosses. Marksman trim/Quiver/lit arrows/Volley/roll/flinch/silent resume.
Killshot: 3 m/4x arrow; pierces guards, not phases.
Wind-up blocks hits/debuffs.
Cobra fan. Callers: 2 s channel. Vultures circle/dive/land exposed 2 s.
Hero Poison: violet drop/stacks/timer/quiet HP ticks; Space/death clears.
Duneclaw 5: 1000 HP, DT-01 L8 81.60 s. 3 scorplings 66–33%, portal 6. Slabs pin 3 s.
Duneclaw turns 360°/s. Tells stay locked.
Shardback 10: 1025 HP, DT-01 L16 92.37 s. 5.5 m burst, smooth turns, 3 pillars, 2 beetles 66–33%, portal 11.
Hierophant 15: 1050 HP, 2.2×, 4 m sweep. Aimed seals reach the wall, pair below 33%. DT-01 143.17 s, Quick Draw 135.30 s.
Skirmisher runs/fires: Ricochet 11/Second Wind 14 (roll guard/pips)/Whirlwind 17/Storm Run 20.

Beastmaster: axes/1.2 s, stationary, cover blocks; Swift Hands.
Pets hunt mark/idle slot; Auto Pounce/Quick Swap. Packleader: Hounds/Howl 11, Boar/Pelts 14, Hawk 17, Frenzy 20.
Frenzy: +30% bite/speed/dmg 8 s; 10 s, +40%/24 s.
Boar holds 5 m. Globs stop on cover, +30% dmg, 25% slow.
Bond Barrier: swap shield 15% HP/landing+3 s. Quick Swap: 25%/2 m shove/0 dmg swap.
Pet guard hits: back/side; hawk always.

## Missing

1. Gravecaller over, Emberwright under. Warden >120 s near 33; no run >19.
2. Sanctum plays as zero; Pact ~1 in 62.
3. Audio/Elites/feel pass/2nd language missing.

## Known issues

1. ArenaView: zero-spawn warning; TimeManager rewrites.
2. ContactDamage pending; bad tree saves strand Continue.
3. Boundary bolts leave bodies. Pooled Bind/Move snap without autoSyncTransforms.
4. Sanctum 1 frame late; Wight undamageable; Knight Charge root snaps 0.22 m.
5. HUD ~15 draws; no PlayMode Continue test; low disk; bosses unprofiled.
6. Mother's pod Frogs survive phases (JG-12h); unload restores broken cover (MK-02b).
7. RangerSandbox: no IRunSession/EnemyViews.
8. Warlord dissolve shows unpainted atlas.
9. PlayMode unload: cover/VFX/player destroyed, errors.
