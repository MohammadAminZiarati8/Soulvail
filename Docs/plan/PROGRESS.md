# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-10-31, MK-07a built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-07a**: core Rain; densest living cluster, fixed eight-pulse zone, ranked damage/radius/slow, cast event; refused casts keep cooldown. |
| **Next task** | **MK-07b**: Rain of Arrows drawn and authored. |
| **Verified** | EditMode 4 880: 4 879 pass, 0 fail, 1 known animator-clock inconclusive; Rain 17/17 including AllocationAssert.None. Compiler errors 0; new analyzer warnings 0; format clean (13 C# files). |

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
Eagle Eye (3, Lv 14); preview/choice, ranked pips and next-rank Learn.
Arc: class then path actives, locks/levels, cooldown sweep, ready border, auto badge; Volley pips.
Ready taps fire even on Auto; 0.45 s hold toggles with settings-aware haptics.
Auto waits 0.5 s after any cast. Corner passives; six-circle ellipse outside the stick at 360 dp,
mirrored cluster option. Player status row under HP clears at boundaries/end.
Enemy CC: stun freezes behaviour, root zeroes moves, strongest slow wins; bosses half duration.
Pooled sparks/coils/frost, animation response, timed bar icons; dev nearest-enemy CC buttons.
Rain core: XZ cluster ties by distance then id; weapon-scaled pulses every 0.25 s for 2 s.
RainDamage/Radius/Slow stats feed casts; 0.35 s slow refresh per pulse. Fixed centre, one event.
Runtime cast guards preserve cooldown, auto gap and bought-cast resources without a target.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; three bosses. Rain needs MK-07b assets/views.

## Known issues

1. ArenaView warns zero spawn points on reload; harmless. TimeManager reserialises; revert before commits.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Stage-boundary bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full.
6. Mother pod Frogs can survive phase beats (JG-12h). Run unload can reactivate destroyed cover (MK-02b smoke).
