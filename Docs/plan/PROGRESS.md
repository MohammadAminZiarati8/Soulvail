# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, JG-15b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **JG-15b**: Warlord shield charge, lane probe port and stone daze in core. |
| **Next task** | **JG-15c** (war cry: two Mossguards from the edges, shield lock, cleared at beats), on the owner's go; JG-15c–g specced. |
| **Verified** | EditMode 5 044: 5 043 pass, 0 fail, 1 known animator-clock inconclusive. All 23 charge cases pass. Compiler errors 0; new analyzer warnings 0; format clean (16 files). No new owner checks for this core-only task. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Stages 1–14 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Warlord core: 60° front guard, 40°/s path turn, 45° walk cone. After a 1.4 s gap,
0.8 s caught-aim sweep: 3.5 m/60°, 1.5× contact damage, no LOS, 0.8 s recovery.
Far/off cooldown: 0.9 s charge warning, probe-clipped 14 m lane, 2× contact damage;
16 m/s dash follows reported XZ travel, bounded timeout. Reached stone: 2 s daze,
guard down; 6 s cooldown from dash end. Root blocks motion; stun pauses behaviour.
Phase drops charge but keeps opened lanes. Probe adapter/body/data await JG-15g.
Ranger: pale arrows, cyan Volley, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run/profile save v5; older saves load.
Clash UI; LocKey strings. Marksman path complete: all 22 ranks from level 11;
Steady Aim, Rain of Arrows, Binding Shot, Eagle Eye, Hunter's Snare, Killshot.
Tree preview/choice, rank pips and Learn. Six-circle arc: tap fire on Auto,
0.45 s hold toggles/haptics; 0.5 s auto gap; mirrored option. Legacy saves expand to six.
Player status icons; pooled enemy CC, bosses half duration. All Marksman skills authored/drawn.
Marksman look: forest hood/cloak, cyan trim, KayKit quiver, emissive string,
brighter arrows; choice shimmer, silent resume, plain menu/fresh run. Reversible.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; Warlord integration awaits JG-15c–g.

## Known issues

1. ArenaView zero-spawn reload warning; TimeManager reserialises.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Boundary non-binding bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full; no reference device.
6. Mother pod Frogs survive phase beats (JG-12h). Unload can reactivate destroyed cover (MK-02b).
7. RangerSandbox scope lacks IRunSession and EnemyViews.
