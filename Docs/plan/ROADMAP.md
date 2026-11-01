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
- A box is ticked, its spec link dropped and its spec deleted in the change that closes the task.

**Spec pointers:** GD = [GameDesign.md](../GameDesign.md) · CH = [Characters.md](../Characters.md) ·
CC = [CoreCombat.md](../CoreCombat.md) · AR = [Architecture.md](../Architecture.md) ·
ADR = [adr/](../adr/)

## Done

M0 walking skeleton · M1 combat feel · M2 stage loop · M3 levelling and the tree · M4 first boss and
run end · M5 second class · M6 systems complete (Sanctum, Veilrot, Pacts, Ordeals, Emberwright,
unlocks, localisation) · M7 partial (specs for roster, affixes, Choirmother and 81 nodes — removed
unbuilt; the Jungle's art, Frog and pitcher plant built) · RS the Ranger · UI the Clash-style UI and
the portrait switch · JG-01–14f the Jungle's stages 1–14 (scripted stages, Pitcher, Puffball, Old
Croaker, Panther, health bars, Mother Pitcher, cover, Mossguard) · RS-07–09a the Ranger's arrows,
rate, volley and flinch · DT-01 test start at any stage. Finished specs are deleted; their *As built*
footers are in `git log` (last sweep 2026-10-31). Tags `m0`–`m5`.

## Next

_No milestone. The owner is setting the next direction._

**The Marksman, the Ranger's first path** (owner, 2026-10-31; design [CH §3.5](../Characters.md), HUD [GD §5.2](../GameDesign.md)). Specs written for an outside implementer; each built only on the owner's go, one at a time, in this order. Playtestable from MK-03 on, with DT-01's test start at stage 12+.

- [x] **MK-01a** — A path's shape: `PathSpec`, skills as ranked branches with an unlock level, installed after the class's tree; a gated node is unavailable below its level (M)
- [x] **MK-01b** — A path is chosen from level 11: `PathFlow`, the free-pick command, derived on resume, no Overflow while a path has room (M)
- [x] **MK-02a** — Steady Aim and Eagle Eye in core: standing stacks, the stop crit, the full-stack guard, far-target damage (M)
- [x] **MK-02b** — The Marksman authored: `PathDefinition`, the Ranger's three path slots, Steady Aim and Eagle Eye as assets (M)
- [x] **MK-03** — The tree screen: one icon per skill with rank pips, path cards, level-locked rows (M)
- [x] **MK-04** — The skill arc: every active a circle beside the roll, tap casts, long-press toggles auto; passives in the corner; auto-casts 0.5 s apart (M)
- [x] **MK-05** — The status row: buffs and debuffs beside the HP bar with timers and stacks (M)
- [x] **MK-06a** — Enemies can be stunned, rooted and slowed (M)
- [x] **MK-06b** — Stun, root and slow are drawn on the enemy and over its bar (M)
- [x] **MK-07a** — Rain of Arrows in core (M)
- [x] **MK-07b** — Rain of Arrows drawn and authored (M)
- [x] **MK-08a** — Binding Shot in core, with Unity's anchor probe as a port (M)
- [x] **MK-08b** — Binding Shot in Unity: the probe, the chain, the assets (M)
- [x] **MK-09a** — Hunter's Snare in core (M)
- [x] **MK-09b** — Hunter's Snare drawn and authored (M)
- [x] **MK-10a** — Killshot in core (M)
- [x] **MK-10b** — Killshot drawn and authored: the kneel, the streak, the jolt (M)
- [x] **MK-11** — The Marksman's look: a path changes the hero's look, data-driven for every later path (M)

**The Mossguard Warlord, stage 15's boss** (owner, 2026-11-01; design [GD §9.1–9.2](../GameDesign.md)). Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order (15e may go any time before 15f). Playtestable from JG-15g with DT-01's test start at stage 15.

- [x] **JG-15a** — The Warlord in core: front guard, slow turn, glaive sweep (M)
- [x] **JG-15b** — Shield charge and stone daze, with a lane probe port (M)
- [x] **JG-15c** — War cry: two Mossguards from the edges, shield lock, cleared at beats (M)
- [x] **JG-15d** — Phases: spear lobs at 66 %, the shield shatters at 33 % (M)
- [x] **JG-15e** — The model: the Mossguard grown and war-painted, glaive, big shield, sculpt slot (M)
- [x] **JG-15f** — The body: animator, guard wedge, daze, shatter (M)
- [x] **JG-15g** — Stage 15 is his: data, stones, spear, lane probe adapter (M)

**Stages 16–19 and the Moss Shaman** (owner, 2026-11-01; design [GD §8.1–8.2](../GameDesign.md)). A lizard shaman in the Choir's role: never attacks, hides behind Mossguards at the edge of range, heals and hastens them, runs when you close. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order (16a any time; 16c needs the owner's two sculpts and may go any time before 16d). Playtestable from JG-16e with DT-01's test start at stage 17.

- [x] **JG-16a** — Stage 16 is a breather: known enemies, 121 threat (S)
- [x] **JG-16b** — The Moss Shaman in core: anchor, range-edge hiding, heal and haste pulse, flee (M)
- [x] **JG-16c** — The model: the owner's sculpt on Rig_Medium, staff on the right hand (M)
- [ ] **JG-16d** — The body: animator, seed glow, aura ring, healed bars, haste mark (M)
- [ ] **JG-16e** — The Shaman in the Jungle: data, roster from 17, stage 17 scripted (M)
- [ ] **JG-16f** — Stages 18 and 19 scripted, climbing to 208 (S)
