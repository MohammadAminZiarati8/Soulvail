# M5-08 — M5 acceptance: is the Gravecaller a second class or a reskin? The ledger judged, and tag `m5`

**Size:** S · **Depends on:** everything in M5 · **Branch:** `m5-08-acceptance`
**Design refs:** CH §3.2, §4.2, §5, §5.4, §8 q1; GD §6.1, §6.2, §11.1, §11.3, §12.4, §13.1, §16.4; AR §18; ADR-0006, ADR-0008, ADR-0011 · **Ledger rows:** **all eight — this is where [M5's carry-forward table](../ROADMAP.md#carry-forward-into-m5) is closed and M6's is opened.** Rows **2**, **7** and **8** are this task's own to rule on; the other five are closed by their owners' *As built* or carried with a named unmerged owner

## Goal

Answer the M5 question on evidence — *is the Gravecaller a second class, or the Oathbound with a
different projectile?* — take the three numbers [row 2](../ROADMAP.md#carry-forward-into-m5) has been
owed since M3, spend the one played minute [row 8](../ROADMAP.md#carry-forward-into-m5) has been
waiting two tasks for, and tag `m5`.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, the feel verdict, the row-2 numbers, the row-8 discrimination, Current State pointing at M6-00a (rule 3) |
| `Docs/plan/archive/PROGRESS-M5.md` | M5's log entries, moved — **on time, for the fourth milestone running** (rule 5) |
| `Docs/plan/ROADMAP.md` | M5 ticked; the M5 ledger closed and **carry-forward into M6** opened; M6's rows promoted from titles where this checklist demands it |
| `Docs/GameDesign.md` · `Docs/Characters.md` · `Docs/CoreCombat.md` | Only if tuning changed a shipped number, or a flagged contradiction is resolved (rules 1, 9) — **CH §8 q1 is expected, see rule 10** |
| `Assets/_Project/Data/…` | Only if tuning changed — `Gravecaller.asset`, `Descent.asset`, `English.asset`, the twelve Gravecaller nodes |

No code. **A bug found here becomes `M5-08a…` with its own PR** — the M2-15a, M3-15 and M4-07
precedent, and rule 8's one exception.

## Checklist (owner)

Ticked at two grains, and the entry says which is which: **[play]** rows are covered by the owner's
playtest verdict rather than attested one at a time; unmarked rows are verified statically against
the assets, the code and the suite. **[device]** rows cannot be answered in the Editor and are
deferred (rule 7).

> **The boxes below are left as written, and that is the two-grain rule doing its job rather than an
> omission.** The **[play]** rows were answered by two instrumented runs — an Oathbound to stage 16
> and a Gravecaller to stage 19, three bosses each — and the answers are numbers, not ticks: they are
> in [*As built*](#as-built) and in the [M5 log entry](../archive/PROGRESS-M5.md). **Three rows were
> not answered and say so rather than being ticked:** row 2's *two-second read* on twenty-four
> descriptions, CH §3.2's *are Wights unmistakable at phone scale*, and the feel question itself —
> all three are judgements a person makes at device scale, and the owner deferred them to **M8-01**
> with M7's content and art in. **Two rows were answered in the opposite direction to their
> expectation**, which is the point of writing them down in advance: row 7 came back *yes*, and the
> Gravecaller's hit counts came back **outside** GD §12.4's band.

**A second class exists** — the ROADMAP's *ends when* for M5, read literally

- [ ] **[play]** The class-select screen offers two cards and the card tapped is the run that starts ([M5-07](M5-07-class-select-screen.md))
- [ ] **[play]** The basic attack is a **bolt that flies and is led** — a walking Husk is hit where it will be, not where it was (CC §3.7, [M5-01](M5-01-projectile-weapon-and-leading.md))
- [ ] **[play]** **Four hits kill the first Husk of the run** — [M5-02](M5-02-gravecaller-and-bone-bolt.md) rule 2's whole argument, counted in the arena rather than computed
- [ ] The speed band moved and the documents moved with it — **3.0 / 3.1 / 3.4 m/s**, GD §6.1, CH §3 and CC §2.5 all edited in M5-02's PR, and the [parking-lot](../ROADMAP.md#parking-lot) line struck (M5-02 rule 1)
- [ ] **[play]** Shroudstep blinks 6 m and **leaves a corpse the arena walks at for three seconds**, and an enemy that reaches it swings and hurts nobody ([M5-03](M5-03-shroudstep-and-corpse-decoy.md) rule 7)
- [ ] **[play]** About a quarter of kills stand back up, walk at the nearest living enemy and hit it; a Wight expires at twenty seconds and pays no experience ([M5-04a](M5-04a-minion-agents-and-registry.md), [M5-04b](M5-04b-rise-and-minion-stats.md))
- [ ] `IRandom.Drops` is the stream Rise draws from, no sixth stream was created, and **`RunSnapshot.CurrentVersion` is still 3** (M5-04b rule 1, [M5-07a-ii](M5-07a-ii-the-half-tree-moment.md) rule 6)
- [ ] **[play]** The tree offers **Gravecaller** nodes, Exhume auto-casts below two Wights, and a Legion node visibly changes what a Wight is worth ([M5-06a](M5-06a-what-a-legion-node-may-reach.md), [M5-06b](M5-06b-gravecaller-tree-v1.md))
- [ ] **[play]** At half the tree the run asks for a second discipline, and the next offer can draw from the borrowed branch (CH §5.4, [M5-07a-i](M5-07a-i-the-runs-tree-widens.md), [M5-07a-ii](M5-07a-ii-the-half-tree-moment.md))

**The feel question, and it is the milestone's** (record the answer and *why*)

- [ ] **[play]** **Is the Gravecaller a second class or a reskin?** M5 shipped three mechanics the Oathbound does not have — a led projectile, a taunting corpse, an army — and the honest failure mode is that the run still plays like *walk at things and hold the stick*. If it does not land, which is wrong: Rise's 25 % at a cap of 3, the Wights' 20 s / 20 HP / 8 damage (**none of [M5-02](M5-02-gravecaller-and-bone-bolt.md) rule 5's eight numbers has a document behind it**), the decoy's 3 s, Bone Bolt's 9 at 4.0/s, or the fact that Wights fight and the player does not
- [ ] **[play]** **CH §3.2's watch item, answered or not: are Wights unmistakable from enemies at phone scale?** They are `Palette.Player`'s cyan at 0.7 scale ([M5-05a](M5-05a-wight-views-and-concurrency.md) rule 8) — **the player's own colour**, which is GD §16.4's language read correctly and a collision anyway. *"If players can't tell their army from the swarm, the class fails"* is CH §3.2's own sentence and this is where it is tested
- [ ] **[play]** **Does *"you are not the damage"* survive contact?** 36 DPS against the Censer's 39 and 80 HP against 140, with an army that is *most* of a second weapon (M5-02 rules 2 and 5)

**Ledger row 2 — the three M3 numbers, owed since M3-15 and carried twice**

- [ ] **[play]** **Row 1's played hit counts** at stages 1 / 15 / 30 against the arithmetic's 3 / 4 / 5 (GD §12.4) — **and now at two classes**, because M5-02 rule 2 authored a second weapon into the same band
- [ ] **[play]** **Row 8's two labelled timings** at stages 1, 5 and 10 — stopwatch **and** `RunState.Time`, and the gap between them **is** GD §13.1's interruption budget
- [ ] **[play]** **Row 9's two-second read**, now on **twenty-four** real English descriptions rather than twelve ([M5-06b](M5-06b-gravecaller-tree-v1.md) rule 7)
- [ ] **The hit-count third was never blocked by the UI and M4-07 said so.** It is *counted in the arena*, so it is produced by the first playtest of any build. **This is the third acceptance it has been owed to.** If it carries again, the reason is written here rather than implied, and the row names an unmerged M6 owner

**Ledger row 7 — a boss stage completes when the boss is down, whatever its adds are doing**

- [ ] **[play]** **On a boss stage, was the player hit by an add after the boss fell?** M5-00a ruled *change nothing and observe*: `SpawnDirector.IsStageComplete` returns `_bossCleared`, and both candidate fixes move something worse — requiring the adds cleared moves boss-stage pacing *and* the depth `ShardPayout` reads, and despawning them robs the player of three Husks' experience
- [ ] **A yes promotes this to an M6 task with a real fix behind it; a no strikes it.** Either way it stops being an observation after three milestones
- [ ] **It needs the boss to die**, which M4-07's playtest did not manage — so this row and row 8 are answered by the same run or neither is

**Ledger row 8 — the Warden: a number or a bug, and the played minute it has been waiting for**

- [ ] **[play]** **Hit the boss and watch whether the segmented bar moves at all.** M4-07's playtest reached stage 5, was killed by the Warden and did not kill it; the owner's call was *"it's good for now"*. **Two things could be true and they need opposite work:** 4 200 HP outrunning the player's real damage at stage 5 is a **number** and moves in `Warden.asset`'s Inspector; a health bar that never moves is a **bug** and means the fight has never been winnable
- [ ] **This row was re-owned to M5-00b and M5-00b could not place it**, because it wanted a played minute before it wanted a task and the minute had not happened. **It is placed here or it is carried a third time with a reason** — and *"the next playtest"* stops being a legitimate owner once there have been three of them
- [ ] **The arithmetic does not settle it and M4-07 said why:** M4-01b's 106.7 s assumes *uninterrupted* damage, and a played fight spends time walking out of a shockwave. A fight that runs long is expected; a bar that does not move is not
- [ ] **A second class makes the question sharper, not softer.** The Gravecaller does 36 DPS to the Censer's 39 **plus** whatever an army adds, so if the bar moves for one class and not the other that is the discrimination arriving for free

**Ledger rows closed by their owners — verified, not assumed** (rule 2)

- [ ] **Row 1** — the twelve serialized values at 28 pt, `Slots_TheShippedWordsFitAtTwentyEight` green, and **the redesign explicitly not done** ([M5-05b](M5-05b-decoy-view-and-the-look.md) rules 5–7). The row's legibility half closes on arithmetic (12.3 dp against a 12 sp floor); the Editor still cannot confirm it (`Screen.dpi` 120, [Traps §9](../../Traps.md))
- [ ] **Row 3** — `_ALPHAPREMULTIPLY_ON` on `M_TelegraphRing.mat`, **six prefabs dimmed at once**, before-and-after recorded (M5-05b rules 8, 9). **The rest of row 3 is device debt and does not close** — it carries into M6 with M5's own additions: 37 bodies against GD §11.3, a cyan Wight against a cyan player, two screens of cards at thumb distance, and the splash's two pages
- [ ] **Row 4** — the instrumentation in `FrameOrderTests`, and **what it said**: if the row failed at all this milestone, the message named *late sync* or *wrong wedge*, and that is the first diagnosis thirteen tasks of tallies have produced. **If it never failed, that is luck and the row carries** ([M5-05a](M5-05a-wight-views-and-concurrency.md) rule 10 says so in advance)
- [ ] **Row 5(i)** — `LevelUpFlow`'s two Overflow `const`s gone, on `Descent.asset` at 0.02 / 0.02, `Overflow_TheShippedRunIsIdentical` green ([M5-06b](M5-06b-gravecaller-tree-v1.md) rules 8–11). **Row 5(ii) closed at M5-02** and **5(iii) is M6-10's**, so 5 closes only if all three are accounted for
- [ ] **Row 6** — `Boss_TickAllocatesNothing` green **across a phase threshold** ([M5-04a](M5-04a-minion-agents-and-registry.md) rule 13), so the clear-and-summon branch is measured rather than the `inner` behaviour

**The two forcing questions M5 opened with** (rule 4)

- [ ] **`IStatBlock` for a minion** — answered by [M5-04b](M5-04b-rise-and-minion-stats.md)'s `MinionStats` and [M5-06a](M5-06a-what-a-legion-node-may-reach.md)'s `MinionRecipeStats`: **four implementations, no generalisation, and `PlayerStat` gained nothing** for a Wight (M5-04b rule 8). Record whether four honest switches still looks right at four
- [ ] **The branch-index limitation** — answered by [M5-07a-i](M5-07a-i-the-runs-tree-widens.md): one index space, branch 3, `SkillTreeSpec.BranchCount` still 3. The [parking-lot](../ROADMAP.md#parking-lot) line struck, and M3-03's own remarks corrected in place if they still claim one tree
- [ ] **[play]** **`StatTarget.Minions`' twenty-second lag, felt rather than argued** (M5-06a rule 3): does a Legion node taken mid-fight read as *nothing happened* before the next Wight rises?

**Findings this milestone's specs predicted and the playtest must confirm or refuse**

- [ ] **An Oathbound borrowing the Gravecaller's Legion branch** — [M5-07a-ii](M5-07a-ii-the-half-tree-moment.md) manual step 6. M5-06a rule 5 refuses a `Minions` effect on a class with no `MinionSpec` **at `Start`**, and a branch borrowed mid-run is not at `Start`. Either the install is refused (rule 10 working) or it is not (a finding, and `M5-08a`)
- [ ] **A borrowed node is owned and invisible on the tree screen** — M5-07a-ii rule 12's ruling. Record whether it reads as a bug; a yes is an M6 row, a no waits for M7's icons
- [ ] **A resumed run can ask for its discipline twice** — M5-07a-ii rule 6's stated cost, seen once on purpose so it is never reported as a bug
- [ ] **CH §8 q1's concurrency answer**, ruled at [M5-05a](M5-05a-wight-views-and-concurrency.md) rule 5: a separate pool, a cap of eight, **no** device-tiered damage buff. Rule 10 annotates CH §8 q1 rather than leaving it open

**Performance and hygiene** (GD §11.1, AR §14)

- [ ] `AllocationAssert` rows green across every new `Tick` path — `MinionSystem`, `RisePassive`, `LureSystem`, `ProjectileLead`, `MinionRecipe`, `RaiseMinionsHandler`, and `BossBehaviour` (row 6)
- [ ] **[play]** No hitch when an army rises, when a decoy drops, or when the splash screen opens
- [ ] Six assemblies, zero compile errors, zero analyzer warnings, the full suite green through `TestRunnerApi` — **EditMode and PlayMode both, and PlayMode run more than once**
- [ ] `dotnet format whitespace --folder --verify-no-changes` green over `Assets/_Project`
- [ ] `git diff m4 HEAD -- ProjectSettings/` shows **nothing**, or one line committed on purpose with `ALLOW_PROJECT_SETTINGS=1` and named here. **`TimeManager.asset` is expected** and has been on most tasks since M3

## Behaviour

1. **Tuning edits go to the ScriptableObjects and the named constants only**, and GD, CH and CC are
   updated in the same PR. Doc and asset never disagree — M2-15 rule 1. **M5-02 rule 5's eight
   authored Wight numbers are the likeliest to move**, and they are the ones with no document behind
   them, which is exactly why the spec said so.
2. **The ledger is closed row by row, and a row is only closed by its owner's *As built*.** Eight
   rows; for each, either the owning task's footer says it is answered — in which case it is struck —
   or it is carried into M6's table with the task that will answer it. **M4-07 rule 11 holds: every
   carried row names an *unmerged* owner, or says explicitly that it has none and why**, and *"the
   next acceptance"* is a legitimate owner only where the row is a **verdict** rather than a fix.
3. **The M6 ledger is opened in the same pass**, from three sources: rows carried forward,
   parking-lot entries that have acquired an owner, and this checklist's own misses. M6-00a's specs
   are written against it.
4. **The PROGRESS entry lists what M6 must know.** Especially: whether four `IStatBlock`
   implementations is still the right answer at four; whether one branch index space survives a third
   class; whether `MinionSystem.MaxConcurrent`'s eight is the right ceiling once a played run has
   seen an army; and **which of M5's numbers the playtest moved**, because M6's Sanctum and Veilrot
   both tune against them.
5. **The milestone is archived here, on time — the fourth time.** Promote durable lessons out of the
   entries first — Traps.md, AR §18, the M6 ledger — then move them. **Current State's three growing
   rows get M4-07 rule 5's rolling treatment**, under PROGRESS's own 10 000-byte cap, measured rather
   than estimated.
6. **When every box is ticked: the owner merges `dev` → `main` and tags `m5`. Claude does not tag.**
7. **The grain is in the tag message**, M1-21's, M2-15's, M3-15's and M4-07's shape: *Editor
   evidence, device rows deferred* — **and multi-touch named**, so `m5` is never read later as a
   hardware sign-off for a feature no hardware has run. **A tag message is not a commit message** and
   the handover labels which is which.
8. **A structural miss becomes an M6 task or a ledger row, not a late edit to this branch.** A
   *number* can move here (rule 1); a mechanism cannot. The one exception is a **bug**, which becomes
   `M5-08a` with its own PR and its own review — and the three likeliest sources are named in the
   checklist above rather than left to be discovered.
9. **Flagged design contradictions are resolved here or carried with a reason.** M5's own: CH §3.2's
   Bone Bolt says 7 and the asset says 9 (M5-02 rule 2 — the doc is edited in that PR, so this is a
   confirmation); CH §5's *"8 (7 + 1 Keystone)"* slip, already corrected at M3-15; and CH §4's Active
   share against a tree with one Active in twelve ([M5-06b](M5-06b-gravecaller-tree-v1.md) rule 3).
10. **CH §8 q1 is annotated in this PR rather than left open.** M5-05a rule 5 answered the first of
    Characters.md's five open questions — *"do Wights count against the enemy concurrency cap?"* —
    with a ruling, a reason and a number, and a design doc that keeps asking a question the code has
    answered is how a later milestone re-decides it. The annotation names the answer, the part of the
    leaning that was **refused** (the device-tiered damage buff), and the milestone that owns the
    refused part (M8-03).
11. **Three splits happened in this milestone and the id-drift pass is owed.** M5-04, M5-05, M5-06
    and M5-07a all split, and the [parking lot](../ROADMAP.md#parking-lot) records that *"already-merged
    specs still point at pre-split task ids"* recurs on every split and that **the next milestone
    that splits owes the same pass to its acceptance**. This is that milestone and this is that pass:
    every `M5-04`, `M5-05`, `M5-06` and `M5-07a` reference in a merged spec, in PROGRESS and in the
    ROADMAP resolves to a file that exists.

## Acceptance

- [x] Checklist ticked; feel verdict recorded, with the Editor-versus-device grain stated
- [x] **Rows 2, 7 and 8 each carry a number or a sentence, not a shrug** — three playtest numbers or
  a written reason they carry a third time, a yes-or-no on the add that hit after the boss fell, and
  **a bar that moved or did not**. These three are the reason this task is not a formality
- [x] Every ledger row struck or carried, with the *As built* that closed it named, and **every
  carried row naming an unmerged owner** (rule 2)
- [x] `PROGRESS.md` updated, M5 entries archived, Current State pointing at **M6-00a**, and rule 5's
  rolling treatment applied under the measured 10 000-byte cap
- [x] Rule 11's id-drift pass done, and the parking-lot line updated with the fifth milestone it has
  now applied to
- [ ] `m5` exists on `main` — **the owner's to make**, with rule 7's message

## Out of scope

- **The UI redesign.** Blocked on M7's icons; the owner's standing ruling since the `m3` tag puts it
  in neither M4 nor M5. This task judges and hands forward.
- **Fixing what the feel verdict finds**, unless it is a number (rule 8).
- **Class unlocks, the Sanctum, and anything that spends a Shard.** M6-02 and M6-09.
- **Veilrot**, and the Rot branch becoming what its name says. M6-04.
- **Tether, Rot Nova, the three Keystones and the remaining fifteen nodes.** M5-06b rules 2 and 3;
  M7-04.
- **The Emberwright.** M6-07. Two classes is what M5 promised.

## As built

**The checklist was instrumented rather than watched, and that is the whole account of why this
acceptance closed rows the last three could not.** Rule 8's *"a bug found here becomes `M5-08a`"* is
the only part of the plan that ran as written; the rest was re-shaped the moment the first row was
attempted. **Row 8 asked for "one played minute" and got a false answer twice:** the owner reported
the Warden's bar as not moving, and every static reading of the damage path — `Health.ApplyDamage`,
`EnemySystem.ApplyDamage`, the cone resolve, `ProjectileSystem.Land`, `ProjectileLead` against a
standing target, `BossPhases`, the band's prefab `m_Type: 3` — said it was sound. What settled it was
asking the owner which of **two** readouts moved: the floating bar did and the band appeared not to.
Both draw `EnemyDamaged.HpFraction`; at 4 200 HP one hit moves the bar **0.29 %**, so the fight was
never broken and was only ever out of band. **A temporary `HudPresenter` probe then wrote two runs to
`Logs/`**, and the owner played an Oathbound to stage 16 and a Gravecaller to stage 19, three bosses
each. The probe was reverted before handover.

**Rule 1's retune, measured:** 4 200 → **3 200** on `Warden.asset`, with `WardenBehaviourTests` and
`BossBarViewTests` following because each file says it mirrors the asset. Six kills at the new value
ran **74, 89, 89, 94, 99 and 103–108 s** against GD §9.1 rule 5's 75–120 s. `GameDesign.md` needed no
number: §9.1 states the rule, and the asset was what violated it.

**Rule 9 resolved a contradiction the checklist did not list, and it had to be resolved first.**
GD §12.4's TTK invariant said *3–5 hits at every depth* while §12.5 says the power curves cross on
purpose; if they cross, TTK must exceed 5 eventually, so §12.4 forbade the thing that makes Descent
losable. **§12.4 is now scoped to *"up to the death horizon"*** and cross-references §12.5 — a
balance pass cannot run against a self-contradictory target, which is why the ruling preceded the
tuning rather than following it. The tuning is M8-05's.

**Three rows discharged by measurement, one answered yes, two findings.** [Row 2](../ROADMAP.md#carry-forward-into-m5):
the Oathbound kills a Husk in **4 hits flat from stage 4 to 16**, the Gravecaller drifts **4 → 8 by
stage 16**, breaking the band at stage 9 **with its entire sixteen-node tree taken** — its twelve
nodes carry one weapon-damage node, so damage grew 15 % against a Husk's 108 %; stage timings and an
interruption budget of 0.3–7 s a stage came with it. [Row 7](../ROADMAP.md#carry-forward-into-m5):
**yes** — `BOSS DEAD` and `STAGE 10 CLEARED` are 0.01 s apart and a hit landed **5.2 s** after the
boss fell, so M5-00a's *"nobody has observed it"* objection is spent and the fix is owed → M6.
[Row 8](../ROADMAP.md#carry-forward-into-m5): **a number**, discharged. GD §12.4's one-shot rule was
measured for the first time — **70 hits taken, none above 35 %** of max HP.

**Rule 8's bug, and it is the finding M5-06a rule 5 predicted.** `SplashFlow.Options` builds an
option per branch unconditionally while `RequireInstallable` runs only inside `Install`, so an
Oathbound borrowing the Gravecaller finds **two of three branches throw out of a `Button.onClick`** —
*Legion* (`RaiseMinions` unregistered) and *Rot* (**Restless Dead targets `Minions`**, filed in the
wrong branch). The guard is correct and atomic; the screen is the defect. It composes badly with
M5-07a-ii's deliberate *no way off it but through*: a dead button, no feedback, and the only escape
is quitting and pressing `Continue` — **rule 6's stated *cost*, the derive-on-resume, turns out to be
the escape hatch**, which nobody designed and nothing tells the player. →
[M5-08a](M5-08a-splash-offers-what-install-refuses.md).

**Rule 11's id-drift pass and rules 9–10's doc annotations merged ahead of this**, in PR #135 on the
same branch name; this footer covers the rest. **Rules 6 and 7 are the owner's** — the tag is theirs
to make, and `M5-08a` is unbuilt, so what `m5` marks is the owner's call. **The feel verdict is
deferred by the owner's ruling:** the three mechanics exist and were played for ~40 minutes, but
*"second class or reskin"* cannot be judged on capsules and placeholder VFX; re-taken at **M8-01**
with M7's content and art in. **Two probe defects are disclosed rather than buried:** the body census
double-decremented (`EnemyDied` *and* `EnemyDespawned`), so GD §11.3's 37-body question is **not**
answered; and `_probeBossDiedAt` never reset, so most row-7 lines were spurious and the two real
windows were verified by hand.
