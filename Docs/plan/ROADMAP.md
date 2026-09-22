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
| **M5** | **Second class** | Gravecaller: projectile weapon + leading, Shroudstep, Wights, its tree, class select | 15 |
| **M6** | **Systems complete** | Sanctum shop, Veilrot + Pacts + Claiming, Ordeals, Emberwright, unlocks, localisation tables | 11 |
| **M7** | **Content pass** | Full V1 roster, Elites/affixes, Choirmother, all 81 nodes, both biomes' art, audio | 8 |
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

**Complete — 15 tasks, 2 521 EditMode + 21 PlayMode green, `m5` the owner's to tag.** The game stopped being one character: a card per class at the door, a Gravecaller behind it with a led projectile, a corpse the arena walks at and an army raised from a quarter of the dead, and CH §5.4's half-tree moment where a run borrows one branch of the other class for good. **And it is the first acceptance this project measured rather than argued** — [M5-08](tasks/M5-08-acceptance-and-tag.md) instrumented a build, two played runs reached stages 16 and 19 killing three bosses each, and that log discharged three ledger rows no amount of reading had moved. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M5.md](archive/ROADMAP-M5.md); the log is [archive/PROGRESS-M5.md](archive/PROGRESS-M5.md). **One task is open:** [M5-08a](tasks/M5-08a-splash-offers-what-install-refuses.md), created *by* the acceptance under its rule 8.

### Carry-forward into M5

**Closed at M5-08: nine rows, six discharged by their owners, three closed by the playtest — none dropped.** What carries is [M6 rows 1–4](#carry-forward-into-m6); rows 2, 7 and 8, which three acceptances in a row had owned, were answered by one instrumented session. The rows and M5-08's closing table are in [the archive](archive/ROADMAP-M5.md#carry-forward-into-m5). This heading stays so links into it keep resolving.

## M6 — Systems complete *(titles only)*

| ID | Task |
|---|---|
| M6-01 | Essence wallet + drops from events |
| M6-02 | Sanctum shop core: reroll, banish, heal, cleanse |
| M6-03 | Sanctum screen |
| M6-04 | Veilrot meter, thresholds, the Claiming (GD §10) |
| M6-05 | Pact node variants in `OfferGenerator` (GD §13.2) |
| M6-06 | Ordeals (GD §13.4) |
| M6-07 | Emberwright spec, Cinder Orb, Blink, Kindling |
| M6-08 | Emberwright tree v1 + actives |
| M6-09 | Class unlocks: Shards + achievements |
| M6-10 | The **rest** of localisation: locale selection, the other languages' tables, and every screen M3 did not build — `ILocalizer`, `TableLocalizer` and one English table land early at [M3-14a](tasks/M3-14a-localizer-and-english-table.md), by the owner's M3-00d ruling |
| M6-11 | M6 acceptance, tag `m6` |

### Carry-forward into M6

**What M6's specs must absorb.** Opened at [M5-08](tasks/M5-08-acceptance-and-tag.md) on [M2's terms](#carry-forward-into-m2), unchanged through five milestones: every row names the task that must deal with it, and **a row leaves only when its owner's *As built* says it is answered.** Four rows carry from [M5's table](#carry-forward-into-m5), one is M5 row 7 promoted from an observation to a fix now that it has been seen, and two are what M5-08's playtest found. Ranked by what it costs to fix later rather than now.

**M4-07 rule 11 still holds: every row names an *unmerged* owner, or says explicitly that it has none and why** — and *"the next acceptance"* is a legitimate owner only where the row is a **verdict** rather than a fix.

**And M5-08 adds a rule of its own, which is the lesson that milestone ends on: a row that wants a number is discharged by an instrument, not by an argument.** Rows 2, 7 and 8 sat on this table for three milestones being reasoned about; one temporary probe and two played runs closed all three in a day. **A row whose text contains a question about what happens *in play* should name the instrument that will answer it**, not the task that will think about it.

| # | Finding | Owner | Cost of leaving it |
|---|---|---|---|
| 1 | **The device debt, carried whole from [M5 row 3](#carry-forward-into-m5), and now five tags deep.** `m0`–`m3` are tagged and `m4`/`m5` are pending, all on Editor evidence, and **nothing has ever run outside the Editor**. [M3-10a](tasks/M3-10a-skill-slot-buttons.md)'s **multi-touch is the one row that risks a *feature*** rather than a verdict: if Android does not deliver a thumb on the stick and a thumb on S3 at once, manual casting does not work at all. **M5-08 sharpened that without settling it** — of 84 casts across two full played runs, **80 were automatic and 4 manual**, so the thumb slots are barely exercised even where they work, and whether that is the auto-cast design succeeding or the buttons being unreachable is a device question. **Kill-from-recents is the one *correctness* row**: M5-08 witnessed `Continue` restoring a run mid-stage in the Editor, which is not the same act as the OS killing a process mid-write against M2-13b's atomic write. The rest, re-stated rather than re-listed: haptics and M1-20's 100 ms window, touch latency, real frame rate, thermal, landscape flip, sustained fps at the concurrency cap, **the Profiler's no-per-frame-`GC.Alloc` check unmet since M1-21**, `Physics.SyncTransforms()`' per-frame price, M2's pacing knobs, M3's six, M4's eight, and **M5's four** — 37 bodies in one arena against GD §11.3, a cyan Wight against a cyan player, two screens of cards at thumb distance, and the splash's two pages. **`Screen.dpi` reads 120 here against a phone's 400** ([Traps §9](../Traps.md)), so the Editor is not a weak instrument for these questions, it is the wrong one. **One M5-08 measurement does carry over, because it is frame time rather than layout:** 36 hitches of 50–300 ms across ~30 minutes of play, none correlated with an army rising or the splash opening. | The first hardware session; **M8-xx** if no phone arrives sooner | **High and compounding, unchanged for five milestones.** Each milestone adds rows and none retire, so the first device session gets larger and its failures get harder to attribute to the milestone that caused them. |
| 2 | **The Gravecaller breaks GD §12.4's TTK invariant from stage 9, and the tree is why.** Measured at [M5-08](tasks/M5-08-acceptance-and-tag.md) over two full runs: a Husk takes the Oathbound **4 hits flat from stage 4 to stage 16**, and the Gravecaller **4 → 5 (stage 3) → 6 (stage 9) → 7 (stage 14) → 8 (stage 16)**. GD §12.4's band is **3–5 at every depth** and its own words are *"drift above 5 means HP scaling is too steep or player scaling too weak"*. **It is the second, and it is not the player's picks** — the run took **all sixteen** nodes available to it, its own twelve plus four borrowed. **The Gravecaller's whole tree carries one weapon-damage node** (Sharpened Bone, +15 %) and one fire-rate node, so player damage grew **+15 %** across nineteen stages while a Husk's HP grew **+108 %**. Three of its twelve nodes buff minions rather than the player. **The Oathbound survives the same curve by starting at 3 hits rather than 4**, which is luck rather than design. **The contradiction behind it was resolved at M5-08 rather than carried:** §12.4's *"at every depth"* forbade the crossing power curves §12.5 is built on, so §12.4 is now scoped to *"up to the death horizon"* and cross-references §12.5 — **a balance pass cannot be run against a target that contradicts itself**, which is why the ruling came first and the tuning did not. | **M8-05**, which the ROADMAP already titles *"balance pass per class against the death horizon (GD §12.5)"* — the row landed on a task that already existed for it | **Medium now, high at M7.** M7-04 authors 81 nodes against this curve and M6-07 adds a third class to it; every class authored before the curve is fixed is a class authored against the wrong target. |
| 3 | **The UI redesign — every taken node shown, an Active or Passive marked, an Auto skill marked by something orbiting it — still has no owner, and its blocker is still M7's icons.** The *legibility* half of [M5 row 1](#carry-forward-into-m5) discharged at [M5-05b](tasks/M5-05b-decoy-view-and-the-look.md): twelve serialized values at 28 pt, **12.36 dp**, `Slots_TheShippedWordsFitAtTwentyEight` green and tight at 143.6 px of S1's 150. The redesign half has not moved since the `m3` tag, by the owner's standing ruling that it belongs to neither M4 nor M5, and M3-10b rule 8's 24 dp cells hold neither an icon nor a word. **M5 added to it rather than moving it**: [M5-07a-ii](tasks/M5-07a-ii-the-half-tree-moment.md) rule 12 ships a borrowed node that is **owned, taken and invisible on the tree screen**, because the screen draws three columns over a run that may hold four. Sequencing is unchanged — **icons → readout → rotation**. | **None, and that is the ruling rather than an omission.** M7-05/06 bring the art and M7-04 the nodes; the earliest honest owner is **M8-01**, where the feel verdict is also re-taken | **Medium and static.** It has cost nothing for two milestones because the content it would display does not exist yet; it starts costing the moment M7-04 authors 81 nodes the tree screen cannot draw. |
| 4 | **`FrameOrderTests.Ticker_RunsTheStepsInOrder` fails about one run in ten, diagnosed and unfixed** — carried from [M5 row 4](#carry-forward-into-m5). The instrumentation [M5-05a](tasks/M5-05a-wight-views-and-concurrency.md) added has now answered twice, and both times it said ***wrong wedge*** rather than *late sync* — M5-06a's failure put the body **0.0001 m behind the apex**. So the seam's synchronisation is **not** late and the fault is the fixture's own zero-margin apex. **Nothing is fixed, deliberately:** changing the margin changes what the project's one frame-order assertion means, and that is a decision rather than a repair. **M5 added a second flake beside it and they are not the same kind**: known issue 7's `Animator_AttackSpeedIsUnreachableFromAnEditorClock` asserts `Time.time` is 0 outside Play, and an Editor that has ticked reads 0.73 and does not reset on reimport — that one is a false premise rather than a margin. | **Unowned by ruling, and the ruling is M5's**: no task may "fix" it without deciding what the assertion is for. The earliest honest owner is **M6-11**'s acceptance, as a verdict | **Low but corrosive.** A suite with a one-in-ten flake trains everyone to re-run rather than read, which is exactly how the second flake went unnoticed until M5-08. |
| 5 | **A boss stage completes when the boss is down, whatever its adds are doing — and it has now been seen.** [M5 row 7](#carry-forward-into-m5) was an observation for three milestones because the boss had never died. At [M5-08](tasks/M5-08-acceptance-and-tag.md) it died six times, and the log answers the question the row actually asked: on the Oathbound's stage 10, `BOSS DEAD` and `STAGE 10 CLEARED` are **0.01 s apart**, and a player hit landed **5.2 s after the boss fell** and before stage 11 arrived. On the Gravecaller's stage 5, two Husks were still being killed **six seconds after the stage completed**. **So `SpawnDirector.IsStageComplete` returning `_bossCleared` does let an add hit the player through `Clear` and `Gate`, and the row is answered yes.** M5-00a weighed two fixes and refused both *on the grounds that nobody had observed the failure* — requiring adds cleared moves boss pacing **and** the depth `ShardPayout` reads; despawning them robs the player of three Husks' experience. **That objection is spent: the failure is now observed, and the fix is owed rather than optional.** The damage seen was trivial (0.4 of 187 max HP), so this is a correctness row rather than a balance one. | **An M6 task, specced by M6-00a** — it is a mechanism, so it may not be a late edit to an acceptance branch (M5-08 rule 8) | **Low today, medium at M7.** One boss authors adds; a second (M7-03) and Elites (M7-02) make it a rule rather than an instance. |
| 6 | **The splash offers branches the install refuses, and the screen it does it on has no exit.** Found at [M5-08](tasks/M5-08-acceptance-and-tag.md) by playing the case [M5-06a](tasks/M5-06a-what-a-legion-node-may-reach.md) rule 5 predicted. `SplashFlow.Options` builds one `SplashOption` per branch unconditionally; `RequireInstallable` — which sweeps for an unregistered primitive and for a `ModifyStat` aimed at `Minions` — runs only inside `Install`, after the choice. So an Oathbound borrowing the Gravecaller finds **two of three branches throw an `ArgumentException` out of a `Button.onClick`**: *Legion* (Exhume casts `RaiseMinions`; Grave Strength and Knitted Bone target `Minions`) and *Rot* (**Restless Dead targets `Minions`** — a minion buff filed in the wrong branch, which is an authoring accident rather than an inherent refusal). **The guard itself is correct and atomic** — *"Nothing has been installed"* — and M5-07a-ii deliberately built the screen with no way off it but through, so the two failures compose into a dead button with no feedback. **Recoverable only by quitting and pressing `Continue`**, because the choice is derived rather than saved — M5-07a-ii rule 6's stated *cost* turns out to be the escape hatch, which nobody designed and nothing tells the player. **DISCHARGED at [M5-08a](tasks/M5-08a-splash-offers-what-install-refuses.md)**, which shipped before the `m5` tag by the owner's ruling. `SplashFlow.TryRefusal` is the sweep as a predicate and both `BranchesOf` and `Choose` read it, so a refused branch is drawn dead with its reason and the exception survives only as the invariant behind it. **One thing the fix surfaced and the row did not predict:** rule 5 makes a refused branch draw its reason *instead of* its node count, which silently weakened two presenter rows that are not about this task — both would have kept passing against less, and the fixture moved one node so they keep their original claim. **The authoring half does not close** — Restless Dead is still a minion node in *Rot*, which is why a minionless class loses two branches rather than one; it is a [parking-lot](#parking-lot) line owned by M7-04 | **High until fixed, and it was in the build for one milestone.** With two classes shipped, CH §5.4's half-tree moment is a three-option screen with one real answer for half the roster — and on a phone there is no console to explain the other two. |
| 7 | **Localisation's last third, carried from [M5 row 5(iii)](#carry-forward-into-m5).** Parts (i) and (ii) discharged at [M5-06b](tasks/M5-06b-gravecaller-tree-v1.md) and [M5-02](tasks/M5-02-gravecaller-and-bone-bolt.md). What remains is locale selection, the other languages' tables and every screen M3 did not build — and M5 added four screens to that list (class select, the splash's two pages, and the tree screen's fourth column when it gets one). | **M6-10**, which the ROADMAP already holds as a title | **Low now, medium at M6-11.** Every screen shipped before it lands is one more to sweep, and the sweep is the cheap part — the tables are not. |


## M7 — Content pass *(titles only)*

| ID | Task |
|---|---|
| M7-01 | Lunger, Weaver, Warden (core + views) |
| M7-02 | Elites + affixes |
| M7-03 | Choirmother |
| M7-04 | All 81 nodes |
| M7-05 | Ashen Reach: 8–12 arenas + art |
| M7-06 | Drowned Choir: 8–12 arenas + art |
| M7-07 | Audio layers, telegraph cues, boss music |
| M7-08 | M7 acceptance, tag `m7` |

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
- **A stage editor — *“maybe later we should have a stage creator”*.** The owner's words at M4-00a, alongside the ruling that **which stages hold which enemies is authored on the mode**. `ModeSpec` already carries an enemy roster with `_introducedAtStage`, and M4-01b adds a boss roster beside it, so the *data* is already per-stage-ish; what does not exist is a way to author a **specific** stage rather than a rule that generates them. Promoted when a designer wants stage 7 to be different from stage 6 for a reason no curve expresses — most likely **M7**, when the roster is full and the biomes differ, or M6-04 if Ordeals need per-stage authoring. **Not M4's**: one boss on a rule is what M4 needs, and building an editor for one entry is the kind of tooling that outlives its content.
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
  on.** Added knowingly at [M4-05a](tasks/M4-05a-shard-payout.md) rule 7, and **struck rather than
  carried** when M4-07 distributed [M4 ledger row 6](#carry-forward-into-m4): a payout is not a mode, a
  character or an enemy, so inventing an asset for it would be M6's Sanctum arriving early and
  unspecified. GD §14.1's `10` and `50` are the one place ADR-0006 is deliberately not honoured, and
  the doc now says so. Promoted by **M6-02**, the first task that gives a Shard somewhere to be spent
  and therefore the first with an asset the two numbers could live on.
- **CI: EditMode tests on every PR** (GitHub Actions + `game-ci/unity-test-runner`). Worth it since M1-21 — 455 tests, and M2 adds migration tests, which rot silently. Blocker: a Unity licence activation secret, not the value.
- **PR template** mirroring a spec's Acceptance section. Not adopted yet.
- **A real Android device.** Every **[device]** row is deferred until one exists and the first hardware session runs them all. BlueStacks cannot run the APK (M0-20a), so there is no fallback outside the Editor.
- **Company name is a placeholder** — `Soulvail`, set in M0-19 with the same status as the application identifier. Both are permanent once uploaded to a store, so M8-06 changes them together before the first upload.
- **Machine-local Gradle configuration is not in this repo** — `~/.gradle/gradle.properties` (HTTP proxy) and `~/.gradle/init.gradle` (Aliyun mirrors) are what make an Android build resolve on this connection; a second machine needs its own. The why, including the SOCKS-vs-HTTP trap, is [Traps.md §10](../Traps.md).
- ~~**Four raw UI strings to localise in M6-10.**~~ Owned by M3-14a rule 8 from M3-00d, because the owner's ruling landed `ILocalizer` three milestones early; shipped there. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- ~~**GD §16.2 and GD §16.4 contradict each other about a dying enemy's colour.**~~ Resolved at M3-15: an ambiguity, not a contradiction — M3-13b's maroon is *toward red* and 0.361 from `#FF4A1F`; §16.2 gained a half-line. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- **Application identifier** — placeholder `com.soulvail.dev`; permanent once uploaded, so it changes in M8-06 before the first store build.
- ~~**Out-of-range focus tap.**~~ Closed at M2-15 as M2 ledger row 12; built by M2-12a. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- ~~**Already-merged specs still point at pre-split task ids.**~~ Fixed at M2-15 in one pass; the class recurs on every split, and the next milestone that splits owes the same pass to its acceptance. **Run again at M5-08 — the fifth milestone it has applied to** — where four tasks split (M5-04, M5-05, M5-06, M5-07a) and thirty-three live pointers were re-aimed. **That pass found the thing this line does not predict:** `PROGRESS-M3.md` had been archived without re-basing its relative paths, so all **73** of its `tasks/`, `ROADMAP.md` and `../Traps.md` links had pointed at nothing since M3 closed. **So the obligation is now two passes, not one** — re-aim the split ids *and* re-base every link in anything moved to `archive/`, which M5-08 did for `PROGRESS-M5.md` and `ROADMAP-M5.md` and verified file-by-file. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- **Restless Dead is a `StatTarget.Minions` node filed in the Gravecaller's *Rot* branch rather than *Legion*.** Found at [M5-08](tasks/M5-08-acceptance-and-tag.md) while confirming [M6 row 6](#carry-forward-into-m6): it is why an Oathbound borrowing the Gravecaller loses **two** of three branches instead of one, since `SplashFlow` refuses any branch carrying a minion effect. *Legion* being closed to a minionless class is inherent and correct; *Rot* being closed is an accident of where one node was filed. Deliberately not moved at M5-08 — shifting it makes Legion five nodes and Rot three, and re-balancing a branch is authoring rather than acceptance. Promoted by **M7-04**, which authors all 81 nodes and will place this one again anyway.
- **A spatial hash for `AlliesNearby`** — its `n² − n` comparisons a frame cost 756 at M2-04's cap of 28 and 4,032 at the registry's 64. Promoted the day a device cap above 40 ships (M8-03), and not before.
- ~~**GD §12.1's stage-40 budget row says 1,772 where its own formula gives 1,876.9.**~~ Corrected at M2-15 to 1,877; the code was pinned against being "fixed" in three places. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- Business model decision (GD §21.1) — needed before M6.
- Google Play Games save sync (GD §21.6) — after M2's local save exists.
- ~~**`dotnet` SDK on the dev machine → activates the pre-commit format check.**~~ Struck at M4-07 as stale: SDK **10.0.401** has been installed since before M3-12c, which is the first task the check ever refused, and `dotnet format` has been a live gate in every handover since. Nothing promoted it; it stopped being true and nobody removed the line.
- GPU Resident Drawer evaluation — when a stage first drops below 60 fps on mid-tier.
- Separate `.NET` class library for Core (better tooling) — if Unity-hosted tests ever feel slow.
