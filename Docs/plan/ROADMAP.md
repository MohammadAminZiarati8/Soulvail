# Soulvail — Roadmap

**The plan: milestones → tasks, one line each.** Where the project stands is
[PROGRESS.md](PROGRESS.md); what a task is exactly is its spec in [tasks/](tasks/).

## How to read this

- A task is **1–8 files**. Size S = 1–3, M = 4–8, L = split before starting. Count new code files
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
the portrait switch · JG-01–04 the Jungle's first five stages (scripted stages, the Pitcher's spit,
the Puffball, Old Croaker) and RS-07 the Ranger's arrows — specs removed 2026-10-26, kept in git.
Tags `m0`–`m5`; history in `git log`.

## Next

_No milestone. The owner is setting the next direction._

**Playtest fixes** (owner, 2026-10-26). Four independent tasks, each built only on the owner's go.

- [x] **JG-05a** — A blocked body reports its real speed, so arrows hit a Pitcher backed against a wall (S) · [spec](tasks/JG-05a-a-blocked-body-reports-its-real-speed.md)
- [x] **JG-05b** — The Frog rests 0.8 s between hops, each hop the same length (S) · [spec](tasks/JG-05b-the-frog-rests-between-hops.md)
- [x] **JG-05c** — Old Croaker hops in like a Frog, never walks; `HopGait` shared with the chaser (M) · [spec](tasks/JG-05c-old-croaker-hops-in.md)
- [x] **JG-05d** — A smaller Puffball blast: 2.5 m, the Bloater keeps 3 m (S) · [spec](tasks/JG-05d-a-smaller-puffball-blast.md)

**Old Croaker's effects** (owner, 2026-10-26). Built only on the owner's go; c after b.

- [x] **JG-06a** — The tongue reaches down the lane: a drawn tongue shoots to the lane's end on `LaneFired` (S) · [spec](tasks/JG-06a-the-tongue-reaches-down-the-lane.md)
- [x] **JG-06b** — The slam lands heavy: camera shake and an earth-and-dust burst on every `ShockwaveEmitted` (M) · [spec](tasks/JG-06b-the-slam-lands-heavy.md)
- [x] **JG-06c** — The shockwave is a ground wave: a band as wide as the bite, with an earth lip on its edge (M) · [spec](tasks/JG-06c-the-shockwave-is-a-ground-wave.md)

**The Lunger: a panther** (owner, 2026-10-27). Specs for the owner to OK; each built only on the owner's go. a and b are independent; c needs b, d needs a and c.

- [x] **JG-07a** — The Lunger dashes down a lane: `LungerBehaviour` stalks, warns with a lane for 0.9 s, dashes 12 m; lanes carry a tongue flag (M) · [spec](tasks/JG-07a-the-lunger-dashes-down-a-lane.md)
- [x] **JG-07b** — The Panther's model: the TRELLIS sculpt cut to the crowd's budget, repainted on the atlas, on `Rig_Panther` with six clips (M) · [spec](tasks/JG-07b-the-panthers-model.md)
- [x] **JG-07c** — The Panther's body: prefab and animator, crouch on the wind-up, bound through the dash; the tongue only down a tongue's lane (M) · [spec](tasks/JG-07c-the-panthers-body.md)
- [x] **JG-07d** — Panthers in the Jungle: `enemy.panther` from stage 6, stage 6 scripted as 2 Panthers (M) · [spec](tasks/JG-07d-panthers-in-the-jungle.md)

**Playtest fixes, the Ranger and Old Croaker** (owner, 2026-10-28). Specs for the owner to OK; each built only on the owner's go. RS-08b after RS-08a (both `PlayerCombat.cs`); the rest are independent.

- [x] **RS-08a** — The Ranger's rate holds (no Focus ramp) and the first arrow after stopping leaves in ~0.27 s, the draw timed to it (M) · [spec](tasks/RS-08a-the-rangers-rate-holds.md)
- [x] **RS-08b** — A volley looses one arrow at every enemy in range, not a fixed fan (M) · [spec](tasks/RS-08b-a-volley-finds-every-enemy.md)
- [x] **RS-08c** — A pale wind trail on the ordinary arrow; the volley arrow cyan and ~1.9× its size (S) · [spec](tasks/RS-08c-a-wind-trail-on-the-arrow.md)
- [x] **JG-08a** — Enemies keep their colour as they lose health; bars carry the read (S) · [spec](tasks/JG-08a-enemies-keep-their-colour.md)
- [x] **JG-08b** — Old Croaker holds its aim down the lane through the lash (S) · [spec](tasks/JG-08b-old-croaker-holds-its-aim.md)
- [x] **JG-08c** — Old Croaker hits for 12, not 22 (S) · [spec](tasks/JG-08c-old-croaker-hits-softer.md)

**Dev tools** (owner, 2026-10-29).

- [x] **DT-01** — Test start at any stage: a dev-only menu panel begins a run at stage N with the XP before it banked as picks (M) · [spec](tasks/DT-01-test-start-at-any-stage.md)
