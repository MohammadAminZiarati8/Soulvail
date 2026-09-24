# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

**Closed milestones are archived, not deleted:** [M0](archive/PROGRESS-M0.md) · [M1](archive/PROGRESS-M1.md) · [M2](archive/PROGRESS-M2.md) · [M3](archive/PROGRESS-M3.md) · [M4](archive/PROGRESS-M4.md) · [M5](archive/PROGRESS-M5.md). The log below covers the milestone in progress only. This file used to be 381 KB and could not be opened whole by any tool; keeping it to one milestone is what stops that happening again — **M2 was the first milestone archived on time, M3 the second and M4 the third, which is what turns a rule into a habit.**

**Current State is a brief, not a second log, and it is capped.** After M3-15 this file was 228 KB with an *empty* Log; by M4-05b it was 298 KB, and 256 KB of that was Current State — thirty-four *Verified* rows back to M3-00b, an eight-deep task chain, and four rows that were appended to for a whole milestone and never rewritten, one of them still opening with a sentence about M3-06. On 2026-09-20 the block was cut to under 10 KB: M3's *Verified* rows went to the [M3 archive](archive/PROGRESS-M3.md#verification-chain), M4's were folded into their Log entries as a **Verified** line, and every other row was rewritten as the present with links to the history. The rules that keep it there are under [How to write an entry](#how-to-write-an-entry) — two on 2026-09-20, and a third at M4-07, which bounds the three rows that only ever grew.

---

## Current State

_Updated: 2026-09-24, as of M6-10's merge — localisation's mechanism and sweeps._

| | |
|---|---|
| **Milestone** | **M6 — Systems complete. Fully specced, and building: all twenty-two rows have specs, the three spec groups are closed, and eighteen implementation rows are merged.** M0–M3 are tagged `m0`–`m3`; **`m5` is tagged and pushed**, and **`m4` is still the owner's to tag**. **M5 is closed and [archived](archive/PROGRESS-M5.md)** — 15 tasks, a second class, and the first acceptance this project measured rather than argued. |
| **Last merged task** | **[M6-10](tasks/M6-10-the-rest-of-localisation.md) — the rest of localisation: a second table, two sweeps, and no second language.** |
| **Next task** | **[M6-11](tasks/M6-11-acceptance-and-tag.md) — M6 acceptance and tag `m6`.** The last row; the [ROADMAP](ROADMAP.md#m6--systems-complete)'s `Depends on` column says *everything*. |
| **Verified** | **3 140 EditMode / 0 / 0, twice consecutively, and PlayMode 26 / 0 / 0 twice** at M6-10. The chain of counts is each Log entry's **Verified** line ([M5's](archive/PROGRESS-M5.md)). **And, since M5-08, a second instrument:** two complete played runs — six boss kills at 74–108 s, 70 player hits against GD §12.4's one-shot rule, hits-to-kill at every stage to 19. |
| **What works** | **A run is a descent that reaches a boss, kills it, pays for dying, and survives the app closing** — and three classes now walk it. Boot → Menu → `Descend` → a card per class → arenas sealed by threat-budgeted, telegraphed waves, a door when they are dead, one stage deeper each time, a boss every fifth. **The Oathbound** swings a cone with Aegis and Charge; **the Gravecaller** leads a Bone Bolt, blinks 6 m leaving a corpse the arena walks at, and raises Wights from a quarter of the dead; **the Emberwright** throws a slow 3 m orb that hits harder the longer nothing touches it, and blinks leaving fire. **Each meets the Veil its own way** (CH §3): the Oathbound resists it, the Gravecaller feeds on it, the Emberwright buys casts with it. **Each has a twelve-node tree**; the Emberwright's burns ground and moves Kindling and its pool. **Levelling:** a kill is experience, a threshold is a pick of three of the class's twelve nodes, an exhausted tree pays the mode's Overflow; **at half its tree a run stops and borrows one branch of the other class for good** (CH §5.4), which arrives in the offer pool from the next level on. **The Warden of Ash** — 3 200 HP, a shockwave, arming cracks, adds across an invulnerable beat at 66 % and 33 % — dies in 74–108 s, inside GD §9.1's band. **Death pays** GD §14.1's three terms as one `ShardsAwarded`, banked in `PlayerProfile` v4 with the archetypes met and any class proved — **and class select spends them**: a locked class is drawn priced, dead until affordable, and a tap buys it. **And every stage clear now pays Essence** — GD §15's `20 + 4·n`, `+60` on a boss, authored on the mode and banked in a per-run wallet the Sanctum spends. **After every stage the run pauses in a Sanctum screen that sells four things** — a banked reroll, a two-tap banish, 30 HP, −15 Veilrot — each priced, and dead with a reason when it cannot be had; Descend leaves. A boss stage is not over while an add stands. **GD §10's Veilrot fills from Pacts** — four thresholds, the Claiming, a `TriggerField`. **A level-up can offer one card as a Pact**, drawn violet with its own description and a Rot price. Four nodes carry one. Taking it applies the corrupted effects and pays the meter, and a resume keeps it. **From stage 25 an Ordeal is dealt every ten stages**, saved, and each moves one number. **The HUD draws both economies**: a violet meter down the right edge with its four marks and the Claiming, and an Essence counter top-right. A banished node has its own frame on the tree. **Saves:** `run.json` **v4** at every boundary, deleted on death — it carries the run's Essence, Veilrot, reroll counters and banished and pacted nodes and reserves the Ordeals, and a v1 file still loads through three steps; `profile.json` **v4** — a v3 file keeps the Gravecaller. Every colour is `Palette`, every string a `LocKey`, read in English or, when the profile says `qps-ploc`, a generated pseudo-locale. Milestone by milestone: the [archives](archive/). |
| **What does not work yet** | **The Gravecaller breaks GD §12.4's TTK invariant from stage 9**, reaching 8 hits on a Husk by stage 16 against a 3–5 band, because its whole tree carries one weapon-damage node; the tuning half is **M8-05**'s and M6-11 takes the third class's curve for it. **The feel verdict is deferred by the owner's ruling** — the three mechanics exist and were played, but *"second class or reskin"* cannot be judged on capsules; re-taken at **M8-01** with M7's content and art in. **Pacts are rare**: 4 of 36 nodes carry one, so an offer shows a Pact far less often than GD §13.2's quarter until M7-04 authors more. Only M6-11 is unbuilt; **no second language ships** ([parking lot](ROADMAP.md#parking-lot)). **No played run has reached an Ordeal** (deepest: 19); M6-11. **The Gravecaller's deed line waits** for M7-03's Choirmother. **Only auto-cast buys a cast with Rot**; a thumb button is M6-11's call. **The arena only reacts** — hazard damage has no core system. **A boss cannot run a `SkillRunner`** — eight of ten `TriggerField` members mean nothing to an enemy; M7's Archon brings the answer. **Not saved, by ruling:** cooldowns, shields, live zones. **Three of CH §3.2's Actives are unauthored** — Tether, Rot Nova and the three Keystones are M7-04's. **Unbuilt by design:** no audio, no Sanctum room — the phase plays in the cleared arena — no Elite, one boss, two arenas. |
| **Known issues** | *(1)* **`FrameOrderTests.Ticker_RunsTheStepsInOrder` fails about one run in ten, diagnosed and unfixed.** The instrument answered ***wrong wedge*** twice, so the seam's sync is **not** late and the fault is the fixture's own zero-margin apex; fixing it changes what the project's one frame-order assertion means. [Ledger row 4](ROADMAP.md#carry-forward-into-m6) carries the account. A second row in the same file, `Ticker_SnapshotPrecedesTheTick`, failed once at M6-03b on a first-frame distance ([*As built*](tasks/M6-03b-the-meter-on-the-right-edge.md)). *(2)* **Every "ring" is a square**: `VFX_TelegraphRing.prefab` is an untextured Quad; one circle texture fixes all five decals; art, M7. *(3)* **`ArenaView.OnValidate` warns "0 spawn point(s)" for both arena prefabs on every domain reload**; they author 8 and 7, and reload-time `Transform` references are fake-null ([Traps §1](../Traps.md)). Found at M4-01a, nobody's. *(4)* **`PlayerStat` lies by name** — it carries `ContactDamage`, which `PlayerStats.Resolve` refuses; renaming it `StatId` moves nine assets' serialized ordinals; the owner's ruling, owed since M4-01a. *(5)* **M3-14c's nine labels were never witnessed in Play.** *(6)* **Environmental:** a PlayMode row can fail on *"`Error reason is 'NoSubscription'`"* (Unity's AI Assistant, signed out); `ProjectSettings/TimeManager.asset` re-serialises whenever the Editor touches the project, so check and revert before every handover ([Traps §5](../Traps.md)); the disk sits near full and free RAM near zero, which crashes asset-import workers on a refresh (M6-06a). *(7)* **`PlayerAnimatorViewTests.Animator_AttackSpeedIsUnreachableFromAnEditorClock` flakes after a Play session** — it asserts `Time.time` is 0 outside Play, and an Editor that has ticked reads 0.73 and does not reset on reimport. Green on a re-run. Found at M5-08. |
| **Reference device** | **None, and the emulator is not a fallback.** BlueStacks 5 **installs the APK and crashes on launch** in its own Vulkan driver, not game code. **Nothing outside the Editor has ever run.** [M0-20a](tasks/M0-20a-apk-runs-on-bluestacks.md) owns it; a phone removes the question. **And the Editor is not a stand-in for layout either:** `Screen.dpi` reads **120** here against a phone's 400, so every dp-sized element ever looked at was drawn at **30 %** of its device size ([Traps §9](../Traps.md)). |
| **Deferred — device-only** | **All of it is [ledger row 3](ROADMAP.md#carry-forward-into-m6)**, five tags deep with nothing ever run outside the Editor. Two are named apart. **Multi-touch risks a *feature*, not a verdict**: if Android does not deliver a thumb on the stick and a thumb on S3 at once, manual casting does not work at all — and M5-08's log makes that sharper, because **80 of 84 casts in two played runs were automatic**. **Kill-from-recents is the one correctness row**; M5-08 witnessed `Continue` restoring a run mid-stage in the Editor, which is not the same act. The full list is on the ledger row. |
| **Where the rules live** | **[Traps.md](../Traps.md)** for things in the toolchain that lie to you — read it before debugging a probe. **[Architecture.md §18](../Architecture.md#18-invariants)** for rules the code depends on — read it before changing an ordering. **[ADRs](../adr/)** for why. An obligation with an owning task is a **[ledger](ROADMAP.md#carry-forward-into-m6)** row and leaves only when its owner's *As built* says so; one with no owner is a **[parking-lot](ROADMAP.md#parking-lot)** line. **M6's ledger has four of its eight rows open, and every one says either which M6 task takes it or that none does and why** (M4-07 rule 11). One has an M6 verdict (4 → M6-11), and three say *no M6 owner* in writing. Three now also name **the instrument** rather than the task that will think about them: **a row that wants a number is discharged by an instrument, not by an argument.** **A ruling is not a discharge.** **And a row is re-read before it is inherited, not after** — M6-00c corrected three (2's arithmetic, 4's named fixture, 7's assumption that there were tables to write) without moving an owner. |
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

**At the end of a milestone**, move its entries to `archive/PROGRESS-M<n>.md`, promote anything durable out of them first, and leave this Log holding only the milestone in progress. **M2 is the first milestone this was done for on time** — M0's and M1's were archived a milestone late and at 381 KB between them, which is the failure the rule was written against. M3 was the second and M4 the third.

---

## Log

_M0, M1, M2, M3, M4 and M5 are archived: [M0](archive/PROGRESS-M0.md) (20 entries) · [M1](archive/PROGRESS-M1.md) (21) · [M2](archive/PROGRESS-M2.md) (27) · [M3](archive/PROGRESS-M3.md) (35, plus M2-15a, which is M2's task merged after that archive closed) · [M4](archive/PROGRESS-M4.md) (11) · [M5](archive/PROGRESS-M5.md) (15)._

**The log below opens M6, and its first entry is M5's.** [M5-08a](tasks/M5-08a-splash-offers-what-install-refuses.md) was created by M5's acceptance and merged after M5's archive closed, so its entry lands here rather than in [the M5 archive](archive/PROGRESS-M5.md) — M2-15a's precedent exactly, which landed in M3's log for the same reason.

### 2026-09-22 · M5-08a · The splash offers what the install refuses

**Built:** `SplashFlow.TryRefusal` — the sweep `RequireInstallable` already performed, as a predicate both it and `BranchesOf` read. `SplashOption` gained `Borrowable` and `RefusedKey`, so an offer now carries the answer `Choose` would give; `SplashPresenter` derives `interactable` from it and draws the reason where the node count sits. Two English rows. `Choose` is untouched and still throws: the predicate is the gate, the exception stays the invariant.

**Verified:** **2 527 EditMode / 0 / 0** (+6 on M5-08's 2 521) and **PlayMode 21 / 0 / 0**, the PlayMode suite run twice — the first pass was 20 / 1 on known issue 1 with its exact *wrong wedge* signature, the body 0.0001 m behind the apex; the second was clean. Console otherwise silent, zero new analyzer warnings, `dotnet format whitespace` green over all four touched C# files. `TimeManager.asset` re-serialised and was reverted ([Traps §5](../Traps.md)).

**Deviations:** 4, in the [spec's *As built*](tasks/M5-08a-splash-offers-what-install-refuses.md). **One changes a decision:** a node moved between branches of the presenter fixture's lender tree, because rule 5 makes a refused branch draw its reason *instead of* its node count — which silently broke two existing rows that are not about this task, leaving them passing while testing less. Two borrowable branches and one refused keeps both claims. The Files table also named the wrong core test file: `BranchesOf` lives in `SplashFlowTests`, not `SplashBranchTests`.

**Learned:** **a guard that is correct and a screen that ignores it compose into a defect neither one contains** — `RequireInstallable` was right, atomic and well-tested, and the only fault was that nothing asked it before the player committed → the rule is *a screen may not offer what the model refuses*, filed as [AR §18.2](../Architecture.md#182-the-boundary)'s neighbourhood and as this entry. · **A behaviour change can weaken a test without reddening it**, which is the failure mode a diff review cannot see: both affected rows would have kept passing against less. · **M5-07a-ii rule 6's stated *cost* was the only escape from this bug** — the splash choice is derived rather than saved, so quitting and pressing `Continue` restores the offer; nobody designed that and nothing told the player — spent (stays here).

**Follow-ups:** **[M6 row 6](ROADMAP.md#carry-forward-into-m6) DISCHARGED.** No row added. The Restless Dead placement stays a [parking-lot](ROADMAP.md#parking-lot) line owned by M7-04.

### 2026-09-22 · M6-00a · Specs for the economy, in core

**Built:** five specs where [the table](ROADMAP.md#m6--systems-complete) had three titles — [M6-01a](tasks/M6-01a-essence-wallet-and-drops.md) the wallet · [M6-01b](tasks/M6-01b-save-format-v4.md) format v4 · [M6-04](tasks/M6-04-veilrot-thresholds-and-the-claiming.md) the meter and the Claiming · [M6-02a](tasks/M6-02a-the-sixth-phase.md) the phase · [M6-02b](tasks/M6-02b-four-things-essence-buys.md) the four services. **M6 opens with three spec groups, not two**, seamed the way M5-00a seamed its own: these five are `Soulvail.Core` end to end, no prefab and no presenter. M6 moves 11 → **16** rows, counted rather than incremented. M6-00b specs M6-03, M6-05 and M6-06.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays M5-08a's **2 527 EditMode / 0 / 0** and **21 PlayMode / 0 / 0**. Every type, member and asset field each rule names was grepped first, and five rulings changed because of it. Branched from `dev` at the `m5` release, clean and level with `origin`.

**Deviations:** *2*. **Build order is no longer ID order** — GD §13.3's fourth service is *Cleanse*, so M6-04 precedes M6-02, and a shop with one service that refuses is M5-08a's defect. And **M6-02 split before starting** at five counted files: the phase against what it sells.

**Learned:** **`Health.OnMaxHpChanged` names its own first caller in writing** — *"nothing in V1 removes max HP … the first thing that does needs the owner to check `IsDead`"* — and the Claiming is it; `PlayerDied` comes from exactly one line, so a run drained to zero would end with nobody told → M6-04 rule 8. · **`ShardPayout` is a pure function of `StageIndex`**, so [row 5](ROADMAP.md#carry-forward-into-m6)'s *"moves the depth the payout reads"* is wrong: what moves is the door → M6-02a. · **GD §10.2's 50 threshold summons a Revenant and GD §19 puts it in V2** → [parking lot](ROADMAP.md#parking-lot), M7-01. · **`new RunSnapshot(...)` has 36 sites across 30 files** — what makes one v4 an arithmetic argument rather than a taste one → M6-01b. · **`Palette_HasTheColoursNobodyReadsYet` has been false since M4-06 and is green**: its `Readers` list is hand-kept and omits `RunEndPresenter` → parking lot, M6-03.

**Follow-ups:** **all six open ledger rows re-read against an unmerged owner; one placed, three ruled ownerless in writing, none carried silently.** [Row 5](ROADMAP.md#carry-forward-into-m6) has an owner for the first time — **M6-02a**, because an untimed room in the gap the defect lives in is not a safe room. Rows 1, 2 and 3 say *no M6 owner* with the reason; **4 → M6-11** as a verdict; **7 → M6-10**, with M6's six screens of strings listed now rather than reconstructed at the acceptance. Rows 1, 2 and 7 gained an **instrument**. **Parking lot:** two lines opened and one re-aimed — `ShardPayout`'s consts stay `const`s, its promoter fired early and was answered **no** at 63 call sites.

### 2026-09-22 · M6-00b · Specs for the screen, the temptation, and the deep-run modifiers

**Built:** six specs where [the table](ROADMAP.md#m6--systems-complete) had three — [M6-03a](tasks/M6-03a-the-sanctum-screen.md) the shop and the pause · [M6-03b](tasks/M6-03b-the-meter-on-the-right-edge.md) GD §16.1's two readouts and the fourth node state · [M6-05a](tasks/M6-05a-what-a-pact-is.md) / [b](tasks/M6-05b-the-offer-that-rolls-one.md) the Pact and the roll · [M6-06a](tasks/M6-06a-what-an-ordeal-is.md) / [b](tasks/M6-06b-four-ordeals-and-two-refusals.md) the draw and four of six. **All three split and only M6-05's was predicted** — M6-03's seam is a screen the player acts on against a readout that only reports; M6-06's is M6-02a/b's. M6 moves 16 → **19** rows, counted.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays M5-08a's **2 527 EditMode / 0 / 0** and **PlayMode 21 / 0 / 0**. Every type, member and field each rule names was grepped first. Branched from `dev`, clean.

**Deviations:** *3*. **Two amend M6-00a specs that have not been built**, made here rather than left to the task that would find them: [M6-01b](tasks/M6-01b-save-format-v4.md) gains `pactedNodeIds` as a sixth v4 field, under the licence that spec wrote for this case; [M6-04](tasks/M6-04-veilrot-thresholds-and-the-claiming.md)'s `public static readonly float[] Thresholds` becomes `ThresholdCount` + `Threshold(int)`. The third is the count: six specs where the ROADMAP predicted four.

**Learned:** **`IEffect` is a marker interface with zero members**, so *"1.8× a clean node"* is six primitive files before one Pact is offered — and multiplying is backwards for three of them → M6-05a rules a Pact is **authored**, which is also what GD §13.2's own examples describe, since they carry downsides no multiplication produces → [parking lot](ROADMAP.md#parking-lot). · **`SkillTree.Restore` replays taken ids through `Record`, which applies `Spec.Effects`** — a Pact would not survive a `Continue` and the player would keep the Rot → M6-01b amended. · **`Soulvail.Core` has no `public static readonly` field at all**, so M6-04's drafted array would have been its first static mutable state with nothing sweeping for one → M6-04 amended, AR §7. · **`W(n)` clamps at 5 from stage 15**, and the arenas author 4 and 5 pillars against a floor of 3 → Echo and Fracture refused, both to the parking lot.

**Follow-ups:** **[Ledger row 8](ROADMAP.md#carry-forward-into-m6) opened** — `Palette_HasTheColoursNobodyReadsYet`, promoted off the parking lot the moment it had owners (M6-03a narrows it to a sweep, M6-03b retires it). **Parking lot: three opened, one struck, one half-answered** — the stage-editor line's Ordeal clause is answered no. **Stale pointers re-aimed in one pass** — fifteen live `M6-03`/`M6-05`/`M6-06` references across M6-00a's five specs now name the halves they mean, which is the split-ids line's standing obligation.

### 2026-09-22 · M6-00c · Specs for the Emberwright, the meta layer, and the close

**Built:** eight specs where [the table](ROADMAP.md#m6--systems-complete) had five — [M6-07a](tasks/M6-07a-the-emberwright-and-the-cinder-orb.md) the class and Kindling · [b](tasks/M6-07b-blink-and-the-ground-that-burns.md) Blink and the first ground that burns · [c](tasks/M6-07c-what-each-class-does-with-the-veil.md) all three classes' Veilrot rows · [M6-08](tasks/M6-08-emberwright-tree-v1.md) the tree, two Actives, five addresses · [M6-09a](tasks/M6-09a-profile-v4-and-what-a-shard-buys.md) / [b](tasks/M6-09b-a-class-you-cannot-pick-yet.md) profile v4 and the first Shard anyone spends · [M6-10](tasks/M6-10-the-rest-of-localisation.md) · [M6-11](tasks/M6-11-acceptance-and-tag.md). **M6-07 split into three where two were predicted; M6-09 split unpredicted at M6-03a's seam; M6-10 and M6-11 did not.** M6 moves 19 → **22**, counted.

**Verified:** docs only — no C#, no compile, no suite; the baseline stays M5-08a's **2 527 EditMode / 0 / 0** and **PlayMode 21 / 0 / 0**. Every type, member and call-site count a rule names was grepped first.

**Deviations:** *3*. **Two amend specs written earlier in this group**: `Kindling`'s two numbers and the Blink pool's three are `Stat`s from M6-07a/b, ADR-0008's rule, and without them M6-08 has nothing to address. The third amends merged [M6-03a](tasks/M6-03a-the-sanctum-screen.md), whose ripple row names a fixture that does not exist.

**Learned:** **three of the four things in M6-07's title need no new code** — a Cinder Orb is `WeaponKind.Projectile` at a bigger `shotRadius`, and `ClassCard.Clear` says the prefab already carries a third card. · **CH §3.3's 30 damage kills a stage-1 Husk in *two* hits** against GD §6.2's 3–5, so [row 2](ROADMAP.md#carry-forward-into-m6)'s DPS arithmetic understated its own invariant; the asset authors **17** → row 2 corrected. · **CH §3's Veilrot column is in none of M6's eleven merged specs** — five numbers, three classes, two specs naming `M6-07` for a third of it → M6-07c. · **`SplashFlow.TryRefusal` sweeps two things and an unresolvable `PlayerStat` is not one**, so an Oathbound borrowing *Ember* throws out of a `Button.onClick` → M6-08 rule 8. · **No document names a second language** → M6-10 ships the mechanism and a pseudo-locale. · **`RunTickerTests` does not exist; `FrameOrderTests` is where phase order lives** → row 4 corrected.

**Follow-ups:** **Three ledger rows corrected, none added, none discharged** — 2, 4, and 7 (31 → **65** rows). **Parking lot: three opened** — CH §3.1's 25 %-weaker clause, the second-language list (waiting on GD §21.1), GD §14.2's cosmetics — **and three re-aimed**: GD §13.2's contradiction goes to M6-11 rule 10 as the one thing the tag should not be made over; Fracture and Echo are confirmed as *not* blocking it, Echo with an instrument. **Four stale `M6-07`/`M6-09` pointers re-aimed.**

### 2026-09-22 · M6-01a · The Essence wallet, and the one event that fills it

**Built:** `EssenceWallet` — a balance, `Earn`, `Spend`, and `CanAfford` standing in front of `Spend` — and `EssenceChanged`, which carries the balance and the delta because neither can be derived from the other. GD §15's income is an authored `EssenceSpec` on the mode, optional and last like `OverflowSpec`, 20 / 4 / 15 / 60 in `Descent.asset`; `StageFlow.EnterClear` pays it once a stage, above the `StageCleared` publish, asking the director whether the stage held a boss rather than computing one. `RunState.Essence` is the read and the wallet is not handed out. **Nothing spends it, nothing saves it, and the debug overlay is all that draws it.**

**Verified:** **2 553 EditMode / 0 / 0** (+26 on M5-08a's 2 527) and **PlayMode 21 / 0 / 0**, both clean on the first pass — known issue 1 did not fire — and both re-run after the overlay edit. Console silent but for the test runner's own two lines, zero new analyzer warnings, `dotnet format whitespace` green over all thirteen touched C# files. `TimeManager.asset` re-serialised and was reverted ([Traps §5](../Traps.md)).

**Deviations:** 6, in the [spec's *As built*](tasks/M6-01a-essence-wallet-and-drops.md). **One changes a decision:** `ForStageClear` is `base + depth·stage`, not `base + depth·(stage − 1)` — GD §15's `20 + 4·n` reads either way, the difference is 4 Essence at every depth, and only the Tests table's own numbers settle it. The sixth is a file the table does not list: `DebugOverlay` gained an `ess` field.

**Learned:** **A spec can forbid the file its own manual verification needs** — the Files table left the overlay out while *Out of scope* named it as the reader until M6-03a, so step 1 was unperformable as written; the fix was a one-line additive edit, declared → spent (stays here). · **`CanAfford` beside `Spend` is M5-08a's lesson as a shape rather than as a rule**: the predicate ships with the invariant from the economy's first line, so there is no window in which a screen could offer what the model refuses → [AR §18.2](../Architecture.md#182-the-boundary)'s neighbourhood, and this entry. · **A boss stage's award needed no boss fight** — the fixture stands a `BossSpec` up and cuts it down with `ApplyDamage`, because `StageFlowTests`' rule of never ticking enemy behaviours makes a `WardenBehaviour` unnecessary → spent.

**Follow-ups:** none, and no ledger row moved: this task makes no draw and adds no stream, so [row 1](ROADMAP.md#carry-forward-into-m6) gains nothing. `EssenceWallet.Restore` is written, `internal`, and called by no production code until [M6-01b](tasks/M6-01b-save-format-v4.md).

### 2026-09-22 · M6-01b · Save format v4

**Built:** `RunSnapshot` is **v4** with six new fields and four new arguments: `RunEconomy` — Essence, Veilrot and the two reroll counters behind one struct, with the format's only *relational* guard — plus `BanishedNodeIds`, `PactedNodeIds` and `OrdealIds`, which refuse `default(ContentId)` where a slot requires one. `MigrateRun` gained an `if (version < 4)` step; `RunMirror` seven flat keys; `RunRecorder.Take` writes the wallet and five placeholders; `RunState` grew four reads answering 0 and empty; `RunSession.Start` calls `EssenceWallet.Restore` below the tree and above `Health.Restore`. **Only Essence has a writer**; four later tasks each fill one argument without bumping the version.

**Verified:** **2 575 EditMode / 0 / 0 twice consecutively** (+22 on M6-01a's 2 553: the Tests table's 26 less four renamed or extended in place) and **PlayMode 21 / 0 / 0**, clean on the first pass — known issue 1 did not fire. One red row on the first EditMode pass (deviation 3), fixed. 42 Console entries, all deliberately provoked by tests and none new. Zero new analyzer warnings, `dotnet format whitespace` green over all 36 touched C# files. `TimeManager.asset` re-serialised and was reverted ([Traps §5](../Traps.md)).

**Deviations:** 6, in the [spec's *As built*](tasks/M6-01b-save-format-v4.md). **One changes a decision:** `SplashFlowTests.Resume_TheFormatIsStillVersionThree` is renamed `Resume_TheFormatCarriesNoBorrowedBranch` and asserts `Is.GreaterThan(3)` — its subject was never the number, and reading it as the number would have made this bump look like a regression.

**Learned:** **a row that pins a version number is usually pinning something else** — three rows asserted `CurrentVersion == 3` as shorthand for *"my feature is derived, not stored"*, a claim the bump leaves true and the spelling false; the shape that survives a bump is the property sweep beside it → [AR §18.1](../Architecture.md#181-ordering)'s restore-order row and this entry. · **The spec's stated risk was collected before any code existed**: `pactedNodeIds` was added at M6-00b by grepping `SkillTree.Restore`, which is the *"re-cut v4 before `m6` is tagged"* escape used at the cost of one argument rather than 36 — spent. · **A shared copy helper had to stop one list short**: all four id lists agree about refusing a defaulted entry, and `ManualSkillIds` cannot join them because there an empty slot **is** a defaulted id → written at the helper.

**Follow-ups:** none, and no ledger row moved. **[AR §18.1](../Architecture.md#181-ordering)'s restore-order row now states where M6's other three restores go** — Veilrot, the banishes and the Pacts below `SkillTree.Restore` and above `Health.Restore`, Ordeals order-free beside them — so their four tasks inherit a placement rather than choose one.

### 2026-09-22 · M6-04 · Veilrot: the meter, four thresholds and the Claiming

**Built:** `Veilrot` — a 0–100 meter only `Gain` raises and only `Cleanse` lowers. 25 speeds up what spawns next, 50 is published and does nothing else (GD §19's Revenant), 75 is a −20 % `PercentMult` on max HP that a cleanse gives back, and 100 latches the Claiming: ×2 damage, ×1.3 speed, dash cooldown halved, and −1 % of the starting maximum a second until death. `PlayerCombat.AnnounceDeath` is now the one publisher of `PlayerDied`, reachable without a `DamageResult`. `TriggerField.Veilrot` has its writer; `RunRecorder` saves the real meter; a resume restores it silently.

**Verified:** **2 612 EditMode / 0 / 0 twice consecutively** (+37 on M6-01b's 2 575). **PlayMode 20 / 1, then 21 / 0 / 0**: known issue 1 fired on the first pass, its first since M5-08a, with its documented wrong-wedge message. Console: nothing new; zero errors, zero new analyzer warnings; `dotnet format whitespace` green over 16 touched C# files. `TimeManager.asset` reverted.

**Deviations:** 8, in the [spec's *As built*](tasks/M6-04-veilrot-thresholds-and-the-claiming.md). None changes a decision. The one to know: `Tick` takes `(dt, now)` — the drain announces a death and `PlayerDied` carries a time.

**Learned:** **a whole second counted from frames is not a whole second** — sixty `1f/60f` sum to 0.9999997, and `0.01f × 100` is not 1, so both the step and the endpoint needed spelling out → the meter's remarks, spent. · **The restore order is uniformity for a writer that only shrinks the maximum** — `Health.OnMaxHpChanged` lands it the same either side; it is the raising writers the order protects → [AR §18.1](../Architecture.md#181-ordering), which also gains the meter's tick row. · **The spec's `Claiming_SurvivesCleansing` row was arithmetically impossible** (25 is on at 40) — spent.

**Follow-ups:** none. The Revenant stays a [parking-lot](ROADMAP.md#parking-lot) line promoted by M7-01; nothing gains Veilrot until M6-05b, and nothing draws it until M6-03b.

### 2026-09-23 · M6-02a · The sixth phase, and the boss stage that was never over

**Built:** `StagePhase.Sanctum` between `Clear` and `Gate`: entered `ClearTime` after the stage clears, announced by `SanctumOpened(stage, essence)`, untimed, and left only by `IProgressionCommands.LeaveSanctum`, with `IsSanctumOpen` beside it. `SpawnDirector.IsStageComplete` on a boss stage now wants the boss down **and** nothing registered breathing. The debug overlay reads `sanctum` and sends the leave when the player walks into the door, until M6-03a's button.

**Verified:** **2 630 EditMode / 0 / 0 twice consecutively** (+18 on M6-04's 2 612, all in `StageFlowTests`), after one pass at 2 629 / 1 on `LocalJsonSaveStoreTests.Store_SaveReplaces` — a Windows file-replace `IOException`, untouched by this task, the disk at 96 %. **PlayMode 21 / 0 / 0** first pass; after the door fix, **20 / 1 twice**, `BootSmokeTests` resuming the owner's saved run into a `Cast` trigger `AC_Player` lacks — environmental, see *As built*. Console: zero errors, zero new analyzer warnings; `dotnet format whitespace` green over 13 touched C# files. `TimeManager.asset` reverted.

**Deviations:** 8, in the [spec's *As built*](tasks/M6-02a-the-sixth-phase.md). None changes a decision. The one to know: `DebugOverlay` is edited outside the table, because nothing else could leave the Sanctum — first on Enter, which the owner's playtest refused, then on walking into the door.

**Learned:** **a phase nothing can leave is a soft-lock the suite cannot see**, and a hidden key is the same soft-lock with a secret — the owner walked to the door, as anyone would → spec *As built*, spent. · **The boundary save precedes the shop** → [AR §18.1](../Architecture.md#181-ordering)'s snapshot row. · **The ripple's named fixture was the wrong file** (`ResumeFlowTests` for `RunSessionResumeTests`), and `RunRecorderTests` was not named at all — spent.

**Follow-ups:** [ledger row 5](ROADMAP.md#carry-forward-into-m6) discharged. None added.

### 2026-09-23 · M6-02b · Four things Essence buys, and the one that sculpts

**Built:** GD §13.3's shop. `SanctumSpec` sits last on the mode (Descent: 25 / 40 / 40 / 30 / 60 / 15). `SanctumShop` prices, refuses and delivers a banked reroll that doubles, a banish, 30 HP and −15 Veilrot; `CanBuy` refuses the unaffordable and the worthless alike. Banish is a third flag on `SkillTree`, so `OfferGenerator` is untouched. The next draw that finds something spends the reroll, and the save carries both counters and the banishes. Five members join `IProgressionCommands`, refused outside the Sanctum. **By the owner's ruling:** `AC_Player` gains a `Cast` trigger, and a row sweeps the view's parameters against the shipped controller.

**Verified:** **2 675 EditMode / 0 / 0 twice consecutively** (+45 on 2 630), after one pass at 2 674 / 1 on the spec's own row (39 affords the 25 reroll; see *As built*). **PlayMode 21 / 0 / 0 with a v4 Gravecaller `run.json` on disk**: `BootSmokeTests` resumed it, and the Console shows zero warnings and zero errors. File deleted after. Console after EditMode: only the suite's asserted logs, plus one Package Manager fetch error. `dotnet format whitespace` green over 23 touched C# files. `TimeManager.asset` reverted.

**Deviations:** 9, in the [spec's *As built*](tasks/M6-02b-four-things-essence-buys.md). The one to know: **a purchase is refused outside the Sanctum**, now a clause of [AR §18.1](../Architecture.md#181-ordering)'s snapshot row. Also: `DebugOverlay` sells on F5–F8 until M6-03a, and the animator parameter is added with no clip, because no cast state exists.

**Learned:** **a test double can hide exactly the gap it stands in for.** The animator fixture's hand-built controller named `Cast` for three milestones and the shipped one never did → spec *As built*, spent. · **Manual step 3 fought AR §18.1**: a quit from the shop rolls the purchase back by design, so the step is reworded rather than the rule → spec *As built*, spent.

**Follow-ups:** none added. Wiring a cast clip into `AC_Player` is the owner's art call (KayKit candidates are named in *As built*).

### 2026-09-23 · M6-03a · The Sanctum screen, and a price that cannot be paid

**Built:** GD §13.3's shop on screen. `Sanctum.prefab` is four priced rows (`ServiceRow`), a balance in `Palette.Essence`, a Descend button, and a second page for Banish (`BanishPicker`, twelve authored rows). A refused row keeps its name and its price and says why where its effect text was: short, full, clean or nothing. `RunTicker.SanctumPhase` holds `PauseReason.Sanctum` below the level-up phase, so the shop is untimed in simulated time too. Every tap goes straight down the port. `DebugOverlay`'s F5–F8 and door stand-ins are gone. `PaletteTests`' reader row is now a sweep of `Soulvail.Game`, about Veilrot alone.

**Verified:** **2 709 EditMode / 0 / 0 twice consecutively** (+34 on 2 675), after one pass at 2 704 / 4 that found deviation 1 and a three-member enum row. **PlayMode 26 / 0 / 0** (+5, the Sanctum pause rows; `Ticker_RunsTheStepsInOrder` green in that run). Console clean after both suites. `dotnet format whitespace` green over 12 touched C# files. `TimeManager.asset` reverted. `Run.unity`'s diff is additive only.

**Deviations:** 10, in the [spec's *As built*](tasks/M6-03a-the-sanctum-screen.md). The one to know: **the first draw waits a frame**, because `SanctumOpened` is published before `RunState.IsSanctumOpen` is true, so a handler that drew would open the shop with every row dead. Also: 17 strings, not 14.

**Learned:** **an event can arrive before the state it announces.** A subscriber that asks the port inside `SanctumOpened` is told the shop is shut → [parking lot](ROADMAP.md#parking-lot), pinned by a row. · **A spec's string count is a floor.** Every label a thumb can hit needs a key, including Back → ledger row 7, spent.

**Follow-ups:** one parking-lot line (the event/state ordering). Ledger row 8's M6-03a half is done, and M6-03b retires the row.

### 2026-09-23 · M6-03b · The meter on the right edge, the counter in the corner, and the node that is gone

**Built:** GD §16.1's two economy readouts. `VeilrotMeterView` hangs down the right edge under the pause icon: a violet fill, four marks read through `Veilrot.Threshold(int)`, and a Claiming cap that never comes off, so a cleanse from 100 draws 40 with the mark still up. An Essence counter in reward gold sits top-right, beside the icon. `HudPresenter` drives both off three events and seeds them from `RunState` on `RunStarted`, because a resume restores them silently. `NodeState.Banished` and `Palette.NodeBanished` (a darker `NodeLocked`) mean a banished node is no longer drawn as locked.

**Verified:** **2 736 EditMode / 0 / 0 twice consecutively** (+27 on 2 709), after one pass at 2 735 / 1 in which M4-07's legibility guard caught four new text elements. **PlayMode 26 / 0 / 0 twice**, after one pass at 25 / 1 on `Ticker_SnapshotPrecedesTheTick`: a first-frame walk of 0.24 m against a 0.5 m floor, in a fixture this task does not touch. Console clean. `dotnet format whitespace` green over 13 C# files. `TimeManager.asset` reverted.

**Deviations:** 8, in the [spec's *As built*](tasks/M6-03b-the-meter-on-the-right-edge.md). **One changes a decision:** the HUD reads words again. `ui.hud.claimed` needs an `ILocalizer`, so `HudPresenter.Construct` takes three parameters and M4-06's arity row was amended. Also: three keys, and five test files outside the table.

**Learned:** **a guard written for one task catches the next one.** `Hud_NothingElseMoved` was M4-07's list of measured text. It refused 24 pt captions that would have shipped under Android's floor → spent (stays here). · **A spec's manual step can need a tool the build no longer has.** Steps 1–2 need a Veilrot grant, and M6-03a removed the debug keys → the *As built*.

**Follow-ups:** **[Ledger row 8](ROADMAP.md#carry-forward-into-m6) DISCHARGED.** No row added. The `Ticker_SnapshotPrecedesTheTick` sighting is in the *As built*, and it is one sighting, not a rate.

### 2026-09-23 · M6-05a · What a Pact is, and why 1.8× is a budget rather than an operation

**Built:** GD §13.2's corrupted node, authored rather than derived. `PactSpec` holds its effects, a Rot price in [10, 20] and its own description. It is `SkillSpec`'s optional last argument and is refused on an Active. `SkillTree.Take(id, asPact)` applies the Pact's effects **instead of** the clean ones, with the same source. `IsPact`/`PactedIds` sit beside the banish flag, and a two-list `Restore` brings a Pact back as a Pact. `RunRecorder` writes `pactedNodeIds` (v4's placeholder replaced, no bump). Four Pacts ship: Keen Censer, Zealotry, Sharpened Bone and Rot Feast, each a power and a price. **Nothing offers one yet**: that is M6-05b.

**Verified:** **2 766 EditMode / 0 / 0 twice consecutively** (+30 on 2 736), after one pass at 2 765 / 1 in which `OathboundTreeTests`' ModifyStat pin caught the four new assets. **PlayMode 26 / 0 / 0 twice**, after one pass at 25 / 1 on known issue 1's `Ticker_RunsTheStepsInOrder`. Console clean. `dotnet format whitespace` green over 13 C# files. `TimeManager.asset` reverted.

**Deviations:** 9, in the [spec's *As built*](tasks/M6-05a-what-a-pact-is.md). None changes a decision. **The one to read:** *"every branch of both classes can produce one"* is impossible with four Pacts across six branches, so four shipped and Oath and Legion have none.

**Learned:** **a resume row is only a resume row if the recorder writes the file it reads.** `Resume_APactSurvivesAKill` goes recorder → fresh session instead of hand-building the snapshot → spent (stays here). · **Manual step 2 needs a Pact debug command the build does not have** → the *As built*.

**Follow-ups:** none. [Ledger row 7](ROADMAP.md#carry-forward-into-m6) gets its four Pact descriptions and is not moved.

### 2026-09-23 · M6-05b · The offer that rolls one, and the two draws it always spends

**Built:** GD §13.2's roll. An offer that writes anything spends `picks + 2` draws on `Offers`: the slot, then a 0.25 chance, both always. A card is a Pact only if the chance says yes and its node carries a block. `LevelUpFlow` holds the index, publishes it on `OfferPresented` and takes a required `Veilrot`. `Choose` takes the node corrupted and then pays the meter, so a Pact at 88 begins the Claiming on the level-up screen. `OfferCard` draws a Pact with a violet frame, the Pact's description and *"Pact · +15 Rot"*. **The HUD meter now fills.**

**Verified:** **2 793 EditMode / 0 / 0 twice consecutively** (+27 on 2 766), after one pass at 2 790 / 3: two rows had pinned the old draw count, and the palette pin was mine, 20 where reflection says 19. **PlayMode 26 / 0 / 0 twice.** Console clean. `dotnet format whitespace` green over 15 C# files. `TimeManager.asset` reverted.

**Deviations:** 9, in the [spec's *As built*](tasks/M6-05b-the-offer-that-rolls-one.md). None changes a decision. **The one to read:** the Files table counted 10 `Draw` sites and there were 24. Five existing rows had pinned rule 1's old cost. One was a splash sweep that assumed one value per draw and went red rather than quietly measuring less.

**Learned:** **a sweep over a scripted stream is a claim about the consumer's cost per call**, so a change to that cost reddens the sweep in a file the task never touched. That is the useful outcome: the silent alternative is a sweep that lands on every third value and still passes → spent (stays here). · **Coverage, not `PactChance`, is the rate a player sees**: 4 of 24 nodes → the *As built*, M7-04.

**Follow-ups:** none. [Ledger row 7](ROADMAP.md#carry-forward-into-m6) gets its two strings and is not moved. [Row 1](ROADMAP.md#carry-forward-into-m6)'s Pact-card device line is unchanged.

### 2026-09-23 · M6-06a · What an Ordeal is, and the loop that deals one

**Built:** GD §13.4's Ordeals, dealt and doing nothing. `OrdealSpec` is five neutral dials with no kind enum. `Descent.asset` schedules them from stage 25, every 10, and stocks Famine, Vigil, Swarm and Hunger. `Ordeals` draws one `NextInt` on `Affixes` over what is left at each scheduled boundary. It draws nothing when the pool is empty, and `StageFlow.Advance` calls it above the composition. The set is kept for the run, written to `RunSnapshot.OrdealIds` and restored silently. **No dial has a reader**, and a sweep says so.

**Verified:** **2 830 EditMode / 0 / 0 twice** (+37 on 2 793) and **PlayMode 26 / 0 / 0 twice**, first pass green both times. Console clean after the first refresh. `dotnet format whitespace` green over 19 C# files. `TimeManager.asset` reverted, and one existing `English.asset` row that Unity's save had re-quoted was put back.

**Deviations:** 8, in the [spec's *As built*](tasks/M6-06a-what-an-ordeal-is.md). None changes a decision. **The one to read:** `StageFlow` holds the `Affixes` stream from its constructor rather than taking it on `Tick`, because a `Tick` parameter would have moved 30 call sites for a draw made once per ten stages. Two files are outside the table: `English.asset`'s eight rows and the overlay's `ord` readout that the manual steps read.

**Learned:** **the Editor at 0.3 GB free RAM crashes its import workers, and says so only as an out-of-memory stack in the Console.** Compilation still finished, so check what loaded before believing a crash → Known issue 6. · **A seed-stability claim is cheapest to assert as a difference between two runs**: stocked and bare from 24 to 45, `Affixes` apart by exactly three and every other stream equal. That avoids counting every drawer in the build → spent (stays here).

**Follow-ups:** none. [Ledger row 7](ROADMAP.md#carry-forward-into-m6) gets its eight strings and is not moved. The [parking-lot](ROADMAP.md#parking-lot) stage-editor line already records its Ordeal half as answered.

### 2026-09-23 · M6-06b · Four Ordeals that work, and two this build cannot ship

**Built:** each of the four turns one dial in one system. Famine multiplies `StageFlow`'s award, rounds it and floors it at 1. Vigil passes 2 to both of `LevelUpFlow.Open`'s draws. Swarm adds 8 to `WaveComposer`'s cap, re-clamped to `DeviceCap`, and halves the Husk's cost with a floor of 1. Hunger multiplies `Veilrot.Gain` above the clamp and leaves `Cleanse` alone. The flow and the meter take `Ordeals` optional and last, and `Compose` takes it as its last argument. **Fracture and Echo stay refused**, and rows pin the arithmetic behind each refusal.

**Verified:** **2 858 EditMode / 0 / 0 twice** (+28 on 2 830) and **PlayMode 26 / 0 / 0 twice**. Known issue 1 did not fire. The first EditMode pass was 8 red, all fixture premises: one roster debuted two archetypes on one stage, and a one-Husk stage bought two under Swarm. Console clean. `dotnet format whitespace` green over 10 C# files. `TimeManager.asset` reverted.

**Deviations:** 7, in the [spec's *As built*](tasks/M6-06b-four-ordeals-and-two-refusals.md). **One changes a decision:** `RunSession` now restores the set **above the opening composition**, not beside the wallet. Otherwise a resumed stage-35 run holding Swarm would compose its first stage without it. [AR §18.1](../Architecture.md#18-invariants)'s restore row is amended. Four test files sit outside the table for assembly reasons. No stage-jump was written: `Descent.asset`'s *Starting Stage* is the jump.

**Learned:** **a restore placed while nothing read it is placed by convenience, and the first reader is when that gets checked** → AR §18.1. · **An Ordeal that changes a composition also changes fixture premises** — "clear the body" meant one Husk until Swarm bought two → spent (stays here).

**Follow-ups:** none opened. [Ledger row 2](ROADMAP.md#carry-forward-into-m6) gains one clause: M6-11's session takes the Ordeals past stage 25. Row 1 already carried Swarm.

### 2026-09-23 · M6-07a · The Emberwright, the Cinder Orb, and heat that builds while nothing touches you

**Built:** a third `CharacterDefinition`, `Emberwright.asset`. It is on `BootScope`, and the class-select screen fills its third card with no prefab edit. The Cinder Orb is `WeaponKind.Projectile` at **17** damage, 1.5/s, 25 m/s and a 3 m blast. CH §3.3's 30 stays in the document with the ruling under it. `Kindling` adds one `PercentAdd` stack of +2 % to `Weapon.Damage` per swing or orb that reached anybody, caps at 30, and drops to zero on damage that was applied. A blocked hit, a dash and a zone never touch it. `MovementSkillKind.Blink` lands and does nothing yet. M5-02's named tree skip is back for `character.emberwright` until M6-08.

**Verified:** **2 900 EditMode / 0 / 0 twice** (+42 on 2 858) and **PlayMode 26 / 0 / 0 twice**. Known issue 1 did not fire. Console clean. `dotnet format whitespace` green over 14 C# files. `TimeManager.asset` reverted. Neither shipped class asset was touched.

**Deviations:** 9, in the [spec's *As built*](tasks/M6-07a-the-emberwright-and-the-cinder-orb.md). **One changes an owner:** `PlayerCombat` builds `Kindling` (Focus's arrangement), so `RunSession.cs` is untouched. The spec's control row was one stage off: 30 damage stays at two hits through **stage 12**, not 13.

**Learned:** **a spec's arithmetic table is a claim like any other** — the Bone Bolt's 0.75 s and "still 2 at stage 13" were both off, and both were caught only because the rows compute from the shipped curve rather than copy the table → spent (stays here).

**Follow-ups:** none opened. [Ledger row 2](ROADMAP.md#carry-forward-into-m6)'s predicted 3-cold / 2-hot are now asserted rows; row 7's two strings shipped.

### 2026-09-23 · M6-07b · Blink, and the first ground in this game that burns

**Built:** `ZoneSide` gives a zone one of two sides, stored per entry. A `BurnsEnemies` pulse damages every living enemy inside through `EnemySystem.ApplyDamage` and publishes one `ZoneBurned` for each, or nothing when the pool is empty. The heal path is unchanged. `ZoneSystem` holds an optional `EnemySystem`/`PlayerCombat` pair, refused half-set, and `RunSession` wires both. A Blink drops a 3 m, 3 s, 4-a-pulse pool on its start edge from the snapshot's position. Its three numbers are `Stat`s on `ChargeSkill`, read and floored at the drop. The pool is drawn as the existing cyan circle.

**Verified:** **2 927 EditMode / 0 / 0 twice** (+27 on 2 900) and **PlayMode 26 / 0 / 0 twice**. Known issue 1 did not fire. Console clean. `dotnet format whitespace` green over 10 C# files. `TimeManager.asset` reverted. `ZoneSystemTests`, `SpawnHealZoneTests` and the other two class assets are unedited.

**Deviations:** 8, in the [spec's *As built*](tasks/M6-07b-blink-and-the-ground-that-burns.md). **None changes a decision.** The Files table counted a ripple of ten `ZoneSystem` sites that the Public API's optional pair makes zero, and missed the two `Blink` sites that rule 4 makes throw. Three of M6-07a's rows described the stand-in Blink and were rewritten.

**Learned:** **a row that pins an intermediate state goes on passing after the state ends** — `Blink_LeavesNothingYet` never passed zones, so it would have stayed green beside a Blink that burns → spent (stays here). · **Rule 11 cites a `PlayerStat` remark that does not exist** → filed for [M6-08](tasks/M6-08-emberwright-tree-v1.md) in the *As built*.

**Follow-ups:** none opened. [Ledger row 1](ROADMAP.md#carry-forward-into-m6) gains the cyan-on-cyan circle.

### 2026-09-23 · M6-07c · What each class does with the Veil, and the cast you buy with it

**Built:** `VeilrotSpec` holds CH §3's Veilrot column as five dials, and `CharacterSpec` takes it optional and last. The meter reads it. A Gravecaller opens at 15, silently, and a resume overwrites that. Every gain is multiplied by the class dial before Hunger (Oathbound ×0.6, Gravecaller ×1.5), and the Gravecaller's weapon carries one `PercentAdd` of +1 % a point, rewritten when the meter moves. `Veilrot.Spend` throws when the meter is short, behind `CanSpend`. The Oathbound's Cleanse costs 30. `SkillRunner` casts an Emberwright active through its cooldown for 5 Rot, once per cooldown, without moving the clock, and publishes `CastBought`. The three assets carry the table.

**Verified:** **2 979 EditMode / 0 / 0 twice** (+52 on 2 927) and **PlayMode 26 / 0 / 0 twice**. Known issue 1 did not fire. Console clean. `dotnet format whitespace` green over 13 C# files. `TimeManager.asset` reverted.

**Deviations:** 10, in the [spec's *As built*](tasks/M6-07c-what-each-class-does-with-the-veil.md). **None changes a decision.** The paid-cast latch clears where a cooldown starts (`Fire`) rather than where it ends, which also covers a manual cast. The spec's *"four paid ones"* over 10 s is five. Five asset rows went to `CharacterDefinitionTests` because `Soulvail.Tests.Core` cannot open an asset.

**Learned:** **a latch belongs on the line that starts the thing it governs.** Clearing it where the cooldown ends would miss a ready tick that `Tick`'s one-cast-a-frame walk spent on an earlier entry → spent (stays here). · **`Relationship_OnlyAPactRaisesTheMeter` is an IL sweep for `call`/`callvirt`**, `PaletteTests.Reads` aimed at a method → spent (stays here).

**Follow-ups:** none opened. The Oathbound's third clause was already on the [parking lot](ROADMAP.md#parking-lot). M6-11 should re-read the Gravecaller's TTK with the ×1.15 start in.

### 2026-09-23 · M6-08 · The Emberwright tree v1

**Built:** twelve Emberwright nodes in Ember, Arcana and Ash, with two Actives (Emberfall and Cinder Nova) and one Upgrade (Deep Well). Both Actives cast `SpawnBurnZone`, the seventh primitive, which is registered for every class. `PlayerStat` gains `KindlingPerStack`, `KindlingMaxStacks`, `PoolDamage` and `PoolDuration`, and `PlayerStats.Has` now answers for the run. `SplashFlow` sweeps a borrowed branch for an address the class lacks. `BootScope` holds 36 skills and 3 trees, and English has 28 new rows.

**Verified:** **3 026 EditMode / 0 / 0** (+47 on M6-07c's 2 979), twice, and **PlayMode 26 / 0 / 0** twice. The Console is silent, there are zero analyzer warnings, and `dotnet format whitespace` is green over all sixteen touched C# files. `TimeManager.asset` re-serialised and was reverted. Neither of the other two tree assets is modified.

**Deviations:** 10, in the [spec's *As built*](tasks/M6-08-emberwright-tree-v1.md). **One changes a claim:** rule 5 said Ash and Arcana are lendable to every class. Ash names the Blink pool's addresses, and a Charge and a Shroudstep have none, so rule 8's sweep refuses Ash to both other classes and only Arcana is lendable. Twenty-one existing rows went red on the ripple and were fixed. The spec had listed only four of them.

**Learned:** **an address a class may not have is a new kind of address.** "Is it a member" and "does this run have one" split again at M6-08. The spec's own two rules disagreed about Ash, and counting which classes own which object settled it before a player could find it (spent, stays here). · **Count the ripple by running the old suite against the new code**, not by grepping for names. It found `GravecallerTreeTests`' pinned catalog, which no grep for `PlayerStat` would (spent).

**Follow-ups:** none opened. [Ledger row 2](ROADMAP.md#carry-forward-into-m6) gains the logged third curve: a stage-15 Husk takes 4 orbs cold and 2 hot with a full Ember branch. Row 7's count of 28 strings for M6-08 matched what shipped.

### 2026-09-24 · M6-09a · Profile v4

**Built:** `PlayerProfile` v4 carries `UnlockedCharacterIds`, `MetArchetypeIds` and `Locale`, in one bump. The v3 → v4 step grandfathers the Gravecaller and not the Emberwright. `UnlockSpec` and `ClassUnlocks` gate a class by its price, with the starter free by authoring none. GD §14.1's third term ships, walked inclusive of the stage died on. `ShardsAwarded` carries the new ids and the mode. `ShardWriter` banks Shards, archetypes and deeds in one save. `ProfileStore.Unlock` buys a class. The three assets carry 0 / 2 000 + Choirmother / 3 500 + stage 20.

**Verified:** **3 080 EditMode / 0 / 0** (+54 on M6-08's 3 026), twice, and **PlayMode 26 / 0 / 0** twice, after a first pass of 25 / 1 on known issue 1's exact *wrong wedge* signature. The Console is silent, there are zero analyzer warnings, and `dotnet format whitespace` is green over all 31 touched C# files. `TimeManager.asset` re-serialised and was reverted.

**Deviations:** 10, in the [spec's *As built*](tasks/M6-09a-profile-v4-and-what-a-shard-buys.md). **One changes a claim:** `Profile_CarriesNoNumberThatAffectsARun` said `ShardPayout` reads the profile. It never does, because the set reaches it through `RunConfig`, so the row pins `ClassUnlocks` as the only reader. `RunTicker` was edited outside the table, and `ShardsAwarded` gained `ModeId`. Manual step 1's expected 55 is 80: the Spitter arrives at stage 2.

**Learned:** **a row that pins an absence names its own executioner.** `Select_ProfileIsStillVersionThree` and `Payout_HasNoArchetypeTerm` each said "until M6-09", and both were retired rather than amended (spent, stays here). · **A refusal on a profile costs more than a refusal on a run.** A refused run loses one run, but a refused profile is replaced by the default and loses every Shard. So the profile adapter drops a bad id and the run adapter refuses it (spent).

**Follow-ups:** none opened. [Ledger row 7](ROADMAP.md#carry-forward-into-m6)'s locale field now exists for M6-10 to fill.

### 2026-09-24 · M6-09b · A class you cannot pick yet

**Built:** the class-select screen draws the unlock gate. A locked class keeps its name and its three numbers, and gains a price. It also gains a deed line when the deed is a depth: the Emberwright's *"or reach stage 20"*. The Gravecaller's Choirmother gets no line until M7-03. `ClassUnlocks.CanBuy` decides whether the card is live. One tap buys through `ProfileStore.Unlock` and redraws every card, and a second tap plays. The Shard balance is drawn in `Palette.Essence` on open and after a purchase, and at no other time.

**Verified:** **3 107 EditMode / 0 / 0** (+27 on M6-09a's 3 080), twice, and **PlayMode 26 / 0 / 0** twice. The Console is silent. `dotnet format whitespace` is green over the eight touched C# files. The prefab change only adds: 99 → 127 YAML documents. `TimeManager.asset` re-serialised and was reverted.

**Deviations:** 10, in the [spec's *As built*](tasks/M6-09b-a-class-you-cannot-pick-yet.md). **One changes the API:** `ProfileStore` is untouched, because M6-09a's `Unlock(id, catalog)` already reads the price itself and the spec's `Unlock(id, price)` would let a caller choose it. `ClassCardState` is its own file. The fourth string is `locked.buy`, so a price that can be paid reads as an offer.

**Learned:** **a per-frame latch cannot guard an awaited load.** The spec's rule 10 had `Update` clear both latches. That would reopen the double descent, so only the purchase latch is per-frame (spent, stays here). · **A pinned reader list is a ripple the Files table never counts:** `PaletteTests` names every `Palette.Essence` reader (spent).

**Follow-ups:** none. [Ledger row 7](ROADMAP.md#carry-forward-into-m6)'s four locked-class strings exist. Manual step 6 is [ledger row 1](ROADMAP.md#carry-forward-into-m6)'s device check, as the spec wrote it.

### 2026-09-24 · M6-10 · The rest of localisation

**Built:** `ILocalizer.Format`, a second member rather than a `params` overload of `Get`, which writes numbers in the reader's culture and returns the row whole on a bad placeholder. `TableLocalizer` reads one of many tables with English behind it, two deep. A table carries a `_locale`. The device's language is the default, and `BootFlow` applies the profile's once, before the Menu. `LocalisationSweepTests` sweeps both assemblies' IL for keys with no row, and generates and checks `Pseudo.asset`: 185 `qps-ploc` rows on `BootScope`, from a menu item. The sentence callers moved to `Format`, and four English formats typed into code became rows.

**Verified:** **3 140 EditMode / 0 / 0** (+33 on M6-09b's 3 107), twice, after passes at 3 138 / 2 (the font row's first draft, deviation 9's gap) and 3 139 / 1 (a row that named this task). **PlayMode 26 / 0 / 0** twice, after two passes at 25 / 1 on Unity's AI Assistant logging mid-row (Known issue 6). The Console is silent. `dotnet format whitespace` is green over all 21 touched C# files. `BootScope.prefab` only adds two lines. `TimeManager.asset` re-serialised and was reverted.

**Deviations:** 10, in the [spec's *As built*](tasks/M6-10-the-rest-of-localisation.md). **One changes a decision:** rule 5's line is *a word or a sentence moves, digits and symbols stay*, so `SkillRow`'s thresholds stay invariant to keep a number when its trigger row is missing, and `Sweep_InvariantCultureStaysOutOfSentences` names the four types left. AR §6's row is corrected, as rule 4 said.

**Learned:** **a second sweep over the same ground finds the first one's gaps** — the code-and-assets cross-check flagged three class descriptions drawn since M5-07 that M3-14b's walk never asked about (spent, stays here). · **A test of what a generator produces must not re-judge what it was given**: the font row went red on English's own `→`, and it now checks only the characters the generator adds (spent).

**Follow-ups:** **[Ledger row 7](ROADMAP.md#carry-forward-into-m6) DISCHARGED.** [Row 1](ROADMAP.md#carry-forward-into-m6) gains step 5 and the `→` glyph. The [parking-lot](ROADMAP.md#parking-lot) languages line now lists the four things M6-10 left the first real language.
