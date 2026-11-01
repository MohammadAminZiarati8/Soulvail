# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, MK-11 built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-11**: the Marksman's look; the Marksman path is complete. |
| **Next task** | **Owner to name — the Marksman path is complete.** |
| **Verified** | EditMode 5 003: 5 002 pass, 0 fail, 1 known animator-clock inconclusive. Compiler errors 0; new analyzer warnings 0; format clean (6 files). Menu/run smoke 1/1 pass. Look/resume playchecks owed (OWED). |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Stages 1–14 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run save v5/profile v5; older saves load.
Clash UI; LocKey strings. Ranger paths from level 11. All 22 Marksman ranks authored:
Steady Aim (5 ranks, Lv 11), Rain of Arrows (4, Lv 11), Binding Shot (4, Lv 14),
Eagle Eye (3, Lv 14), Hunter's Snare (3, Lv 17), Killshot (3, Lv 20).
Tree preview/choice, rank pips and Learn. Six-circle skill arc: ready taps fire on Auto,
0.45 s hold toggles with haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half duration. Rain: densest XZ cluster,
eight pulses in 2 s, slow, cyan rim, pooled arrows/dust. Binding: 2× arrow, rear body/solid
probe; 2–3 s stun, alone 0.75 s. Snare: two slots; 0.3 s arm, 0.8 m + body radius,
2–3 s root, 3× weapon damage, 10 s expiry; pooled rope/stakes/snap/dust.
Killshot: crowded XZ line; 0.7 s crouched draw/aim, 30 m cyan-white streak, camera jolt,
pooled sparks/execute bursts; 8× weapon with per-hit falloff; non-boss executes at 15–25% HP.
Marksman look: data-driven material slots/atlas meshes and socket props; forest hood/cloak,
cyan trim, KayKit back quiver, emissive string, brighter ordinary arrows. Choice shimmers
0.4 s; resume silent; fresh run/menu plain. Reversible; shared assets untouched by applying.

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
