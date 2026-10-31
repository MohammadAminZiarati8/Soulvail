# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks: [OWED.md](OWED.md).

_Updated 2026-11-01, MK-08b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; m6 owner to tag. |
| **Last merged** | **MK-08b**: Binding Shot collider probe, pooled chains, quick bow shot, four ranks and localisation. |
| **Next task** | **MK-09a**: Hunter's Snare in core. |
| **Verified** | EditMode 4 936: 4 935 pass, 0 fail, 1 known animator-clock inconclusive. All 30 Binding Shot/Marksman focused rows pass. Compiler errors 0; new analyzer warnings 0; format clean (14 C# files). Chain/alone render inspected. Stage-12 Editor playcheck appended to OWED. |

## What works

Portrait; Heroes/Places; Jungle 30 stages. Veilrot, Sanctum and disc off; Ranger alone.
Budgeted waves → door; fifth-stage bosses: Warden/Croaker, stage 10 Mother.
Stages 1–14 scripted; five enemies have bodies; Pitcher lobs ringed. Boss adds give no XP.
Mother: facing shields, lobs, blocked spray/goo, pods → Frogs, phase beats.
Ranger: pale arrows, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. Run save v5/profile v5; older saves load.
Clash UI; LocKey strings. Ranger paths from level 11. Marksman: Steady Aim (5 ranks, Lv 11),
Rain of Arrows (4, Lv 11), Binding Shot (4, Lv 14), Eagle Eye (3, Lv 14).
Preview/choice, pips and next-rank Learn. Arc: class then path actives, locks/levels,
cooldown sweep, ready border, auto badge. Ready taps fire on Auto; 0.45 s hold toggles
with haptics; 0.5 s auto gap. Six-circle ellipse outside stick at 360 dp; mirrored option.
Four legacy manual identities expand to six. Player statuses under HP; pooled enemy CC visuals
and timed icons; bosses half duration. Rain: densest living XZ cluster, fixed 2 s zone,
eight weapon-scaled pulses, slow refresh; bow-up release, cyan rim, pooled arrows/dust.
Binding: weapon-speed arrow, 2× damage; nearest living enemy behind in 4 m/20° cone,
otherwise same-frame 0.2 m sphere at 1.1 m against Cover and Default wall/tree solids.
Facts resolve next projectile step; bound 2 s (+0.4/+0.6), alone 0.75 s; bosses half.
12 s auto at one enemy; rank four −3 s cooldown. Quick draw/release, cyan rope tail,
32 pooled chains/clasps; taut in 0.15 s, follow bodies, fade on stun end; alone rope snap.
Death, despawn and stage boundaries clear chains and binding arrows.

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
