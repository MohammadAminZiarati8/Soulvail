# Soulvail — Roadmap

**The map.** Milestones → tasks, one line each. Where we are *right now* lives in [PROGRESS.md](PROGRESS.md). What each task is exactly lives in [tasks/](tasks/).

## How to read this

- One milestone at a time. Every milestone ends with something **playable on a phone** and a tag on `main`.
- A task is **one branch, one PR, 1–5 files.** Size S = 1–3 files, M = 4–5, L = must be split before starting. Size counts **new code files and substantial rewrites**; small additive edits to existing files (a new field on a spec, a registration, an event struct) are listed in the spec but don't count — they're trivial to review.
- Task IDs are `M<milestone>-<nn>`. Branch = lowercase ID + slug: `m0-03-domain-events`. PR title = the spec's H1.
- **A milestone's specs are written by its own first tasks** — `M<n>-00a…`, themed groups of 3–5 specs each, against the previous milestone's carry-forward ledger — before its `-01` starts. Later milestones are titles + one-line goals. (The earlier rule, "spec the next milestone when this one is ~75 % merged", missed twice; nothing in the protocol could check it.)
- A box is ticked **in the PR that closes the task**; it becomes true when that PR merges into `dev`. The PR number lives in the merge commit, not here.
- Splits keep the parent ID: `M0-07a`, `M0-07b`.

**Spec pointers:** GD = [GameDesign.md](../GameDesign.md) · CH = [Characters.md](../Characters.md) · CC = [CoreCombat.md](../CoreCombat.md) · AR = [Architecture.md](../Architecture.md) · ADR = [adr/](../adr/)

## Milestones

| | Milestone | Ends when | Tasks |
|---|---|---|---|
| **M0** | **Walking skeleton** | Floating stick → core motor → intent → capsule moves in a grey box **on the phone**, through VContainer scopes, with events/snapshot/intent plumbing real and tested | 21 |
| **M1** | **Combat feel** | Stat, Health/Aegis, targeting + reticle, tap-to-focus, Censer, Charge, Focus, chaser dummies. [CC §8](../CoreCombat.md) checklist passes on device | 21 |
| **M2** | **Stage loop** | Mode as data, threat budget, director, Husk/Spitter/Bloater, arenas, seal/gate, run persistence across app kill | 27 |
| **M3** | **Levelling and the tree** ✅ | XP, tree rules, offers, level-up screen, SkillRunner + auto-cast, effect primitives, first Oathbound nodes, health-bar treatment — **complete, accepted on Editor evidence, tagged `m3`** | 35 |
| **M4** | **First boss and run end** ✅ | Boss phases, Warden of Ash, death → Shard payout, profile persisted, a run-end screen. The UI work M3-15's acceptance surfaced was ruled out after the tag and stays [ledger row 1](#carry-forward-into-m5) — **now with a number under it** — **complete, accepted on Editor evidence, tagged `m4`** | 11 |
| **M5** | **Second class** ✅ | Gravecaller: projectile weapon + leading, Shroudstep, Wights, its tree, class select — **complete, accepted on Editor evidence, tagged `m5`** | 15 |
| **M6** | **Systems complete** ✅ | Sanctum shop, Veilrot + Pacts + Claiming, Ordeals, Emberwright, unlocks, localisation tables — **complete, accepted on Editor evidence; `m6` the owner's to tag** — [both bugs ruled to go first](#m6--systems-complete) are merged | 22 + 5 |
| **M7** | **Content pass** | Full V1 roster, Elites/affixes, Choirmother, all 81 nodes, both biomes' art, audio | 9 |
| **M8** | **Feel, perf, ship** | Game-feel checklist, options/accessibility, device tiering, thermal, per-class balance, store build | 6 |

---

## M0 — Walking skeleton

**Complete, tagged `m0` on Editor evidence** — 21 tasks, 153 EditMode + 3 PlayMode green. The goal, the task table and its status notes are in [archive/ROADMAP-M0.md](archive/ROADMAP-M0.md); the log is [archive/PROGRESS-M0.md](archive/PROGRESS-M0.md).

## M1 — Combat feel

**Complete, tagged `m1` on Editor evidence** — 21 tasks, 452 EditMode + 3 PlayMode green, the fight playtested and called good in the Editor. Archived in [archive/ROADMAP-M1.md](archive/ROADMAP-M1.md); the log is [archive/PROGRESS-M1.md](archive/PROGRESS-M1.md).

## M2 — Stage loop

**Complete, tagged `m2` on Editor evidence** — 27 tasks, 1 077 EditMode green, the descent playtested and called good in the Editor. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M2.md](archive/ROADMAP-M2.md); the log is [archive/PROGRESS-M2.md](archive/PROGRESS-M2.md).

### Carry-forward into M2

**Closed at M2-15: all fourteen rows struck, none carried into M3.** The rows and the closing notes are in [the archive](archive/ROADMAP-M2.md#carry-forward-into-m2). This heading stays so links into it keep resolving.

## M3 — Levelling and the tree

**Complete, tagged `m3` on Editor evidence** — 35 tasks, 1 981 EditMode green, accepted on a verdict rather than a number: the mechanisms work and the UI cannot be read, which is why [M4's ledger](#carry-forward-into-m4) opens with a readout. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M3.md](archive/ROADMAP-M3.md); the log is [archive/PROGRESS-M3.md](archive/PROGRESS-M3.md).

### Carry-forward into M3

**Closed at M3-15: seven of nine struck, three carried, none dropped.** What was carried is [M4 rows 2 and 3](#carry-forward-into-m4). The rows, M3-15's closing note and the exponent ruling M4 row 2 cites are in [the archive](archive/ROADMAP-M3.md#carry-forward-into-m3). This heading stays so links into it keep resolving.

## M4 — First boss and run end

**Complete, tagged `m4` on Editor evidence** — 11 tasks, 2 197 EditMode + 16 PlayMode green, and the first acceptance in this project to close a readability row on a **number**: seven of the eight text elements on the two screens clear Android's 12 sp floor and the eighth, S1–S4's labels, sits at **7.9 dp**. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M4.md](archive/ROADMAP-M4.md); the log is [archive/PROGRESS-M4.md](archive/PROGRESS-M4.md).

### Carry-forward into M4

**Closed at M4-07: seven rows, two discharged by their owners, one distributed, four carried — none dropped.** What was carried is [M5 rows 1–4](#carry-forward-into-m5), and what was distributed is [M5 row 5](#carry-forward-into-m5) plus one parking-lot line. The rows and M4-07's closing table are in [the archive](archive/ROADMAP-M4.md#carry-forward-into-m4). This heading stays so links into it keep resolving.

## M5 — Second class

**Complete — 15 tasks, 2 521 EditMode + 21 PlayMode green, tagged `m5` on Editor evidence.** The game stopped being one character: a card per class at the door, a Gravecaller behind it with a led projectile, a corpse the arena walks at and an army raised from a quarter of the dead, and CH §5.4's half-tree moment where a run borrows one branch of the other class for good. **And it is the first acceptance this project measured rather than argued** — [M5-08](tasks/M5-08-acceptance-and-tag.md) instrumented a build, two played runs reached stages 16 and 19 killing three bosses each, and that log discharged three ledger rows no amount of reading had moved. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M5.md](archive/ROADMAP-M5.md); the log is [archive/PROGRESS-M5.md](archive/PROGRESS-M5.md). [M5-08a](tasks/M5-08a-splash-offers-what-install-refuses.md), created *by* the acceptance under its rule 8, merged before the tag; its entry opens [M6's log](archive/PROGRESS-M6.md).

### Carry-forward into M5

**Closed at M5-08: nine rows, six discharged by their owners, three closed by the playtest — none dropped.** What carries is [M6 rows 1–4](#carry-forward-into-m6); rows 2, 7 and 8, which three acceptances in a row had owned, were answered by one instrumented session. The rows and M5-08's closing table are in [the archive](archive/ROADMAP-M5.md#carry-forward-into-m5). This heading stays so links into it keep resolving.

## M6 — Systems complete

**Complete — 22 tasks, 3 140 EditMode + 26 PlayMode green, accepted on Editor evidence; `m6` is the owner's to tag** — [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md) and [M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md), the two bugs ruled to go before it, are merged. The systems a run is made of: an Essence economy and a Sanctum that sells four things, a corruption meter with four thresholds and a hundred-second death sentence, Pacts in the offer, Ordeals from stage 25, a third class, Shard-bought unlocks, and every string in a table with a pseudo-locale to prove it. **The acceptance was played past stage 25 for the first time — to 39 — and found five defects no reading had.** The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M6.md](archive/ROADMAP-M6.md); the log is [archive/PROGRESS-M6.md](archive/PROGRESS-M6.md). **Five tasks were created by the acceptance under its rule 8**, [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md) to [M6-11e](tasks/M6-11e-two-rows-that-assert-a-premise.md); **a to d are merged, e is open.**

### Carry-forward into M6

**Closed at M6-11: eight rows — four discharged by their owners, one measurement discharged with its tuning carried, one ruled and handed to M6-11e, two carried whole — none dropped.** What carries is [M7 rows 1–3 and 8](#carry-forward-into-m7). The rows and M6-11's closing table are in [the archive](archive/ROADMAP-M6.md#carry-forward-into-m6). This heading stays so links into it keep resolving.

## M7 — Content pass *(titles only)*

| ID | Task |
|---|---|
| M7-00a | Specs for M7's first group, written against [its ledger](#carry-forward-into-m7) — M6's rule, one milestone on |
| M7-01 | Lunger, Weaver, Warden (core + views) |
| M7-02 | Elites + affixes |
| M7-03 | Choirmother |
| M7-04 | All 81 nodes |
| M7-05 | Ashen Reach: 8–12 arenas + art |
| M7-06 | Drowned Choir: 8–12 arenas + art |
| M7-07 | Audio layers, telegraph cues, boss music |
| M7-08 | M7 acceptance, tag `m7` |

### Carry-forward into M7

**What M7's specs must absorb.** Opened at [M6-11](tasks/M6-11-acceptance-and-tag.md) on [M2's terms](#carry-forward-into-m2), unchanged through six milestones: every row names the task that must deal with it, and **a row leaves only when its owner's *As built* says it is answered.** Three rows carry from [M6's table](#carry-forward-into-m6), five are the tasks M6-11 created under its rule 8, and three are what its instruments found. **M4-07 rule 11 holds** — every row names an unmerged owner or says it has none and why — **and M5-08's rule applies at the opening**: a row that wants a number names the instrument that will take it. M6-11's instrument is described in [its *As built*](tasks/M6-11-acceptance-and-tag.md#as-built); it was reverted, so a row naming it means *build that probe again*.

| # | Finding | Owner | Cost of leaving it |
|---|---|---|---|
| 1 | **The device debt, carried whole from [M6 row 1](archive/ROADMAP-M6.md#carry-forward-into-m6), six tags deep.** Nothing has run outside the Editor, and `Screen.dpi` reads 120 here against a phone's 400 ([Traps §9](../Traps.md)). Multi-touch is still the one row that risks a *feature*, kill-from-recents the one *correctness* row. **M6-11 adds four Editor numbers a phone must re-take:** bodies peaked at **25** under Swarm at stage 37 against GD §11.3's 28; frame time held **p95 ≤ 17.8 ms at 60 fps** to stage 39 on this PC; every screen that opens costs **one ~50 ms frame**; and the pseudo-locale walk found **no plain English and nothing clipped** — at 120 dpi. The rest of the list is M6 row 1's, in the archive. | **No owner, by ruling**: the instrument is a phone. The first hardware session; **M8-xx** if none arrives sooner | **High and compounding.** Every milestone adds rows and none retire. |
| 2 | **Per-class balance against GD §12.4 and §12.5 — the measurement is discharged, the tuning is owed.** M6-11's instrument A took all three curves ([table](tasks/M6-11-acceptance-and-tag.md#as-built)): the Oathbound **4 flat** over 4–16 (M5-08); the Gravecaller **4 → 8** by 16, out of band from 9, with M6-07c's ×1.15 start now holding stages 1–4 at 4; the Emberwright **under the band at 2 hits from stage 1 to 9**, then **3–4 from 10 to 39** with a full tree — its *"3 cold"* is arithmetic nobody lives, because the blast warms Kindling within three orbs. **Kindling at full ran 2 %, 36 % and 0 % of combat** over three runs: it tracks being hit. **The Warden's fight lengthens with depth** — 89, 99, 102, 105, 113 and ~126 s at stages 5–35 — and leaves GD §9.1's 75–120 near stage 33. **A stage-5 Warden hit took 36 % of an Emberwright's maximum**, one point over §12.4's one-shot rule. **No natural run has passed stage 19**, so §12.5's 35–50 horizon is still unmeasured, and every early-levelling number carries [row 6](#carry-forward-into-m7)'s eight extra Husks. | **[M8-05](#m8--feel-perf-ship-titles-only)**, *"balance pass per class against the death horizon"*; the **instrument** is M6-11's probe, rebuilt | **Medium now, high at M7-04**, which authors 81 nodes against these curves. |
| 3 | **The UI redesign — every taken node shown, Active or Passive marked, Auto marked by something orbiting it — carried unchanged from [M6 row 3](archive/ROADMAP-M6.md#carry-forward-into-m6).** Sequencing is still **icons → readout → rotation**. M6 added to what it will have to draw rather than moving it: four priced Sanctum rows that can refuse, a violet meter with a Claiming mark, a Pact card, three class cards with a lock, a fourth `NodeState`, and a borrowed branch the tree screen still cannot show. | **None, by the owner's standing ruling.** M7-04 brings the nodes and M7-05/06 the art; **M8-01** is the earliest honest owner | **Medium and static** until M7-04 authors 81 nodes the screen cannot draw. |
| 4 | **Continue resumes the run the Menu read at boot, and its opening write destroys the real save.** `SavedRun` is set once by `BootFlow` and by nothing else, so within one app session a quit-and-Continue restores a stale run — or a dead one. Found by losing a stage-30 run to it. **DISCHARGED at [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md#as-built)**, before `m6`: `SaveWriter` mirrors every snapshot into `SavedRun` and clears it on every death, each before the disk operation is queued, so Continue offers the run last written — in one session as across a relaunch. Red-checked: without the mirror, exactly the five new rows fail, one of them with *"Continue resumed the run boot read, not the one just played."* A failed write leaves memory newer than disk, which is rule 3's stated cost. | **[M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md)**, before `m6` | **High**: silent data loss on the ordinary in-app path. |
| 5 | **A resume keeps the meter and drops the Claiming.** A Claimed run whose meter fell below 100 — a paid cast, a Cleanse — comes back unclaimed, so quitting is a way out of GD §10.3's death sentence. Witnessed at stage 36. **DISCHARGED at [M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md#as-built)**, before `m6`, as a v4 re-cut rather than a v5: `RunEconomy.Claimed` is saved beside the meter, and `Veilrot.Restore` latches on the flag *or* on 100, so a file written before the flag still restores as M6-04 rule 9 said. Red-checked: with the restore ignoring the flag exactly the three latch rows fail, one with *"A quit is not a way out of GD §10.3's hundred seconds"*; with the recorder and the mirror dropping it, exactly five. `ClaimedFor` still restarts at zero, which is rule 9's stated cost. | **[M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md)**, before `m6`: M6-01b's re-cut licence expires at the tag, after which the same fix is a v5 | Medium, and cheaper this week than next. |
| 6 | **Stage 1 carries eight M1 dummy Husks**, dressed by `Run.unity` at t = 0 — outside the budget and inside the arrival beat, on every run since M2. **DISCHARGED at [M6-11c](tasks/M6-11c-stage-one-without-the-m1-dummies.md#as-built)**: `_dummyPositions` is empty and `_dummySpec` stays, so the pool still prewarms `DeviceEnemyCap + 1`. Red-checked against the old scene: *"Expected: 0 But was: 8"*. Witnessed in Play as **0 enemies from the scene's first frame to t + 2.83 s**, arrival and wave 1's telegraph. Stage 1 is ten Husks now, and row 2's early-XP figures were taken with eighteen. | **[M6-11c](tasks/M6-11c-stage-one-without-the-m1-dummies.md)** | Low, but it skews every early-levelling number [row 2](#carry-forward-into-m7) reads. |
| 7 | **The Sanctum sells a Reroll a finished tree can never spend** — five bought for 775 Essence at stage 37. **DISCHARGED at [M6-11d](tasks/M6-11d-no-reroll-for-a-finished-tree.md#as-built)**: `CanBuy(Reroll)` asks Banish's question — anything left in the pool — after the price, and the dead row says *"Nothing left to offer"*; a charge already banked stays banked. Red-checked: with `Reroll => true` back, exactly the four refusal rows fail. An orphaned Upgrade still counts as left — the [parking lot](#parking-lot)'s line. | **[M6-11d](tasks/M6-11d-no-reroll-for-a-finished-tree.md)** | Low. |
| 8 | **Two rows assert a premise rather than a behaviour**: [M6 row 4](archive/ROADMAP-M6.md#carry-forward-into-m6)'s wedge, ruled at M6-11 to mean *"the seam is synchronous"*, and known issue 7's Editor clock. | **[M6-11e](tasks/M6-11e-two-rows-that-assert-a-premise.md)** | Low but corrosive: a flaking suite trains everyone to re-run rather than read. |
| 9 | **The Sanctum is not a decision in play.** GD §10 is the section the design says not to cut, and in two natural runs the owner spent **0 of 448 Essence**, declining Heal at 7 / 85 HP **by choice**. From about stage 13 a full tree leaves three of four rows refused, and Essence reaches **2 400 unspent by stage 30**. The shop has nothing worth buying early and nothing to sell late. | **The owner's ruling first** — what a finished tree's Essence buys is a question about the game — **then M8-05** for prices against income; the **instrument** is Essence earned and spent per run, by service | **Medium**: GD §13.3 is one of M6's pillars and it plays as zero. |
| 10 | **A Pact is taken when it is seen, and it is almost never seen.** One Pact card in **62 offers** across five runs, taken the one time. **4 of 36 nodes carry a Pact and the Emberwright's twelve carry none**, and a finished tree rolls no offer at all — so GD §13.2's *"continuous temptation"* is about 2 % of level-ups, and zero after stage ~13. CH §3.1's new ¾ budget also asks for Keen Censer and Zealotry to be re-read. | **[M7-04](#m7--content-pass-titles-only)**, which authors all 81 nodes — coverage is authoring; the **instrument** is offers rolled against Pacts shown | **Medium**: the signature system is invisible. |
| 11 | **At depth the waves collapse into Bloaters.** From stage ~21 waves 4–5, and from ~26 waves 3–5, are **Bloaters only**, 20–28 each, with Husks and Spitters confined to waves 1–2. Echo-as-*replace* therefore changes nothing from stage 26 (waves 3, 4 and 5 are identical), and Swarm barely moves the count because the cap already binds. The cause is not diagnosed here. | **[M7-01](#m7--content-pass-titles-only)**, which adds three archetypes the composer can buy, and **[M7-02](#m7--content-pass-titles-only)**, which spends `WavePlan.UnspentThreat`; the **instrument** is per-wave composition at stages 20–35 | **Medium at M7**: GD §8's variety of pressure is gone past stage 25. |

## M8 — Feel, perf, ship *(titles only)*

| ID | Task |
|---|---|
| M8-01 | Game-feel checklist: hit-stop, shake slider, low-HP vignette (GD §16.3) |
| M8-02 | Options + accessibility: control modes, mirror, sliders, colourblind palettes (GD §18) |
| M8-03 | Device tiering + device-independence rule (GD §11) |
| M8-04 | Frame-rate setting, thermal validation |
| M8-05 | Balance pass per class against the death horizon (GD §12.5) |
| M8-06 | Store build, signing, V1 tag |

---

## Parking lot

Unscheduled. **One item, one line: what it is and what promotes it.** History lives in the archive; an item that acquires an owning task becomes a ledger row.

- ~~**`PlayerAnimatorView` has no tests.**~~ Promoted at M2-15 to M3 ledger row 5, closed by M3-11c with fourteen rows and no behaviour change. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- **There are two player cyans in the build, and only one of them is GD §16.4's.** `HpBarView`,
  `ThreatArrows`, `VFX_ConsecrateZone` and `VFX_Bulwark` are `#22D3EE` — now `Palette.Player` — while
  `ReticleView._colour`, `FocusGlowView._colour` and `PlayerView._swingColour` are `#4CE6FF`, dressed
  that way on `Player.prefab` and `Reticle.prefab` since M0/M1. Found at **M3-13a** and deliberately
  left: unifying them recolours the reticle, the focus glow and the swing arc on every frame, which is
  a **visible** change rather than the relocation that task was, and which cyan the player's own
  world-space views should be is a look decision rather than an implementation one.
  `Palette_IsNotTheOnlyCyanInTheBuild` pins all three so neither value can drift further in silence.
  Promoted by the owner's ruling, or by **M7**'s art pass, which replaces all three surfaces anyway.
- **A hand-edited save can hang a frame, and no guard in the project is placed to stop it. — *Its named promoter fired and nobody caught it*, found at [M3-15](tasks/M3-15-acceptance-and-tag.md):** this line says *“promoted by M3-14b, which is already the task that refuses content the game cannot survive”*, **M3-14b shipped and did not take it**, and nothing in the protocol noticed. Deliberately **not** made an M4 ledger row — it still has no owner and capping the value is still a ruling about what such a save *becomes*, which is the exact condition for staying here. What changed is that the trigger is now known to have been missed rather than still pending. 
  `LevelTracker`'s `while (Xp >= XpToNext)` terminates for any finite XP, but `"xp":1e38` in
  `run.json` is a legal `RunSnapshot` and settles in ~10^15 iterations; `Grant` has the identical
  exposure through a content-authored `EnemySpec.XpValue`. Found at M3-01b and deliberately not
  fixed there: a cap means ruling what a save above it *becomes*, which is a decision rather than an
  implementation detail. Promoted by the first ruling on it, or by **M3-14b**, which is already the
  task that refuses content the game cannot survive.
- **`Core/Content` stopped being a leaf at M3-02a, and `Content ↔ Combat` is now a namespace cycle.**
  Before this task `Core/Content/` held zero `using Soulvail.*`; `TriggerSpec` now names
  `Core.Combat` (`CombatBlackboard`) and `SkillSpec` names `Core.Effects` (`IEffect`), while
  `Core/Combat` has depended on `Core/Content` since M0-07. It compiles and nothing is wrong —
  AR §5's table has no dependency column and §12 promises no acyclicity, and both types are content
  by AR §10.1 — but it would block AR §5's own escape hatch, *"split into separate assemblies only
  if compile times demand it."* Nothing owns it; promoted by the first task that wants that split,
  which would have to move `CombatBlackboard` or invert the `IsMet` call.
- ~~**CH §5's tree table says 8 nodes a branch and 27 a class, and 3 × 8 is 24.**~~ Corrected at M3-15 — two errors, the table row and the diagram, both now M3-02a rule 7's nine a branch. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- ~~**CH §5.2's curve cannot hit its own table past stage 10.**~~ Settled at M3-15 the other way round: the doc's stage-20 row moved and `Descent.asset`'s 1.4 stayed, because 1.5 or 1.6 would push stage-15 TTK to five hits. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- **A stage editor — *“maybe later we should have a stage creator”*.** The owner's words at M4-00a, alongside the ruling that **which stages hold which enemies is authored on the mode**. `ModeSpec` already carries an enemy roster with `_introducedAtStage`, and M4-01b adds a boss roster beside it, so the *data* is already per-stage-ish; what does not exist is a way to author a **specific** stage rather than a rule that generates them. Promoted when a designer wants stage 7 to be different from stage 6 for a reason no curve expresses — most likely **M7**, when the roster is full and the biomes differ. **The Ordeal half of this line is answered and struck:** it read *"or M6-04 if Ordeals need per-stage authoring"*, and [M6-06a](tasks/M6-06a-what-an-ordeal-is.md) turns GD §13.4's *"each new biome loop"* into a **period** on the mode — `firstStage` 25, `everyNStages` 10 — with the pool a mode's list, so nothing about Ordeals wants a specific stage authored. **Not M4's**: one boss on a rule is what M4 needs, and building an editor for one entry is the kind of tooling that outlives its content.
- **A tree editor for `SkillTreeDefinition`.** Three nested arrays in the default Inspector is enough
  for twelve nodes. Promoted when **M7-04** authors eighty-one by hand and it hurts.
- **`ProjectileViews` has no answer to the silent boundary sweep, and `MinionViews` now does.** Found
  at [M5-05a](tasks/M5-05a-wight-views-and-concurrency.md) while discharging [row 9](#carry-forward-into-m5).
  `StageFlow.Advance` calls `ProjectileSystem.Clear`, which publishes nothing and resets ids to 1, so
  a bolt in the air at a crossing leaves a body standing in the next arena for ever and the next
  stage's shot 1 overwrites its census entry — the leak the minion census now closes by tearing down
  on `StageArrived`. **Inert today by arithmetic rather than by design**: a bolt lives well under a
  second against a boundary's two of gate and arrival, so the window needs a slow shot to open.
  **M5-05b fired as promoter and the owner ruled: close the decoy's half, leave this one open.**
  `DecoyViews` takes the fourth subscription — a 3 s corpse against a 2 s crossing is reachable, so
  it is the second census to need it — and `ProjectileViews` is still the only one without an
  answer, now conspicuously rather than by omission. **Two of three closed makes the third a
  one-line change with two worked precedents**, which is the cheapest this ever gets; the
  alternative fix — core announcing the sweep — is one line in three `Clear` methods and a ruling
  about whether a farewell is owed at a boundary but not at a run's end. Promoted by the first task
  that gives a bolt a reason to live longer than a second, or by any task already in
  `ProjectileViews`.
- **A body teleported by `Bind` can be snapped back by its own first `Move`.** Found at
  [M5-05a](tasks/M5-05a-wight-views-and-concurrency.md), in the fixture rather than in play:
  `autoSyncTransforms` is 0, so after `transform.position = …` the `CharacterController` PhysX holds
  is still parked where the pool left the body, and `Move` can resolve from *there*.
  `Ticker_MinionsMoveBeforeTheFlush` failed about one run in three with the Wight a kilometre from
  where it was raised until `RaiseTheWight` called `Physics.SyncTransforms()`. **`EnemyView` has the
  identical shape and has since M1-19**, and `RunTicker` applies the first move **above** its own
  flush, so the window is open in the game — an enemy's pooled body parks inside the arena, so the
  snap would be small and read as spawn jitter rather than as a bug, which is why four milestones of
  playtests have not named it. No owner: the candidate fixes are a sync inside `Bind` (a flush per
  spawn, on the frame a wave lands) and moving the flush above the bodies step (which is M2-15a
  inverted), and choosing between them is a ruling. Promoted by the first playtest that reports a
  body appearing somewhere it was not raised.
- **CC §6.2's drag-to-reorder for the four manual slots.** Ruled out of V1 at M3-00c, on M3-07a's own
  terms — *"the mechanic lands in M3-09 or not at all in V1"*. It costs one port member
  (`SetSkillSlot(skillId, slot)`) and a drag handler, and **no format bump**, because
  [M3-07b](tasks/M3-07b-save-format-v3.md) rule 1 saves slot *positions* rather than a set. Promoted
  by a playtest that says the lowest-free-slot rule puts the wrong skill under the thumb.
- **Icons for the twelve nodes — promoted at [M3-15](tasks/M3-15-acceptance-and-tag.md), by the playtest this line named as one of its two triggers, and it arrived carrying more than icons.** Now [M5 ledger row 1](#carry-forward-into-m5), measured at M4-07. The owner's acceptance verdict was *too cramped to read*, and the redesign asked for on the back of it — **every taken node shown, Active or Passive, with an Auto skill marked by something orbiting it** — turns this from an art nicety into a **blocking dependency**: the auto-cast row admits Actives only today, and putting twelve nodes into 24 dp cells that hold neither an icon nor a word would draw **twelve identical dots**. Sequencing is **icons → readout → rotation**. The original line:  [M3-10b](tasks/M3-10b-progression-hud.md) rule 8's 24 dp auto-cast
  cells can hold neither a `LocKey` nor a word, so which skill a cell is showing is not communicated
  at all — the one part of [ledger row 9](#carry-forward-into-m3) that M6-10's localiser cannot fix.
  [M3-10a](tasks/M3-10a-skill-slot-buttons.md) rule 9 has the same gap on the slot buttons, where
  position at least distinguishes them. Promoted by **M7**'s art pass, or by M3-15 if a playtest
  says the row is unreadable without them. **Narrowed at M3-00d: the slot buttons are no longer part
  of it** — [M3-14a](tasks/M3-14a-localizer-and-english-table.md) puts a *word* on a 60 dp circle,
  which is what M3-10a rule 9 wanted and could not have. What is left is the 24 dp cells alone, and
  they are the one reader localisation cannot reach. **M3-13 is no longer named here**: it was, before
  it split into a palette and a health-bar task, neither of which can hold an icon. **The cells exist
  as of M3-10b**, so this is no longer a prediction: twelve of them are authored on `Hud.prefab`, each
  a kind-tinted body with a radial sweep over it, and in every run this build can play they are all
  deactivated because nothing owns an Active until M3-12. The first build in which anyone sees the gap
  is M3-12's.
- **A counting `IRandom` fake in `Tests/Core/Fakes/`.** `SpawnDirectorTests` has a private one and
  **`OfferGeneratorTests` is now the second, written at M3-04** — same shape, renamed reads
  (`OffersDraws` / `OtherDraws` rather than `SpawnDraws` / `OtherDraws`), which is the only thing
  the two copies differ in and the thing a shared fake would have to generalise. **Promoted by the
  third.** `OfferGeneratorTests` also holds a private **LCG** `IRandomStream`, for the two rows that
  need ten thousand different answers rather than a script: `SeededRandom` lives in
  `Soulvail.Game` and `Soulvail.Tests.Core` does not reference it. That is a **first** copy, not a
  second — it is promoted on its own count.
- **`EffectRegistry.CanApply` answers only *"is there a handler for this type"*, and the general
  version — `IEffectHandler<T>` gaining a `CanApply(TEffect)` member — was weighed at **M3-03** and
  deliberately not built.** The owner's ruling, with the trace behind it: for `ModifyStat`, the one
  primitive that exists, every other door is already doubled (`ModifyStat`'s constructor and
  `Modifier`'s both refuse the kind, non-finite values and a null source), the only residual is
  `PlayerStats.Resolve`'s address throw, and **`ModifyStatDefinition.ToEffect` is the only non-test
  constructor of a `ModifyStat` in the project** — which is exactly where M3-02b put
  `Enum.IsDefined`. So nothing reachable is open. Widening the interface now would duplicate that
  check with *worse* diagnostics (a node id, where the authoring door names the asset file and the
  field), and most implementations would be `=> true` — a rubber-stamp member that makes
  `SkillTree`'s rule-4 promise look structural while still resting on each author. **Promoted by the
  first primitive whose applicability is a *run-scoped* question** — one no authoring check could
  answer, because the answer depends on the run rather than on the asset (`SpawnZone`, or anything
  gated on the class) — which is also the first task with a second implementer to write the member
  against. Until then a new primitive owes its own door, and `SkillTree`'s remarks are where that
  rule is written down.
- **`EnemyBlackboard`'s four player fields lie while a corpse stands, and M5-03 made them do it
  rather than renaming them.** `PlayerPosition`, `DistanceToPlayer`, `DirectionToPlayer` and
  `PathDirectionToPlayer` mean *"where this enemy's quarry is"* for as long as a Shroudstep's corpse
  stands ([M5-03](tasks/M5-03-shroudstep-and-corpse-decoy.md) rule 2), which arrives one milestone
  before the blackboard's own remarks expect it — they say the sketch's `TargetId` *"returns with the
  first enemy that chooses among targets — the Choir, M7-01."* **Deliberately not renamed at M5-03:**
  it is roughly forty reader sites across four behaviours and their fixtures, it is a rename rather
  than a feature, and the honest version needs the `TargetId` that **M7-01** has to build anyway.
  **What M5-03 added instead is a fifth field, `QuarryIsADecoy`**, written unconditionally by the one
  site that writes the other four — and it has exactly one reader, `ChaserBehaviour.EnterStrike`,
  because a melee strike is the only way an enemy hurts the player that does *not* already resolve
  against the real `RunState.PlayerPosition`. **That reader is what the rename would delete**: with a
  target id, "am I in reach of my quarry" and "did I hit the player" stop being the same question.
  Promoted by M7-01, or by the second mechanic that redirects an enemy.
- **`handslot.l` / `handslot.r` are empty**, so the Knight swings a fist. The 31 props in
  `ThirdParty/KayKit/Adventurers/Props` are built to parent there. Promoted when the weapon-ownership
  question (class property vs swappable) is settled, because the answer decides who owns the socket.
- **The KayKit skeletons are the enemy roster, and M2-06 decided not to import them yet:**
  `Skeleton_Minion` → Husk, `Skeleton_Warrior` → Elite, `Skeleton_Mage` → Spitter, `Skeleton_Rogue`
  → Lunger, all on `Rig_Medium` so all 139 clips already play on them. **The pack has no Bloater**,
  and three bodies means a prefab, a controller and a `ViewPool` each — an M2-art-sized task.
  [M2-06](tasks/M2-06-enemy-authoring.md) rule 9 buys legibility with a per-archetype tint and
  scale on the one shared body instead, which is what GD §11.3 asks for anyway. Promoted when the
  owner brings enemy art in, the way M2-art was brought in. `Rig_Medium_Special`'s
  `Skeletons_Awaken_Floor` is a diegetic spawn telegraph that would replace
  [M2-12b](tasks/M2-12b-telegraph-rings.md)'s ring decal.
- **Arenas 3 through 12.** [M2-11a](tasks/M2-11a-arena-contract-and-pool.md) rule 11 ships two —
  the contract's proof, not its content — against GD §7.2's target of 8–12 per biome. Promoted by
  M7-05/06's art pass, or the day a playtest says two rooms is where a run starts feeling repetitive.
- **`EnemyRegistry` could prefer a free agent whose behaviour already matches the requested kind**,
  which would make [M2-07b](tasks/M2-07b-spitter-ai.md) rule 4's one-object-per-changed-rental churn
  rare in a mixed arena. Promoted by a profile that says so, not by a hunch — it is a spawn-path
  allocation, and AR §14's ban is about the frame path.
- ~~**The class speed band contradicts the shipped Oathbound.**~~ Done 2026-09-20; closed at [M5-02](tasks/M5-02-gravecaller-and-bone-bolt.md), whose rule 1 scaled the band by the retune's own ×0.5556 to **3.0 / 3.1 / 3.4 m/s**. [GD §6.1](../GameDesign.md)'s row, [Characters.md §3](../Characters.md)'s table and [CoreCombat.md §2.5](../CoreCombat.md)'s flagging paragraph were all three edited in that PR, which is the condition this line set for leaving.
- ~~**The asset-pinning rows assert number pairs where the invariant is a *ratio*.**~~ Done 2026-09-20; closed at [M5-02](tasks/M5-02-gravecaller-and-bone-bolt.md). `GravecallerTests.Speed_EveryClassOutrunsEveryEnemy` walks both shipped characters against every shipped enemy asset and asserts `speed / moveSpeed > 1` rather than a pinned pair. **The old pinned rows in `EnemyDefinitionTests` were left alone**: they are a different claim — that an asset carries the number it was authored with — and deleting them would trade a row that is occasionally noisy for no row at all.
- ~~**`PROGRESS.md` is 228 KB with an empty Log, and the Log is no longer what grows it.**~~ Done 2026-09-20; closed at M4-07. [History](archive/ROADMAP-M4.md#parking-lot-items-closed-in-m4).
- **`ShardPayout.PerStage` and `PerBoss` are `const`s in core, and there is no asset a payout belongs
  on. — *The promoter fired at M6-00a and the answer is no, with the count behind it.*** Added
  knowingly at [M4-05a](tasks/M4-05a-shard-payout.md) rule 7, and **struck rather than
  carried** when M4-07 distributed [M4 ledger row 6](#carry-forward-into-m4): a payout is not a mode, a
  character or an enemy, so inventing an asset for it would be M6's Sanctum arriving early and
  unspecified. GD §14.1's `10` and `50` are the one place ADR-0006 is deliberately not honoured, and
  the doc now says so. This line said *"promoted by **M6-02**, the first task that gives a Shard
  somewhere to be spent and therefore the first with an asset the two numbers could live on"* — and
  the task that actually opens `ModeDefinition` for an economy block is
  [M6-01a](tasks/M6-01a-essence-wallet-and-drops.md), one earlier, which is where the question was
  asked. **`new ModeSpec(...)` has 63 call sites across 44 files**, so an `EssenceSpec`-shaped
  optional-and-last argument defaults to **zero** — which is harmless for a number nothing asserts
  yet and destructive for two that every `ShardPayoutTests` row pins. The two ways out are
  re-authoring 63 fixtures for no behaviour change, or an `EssenceSpec.Default` in code, which is
  the second home for a number [M5-06b](tasks/M5-06b-gravecaller-tree-v1.md) rule 10 refused when it
  deleted the Overflow consts rather than defaulting them. Re-aimed at **M8-05**, the first task
  that wants to *retune* the payout and would therefore pay the cost for a reason.
- **GD §10.2's 50-Veilrot threshold summons an enemy V1 does not have.** Found at M6-00a while
  speccing [M6-04](tasks/M6-04-veilrot-thresholds-and-the-claiming.md): the row reads *"a Revenant
  stalks you each stage, regardless of depth"*, and the Revenant is GD §8.1's eighth archetype,
  introduced at stage 17 by GD §8.2 and **placed in V2 by GD §19** — *"Remaining 2 biomes + Choir and
  Revenant"*. M7-01 builds the Lunger, the Weaver and the Warden and stops. So M6-04 ships the
  threshold **crossed, published and otherwise silent**, and substituting a shipped archetype is
  refused on GD §8.1's own rule that two enemies pressuring the player identically means one is
  redundant: a Husk at 4 threat following a 50-Veilrot player is a free kill, which would make the
  threshold a **reward**. The ambient half is M7-07's audio pass. Promoted by **M7-01**, the task
  that builds archetypes — which also inherits the second half of this, that §10.2's *"regardless of
  depth"* is a deliberate override of §8.2's schedule and needs to be read as one.
- ~~**`PaletteTests.Palette_HasTheColoursNobodyReadsYet` has been false since M4-06, and it is
  green.**~~ **Promoted at M6-00b to [ledger row 8](#carry-forward-into-m6), because it acquired two
  owners** — M6-03a narrows it into a sweep and M6-03b retires it — which is this section's own
  condition for leaving. The account, including why the fix is the sweep rather than a tenth entry
  in a hand-kept list, is on the row.
- ~~**GD §13.2's Pact table and its own worked examples describe two different mechanics.**~~ **Ruled at [M6-11](tasks/M6-11-acceptance-and-tag.md) rule 10, for the build**: the table now reads *"~1.8× the budget, authored — may carry a downside"*, which is what [M6-05a](tasks/M6-05a-what-a-pact-is.md) shipped and what the examples always said. Three of the four shipped Pacts carry a downside, and Keen Censer is the doc's own first example word for word.
- ~~**CH §3.1's third Oathbound clause is the multiplication M6-05a refused.**~~ **Ruled at M6-11 by the same edit**: *"25 % weaker"* is now an **authoring budget** — his own Pact forms at about ¾ of §13.2's 1.8× — rather than a multiplier that would turn a Pact's downside into a discount. Keen Censer and Zealotry predate it; [M7 row 10](#carry-forward-into-m7) asks M7-04 to re-read them.
- **Nothing in this project names a second language, so M6-10 ships the mechanism and no table.**
  Found at M6-00c by sweeping the design documents rather than the code: GD §18's options list has no
  language row, GD §19's V1 scope has no localisation line, GD §21's open questions has none, and
  ADR-0012 stops at *"adding a language is adding a table."*
  [M6-10](tasks/M6-10-the-rest-of-localisation.md) therefore ships a two-deep fallback, a `Format`
  member, the profile's locale, an assembly sweep and a **pseudo-locale**, and refuses the languages:
  choosing a list is a business decision that waits on **GD §21.1's business-model question** (the
  line below), and ~177 rows × *n* is translation this project has no author for and no way to check.
  Promoted by that ruling, or by **M8-06**, which decides what a store listing says and is therefore
  the first task that cannot avoid knowing. The **picker** is a separate half and is GD §18's options
  screen, **M8-02** — which also inherits the redraw problem M6-10 rule 6 states, because every label
  in this game is written in `Start`. **M6-10 shipped the mechanism, and left the first real language
  four things**, each ruled rather than missed: `SkillRow`'s seconds threshold and cooldown keep an
  invariant `0.5 s`, because a missing trigger row must keep its number; the Overflow toast joins its
  word to `×n` in code; `Boot.unity`'s `"Soulvail"` is typed into the scene; and the default font's
  static atlas carries Latin-1 and nothing past it, so any language beyond Latin-1 needs a font first.
- **Cosmetics are the third row of GD §14.2 and nothing owns them.** Opened at
  [M6-09a](tasks/M6-09a-profile-v4-and-what-a-shard-buys.md), which ships class unlocks and leaves
  *"Cosmetics · 100–300 · pure vanity, no power"* unbuilt: there is no cosmetic, no slot, no screen
  and no art. GD §19's V1 list says *"Soul Shards → class unlocks **and cosmetics** only"*, so the
  half that ships is half of a V1 line rather than all of an optional one. Promoted by **M7-05/06**,
  the art pass, which is the first thing that could produce one — and by then the profile already has
  a list-shaped field to copy.
- **GD §13.4's Fracture cannot be built against the arenas that exist.** Refused at
  [M6-06b](tasks/M6-06b-four-ordeals-and-two-refusals.md) with three counts: core has never seen an
  arena's cover (`ArenaView.CoverCount` walks Unity objects on the `Cover` layer); the two shipped
  prefabs author **4** and **5** pillars against GD §7.2's floor of 3 and `ArenaView.MinCoverPillars`
  of 3, so *"3 fewer"* leaves 1 and 2 — under the arena contract and under GD §12.4's *Cover
  guarantee*; and each arena's `NavMeshSurface` collects every layer into a **baked** `NavMeshData`
  asset, so a pillar deactivated at runtime leaves its carved hole and enemies path around nothing.
  Promoted by **M7-05/06**, the arena art pass, where *"fractured"* can be an authored variant room
  swapped through `ModeSpec.ArenaFor` rather than a subtraction from a room built assuming its cover.
  **[M6-11](tasks/M6-11-acceptance-and-tag.md) rule 10 does not wait on it**: the refusal is counted,
  `Arena_HasFewerThanFourPillarsToSpare` pins the arithmetic, and answering it needs arenas that do
  not exist — so nothing this acceptance can measure moves it.
- **GD §13.4's Echo admits two readings with opposite signs, and the wave curve makes it one wave in
  five either way.** Refused at [M6-06b](tasks/M6-06b-four-ordeals-and-two-refusals.md).
  `W(n) = clamp(2 + floor(n/5), 2, 5)` is 5 for every stage from 15 on and Ordeals start at 25, so
  *"every 4th wave"* is wave 4 of 5, for ever. Read as *replace*, it hands wave 4 wave 3's
  composition and **removes about 6.7 % of the stage's threat budget** — the triangular split makes
  wave 3 worth 3/15 and wave 4 worth 4/15 — so a *deep-run modifier* lowers difficulty. Read as
  *add*, two waves are alive at once and GD §12.2's concurrency cap, which protects frame rate
  **and** P1's readability, is doubled. Choosing is a design decision; promoted by the owner's
  ruling, or by **M7-02**, which already has to spend `WavePlan.UnspentThreat` inside
  `WaveComposer` and arrives with the budget arithmetic open. **[M6-11](tasks/M6-11-acceptance-and-tag.md)
  rule 10 does not wait on it either, and it is the first task that can put a number under it:**
  instrument B plays past stage 25 for the first time, so *what one wave in five is actually worth at
  stage 35* is measured for free rather than derived from the triangular split on paper. The entry
  records the figure so M7-02 opens with one instead of with 6.7 %. **The figure, from M6-11's log:** from stage ~26 waves 3, 4 and 5 are the **same composition — Bloaters only, 26–28 each** — so *replace* removes nothing at all at the depths Echo would be dealt, and *add* doubles a full-cap Bloater wave. The collapse itself is [M7 row 11](#carry-forward-into-m7).
- **CI: EditMode tests on every PR** (GitHub Actions + `game-ci/unity-test-runner`). Worth it since M1-21 — 455 tests, and M2 adds migration tests, which rot silently. Blocker: a Unity licence activation secret, not the value.
- **PR template** mirroring a spec's Acceptance section. Not adopted yet.
- **A real Android device.** Every **[device]** row is deferred until one exists and the first hardware session runs them all. BlueStacks cannot run the APK (M0-20a), so there is no fallback outside the Editor.
- **Company name is a placeholder** — `Soulvail`, set in M0-19 with the same status as the application identifier. Both are permanent once uploaded to a store, so M8-06 changes them together before the first upload.
- **Machine-local Gradle configuration is not in this repo** — `~/.gradle/gradle.properties` (HTTP proxy) and `~/.gradle/init.gradle` (Aliyun mirrors) are what make an Android build resolve on this connection; a second machine needs its own. The why, including the SOCKS-vs-HTTP trap, is [Traps.md §10](../Traps.md).
- ~~**Four raw UI strings to localise in M6-10.**~~ Owned by M3-14a rule 8 from M3-00d, because the owner's ruling landed `ILocalizer` three milestones early; shipped there. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- ~~**GD §16.2 and GD §16.4 contradict each other about a dying enemy's colour.**~~ Resolved at M3-15: an ambiguity, not a contradiction — M3-13b's maroon is *toward red* and 0.361 from `#FF4A1F`; §16.2 gained a half-line. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- **Application identifier** — placeholder `com.soulvail.dev`; permanent once uploaded, so it changes in M8-06 before the first store build.
- ~~**Out-of-range focus tap.**~~ Closed at M2-15 as M2 ledger row 12; built by M2-12a. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- ~~**Already-merged specs still point at pre-split task ids.**~~ Fixed at M2-15 in one pass; the class recurs on every split, and the next milestone that splits owes the same pass to its acceptance. **Run again at M5-08 — the fifth milestone it has applied to** — where four tasks split (M5-04, M5-05, M5-06, M5-07a) and thirty-three live pointers were re-aimed. **That pass found the thing this line does not predict:** `PROGRESS-M3.md` had been archived without re-basing its relative paths, so all **73** of its `tasks/`, `ROADMAP.md` and `../Traps.md` links had pointed at nothing since M3 closed. **So the obligation is now two passes, not one** — re-aim the split ids *and* re-base every link in anything moved to `archive/`, which M5-08 did for `PROGRESS-M5.md` and `ROADMAP-M5.md` and verified file-by-file. **Run a sixth time at M6-11**, where seven titles split (M6-01, 02, 03, 05, 06, 07 and 09): **61** split ids across twenty-five specs and AR §6 were re-aimed, counted from the diff, quotations of history left alone, and both of M6's archives re-based and checked link by link by a script that resolves every file and heading anchor. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- **Restless Dead is a `StatTarget.Minions` node filed in the Gravecaller's *Rot* branch rather than *Legion*.** Found at [M5-08](tasks/M5-08-acceptance-and-tag.md) while confirming [M6 row 6](#carry-forward-into-m6): it is why an Oathbound borrowing the Gravecaller loses **two** of three branches instead of one, since `SplashFlow` refuses any branch carrying a minion effect. *Legion* being closed to a minionless class is inherent and correct; *Rot* being closed is an accident of where one node was filed. Deliberately not moved at M5-08 — shifting it makes Legion five nodes and Rot three, and re-balancing a branch is authoring rather than acceptance. Promoted by **M7-04**, which authors all 81 nodes and will place this one again anyway.
- **A spatial hash for `AlliesNearby`** — its `n² − n` comparisons a frame cost 756 at M2-04's cap of 28 and 4,032 at the registry's 64. Promoted the day a device cap above 40 ships (M8-03), and not before.
- ~~**GD §12.1's stage-40 budget row says 1,772 where its own formula gives 1,876.9.**~~ Corrected at M2-15 to 1,877; the code was pinned against being "fixed" in three places. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- **`SanctumOpened` is published before `RunState.IsSanctumOpen` is true.** `StageFlow` publishes from inside its tick and `RunSession` copies the phase after the tick returns, so a subscriber asking the port inside the event is told the shop is shut and `CanBuy` refuses all four. `SanctumPresenter` draws on its next frame instead, and `SanctumPresenterTests.Sanctum_TheFirstDrawWaitsForTheShopToOpen` pins the ordering. Promoted by the first task with a second subscriber that must answer inside the event, or an owner's ruling to move the write — an AR §18.1 change, found at M6-03a.
- Business model decision (GD §21.1) — was *"needed before M6"*, and M6 shipped without it; it now blocks the second-language line above and **M8-06**'s store listing.
- Google Play Games save sync (GD §21.6) — after M2's local save exists.
- **A boot load that lands after a run has started would seed `SavedRun` over a newer mirror** — [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md#as-built) rule 4's one hazard, harmless while `LocalJsonSaveStore` answers inside `BootFlow.Start`. Promoted by the first asynchronous `ISaveStore`, which is ADR-0007's syncing one and the line above.
- **The PlayMode suite writes the owner's real `run.json`.** `BootSmokeTests.Descend_StartsARun_AndRefusesASecondTap` reaches a run through boot's `LocalJsonSaveStore` over `persistentDataPath`, unshadowed — unlike `ResumeFlowTests.PlayARun`, which shadows the store for this reason — so every handover's PlayMode pass replaces whatever run an Editor playtest left. Found at [M6-11c](tasks/M6-11c-stage-one-without-the-m1-dummies.md#as-built) by the file's timestamp. Promoted by the next played acceptance that means to Continue a run across sessions, or the next task that touches `BootSmokeTests`.
- **The Sanctum still sells a Reroll when every untaken node is unreachable** — an Upgrade whose parent was banished, a Keystone its branch can no longer reach. `SanctumShop.AnythingInThePool` counts nodes left, not nodes offerable; [M6-11d](tasks/M6-11d-no-reroll-for-a-finished-tree.md#as-built)'s stated cost. Promoted by M7-04, whose 27-node trees carry enough Upgrades and Keystones for a banish to orphan the last of a pool.
- ~~**`dotnet` SDK on the dev machine → activates the pre-commit format check.**~~ Struck at M4-07 as stale: SDK **10.0.401** has been installed since before M3-12c, which is the first task the check ever refused, and `dotnet format` has been a live gate in every handover since. Nothing promoted it; it stopped being true and nobody removed the line.
- GPU Resident Drawer evaluation — when a stage first drops below 60 fps on mid-tier.
- Separate `.NET` class library for Core (better tooling) — if Unity-hosted tests ever feel slow.
