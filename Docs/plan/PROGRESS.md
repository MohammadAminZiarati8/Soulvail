# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

**Closed milestones are archived, not deleted:** [M0](archive/PROGRESS-M0.md) · [M1](archive/PROGRESS-M1.md) · [M2](archive/PROGRESS-M2.md) · [M3](archive/PROGRESS-M3.md) · [M4](archive/PROGRESS-M4.md) · [M5](archive/PROGRESS-M5.md) · [M6](archive/PROGRESS-M6.md). The log below covers the milestone in progress only. This file used to be 381 KB and could not be opened whole by any tool; keeping it to one milestone is what stops that happening again — **M2 was the first milestone archived on time, and M6 is the fifth running, which is what turns a rule into a habit.**

**Current State is a brief, not a second log, and it is capped.** After M3-15 this file was 228 KB with an *empty* Log; by M4-05b it was 298 KB, and 256 KB of that was Current State — thirty-four *Verified* rows back to M3-00b, an eight-deep task chain, and four rows that were appended to for a whole milestone and never rewritten, one of them still opening with a sentence about M3-06. On 2026-09-20 the block was cut to under 10 KB: M3's *Verified* rows went to the [M3 archive](archive/PROGRESS-M3.md#verification-chain), M4's were folded into their Log entries as a **Verified** line, and every other row was rewritten as the present with links to the history. The rules that keep it there are under [How to write an entry](#how-to-write-an-entry) — two on 2026-09-20, and a third at M4-07, which bounds the three rows that only ever grew.

---

## Current State

_Updated: 2026-10-16, as of UI-12c. UI-01 to UI-06 are complete (the HUD, levelling by banked points, the tree, the character page and the menu in the new art); UI-07, UI-08 and UI-10 fix what the Device Simulator showed and switch to portrait. **This Log has no entry for M7-05a–r, RS-05a–d or RS-06a–d**, which merged on their specs alone; each spec's *As built* is its record._

| | |
|---|---|
| **Milestone** | **M7 — Content pass. Specs: groups 1 to 4 of 5 written ([M7-00a to M7-00d](ROADMAP.md#m7--content-pass)); the fifth waits on the owner's audio ruling.** M7-05's Jungle art was pulled forward and built (M7-05a–r). **Two side tracks run beside it:** [RS](ROADMAP.md#rs--the-ranger-a-side-track-the-model-first), the Ranger, and [UI](ROADMAP.md#ui--the-hud-in-the-new-art-a-side-track-the-top-band-first), the HUD in the owner's Clash of Clans–style art, from 2026-09-29. M0–M5 are tagged `m0`–`m5`. **M6 is closed and [archived](archive/PROGRESS-M6.md)**; **`m6` is the owner's to tag**. |
| **Last merged task** | **[UI-12c](tasks/UI-12c-the-hero-sheets-model.md) — The hero sheet's model**: `HeroSheet.Build` turns a class into its description, five base stats, attack, special move and every rank, and every label is in English. |
| **Next task** | **[UI-12d](tasks/UI-12d-the-page-shows-it.md) — The page shows it**: the character page scrolls the hero sheet and loses the skill strip. Waiting elsewhere: [UI-10b](tasks/UI-10b-one-player-cyan.md) (the owner's cyan), UI-09 (a Gemini sheet), **RS-04**, **M7-00e** (the owner's audio ruling). Also owed: the owner's Play look at the Heroes tab (UI-12b: portraits were seen in a throwaway render, not in Play), the owner's Editor look at the tab bar (UI-11h-a's manual step 2: tap Heroes, Places, Play) and at the rings in a Jungle stage (UI-10a's manual step). Every UI-08 screen is still owed a `Capture Look` at both phone sizes. **The `m6` tag is still the owner's.** |
| **Verified** | **UI-12c: 4 275 EditMode — 4 273 passed / 1 failed / 1 inconclusive** (+15), in the open Editor through `TestRunnerApi`. **`CharacterPreviewTests.Resize_…` fails in the open Editor and passes in batch mode**: Direct3D12 reports a 24-bit depth as 32 ([Traps §9](../Traps.md#9-rendering-canvas-and-tmp)). The inconclusive row is one of the two animator clock rows, by construction ([Traps §3](../Traps.md)). **`RangerSandboxTests.Loop_ShootsOnlyStandingStill` fails while Unity is not the active application** ([Traps §8](../Traps.md)). **A save-store row can fail a full pass on a Windows file lock; re-run it alone** ([Traps §7](../Traps.md)). The chain of counts is each Log entry's **Verified** line. **The played numbers are [M6-11's *As built*](tasks/M6-11-acceptance-and-tag.md#as-built) and, for the Ranger to stage 16, [RS-03e's](tasks/RS-03e-the-rangers-acceptance.md#as-built).** |
| **What works** | **A run is a descent that reaches a boss, kills it, pays for dying, and survives the app closing**, and four classes walk it — three with a twelve-node tree and their own way with the Veil, and the Ranger with nine nodes and no relationship yet. Boot → Menu → a card per class, a locked one priced in Shards → arenas sealed by threat-budgeted, telegraphed waves → a door → one stage deeper, a boss every fifth. **Levelling:** a kill is experience, a threshold is a pick of three, a full tree pays Overflow, and at half a tree a run borrows one branch of another class. **The Warden** dies in 89–113 s to stage 30. **Death pays** Shards into `PlayerProfile` v4, and class select spends them. **Every stage clear pays Essence**, and a Sanctum after it sells a reroll, a banish, 30 HP and −15 Veilrot, each priced and refused with a reason. **Veilrot** fills from Pacts through four thresholds to the Claiming; **a Pact** is an authored card with a Rot price; **Ordeals** are dealt from stage 25, one dial each. **The HUD** draws both economies. Its top band is in the owner's Clash-style art: health, experience, level, skills, stage, enemies left, pause and the boss. **Saves:** `run.json` is v4 and `profile.json` v5, every earlier version still loads, Continue offers the run last written — in one session as across a relaunch — and a Claimed run resumes Claimed. **Every string is a `LocKey`**, read in English or a generated pseudo-locale. **A run wears the body its class names**, the Knight for three and KayKit's Ranger for the fourth, **and flies the shot its class names**, the default bolt for three and an arrow for the Ranger, which holds fire on the move with its bow down, earns a volley from its tree and glows cyan when one is ready, rolls the way it goes, and kills a Husk in 3 to 5 arrows to stage 16. **Beside the run, a Ranger sandbox** ([RS-02a](tasks/RS-02a-a-playable-ranger.md)) plays a bow on the game's stick and camera. Milestone by milestone: the [archives](archive/). |
| **What does not work yet** | *(1)* **Hits to kill leave GD §12.4's band** — the Gravecaller over it from stage 9, the Emberwright under it to 9 — and the Warden leaves §9.1's near 33: [M7 row 2](ROADMAP.md#carry-forward-into-m7), M8-05. *(2)* **The Sanctum plays as zero**: nothing bought unprompted in two natural runs — [row 9](ROADMAP.md#carry-forward-into-m7). *(3)* **A Pact is one offer in 62** — [row 10](ROADMAP.md#carry-forward-into-m7), specced as M7-04a to M7-04k. *(4)* **Past stage 26 the late waves are all Bloaters** — [row 11](ROADMAP.md#carry-forward-into-m7), diagnosed and specced as M7-02b. *(5)* **The feel verdict is deferred to M8-01** by the owner's ruling, and no second language ships ([parking lot](ROADMAP.md#parking-lot)). *(6)* **A boss cannot run a `SkillRunner`, and the arena only reacts**; not saved, by ruling: cooldowns, shields, live zones. *(7)* **Unbuilt by design:** no audio, no Elites, one boss, two arenas; Tether, Rot Nova and the Keystones are M7-04's. |
| **Known issues** | *(1)* **The rings are circles now, not yet seen in a Jungle stage** — [UI-10a](tasks/UI-10a-the-telegraph-ring-texture.md)'s manual step. *(2)* **`ArenaView.OnValidate` warns "0 spawn point(s)" for both arena prefabs on every domain reload**; they author 8 and 7, and reload-time `Transform` references are fake-null ([Traps §1](../Traps.md)). *(3)* **`PlayerStat` lies by name** — it carries `ContactDamage`, which `PlayerStats.Resolve` refuses; the owner's ruling, owed since M4-01a. *(4)* **M3-14c's nine labels were never witnessed in Play.** *(5)* **Environmental:** a PlayMode row can fail on Unity's AI Assistant (*NoSubscription*, or a token refresh timing out); `ProjectSettings/TimeManager.asset` re-serialises whenever the Editor touches the project, so revert it before every handover ([Traps §5](../Traps.md)); the disk sits near full. |
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

### 2026-09-25 · RS-03c · The Ranger joins the roster

**Built:** [RS-03c](tasks/RS-03c-the-ranger-joins-the-roster.md). `Ranger.asset` is the fourth class, free and owned from the start: 90 HP and 3.2 m/s; a 13-damage bow at 2.2 shots/s that holds fire on the move; a 5 m roll that deals nothing; a volley of 3 arrows at 30° and ×1.5. It wears `Bodies/Ranger` and flies `Arrow.prefab`. Its tree is three chains of three one-node tiers, Quick Draw (+12 % fire rate a rank), Volley (`Every` 5, 4, 3) and Fleet Foot (+8 % speed a rank), with no Pact. `BootScope` lists all of it. There are 23 English rows, Pseudo is regenerated, and `ClassSelect.prefab` lays out four 420-wide cards. The bow sits at X 45, Z 180 in the hand, the owner's angle from review.

**Verified:** **3 262 EditMode — 3 261 / 0 / 1 inconclusive (+11)**, on two full passes, and **PlayMode 56 / 0 / 0 (+1)**, the Ranger's body case, through `TestRunnerApi`, on the final tree. The pass before it failed `Loop_ShootsOnlyStandingStill` ([Traps §8](../Traps.md)); the row then passed alone after a reload, both with the bow turned and at `dev`'s identity. **Red-checked:** Card3 at x = 800 and Volley III at 0 turned exactly their three rows red, 8 / 3. Console after a cleared EditMode pass: 46 entries, each from a passing negative-path row, none from the Ranger. Unity's compiler with the analyzers: zero warnings. `dotnet format` clean over 11 files, `TimeManager.asset` reverted, `run.json` restored. Branched from `dev` at #173, RS-03b's merge, opened and merged on the owner's green light.

**Deviations:** *2*, in the spec's *As built*: three ripple rows beyond the Files table's list, in `EmberwrightTreeTests`, `GravecallerTreeTests` and `ClassSelectPresenterTests`; and the owner's bow angle on `Bodies/Ranger.prefab`, with the sandbox row that pinned identity. The spec's "four lists" in `BootScope` are three. None changes a decision.

**Learned:** **`LevelUpFlow.Choose` redraws at once while a pick is owed**, so only the first of several owed picks is `IsLevelUpPending`; a fixture that takes ranks in a row asks `HasOffer` too → spent (stays here). · **Saving `English.asset` through `SerializedObject` re-folds rows it did not touch**, three of them here, which were restored by hand → spent.

**Follow-ups:** RS-03d is now unblocked. [Parking lot](ROADMAP.md#parking-lot) gains one line: four classes fill the splash page's three lender cards, and a fifth would drop one silently. The Ranger's price and V1 question stays there.

### 2026-09-26 · RS-03d · The Ranger, seen

**Built:** [RS-03d](tasks/RS-03d-the-ranger-seen.md). `RangerAnimatorView` raises the bow only with a target and fire not held, so `HoldFireChanged` lowers it and raises it again. It shows `_volleyGlow` while `VolleyReady` says so. `ChargeStarted` starts a roll in the dash's direction, read in the body's frame, and `ChargeEnded` ends it. `PlayerDied` latches the glow out and the bow down. `AC_Ranger` gains a `Dodge` 2D blend on `DodgeX` and `DodgeZ`, entered on `Dodging`, and holds the arms in `Empty` for the roll. The blend plays in-place copies of KayKit's four `Dodge_*`. `Bodies/Ranger` wears a cyan `VolleyGlow` on the bow. `RangerSandboxLoop` names its target while it runs and holds fire with `HoldFireChanged`.

**Verified:** **3 269 EditMode — 3 268 / 0 / 1 inconclusive (+7)** and **PlayMode 57 / 0 / 0 (+1)**, each a full pass on the final tree through `TestRunnerApi`. **Red-checked:** `Roll_TheBodyStaysInItsCapsule` failed on KayKit's clips, with the hips 0.54 m out on the backward roll, and passed on the copies. Three red passes on the way were environmental (Learned). Console after the last EditMode pass: 125 entries, each from a passing negative-path row or the direct-Play seed, none from the Ranger. Unity's `csc` with the analyzers: zero warnings over the three assemblies touched. `dotnet format` clean over 4 files, `TimeManager.asset` reverted, `run.json` restored. Branched from `dev` at #174, RS-03c's merge, opened and merged on the owner's green light.

**Deviations:** *2*, in the spec's *As built*: `Empty`'s two exits also wait for the roll, and two ripple rows in `RangerSandboxTests`. The in-place clips are rule 4's own branch. None changes a decision.

**Learned:** **KayKit's `Dodge_*` move the mesh up to 0.54 m with root motion off**, because no root node extracts it → spent here; the Knight's `Charge` plays one → [parking lot](ROADMAP.md#parking-lot). · **A prefab re-saved in a live domain leaves an asset's field holding a second managed object, and `Is.SameAs` fails between two `<Ranger>`s** → [Traps §5](../Traps.md). · **`Loop_ShootsOnlyStandingStill` failed a full pass on `dev`'s own code, with the change stashed** → [Traps §8](../Traps.md). · **`Store_RoundTripsClaimed` hit the file lock again, 34 / 34 alone** → Traps §7 already says it.

**Follow-ups:** one [parking-lot](ROADMAP.md#parking-lot) line, the Knight's forward-dodge snap. RS-03e, the Ranger's acceptance, is now unblocked.

### 2026-09-26 · RS-03e · The Ranger's acceptance

**Built:** no production code. [RS-03e](tasks/RS-03e-the-rangers-acceptance.md) rebuilt M6-11's hits-to-kill probe with a temporary PlayMode driver in place of the owner's thumbs, ran one natural Descent run as the Ranger to stage 17, and reverted it with its temporary 100 000 HP. **Every stage from 1 to 16 is inside GD §12.4's 3–5**: median 3 at 1–2, 4 from 3, 5 at 12, 14 and 16, over 135 Husks with none left out. Arrows alone reach 5 at stage 9 and, by arithmetic, 6 near 23: [row 2](ROADMAP.md#carry-forward-into-m7)'s. No number moved. The spec's manual steps are the owner's play list: the roll, and every number with where it lives.

**Verified:** **3 269 EditMode — 3 268 / 0 / 1 inconclusive (+0)** and **PlayMode 57 / 0 / 0 (+0)** on the final tree, through `TestRunnerApi`. The first PlayMode pass ran 56 / 1 on `BootSmokeTests`' Descend row with no `run.json` on disk, and it passed 3 / 3 alone (Learned). The instrument's run took 949 game seconds in 291 real, with no error. Console after EditMode: 46 entries, each from a passing negative-path row. No C# is in the change. `TimeManager.asset` reverted, the owner's `profile.json` untouched, and the runs' `run.json`s removed. Branched from `dev` at #175, RS-03d's merge, opened and merged on the owner's green light.

**Deviations:** *3*, in the spec's *As built*: the driver also steps in and unsticks; the splash's scoring tied and took the first branch; six runs for one clean. None changes a decision.

**Learned:** **`Descend_StartsARun_AndRefusesASecondTap` taps Continue whenever a run is on disk**, because Continue comes first in `Menu.unity`; with none it taps Descend, which has had no guard since M5-07 → RS-03f. · **A planted Ranger's bow goes silent while a Spitter at 10–12 m holds the target** — CC §3.2's priority, a 12 m acquire, a 10 m bow → the owner's play list. · **A fixed 1/60 s step through `Time.captureDeltaTime` played sixteen stages in five minutes, unfocused** → spent (*As built*). · **Two runs ended on the Editor's toolbar Play button**, named by a `playModeStateChanged` hook → spent.

**Follow-ups:** RS-03f opened on the ROADMAP. Row 2 gains the Ranger's curve.

### 2026-09-26 · RS-03f · The Descend row taps Descend

**Built:** [RS-03f](tasks/RS-03f-the-descend-row.md). `BootSmokeTests.Descend_StartsARun_AndRefusesASecondTap` reads each control from the field its presenter wires. It taps Descend and then the first owned card, and asserts a fresh run of that class, with every card dead, before Run loads. It used to tap the Menu's first `Button`, which is Continue whenever a run is on disk. `ResumeFlowTests.Menu_ContinueRefusesASecondTap` now holds the Continue guard, which the old row asserted only by accident. No production code.

**Verified:** **3 270 EditMode — 3 269 / 0 / 1 inconclusive (+1)**, with a `run.json` on disk and without one, through `TestRunnerApi`. `BootSmokeTests` went 3 / 3 both ways. **PlayMode 57 went 56 / 1 both ways.** The red row is `RangerSandboxTests.Loop_ShootsOnlyStandingStill`, which also failed on `dev`'s code and went 57 / 57 on the one pass with Unity the active application (Learned). **Red-checked:** the old row went 2 / 1 with no save, and each new row went red with its guard taken out. Console after EditMode: 46 entries with no save and 47 with one, each from a passing negative-path row or the direct-Play seed. Zero analyzer warnings, `dotnet format` clean over 2 files, `TimeManager.asset` reverted, and the owner's `run.json` and `profile.json` byte-identical. Branched from `dev` at #176, RS-03e's merge. RS-03e's branch had never been pushed, so it was pushed as committed, then opened and merged on the owner's green light.

**Deviations:** *2*, in the spec's *As built*: `Traps.md` joined the Files table, and `RunBodyTests`' remark is left for RS-03g. Neither changes a decision.

**Learned:** **A row that finds a control by its position taps whichever control is on screen, and a PlayMode row that boots the game reads the machine's real saves** → spent (the spec's diagnosis). · **`Loop_ShootsOnlyStandingStill` fails after `FrameOrderTests` (46 / 1) and passes alone or after `BootSmokeTests`. Seven full passes failed it, and the one with Unity active passed** → [Traps §8](../Traps.md).

**Follow-ups:** RS-03g on the ROADMAP, promoted from the parking lot (rule 6).

### 2026-09-26 · RS-03g · The suite keeps the saves

**Built:** [RS-03g](tasks/RS-03g-the-suite-keeps-the-saves.md). `SaveShelter` is an `IPrebuildSetup` and `IPostBuildCleanup` in `Tests.PlayMode`. It moves `run.json` and `profile.json` out of `persistentDataPath` before a PlayMode run enters Play, marks a file that was absent, and moves both back after the run leaves. All six PlayMode fixtures carry it, so every row meets a fresh install. A killed pass is restored by the next one. No production code.

**Verified:** **3 270 EditMode — 3 269 / 0 / 1 inconclusive (+0)** and **PlayMode 67 / 67 / 0 (+10)**, on the pass queued with Unity active, through `TestRunnerApi`. The owner's two files came back byte-identical after all six passes on this tree, four with a `run.json` and two without. The pass on `dev`'s code replaced the `run.json`. **Red-checked:** with the attributes off, the guard row failed and the owner's `run.json` was replaced. The five other passes on this tree each failed `RangerSandboxTests.Loop_ShootsOnlyStandingStill`, as the pass on `dev`'s code did (Learned). Console after EditMode: 46 entries, each from a passing negative-path row. Zero analyzer warnings, `dotnet format` clean over 7 files, `TimeManager.asset` reverted. Branched from `dev` at #177, RS-03f's merge, which was opened and merged on the owner's green light.

**Deviations:** *2*, in the spec's *As built*: `Traps.md` §8 joined the Files table, and the parking lot gains the Continue row. Neither changes a decision.

**Learned:** **An assembly-level `[PrebuildSetup]` compiles and does nothing unless the assembly references `UnityEditor.TestRunner`** → [Traps §7](../Traps.md). · **BlueStacks takes the foreground back from Unity. A pass queued by a command that waits for `isApplicationActive` runs active, and active is not enough: of two such passes, one passed the sandbox row and one failed it** → [Traps §8](../Traps.md). · **An AI Assistant *"Token refresh timed out"* warning fails whichever PlayMode row is running** → Known issue 5.

**Follow-ups:** one [parking-lot](ROADMAP.md#parking-lot) line: a PlayMode row for Continue, end to end.

### 2026-09-26 · M7-00d · Specs for the eighty-one nodes

**Built:** eleven specs where [the table](ROADMAP.md#m7--content-pass) had one title — [M7-04a](tasks/M7-04a-a-pact-on-every-node.md) a Pact on every node · [M7-04b](tasks/M7-04b-a-burst-around-you.md) a burst · [M7-04c](tasks/M7-04c-a-buff-that-runs-out.md) a buff that runs out · [M7-04d](tasks/M7-04d-an-upgrade-that-adds-to-a-cast.md) an Upgrade that adds to a cast · [M7-04e](tasks/M7-04e-the-oathbounds-keystones.md) to [M7-04g](tasks/M7-04g-the-emberwrights-keystones.md) each class's Keystone mechanisms · [M7-04h](tasks/M7-04h-the-oathbounds-twenty-seven.md) to [M7-04j](tasks/M7-04j-the-emberwrights-twenty-seven.md) three trees of 27 · [M7-04k](tasks/M7-04k-a-full-tree-still-tempts.md) a full tree that still tempts. 72 of 81 nodes carry a Pact. M7 is 34 rows, 25 of them counted.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays RS-03g's **3 270 EditMode (3 269 / 0 / 1 inconclusive)** and **PlayMode 67 / 67 / 0**. Every member, count and asset field a rule names was read or grepped, five areas by research agents, and the 36 shipped nodes were read off their assets by a script. A second reader checked M7-04c to M7-04g against the code and found 33 defects. The readers for the other six dropped their connections three times, so those were read directly, and nine more were found. All 42 are fixed; the costliest were a release that dropped a break, `PlayerAttacked` placed in the wrong method, a Bloom drain that never reached −1, a speed row `EnemySystem.Spawn` would skip, and a buff that could strip its own node's Pact. A script resolved every link and anchor. Branched from `dev` at #178, RS-03g's merge, opened and merged on the owner's green light.

**Deviations:** *3*. **Eleven specs, in ID order** — the order is the dependency. **Two readings of CH, made in the specs**: The Host's *"50 % HP"* is −35 % damage because nothing hurts a Wight, and the Emberwright's *Overflow* is **Surge** because the toast owns the word. **One mechanic read from GD §10.3 and §13.2**: past a full tree an owned node can be corrupted (M7-04k), and the owner's ruling against it deletes that task.

**Learned:** **A saved take order is gated, and a refused resume strands the run** — no catch in `RunTicker.Start`, `run.json` kept → [parking lot](ROADMAP.md#parking-lot); every shipped node stays put (M7-04h). · **No sweep but `SkillTree`'s reads a Pact's effects** → M7-04a rule 5. · **Nothing hurts a Wight** → parking lot, M7-04f, M7-04i. · **The zone table throws on a ninth** → M7-04g rule 5. · **The swing is drawn at a fixed 60°, and the Banish list stops at twelve** → M7-04h.

**Follow-ups:** [row 10](ROADMAP.md#carry-forward-into-m7) re-owned to M7-04a–k; rows 2 and 3 gain M7-04. Parking lot: two lines added, one struck, two answered. M7-00e waits on the audio ruling.

### 2026-09-29 · UI-01a · The HUD's art and its font, imported

**Built:** UI-01a. The owner's pieces for the top band, 16 under `Art/UI/Hud/` and 3 under `Art/UI/Icons/`, copied from `UI_v2/png/` and imported as UI sprites: no mipmaps, ASTC 4×4 on Android, and 9-slice borders on the eight that stretch. Lilita One is `Art/UI/Fonts/LilitaOne_SDF`, a static 1024² atlas of 160 glyphs on TMP's mobile SDF shader, outlined and shadowed in the new `Palette.Ink` `#0F1419`, with its OFL licence beside it. The [UI track](ROADMAP.md#ui--the-hud-in-the-new-art-a-side-track-the-top-band-first) opens with six specs, UI-01a to UI-01f, and CLAUDE.md's naming table gains UI sprites and fonts. Nothing draws the art yet.

**Verified:** **3 615 EditMode — 3 614 / 0 / 1 inconclusive (+106 against `dev` at #206)**, through `TestRunnerApi`; `HudArtTests` 105 rows and `Ink_IsTheArtsOutline`. Console after EditMode: 16 errors, each from a passing negative-path row. Zero analyzer warnings, `dotnet format` clean over 3 files, `TimeManager.asset` untouched. Branched from `dev` at #206, M7-05r's merge.

**Deviations:** *2*, in the spec's *As built*: `.gitattributes` gains the licence's `-whitespace` line, and the font's numbers are the build's. Neither changes a decision.

**Learned:** **A static TMP font asset is made from a command**: create it dynamic, add the characters, set it static, then add the atlas and the material to the asset as sub-objects → spent (the spec's *As built*).

**Follow-ups:** one [parking-lot](ROADMAP.md#parking-lot) line: the HUD's pieces are separate textures.

### 2026-09-29 · UI-01b · The health bar, the XP strip and the level star

**Built:** UI-01b. The player's row in the owner's art: `HealthBar_Frame` with a `Fill_Bar` fill, ghost and gloss in its well, each as wide as its fraction; the ghost in `Palette.PlayerGhost` `#A5F3FC`, not the prefab's gold; below a quarter the fill beats between `Palette.Player` and `Palette.PlayerLow` `#0E7490`; `72 / 120` inside the bar in Lilita. The level sits in a 44 dp `LevelStar` with the Aegis ring behind it, and the XP strip is an 8 dp `XpStrip_Track`. `HudLayout` holds the band's dp numbers; the bar takes what the row leaves, 240 dp landscape and 201 at 9:20. The boss band and the skill row moved under the new row.

**Verified:** **3 643 EditMode — 3 642 / 0 / 1 inconclusive (+28)** and **PlayMode 66 / 1 / 0**, through `TestRunnerApi`. The one is `RangerSandboxTests.Loop_ShootsOnlyStandingStill`, *"No arrow on the run"*, which failed alone too (28 / 1) with `isApplicationActive` false throughout ([Traps §8](../Traps.md)); it touches no HUD code. Captured in Play: landscape on the Device Simulator at 480 dpi, and 9:20 as the Game view at its dp-equivalent 308 × 686, in `Soulvail_GeminiSamples/UI_v2/captures/`. Zero Console errors, zero analyzer warnings, `dotnet format` clean over 14 files, `TimeManager.asset` reverted.

**Deviations:** *5*, in the spec's *As built*; none changes a decision. The row that reads a run's numbers lives in `GrantedShieldTests`, which builds one.

**Learned:** **The Device Simulator gives a phone's dpi to a capture, and refuses an orientation Player Settings do not allow** → spent (the spec's *As built*). · **A field initialiser split across lines under an attribute fails `dotnet format`** → spent.

**Follow-ups:** two [parking-lot](ROADMAP.md#parking-lot) lines: the empty Aegis ring behind the star, and the skill row past eight cells at 9:20.

### 2026-09-29 · UI-01c · Enemies left, in core

**Built:** UI-01c. `SpawnDirector.Unspawned` is the bodies a stage will still stand up — each started wave's queued and telegraphed, every later wave's `BodyCount`, a boss until it stands — summed from the bookkeeping it already kept. `RunState.EnemiesLeft` is that plus `EnemySystem.LivingCount()`, copied by `RunSession.Tick` after the flow, as `StageIndex` is. It only falls: a corpse is not left, the arrival and the Sanctum read 0, and the clear reads 0 on its tick. No UI yet; UI-01d's chip reads it.

**Verified:** **3 656 EditMode — 3 655 / 0 / 1 inconclusive (+13)** and **PlayMode 66 / 1 / 0**, through `TestRunnerApi`; the one PlayMode failure is Traps §8's sandbox row, Unity inactive. Zero Console errors, zero analyzer warnings, `dotnet format` clean over 6 files, `TimeManager.asset` reverted.

**Deviations:** *3*, in the spec's *As built*: the session rows live in `StageFlowTests` and the boss row in `BossPhasesTests`, beside the fixtures that build a flow and a boss, and one row asserts an invariant instead of a count. None changes a decision.

**Learned:** **A director with a large wave stands several bodies per `TickThrough` step, so a row about a count per tick has to tick finer** → spent (the spec's *As built*).

**Follow-ups:** none.

### 2026-09-29 · UI-01d · The stage chip, the enemies-left chip and the pause button

**Built:** UI-01d. `StageReadout`, a fifth view on `Hud.prefab`, draws *STAGE 3* over *Wave 2/3* in a `Pill_Dark` left of the pause button, and the stage alone through the arrival; `ui.hud.stage` and `ui.hud.wave` are keys, in `Palette.HudCaption` `#CCC4B7` and `HudText`. Under the pause button a `Chip_Counter` with a skull shows `RunState.EnemiesLeft` for the last five and hides at 0. The pause button wears `Button_Pause` at the band's margin. The Essence counter and the Veilrot meter step down below the band, still wired.

**Verified:** **3 673 EditMode — 3 672 / 0 / 1 inconclusive (+17)** and **PlayMode 66 / 1 / 0**, through `TestRunnerApi`; the one PlayMode failure is Traps §8's sandbox row, Unity inactive. Captured in Play at 480 dpi landscape and at 9:20's dp-equivalent, in `Soulvail_GeminiSamples/UI_v2/captures/`: the corner matches the mockup's. Zero Console errors, zero analyzer warnings, `dotnet format` clean over 15 files, `TimeManager.asset` reverted.

**Deviations:** *6*, in the spec's *As built*; none changes a decision. `HudLayoutTests` moved the skill row 2 dp down and the Essence counter over the meter, and `Hud_NothingElseMoved` raised *STAGE* to Android's 12 sp floor.

**Learned:** **TMP's `SetText(string, float)` allocates a string in the Editor alone, so an EditMode allocation row over a changing count always fails** → [Traps §9](../Traps.md). · **Saving `Run.unity` from a command carries every pending TMP override on its prefab instances; wire a field by hand when the Editor's diff is not the change** → spent (the spec's *As built*).

**Follow-ups:** none.

### 2026-09-29 · UI-01e · The auto-cast row: icons, sweeps, glows and the volley's pips

**Built:** UI-01e. `AutoCastRow` draws 34 dp `SkillCell_Frame` cells, each with its skill's icon from the new root `SkillIconBook` (built at boot from `SkillDefinition._icon`, or `Icon_Placeholder`), a `Sweep_Disc` shade over the cooldown left in `Palette.Ink`, and a `Glow_Soft` in `Palette.Player` when ready. The class's volley heads the row once a node gives it a count: `CharacterDefinition._volleyNode` names the node it is drawn as (the Ranger's Volley, wearing `Icon_Volley`), and five pips under it fill a shot at a time from three new `RunState` reads. No other Passive makes a cell; the kind tint is gone.

**Verified:** **3 692 EditMode — 3 691 / 0 / 1 inconclusive (+19)** and **PlayMode 66 / 1 / 0**, through `TestRunnerApi`; the one PlayMode failure is Traps §8's sandbox row, Unity inactive. Captured in Play at 480 dpi landscape and 9:20's dp-equivalent, in `Soulvail_GeminiSamples/UI_v2/captures/`: the top band now reads as the mockup's. Zero Console errors, zero analyzer warnings, `dotnet format` clean over 15 files, `TimeManager.asset` reverted.

**Deviations:** *6*, in the spec's *As built*; none changes a decision. A 9:20 portrait holds seven cells, not nine, beside UI-01d's chip; the volley's arrival is polled, because its node is a Passive.

**Learned:** **With a compile error in any assembly the Editor reloads none, so a command that dresses a prefab by a new field meets the old type** → spent (the spec's *As built*). · **A `ScreenCapture` queued in the command that shows an object can land a frame before it draws** → spent.

**Follow-ups:** the [parking-lot](ROADMAP.md#parking-lot) line on the row's capacity now reads seven cells.

### 2026-09-30 · UI-01f · The boss bar in the new art

**Built:** UI-01f. The boss band is `BossBar_Frame`, 18 dp tall, at UI-01b's place. In its well runs a sliced `Fill_Bar` in `Palette.Boss`, as wide as the boss's whole fraction and switched off at zero. `BossBar_PhaseSeam` marks sit untinted at the authored thresholds, a little taller than the well. The frame's dark well is the track now, so the faint bone track and `_trackAlpha` are gone. **UI-01 is complete.**

**Verified:** **3 696 EditMode — 3 695 / 0 / 1 inconclusive (+4)** and **PlayMode 66 / 1 / 0**, through `TestRunnerApi`; the one PlayMode failure is Traps §8's sandbox row. Captured in Play at 480 dpi landscape and at 9:20's dp-equivalent, in `Soulvail_GeminiSamples/UI_v2/captures/`, with the band bound to three phases at 80 %. The mockup draws no boss, so the band was checked against the art. Zero Console errors, zero analyzer warnings, `dotnet format` clean over 3 files, `TimeManager.asset` reverted.

**Deviations:** *5*, in the spec's *As built*; none changes a decision. The frame is the band's own image rather than a child, and four rows changed expectation beyond the spec's list, the seams' height among them.

**Learned:** **A PlayMode pass queued from a command saved the dirty `Run.unity`, with TMP's shader-channel overrides in it**, and the first request after the EditMode pass ran nothing and logged nothing. Revert the scene after the pass, and re-queue a pass that never starts → spent.

**Follow-ups:** none. The UI track's next row is the owner's.

### 2026-10-12 · UI-02a · The lower controls' art, imported

**Built:** UI-02a. Eight sprites for the lower HUD controls imported from `UI_v2/png/` with phone settings: `Stick_Base` and `Stick_Knob` for the virtual stick, `RollButton_Ready`, `RollButton_Cooling`, `RollButton_Pressed` and `Icon_Roll` for the Roll button, and `SkillButton_Ready` and `SkillButton_Cooling` for manual skill buttons. Texture type Sprite, single, full-rect, 100 PPU, clamped, bilinear, no mipmaps, ASTC 4×4 on Android, and unsliced (`spriteBorder` zero). `LowerControlsArtTests` pins the assets, settings, and cut dimensions. The UI track is planned in ROADMAP through UI-02c, with specs for UI-02a–c written.

**Verified:** **3 746 EditMode — 3 745 / 0 / 1 inconclusive (+50)** through `TestRunnerApi`; `LowerControlsArtTests` 50 rows and `HudArtTests` 105 rows green. Console after EditMode: 16 expected errors from passing negative-path tests, zero compiler errors, zero analyzer warnings, `dotnet format whitespace` clean over `Assets/_Project/Tests/Game/Art/`, `TimeManager.asset` untouched.

**Deviations:** *1*, in the spec's *As built*: `HudArtTests.cs` relaxed its total piece assertion to a subset check (`PiecePaths().IsSubsetOf(found)`) so the 8 new lower control sprites do not fail UI-01's check, while `LowerControlsArtTests` pins the combined 27 count.

**Learned:** **A phone control sprite imported without slicing needs full-rect mesh to avoid edge bleeding when packed** → spent. · **Unity's CLI pipeline reloads the domain and restarts its communication port on assembly changes** → spent.

**Follow-ups:** none. UI-02b owns dressing the floating stick.

### 2026-10-12 · UI-02b · The floating stick in the new art

**Built:** UI-02b. `FloatingStick`'s `_base` and `_knob` on `Hud.prefab` wear `Stick_Base` and `Stick_Knob` with `preserveAspect = true` and untinted `Color.white`. `FloatingStick.Awake` sizes both elements to their 120 dp and 52 dp specifications via `StickShaper.PixelsPerDp(Screen.dpi)` and ensures native aspect preservation. Dynamic touch-down placement within the left 45% region, drag recentring past 60 dp (`StickShaper.MaxRadiusDp`), touch-up release hiding, single-touch capture, and virtual gamepad left stick routing (`<Gamepad>/leftStick`) are preserved intact.

**Verified:** **3 754 EditMode — 3 753 / 0 / 1 inconclusive (+8)** through `TestRunnerApi`; `FloatingStickTests` 8 rows, `LowerControlsArtTests` 50 rows and `HudArtTests` 105 rows green. Console after EditMode: 16 expected errors from passing negative-path tests, zero compiler errors, zero analyzer warnings, `dotnet format whitespace` clean over `FloatingStick.cs` and `FloatingStickTests.cs`, `TimeManager.asset` reverted.

**Deviations:** none.

**Learned:** **EditMode `Object.Instantiate` does not run `Awake` on newly instantiated prefabs; fixtures testing runtime initialization in EditMode invoke `Awake` via reflection** → spent.

**Follow-ups:** none. UI-02c owns the Roll and manual skill buttons.

### 2026-10-12 · UI-02c · The Roll and skill buttons in the new art

**Built:** UI-02c. `SkillButton` (Roll) and `ManualSkillButton` (`S1`..`S4`) on `Hud.prefab` wear the Clash-style art. Roll displays `RollButton_Ready` with child `Icon_Roll`, swapping to `RollButton_Pressed` on pointer down, `RollButton_Cooling` while cooling, with CC §5's input buffer intact. Manual skill buttons wear `SkillButton_Ready`, and `SkillButton_Cooling` while cooling; CC §6.2's dimming and dead tap are unchanged. **UI-02 is complete.**

**Verified:** **3 759 EditMode — 3 758 / 0 / 1 inconclusive (+5)** through `TestRunnerApi`; `SkillButtonTests` 6 rows, `SkillBarPresenterTests` 30 rows, `FloatingStickTests` 8 rows, `LowerControlsArtTests` 50 rows green. Zero Console errors, zero analyzer warnings, `dotnet format whitespace` clean over touched files, `TimeManager.asset` reverted.

**Deviations:** none.

**Learned:** **`RunState.Combat` is `internal` in `Soulvail.Core`, so movement cooldown tests drive it via public `session.MovementSkill()` and `session.Tick`** → spent (stays here).

**Follow-ups:** none. UI-02 is complete.

### 2026-10-13 · UI-02d · A fixed stick

**Built:** UI-02d. By the owner's ruling of 2026-10-13, `FloatingStick` is fixed: base and knob always visible at 110 × 110 dp from the safe area's bottom-left (`_fixedOffsetDp`), a touch anywhere in the left 45 % read from that centre, no recentring, knob clamped at the 60 dp rim. The floating behaviour is kept behind `_floating` (off). CC §2.1–2.3 rewritten to match.

**Verified:** **3 763 EditMode — 3 762 / 0 / 1 inconclusive (+4)** through `TestRunnerApi`; `FloatingStickTests` 12 rows green. Zero compiler errors, `dotnet format whitespace` clean over touched files, `TimeManager.asset` reverted.

**Deviations:** none.

**Learned:** **`SendValueToControl` is a no-op in EditMode (no virtual gamepad), so the stick remembers the last value it reported for tests to read** → spent.

**Follow-ups:** the 110 × 110 dp offset is untuned; tune on a device (deferred).

### 2026-10-13 · UI-03a · Skill points and the free pick, in core

**Built:** [UI-03a](tasks/UI-03a-skill-points-in-core.md). Behind `ModeSpec.HasFreePick` (off in Descent and the Jungle) a level banks a point and opens no offer; `IProgressionCommands` gains `SkillPoints`, `TakeNode(id)` and `SettleFullTree()`. `RunTicker.LevelUpPhase` settles a full tree's points as Overflow; Reroll and Banish are refused while free. No view.

**Verified:** **3 775 EditMode — 3 774 / 0 / 1 inconclusive (+12)** through `TestRunnerApi`; PlayMode 67 / 1 / 0 (+1; the one is `RangerSandboxTests.Loop_ShootsOnlyStandingStill`, Unity inactive). Console clean of errors, `dotnet format whitespace` clean over touched files, `TimeManager.asset` reverted.

**Deviations:** 8, in the spec's As built; none changes a decision. The widest: `CanBuy(Banish)` is false while free too, not only Reroll.

**Learned:** **a new serialized field on `ModeDefinition` fails `Descent_EveryYamlKeyBindsToAField` until every mode asset carries the key, and the failed run rewrites `Descent.asset` itself** → spent (Traps §7 already says why). **A `File.Delete` in a `Unity_RunCommand` refuses the whole command as "user interactions"** — already [Traps §4](../Traps.md#4-the-unity-mcp) (M3-01b); not read first, which cost two probes → spent.

**Follow-ups:** none. `TreeViewPresenterTests` asserts the tree screen holds no `IProgressionCommands` (CH §5.1); UI-03c reverses it.

### 2026-10-13 · UI-03b · The level-up banner and the point badge

**Built:** [UI-03b](tasks/UI-03b-the-point-badge.md). `PointBadge` on `Hud.prefab`: a green count of `RunState.SkillPoints` on the star's upper-right corner, hidden at 0, and a 1.5 s unscaled "LEVEL UP!" toast above the arena's centre that blocks no touch. Off unless the run is free. `HudPresenter` binds and places it; `Palette.Available`, `ui.hud.levelup`.

**Verified:** **3 788 EditMode: 3 787 / 0 / 1 inconclusive (+14)** through `TestRunnerApi`; PlayMode not run, nothing in the frame order moved. Console clean of errors from this task, `dotnet format whitespace` clean over nine files, `TimeManager.asset` reverted.

**Deviations:** 9, in the spec's As built. The one that changes how a later task reads: **`NodeTaken` fires before the point is spent**, so the badge flushes in `LateUpdate`.

**Learned:** **a text, a key or a colour added to the HUD trips four guard rows (`Palette` member count, `Hud_NoLongerOwnsTheDeathOverlay`, `Hud_PrefabHasNoOverlay`, `Hud_NothingElseMoved`)**; they are the checklist, edit them in the same change → spent. **`NodeTaken` is published mid-operation: a handler that reads state there sees the old one** → UI-03c.

**Follow-ups:** UI-03c reads `SkillPoints` after `NodeTaken` returns, not in the handler. The disc sprite and the 20 dp size are placeholders for the owner's art and a device.

### 2026-10-13 · UI-03c · The tree screen: pause, look, take

**Built:** [UI-03c](tasks/UI-03c-the-tree-screen.md). In a free run the level star opens the tree screen over its own `PauseReason.Tree`, or not at all when anything else holds the pause. The screen is a grid with tier 1 at the bottom; its header shows the points and Close, and Android Back closes it. A tap on an Available cell with a point banked calls `TakeNode` and redraws, reading the points after it returns. A drawing run gets the same screen read-only. `TreeNodeView` became `TreeNodeButton` in place; `ui.tree.points`.

**Verified:** **3 805 EditMode: 3 804 / 0 / 1 inconclusive (+16)** through `TestRunnerApi`; **PlayMode 68 / 0 / 0** (`Loop_ShootsOnlyStandingStill` passed this time). Console clean of errors from this task, `dotnet format whitespace` clean over ten files, `TimeManager.asset` reverted.

**Deviations:** 10, in the spec's As built. The one a later task reads: **the screen's chrome is laid out in dp at runtime** (`PlaceChrome`). The prefab's landscape reference units ran the header off a 9:20 safe area.

**Learned:** **a screen whose cells are in dp and whose chrome is in reference units passes every landscape row and fails portrait**; `Layout_FitsPortrait` found it on the first run → spent. **`Unity_RunCommand` wraps the script in a namespace, so a nested `private` class is duplicated and refused, and `Image`/`Navigation` collide with `Unity.AI.*` namespaces**: use top-level `internal` classes and alias UGUI types → spent.

**Follow-ups:** none. UI-03d turns the flag on for the Jungle and adds the pause panel's door. The padlock sprite, the outline and every dp size are placeholders for the owner's art and a device.

### 2026-10-13 · UI-03d · The switch: pause door, the Jungle on, the docs

**Built:** [UI-03d](tasks/UI-03d-the-switch-and-the-docs.md). `Jungle.asset` has `_hasFreePick` on; Descent stays off. In a free run the pause panel's View Tree releases `Menu` and the tree screen takes `Tree` in the same call (`TreeViewPresenter.OpenHoldingPause`), and closing it takes `Menu` back; a drawing run keeps the read-only view over `Menu`. View Tree's label carries the banked points in `Palette.Available`, plain at 0 (`ui.pause.tree.points`). CH §5.1 and GD §13.1 describe banked points, with the random three under "Descent, until its classes are reshaped".

**Verified:** **3 812 EditMode: 3 811 / 0 / 1 inconclusive (+7)** through `TestRunnerApi`; PlayMode not re-run. `dotnet format whitespace` clean over the three C# files, `TimeManager.asset` reverted.

**Deviations:** in the spec's As built.

**Learned:** nothing new.

**Follow-ups:** none. The owner plays the flow in the Editor; device checks stay deferred.

### 2026-10-13 · UI-04a · The tree screen's art, imported

**Built:** [UI-04a](tasks/UI-04a-the-tree-art.md). The UI-04 ROADMAP section and specs [UI-04b](tasks/UI-04b-the-tree-frame.md) (frame), [UI-04c](tasks/UI-04c-the-class-block.md) (class block) and [UI-04d](tasks/UI-04d-select-and-learn.md) (select, then Learn), to the owner's scope: no capstone, no second class block, no stat diff. 22 sprites from `UI_v2/png`: 7 popup pieces in `Art/UI/Popup/`, 12 tree pieces in `Art/UI/Tree/`, 3 icons in `Icons/`, with the mockup's 9-slice borders on the 10 that stretch.

**Verified:** **3 923 EditMode: 3 922 / 0 / 1 inconclusive (+111)** through `TestRunnerApi`; PlayMode not re-run. Console clean of errors, `dotnet format whitespace` clean over the two C# files.

**Deviations:** none. Branched off `ui-03d-the-switch-and-the-docs`, since `origin/dev` holds none of UI-02d or UI-03.

**Learned:** nothing new.

**Follow-ups:** UI-04b–d specced. `LowerControlsArtTests`' folder count went from 27 to 30 for the three icons.

### 2026-10-13 · UI-04b · The tree screen's frame

**Built:** [UI-04b](tasks/UI-04b-the-tree-frame.md). The tree screen sits in a sliced `Popup_Panel` over the dimmed run, with a `Popup_TitleRibbon` reading `ui.tree.title` on its top edge, a `Pill_Dark` holding `Icon_SkillPoint` and the count (`Palette.Available` above 0, plain at 0, shown in a drawing run as 0), and a 48 dp `Button_Close`. `ui.tree.points` is now the bare count. All of it is placed in dp by `TreeViewPresenter.PlaceChrome`.

**Verified:** **3 930 EditMode: 3 928 passed / 1 failed / 1 inconclusive (+7)** through `TestRunnerApi`; the one failure is `LocalJsonSaveStoreTests.Store_SaveReplaces`, the Windows file lock of [Traps §7](../Traps.md), unrelated to this task. Seven new rows in `TreeFrameTests`. PlayMode not re-run. `dotnet format whitespace` clean over three C# files, `TimeManager.asset` reverted.

**Deviations:** in the spec's As built. The one a later task reads: the frame's top margin is deeper than its others, so the ribbon on its edge stays in the safe area.

**Learned:** nothing new.

**Follow-ups:** none. UI-04c lays the class block into the frame's content area. The owner checks the header against the mockup in the Editor; the Close under a thumb is device-only.

### 2026-10-13 · UI-04c · The class block

**Built:** [UI-04c](tasks/UI-04c-the-class-block.md). The tree draws as one stone block: a tile per node by state with its icon, a check, plus or lock badge and tier numeral, links up each branch lit by the lower node, "LV n" per row, the branch name under tier 1 and a class plate with an emblem when the look has one. `TreeBlockView` places all of it in dp; `TreeNodeButton` lost its name, description, stripe, outline and padlock. The Ranger's nine skills carry their branch's icon and `Ranger.asset` its emblem; `CharacterLook` gained `Emblem`.

**Verified:** **3 941 EditMode: 3 940 passed / 0 failed / 1 inconclusive (+11)** through `TestRunnerApi`; the inconclusive row is an animator clock row. Twelve new rows in `TreeBlockTests`, one deleted. PlayMode not re-run. `dotnet format whitespace` clean, `TimeManager.asset` reverted. Not seen in the Editor.

**Deviations:** five, in the spec's As built. The one a later task reads: `TreeBlockView.Arrange` owns the layout and `TreeNodeButton.Show` no longer takes a localizer.

**Learned:** nothing new.

**Follow-ups:** none. UI-04d fills the 190 dp band under the plate with the card.

### 2026-10-14 · UI-04d · Select, then Learn
**Built:** a tap on any tile selects it (`TreeTile_SelectedRing`), `TreeDetailCard` under the block shows its name and tier, tile and icon, "Level n of m" and description, and its green Learn button calls `TakeNode` once. Opening or closing the screen clears the selection; a drawing run selects and has no Learn.
**Verified:** **3 950 EditMode: 3 949 passed / 0 failed / 1 inconclusive (+9)** through `TestRunnerApi`. Nine new rows in `TreeDetailCardTests` (a partial of `TreeScreenTests`); the take rows in four files now select then press Learn. PlayMode not re-run. `dotnet format whitespace` clean. Not seen in the Editor.
**Deviations:** three, in the spec's As built. The one a later task reads: `Show` takes a trailing `free` flag so a drawing run's card has no Learn.
**Learned:** nothing new.
**Follow-ups:** none. The card's look and its 176 dp height are guesses until a phone exists.

### 2026-10-14 · UI-05a · The character page's art, imported
**Built:** `Soulvail_GeminiSamples/UI_v3/cut_sheets.py` (outside the repo) cut the 14 pieces; `Art/UI/Character/` ×11 (background included), `Popup/Button_Violet` and `_Pressed`, `Icons/Icon_SoulShard` import as full-rect 100 PPU sprites, ASTC 4×4 (background 6×6). `CharacterPageArtTests`, seven rows.
**Verified:** **4 034 EditMode: 4 033 passed / 0 failed / 1 inconclusive (+84)**, run in Unity batch mode (the Editor was not open). Contact sheet of the cut on green read by eye; magenta-leaning edge pixels 0 of 2 000–8 000 per piece, at most 4 on a button. `dotnet format whitespace` clean.
**Deviations:** five, in the spec's As built. The one a later task reads: the violet buttons needed their own key and are 1000 px wide, not the sheet's 2404.
**Learned:** `key_magenta` reads a fill near the key colour (violet) as a blend and turns it translucent blue; a button needs a distance key seeded from the corners. Spent.
**Follow-ups:** none. UI-06a's blue button waits on its sheet.

### 2026-10-14 · UI-05b · The character preview
**Built:** `CharacterPreview` (Show, Hide, Resize, Rotate) and `CharacterStage.prefab`: a disabled camera into a capped `RenderTexture`, a key and a rim light, a cylinder pedestal and a body anchor, parked at y = −500; one instance in `Menu.unity`, registered by `MenuScope` when assigned.
**Verified:** **4 042 EditMode: 4 041 passed / 0 failed / 1 inconclusive (+8)**, in Unity batch mode (no Editor open for this project); `TimeManager.asset` reverted. Eight rows in `CharacterPreviewTests`. `dotnet format whitespace` clean. Not seen in the Editor.
**Deviations:** four, in the spec's As built. The one a later task reads: `Show` switches off every `MonoBehaviour` on the body, because the Ranger's `RangerAnimatorView` throws in `Start` without a `PlayerView`.
**Learned:** `OnDestroy` does not run on an EditMode `DestroyImmediate`, so the test invokes it by hand. Spent.
**Follow-ups:** none. UI-05c calls `Resize` with its `RawImage`'s pixel size and `Rotate` from its drag.

### 2026-10-14 · UI-05c · The character page
**Built:** `CharacterPage` and a rewritten `ClassSelectPresenter`: class select is one class at a time (name, role, the 3D body from `CharacterPreview`, arrows, dots, swipe on the name band, drag to turn) with one button that is Choose, Unlock or dead. `ClassSelect.prefab` is rebuilt portrait; the start, buy and double-tap paths are unchanged.
**Verified:** **4 036 EditMode: 4 034 passed / 1 failed / 1 inconclusive (−6)**, in Unity batch mode (no Editor open); `TimeManager.asset` reverted. The failure is `CharacterPreviewTests.Resize_…` (depth 32, not 24), which fails identically on a clean `dev`. PlayMode `BootSmokeTests` 3/3. `dotnet format whitespace` clean; no new warnings. Not seen in the Editor.
**Deviations:** ten, in the spec's As built. The ones a later task reads: `MenuScope` now requires `CharacterPreview`; three strings retired, not one.
**Learned:** a retired screen's keys must go with it: the orphan sweep fails on a row no code names. Spent.
**Follow-ups:** none. UI-05d adds the skill strip to this page; UI-06b can read `ClassPageModel`.

### 2026-10-14 · UI-05d · The skill strip
**Built:** `SkillStrip` (four `SkillFrame` tiles, a selection, a three-line caption) on `CharacterPage`, laid out in dp between the body and the dots. `SkillStrip.Build` picks the tier-1 node of each branch, then the Space move; `CharacterLook` gained `SpaceIcon` and two keys, authored on the Ranger (`Icon_Roll`, "Roll").
**Verified:** **4 045 EditMode: 4 043 passed / 1 failed / 1 inconclusive (+9)**, in Unity batch mode (no Editor open for this project); `TimeManager.asset` reverted. Failure is `CharacterPreviewTests.Resize_…`, as on a clean `dev`. Nine rows in `SkillStripTests`. `dotnet format whitespace` clean. Not seen in the Editor.
**Deviations:** five, in the spec's As built. The one a later task reads: the Space move's words ride on `CharacterLook`, not on core's `MovementSkillSpec`.
**Learned:** nothing new.
**Follow-ups:** none. The strip's 118 dp height is a guess until a phone exists.

### 2026-10-14 · UI-06b · The menu screen
**Built:** `MenuPresenter.Layout(px, hasSave)` places the plate, title and buttons in dp; `Menu.unity` gained a `Safe` container (`SafeAreaFitter`), `Background`, `TitlePlate` and the dressed buttons. Descend and Continue logic untouched; no new buttons.
**Verified:** **4 063 EditMode: 4 062 passed / 0 failed / 1 inconclusive (+18 since UI-05d, of which `MenuLayoutTests` is 8 cases and UI-06a's `MenuArtTests` 10)**, Unity batch mode, no Editor open; `TimeManager.asset` reverted. `dotnet format whitespace` clean. Not seen in the Editor.
**Deviations:** hand-edited YAML scene (no Editor); see the spec's As built.
**Learned:** nothing new.
**Follow-ups:** none.

### 2026-10-14 · UI-07a · The look pass's specs
**Built:** the UI-07, UI-08 and UI-10 rows and their paragraph in the ROADMAP, a UI-09 title row, and ten specs from `_TEMPLATE.md` (UI-07b–d, UI-08a–e, UI-10a–b) plus this one; five Device Simulator bullets in [Traps §9](../Traps.md#9-rendering-canvas-and-tmp). Docs only.
**Verified:** no code changed, so no suite run; the count stays UI-06b's 4 063. Every spec link from the ROADMAP resolves; Current State measured under its cap.
**Deviations:** none. The cyan parking-lot line now points at UI-10b and stays until it merges; *Known issues* (1) points at UI-10a.
**Learned:** the look pass found six layout faults that 4 063 green rows did not, because every layout row checks the arithmetic the presenter was written with, never the picture → UI-07b, and the Simulator's traps → Traps §9.
**Follow-ups:** UI-07b–d, UI-08a–e, UI-09 (title), UI-10a–b. UI-10b holds one owner question.

### 2026-10-14 · UI-07b · The look-capture tool
**Built:** `LookCapture` (menu `Soulvail/Capture Look`, Play and Simulator only) renders the main camera and every overlay canvas through URP `SingleCameraRequest`s and composites them with the pure `LookCompositor` into `Soulvail_Looks/<Scene>_<w>x<h>_<dpi>dpi_<time>.png`; every canvas, renderer and camera is put back.
**Verified:** **4 068 EditMode: 4 065 passed / 2 failed / 1 inconclusive (+5)**, in the open Editor. `SaveWriterTests.Writer_ObservesEveryFault` failed on an Editor websocket exception and passed alone; `CharacterPreviewTests.Resize_…` fails in the open Editor (D3D12 depth 32). `LookCompositorTests` 5/5 after formatting. Captured Menu and Run at 2658 × 960 @ 480 dpi; the Game-view and no-Simulator refusals both seen. Console clean; `dotnet format whitespace` clean; manifest untouched.
**Deviations:** four, in the spec's As built. The one a later task reads: the UI pass is unpremultiplied before `Over`.
**Learned:** the UI pass reads back premultiplied; D3D12 turns depth 24 into 32 → both Traps §9.
**Follow-ups:** none. The Menu capture shows UI-07a's finding 2 (LiberationSans); finding 1's page is UI-08d's to capture.

### 2026-10-14 · UI-07c · Lilita One everywhere
**Built:** every `TMP_Text` in `Menu.unity` and nine `Prefabs/UI/` prefabs now names `LilitaOne_SDF` (material too, unless already a Lilita preset); `FontTests` holds that for all UI prefabs but DebugOverlay and the Menu, and holds TMP Settings' default at LiberationSans.
**Verified:** **4 071 EditMode: 4 069 passed / 1 failed / 1 inconclusive (+3)**, in the open Editor through `TestRunnerApi`; the one failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Menu texts measured in Play: none overflow. Console sweep shows only the suite's own expected errors; `dotnet format whitespace` clean; `TimeManager.asset` clean.
**Deviations:** nine prefabs edited, not four, and Hud was not already Lilita; in the spec's As built. No `Capture Look` PNG (no Simulator open).
**Learned:** nothing new.
**Follow-ups:** none. The tree, Pause and RunEnd are still to be looked at in a capture; overflow there is UI-08e's.

### 2026-10-14 · UI-07d · The tree's labels and centring
**Built:** below 480 dp of safe-area height the tree screen goes compact: an 88 dp horizontal `TreeDetailCard` (tile left, name, level and description middle, Learn right), the block above it with no class plate and tighter padding, branch names 8 dp clear of the card. `TreeViewPresenter.UsePixelsPerDp` lets a row lay out at a device's px per dp.
**Verified:** **4 074 EditMode: 4 072 passed / 1 failed / 1 inconclusive (+3)**, in the open Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). `TreeLayoutTests` 3/3 at 2658 × 960 @ 480 dpi and 1920 × 1080 @ 400 dpi. Console shows only the suite's own expected errors; `dotnet format whitespace` clean; `TimeManager.asset` reverted. No `Capture Look` PNG (no Simulator opened).
**Deviations:** the owner ruled the compact card after the spec's stack proved 28 dp short at 320 dp; `TreeDetailCard.cs` and `TreeBlockView.cs` edited, class plate hidden when compact. In the spec's As built.
**Learned:** nothing new.
**Follow-ups:** none. The look of the compact card is owed to a `Capture Look` of the tree at both sizes; portrait is UI-08e's.

### 2026-10-14 · UI-08a · The portrait switch
**Built:** Player Settings is portrait-only (default orientation Portrait, all four autorotate sides off) with `OrientationTests` reading it; CLAUDE.md, GD (five mentions), CC and the ROADMAP UI bullet say portrait. Landscape-derived numbers (stick 45 %, HUD diagram) are marked for UI-08c.
**Verified:** **4 075 EditMode: 4 073 passed / 1 failed / 1 inconclusive (+1)**, in the open Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Console errors are the fixtures' own logged faults. Format check clean on the new test.
**Deviations:** none. No Simulator capture (the spec asks for none); the upright/no-rotate check is the owner's.
**Learned:** nothing new.
**Follow-ups:** none. Screens misfit portrait until UI-08b to e.

### 2026-10-14 · UI-08b · The camera in portrait
**Built:** `FollowCamera` keeps the short side's 60° and derives the vertical field from the aspect (104° at 9:20), written when the aspect changes and on no other frame. Distance, pitch and `Run.unity` unchanged.
**Verified:** **4 079 EditMode: 4 077 passed / 1 failed / 1 inconclusive (+4)**, in the open Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Console holds only the fixtures' own logged faults; `dotnet format whitespace` clean on both files; `TimeManager.asset` reverted. Framing checked from an edit-mode render of `Arena_Hollow` at 1080 × 2400, not a `Capture Look` (no Simulator opened).
**Deviations:** none to the Files table; the look is a render, not the capture tool. In the spec's As built.
**Learned:** nothing new.
**Follow-ups:** none. The owner's look at the framing, and the distance if the side walls should be in, is a device step.

### 2026-10-15 · UI-08c · The HUD in portrait
**Built:** `HudPresenter.PlaceLowerControls`: in portrait the stick's zone is re-anchored to the left half of the bottom 40 %. The threat arrows' rect always starts 126 dp down, under the boss band. `DebugOverlay.Place` hangs its line, wrapped, from 122 dp. `Hud.prefab` and `DebugOverlay.prefab` scalers read 1080 × 1920. The skill cluster's offsets are a 2 × 2 grid above the Roll.
**Verified:** **4 083 EditMode: 4 081 passed / 1 failed / 1 inconclusive (+4)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. The first tally caught a staggered cluster's S1 over the Roll. The Console holds only the fixtures' own logged faults. `dotnet format whitespace` is clean on the three scripts, and `TimeManager.asset` was reverted. No `Capture Look`: no Simulator was opened.
**Deviations:** five, in the spec's As built. The one that changes a decision: CC §6.2's quarter arc cannot fit the right half of a 360 dp screen, so the four buttons are a grid. CC §6.2 is not yet rewritten.
**Learned:** at match 0.5 a `CanvasScaler`'s reference orientation changes no pixel, because the scale is `√(w·h ÷ refW·refH)`. Spent.
**Follow-ups:** one parking-lot line, the Veilrot caption over the level-up toast at 360 dp; the skill-row line now names 360 dp.

### 2026-10-15 · UI-08d · The menu and the character page in portrait
**Built:** the Menu's `Background` hangs from the canvas, not `Safe`, so the notch strip is painted; the Menu's scaler reads 1080 × 1920. `MenuPresenter` and `CharacterPage` re-lay out from `LateUpdate` through `Refit` whenever the screen size, dpi, safe area or canvas scale moves, and on no other frame. The page stacks wherever it fits with a preview of at least 120 dp, and a short, wide page lays out in three columns. At the look pass's 886 × 320 dp the preview is 170 × 140.
**Verified:** **4 094 EditMode: 4 092 passed / 1 failed / 1 inconclusive (+11)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. Both fixtures ran 27/27 alone first. The first compile failed with *Failed to create CoreCLR*, because the machine was out of commit memory: the Editor held 21 GB and 28 idle MSBuild nodes were left over. `dotnet build-server shutdown --msbuild` freed 2 GB, and the compile went through. The Console holds no errors or warnings after it. `dotnet format whitespace` is clean on the four scripts, and `TimeManager.asset` was untouched. No `Capture Look`: no Simulator was opened.
**Deviations:** five, in the [spec's As built](tasks/UI-08d-menu-and-character-page-in-portrait.md#as-built). The one that changes a decision: the page has a column layout for a short, wide screen, because rule 5's 320 dp page cannot hold the stack.
**Learned:** *Failed to create CoreCLR* (`0x8007000E`) in the Console is the commit limit, not a compile error, and the CLI's `recompile` then reports "compile errors" and refuses. Re-importing a changed script recompiles once memory is free. Not filed (Traps is outside the table); a Traps §5 candidate.
**Follow-ups:** one parking-lot line: the class screen's chrome and the preview's texture are sized only on open and on a step.

### 2026-10-15 · UI-08e · The tree, Pause and Run end in portrait
**Built:** `TreeView.prefab`, `Pause.prefab` and `RunEnd.prefab` scalers read 1080 × 1920. In portrait the tree's popup is the safe width less 16 dp a side, with the full card at the bottom and the block fitted to what it drew and centred above it. `PausePresenter.LayOutColumn` and `RunEndPresenter.LayOut` each draw one column centred on the safe area, at most the safe width less 32 dp, with 56 dp buttons and text at 22 dp or more. Both presenters gained `UsePixelsPerDp`.
**Verified:** **4 100 EditMode: 4 098 passed / 1 failed / 1 inconclusive (+6)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. New rows at 1080 × 2400 @ 420 and 720 × 1600 @ 320. The Console holds only the fixtures' own warnings. `dotnet format whitespace` is clean on the six scripts, and `TimeManager.asset` was untouched. No `Capture Look`: no Simulator was opened.
**Deviations:** five, in the [spec's As built](tasks/UI-08e-tree-pause-and-run-end-in-portrait.md#as-built). `TreeFrameTests.cs` is a sixth file, owner-approved: UI-04b's 12 dp margin row moved to landscape. Learn stays at the card's foot.
**Learned:** nothing new.
**Follow-ups:** none. UI-08 is complete; UI-09 waits on art.

### 2026-10-15 · UI-10a · A circle for the telegraph rings
**Built:** `T_TelegraphRing_Mask.png`, a 256 px white disc inscribed in the texture with an alpha-1 rim over the outer 8 % and 0.45 inside, written by the menu item `Soulvail/VFX/Generate Telegraph Ring Mask`. `M_TelegraphRing` samples it as `_BaseMap`; blend state and the five decals are unchanged.
**Verified:** **4 105 EditMode: 4 103 passed / 1 failed / 1 inconclusive (+5)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. The Console holds only the fixtures' own logged faults. `dotnet format whitespace` clean on both scripts; `TimeManager.asset` reverted.
**Deviations:** none to the Files table. The Jungle-stage look is the owner's Editor step; no capture was taken. In the spec's As built.
**Learned:** nothing new.
**Follow-ups:** none. UI-10b still waits on the owner's choice of cyan.

### 2026-10-15 · UI-11a · The profile remembers places and picks
**Built:** `PlayerProfile` v5: `FinishedModeIds`, `BestStages` (`ModeBest` rows), `SelectedCharacterId`, `SelectedModeId`, `BestStage`, and `WithFinished`, `WithBestStage`, `WithSelection`. The v4 → v5 step adds empty lists and no picks. `LocalJsonSaveStore`'s mirror writes five flat keys, the bests as two parallel arrays.
**Verified:** **4 119 EditMode: 4 117 passed / 1 failed / 1 inconclusive (+14)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. Console holds only the arena prefabs' spawn-point warnings. `dotnet format whitespace` clean on the touched folders.
**Deviations:** none to the Files table. The decoder drops a malformed best row, a repeat or an unparseable id instead of refusing the profile, as it does for the other id lists. In the spec's As built.
**Learned:** nothing new.
**Follow-ups:** none. UI-11d and UI-11e write the new fields.

### 2026-10-15 · UI-11b · Places unlock in order
**Built:** `ModeSpec.UnlockedBy` and `IsListed` (constructor arguments after `hasFreePick`; a mode naming itself is refused); `PlaceUnlocks.IsUnlocked`, `IsFinished` and `Listed`; the same two fields on `ModeDefinition`. `Jungle.asset` is finite at stage 30 (a Warden stage), listed and open; `Descent.asset` is unlisted. GD §3 and §4.5 gain the places paragraph.
**Verified:** **4 130 EditMode: 4 128 passed / 1 failed / 1 inconclusive (+11)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. `dotnet format whitespace` clean on the touched files; `TimeManager.asset` reverted.
**Deviations:** two test edits outside the Files table: `JungleTests.SharedBlocks` drops `_isEndless` and `_finalStage` (the Jungle now differs from Descent on purpose), and `ClassUnlocksTests.Profile_CarriesNoNumberThatAffectsARun` names `PlaceUnlocks` as the second profile reader, still bool and int only. In the spec's As built.
**Learned:** a test run started right after an edit can execute the previous assembly; run `unity recompile` first.
**Follow-ups:** none. UI-11c writes the finish; UI-11d writes `FinishedModeIds`.

### 2026-10-15 · UI-11c · Finishing a place, in core
**Built:** `ModeFinished(modeId, finalStage)` in `RunEvents.cs`. On `IsModeComplete`, `RunSession.Tick` now publishes `ShardsAwarded` → `ModeFinished` → `RunEnded`; the death branch's payout is one private `PublishPayout()` that both branches call. No snapshot follows a final clear, and an endless mode or a death never publishes the event.
**Verified:** **4 135 EditMode: 4 133 passed / 1 failed / 1 inconclusive (+5)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. Console clean. `dotnet format whitespace` clean on the touched files.
**Deviations:** none to the Files table.
**Learned:** nothing new.
**Follow-ups:** none. UI-11d writes `FinishedModeIds` from `ModeFinished` and says it on Run end.

### 2026-10-15 · UI-11d · The profile hears a finish, and Run end says so
**Built:** `PlaceWriter` (scoped, constructor-subscribed): notes `ShardsAwarded` and `ModeFinished`, writes one `ProfileStore.Save` on `RunEnded` — best stage via `WithBestStage`, the place via `WithFinished`. `RunEndPresenter` hears `ModeFinished`: title "{0} cleared!" in `Palette.Essence`, and "New place: {0}" under it when a listed mode's `UnlockedBy` is this one. `ui.runend.finished` and `ui.runend.opened` in English and Pseudo.
**Verified:** **4 147 EditMode: 4 145 passed / 1 failed / 1 inconclusive (+12)**, in the open Editor through `TestRunnerApi`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. `dotnet format whitespace` clean on the touched files; `TimeManager.asset` reverted.
**Deviations:** edits outside the Files table: `RunInstaller` registers the writer and `RunTicker` takes it as an optional last parameter (so it is constructed); `RunEndPresenter.Construct` gains a `ContentCatalog` (null-tolerant); the "opened" line is a TMP text built in `Start` because the prefab is not in the table; `Pseudo.asset`; three `Construct` calls and one key count in `RunEndPresenterTests`. The writer saves on `RunEnded`, not on the award, to make rule 3's one save. Manual step 1 is not yet done.
**Learned:** nothing new.
**Follow-ups:** none. UI-11e reads the new fields for the home screen.

### 2026-10-15 · UI-11e · The home screen and its tab bar
**Built:** `MenuPresenter` becomes the home screen: `PickedMode` / `PickedCharacter` resolve the profile's picks (listed and open place, owned class on offer, else the first), a place card (name, "Best: stage N" when above 0) and a class card (name, `CharacterPreview` body), Play (once Descend) starting a run with both and saving them, Continue green on top with a save. `TabBar` (+ `Tab` enum): three equal 64 dp tabs on the safe area's bottom edge, Play lit. `ProfileStore.Pick`. The class page's Choose takes its place from `PickedMode`; `FirstModeId` is gone. `ui.menu.play`, `ui.tab.*`, `ui.home.best` in English and Pseudo; `ui.menu.descend` removed.
**Verified:** **4 161 EditMode: 4 159 passed / 1 failed / 1 inconclusive (+14)**, in the open Editor through `TestRunnerApi` after `unity command recompile`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. `dotnet format whitespace` clean on the touched files; `TimeManager.asset` reverted.
**Deviations:** edits outside the Files table: `Menu.unity` also renames the Descend object to Play and adds the cards and bar; `MenuPresenter.Construct` gains optional trailing catalog, profile, roster, preview and look-book parameters (VContainer fills them; old fixtures omit them); `Pseudo.asset` regenerated; call-site and key updates in `TableLocalizerTests` (`ui.menu.play`), `ClassSelectPresenterTests` (the Descend row became `Menu_ClassCardOpensTheScreen`, its fixture wires a class card) and `BootSmokeTests` (taps the class card). The Heroes tab and the class card open the class page; the Places tab and the place card do nothing, until UI-11f and g. Manual step 1 is not yet done.
**Learned:** nothing new.
**Follow-ups:** none. UI-11f makes Choose pick rather than start; UI-11g fills the Places tab.

### 2026-10-15 · UI-11f · The Heroes tab
**Built:** `HeroGrid`: a two-column scrolling grid of one card per `ClassSelectRoster` class in roster order, built in code on its first show. An owned card is the emblem and name; the picked class wears a `Palette.Player` rim and "Picked"; a locked card is the emblem at 35 % in `Palette.NodeLocked` with the lock badge, the Shard price and the deed line. `ClassSelectPresenter.Open(ContentId)` opens on a given class, and Choose now saves `ProfileStore.Pick`, closes the page and raises `Picked` instead of starting a run. `MenuPresenter` shows the grid for the Heroes tab and the class card, opens the page on a card tap, and returns home on `Picked`; the Play tab hides the grid. `ui.heroes.title` and `ui.heroes.picked` in English and Pseudo.
**Verified:** **4 167 EditMode: 4 165 passed / 1 failed / 1 inconclusive (+6)**, in the open Editor through `TestRunnerApi` after `unity command recompile`. The failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row is the animator clock row. `dotnet format whitespace` clean on the touched files; `TimeManager.asset` reverted. The PlayMode `BootSmokeTests` journey was rewritten and **not run**.
**Deviations:** edits outside the Files table: `Menu.unity` (a `Heroes` object under `Safe` with `HeroGrid`, wired to `MenuPresenter._heroes`); `MenuPresenter`; `Pseudo.asset` regenerated; `ClassSelectPresenter.Construct` keeps its `pending` and `loader` parameters, still null-checked, but no longer stores them (every caller and `MenuScope` stay as they were); `HomeScreenTests.ClassCard_Tap_OpensHeroes` now asserts the grid; `PaletteTests` lists `HeroGrid` among `Palette.Essence`'s readers (the price); `ClassSelectPresenterTests` drops `Menu_ClassCardOpensTheScreen`, `Choose_AFailedLoadGivesTheButtonBack` and the slow and refusing loaders, and `Choose_StartsTheRunOnce` becomes `Choose_PicksOnce_StartsNothing`; `BootSmokeTests` taps the class card, a hero card, Choose, then Play. Manual step 1 is not yet done.
**Learned:** nothing new.
**Follow-ups:** none. UI-11g fills the Places tab.

### 2026-10-15 · UI-11h-a · The home screen's art, and the tab bar
**Built:** `UI_v4/cut_sheets.py` (beside the repository, in `UI_v3`'s style) cuts the owner's six Gemini sheets into thirteen pieces under `Art/UI/Home/`: the tab plate, three off and three lit tiles, the place and hero card frames in normal and selected, and the Jungle and "soon" pictures. A pair shares one canvas. `TabBar` wears them: the plate is the bar's own sliced `Image`, each tab a 44 dp tile with its label under it, a tap reaching the whole column through `raycastPadding`, a press tinting rather than swapping. `HeightDp` is 72 and `TextDp` 12. `Menu.unity` wired.
**Verified:** **4 226 EditMode: 4 224 passed / 1 failed / 1 inconclusive (+51)**, in the open Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth) and the inconclusive row the animator clock row. `dotnet format whitespace` clean on the three files; `TimeManager.asset` reverted. Look at 411 × 914 and 360 × 780: no overlap, the caps close at 360 (spec As built).
**Deviations:** 3, in the spec's As built: the 9-slice borders set by eye; the row-8 colours go to the label, not the tile; the Look was rendered by a throwaway EditMode test, not Play.
**Learned:** an unfocused Editor accepted three `TestRunnerApi.Execute` calls and ran none until it was the active application, against [Traps §3](../Traps.md)'s first row; stays here, Traps.md is outside the Files table.
**Follow-ups:** none. UI-11h-b and -c use the frames and pictures.

### 2026-10-15 · UI-11h-b · The place cards
**Built:** a place card is its picture in the stone frame. `PlaceLookBook` (place id → sprite, `SkillIconBook`'s shape) is built by `BootInstaller` from `ModeDefinition._picture` and handed through `MenuPresenter.Construct` to `PlaceList.Configure`. Each card gains a sliced, outset `Frame`, `Rim` as the selected frame, a `Lock` and a `Scrim` strip behind the words; the picture is tinted `NodeLocked` when locked, the "soon" card wears `PlaceCard_Soon` at half alpha, and a place with no picture keeps the flat plate. `Jungle.asset` carries `PlaceCard_Jungle`; `Menu.unity` wired.
**Verified:** **4 236 EditMode: 4 234 passed / 1 failed / 1 inconclusive (+10)**, in the focused Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Console: only rows that provoke their own errors. `dotnet format whitespace` clean on the seven files; `TimeManager.asset` reverted. Look at 411 × 914 and 360 × 780 in the spec.
**Deviations:** 4, in the spec's As built: `Descent.asset` takes the new key, `Scrim` and `LockDp` are public, the injection note, and the text bands rise to clear the frame.
**Learned:** a `Unity_RunCommand` containing `File.Delete` was refused as "user interaction"; the same script without it ran.
**Follow-ups:** none. UI-11h-c is the hero cards.

### 2026-10-15 · UI-11h-c · The hero cards
**Built:** a hero card wears the portrait frame. `HeroGrid` gains `_frame` and `_frameSelected` (`HeroCard_Frame`, `…Selected`), `Card.Frame`, and `FrameBorderDp` 18 / `GlowPadDp` 6 in place of `RimDp`. The frame is sliced to a drawn border of 18 dp and outset 6; `Rim` is the selected frame over it, white, on the picked card only. The plate keeps its flat colour, inset to the frame's opening, with the emblem, padlock and words 4 dp inside it. `Menu.unity` wired.
**Verified:** **4 240 EditMode: 4 238 passed / 1 failed / 1 inconclusive (+4)**, in the focused Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Console: only rows that provoke their own errors. `dotnet format whitespace` clean on the two files; `TimeManager.asset` reverted. Look at 411 × 914 and 360 × 780: the selected frame clear of the edge and title; no emblem drew in the harness (spec As built).
**Deviations:** 2, in the spec's As built: the plate inset and draw order, and a tolerance on an offset assert.
**Learned:** nothing new.
**Follow-ups:** none. UI-11h is closed.

### 2026-10-16 · UI-12a · The avatar, baked
**Built:** `Soulvail/Heroes/Bake Portraits` (`Editor/Heroes/PortraitBaker.cs`, with `PortraitFraming`) stands each class's body on `CharacterStage` in an Editor preview scene, idle's first frame, frames head and shoulders by arithmetic, renders it transparent through a URP `SingleCameraRequest` and writes `Art/UI/Heroes/Portrait_<Class>.png` as a UI sprite. `CharacterDefinition._portrait` holds it, set by the bake unless a painted one is there; `CharacterLook.Portrait` carries it, and nothing reads it yet. Four PNGs baked and committed: the Ranger, and the Knight three times.
**Verified:** **4 257 EditMode: 4 255 passed / 1 failed / 1 inconclusive (+17)**, in the focused Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth), the inconclusive an animator clock row. Console: 0 errors after a recompile. `dotnet format whitespace` clean on the six files; `TimeManager.asset` reverted. Manual steps 1–3 done: a second bake is byte-identical, and the open scene stays clean.
**Deviations:** 6, in the spec's As built. One changes a number: **`ShoulderFraction` is 0.6, not 0.42**, because a KayKit head is 44 % of its body and 0.42 framed the face alone.
**Learned:** `Is.SameAs` on an asset reference fails after a reimport, with the same name on both sides — Traps §7.
**Follow-ups:** none. The Knight's helmet peak touches the portrait's top edge; UI-12b shows whether it reads.

### 2026-10-16 · UI-12b · The card wears it
**Built:** a hero card draws its class's `CharacterLook.Portrait` in the `Emblem` image: white, `preserveAspect`, the opening's full width, bottom-centre pivot, standing on the name band and stopping `PortraitTopDp` (4) under the opening's top. A class with no portrait, or no look book, draws the emblem as UI-11h-c laid it out. A locked card greys the portrait like its emblem, with the padlock over it. `Card.ShowsPortrait` is new; `Menu.unity` is unchanged.
**Verified:** **4 260 EditMode: 4 258 passed / 1 failed / 1 inconclusive (+3)**, in the open Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Console: only rows that provoke their own errors. `dotnet format whitespace` clean on the two files; `TimeManager.asset` untouched. Looked at 411 × 914 and 360 × 780 through a throwaway render of the Heroes tab: the Ranger in its cyan frame, a locked class greyed under the padlock, the Knight's helmet clear of the top stone, the name reading cleanly.
**Deviations:** none; the spec's As built has three notes.
**Learned:** nothing new.
**Follow-ups:** none. The home screen's class card keeps its emblem, as specified.

### 2026-10-16 · UI-12c · The hero sheet's model
**Built:** `HeroSheet.Build(spec, catalog, look, icons)` (`Game/Presentation/HeroSheet.cs`): description key, five base stats (Health, Damage, Attacks/s, Range, Speed) as label, format and value, the attack and Space move entries, and every rank of every branch with the branch's first icon as fallback. `CharacterLook` and `CharacterDefinition` gain the attack's two keys; the Ranger names its Longbow; 16 English rows, pseudo-locale regenerated.
**Verified:** **4 275 EditMode: 4 273 passed / 1 failed / 1 inconclusive (+15)**, in the open Editor through `TestRunnerApi`; the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth). Console: only rows that provoke their own errors. `dotnet format whitespace` clean on the six `.cs` files; `TimeManager.asset` and `ProjectSettings.asset` reverted.
**Deviations:** 2, in the spec's As built: `ContentValidationTests` sweeps the new keys, and the two-branch test tree has three branches.
**Learned:** a key an asset names is invisible to the sweep until `ContentValidationTests.AuthoredKeys` yields it; spent.
**Follow-ups:** none. UI-12d draws the sheet and removes `SkillStrip`.
