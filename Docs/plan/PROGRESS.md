# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

**Closed milestones are archived, not deleted:** [M0](archive/PROGRESS-M0.md) · [M1](archive/PROGRESS-M1.md) · [M2](archive/PROGRESS-M2.md) · [M3](archive/PROGRESS-M3.md) · [M4](archive/PROGRESS-M4.md) · [M5](archive/PROGRESS-M5.md) · [M6](archive/PROGRESS-M6.md). The log below covers the milestone in progress only. This file used to be 381 KB and could not be opened whole by any tool; keeping it to one milestone is what stops that happening again — **M2 was the first milestone archived on time, and M6 is the fifth running, which is what turns a rule into a habit.**

**Current State is a brief, not a second log, and it is capped.** After M3-15 this file was 228 KB with an *empty* Log; by M4-05b it was 298 KB, and 256 KB of that was Current State — thirty-four *Verified* rows back to M3-00b, an eight-deep task chain, and four rows that were appended to for a whole milestone and never rewritten, one of them still opening with a sentence about M3-06. On 2026-09-20 the block was cut to under 10 KB: M3's *Verified* rows went to the [M3 archive](archive/PROGRESS-M3.md#verification-chain), M4's were folded into their Log entries as a **Verified** line, and every other row was rewritten as the present with links to the history. The rules that keep it there are under [How to write an entry](#how-to-write-an-entry) — two on 2026-09-20, and a third at M4-07, which bounds the three rows that only ever grew.

---

## Current State

_Updated: 2026-09-24, as of M6-11c's merge — M6 accepted and archived, and nothing left in front of its tag; M7 not started._

| | |
|---|---|
| **Milestone** | **M7 — Content pass. Not started: its first task is a spec group, and three M6 bug tasks come first.** M0–M5 are tagged `m0`–`m5` — **`m4` included, since 2026-09-20**, which this row wrongly called pending for two milestones. **M6 is closed and [archived](archive/PROGRESS-M6.md)** — 22 tasks, accepted on Editor evidence and five played runs to stage 39; **`m6` is the owner's to tag** — both bugs ruled to go before it, [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md) and [M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md), are merged. |
| **Last merged task** | **[M6-11c](tasks/M6-11c-stage-one-without-the-m1-dummies.md) — stage 1 without the eight M1 dummies.** `Run.unity` dresses no enemy and still prewarms the pool, so stage 1 is the ten Husks the director composed and its arrival is empty; `RunSceneTests` opens the shipped scene and holds both. |
| **Next task** | **The `m6` tag, the owner's**; then [M6-11d](tasks/M6-11d-no-reroll-for-a-finished-tree.md) and [M6-11e](tasks/M6-11e-two-rows-that-assert-a-premise.md) in either order, neither gating it; then **M7-00a**, M7's first spec group, against [its ledger](ROADMAP.md#carry-forward-into-m7). |
| **Verified** | **3 154 EditMode / 0 / 0 and PlayMode 26 / 0 / 0** at M6-11c — an EditMode pass taken *after* PlayMode fails the Editor-clock row, and PlayMode can fail the frame-order flake: both [M7 row 8](ROADMAP.md#carry-forward-into-m7). **A PlayMode pass also overwrites the real `run.json`** ([parking lot](ROADMAP.md#parking-lot)). The chain of counts is each Log entry's **Verified** line ([M6's](archive/PROGRESS-M6.md)). **The played numbers — hits to kill at seven depths for three classes, bodies, frame times, the economy, the Claiming — are [M6-11's *As built*](tasks/M6-11-acceptance-and-tag.md#as-built).** |
| **What works** | **A run is a descent that reaches a boss, kills it, pays for dying, and survives the app closing**, and three classes walk it — each with a twelve-node tree and its own way with the Veil. Boot → Menu → a card per class, a locked one priced in Shards → arenas sealed by threat-budgeted, telegraphed waves → a door → one stage deeper, a boss every fifth. **Levelling:** a kill is experience, a threshold is a pick of three, a full tree pays Overflow, and at half a tree a run borrows one branch of another class. **The Warden** dies in 89–113 s to stage 30. **Death pays** Shards into `PlayerProfile` v4, and class select spends them. **Every stage clear pays Essence**, and a Sanctum after it sells a reroll, a banish, 30 HP and −15 Veilrot, each priced and refused with a reason. **Veilrot** fills from Pacts through four thresholds to the Claiming; **a Pact** is an authored card with a Rot price; **Ordeals** are dealt from stage 25, one dial each. **The HUD** draws both economies. **Saves:** `run.json` and `profile.json` are v4, every earlier version still loads, Continue offers the run last written — in one session as across a relaunch — and a Claimed run resumes Claimed. **Every string is a `LocKey`**, read in English or a generated pseudo-locale. Milestone by milestone: the [archives](archive/). |
| **What does not work yet** | *(1)* **Hits to kill leave GD §12.4's band** — the Gravecaller over it from stage 9, the Emberwright under it to 9 — and the Warden leaves §9.1's near 33: [M7 row 2](ROADMAP.md#carry-forward-into-m7), M8-05. *(2)* **The Sanctum plays as zero**: nothing bought unprompted in two natural runs — [row 9](ROADMAP.md#carry-forward-into-m7). *(3)* **A Pact is one offer in 62** — [row 10](ROADMAP.md#carry-forward-into-m7), M7-04. *(4)* **Past stage 26 the late waves are all Bloaters** — [row 11](ROADMAP.md#carry-forward-into-m7). *(5)* **The feel verdict is deferred to M8-01** by the owner's ruling, and no second language ships ([parking lot](ROADMAP.md#parking-lot)). *(6)* **A boss cannot run a `SkillRunner`, and the arena only reacts**; not saved, by ruling: cooldowns, shields, live zones. *(7)* **Unbuilt by design:** no audio, no Elites, one boss, two arenas; Tether, Rot Nova and the Keystones are M7-04's. |
| **Known issues** | *(1)* **Every "ring" is a square**: `VFX_TelegraphRing.prefab` is an untextured Quad; one circle texture fixes all five decals; art, M7. *(2)* **`ArenaView.OnValidate` warns "0 spawn point(s)" for both arena prefabs on every domain reload**; they author 8 and 7, and reload-time `Transform` references are fake-null ([Traps §1](../Traps.md)). *(3)* **`PlayerStat` lies by name** — it carries `ContactDamage`, which `PlayerStats.Resolve` refuses; the owner's ruling, owed since M4-01a. *(4)* **M3-14c's nine labels were never witnessed in Play.** *(5)* **Environmental:** a PlayMode row can fail on Unity's AI Assistant (*NoSubscription*); `ProjectSettings/TimeManager.asset` re-serialises whenever the Editor touches the project, so revert it before every handover ([Traps §5](../Traps.md)); the disk sits near full. **The frame-order flake and the animator clock row left this list at M6-11**: both were ruled, and both are [M6-11e](tasks/M6-11e-two-rows-that-assert-a-premise.md). |
| **Reference device** | **None, and the emulator is not a fallback.** BlueStacks 5 **installs the APK and crashes on launch** in its own Vulkan driver, not game code. **Nothing outside the Editor has ever run.** [M0-20a](tasks/M0-20a-apk-runs-on-bluestacks.md) owns it; a phone removes the question. **And the Editor is not a stand-in for layout either:** `Screen.dpi` reads **120** here against a phone's 400, so every dp-sized element ever looked at was drawn at **30 %** of its device size ([Traps §9](../Traps.md)). |
| **Deferred — device-only** | **All of it is [M7 row 1](ROADMAP.md#carry-forward-into-m7)**, six tags deep with nothing run outside the Editor. **Multi-touch risks a *feature*** — 80 of 84 casts in M5-08's runs were automatic — and **kill-from-recents is the one correctness row**. M6-11 adds four Editor numbers a phone must re-take: bodies under Swarm, frame time at depth, the ~50 ms frame every screen costs to open, and the pseudo-locale at 400 dpi. |
| **Where the rules live** | **[Traps.md](../Traps.md)** for things in the toolchain that lie to you — read it before debugging a probe. **[Architecture.md §18](../Architecture.md#18-invariants)** for rules the code depends on. **[ADRs](../adr/)** for why. An obligation with an owning task is a **[ledger](ROADMAP.md#carry-forward-into-m7)** row and leaves only when its owner's *As built* says so; one with no owner is a **[parking-lot](ROADMAP.md#parking-lot)** line. **M7's ledger has eleven rows; rows 4, 5 and 6 are discharged by [M6-11a](tasks/M6-11a-continue-resumes-the-run-on-disk.md), [M6-11b](tasks/M6-11b-a-resume-keeps-the-claiming.md) and [M6-11c](tasks/M6-11c-stage-one-without-the-m1-dummies.md), and every open one names an unmerged owner or says why it has none** (M4-07 rule 11); two name M6-11's remaining bug tasks, and four name **the instrument** that will take their number. **A row that wants a number is discharged by an instrument, not by an argument** — and M6-11 adds one corollary: **a resume has only been witnessed if the quit and the Continue happened in the same session.** |
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
