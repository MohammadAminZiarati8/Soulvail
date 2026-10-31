# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-10-31, MK-05 built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; `m6` owner to tag. |
| **Last merged** | **MK-05**: core status board (8 max, debuffs first, timers/stacks); Steady Aim stacks and guard show as icons under the HP bar. |
| **Next task** | **MK-06a**: enemies can be stunned, rooted and slowed. |
| **Verified** | EditMode 4 832: 4 831 pass, 0 fail, 1 known animator-clock inconclusive; new 11/11. PlayMode FrameOrder 20/20. Compiler errors 0; new analyzer warnings 0; format clean (11 C# files). |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Mother has facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Stages 1–14 scripted: Frogs, Pitchers, Puffballs, Panthers, Mossguard and shield/spit/pincer exams.
Five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Test start can jump stages.
Run save v5/profile v5; older saves load. Four legacy manual identities expand to six.
Clash UI; LocKey strings. Ranger paths from level 11. Marksman: Steady Aim (5 ranks, Lv 11),
Eagle Eye (3, Lv 14); path icons, preview/choice, ranked pips and next-rank Learn.
Minimal arc: class then chosen-path actives, locks/levels, sweep/seconds, cyan ready border,
auto badge; Volley retains shot pips. Ready taps fire even on Auto; 0.45 s hold toggles with
settings-aware haptics. Shot-charged Volley taps use fresh senses next tick. All auto casts,
including bought casts and Volley, wait 0.5 s after any cast. Untappable corner passives;
six-circle ellipse stays outside the stick at 360 dp, with a mirrored cluster option.
Status row: 24 dp placeholder icons under the HP bar, cyan/violet rims, sweep or stack count;
cleared at stage boundaries and run end.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; only three bosses. Path actives are future tasks.

## Known issues

1. ArenaView warns zero spawn points on reload; harmless. TimeManager reserialises; revert before commits.
2. ContactDamage stat ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Stage-boundary bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum event draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full.
6. Mother pod Frogs can survive phase beats (JG-12h). Run unload can reactivate destroyed cover (MK-02b smoke).
