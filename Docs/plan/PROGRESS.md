# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, MK-09b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-09b**: Hunter's Snare drawn, authored and on the Marksman path. |
| **Next task** | Owner to name. |
| **Verified** | EditMode 4 959: 4 958 pass, 0 fail, 1 known animator-clock inconclusive. All 8 snare rows pass. Compiler errors 0; new analyzer warnings 0; format clean. Level-17 snare playcheck owed. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Stages 1–14 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run save v5/profile v5; older saves load.
Clash UI; LocKey strings. Ranger paths from level 11. Marksman: Steady Aim (5 ranks, Lv 11),
Rain of Arrows (4, Lv 11), Binding Shot (4, Lv 14), Eagle Eye (3, Lv 14), Hunter's Snare (3, Lv 17).
Tree preview/choice, rank pips and Learn. Six-circle skill arc: ready taps fire on Auto,
0.45 s hold toggles with haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six. Player statuses under HP; pooled enemy CC visuals
and timed icons; bosses half duration. Rain: densest living XZ cluster, fixed 2 s zone,
eight weapon-scaled pulses, slow refresh; bow-up release, cyan rim, pooled arrows/dust.
Binding: 2× weapon arrow, rear enemy in 4 m/20° cone or same-frame solid probe.
Next-step facts; bound 2 s (+0.4/+0.6), alone 0.75 s; bosses half. Auto 12 s (rank four −3 s).
Quick shot, pooled chains; death/despawn/boundary teardown.
Snare: cast at feet; arms 0.3 s; first living body within 0.8 m + body radius,
XZ, roots 2 s (+0.5/+0.5; bosses half), hurts 3× weapon (+33% rank two). Expires at 10 s;
two slots, third expires oldest. Auto nearest enemy <3 m, 10 s cooldown (rank three −2 s).
Drawn: upper-body stoop, primitive rope loop + stakes, dust, faint cyan pulse when armed,
snaps to feet then hides; expiry sinks/fades. Pooled four; stage/run clear.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; three bosses. Jungle trees without colliders remain out of scope.

## Known issues

1. ArenaView warns zero spawn points on reload; harmless. TimeManager reserialises; revert before commits.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Stage-boundary non-binding bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full. No reference device.
6. Mother pod Frogs can survive phase beats (JG-12h). Run unload can reactivate destroyed cover (MK-02b smoke).
7. RangerSandbox scope lacks the IRunSession and EnemyViews the Ranger body asks for.