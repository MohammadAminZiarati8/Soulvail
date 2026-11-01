# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, JG-16b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **JG-16b**: Moss Shaman core: anchor, range-edge hiding, heal/haste pulse and flee. |
| **Next task** | **JG-16c**. Owner supplied body and staff; model conversion underway, then 16d–e. |
| **Verified** | EditMode 5 128: 5 127 pass, 1 known inconclusive. Compiler errors 0; analyzer warnings 0 new; format clean. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother, 15 Warlord.
Stages 1–14 and 16 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Warlord core: 60° front guard, 40°/s turn, 45° walk cone; 1.4 s gap, 0.8 s
sweep 3.5 m/60°, 1.5× damage, 0.8 s recovery. Far: 0.9 s charge warning,
probe-clipped 14 m lane, 2× damage, 16 m/s dash; reached stone: 2 s daze, guard
down; 6 s cooldown. Phase drops the move, keeps opened lanes. Cover sphere
cast probes the lane. Stage 15: 825 HP (~95 s), five uneven stones.
War cry from 8 s, 14 s cooldown: 1 s stand, up to two 0-XP Mossguards on far
open ground, else a ring; a guard within 3.5 m locks 90°; beats despawn, rearm 8 s.
Phase 1: 0.8 s lob, 3 ringed spears at ≥ 5 m, 5 s cooldown. Phase 2: shield
shatters (no guard, +40 % speed, 120°/s, 0.5/0.9 s gaps), sweep → 0.8+0.6 s combo.
Warlord: painted Mossguard variant, glaive; a clip per move, guard wedge, daze, shatter.
Ranger: pale arrows, cyan Volley, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run/profile save v5; older saves load.
Clash UI; LocKey strings. Marksman path complete: all 22 ranks from level 11;
Steady Aim, Rain of Arrows, Binding Shot, Eagle Eye, Hunter's Snare, Killshot.
Tree preview/choice, rank pips and Learn. Six-circle arc: tap fire on Auto,
0.45 s hold toggles/haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half duration. All Marksman skills authored/drawn.
Marksman look: forest hood/cloak, cyan trim, KayKit quiver, emissive string,
brighter arrows; choice shimmer, silent resume, plain menu/fresh run.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language.

## Known issues

1. ArenaView zero-spawn reload warning; TimeManager reserialises.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Boundary non-binding bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; no reference device.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
8. Warlord's death dissolve shows the unpainted atlas.
