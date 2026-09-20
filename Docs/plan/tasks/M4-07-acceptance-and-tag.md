# M4-07 — M4 acceptance: does a run have a wall to hit, and does dying pay? The ledger judged, and tag `m4`

**Size:** S · **Depends on:** everything in M4 · **Branch:** `m4-07-acceptance`
**Design refs:** GD §9.1, §9.2, §11.3, §14.1, §14.3, §16.2, §16.4; CH §4.1; AR §18; ADR-0004, ADR-0006, ADR-0007 · **Ledger rows:** **all seven — this is where [M4's carry-forward table](../ROADMAP.md#carry-forward-into-m4) is closed and M5's is opened.** Rows **1, 2, 3 and 4** are this task's own to rule on, and rows 1, 2 and 4 reach it having outlived the task that was supposed to own them

## Goal

Answer the M4 question on evidence — *does a run have a wall to hit, and does dying pay for having hit it?* — say out loud whether the HUD can be read, run the experiment row 4 has been waiting ten tasks for, and tag `m4`.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, the feel verdict, the row-4 numbers, Current State pointing at M5-00a (rule 3) |
| `Docs/plan/archive/PROGRESS-M4.md` | M4's log entries, moved — **on time, for the third milestone running** (rule 5) |
| `Docs/plan/ROADMAP.md` | M4 ticked; the M4 ledger closed and **carry-forward into M5** opened; M5's rows promoted from titles where this checklist demands it |
| `Docs/GameDesign.md` · `Docs/Characters.md` · `Docs/CoreCombat.md` | Only if tuning changed a shipped number, or a flagged contradiction is resolved (rules 1, 9) — **GD §14.1 is expected, see rule 10** |
| `Assets/_Project/Data/…` | Only if tuning changed — `WardenBoss.asset`, `Warden.asset`, `Descent.asset`, `English.asset` |

No code. **A bug found here becomes `M4-07a…` with its own PR** — the M2-15a and M3-15 precedent, and rule 8's one exception.

## Checklist (owner)

Ticked at two grains, and the entry says which is which: **[play]** rows are covered by the owner's playtest verdict rather than attested one at a time; unmarked rows are verified statically against the assets, the code and the suite. **[device]** rows cannot be answered in the Editor and are deferred (rule 7).

**A run has a wall** — the ROADMAP's *ends when* for M4, read literally

- [~] **[play]** Stage 5 is a boss stage, one body stands up, and no waves arrive with it (M4-01b rule 1)
- [x] Which stages hold a boss is **authored**, not computed — `Descent.asset` says *every 5th* in a field, and `Boss_StageRuleIsAuthoredNotHardcoded` drives an interval no shipped asset uses (M4-01b)
- [~] **[play]** Three phases at 1.0 / 0.66 / 0.33, an invulnerable beat at each threshold, and adds on the phases that author them (GD §9.1 rule 3, M4-01b)
- [x] The beat raises **both** `EnemyAgent.IsVulnerable` and `Health.SetExternalInvulnerable` — the half M4-01b's own spec missed and its *As built* caught
- [~] **[play]** GD §9.1 rule 5's **75–120 s** fight: the Warden's 4 200 HP against the shipped DPS is **103.7 s + two 1.5 s beats = 106.7 s**. A played run agreeing with the arithmetic, or not
- [~] **[play]** A shockwave anchored to the ground the boss stood on, cracks that do **not** track, and the ring's front edge biting rather than a band (M4-02 rule 5)
- [x] Capacity overflow **drops** rather than throws, on both hazards, and the behaviour pays the cooldown either way — the deliberate opposite of M3-11b's ninth zone (M4-02 rule 8)
- [x] `RunSession.Start` refuses a run naming an unauthored boss — the claim three files made before M4-02 made it true

**Ledger row 1 — the HUD, and the first place anybody judges it**

- [~] **[play]** **Can the HUD be read?** The arrangement M4-04 shipped and refused to judge: **0–4 dp** XP strip, **5–15 dp** boss band, **16 dp** HP row, two 1 dp gaps, on a ~432 dp landscape safe area. `Hud_StripAndBarDoNotOverlap` says they do not collide; nothing says they can be read
- [~] **[play]** **Do the boss bar's marks read as *"a transition is coming"*?** The shipped Warden's marks sit **7 px and 4 px** from even spacing on a 1080-wide safe area, so the element is doing its job only if a player can see *where* the next beat is — M4-04's own measurement, handed here
- [~] **[play]** **Is the run-end screen readable on its own terms?** Five labels and three numbers on a full-screen canvas (M4-06 rule 11). Unlike the band it collides with nothing; what it risks is being unreadable by itself
- [x] **Whatever the verdict, it is a sentence with a measurement behind it, not a shrug.** M3-15's precedent is binding: that task could not tick this row and said so with 44 dp, four cells and 226 dp of 432. **A row that cannot be closed is carried into M5's ledger with its own numbers**
- [x] Record whether the owner's asked-for redesign — every taken node shown, Active or Passive, an Auto skill marked by something orbiting it — is still blocked on M7's icons, and whether the sequencing **icons → readout → rotation** still holds

**Ledger row 2 — the three M3 numbers a playtest was supposed to produce**

- [~] **[play]** **Row 1's played hit counts** at stages 1 / 15 / 30 against the arithmetic's 3 / 4 / 5 (GD §12.4)
- [~] **[play]** **Row 8's two labelled timings** at stages 1, 5 and 10 — stopwatch **and** `RunState.Time`, and the gap between them **is** GD §13.1's interruption budget
- [~] **[play]** **Row 9's two-second read** on twelve real English descriptions (GD §13.1)
- [x] **These three sit behind row 1 by construction**, and the UI was ruled out of M4. **So the honest outcome may be that they carry a second time — and if they do, the reason is written down rather than implied.** Carrying a row twice is a decision; carrying it silently is the failure the ledger exists to prevent

**Ledger row 4 — `FrameOrderTests`, and the experiment that has gone unrun for ten tasks**

- [x] **Run M3-08b's experiment, at n ≥ 20 each side.** `FrameOrderTests` **alone**, against `BootSmokeTests` + `FrameOrderTests` **together** — because `BootSmokeTests` loads `Run.unity` in the same PlayMode process first, and *"something inside a single run that moves between rows"* is the only hypothesis left standing. The two filters, through `TestRunnerApi`:

  ```
  Soulvail.Tests.PlayMode.FrameOrderTests
  Soulvail.Tests.PlayMode.BootSmokeTests,Soulvail.Tests.PlayMode.FrameOrderTests
  ```

- [x] **Quote both rates against M3-08a's measured baseline of 5 failures in 50 isolated runs (10 %)**, and say what the difference means at that n. A clean sweep of eight is a ≈ 0.43 outcome at 10 % — M3-15 did that arithmetic and it is the standard this row is held to
- [x] **Say which row failed, every time**, and whether it is still the physics half rather than the ordering half — M2-15a's tell, which has held since
- [x] **If the experiment diagnoses something, the fix is `M4-07a` with its own PR** (rule 8). If it retires the hypothesis, the row carries into M5 **with a new next step**, not with the same one
- [x] `Run.unity` was changed again this milestone (M4-06 dresses a screen into it), which is a fifth data point for the lead and is recorded as such

**Ledger row 3 — the device debt, now eight questions and four milestones old**

- [~] `m4` is accepted on **Editor evidence**, like `m0`, `m1`, `m2` and `m3`, and the tag message says so (rule 7)
- [x] **Multi-touch is still the one row that risks a *feature*** — a thumb on the stick and a thumb on S3 at once. Every other row risks a verdict
- [x] The M0–M3 rows re-stated and carried: haptics and M1-20's 100 ms window, touch latency, real frame rate, thermal, landscape flip, sustained fps at the concurrency cap, **the Profiler's no-per-frame-`GC.Alloc` check unmet since M1-21**, `Physics.SyncTransforms()`' per-frame cost, **kill-from-recents mid-stage** — now with a second file behind it (M4-05b)
- [x] M4's own eight: (1) whether a 14 m ring and a 5 m crack read on a six-inch screen; (2) GD §11.3's fill-rate ceiling, reachable for the first time — ring, eight cracks, a zone and a spawn telegraph at once; (3) whether the arm-and-fire shape change reads as a shape change; (4) whether the beat shell says *not taking damage* or just hides the boss; (5) whether a 10 dp band, a 4 dp strip and a cutout coexist in the safe area; (6) whether the beat's dim reads as *"you cannot hurt it"* rather than as the HUD glitching; (7) whether five labels and three numbers read at thumb distance; (8) whether the run-end button clears a cutout on **both** rotations
- [x] **And one that is a finding rather than a question:** `M_TelegraphRing.mat` blends premultiplied without URP's `_ALPHAPREMULTIPLY_ON`, so `ZoneView`, `TelegraphRingView` and `BulwarkView` draw at full brightness whatever their alpha says. **Ruled here or carried with an owner** — it is one keyword on one material, and it has been the owner's call since M4-03

**Death pays** — GD §14.1

- [~] **[play]** Dying always pays something, at any depth, including stage 1 (GD §14.1)
- [x] The payout is **10 per stage + 50 per boss stage passed**, and the third term does **not** ship — `Payout_HasNoArchetypeTerm` is the row that pins it (M4-05a rule 6)
- [x] `ShardsAwarded` is published on the death path and **never** on a scope teardown — `Run_AwardsNothingWhenTheScopeIsTornDown` (M4-05a rule 1)
- [x] `PlayerProfile` is **v3**, the v2 → v3 step shipped in the same PR as the field, and the profile chain runs **two** steps for the first time (M4-05b, [ledger row 5](../ROADMAP.md#carry-forward-into-m4))
- [~] **[play]** Die twice; the total on disk is the sum, and haptics and the first-Active flag both survived the write (M4-05b rule 2)
- [x] **A resumed run under-pays its bosses, deliberately** (M4-05a rule 4). Confirm the size of it — at most 50 per boss killed before an app kill — and decide whether it becomes an M5 ledger row or stays a spec footnote
- [x] Shards buy nothing, and the record says so rather than implying it (M4-05b rule 8)

**Ledger rows closed by their owners — verified, not assumed** (rule 2)

- [x] **Row 5** — the format bump shipped with its migration and its fixture, in its own PR ([M4-05b](M4-05b-profile-v3-and-shard-writer.md)), and the reflection row at the new door holds
- [x] **Row 6** — three content-discipline leaks, plus the **fourth reader M4-05a rule 7 added on purpose** (`ShardPayout.PerStage` / `PerBoss` as `const`s). Either an owner or a carry with the count updated
- [x] **Row 7** — Current State was rotated by every task this milestone: task chain, *Verified* row, stamp, Milestone row. The one nothing checks, checked

**Performance and hygiene** (GD §11.1, AR §14)

- [ ] `AllocationAssert` rows green across every new `Tick` path — `BossBehaviour`, `WardenBehaviour`, `ShockwaveSystem`, `FissureSystem`, `ShardPayout`
- [~] **[play]** No hitch when a boss spawns, when a phase changes, or when the run-end screen opens
- [x] Six assemblies, zero compile errors, zero analyzer warnings, the full suite green through `TestRunnerApi` — **EditMode and PlayMode both, and PlayMode run more than once**
- [x] `dotnet format whitespace --folder --verify-no-changes` green over `Assets/_Project` — **live since M3-12c, so a failure is ours**
- [x] `git diff m3 HEAD -- ProjectSettings/` shows **nothing**, or one line committed on purpose with `ALLOW_PROJECT_SETTINGS=1` and named here. **`TimeManager.asset` has been found and reverted on six of the last ten tasks** and is expected again (M4-04's finding)

**The feel question** (record the answer and *why*): **does a run have a wall to hit?** Is the Warden a fight you learn, or a health bar with three pauses in it? If it does not land, which is wrong: the 4 200 HP, the 0.66 / 0.33 thresholds, the 1.5 s beat, the two hazards' telegraph windows, the add counts, or the fact that the player cannot read what any of it is doing. **And the second question this milestone adds: does dying pay enough to want to go again**, when what it pays cannot yet be spent?

## Behaviour

1. **Tuning edits go to the ScriptableObjects and the named constants only**, and GD, CH and CC are updated in the same PR. Doc and asset never disagree — M2-15 rule 1, which is also what licensed that task to fix two GD numbers nothing had tuned.
2. **The ledger is closed row by row, and a row is only closed by its owner's *As built*.** Seven rows; for each, either the owning task's footer says it is answered — in which case it is struck — or it is carried into M5's table with the task that will answer it. **A row silently dropped is the failure the ledger exists to prevent**, and this milestone is the one that proved the mechanism can fail the other way: **three rows named M4-00a as their owner and that task merged without discharging any of them**, which is why rule 11 exists.
3. **The M5 ledger is opened in the same pass**, from three sources: rows carried forward, parking-lot entries that have acquired an owner, and this checklist's own misses. M5-00a's specs are written against it.
4. **The PROGRESS entry lists what M5 must know.** Especially: whether `SkillRunner`'s twelve-Active cap and `TimedEffects`' sixteen-entry capacity held under a boss with adds and phases — the thing M3-15 said would be the first to press them; whether `IStatBlock` and `CombatantStats` are the shape a second class's minions want; and the seven `TriggerField` members M4-01b refused to define for an enemy, which M7's Archon is the first caller of.
5. **The milestone is archived here, on time — the third time, which is what turns a rule into a habit.** Promote durable lessons out of the entries first — Traps.md, AR §18, the M5 ledger — then move them. **And Current State's own growth is now the problem**: PROGRESS is ~230 KB against a 256 KB read limit with an *empty* log, because *What works*, *What does not work yet* and *Known issues* only ever get longer. **This task gives those three rows the rolling treatment the task chain already has**, or says why not.
6. **When every box is ticked: the owner merges `dev` → `main` and tags `m4`. Claude does not tag.**
7. **The grain is in the tag message**, M1-21's, M2-15's and M3-15's shape: *Editor evidence, device rows deferred* — **and multi-touch named**, so `m4` is never read later as a hardware sign-off for a feature no hardware has run. **A tag message is not a commit message** and the handover labels which is which.
8. **A structural miss becomes an M5 task or a ledger row, not a late edit to this branch.** A *number* can move here (rule 1); a mechanism cannot. The one exception is a **bug**, which becomes `M4-07a` with its own PR and its own review — and row 4's experiment is the likeliest source of one.
9. **Flagged design contradictions are resolved here or carried with a reason.** The standing one is the owner's retune: the Oathbound is 3 m/s while GD §6.1's 5.4–6.2 band and CH §3's `140 / 5.4` row still carry the old numbers. **It stays parked until M5-02**, which is the first task that cannot avoid it — the owner's standing ruling, re-stated rather than re-opened.
10. **GD §14.1 is annotated in this PR rather than left to disagree with the code.** The shipped formula is two terms of three ([M4-05a](M4-05a-shard-payout.md) rule 6), and a design doc that keeps claiming three is how a later milestone re-discovers a decision. The annotation names the term, the reason it waits, and the milestone that owns it.
11. **A ledger row may not be left pointing at a task that has already merged.** M4-00a owned rows 1, 2 and 4 and discharged none, and nothing in the protocol noticed for five tasks. **Every row this task carries names an *unmerged* owner or says explicitly that it has none and why** — and *"the next acceptance"* is a legitimate owner only when the row is a verdict rather than a fix.

## Acceptance

- [~] Checklist ticked; feel verdict recorded, with the Editor-versus-device grain stated as M1-21's, M2-15's and M3-15's were
- [x] **Rows 1, 2 and 4 each carry a number or a sentence, not a shrug** — a readability verdict with a measurement behind it, the three playtest numbers or a written reason they carry again, and two failure rates from row 4's experiment. **These three are the reason this task is not a formality**, and all three arrive having already survived one acceptance
- [x] Every ledger row struck or carried, with the *As built* that closed it named, and **every carried row naming an unmerged owner** (rule 11)
- [x] `PROGRESS.md` updated, M4 entries archived, Current State pointing at **M5-00a**, and rule 5's rolling treatment applied to the three growing rows or refused with a reason
- [~] `m4` exists on `main` — **the owner's to make**, with rule 7's message

## Out of scope

- **The UI redesign.** The owner ruled it out of M4 immediately after `m3` was tagged. This task **judges** the HUD and hands the fix forward; it does not do it, and a tuning session on `_cellHeightDp` is the only edit rule 1 licenses.
- **GD §14.1's third term**, the archetype first-encounter set, and anything that spends a Shard — M4-05a rule 6, and M6-02 / M6-09.
- **A second boss.** The Choirmother is M7's; GD §9 lists two and M4 always owned one.
- **Elites and affixes** — M7-02. `EnemySpawned.IsElite` is still `0` on every shipped archetype.
- **Fixing what the feel verdict finds**, unless it is a number (rule 8).
- **The second class** — M5. One boss and a payout is what M4 promised.

## As built

**No code, no tuning, no asset touched.** Marks read `[x]` verified statically, `[~]` the owner's — every **[play]** row, the feel verdict, the tag — and `[ ]` **not met and carried**, of which there is one.

**Deviation 1 — M4's ROADMAP tables and ledger were archived to [ROADMAP-M4.md](../archive/ROADMAP-M4.md), which the Files table does not list.** The table names `ROADMAP.md` and says *"the M4 ledger closed"*; closing it in place leaves a 180-line table live in the file every session opens first, which is the state the 2026-09-20 docs PR created `archive/` to end. M0–M3 each have a stub heading plus an archive, so M4 got the same — **at its own acceptance rather than in a later sweep.** The stub and the *Carry-forward into M4* pointer stay, and every link into them was checked.

**Deviation 2 — `Docs/Traps.md` took three findings** (§4, §9, §11), on rule 5's instruction to promote durable lessons before moving the entries.

**Row 1, answered with arithmetic because the Editor cannot answer it.** `Screen.dpi` reads **120** here, so `StickShaper.PixelsPerDp` is **0.75** against a 400 dpi phone's **2.5** — every dp-sized element ever looked at in this Editor was drawn at **30 %** of device size. A *font* size is not a dp at all: both `Hud.prefab` and `RunEnd.prefab` scale with a `1920 × 1080` reference at match 0.5, so a point size is reference px and moves with the canvas while the element under it moves with the density. **Two units on one object, which is why nobody caught it.** At the project's own reference density — 1080 px short edge at 2.5 px/dp, the source of its *"~432 dp landscape safe area"* — on 2340 × 1080 (scale 1.104): run-end title **42.4 dp**, Shard figure **28.3**, its five labels and two figures **21.2**, exit hint **14.1**; HUD level **17.7**, HP readout **15.9**, Overflow toast **15.0**; **S1–S4's labels 7.9**. Android's body-text floor is 12 sp and Material's comfortable figure 14 sp, so **seven of eight clear the floor and the eighth sits at 66 % of it**, inside a 60 dp circle — and it is the one [M3-14a](M3-14a-localizer-and-english-table.md) put a *word* on because M3-10a rule 9's icons do not exist. **So the verdict is not *"the UI cannot be read"* but *"one element cannot, and it is one `m_fontSize` on four objects"*.** Not changed here: [Out of scope](#out-of-scope) licenses one tuning edit and it is `_cellHeightDp`. **The boss bar:** marks **2 dp** wide, the authored 0.66 / 0.33 sitting **15.6 px and 7.8 px** from even spacing on a 2340 px band — 3.1 and 1.6 mark-widths, M4-04's *"three seam-widths and one"* at real width. The redesign is untouched, still blocked on M7's icons, sequencing unchanged.

**Row 2 carries a second time, and the written reason is harder than the predicted one.** M4-00b expected it to carry for want of a *fixed* UI. What is true is that **nobody has played this build to stage 5** — M4-03, M4-04 and M4-06 each said so — so there is no run to take a number from. **One of the three was never blocked by legibility**: hit counts are counted in the arena, not read off the HUD, and are owed to the first playtest of any build.

**Row 4 ran, and the result is the opposite of the hypothesis rather than a null.** `FrameOrderTests` alone, n = 20 (after 3 green pilot runs): **4 failures, 20 %**. `BootSmokeTests` + `FrameOrderTests`, n = 20: **1, 5 %** — plus three runs red on `Frame_PausedTicksCostNoSimulatedTime` carrying the Unity AI Assistant's `NoSubscription` log, environmental and not this row. Against M3-08a's 10 % baseline both are ordinary: P(X ≥ 4) = **0.133**, P(X ≤ 1) = **0.392**; between the sides, Fisher's exact two-tailed **p ≈ 0.34**. **Running `BootSmokeTests` first made the row fail *less***, so M3-08b's hypothesis is **retired**, not merely unproven. Every failure was `Ticker_RunsTheStepsInOrder` on M2-15a's message, and the tell is sharper than before: **assertion 3, which reads the body's `Transform`, passes on the frame assertion 5's physics sweep fails.** The body moved; the query did not see it. **No bug, so no `M4-07a`.** The new next step is to instrument rather than re-run: on failure, re-issue the same cone query in the same frame — finding the body means the sync is late, not finding it means the wedge is misplaced.

**Row 6's count was wrong; every site was re-checked rather than quoted forward.** (i) is **two** — `LevelUpFlow.OverflowDamage` *and* `OverflowMaxHp`. (ii) `TimeToKillTests` still passes `overflowLevels: 7` at stage 15. (iii) `Boot.unity` still carries `m_text: Soulvail`. (iv) is **struck**: `ShardPayout`'s two are correct as built, no asset exists for a payout until M6-02, and the parking lot now says so. Five across four sites.

**The one row not met.** `BossBehaviour.Tick` has no `AllocationAssert` row, where `BossPhases`, `WardenBehaviour`, `ShockwaveSystem`, `FissureSystem` and `ShardPayout` all do — the wrapper that clears adds, raises the beat and summons a phase's adds is measured only through its collaborator and its `inner`. [M5 ledger row 6](../ROADMAP.md#carry-forward-into-m5).

**Two rulings the checklist asked for.** The resumed run's **under-payment stays a spec footnote**: bounded at 50 per boss, asserted both ways at M4-05a, reachable only by an app kill between a boss dying and the player dying. **The premultiply finding carries with an owner** — M5-00a, whose projectile class inherits all three affected views unchanged.

**One observation about row 7's mechanism** — `.githooks/pre-commit` check 8 cannot see an acceptance commit, because it skips when the Log is empty → [Traps §11](../../Traps.md).

**Verified:** 2 197 EditMode / 0 / 0 ×3; PlayMode 16 / 0 / 0 ×3; 63 further PlayMode runs for row 4; Console 20 errors / 50 warnings, all deliberate, zero `CS`/`UNT`/`IDE`; `dotnet format` exit 0; six assemblies; `git diff m3 HEAD -- ProjectSettings/` empty, `TimeManager.asset` reverted. **`m4` is the owner's to tag.**
