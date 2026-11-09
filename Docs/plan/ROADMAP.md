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

**The Beastmaster, the second character** (owner, 2026-11-07; design [CH §3.6–3.7](../Characters.md)). The Jungle's reward: returning axes, the Mark, the Hound, Wild Swap, a 3 × 3 tree, then the Packleader path. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. Playtestable from BM-02a (class select); **playtest pet survival after BM-06c**, before the tree (the 20 s self-revive is the lever, CH §3.6.1). BM-03b, PL-04b and PL-05b need the owner's TRELLIS sculpt (prompt in the spec) any time before their drawn task. The drawn tasks name no screen position, so they hold through the landscape switch.

- [x] BM-01a — Returning axes in core: full-range flight, homing return, hits on both passes (M)
- [x] **BM-01b** — The Mark in core: an axe hit marks for 4 s (S)
- [x] **BM-02a** — The Beastmaster's body and character asset, in class select (M)
- [x] **BM-02b** — Axes and the Mark drawn: spin, cyan trail, claw glyph (M)
- [x] BM-03a — The Hound in core: `PetSystem`, leash, marked-first, bite, XP, level growth (M)
- [x] BM-03b — The Hound's model on `Rig_Panther` (M)
- [x] BM-03c — The Hound drawn and in the run, always-on cyan bar (M)
- [x] BM-04 — Enemies turn on the Hound: the hunt rule, two hunters, strikes land, it falls (M)
- [x] BM-05a — Heel, regen and rescue in core (M)
- [x] BM-05b — Heel, fall and rescue drawn: the ring, the kneel (M)
- [x] BM-06a — Wild Swap in core on `ChargeSkill` (M)
- [x] BM-06b — Wild Swap drawn and authored: both leap (M)
- [x] BM-06c — Wild Swap's cyan streak and button icon (M)
- [x] BM-07a — Feral Bite and light enemies (M)
- [x] BM-07b — Cleaving Return and Quick Swap in core (S)
- [x] BM-07c — Pounce in core (M)
- [x] **BM-07d** — The tree authored: nine nodes, Pounce in the arc (M)
- [x] BM-07e — Quick Swap's roar drawn: the howl, the cyan ring (S)
- [x] BM-08 — The Beastmaster is the Jungle's reward: beat the Apex or buy (S)
- [x] PL-01a — Pack Hounds in core (M)
- [x] PL-01b — The Packleader authored: the path slot, its card, Pack Hounds (M)
- [x] PL-02a — Howl in core (M)
- [x] PL-02b — Howl authored: four ranks, the second Lv-11 row (S)
- [x] PL-02c — Howl drawn: the howl, the glow, the pulse (M)
- [x] PL-03 — Pack Tactics and Thick Pelts (M)
- [x] PL-04a — Hawk in core (M)
- [x] PL-04b — The hawk's model on `Rig_Hawk` (M)
- [x] PL-04c — Hawk drawn and authored (M)
- [x] PL-05a — Stampede in core (M)
- [x] PL-05b — The boar's model on `Rig_Panther` (M)
- [x] PL-05c — Stampede drawn and authored (M)
- [x] PL-06 — The Packleader's look: cyan war-paint, data and a pet tint (S)

**Beastmaster tune** (owner playtest, 2026-11-08; design CH §3.6–3.7 updated the same day). One at a time on the owner's go, in this order.

- [x] BT-01 — Two axes at once, attack speed sets the cadence, Swift Hands (M)
- [x] BT-02 — Axes stop at cover (S)
- [x] BT-03 — Animals hunt the whole arena, your last mark first; the hawk climbs back (M)
- [x] BT-04 — The pack's formation: slots, spacing, eased following (M)
- [x] BT-05 — Wild Swap lands him exactly where the hound was (S)
- [x] BT-06a — Frenzy in core; Howl heals only (M)
- [x] BT-06b — Frenzy authored at Lv 20 and drawn (M)
- [x] BT-06c — Stampede's code removed (S)
- [x] BT-07a — The boar in core: a ranged animal that spits (M)
- [x] BT-07b — The boar drawn and authored at Lv 14 (M)
- [x] BT-08 — Bond Barrier: every swap shields him; Quick Swap shoves (M)
- [x] BT-09a — The axe pair closes on its target; the swap shoves nobody (S)
- [x] BT-09b — A frontal guard judges where a pet's hit comes from (M)

- [ ] AM — Axe Master, the second path (titles only; specced on the owner's word)
- [ ] AB — Apex Bond, the third path (titles only; specced on the owner's word)

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
- [x] **MK-10c** — Killshot pierces frontal guards, keeps boss phase shields, widens to 3 m, and flies as a 4x arrow with wind (M)
- [x] **MK-11** — The Marksman's look: a path changes the hero's look, data-driven for every later path (M)

**The Skirmisher, the Ranger's second path** (owner, 2026-11-05; design [CH §3.5](../Characters.md)). Each built only on the owner's go, one at a time, in this order. Playtestable from SK-01b on, with DT-01's test start at stage 12+.

- [x] **SK-01a** — Running Shot and Hit and Run in core: moving fire rate, aim-facing, speed burst, Momentum (M)
- [x] **SK-01b** — The Skirmisher authored: the Ranger's second path slot, its card, both passives as assets (M)
- [x] **SK-02** — The Ranger shoots on the run: bow up, strafing legs, upper-body release (M)
- [x] **SK-03a** — Ricochet Shot in core: an arrow chain that bounces to the nearest unhit enemy (M)
- [x] **SK-03b** — Ricochet Shot drawn and authored: bright trail, bounce sparks (M)
- [x] **SK-04a** — Second Wind in core: roll cooldown, the after-roll guard, two charges (M)
- [x] **SK-04b** — Second Wind drawn and authored: charge pips on the roll button, the Dodged pop (M)
- [x] **SK-05a** — Whirlwind in core: rings of arrows all round, 0.3 s apart (M)
- [x] **SK-05b** — Whirlwind drawn and authored: the spin and the swirl (M)
- [x] **SK-06a** — Storm Run in core: rolls end in a volley, roll cooldown halved (M)
- [x] **SK-06b** — Storm Run drawn and authored: the wind aura, the burst ring (M)
- [x] **SK-07** — The Skirmisher's look: silver-cyan trim and tints, data only (S)
- [x] **SK-08** — Skirmisher retune: Running Shot to 125%, Whirlwind and Storm Run cast on one enemy, data only (S)

**The Echo Archer, the Ranger's third path** (owner, 2026-11-06; design [CH §3.5](../Characters.md)). Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. Playtestable from EA-02 on, with DT-01's test start at stage 12+. The drawn tasks name no screen position, so they hold through the landscape switch.

- [x] **EA-01a** — Shades and Mirror Image in core: bodies that stand and shoot, the cap of 8, expiry (M)
- [x] **EA-01b** — Echo Arrow in core: every Nth basic arrow loosed again 0.15 s later (S)
- [x] **EA-01c** — The Echo Archer authored: the third path slot, its card, Echo Arrow's assets (M)
- [x] **EA-02** — Shades drawn, Mirror Image authored: the smoky copy, its shot, its fade (M)
- [x] **EA-03a** — Decoy Roll in core (M)
- [x] **EA-03b** — Fading Burst in core (S)
- [x] **EA-03c** — Decoy Roll and Fading Burst drawn and authored (M)
- [x] **EA-04a** — Crossfire in core: two shades appear, every shade looses at the target (M)
- [x] **EA-04b** — Crossfire drawn and authored: converging heavy arrows (M)
- [x] **EA-05a** — Hall of Mirrors in core: a ring of shooting shades (M)
- [x] **EA-05b** — Hall of Mirrors drawn and authored: the ring rising (M)
- [x] **EA-06** — The Echo Archer's look: smoky deep-cyan trim and tints, data only (S)

**Landscape** (owner, 2026-11-06): the game turns from portrait to landscape. Not specced; camera, HUD, tree screen and menus all move. To be planned on the owner's word.

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
- [x] **JG-16d** — The body: animator, seed glow, aura ring, healed bars, haste mark (M)
- [x] **JG-16e** — The Shaman in the Jungle: data, roster from 17, stage 17 scripted (M)
- [x] **JG-16f** — Stages 18 and 19 scripted, climbing to 208 (S)
- [x] **JG-16g** — Preserve the repaired Shaman body and texture in game (owner correction)
- [x] **JG-16h** — Preserve the repaired staff and source grip in game (owner correction)
- [x] **JG-16i** — Shaman gait: hips that move, a staff that stays, faces where it goes (owner correction)
- [x] **JG-16j** — Shaman hit, cast and death rebuilt without frame flips (owner correction)

**Buffs and debuffs** (owner, 2026-11-02; design [GD §16.5](../GameDesign.md)). Statuses get an identity and a side; heal stays an effect. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order (ST-02 and ST-03a may swap). Playtestable from ST-03b with the debug overlay's Poison key; a poison source in play is the owner's pick.

- [x] **ST-01** — Enemy statuses get an id, a side and a priority; the enum goes (M)
- [x] **ST-02** — The enemy bar's row: by id, debuffs first, one icon on basic enemies, four on Elites (M)
- [x] **ST-03a** — Poison in core: capped stacks, one timer, pulses through the damage door (M)
- [x] **ST-03b** — Poison drawn: bubbles on the body, a drop on the bar (S)
- [ ] ST-04 — Regen: heal over time as a buff (titles only from here)
- [ ] ST-05 — Stat-up buffs: attack, defense, attack speed; strongest wins
- [ ] ST-06 — Stat-down debuffs: weaken, armour break
- [ ] ST-07 — A status row under the boss bar, once a boss carries one

**Thornheart, stage 20's boss** (owner, 2026-11-02; design [GD §9.2](../GameDesign.md)). A strangler tree rooted in the centre, always shootable: thorn spokes that block only the player, a slam that bares its heart, seed pods that grow Rootlings unless shot, a thorn ring closing in from 66 %. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order (20e after 20a, 20f any time before 20g). Playtestable from JG-20h with DT-01's test start at stage 20.

- [x] **JG-20a** — Thorn walls in core: arm, bite, stand, wither (M)
- [x] **JG-20b** — Thornheart in core: rooted, spokes, the slam that bares its heart (M)
- [x] **JG-20c** — Seed pods: a kill race that grows Rootlings (M)
- [x] **JG-20d** — Phases: the strangling ring at 66 %, a faster grove at 33 % (M)
- [x] **JG-20e** — Thorn walls in Unity: warned strip, a wall that blocks only you (M)
- [x] **JG-20f** — The model: strangler tree, glowing heart, thorn pod (M)
- [x] **JG-20g** — The body: tells, lit heart, ripening pods, the ring (M)
- [x] **JG-20h** — Stage 20 is Thornheart's: data, pod, roster (M)
- [x] **JG-20i** — The thorn wall's vine: a tiled TRELLIS segment replaces the box (S)
- [x] **JG-20j** — The ring reads: everything outside it tinted, each bite flashes (S)

**Stages 21–24** (owner, 2026-11-03; design [GD §8.2](../GameDesign.md)). The cousins replace the easy four, then the Chameleon stalker and the Thornweaver arrive; threat climbs 194 → 218 → 242 → 269 into Gravemaw. Specs in [tasks/](tasks/); each built only on the owner's go, in this order. 22b and 24b need the owner's TRELLIS sculpt (prompt in the spec) and may be built any time before their body task.

- [x] **JG-21a** — The Bullfrog's leap: crouch, fixed landing ring, flight, bite (M)
- [x] **JG-21b** — Shadow panthers pair up: leader in, flanker to the side, lunge together (M)
- [x] **JG-21c** — The Thorn pitcher throws a fan of three ringed lobs (S)
- [x] **JG-21d** — The Chain bomber's blast lights nearby bombers (S)
- [x] **JG-21e** — Drawn: the landing ring, the hop arc, chain links and fuse flash (M)
- [x] **JG-21f** — Cousin assets, the easy four retired at 21, stage 21 scripted (M)
- [x] **JG-22a** — Chameleon stalker in core: hides when still, strikes from behind (M)
- [x] **JG-22b** — The stalker's model on `Rig_Medium` (M)
- [x] **JG-22c** — The stalker's body: shimmer, reveal flash, hidden bar (M)
- [x] **JG-22d** — Stalker asset, roster at 22, stage 22 scripted (S)
- [x] **JG-23** — Stage 23 mixes what the player knows, 242 threat (S)
- [x] **JG-24a** — Thornweaver in core: walls beyond the player on `ThornSystem` (M)
- [x] **JG-24b** — The Thornweaver's model on `Rig_Medium` (M)
- [x] **JG-24c** — The Thornweaver's body: cast, crown glow, root line (M)
- [x] **JG-24d** — Thornweaver asset, roster at 24, stage 24 scripted (S)

**Stages 25–30** (owner, 2026-11-02; design [GD §8.2, §9.2](../GameDesign.md)). Specced after stages 21–24 are built and played. Titles only.

- [x] **JG-25a** — Gravemaw in core: burrow chase, predicted ring, eruption, window (M)
- [x] **JG-25b** — Gravemaw's eruption throws the bitten player out of the ring, stopped by walls (S)
- [x] **JG-25c** — Gravemaw's tremor and rock spit, one move per window (M)
- [x] **JG-25d** — Gravemaw's phase 2 at 50 %: double surface, Frogs from cracks (M)
- [x] **JG-25e** — Gravemaw's model: TRELLIS burrower on its own rig, maw lit apart (M)
- [x] **JG-25f** — Gravemaw's body: trail, ring, rise and dive, lit maw, cracks (M)
- [x] **JG-25g** — stage 25 is Gravemaw's: boss data, ruins, rock, roster row (M)
- [x] **JG-25h** — Gravemaw tunnels through cover and surfaces only on open ground (M)
- [x] **JG-26a** — bosses ignore stun, root and slow; poison still applies (M)
- [x] **JG-26b** — Binding shot on a boss draws no rope unless it holds an ordinary enemy (S)
- [x] **JG-26c** — Stages 26–29 mix what the player knows, 295 → 373 threat (S)
- [x] **JG-26d** — Every boss's adds die with it (S)

**Stage 30, the Apex** (owner, 2026-11-04; design [GD §9.2](../GameDesign.md)). The Jungle's last boss, its one fast one: visible, no grass. Built in this order on the owner's go; 30d needs the owner's TRELLIS sculpt (prompt in the spec) any time before it.

- [x] **JG-30a** — The Apex in core: hunt, dash lane, rake (M)
- [x] **JG-30b** — The Apex's pounce and feint (M)
- [x] **JG-30c** — The Apex's phases: the pack at 66 %, the hunt at 33 % (S)
- [x] **JG-30d** — The Apex's model on `Rig_Panther` (M)
- [x] **JG-30e** — The Apex drawn: eyes, feint, dash streak, window, roar (M)
- [x] **JG-30f** — Stage 30 is the Apex's; the Jungle is done (M)

**Killshot presentation** (owner, 2026-11-05).

- [x] **JG-31b** — Killshot reads as a giant arrow: 4x existing arrow, streaky wind particles and faint path trail; view only (S)

**Levelling growth** (owner, 2026-11-05).

- [x] **LV-01a** — Every level-up raises max HP and weapon damage: +3 % HP, +2 % damage of base, mode-authored, heals the gain (S)
- [x] **LV-01b** — Boss HP rises to match level growth: seven bosses re-authored, fight-length probes price the player's level (S)
- [x] **LV-02a** — Level cap 30, reached at stage 25 (perLevel 5.1); XP strip reads MAX (S)
- [x] **LV-02b** — The Overflow toast leaves the HUD (S)
- [x] **LV-02c** — Overflow leaves core and the modes; old saves still load (M)
- [x] **LV-02d** — Marksman ranks merge to fit 29 points; over-rank saves refunded (S)

**Playtest fixes** (owner, 2026-11-05). Any order; each its own commit.

- [x] **FX-01** — No banked points past the level budget on resume (S)
- [x] **FX-02** — Killshot fires on whatever is left in reach (S)
- [x] **FX-03** — Auto-aim skips enemies behind cover (M)
- [x] **FX-04** — Ground warnings stay above the floor (S)
- [x] **FX-05** — Killshot wind-up grants immunity (M)
