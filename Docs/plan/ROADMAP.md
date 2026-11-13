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

### Landscape

**Landscape** (owner, 2026-11-06; scope ruled 2026-11-09: the device orientation only, no menu or UI; design [GD §5.2, §7.2](../GameDesign.md)). The phone turns sideways; the arenas keep their square, south-to-north shape and the camera follows as before. LS-01's camera bound and LS-02/03's wide arenas came from a misread of "landscape" and were reverted the same day (owner, 2026-11-09).

- [x] LS-01 — Landscape orientation (its camera bound reverted: landscape is the device only) (M)
- [x] ~~LS-02 — The Jungle's arenas go wide~~ — reverted: landscape is the device only
- [x] ~~LS-03 — Descent and Courtyard go wide~~ — reverted: landscape is the device only

**For the UI rework** — listed only, never specced or fixed under LS. What will look wrong or break once the phone is sideways:

- Canvas scalers on a 1080 × 1920 portrait reference: `Hud`, `Pause`, `RunEnd`, `DebugOverlay` prefabs and `Menu.unity` (match 0.5) — scaled for a tall screen; `LevelUp`, `Sanctum`, `FirstActiveHint`, `Boot` are still 1920 × 1080.
- `HudLayout` (UI-08c bottom band, arc, roll, `RollRightDp`), `SkillArc`/`SkillArcButton`, the skill button and passives: derived for 9:20.
- The move zone (left 45 %, fixed stick bottom-left, GD §5.2) — wider in landscape; the arc sits above the roll and eats the short height.
- Top row: `HpBarView` width rule, `StatusRow`, `StageReadout` chip, pause, `XpBarView` (cutout on one rotation), `BossBarView`, `VeilrotMeterView` (right edge, vertical).
- `ThreatArrows`: works on any aspect; its `EdgeMargin` was tuned for portrait.
- Tree screen: `TreeViewPresenter` portrait branch (`IsPortrait`, `PortraitSideDp`), `TreeBlockView`, `TreeDetailCard`, `PathCardsView`; tests pin portrait (`Layout_FitsPortrait`, `Tree_Portrait_*`).
- Menus: `MenuPresenter`, `TabBar`, `PlaceList`, `HeroGrid`, `HeroSheetView`, `CharacterPage` (`PreviewShare`), `ClassSelectPresenter` (`Page_FitsPortrait`), `CharacterPreview` camera.
- Run overlays: `LevelUpPresenter`, `SanctumPresenter`, `PausePresenter`, `RunEndPresenter`, `SplashPresenter`, `FirstActiveHint`, `PointBadge`.
- Debug: `TestStartPanel` scales by a portrait `ReferenceHeight`; `DebugOverlay` IMGUI rects.
- `HudLayoutTests`, `SkillsPresenterTests` and the tree tests assert 9:20 layouts.
- Stage flow (owner, 2026-11-09; world side is SF below): a STAGE CLEAR banner on the last kill; a BOSS label on the arrival card (STAGE N is SF-05); a run progress strip; an off-screen edge arrow to the open gate.
- `HudPresenter`'s black stage fade (`_fade`): retire it once SF-02's `GateFlashView` covers the transition.

### Stage flow

**Stage flow** (owner, 2026-11-09; world side only, no menu or UI; design [GD §7.1](../GameDesign.md)). After a clear the way on must be obvious, and an arrival must read as a new stage. All cyan, never gold; readable at first look on a phone. Core decides every beat (slow motion, idle hint, walk-in, seal, spawn delay, arena choice); Unity renders. The UI pieces are listed under Landscape › *For the UI rework*. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. Playtestable from SF-01.

- [x] SF-01 — Clear guide: ~1 s slow-mo beat, tall gate beam, floor trail, idle pulse (M)
- [x] SF-02 — Entry: cyan-white flash, walk-in through the door opposite the gate, door shuts, a beat before waves (M)
- [x] SF-03 — The Grove: a third Jungle arena, so no stage repeats the last (M)
- [x] SF-04 — The gate is a glowing cyan portal; no entry door, no floor trail (owner correction, 2026-11-10) (M)
- [x] SF-05 — A STAGE N card on every arrival (owner, 2026-11-10) (S)

### Sunscar

**The Sunscar, place 2** (owner, 2026-11-09; design [GD §3 › The Sunscar](../GameDesign.md#the-sunscar-place-2)). A golden desert after the Jungle: 30 stages, its own enemies decided one at a time, weather visual only. Titles only; each spec is written and built on the owner's go, one at a time.

- [x] SS-00 — The Sunscar design pass, docs only (S)
- [x] SS-01 — The first Sunscar arena, walkable with the Ranger, no enemies (M)
- [x] SS-02 — The sandstorm: drifting sand that thins around the play space (S)
- [x] SS-03 — Quicksand patches, ground dressing with no rule (S)

**The sand scorpion, the Sunscar's first enemy** (owner, 2026-11-10; design [GD §8.4](../GameDesign.md)). The Frog's role in the desert: fodder from stage 1 whose pack fans out and closes from several sides, then a locked tail strike into a small warned strip. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-04a and SS-04b may swap; SS-04c needs both; SS-04d needs all three. Playtestable from SS-04d with DT-01's test start in the Sunscar.

- [x] SS-04a — The scorpion in core: the fan approach and a locked tail strike strip (M)
- [x] SS-04b — The model: scorpion.py, Rig_Scorpion, six clips, turnaround first (M)
- [x] SS-04c — The body: prefab, animator, stinger glow and the ground strip (M)
- [x] SS-04d — The scorpion in the Sunscar: data, roster from stage 1 (M)

**The jackal slinger, the Sunscar's second enemy** (owner, 2026-11-10; design [GD §8.5](../GameDesign.md)). The Pitcher's role in the desert: from stage 8 (moved from 2 by the owner, 2026-11-10, for the beetle) it holds 12 m and slings a stone that lands where the hero is heading, under a red-orange circle; rushed, it backs away slowly and cannot throw. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-05a and SS-05b may swap; SS-05b needs the owner's sculpt (prompt in the spec); SS-05c needs both; SS-05d needs all three and SS-06d. Playtestable from SS-05d with DT-01's test start at Sunscar stage 8.

- [x] SS-05a — The slinger in core: a led throw and a retreat that cannot throw (M)
- [x] SS-05b — The model: the jackal on Rig_Medium and a sling prop (M)
- [x] SS-05c — The body: prefab, animator, the whirling sling and a red-orange stone (M)
- [x] SS-05d — The slinger in the Sunscar: data, roster from stage 8 (M)

**The bombardier beetle, the Sunscar's stage-2 enemy** (owner, 2026-11-10; design [GD §8.6](../GameDesign.md)). The Pitcher's role, plainer than the jackal: from stage 2 it plants at 12 m, turns its rear to the hero at 45°/s and lobs one slow glob at where the hero stands, under a red-orange ring; circle it close and it drops the shot. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order, before SS-05d. SS-06a and SS-06b may swap; SS-06c needs both; SS-06d needs all three. Playtestable from SS-06d with DT-01's test start at Sunscar stage 2.

- [x] **SS-06a** — The beetle in core: a slow rear turn and a shot it can drop (M)
- [x] SS-06b — The owner's beetle sculpt on Rig_Beetle, with seven clips (M)
- [x] SS-06c — The body: prefab, animator, the glowing vent and a red-orange glob (M)
- [x] SS-06d — The beetle in the Sunscar: data, roster from stage 2 (M)

**The brood scorpion, the Sunscar's stage-4 enemy** (owner, 2026-11-10; design [GD §8.7](../GameDesign.md)). The Weaver's role, one generation: from stage 4 a fat, slow scorpion with a pale brood sac on its back fans in and strikes like the sand scorpion; killed, its hump swells and pulses for 0.5 s, then three small, fast, weak scorplings drop and fan in from three sides. Kill it at range so they cross open sand. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order, before SS-05b–d. SS-07a and SS-07b may swap; SS-07b needs the owner's sculpt (prompt in the spec, sent with the go); SS-07c needs both; SS-07d needs all three. SS-05d then adds the jackal to a roster that already holds the brood at 4 (its roster rows gain that entry; the code wins). Playtestable from SS-07d with DT-01's test start at Sunscar stage 4.

- [x] SS-07a — The brood in core: a swelling corpse that drops three scorplings (M)
- [x] SS-07b — The brood model on Rig_Scorpion with six clips (M)
- [x] SS-07c — The body: two prefabs and the swelling hump (M)
- [x] SS-07d — The brood in the Sunscar: data, roster from stage 4 (M)

**The sidewinder viper, the Sunscar's stage-6 enemy** (owner, 2026-11-11; design [GD §8.8](../GameDesign.md)). The Lunger's role in the desert: from stage 6 it slithers to 8 m, coils for 0.9 s while a red-orange strip 12 m long shows on the sand, and strikes down it; between strikes it slides 45° round the hero, alternating sides, so each lane comes from a new angle. A miss leaves it stretched out for 2 s: the punish window. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-08c needs SS-08a and SS-08b; SS-08d needs all three. The jackal's SS-05b–d stay queued: whichever of SS-08d and SS-05d lands second inserts its roster entry in stage order (viper 6 before jackal 8); the code wins. Playtestable from SS-08d with DT-01's test start at Sunscar stage 6.

- [x] **SS-08a** — The viper in core: a long recoil on a miss and a sidewind (M)
- [x] SS-08b — The model: the owner's viper on Rig_Viper, nine clips, turnaround first (M)
- [x] SS-08c — The body: prefab, animator, the coil and the lane strip (M)
- [x] SS-08d — The viper in the Sunscar: data, roster from stage 6 (M)

**The glassback beetle, the Sunscar's stage-9 enemy** (owner, 2026-11-11; design [GD §8.9](../GameDesign.md)). No Jungle role: it punishes firing without looking. A melee chaser that, when it is the hero's target, glints for 0.5 s and then shines for 2 s. While shining it takes no damage and sends a glass bolt back for each hero hit. Then it stands open for 1 s at double damage. Specs in [tasks/](tasks/), each built only on the owner's go, one at a time, in this order. SS-09b needs SS-09a. SS-09c uses the owner's sculpt (path in the spec) and may run beside SS-09a–b. SS-09d needs SS-09a–c, and SS-09e needs all four. Playtestable from SS-09e with DT-01's test start at Sunscar stage 9.

- [x] SS-09a — The glassback in core: glint, shine, open, on the hero's target (M)
- [x] SS-09b — The reflection: hero hits on a shining shell send glass bolts back (M)
- [x] SS-09c — The model: the owner's sculpt on Rig_Beetle, wing cases, eight clips (M)
- [x] SS-09d — The body: prefab, animator, the white shine and the glass bolt (M)
- [x] SS-09e — The glassback in the Sunscar: data, roster from stage 9 (M)

**The antlion, the Sunscar's stage-11 enemy** (owner, 2026-11-12; design [GD §8.10](../GameDesign.md)). The Revenant's role: it denies safe spots. From stage 11 it tunnels unseen but for a sand ripple, comes round behind the hero, locks a red-orange ring on where the hero stands for 0.8 s and bursts up; then it sits exposed for 2.5 s and digs back in. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-10b uses the owner's TRELLIS sculpt; SS-10c needs both; SS-10d needs all three. Playtestable from SS-10d with DT-01's test start at Sunscar stage 11.

- [x] SS-10a — The antlion in core: tunnel behind, a ring under the hero, a slow dig (M)
- [x] SS-10b — The model: the owner's antlion on Rig_Beetle, mandibles, seven clips (M)
- [x] SS-10c — The body: prefab, animator, the sand ripple and the eruption ring (M)
- [x] SS-10d — The antlion in the Sunscar: data, roster from stage 11 (M)

**The jackal priest, the Sunscar's stage-14 enemy** (owner, 2026-11-12; design [GD §8.11](../GameDesign.md)). The Choir's role: a priority target that never attacks. From stage 14 it hides behind the most hurt ally and mends it down a lime beam for up to 3 s; any hit on it breaks the beam and stops it mending for 2 s. Built after the antlion. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-11b uses the owner's TRELLIS sculpt (path in the spec) and may run beside SS-11a; SS-11c needs both; SS-11d needs all three and SS-10d. Playtestable from SS-11d with DT-01's test start at Sunscar stage 14.

- [x] SS-11a — The priest in core: a mend beam on the most hurt ally, broken by a hit (M)
- [x] SS-11b — The model: the jackal priest on Rig_Medium and a sun-disc staff (M)
- [x] SS-11c — The body: prefab, animator, the lime mend beam (M)
- [x] SS-11d — The priest in the Sunscar: data, roster from stage 14 (M)

**The spitting cobra, the Sunscar's stage-16 enemy** (owner, 2026-11-13; design [GD §8.12](../GameDesign.md)). The Spitter's role: it punishes standing still. From stage 16 it holds 8 m, slithers in to 4 m when its spit is ready, rears 0.7 s over a locked red-orange cone (60°, 5 m) and spits; a hit poisons the hero, up to three stacks that a roll clears. The hero's first Poison. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-12b uses the owner's TRELLIS sculpt (path sent with the go) and may run beside SS-12a; SS-12c needs both; SS-12d needs all three. Playtestable from SS-12d with DT-01's test start at Sunscar stage 16.

- [ ] SS-12a — The cobra in core: a locked spit cone and Poison on the hero (M)
- [ ] SS-12b — The model: the owner's cobra on Rig_Viper, hood bones, six clips (M)
- [ ] SS-12c — The body: prefab, animator, the cone and the violet Poison icon (M)
- [ ] SS-12d — The cobra in the Sunscar: data, roster from stage 16 (M)

**The storm caller, the Sunscar's stage-18 enemy** (owner, 2026-11-13; design [GD §8.13](../GameDesign.md)). No Jungle role: a hazard that chases. From stage 18 a hyena sorcerer holds 12 m, flees inside 6 m and channels for 2 s; any hit cancels it. A finished channel raises a tornado that drifts after the hero at half the Ranger's speed for 8 s, hurting and gently pulling only the hero inside a red-orange ring; at most two in the arena, and the caller's death collapses its own. Specs in [tasks/](tasks/); each built only on the owner's go, one at a time, in this order. SS-13a needs SS-12a (the hero's pulse door). SS-13b needs the owner's TRELLIS sculpt (prompt in the spec, path sent with the go) and may run beside SS-13a; SS-13c needs both; SS-13d needs all three and SS-12d. Playtestable from SS-13d with DT-01's test start at Sunscar stage 18.

- [ ] SS-13a — The storm caller in core: a breakable channel and a chasing tornado (M)
- [ ] SS-13b — The model: the hyena caller on Rig_Medium and a spiral staff (M)
- [ ] SS-13c — The body: prefab, animator, the sand spiral and the tornado (M)
- [ ] SS-13d — The storm caller in the Sunscar: data, roster from stage 18 (M)

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
