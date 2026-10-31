# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, MK-07b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-07b**: Rain authored in four ranks; bow-up release, cyan rim, pale-blue slow fill, pooled falling arrows/dust, boundary cleanup. |
| **Next task** | **MK-08a**: Binding Shot in core, with Unity's anchor probe as a port. |
| **Verified** | EditMode 4 890: 4 889 pass, 0 fail, 1 known animator-clock inconclusive; 10 new Rain/path rows pass. Compiler errors 0; new analyzer warnings 0; format clean (10 C# files). Render preview checked. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Stages 1–14 scripted: Frogs, Pitchers, Puffballs, Panthers, Mossguard and shield/spit/pincer exams.
Five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Test start can jump stages.
Run save v5/profile v5; older saves load. Four legacy manual identities expand to six.
Clash UI; LocKey strings. Ranger paths from level 11. Marksman: Steady Aim (5 ranks, Lv 11),
Rain of Arrows (4, Lv 11), Eagle Eye (3, Lv 14); preview/choice, pips and next-rank Learn.
Arc: class then path actives, locks/levels, cooldown sweep, ready border, auto badge; Volley pips.
Ready taps fire even on Auto; 0.45 s hold toggles with settings-aware haptics.
Auto waits 0.5 s after any cast. Six-circle ellipse outside stick at 360 dp; mirrored option.
Player status row under HP clears at boundaries/end. Enemy CC: stun freezes behaviour,
root zeroes moves, strongest slow wins; bosses half duration. Pooled CC visuals and timed icons.
Rain picks densest living XZ cluster; fixed 2 s zone, eight weapon-scaled pulses, 0.35 s slow
refresh. Four ranks: base 1.5 m/10 s, +25% damage, +0.5 m/30% slow, −2 s cooldown.
Refused casts keep cooldown/resources/auto gap. Upper-body upward release and sky streak;
cyan rim, faint fill (pale blue from rank 3), ~16 arrows/s at 0.8 ordinary arrow scale and dust.
Prewarmed view uses core time; clears on expiry, stage transition/arrival and run end.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; three bosses. Binding Shot is next.

## Known issues

1. ArenaView warns zero spawn points on reload; harmless. TimeManager reserialises; revert before commits.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Stage-boundary bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full. No reference device.
6. Mother pod Frogs can survive phase beats (JG-12h). Run unload can reactivate destroyed cover (MK-02b smoke).
