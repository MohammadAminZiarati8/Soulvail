# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, MK-10b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-10b**: Killshot drawn and authored. |
| **Next task** | **MK-11** — The Marksman's look. |
| **Verified** | EditMode 4 995: 4 994 pass, 0 fail, 1 known animator-clock inconclusive. Compiler errors 0; new analyzer warnings 0; format clean (14 files). Level-20 playcheck owed (OWED). |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Stages 1–14 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run save v5/profile v5; older saves load.
Clash UI; LocKey strings. Ranger paths from level 11. Marksman: Steady Aim (5 ranks, Lv 11),
Rain of Arrows (4, Lv 11), Binding Shot (4, Lv 14), Eagle Eye (3, Lv 14), Hunter's Snare (3, Lv 17), Killshot (3, Lv 20).
Tree preview/choice, rank pips and Learn. Six-circle skill arc: ready taps fire on Auto,
0.45 s hold toggles with haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half duration. Rain: densest XZ cluster, eight
weapon-scaled pulses in 2 s; slow, cyan rim, pooled arrows/dust. Binding: 2× arrow,
rear body or solid probe; 2 s (+0.4/+0.6), alone 0.75 s. Auto 12 s (rank four −3 s).
Snare: two slots; arms 0.3 s, radius 0.8 m + body, roots 2 s (+0.5/+0.5), damage 3×
(+33% rank two), expires 10 s. Auto nearest <3 m, cooldown 10 s (rank three −2 s).
Drawn stoop, pooled rope/stakes, pulse, snap, dust and expiry fade; stage/run clear.
Killshot: locked crowded XZ line; 0.7 s crouched draw/glow/aim; 30 m cyan-white streak,
0.1 s bright + 0.4 s fade; 0.55 m/0.22 s camera jolt; pooled sparks and execute bursts.
8× weapon, −0.15/hit to 0.4 floor; ignores walls. Non-boss executes at ≤15/20/25% HP.
Auto ≥3 in acquire range; 30 s cooldown (rank three −8 s); rank two +25% damage.
Stands in 0.18 s; death/boundary/run end clear pose and VFX. All 22 Marksman ranks authored.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; three bosses. Jungle trees without colliders out of scope.

## Known issues

1. ArenaView zero-spawn reload warning; TimeManager reserialises.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Boundary non-binding bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; no reference device.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
