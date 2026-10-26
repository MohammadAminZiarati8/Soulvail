# Soulvail — Roadmap

**The plan: milestones → tasks, one line each.** Where the project stands is
[PROGRESS.md](PROGRESS.md); what a task is exactly is its spec in [tasks/](tasks/).

## How to read this

- A task is **1–5 files**. Size S = 1–3, M = 4–5, L = split before starting. Count new code files
  and substantial rewrites; small additive edits (a field, a registration, an event struct) are
  listed in the spec but not counted.
- IDs are `<track>-<nn>` (`M7-01`, `UI-03`); splits keep the parent ID (`UI-03a`, `UI-03b`). Branch
  = lowercase ID + slug.
- A milestone's specs are written by its own first tasks (`-00a…`) before its `-01` starts; later
  milestones stay titles.
- A box is ticked in the change that closes the task.

**Spec pointers:** GD = [GameDesign.md](../GameDesign.md) · CH = [Characters.md](../Characters.md) ·
CC = [CoreCombat.md](../CoreCombat.md) · AR = [Architecture.md](../Architecture.md) ·
ADR = [adr/](../adr/)

## Done

M0 walking skeleton · M1 combat feel · M2 stage loop · M3 levelling and the tree · M4 first boss and
run end · M5 second class · M6 systems complete (Sanctum, Veilrot, Pacts, Ordeals, Emberwright,
unlocks, localisation) · M7 partial (specs for roster, affixes, Choirmother and 81 nodes — removed
unbuilt; the Jungle's art, Frog and pitcher plant built) · RS the Ranger · UI the Clash-style UI and
the portrait switch. Tags `m0`–`m5`; history in `git log`.

## Next

_No milestone. The owner is setting the next direction._

- [x] **JG-01** — Scripted stages: a mode lists a stage's enemies by hand; the Jungle opens with 3 Frogs, then 2 Pitchers (M) · [spec](tasks/JG-01-scripted-stages.md)
- [x] **JG-02** — The Pitcher's spit: an enemy can name its own shot; the Pitcher lobs slower red-orange goo with a trail and a splat (M) · [spec](tasks/JG-02-pitcher-spit.md)
- [x] **RS-07a** — Bigger arrows: the Ranger's arrow at 1.6× with a thin, short cyan trail (S) · [spec](tasks/RS-07a-bigger-arrows.md)
- [x] **RS-07b** — Volley's own arrow: a volley shot is marked, and flies a bigger arrow with a brighter trail and its own burst (M) · [spec](tasks/RS-07b-volley-arrow.md)
- [x] **JG-03a** — The Puffball's model: the Jungle's Bloater as a puffball mushroom, `puffball.py` on a rig of its own with five clips (S) · [spec](tasks/JG-03a-the-puffballs-model.md)
- [x] **JG-03b** — The Puffball's body: prefab, controller, a cyan-white wind-up glow and a spore burst on death (M) · [spec](tasks/JG-03b-the-puffballs-body.md)
- [x] **JG-03c** — Puffballs in the Jungle: `enemy.puffball` with the Bloater's numbers, rostered at stage 4, stage 4 scripted as 2 Puffballs (S) · [spec](tasks/JG-03c-puffballs-in-the-jungle.md)

**Old Croaker, the Jungle's first boss** (owner's go, 2026-10-26). A giant old frog on `Rig_Frog`: hop-slams, tongue lash, croaks up Frogs. Later Jungle bosses (Pitcher Mother, Sporeheart, the Strangler, the Overgrown) are titles only until the owner names them.

- [x] **JG-04a** — Old Croaker's model: `croaker.py` on `Rig_Frog`, boss dressing and seven clips (S) · [spec](tasks/JG-04a-the-croakers-model.md)
- [x] **JG-04b** — Old Croaker's behaviour: `CroakerBehaviour` hop-slams with fissure + shockwave; `EnemyTelegraph.Move` (S) · [spec](tasks/JG-04b-the-croakers-behaviour.md)
- [x] **JG-04c** — Old Croaker in the Jungle: boss behaviour picked by id, the two assets, Jungle's roster (M) · [spec](tasks/JG-04c-the-croaker-in-the-jungle.md)
- [x] **JG-04d** — Old Croaker's body: prefab and controller, a clip per `Move` (M) · [spec](tasks/JG-04d-the-croakers-body.md)
- [x] **JG-04e** — The tongue lash: `LaneSystem` in core, its view, the Croaker's second attack (M) · [spec](tasks/JG-04e-the-tongue-lash.md)
