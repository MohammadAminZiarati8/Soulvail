# M2-15 — M2 acceptance: the stage loop end to end, the ledger closed, tag `m2`

**Size:** S · **Depends on:** everything in M2 · **Branch:** `m2-15-acceptance`
**Design refs:** GD §7.1–7.3, §8.1–8.2, §11.1, §12.1–12.5, §16.3; AR §18; ADR-0007, ADR-0011 · **Ledger rows:** all fourteen — this is where the [carry-forward table](../ROADMAP.md#carry-forward-into-m2) is closed and M3's is opened

## Goal

Answer the M2 question on evidence — *is descending, stage after stage, worth doing for ten minutes?* — prove that every ledger row left the table, and tag `m2`.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, the feel verdict, tuned numbers, Current State pointing at M3-00a |
| `Docs/plan/archive/PROGRESS-M2.md` | M2's log entries, moved — on time this once (rule 5) |
| `Docs/plan/ROADMAP.md` | M2 ticked; the M2 ledger closed and **carry-forward into M3** opened; M3 rows promoted from titles |
| `Docs/GameDesign.md` · `Docs/CoreCombat.md` | Only if tuning changed a shipped number — doc and asset never disagree |
| `Assets/_Project/Data/…` | Only if tuning changed |

No code. A bug found here becomes `M2-15a…` with its own PR.

## Checklist (owner)

Ticked at two grains, and the entry says which is which: **[play]** rows are covered by the owner's playtest verdict rather than attested one at a time; unmarked rows are verified statically against the assets, the code and the suite. **[device]** rows cannot be answered in the Editor and are deferred to the first session with a phone.

**The loop** — the ROADMAP's *ends when* for M2, read literally

- [x] **[play]** A run goes Boot → Menu → Descend → stage 1 → door → stage 2 → … without a seam that reads as a load
- [x] **[play]** Arrival seals and announces; nothing spawns during it. Clear drops the barrier and opens the door. The gate never times out
- [x] **[play]** Walking into the door is the only way onward, and the next room is never visible before you are in it
- [x] Descent is an asset, not an assumption — `Data/Modes/Descent.asset` carries the roster and the schedule, and no code says "endless" or "stage 1" (GD §4.5)
- [x] **[play]** Waves overlap at 25 % remaining; the "chase the last enemy" gap is gone (GD §7.3)
- [x] **[play]** All three archetypes are tellable apart at a glance, and each is doing its own job — Husk closes, Spitter kites and will not shoot through a pillar, Bloater makes you walk away (GD §8.1)
- [x] Introduction schedule holds: Husk from 1, Spitter from 2, Bloater from 4, one new thing at a time (GD §8.2)
- [x] **[play]** Every spawn telegraphs 0.8 s before it exists, and nothing arrives within 6 m of you or on top of another spawn
- [x] **[play]** A threat you cannot see has an edge arrow; a focus you tapped out of range reads as *held*, not as a miss — the CC §8 row M1 could not tick

**Difficulty, at depth rather than at stage 1** (GD §12)

- [x] Threat budget matches GD §12.1's table at stages 1, 10, 20, 30 and 40 — **check the arithmetic against the formula, not against the table**, which was wrong by 105 at stage 40 when M2-00b checked it (parking lot)
- [x] One-shot rule: no single hit exceeds 35 % of max HP, at stage 1 **and** at stage 30 (GD §12.4)
- [x] TTK invariant: a basic enemy still dies in 3–5 hits at stage 30 with an unlevelled class — or the miss is recorded as the input M3's tree must close (GD §6.2, §12.3)
  > **Missed, and recorded** — the row's second clause. Husk 36 HP against weapon 13: **3 hits at stage 1, 5 at stage 14, 6 at stage 15, 8 at stage 30**; with the Focus ramp maxed (×1.3 → 16.9) the band holds to stage 23 and is still 6 hits at 30. The invariant breaks at **15**, not 30. `h(n)` is exactly as GD §12.3 specifies and the assets match it — the gap is that no player power exists yet. [M3 ledger row 1](../ROADMAP.md#carry-forward-into-m3).
- [x] Concurrency never exceeds the cap, and the cap is the one M2-04 priced against the pathfinder rather than the one GD §12.2 wants
- [ ] **[play]** Stage length lands in GD §7.3's 40–75 s band at stages 1, 5 and 10 — timed, not estimated
  > **Not measured.** The playtest returned a verdict on the descent as a whole, not three stopwatch readings, and this row asks for the readings. Accepted by feel, **not verified** — recorded rather than ticked, because inventing three numbers is worse than carrying the gap. [M3 ledger row 8](../ROADMAP.md#carry-forward-into-m3), which also says why it gets harder to answer after M3's level-up screen starts interrupting a stage.

**Persistence** (GD §7.3, ADR-0007)

- [x] `run.json` exists from the run's first frame and says the stage the player is about to play
- [x] It is rewritten at every boundary and deleted on death
- [x] **[play]** `Continue` appears only when there is a run, and resuming lands in the same arena with the same waves and the health the player had
- [x] A corrupt or unknown-version save does not stop the app launching — it is deleted and the button is simply absent
- [x] The v1 fixtures still decode, and the migration chain test is green (AR §11.6)
- [x] Haptics persist through `ISaveStore`, not `PlayerPrefs`, and no `PrefsKey` remains
- [ ] **[device]** Kill from recents mid-run and relaunch: `Continue` restores the stage
- [ ] **[device]** An incoming call mid-stage — GD §7.3's actual scenario, and the reason the whole feature exists

**Performance and hygiene** (GD §11.1, AR §14)

- [x] **[play]** No hitch at a stage boundary, where an arena is instantiated behind a 0.3 s fade
- [x] `AllocationAssert` rows green across every `Tick` path — director, flow, projectiles, recorder
- [ ] **[device]** Sustained frame rate at the concurrency cap with three archetypes and projectiles in the air
- [ ] **[device]** Profiler shows no per-frame `GC.Alloc` in a 60 s session — **carried unmet from M1-21**, where the unfocused Editor made it unrunnable (Traps §3)
- [ ] Six assemblies, zero errors, zero analyzer warnings, the full suite green through `TestRunnerApi`
  > **EditMode yes, PlayMode qualified.** Six assemblies; zero compile errors; zero analyzer warnings; **EditMode 1077 / 0 / 0, run twice** (14.8 s, 13.1 s); every Console error and warning an expected log from a passing negative-path test. **PlayMode gave 9/11 then 11/11 on one unchanged copy of `dev`** — the two failures always `Ticker_ReportsFactsAfterBodiesMoved` and `Ticker_RunsTheStepsInOrder`, always on the `ConeReport` assertion. Left unticked because "the full suite green" was not true on the first run, and an intermittent ordering failure should not be rounded up. [M3 ledger row 3](../ROADMAP.md#carry-forward-into-m3).
- [ ] `git diff ProjectSettings/` shows the `Cover` layer (M2-11a rule 9) and **nothing else**
  > **Fails by one line.** `git diff m1 HEAD -- ProjectSettings/` shows the `Cover` layer as expected, **plus** `URPProjectSettings.asset` gaining `m_ProjectSettingFolderPath: URPDefaultResources` — Unity backfilling a default, the [Traps §5](../../Traps.md) behaviour this row exists to catch. Committed on `m2-art-character-import`, the one M2 branch with no spec and no session protocol behind it. The line is harmless; **the gap in the guard is the finding**. [M3 ledger row 7](../ROADMAP.md#carry-forward-into-m3).

**The feel question** (record the answer and *why*): descending for ten minutes with no tree and no boss — is the stage loop worth repeating? If not, which number is wrong: the budget curve, the wave count, the concurrency cap, the arrival and clear beats, the telegraph, or the archetype mix?

> **Answered: yes — "good for now."** The owner played the descent and passed it as a whole rather than row by row, which is the grain this checklist's **[play]** rows are written for. **No number was changed**, and that is the substance of the answer: the budget curve, the wave count, the concurrency cap, the arrival and clear beats, the telegraph timing and the archetype mix were all left exactly as authored, so none of the six candidates this question lists was found wrong. **On Editor evidence**, like `m0`'s and `m1`'s verdicts before it — the loop has never been played on a phone, and the pacing questions that only a device can settle are listed in PROGRESS → *Deferred — device-only*. The honest caveat is the one row above: the 40–75 s band was accepted by feel rather than timed, so "the commute is the right length" is the part of this yes that rests on impression rather than measurement.

## Behaviour

1. Tuning edits go to the ScriptableObjects and the named constants only; GD §7.1, §8.1 and §12's numbers are updated in the same PR. Doc and asset never disagree.
2. **The ledger is closed row by row, and a row is only closed by its owner's *As built*.** Fourteen rows, each naming a task; for each, either the task's footer says it is answered — in which case the row is struck — or it is carried into M3's table with the task that will answer it. A row silently dropped is the failure mode the ledger was built to prevent (M2-00a).
3. **The M3 ledger is opened in the same pass**, from three sources: rows carried forward, the parking lot's entries that now have an owner, and whatever this checklist's misses produce. M3-00a will write M3's specs against it, which is the rule M2-00a's *Follow-ups* established.
4. The PROGRESS entry lists what M3 must know: anything about the director, the flow, the snapshot format or the streams that turned out different from the specs — especially **any field M3's tree will have to add to `RunSnapshot`**, since that is the first real migration and M2-13b's chain test is what will catch it being skipped.
5. **The milestone is archived here, on time.** M0's and M1's logs were archived by M2-00a, one milestone late and at 381 KB; PROGRESS's *"at the end of a milestone"* rule is followed at the end of this one. Promote durable lessons out of the entries first — Traps.md, AR §18, the M3 ledger — then move them.
6. When every box is ticked: the owner merges `dev` → `main` and tags `m2`. **Claude does not tag.**

## Acceptance

- [x] Checklist ticked; feel verdict recorded, with the Editor-versus-device grain stated as M1-21's was
- [x] Every ledger row struck or carried, with the *As built* that closed it named
- [x] `PROGRESS.md` updated, M2 entries archived, Current State pointing at **M3-00a**
- [ ] `m2` exists on `main` — **the owner's to make.** The tag message says *Editor evidence, device rows deferred*, so it is not read later as a hardware sign-off

## Out of scope

- **Levelling, XP, the tree, offers** — M3. M2 exists so that this milestone stays about the *loop*: an unlevelled class descending is the honest test of whether the stage structure carries itself.
- **The first boss** — M4. Every fifth stage being a boss (GD §7.1) is not part of this checklist and no stage 5 special-case is added to make it look closer.
- **The Sanctum** — M6. GD §7.1 puts it between Clear and Gate; M2-10 rule 7 left `Clear` a state rather than an instant so it can land there without a rewrite.
- **Fixing what the feel verdict finds**, unless it is a number. A structural miss becomes an M3 task or a ledger row, not a late edit to this branch.

## As built

**Files: as specified, minus two.** `PROGRESS.md` (rewritten Current State, log emptied), `archive/PROGRESS-M2.md` (new, 27 entries moved with their link depths rewritten), `ROADMAP.md` (M2 ticked and given a status block, ledger rows 12 and 14 struck, the M2 ledger closed, **carry-forward into M3 opened with eight rows**, M3-00a…M3-00d added, three parking-lot entries graduated), and `GameDesign.md`. **`Docs/CoreCombat.md` and `Assets/_Project/Data/…` were not touched**: no tuning number changed, which is what the Files table made them conditional on. Five task specs were edited that the table does not list — see deviation 2.

**Two deviations, neither changing a decision.**

1. **The stage-length band was not measured.** The checklist asks for stages 1, 5 and 10 *timed, not estimated*, and the playtest returned a verdict on the descent as a whole rather than three stopwatch readings. The row is therefore **accepted by feel, not verified**, and it is recorded that way rather than ticked — inventing three numbers would have been worse than carrying the gap. **Filed as [M3 ledger row 8](../ROADMAP.md#carry-forward-into-m3)** with the reason it gets harder rather than easier to answer later: M3's level-up screen interrupts a stage, so the number after M3 does not describe the same thing as the number before it.

2. **Two `GameDesign.md` numbers were corrected, and five task specs were edited, under clauses that arguably did not cover them.** The Files table makes `GameDesign.md` conditional on *"only if tuning changed a shipped number"*, and nothing was tuned. But behaviour rule 1 says **doc and asset never disagree**, and GD §12.1's stage-40 row (1,772 against the formula's 1,876.9) and the "44×" beneath it (really 46.9×) were the last copies still disagreeing with a formula the code has pinned in three places since M2-03. Corrected to **1,877** and **47×**. The five spec edits are the parking lot's *"already-merged specs still point at pre-split task ids"* entry, which names M2-15 as the task that should do it "because it is already reading every spec's *As built*" — that prediction held, so it was done: fourteen references across M2-01, M2-02, M2-05, M2-07a and M2-10 now name the task that shipped the work.

**The ledger closed row by row, and every row was closed by its owner's *As built*** (rule 2). Twelve were already struck on arrival; **rows 12 and 14 were verifications rather than builds**, since M2-12a shipped both indicators and what they were owed was a playtest saying they read. **Nothing was carried into M3** — the first milestone ledger to close empty.

**The M3 ledger was opened in the same pass** (rule 3), from the three sources the rule names. Nothing came from source one, because nothing was carried. Two came from the parking lot acquiring owners (`PlayerAnimatorView`'s missing tests → M3-11; GD §16.4's palette → M3-13). **Five came from this checklist**, which is the mechanism working as intended: the TTK gap at stage 15, the frame-order intermittency now proven not to be branch-local, the `ProjectSettings` guard's gap on non-task branches, the three-milestone device debt, and the unmeasured stage band. **The eighth is rule 4's** — the `RunSnapshot` field M3's tree must add, and the migration that has to ship in the same PR as it.

**Three things the spec did not say and this task had to decide.**

- **What a blanket playtest verdict licenses.** The owner played and said *good for now*, which the spec anticipates — **[play]** rows are *"covered by the owner's playtest verdict rather than attested one at a time"*. So the rows are ticked at that grain and the entry says so. The one row this does **not** license is the timed one, because a stopwatch reading is not a verdict; hence deviation 1.
- **Whether the TTK miss is a tuning failure or an input.** The checklist offers both readings (*"or the miss is recorded as the input M3's tree must close"*). It is the second: `h(n)` is behaving exactly as GD §12.3 specifies, the assets match the design, and the gap is the absence of player power — which is the definition of the next milestone. Tuning HP down to hit the band would have made M3's tree push it straight back out.
- **How much of `PROGRESS.md`'s "What works" to keep.** It had grown to a single paragraph covering every merged task in three milestones, and the archives now hold that task by task. It was cut to the shape of the game and given a companion row, **"What does not work yet"** — because a state block that only lists what exists reads, at the start of a milestone, as if the game were further along than it is.

**Verification.** 1077 EditMode passed / 0 failed / 0 skipped, run twice (14.8 s, 13.1 s) on a tree identical to `dev`; six assemblies; zero compile errors; zero analyzer warnings; every Console error and warning an expected log from a passing negative-path test. PlayMode **9/11 then 11/11** on the same unchanged copy. `TestRunnerApi` was reached through [Traps §4](../../Traps.md)'s documented workaround — a temporary runner inside `Soulvail.Tests.Core`, invoked reflectively, **deleted before handover** — which is the first time that entry has been used as written rather than rediscovered.
