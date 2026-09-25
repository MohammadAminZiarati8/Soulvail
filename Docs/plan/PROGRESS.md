# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

**Closed milestones are archived, not deleted:** [M0](archive/PROGRESS-M0.md) · [M1](archive/PROGRESS-M1.md) · [M2](archive/PROGRESS-M2.md) · [M3](archive/PROGRESS-M3.md) · [M4](archive/PROGRESS-M4.md) · [M5](archive/PROGRESS-M5.md) · [M6](archive/PROGRESS-M6.md). The log below covers the milestone in progress only. This file used to be 381 KB and could not be opened whole by any tool; keeping it to one milestone is what stops that happening again — **M2 was the first milestone archived on time, and M6 is the fifth running, which is what turns a rule into a habit.**

**Current State is a brief, not a second log, and it is capped.** After M3-15 this file was 228 KB with an *empty* Log; by M4-05b it was 298 KB, and 256 KB of that was Current State — thirty-four *Verified* rows back to M3-00b, an eight-deep task chain, and four rows that were appended to for a whole milestone and never rewritten, one of them still opening with a sentence about M3-06. On 2026-09-20 the block was cut to under 10 KB: M3's *Verified* rows went to the [M3 archive](archive/PROGRESS-M3.md#verification-chain), M4's were folded into their Log entries as a **Verified** line, and every other row was rewritten as the present with links to the history. The rules that keep it there are under [How to write an entry](#how-to-write-an-entry) — two on 2026-09-20, and a third at M4-07, which bounds the three rows that only ever grew.

---

## Current State

_Updated: 2026-09-25, as of RS-03b's merge — core can give a class a volley, and none of the three has one. M7's third spec group is written, two to go, nothing of M7 built._

| | |
|---|---|
| **Milestone** | **M7 — Content pass. Specs: groups 1 to 3 of 5 written ([M7-00a to M7-00c](ROADMAP.md#m7--content-pass)); no build task starts until all five are, and the fifth waits on the owner's audio ruling. Art was ruled on 2026-09-25.** M0–M5 are tagged `m0`–`m5` — **`m4` included, since 2026-09-20**, which this row wrongly called pending for two milestones. **M6 is closed and [archived](archive/PROGRESS-M6.md)** — 22 tasks, accepted on Editor evidence and five played runs to stage 39; **`m6` is the owner's to tag** — both bugs ruled to go before it, [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md) and [M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md), are merged. |
| **Last merged task** | **[RS-03b](tasks/RS-03b-the-volley.md) — the volley.** After `Volley.Every` ordinary shots the next is a fan at the target: one tell, up to seven arrows, each at ×`Damage`. `Every` starts at 0, so a node gives a class its volley. The player's shots queue and the run fires them all on one tick. `Start` refuses a class's own tree that names a stat the class lacks. |
| **Next task** | **M7-00d** — specs for the eighty-one nodes — on M7's track, and **RS-03c** (the Ranger joins the roster) on the Ranger's, then RS-03d; the two tracks do not depend on each other. **After the Ranger's kit, the owner designs the second new class.** **The `m6` tag is still the owner's.** **One ruling is the owner's before M7-00e: where the audio comes from.** Art was ruled on 2026-09-25: the KayKit packs in the project, plus Gemini-made models skinned onto `Rig_Medium`, with weapons as separate props. |
| **Verified** | **RS-03b: 3 251 EditMode — 3 250 passed / 0 failed / 1 inconclusive — and PlayMode 55 / 0 / 0**, run through `TestRunnerApi` because the CLI's `run_tests` hung at RS-03a ([Traps §3](../Traps.md)). **A save-store row can fail a full pass on a Windows file lock; re-run it alone** ([Traps §7](../Traps.md)). The inconclusive row is by construction: one of the two animator clock rows always is, and which one depends on the Editor's clock ([Traps §3](../Traps.md)). **`RangerSandboxTests.Loop_ShootsOnlyStandingStill` can fail on unchanged code, alone as well as in a pass; bisect against `dev`** ([Traps §8](../Traps.md)). **A PlayMode pass also overwrites the real `run.json`** ([parking lot](ROADMAP.md#parking-lot)). The chain of counts is each Log entry's **Verified** line ([M6's](archive/PROGRESS-M6.md)). **The played numbers — hits to kill at seven depths for three classes, bodies, frame times, the economy, the Claiming — are [M6-11's *As built*](tasks/M6-11-acceptance-and-tag.md#as-built).** |
| **What works** | **A run is a descent that reaches a boss, kills it, pays for dying, and survives the app closing**, and three classes walk it — each with a twelve-node tree and its own way with the Veil. Boot → Menu → a card per class, a locked one priced in Shards → arenas sealed by threat-budgeted, telegraphed waves → a door → one stage deeper, a boss every fifth. **Levelling:** a kill is experience, a threshold is a pick of three, a full tree pays Overflow, and at half a tree a run borrows one branch of another class. **The Warden** dies in 89–113 s to stage 30. **Death pays** Shards into `PlayerProfile` v4, and class select spends them. **Every stage clear pays Essence**, and a Sanctum after it sells a reroll, a banish, 30 HP and −15 Veilrot, each priced and refused with a reason. **Veilrot** fills from Pacts through four thresholds to the Claiming; **a Pact** is an authored card with a Rot price; **Ordeals** are dealt from stage 25, one dial each. **The HUD** draws both economies. **Saves:** `run.json` and `profile.json` are v4, every earlier version still loads, Continue offers the run last written — in one session as across a relaunch — and a Claimed run resumes Claimed. **Every string is a `LocKey`**, read in English or a generated pseudo-locale. **A run wears the body its class names**, the Knight for all three, **and flies the shot its class names**, the default bolt for all three. **Beside the run, a Ranger sandbox** ([RS-02a](tasks/RS-02a-a-playable-ranger.md)) plays a bow on the game's stick and camera. Milestone by milestone: the [archives](archive/). |
| **What does not work yet** | *(1)* **Hits to kill leave GD §12.4's band** — the Gravecaller over it from stage 9, the Emberwright under it to 9 — and the Warden leaves §9.1's near 33: [M7 row 2](ROADMAP.md#carry-forward-into-m7), M8-05. *(2)* **The Sanctum plays as zero**: nothing bought unprompted in two natural runs — [row 9](ROADMAP.md#carry-forward-into-m7). *(3)* **A Pact is one offer in 62** — [row 10](ROADMAP.md#carry-forward-into-m7), M7-04. *(4)* **Past stage 26 the late waves are all Bloaters** — [row 11](ROADMAP.md#carry-forward-into-m7), diagnosed and specced as M7-02b. *(5)* **The feel verdict is deferred to M8-01** by the owner's ruling, and no second language ships ([parking lot](ROADMAP.md#parking-lot)). *(6)* **A boss cannot run a `SkillRunner`, and the arena only reacts**; not saved, by ruling: cooldowns, shields, live zones. *(7)* **Unbuilt by design:** no audio, no Elites, one boss, two arenas; Tether, Rot Nova and the Keystones are M7-04's. |
| **Known issues** | *(1)* **Every "ring" is a square**: `VFX_TelegraphRing.prefab` is an untextured Quad; one circle texture fixes all five decals; art, M7. *(2)* **`ArenaView.OnValidate` warns "0 spawn point(s)" for both arena prefabs on every domain reload**; they author 8 and 7, and reload-time `Transform` references are fake-null ([Traps §1](../Traps.md)). *(3)* **`PlayerStat` lies by name** — it carries `ContactDamage`, which `PlayerStats.Resolve` refuses; the owner's ruling, owed since M4-01a. *(4)* **M3-14c's nine labels were never witnessed in Play.** *(5)* **Environmental:** a PlayMode row can fail on Unity's AI Assistant (*NoSubscription*); `ProjectSettings/TimeManager.asset` re-serialises whenever the Editor touches the project, so revert it before every handover ([Traps §5](../Traps.md)); the disk sits near full. |
| **Reference device** | **None, and the emulator is not a fallback.** BlueStacks 5 **installs the APK and crashes on launch** in its own Vulkan driver, not game code. **Nothing outside the Editor has ever run.** [M0-20a](tasks/M0-20a-apk-runs-on-bluestacks.md) owns it; a phone removes the question. **And the Editor is not a stand-in for layout either:** `Screen.dpi` reads **120** here against a phone's 400, so every dp-sized element ever looked at was drawn at **30 %** of its device size ([Traps §9](../Traps.md)). |
| **Deferred — device-only** | **All of it is [M7 row 1](ROADMAP.md#carry-forward-into-m7)**, six tags deep with nothing run outside the Editor. **Multi-touch risks a *feature*** — 80 of 84 casts in M5-08's runs were automatic — and **kill-from-recents is the one correctness row**. M6-11 adds four Editor numbers a phone must re-take: bodies under Swarm, frame time at depth, the ~50 ms frame every screen costs to open, and the pseudo-locale at 400 dpi. |
| **Where the rules live** | **[Traps.md](../Traps.md)** for things in the toolchain that lie to you — read it before debugging a probe. **[Architecture.md §18](../Architecture.md#18-invariants)** for rules the code depends on. **[ADRs](../adr/)** for why. An obligation with an owning task is a **[ledger](ROADMAP.md#carry-forward-into-m7)** row and leaves only when its owner's *As built* says so; one with no owner is a **[parking-lot](ROADMAP.md#parking-lot)** line. **M7's ledger has twelve rows; rows 4 to 8 are discharged by [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md) to [M6-11e](tasks/M6-11e-two-rows-that-assert-a-premise.md), and every open one names an unmerged owner or says why it has none** (M4-07 rule 11); four name **the instrument** that will take their number. **A row that wants a number is discharged by an instrument, not by an argument** — and M6-11 adds one corollary: **a resume has only been witnessed if the quit and the Continue happened in the same session.** |
---

## How to write an entry

Append one entry per merged task, newest at the bottom. **Twelve lines and 3 000 bytes or fewer, measured** — a line here is a paragraph, so the line cap alone bounded nothing: M4's entries ran 3.3 to 7.4 KB under it, and they stay as they are, archiving with the milestone; the byte cap applies from M4-06. The spec's *As built* footer owns the full account of what deviated and why, under its own 6 000-byte cap in [the template](tasks/_TEMPLATE.md); this entry is the index — what happened, how it was checked, and where each lesson was filed. **No superlatives**, here or in the *As built*: *"the first task in the project to…"* is history, not information — say what exists and link. The newest entry's size, in one line:

```sh
awk '/^### 20/{e=""} {e=e $0 "\n"} END{printf "%s", e}' Docs/plan/PROGRESS.md | wc -c
```

```markdown
### YYYY-MM-DD · M0-03 · Domain events
**Built:** one or two lines — what exists now that didn't.
**Verified:** one line — the suite count and its delta against the previous entry, the runs, the Console sweep, the format check. The chain of counts lives here, not in Current State.
**Deviations:** none | *n*, in the spec's As built. Name here only one that changes a decision.
**Learned:** one line each, ending with where it was filed — Traps §n · AR §18.x · ledger row n · spent (stays here).
**Follow-ups:** tasks created, ledger rows added, or "none".
```

The PR number lives in the merge commit — do not write it here. Current State is written **as of the PR's merge**: the task the PR closes is *Last merged*. If a deviation changes a decision in `Architecture.md`, it needs a superseding ADR — say so here and link it.

**A durable lesson does not go in Current State.** A toolchain trap goes in [Traps.md](../Traps.md); a rule the code now depends on goes in [Architecture.md §18](../Architecture.md#18-invariants); a thing a named task must handle goes in the [carry-forward ledger](ROADMAP.md#carry-forward-into-m5), and one with no owner yet in the ROADMAP parking lot, one line. The entry says *what happened*, and links.

**Current State is overwritten, not appended.** Every row states the present and links to where the history is — the Log entry, the spec's *As built*, an archive, a ledger row. A row never says *"and as of M3-06…"*; that sentence is the Log's. *Last merged* names the task of the newest Log entry and no older one — there is no task chain — and the *Verified* row carries only the latest count, because the chain is the **Verified** line of each entry. A handover that makes a row longer has usually put history in it: move the history, keep the sentence that is true today.

**Current State is capped at 10 000 bytes, measured rather than estimated** — the twelve-line cap's device, one section over. It reached 256 KB at M4-05b before this rule existed, and was 9 567 bytes when the rule was written. One line settles it before a handover:

```sh
awk '/^## Current State/,/^## How to write an entry/' Docs/plan/PROGRESS.md | wc -c
```

Over the cap, rewrite the row that grew; do not move the cap.

**The three rows that only ever grew are bounded too, and this is the rule that bounds them** — [M4-07](tasks/M4-07-acceptance-and-tag.md) rule 5, the rolling treatment the task chain already had. The block cap alone was not enough: it says *something* must shrink and never which, so the row rewritten is whichever is easiest rather than whichever grew.

- **What works** is the **present tense, one clause a subsystem** — what a player can do today, never how it got there. When a milestone closes, its sentence is compressed to a clause and the detail stays in its archive, which is where it already is.
- **What does not work yet** and **Known issues** are **capped at nine numbered items each**. A tenth is not appended: the weakest is promoted out first. **An item leaves the moment it acquires an owning task** — it becomes a [ledger](ROADMAP.md#carry-forward-into-m5) row — **or the moment it is ruled to have none** — it becomes a [parking-lot](ROADMAP.md#parking-lot) line.
- **An item that repeats a ledger or parking-lot entry states its *symptom* in one clause and links.** Duplication is what made these rows grow: at M4-06 three of nine *Known issues* were second copies of rows that already had a home, each written out in full, and each re-edited whenever its real home moved. M4-07 took six of the nine out on that rule and the row halved.

**At the end of a milestone**, move its entries to `archive/PROGRESS-M<n>.md`, promote anything durable out of them first, and leave this Log holding only the milestone in progress. **M2 is the first milestone this was done for on time** — M0's and M1's were archived a milestone late and at 381 KB between them, which is the failure the rule was written against. M3 was the second, M4 the third, M5 the fourth and M6 the fifth.

---

## Log

_M0, M1, M2, M3, M4, M5 and M6 are archived: [M0](archive/PROGRESS-M0.md) (20 entries) · [M1](archive/PROGRESS-M1.md) (21) · [M2](archive/PROGRESS-M2.md) (27) · [M3](archive/PROGRESS-M3.md) (35, plus M2-15a, which is M2's task merged after that archive closed) · [M4](archive/PROGRESS-M4.md) (11) · [M5](archive/PROGRESS-M5.md) (15) · [M6](archive/PROGRESS-M6.md) (23, the first of them M5-08a, M5's task merged after that archive closed)._

**M7 has not started, and its log opens with M6's tasks**: [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md) to [M6-11e](tasks/M6-11e-two-rows-that-assert-a-premise.md), created by M6's acceptance and merged after M6's archive closed — M5-08a's precedent, which opened M6's log the same way. M7-00a's entry follows them.

### 2026-09-24 · M6-11a · Continue resumes the run on disk, not the one the Menu read at boot

**Built:** `SaveWriter` takes the boot scope's `SavedRun` and mirrors each snapshot into it, and clears it on each death, before queuing the disk operation — so the Menu's Continue offers the run last written, within one app session as across a relaunch. `BootFlow` and every registration are unchanged; the 18 constructor sites followed.

**Verified:** **3 145 EditMode / 0 / 0** (+5 on M6-11's 3 140) and **PlayMode 26 / 0 / 0 on 2 of 3 passes**, the other 25 / 1 on the wedge flake ([M7 row 8](ROADMAP.md#carry-forward-into-m7)). **Red-checked:** with the mirror removed the two fixtures ran 36 / 5, exactly the new rows; with it moved below `Enqueue`, 39 / 2. Console: 46 entries, each from a passing negative-path row; zero new analyzer warnings, `dotnet format` green over ten files, `TimeManager.asset` reverted.

**Deviations:** 4, in the [spec's *As built*](tasks/M6-11a-continue-resumes-the-run-on-disk.md#as-built). None changes a decision. Three comments outside the table were corrected because they described the bug as the design — `MenuPresenter`'s said Continue records *"the run that was on disk at launch."*

**Learned:** **an end-state assertion cannot see an ordering rule** — with the mirror moved below the write, three of the five new rows stayed green, and only spies reading `SavedRun` at the moment the store is called went red → spent (stays here). · **A test that taps the button finds what two half-tests miss**: M2-14b asserted visibility and `PendingRun` apart, and the bug lived between them → spent. · **Boot seeds from a continuation**, harmless while the local store answers inside `Start` → [parking lot](ROADMAP.md#parking-lot), promoted by the first asynchronous store.

**Follow-ups:** [M7 row 4](ROADMAP.md#carry-forward-into-m7) discharged; `m6` waits on [M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md) alone. One parking-lot line.

### 2026-09-24 · M6-11b · A resume keeps the Claiming

**Built:** `RunEconomy.Claimed` — v4 re-cut, not bumped — written by `RunRecorder.Take`, carried by `LocalJsonSaveStore` beside `veilrot`, and handed to `Veilrot.Restore(value, claimed)`, which latches on the flag or on 100. A Claimed run spent or cleansed below 100 comes back Claimed and draining; a v4 file written before the key reads unclaimed, as M6-04 rule 9 said.

**Verified:** **3 152 EditMode / 0 / 0** (+7 on M6-11a's 3 145) and **PlayMode 26 / 0 / 0** on one pass; an EditMode pass taken after it ran 3 151 / 1 on the Editor-clock row M6-11e owns ([M7 row 8](ROADMAP.md#carry-forward-into-m7)). **Red-checked:** with `Restore` ignoring the flag, 3 149 / 3 — exactly the three latch rows; with the recorder and the mirror dropping it, 3 147 / 5. Console: 46 entries, each from a passing negative-path row; zero new analyzer warnings, `dotnet format` green over 13 files, `TimeManager.asset` reverted.

**Deviations:** 5, in the [spec's *As built*](tasks/M6-11b-a-resume-keeps-the-claiming.md#as-built). One changes the Public API: no one-argument `Restore`, because it would be the defect's own call site. `RunState.IsClaimed`'s remarks and AR §18.1's restore clause stated the bug as design and were corrected.

**Learned:** **a private method reached by `GetMethod(name)` is an overload tax** — four fixtures found `Veilrot.Restore` by name, and a second overload throws `AmbiguousMatchException` in all four → spent (stays here). · **A bought cast is a cast for M3-06 rule 6** — one a frame across every active, so *n* paid casts take 2*n* frames → spent.

**Follow-ups:** [M7 row 5](ROADMAP.md#carry-forward-into-m7) discharged; `m6` has nothing in front of it.

### 2026-09-24 · M6-11c · Stage 1 without the eight M1 dummies

**Built:** `Run.unity`'s `RunScope` dresses no enemy — `_dummyPositions` is empty, `_dummySpec` stays so the pool still prewarms `DeviceEnemyCap + 1` — and `RunSceneTests` opens the shipped scene and holds both. Stage 1 is the ten Husks the director composed, not eighteen, and GD §7.1's arrival is empty on every stage.

**Verified:** **3 154 EditMode / 0 / 0** (+2 on M6-11b's 3 152) and **PlayMode 26 / 0 / 0** on the first pass. **Red-checked:** against the old scene the fixture ran 1 / 1, *"Expected: 0 But was: 8"*; with `_dummySpec` cleared, 1 / 1 the other way. **Witnessed in Play**: 0 enemies from the Run scene's first frame to t + 2.83 s, then wave 1. Console: 46 entries, each from a passing negative-path row; zero new analyzer warnings, `dotnet format` green over two files, `TimeManager.asset` reverted.

**Deviations:** 3, in the [spec's *As built*](tasks/M6-11c-stage-one-without-the-m1-dummies.md#as-built). None changes a decision. `_dummySpec`'s tooltip told the Inspector to *"leave empty for an arena that starts bare"* — the regression the new row goes red on — and was corrected outside the table.

**Learned:** **the PlayMode suite writes the owner's real `run.json`** — `BootSmokeTests` reaches a run through boot's store, so every handover's pass replaces an Editor playtest's saved run → [parking lot](ROADMAP.md#parking-lot). · **A row that reads a field asserts a premise; invoke what reads it** — both rows call the private method the field feeds, M7 row 8's lesson applied before it bit → spent (stays here).

**Follow-ups:** [M7 row 6](ROADMAP.md#carry-forward-into-m7) discharged. One parking-lot line.

### 2026-09-24 · M6-11d · The Sanctum does not sell a reroll a finished tree can never spend

**Built:** `SanctumShop.CanBuy(Reroll)` asks Banish's question — taken plus banished short of `TreeRules.Count` — after the price, so a finished tree's Reroll row is dead and says *"Nothing left to offer"* (`ui.sanctum.refused.complete`, English and a regenerated pseudo row). A charge already banked stays banked and is not refunded.

**Verified:** **3 159 EditMode / 0 / 0** (+5 on M6-11c's 3 154) on the final code, then **PlayMode 26 / 0 / 0** on the first pass; an earlier EditMode pass ran 3 158 / 1 on `SeededRandomTests.Capture_AllocatesNothing`, M3-01a's once-seen allocation flake, in code this task does not touch. **Red-checked:** with `Reroll => true` back, 62 / 4 over the two fixtures, exactly the four refusal rows; with the presenter's arm removed, 65 / 1, the screen row alone. Console: 46 entries, each from a passing negative-path row; zero new analyzer warnings, `dotnet format` green over four files, `TimeManager.asset` reverted.

**Deviations:** 3, in the [spec's *As built*](tasks/M6-11d-no-reroll-for-a-finished-tree.md#as-built). None changes a decision. A fifth row pins rule 2, which the Tests table left without one. M6-11a to c had never been opened as PRs; they were merged as #158–#160 on the owner's green light before this branch was cut.

**Learned:** **a row testing one half of an *and* must make the other half true** — the rule-2 row bought its reroll with its whole balance, so the refusal it asserted was the wallet's, and it stayed green against the bug until red check A counted three failures where four were expected → spent (stays here). · **A node left is not a node reachable** — an orphaned Upgrade still sells a Reroll → [parking lot](ROADMAP.md#parking-lot).

**Follow-ups:** [M7 row 7](ROADMAP.md#carry-forward-into-m7) discharged. One parking-lot line.

### 2026-09-24 · M6-11e · Two rows that assert a premise rather than a behaviour

**Built:** `FrameOrderTests.Ticker_RunsTheStepsInOrder`'s step 5 probes the body's trigger capsule at fact time — found where this frame's move put it, absent where the snapshot saw it — in place of the wedge, and M5-05a's diagnosis and its control row retire. `PlayerAnimatorViewTests` `Assume`s the zero clock, and `Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns` asserts the ratio and its ceiling of 6 on a moved one.

**Verified:** **3 160 EditMode — 3 159 / 0 / 1 inconclusive** (+1 on M6-11d's 3 159), on the final code on a zero clock and on a moved one, each clock row running once; **PlayMode 25 / 0 / 0 on ten passes of ten** (−1, the control row), the body walking 0.949–1.031 m against a 0.6 m floor, and an eleventh after the red checks. **Red-checked:** without `Physics.SyncTransforms()`, 15 / 3 — exactly the three fact-time rows; without the ceiling, the sibling alone. Console: 48 entries, M6-11d's 46 plus two AI Assistant token warnings; `dotnet format` green over two files, `TimeManager.asset` reverted, `run.json` restored.

**Deviations:** 4, in the [spec's *As built*](tasks/M6-11e-two-rows-that-assert-a-premise.md#as-built). None changes a decision. The spec's premise — the clock moves on entering Play — was wrong, and rule 3's rows hold regardless. M6-11d had never been opened as a PR; it was merged as #161 on the owner's go before this branch was cut.

**Learned:** **an unfocused Editor's `Time.time` reads 0, and focus is what moves it** — 0 after ten PlayMode passes and on leaving Play, 1.83 s once the owner put the Editor in front; witnessing the moved half cost two focus requests → [Traps §3](../Traps.md). · **A kickoff saying the last task is merged was wrong twice running** — M6-11a to c at M6-11d, M6-11d here — because the prompt asserted a merge no one had green-lit → spent (stays here); this task's handover asks for the green light instead.

**Follow-ups:** [M7 row 8](ROADMAP.md#carry-forward-into-m7) discharged; every M6 bug task is merged, and `m6` is the owner's to tag.

### 2026-09-24 · M7-00a · Specs for the roster, in core

**Built:** five specs where [the table](ROADMAP.md#m7--content-pass) had two titles — [M7-01a](tasks/M7-01a-the-lunger.md) the Lunger · [M7-01b](tasks/M7-01b-the-weaver.md) the Weaver · [M7-01c](tasks/M7-01c-the-warden.md) the Warden · [M7-02a](tasks/M7-02a-what-an-elite-is.md) the Elite body · [M7-02b](tasks/M7-02b-who-buys-an-elite.md) who buys one. All `Soulvail.Core` and authored data, no view — M6-00a's seam. M7 is now five spec groups and twenty rows, five of them counted.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays M6-11e's **3 160 EditMode (3 159 / 0 / 1 inconclusive)** and **PlayMode 25 / 0 / 0**. Every type, member, call-site count and asset field a rule names was grepped first. Branched from `dev` at #162.

**Deviations:** *2*. **Build order is not ID order**: M7-01c's Warden depends on M7-02b, or a capped wave becomes twenty Wardens. **Both titles split, neither as predicted** — three core tasks and a view for 00b; the Elite as body and buyer, with the affixes to 00b.

**Learned:** **row 11 is `WaveComposer.Upgrade`, and new archetypes move it rather than end it** → M7-02b. · **The one-shot rule has only ever been checked against the Oathbound's 140 HP**; against the Emberwright's 70 the Bloater is past it from stage 20 → [row 2](ROADMAP.md#carry-forward-into-m7). · **`ApplyDamage` has never known where a hit came from**, and `enemy.warden` is the boss's body, which three tree fixtures read by path → M7-01c. · **The design documents are this project's own drafts**, so an ambiguity in them — the Weaver's *"2 generations"* — is ruled and written down, never asked → spent (stays here).

**Follow-ups:** [row 11](ROADMAP.md#carry-forward-into-m7) re-owned to M7-02b with its diagnosis; [row 2](ROADMAP.md#carry-forward-into-m7) gains the one-shot finding. **Parking lot: three re-aimed** — the blackboard rename and the Revenant to V2 (GD §19), Echo to M7-08's instrument — and AR §18.1's lure row with them. **Two owner rulings named for M7-00e**: art and audio sourcing.

### 2026-09-25 · M7-00b · Specs for the affixes and what a player sees

**Built:** five specs where [the table](ROADMAP.md#m7--content-pass) predicted four — [M7-01d](tasks/M7-01d-three-archetypes-a-player-can-read.md) the lane, the shield and the shrug · [M7-02c](tasks/M7-02c-the-affix-roll.md) the roll, Hasted, Warded, Siphoning · [M7-02d](tasks/M7-02d-the-two-that-fire-on-death.md) Volatile and Splintered · [M7-02f](tasks/M7-02f-a-second-affix-bought.md) the second affix, bought · [M7-02e](tasks/M7-02e-an-elite-you-can-see.md) the outline, the label, the ward and the line. M7 is twenty-one rows, ten of them counted.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays M6-11e's **3 160 EditMode (3 159 / 0 / 1 inconclusive)** and **PlayMode 25 / 0 / 0**. Every member, call-site count, asset field and view behaviour a rule names was read or grepped first. Branched from `dev` at #163, M7-00a's merge, opened and merged on the owner's green light.

**Deviations:** *3*. **A fifth spec**, M7-02f: M7-02b handed the second affix on without an owner, and a schedule would make difficulty depend on the phone (GD §11.2). **Build order moved again**: M7-01d follows M7-01c, before the affixes. **Two unbuilt 00a specs amended**: M7-01a's `LungeTelegraphed` carries its half-width; M7-02b's hand-off names M7-02f.

**Learned:** **every way core hurts the player is a blow**, so a pulse through `ApplyDamage` keeps the Oathbound in i-frames and an Aegis refilling at 15/s absorbs a 2/s drain for ever → M7-02c rule 10's `Drain`. · **A blocked hit flashes white and shows the bar** — `EnemyHitFeedback` never read `Amount` → M7-01d rule 3. · **`ZoneView` draws every zone in the player's cyan**, which an enemy's pool cannot be → M7-02d rule 12. · **At depth every body is an Elite** → [row 12](ROADMAP.md#carry-forward-into-m7), M7-08's instrument.

**Follow-ups:** [row 12](ROADMAP.md#carry-forward-into-m7) opened; no row re-owned. The `m6` tag is still the owner's, and M7-00e still waits on the art and audio rulings.

### 2026-09-25 · RS-02a · A playable Ranger, in a sandbox

**Built:** [RS-02a](tasks/RS-02a-a-playable-ranger.md), at the owner's request, ahead of the new model: `RangerAnimatorView` (legs as a directional blend on the body-frame velocity, arms as a masked layer that draws on `PlayerAttacked` and releases on the player's `ProjectileFired`, and the bowstring's blend shape), `RangerSandboxLoop` and `RangerSandboxScope`. **It shoots standing still, by the owner's second ruling**; the running shot is a switch until RS-03 makes it a skill. The assets are `AC_Ranger`, `AC_TrainingDummy`, `AM_Ranger_UpperBody`, `Player_Ranger.prefab`, `Arrow.prefab` and `RangerShowcase.unity`, with six dummies. No core file changed; RS-00's plan, merged without an entry, is recorded here.

**Verified:** **3 196 EditMode — 3 195 / 0 / 1 inconclusive (+36)**, one pass, and **PlayMode 53 / 0 / 0 (+28)** on three full passes. The new rows play the scene: facing, cadence, arrows landing, strafing, lowering the bow. Unity's `csc` with the analyzers gave zero warnings on the three assemblies touched. The format check passed on 5 of 5 files, and the Console shows nothing from the sandbox. Frames were captured from a temporary PlayMode test and read by eye. Branched from `rs-00-plan-the-rangers-model`, which is not yet on `dev`.

**Deviations:** *2*, in the spec's *As built*. **No spec preceded the build**: it was written with it. **The bow's numbers are placeholders**: the kit is RS-03's.

**Learned:** **`Targeter` falls back to "nearest, blocked" when nothing in its span scores**, so a caller that skips CC §3.1's range filter faces a target 14 m away → spent, pinned by `Sandbox_StartsIdleWithNothingInRange`. · **`EditorApplication.Step` does not advance an unfocused Play session** → [Traps §3](../Traps.md). · **The CLI's `capture_game_view` writes a relative path under `Assets/`** → [Traps §4](../Traps.md). · **A PlayMode row's gamepad is reset by a focus change, and `IgnoreFocus` lets another window's typing in** → [Traps §8](../Traps.md).

**Follow-ups:** RS-02 is now "a body per class" alone, and RS-04's scene is partly this sandbox. RS-03 owns the running shot as a skill. No ledger row.

### 2026-09-25 · RS-00b · Specs for the Ranger's class and its three passives

**Built:** six specs from the owner's design of 2026-09-25 — three passives, each taken three times (attack speed; a volley every 5, 4, then 3 shots; movement speed), a dodge roll that can be switched off, the name *Ranger*, free while built. [RS-02b](tasks/RS-02b-a-body-per-class.md) a body per class · [RS-02c](tasks/RS-02c-an-arrow-per-shooter.md) an arrow per shooter · [RS-03a](tasks/RS-03a-holding-fire-on-the-move.md) holding fire and `MovementSkillKind.None` · [RS-03b](tasks/RS-03b-the-volley.md) the volley · [RS-03c](tasks/RS-03c-the-ranger-joins-the-roster.md) the class, its nine nodes and a fourth card · [RS-03d](tasks/RS-03d-the-ranger-seen.md) the views. RS-03e's acceptance is a title.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays RS-02a's **3 196 EditMode (3 195 / 0 / 1)** and **PlayMode 53 / 0 / 0**. Every member, count and asset field a rule names was read first, three areas by research agents and the rest directly.

**Deviations:** none.

**Learned:** **class select holds exactly three cards, hand-placed** → RS-03c. · **`ProjectileFired.SpecId` was built for a look per shooter and nothing picks one** → RS-02c. · **`PendingShot` holds one shot, so no damage frame can loose two** → RS-03b. · **A class's own tree is never checked against the stats it names; a bad node throws from `SkillTree.Take` after it is recorded** → RS-03b rule 7. · **A zero-damage dash still calls `ApplyDamage(0)` on everything it crosses** → RS-03a rule 8.

**Follow-ups:** parking lot gains the Ranger's price and V1/V2 question. No ledger row.

### 2026-09-25 · RS-03a · Holding fire on the move, and a class with no movement skill

**Built:** [RS-03a](tasks/RS-03a-holding-fire-on-the-move.md). `PlayerCombat.FireWhileMoving` is a `Stat` seeded from `WeaponSpec.FiresWhileMoving`, the seventeenth `PlayerStat`, and `IsHoldingFire` follows it. A class at 0 holds fire until it has stopped: stick centred, no dash, and the body at or under `PlayerMotor`'s 0.05 m/s. It drops a draw it moves out of, faces where it runs, and publishes `HoldFireChanged` on each change. `MovementSkillKind.None` ignores the press and hides `SkillButton`. A dash that deals nothing touches nobody. The shipped classes play as before.

**Verified:** **3 216 EditMode — 3 215 / 0 / 1 inconclusive (+20)** and **PlayMode 53 / 0 / 0 (+0)** on the final tree; Console clean; format check clean on 16 files. Branched from `dev` at #168, after RS-00, RS-02a and RS-00b were opened and merged as #166–#168 on the owner's green light. Earlier PlayMode passes failed on a sandbox scene the owner then discarded, and on a flaky row (Learned).

**Deviations:** *3*, in the spec's *As built*. **A hold drops a draw, never a swing whose arrow has left**: the reset clears the cadence, so a step after each arrow would out-shoot standing still. Rule 8 guards damage and knockback separately. Two rows live in `Tests.Game`.

**Learned:** **The CLI's `run_tests` listed 3 216 tests, ran none, and left the pipeline server refusing every command; `TestRunnerApi` from `Unity_RunCommand` ran them in 29 s** → Traps §3, CLAUDE.md. · **`Loop_ShootsOnlyStandingStill` failed three full passes, then passed four, the identical tree included** → Traps §8. · **`RangerShowcase.unity` had been saved with the running shot on and a 0.14 release, failing three sandbox rows**; the owner discarded it → spent. · **The sandbox still resets any swing on a move** → RS-03d, which moves it onto `HoldFireChanged`.

**Follow-ups:** none. RS-03b is now unblocked, beside RS-02b.

### 2026-09-25 · M7-00c · Specs for the Choirmother

**Built:** four specs where [the table](ROADMAP.md#m7--content-pass) predicted one title — [M7-03a](tasks/M7-03a-the-ring.md) the ring · [M7-03b](tasks/M7-03b-the-choirmother.md) the singer, and the run's first choice between two fights · [M7-03c](tasks/M7-03c-a-song-you-can-see.md) the wedge with its pillar shadows, and the ring's arcs · [M7-03d](tasks/M7-03d-the-choirmother-at-stage-ten.md) the roster, a boss mark per arena, and the Gravecaller's deed. M7 is twenty-four rows, fourteen of them counted.

**Verified:** docs only — no C#, no compile, no suite; the baseline is RS-03a's **3 216 EditMode (3 215 / 0 / 1 inconclusive)** and **PlayMode 53 / 0 / 0**, merged just before it; the specs were read against the code at #164. Every member, call-site count, asset field and arena position a rule names was read or grepped first. A second reader then checked the four against the code and found about thirty defects, all fixed: asset rows filed under Tests.Core, a second writer on `IsVulnerable` that M7-02c rule 7 forbids, a fault that asked physics of a prefab outside a scene, and 25 rays for 2° gaps. Branched from `dev` at #164, M7-00b's merge, opened and merged on the owner's green light.

**Deviations:** *4*. **Four specs split by mechanism, the roster built last**: M4-02 rostered the Warden a task before M4-03 drew it. **Every boss stands on an arena mark, the Warden included**: a boss that never walks cannot use a drawn point, and `Arena_Pillars`' `Spawn_04` is 4.5 m from a pillar. **The art ruling corrected**: Current State and the M7-00e paragraph still called it pending. GD §21.4 and the KayKit parking-lot line now record it too. **[`_TEMPLATE.md`](tasks/_TEMPLATE.md) gains one line**: a row that opens an asset names its Tests.Game file.

**Learned:** **a beat clears only the ids it summoned**, so a split add's children outlive their phase → M7-03a rule 9. · **The opening phase never summons**, because `Summon` runs only on a crossing → M7-03a rule 8. · **`RequireAuthored` walks a boss's body and not its summons** → M7-03a rule 11. · **A Warden slam in its windup when a beat opens is frozen and released after it** → [parking lot](ROADMAP.md#parking-lot).

**Follow-ups:** [rows 1 and 2](ROADMAP.md#carry-forward-into-m7) gain her; no row re-owned. One parking-lot line added, one re-aimed. M7-00e waits on the audio ruling alone. 00a and 00b's fourteen specs have no *As built* placeholder and file asset rows under Tests.Core, and M7-01c pins an edge at exactly 60°; each builder fixes its own.

### 2026-09-25 · RS-02b · A body per class

**Built:** [RS-02b](tasks/RS-02b-a-body-per-class.md). `CharacterLookBook`, built at boot from each `CharacterDefinition`'s new `_body`, maps a class to its body prefab. `RunScope` raises that body under the player in a build callback and injects it, falling back to its required `_defaultBody` when a class names none. `RunCharacter.Choose` is the one choice of class, made by the raise and by `RunTicker`. Both animator views live on their body and find the `PlayerView` above it. The Knight left `Player.prefab` for `Prefabs/Player/Bodies/Knight`, which all three classes name. The sandbox's Ranger is `Bodies/Ranger`, nested in `Player_Ranger`. Nothing that plays changed.

**Verified:** **3 227 EditMode — 3 226 / 0 / 1 inconclusive (+11)** and **PlayMode 55 / 0 / 0 (+2)**, one full pass each on the final tree, through `TestRunnerApi`. The first EditMode pass ran 3 225 / 1 on `RunEndPresenterTests`' guard walk (*As built*). **Red-checked:** views back on `GetComponent`, 46 / 1, the view row alone; the ticker on the catalog's first class, the Gravecaller row alone; no `InjectGameObject`, both PlayMode rows. Console: 46 entries, each from a passing negative-path row. Unity's `csc` with the analyzers: zero warnings over the three assemblies touched. `dotnet format` clean over 15 files, `TimeManager.asset` reverted. Branched from `dev` at #170.

**Deviations:** *3*, in the spec's *As built*. None changes a decision. `PlayerAnimatorView` gains `Step`, because the Tests table steps both views. "At identity" keeps the body's own 0.66 scale. `FallbackCharacterId` is gone rather than wrapping `Choose`.

**Learned:** **saving a scene whose nested prefab was just restructured wrote overrides against a fileID the prefab no longer has.** The showcase gained AABB overrides on the old bow, plus URP light data, and both were dropped by hand → spent (stays here). · **`LogAssert.NoUnexpectedReceived` fails a row on a warning, not only an error.** The direct-Play seed warning is now expected → spent.

**Follow-ups:** none. RS-02c is now unblocked, beside RS-03b.

### 2026-09-25 · RS-02c · An arrow per shooter

**Built:** [RS-02c](tasks/RS-02c-an-arrow-per-shooter.md). `ProjectileViews` keeps one pool per prefab and picks a shot's pool through `CharacterLookBook` by its `SpecId`. A class that names a `Projectile` flies it. Every enemy's shot, and every class that names none, flies the default, which stays prewarmed. A class's pool is built on its first shot. A body goes back to the pool it came from, on impact and on `Dispose`. `CharacterDefinition` gains `_projectile`, which all three classes leave empty, so nothing that flies has changed. RS-03c names `Arrow.prefab` on the Ranger.

**Verified:** **3 235 EditMode — 3 234 / 0 / 1 inconclusive (+8)** and **PlayMode 55 / 0 / 0 (+0)**, one full pass each on the final tree, through `TestRunnerApi`. The first PlayMode pass ran 54 / 1 on `Loop_ShootsOnlyStandingStill` (Learned). **Red-checked** with two mutations, each turning exactly its own rows red (*As built*). Console: no errors, and the one warning is the expected direct-Play seed. Unity's `csc` with the analyzers: zero warnings over `Soulvail.Game` and `Soulvail.Tests.Game`. `dotnet format` clean over 6 files, `TimeManager.asset` reverted. Branched from `dev` at #171.

**Deviations:** *2*, in the spec's *As built*. `PooledCountOf` is public API the Tests table needed, and `CharacterLook`'s projectile is an optional argument.

**Learned:** **`Loop_ShootsOnlyStandingStill` failed alone in fresh domains, `dev`'s code included, then passed alone and in a full pass on the same tree.** Passing alone no longer tells it apart from a regression; a bisect against `dev` does. The Input System has a focus path that could drop the stick, and it does not explain the passes → [Traps §8](../Traps.md). · **`editor_focus` focuses a dock area, not the application**: `isApplicationActive` stays `False` → Traps §8.

**Follow-ups:** none. RS-03c now waits on RS-03b alone.

### 2026-09-25 · RS-03b · The volley

**Built:** [RS-03b](tasks/RS-03b-the-volley.md). `Volley` holds three `Stat`s (`Every` at base 0, `Arrows`, `Damage`), the counter, and the fan. `VolleySpec` is optional on `CharacterSpec`, and `PlayerStat` appends `VolleyEvery`, `VolleyArrows` and `VolleyDamage`. After `Every` ordinary shots the next is a fan at the target, one `PlayerAttacked` and up to seven shots. `PendingShot` is now the head of a queue, and `RunSession` fires every queued shot on its tick. `Start` refuses a class's own tree that names a stat the class lacks. No shipped class carries a volley, so nothing that plays has changed.

**Verified:** **3 251 EditMode — 3 250 / 0 / 1 inconclusive (+16)** and **PlayMode 55 / 0 / 0 (+0)**, through `TestRunnerApi`. The first EditMode pass failed one save-store row on a file lock (Learned). **Red-checked** with three mutations, which turned exactly their three rows red (*As built*). Console after the last EditMode pass: 46 entries from negative-path rows plus PlayMode's direct-Play seed warning; none from this task. Unity's `csc` with the analyzers: zero warnings over the four assemblies touched. `dotnet format` clean over 14 files, `TimeManager.asset` reverted. Branched from `dev` at #172, RS-02c's merge.

**Deviations:** *2*, in the spec's *As built*. Two rows live in `Tests.Game`. `Volley` gains five public members, which the rows and `PlayerCombat` need.

**Learned:** **`LocalJsonSaveStoreTests.Store_RoundTripsClaimed` failed a full pass on `IOException: Unable to remove the file to be replaced`, then passed alone and in two full passes** → [Traps §7](../Traps.md). · **A PlayMode run clears the Console, so the sweep is taken after EditMode** → spent (Traps §7 already says it).

**Follow-ups:** none. RS-03c is now unblocked.
