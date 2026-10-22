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
