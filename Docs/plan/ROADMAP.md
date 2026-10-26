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

- [ ] **JG-06a** — The tongue reaches down the lane: a drawn tongue shoots to the lane's end on `LaneFired` (S) · [spec](tasks/JG-06a-the-tongue-reaches-down-the-lane.md)
- [ ] **JG-06b** — The slam lands heavy: camera shake and an earth-and-dust burst on every `ShockwaveEmitted` (M) · [spec](tasks/JG-06b-the-slam-lands-heavy.md)
- [ ] **JG-06c** — The shockwave is a ground wave: a band as wide as the bite, with an earth lip on its edge (M) · [spec](tasks/JG-06c-the-shockwave-is-a-ground-wave.md)
