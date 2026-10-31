# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, MK-08a built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-08a**: Binding Shot core: projectile hit, enemy-first anchor, obstacle fact port, delayed stuns, rank stat and boundary cleanup. |
| **Next task** | **MK-08b**: Binding Shot in Unity: the anchor probe, chain and assets. |
| **Verified** | EditMode 4 916: 4 915 pass, 0 fail, 1 known animator-clock inconclusive; all 26 Binding Shot rows pass, including AllocationAssert.None. Compiler errors 0; new analyzer warnings 0; format clean (20 C# files). No new manual Editor checks. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Stages 1–14 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards.
Run save v5/profile v5; older saves load. Four legacy manual identities expand to six.
Clash UI; LocKey strings. Ranger paths from level 11. Marksman: Steady Aim (5 ranks, Lv 11),
Rain of Arrows (4, Lv 11), Eagle Eye (3, Lv 14); preview/choice, pips and next-rank Learn.
Arc: class then path actives, locks/levels, cooldown sweep, ready border, auto badge.
Ready taps fire on Auto; 0.45 s hold toggles with haptics; 0.5 s auto gap.
Six-circle ellipse outside stick at 360 dp; mirrored option. Player statuses under HP.
Enemy stun freezes behaviour, root zeroes moves, strongest slow wins; bosses half duration.
Pooled CC visuals and timed icons. Rain: densest living XZ cluster, fixed 2 s zone,
eight weapon-scaled pulses, slow refresh; four ranks, bow-up release, cyan rim,
pooled falling arrows/dust, boundary cleanup.
Binding Shot core: current target else nearest in range; weapon-speed arrow, 2× damage.
On hit: nearest living enemy behind in a 4 m/20° cone first, otherwise an obstacle probe.
Facts resolve next projectile step; two unanswered ticks → alone. Bound 2 s + BindStun
(+0.4/+0.6), alone 0.75 s, bosses half; one event, no dead-target bind.
12 s auto trigger at one enemy; rank-four cooldown supports −3 s. Probes clear with shots.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past ~26 late waves are all Bloaters/Puffballs.
4. No audio, Elites, feel pass or second language; three bosses. Binding probe/visuals/assets await MK-08b.

## Known issues

1. ArenaView warns zero spawn points on reload; harmless. TimeManager reserialises; revert before commits.
2. ContactDamage ruling pending. Invalid tree saves strand Continue; XP 1e38 hangs levelling.
3. Stage-boundary bolts leave bodies; pooled Bind/Move can snap with autoSyncTransforms off.
4. Sanctum draws a frame late; nothing damages a Wight; Knight Charge root snaps 0.22 m.
5. HUD ~15 draw calls; no PlayMode Continue row; disk near full. No reference device.
6. Mother pod Frogs can survive phase beats (JG-12h). Run unload can reactivate destroyed cover (MK-02b smoke).
