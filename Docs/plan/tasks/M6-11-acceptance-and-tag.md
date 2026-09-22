# M6-11 — M6 acceptance: two instruments, a frame-order verdict, and tag `m6`

**Size:** S · **Depends on:** everything in M6 · **Branch:** `m6-11-acceptance`
**Design refs:** GD §6.2, §9.1, §10, §11.3, §12.2, §12.4, §12.5, §13.1, §13.2, §13.3, §13.4, §14.1, §14.2, §15, §16.1, §16.4, §19; CH §3, §3.3, §4, §5.4; AR §18; ADR-0006, ADR-0008, ADR-0009, ADR-0011, ADR-0012 · **Ledger rows:** **all eight — this is where [M6's carry-forward table](../ROADMAP.md#carry-forward-into-m6) is closed and M7's is opened.** Rows **2** and **4** are this task's own to rule on; the other six close by their owners' *As built* or carry with a named unmerged owner

## Goal

Answer the M6 question on evidence — *are the systems a run is made of complete?* — take the two
numbers [rows 1 and 2](../ROADMAP.md#carry-forward-into-m6) have been owed since M3, look past stage
25 for the first time in this project's history, settle what the frame-order assertion means, and
tag `m6`.

## Two instruments, not one, and M5-08 is why there are any

M5-08's closing lesson is the rule this milestone opened under: **a row that wants a number is
discharged by an instrument, not by an argument.** That acceptance instrumented a build, played two
runs and closed three rows no amount of reading had moved. This one inherits **two** instruments,
each named by a merged spec before M6-11 existed.

**(A) Hits-to-kill, at three classes.** [Ledger row 2](../ROADMAP.md#carry-forward-into-m6) names
this task in writing: *"M6-11's acceptance carries the same hits-to-kill instrument M5-08 used, taken
for all three classes at the same depths, so M8-05 opens with three measured curves rather than two
and a guess."* M5-08 measured two — the Oathbound flat at 4 from stage 4 to 16, the Gravecaller
drifting 4 → 8 and breaking GD §12.4's band at stage 9. **The third is the one this milestone
authored**, and [M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md) already made two predictions
about it that a played run confirms or refuses:
- **3 hits cold and 2 at full Kindling**, at stage 1 — the ruling that cut CH §3.3's 30 to 17.
- **A curve that stays inside the band at depth**, because
  [M6-08](M6-08-emberwright-tree-v1.md) rule 11 gives the tree **four** weapon-scaling nodes where
  the Gravecaller's has one, for something like **×2.9** against depth rather than ×1.15.
  **If that is right, this acceptance has the first tree in the project that answers ledger row 2's
  cause rather than adding to it. If it is wrong in the other direction, the Emberwright breaks the
  band from the bottom and the fix is M8-05's either way.**

**(B) The first look past stage 25, because nothing in this game has ever been there.**
[M6-06b](M6-06b-four-ordeals-and-two-refusals.md)'s own note: *"Ordeals begin at stage 25 and M5-08's
two instrumented runs reached 16 and 19, so 'does Swarm make stage 35 unreadable' is a question no
Editor row here can answer."* Everything M6 built at depth is unobserved: the four Ordeals, the
Claiming's hundred seconds, a Sanctum at stage 30's prices, and GD §12.5's own death-horizon table,
which says a *"skilled, well-built"* player dies at **35–50** and nobody has been past 19.
- **It needs the debug stage-jump** M6-06b manual step 1 ships — no new file.
- **It is not a balance pass.** What it produces is a log; what the log is *for* is M8-05.
- **GD §11.3's 28-body budget rides on it**: M5-08 measured 37 bodies in one arena and
  [M6-06b](M6-06b-four-ordeals-and-two-refusals.md) rule 3 raises the cap by up to eight on top,
  which is the one thing here that could be a frame-rate incident rather than a number.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, both instruments' numbers, the row-4 verdict, Current State pointing at M7-00a (rule 3) |
| `Docs/plan/archive/PROGRESS-M6.md` | M6's log entries, moved — **on time, for the fifth milestone running** (rule 5) |
| `Docs/plan/ROADMAP.md` | M6 ticked; the M6 ledger closed and **carry-forward into M7** opened; M7's rows promoted from titles where this checklist demands it |
| `Docs/plan/archive/ROADMAP-M6.md` | M6's task table, spec-group notes and ledger, moved — and **every relative link in it re-based**, which is rule 11's second pass and the failure M5-08 found in `PROGRESS-M3.md` |
| `Docs/GameDesign.md` · `Docs/Characters.md` · `Docs/CoreCombat.md` | Only if tuning changed a shipped number, or a flagged contradiction is resolved (rules 1, 9) — **GD §13.2 is expected, see rule 10** |
| `Assets/_Project/Data/…` | Only if tuning changed — `Emberwright.asset`, `Descent.asset`, the four Ordeals, the four Pacts, the twelve Emberwright nodes, `English.asset` |

No code. **A bug found here becomes `M6-11a…` with its own PR** — the M2-15a, M3-15, M4-07 and
M5-08a precedent, and rule 8's one exception.

## Checklist (owner)

Ticked at two grains, and the entry says which is which: **[play]** rows are covered by the owner's
playtest verdict rather than attested one at a time; unmarked rows are verified statically against
the assets, the code and the suite. **[device]** rows cannot be answered in the Editor and are
deferred (rule 7).

**The systems a run is made of, complete** — the ROADMAP's *ends when* for M6, read literally

- [ ] **[play]** A stage clear pays Essence, the number climbs, and it survives a `Continue` ([M6-01a](M6-01a-essence-wallet-and-drops.md), [M6-01b](M6-01b-save-format-v4.md))
- [ ] **[play]** A cleared stage ends in an **untimed room** rather than a door, and **a boss stage is not over while an add is still swinging** — [ledger row 5](../ROADMAP.md#carry-forward-into-m6) closed by looking at it ([M6-02a](M6-02a-the-sixth-phase.md))
- [ ] **[play]** Four priced rows; three of them can be refused and each says **why** ([M6-02b](M6-02b-four-things-essence-buys.md), [M6-03a](M6-03a-the-sanctum-screen.md))
- [ ] **[play]** A meter down the right edge, four marks, a counter in the corner, and a banished node drawn as gone ([M6-03b](M6-03b-the-meter-on-the-right-edge.md))
- [ ] **[play]** Veilrot only goes up, 75 takes a fifth of the maximum, and **100 buys a hundred seconds and then kills you** ([M6-04](M6-04-veilrot-thresholds-and-the-claiming.md))
- [ ] **[play]** A violet card with its own sentence and its own Rot price, and taking it moves the meter ([M6-05a](M6-05a-what-a-pact-is.md), [M6-05b](M6-05b-the-offer-that-rolls-one.md))
- [ ] **[play]** An Ordeal is dealt at 25 and every ten stages after, four of them accumulate, and each moves exactly the one dial it owns ([M6-06a](M6-06a-what-an-ordeal-is.md), [M6-06b](M6-06b-four-ordeals-and-two-refusals.md))
- [ ] **[play]** A third class: a slow orb with a 3 m blast, a ramp that pays for not being touched, a teleport that leaves fire, and a tree of its own ([M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md), [M6-07b](M6-07b-blink-and-the-ground-that-burns.md), [M6-08](M6-08-emberwright-tree-v1.md))
- [ ] **[play]** The Oathbound cleanses at half price, the Gravecaller opens at 15 Rot and gets stronger as it rots, and the Emberwright casts through a cooldown for 5 ([M6-07c](M6-07c-what-each-class-does-with-the-veil.md))
- [ ] **[play]** A locked class shows its price and **a Shard is spent for the first time in this project** ([M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md), [M6-09b](M6-09b-a-class-you-cannot-pick-yet.md))
- [ ] `RunSnapshot.CurrentVersion` is **4** and `PlayerProfile.CurrentVersion` is **4**, and **each was bumped exactly once** — M6-01b's and M6-09a's whole argument, checkable by reading two constants
- [ ] The whole game runs in the pseudo-locale with nothing clipped and **no plain English left on any screen** ([M6-10](M6-10-the-rest-of-localisation.md))

**Instrument A — hits-to-kill, three classes, [ledger row 2](../ROADMAP.md#carry-forward-into-m6)**

- [ ] **[play]** Hits to kill a Husk at stages **1, 5, 10, 15, 20, 25, 30**, per class, against GD §12.4's 3–5 band, with the tree taken recorded beside each number
- [ ] **[play]** **The Emberwright's two predicted numbers**: 3 cold and 2 at full Kindling at stage 1 ([M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md)'s ruling), and whether the curve holds inside the band where the Gravecaller's does not ([M6-08](M6-08-emberwright-tree-v1.md) rule 11's ×2.9)
- [ ] **[play]** **How often Kindling is actually at full.** The ramp needs thirty consecutive weapon hits on 70 HP; if a played run never sees +60 %, the signature is decorative and that is a finding rather than a number
- [ ] **This is the fourth acceptance row 2 has been owed to** (M3-15, M4-07, M5-08, here). M5-08 discharged the *measurement* for two classes; **M6 owes the third and nothing else**, and if it carries again the reason is written here rather than implied

**Instrument B — past stage 25, for the first time**

- [ ] **[play]** Reach **stage 35 at minimum** with the debug stage-jump, on at least one class, with the overlay up
- [ ] **[play]** **Which Ordeals were dealt, at which stages, and what each visibly did** — Famine's thinner income, Vigil's two cards, Swarm's fuller arena, Hunger's faster meter ([M6-06b](M6-06b-four-ordeals-and-two-refusals.md) manual step 2's list, played rather than reasoned)
- [ ] **[play]** **Body count at stage 35 under Swarm**, against GD §11.3's 28 and the 37 M5-08 measured. **And whether the arena is readable**, which is what GD §12.2's cap is half for
- [ ] **[play]** **Frame time past stage 30.** M5-08 found 36 hitches of 50–300 ms across ~30 minutes and correlated none with a mechanic; this is the same measurement at twice the depth and a raised cap
- [ ] **[play]** **The Claiming, end to end**: reach 100, and record whether a hundred seconds of +100 % damage bought two more stages or one — GD §10.3's own claim, measured
- [ ] **[play]** **What the Sanctum costs at depth.** Essence is `20 + 4n` (GD §15) and the Reroll doubles; at stage 35 income is 160 a stage and a fifth reroll is 400. Whether the shop is still a decision or has become free money is a number this is the first run able to take

**Ledger row 4 — what the project's one frame-order assertion means (a verdict, not a fix)**

- [ ] **The row is M6-11's *as a verdict*** and M4-07 rule 11 admits that only because it is a judgement rather than a repair. The standing ruling is unchanged: **no task may "fix" it without deciding what the assertion means first**
- [ ] The instrument has now answered **twice** and both times said ***wrong wedge*** rather than *late sync* — M5-06a's failure put the body **0.0001 m behind the apex**. So the seam's synchronisation is not late and the fault is the fixture's own **zero-margin apex**
- [ ] **Decide, and write the decision down rather than the outcome:** either `Ticker_RunsTheStepsInOrder` asserts *"the body was inside the wedge"* (and the apex needs a margin, which weakens the claim by a measurable amount that has to be stated), or it asserts *"the seam is synchronous"* (and the wedge test is the wrong probe for it and should be replaced by one that reads the sync directly)
- [ ] **What changed since the row was written, and it changes the reasoning rather than the verdict:** the row says *"M6 opens `Tests/PlayMode` for nothing"*, which was true of [M6-00a](M6-01a-essence-wallet-and-drops.md)'s five Core specs and **false as of M6-00b** — [M6-03a](M6-03a-the-sanctum-screen.md) adds a sixth phase to `RunTicker`, and `Frame_LevelUpPhaseRunsAboveCommands` lives in that fixture. So a task **did** have a reason to be in the file, and whether it disturbed the flake is evidence this acceptance has and the row's author did not
- [ ] **Known issue 7 is beside it and is not the same kind.** `Animator_AttackSpeedIsUnreachableFromAnEditorClock` asserts `Time.time` is 0 outside Play and an Editor that has ticked reads 0.73 — a **false premise**, where row 4 is a **margin**. Rule both or say which is carried

**Ledger rows closed by their owners — verified, not assumed** (rule 2)

- [ ] **Row 5** — `SpawnDirector.IsStageComplete` requires the adds cleared, `Boss_AnAddCannotHitThePlayerThroughTheSanctum` green, and the depth the payout reads is unmoved ([M6-02a](M6-02a-the-sixth-phase.md))
- [ ] **Row 7** — [M6-10](M6-10-the-rest-of-localisation.md) shipped, and **it discharges as something other than what the row asked for**: the mechanism, two sweeps and a pseudo-locale, with *"the other languages' tables"* refused because no document names a language. Confirm the refusal is on the [parking lot](../ROADMAP.md#parking-lot) with the decision it waits on, and that the row's count moved **31 → 65**
- [ ] **Row 8** — `Palette_HasTheColoursNobodyReadsYet` narrowed to a sweep at [M6-03a](M6-03a-the-sanctum-screen.md) and retired at [M6-03b](M6-03b-the-meter-on-the-right-edge.md); both `Palette.Essence`'s and `Palette.Veilrot`'s summaries corrected. **Neither did the other's half**, which the row required in writing
- [ ] **Rows 1 and 3 do not close and have never claimed they would** — each says *no M6 owner* with the reason. Re-state what M6 **added** to row 1 (the Sanctum's four rows, the meter's violet, the Claiming's falling bar, the Pact card, Swarm's raised cap, three class cards with two refusals, and [M6-10](M6-10-the-rest-of-localisation.md)'s 35 % expansion check) rather than re-listing the whole row

**The M6 question, and it is the milestone's** (record the answer and *why*)

- [ ] **[play]** **Is the economy a decision or an inventory?** GD §13.3's shop has four services and GD §10 is the game's *"if we cut one thing, it should not be this"* section. The honest failure mode is that the player buys Heal every time and never touches the other three, or that Essence accumulates faster than there is anything to spend it on
- [ ] **[play]** **Is a Pact ever taken?** GD §13.2's whole design is a continuous temptation. If the answer is *never* the price is too high or the power is too low; if it is *always* the corruption is not a cost. **Count them** — offers rolled, offers taken — the way M5-08 counted casts
- [ ] **[play]** **Is the Emberwright a third class or a third reskin?** M5-08 deferred the same question about the Gravecaller to **M8-01** on the owner's ruling, *"because second class or reskin cannot be judged on capsules"*. That ruling covers this one too, and the honest thing is to ask it and defer it in the same sentence rather than to leave the row off

**Findings this milestone's specs predicted and the playtest must confirm or refuse**

- [ ] **A healing circle and a burning circle are the same cyan** — [M6-07b](M6-07b-blink-and-the-ground-that-burns.md) rule 9. Reachable only by an Emberwright that borrows *Judgment* at CH §5.4's half-tree moment. Record whether it reads as a bug
- [ ] **An Oathbound is offered *Ash* and *Arcana* and refused *Ember*** — [M6-08](M6-08-emberwright-tree-v1.md) rule 8's third sweep, which is M5-08a's defect caught by counting rather than by playing. Either the refusal is drawn (rule 8 working) or it is not (a finding, and `M6-11a`)
- [ ] **A Claimed Gravecaller at 100 Rot does four times its base weapon damage** — [M6-07c](M6-07c-what-each-class-does-with-the-veil.md) rule 9, pinned rather than discovered. Record whether that is a build or a bug
- [ ] **The owner's own profile finds the Emberwright locked at 3 500** — [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md) rule 10's stated cost, and the only place the gate is visible on an install that has been playing since `m5`
- [ ] **A run restored at exactly 100 Veilrot comes back Claimed with the clock reset** — M6-04 rule 9's stated cost, *"a `Continue` being worth up to a hundred seconds"*. Seen once on purpose so it is never filed as a bug

**Performance and hygiene** (GD §11.1, AR §14)

- [ ] `AllocationAssert` rows green across every new `Tick` path — `Veilrot`, `EssenceWallet`, `Ordeals`, `SanctumShop`, `Kindling`, `ZoneSystem`'s burn, and `SkillRunner`'s paid cast
- [ ] **[play]** No hitch when the Sanctum opens, when an Ordeal is dealt, when the Claiming starts, or when a pool burns twenty-eight bodies
- [ ] Six assemblies, zero compile errors, zero analyzer warnings, the full suite green through `TestRunnerApi` — **EditMode and PlayMode both, and PlayMode run more than once**
- [ ] `dotnet format whitespace --folder --verify-no-changes` green over `Assets/_Project`
- [ ] `git diff m5 HEAD -- ProjectSettings/` shows **nothing**, or one line committed on purpose with `ALLOW_PROJECT_SETTINGS=1` and named here. **`TimeManager.asset` is expected**

## Behaviour

1. **Tuning edits go to the ScriptableObjects and the named constants only**, and GD, CH and CC are
   updated in the same PR. Doc and asset never disagree — M2-15 rule 1. **The likeliest to move are
   the numbers with no document behind them**: the Emberwright's 17, the fire pool's four, the two
   Actives' five numbers each, and the four Pacts' Veilrot prices.
2. **The ledger is closed row by row, and a row is only closed by its owner's *As built*.** Eight
   rows; for each, either the owning task's footer says it is answered — in which case it is struck —
   or it is carried into M7's table with the task that will answer it. **M4-07 rule 11 holds: every
   carried row names an *unmerged* owner, or says explicitly that it has none and why.**
3. **The M7 ledger is opened in the same pass**, from three sources: rows carried forward,
   parking-lot entries that have acquired an owner, and this checklist's own misses. M7's specs are
   written against it. **M5-08's rule applies at the opening rather than at the closing**: a row whose
   text contains a question about what happens *in play* names the **instrument**, not the task that
   will think about it.
4. **The PROGRESS entry lists what M7 must know.** Especially: which of M6's authored numbers the
   playtest moved; whether four `PlayerStat` addresses that depend on the class is the right shape at
   sixteen members ([M6-08](M6-08-emberwright-tree-v1.md) rule 8); whether `CharacterSpec`'s four
   nullable blocks survive a fourth class; and **what the stage-35 log says about GD §12.5's death
   horizon**, because M7-04 authors eighty-one nodes against it.
5. **The milestone is archived here, on time — the fifth time.** Promote durable lessons out of the
   entries first — Traps.md, AR §18, the M7 ledger — then move them. **Current State's three growing
   rows get M4-07 rule 5's rolling treatment**, under PROGRESS's own 10 000-byte cap, measured rather
   than estimated.
6. **When every box is ticked: the owner merges `dev` → `main` and tags `m6`. Claude does not tag.**
   **And `m4` is still the owner's to tag** — it has been pending since M4-07 and a milestone tagged
   out of order is worth one line in the handover rather than a silent gap.
7. **The grain is in the tag message**, M1-21's through M5-08's shape: *Editor evidence, device rows
   deferred* — **and multi-touch named**, so `m6` is never read later as a hardware sign-off.
   **A tag message is not a commit message** and the handover labels which is which.
8. **A structural miss becomes an M7 task or a ledger row, not a late edit to this branch.** A
   *number* can move here (rule 1); a mechanism cannot. The one exception is a **bug**, which becomes
   `M6-11a` with its own PR — and the three likeliest sources are named in the checklist above rather
   than left to be discovered.
9. **Flagged design contradictions are resolved here or carried with a reason**, and M6 opened three.
   Rule 10 rules on all three in one place, because two of them are the same argument.
10. **The three flagged lines, and which of them the tag actually waits on.**
    - **GD §13.2's table contradicts its own worked examples — and `m6` should not be tagged over
      it.** The table says *"Power: ~1.8×"* against a clean node; the examples two lines below carry
      **downsides** — *"+45 % damage, **−25 max HP** (+15 Rot)"* — which no multiplication produces.
      [M6-05a](M6-05a-what-a-pact-is.md) ruled **for the examples** and shipped four authored Pacts,
      so the build and the document now disagree on the record about this milestone's signature
      system. **It is a one-line edit either way** — *"roughly 1.8× the power budget"* on the table,
      or the downsides off the examples — and it is M2-15 rule 1's *doc and asset never disagree*
      applied to the one place M6 left them disagreeing. **The same edit settles CH §3.1's third
      Oathbound clause**, which [M6-07c](M6-07c-what-each-class-does-with-the-veil.md) refused for the
      identical reason: *"Pact effects are 25 % weaker"* is a multiplication of a hand-authored node
      with a downside in it. **One ruling, two lines, and the acceptance is where it belongs** because
      it is the owner's call and this is the task that is allowed to spend one.
    - **Fracture stays refused and does not block the tag.**
      [M6-06b](M6-06b-four-ordeals-and-two-refusals.md) refused it on three counted grounds and
      `Arena_HasFewerThanFourPillarsToSpare` pins the arithmetic. Answering it needs arenas that do
      not exist; the owner is **M7-05/06** and nothing this acceptance can measure moves it.
    - **Echo stays refused and does not block the tag — but this is the first task that can put a
      number under it.** Its two readings have opposite signs and the choice is a design decision
      owned by the owner or **M7-02**. What instrument B produces for free is the thing the argument
      was missing: **what one wave in five is actually worth at stage 35**, measured rather than
      derived from the triangular split. Record it in the entry so M7-02 opens with a figure instead
      of with 6.7 % on paper.
11. **Three splits happened in this milestone and the id-drift pass is owed.** M6-01, M6-02, M6-03,
    M6-05, M6-06, M6-07 and M6-09 all split, and the
    [parking lot](../ROADMAP.md#parking-lot) records that *"already-merged specs still point at
    pre-split task ids"* recurs on every split and that **the next milestone that splits owes the same
    pass to its acceptance**. This is the **sixth** milestone it has applied to. **And it is two
    passes, not one** — M5-08 found that `PROGRESS-M3.md` had been archived without re-basing its
    relative paths and all **73** of its links had pointed at nothing since M3 closed — so rule 5's
    archive re-bases every link in `PROGRESS-M6.md` and `ROADMAP-M6.md` and verifies them file by
    file.

## Acceptance

- [ ] Checklist ticked; the M6 question answered, with the Editor-versus-device grain stated
- [ ] **Instrument A carries a table, not a shrug** — hits to kill at seven depths for three classes,
  with the tree taken beside each number, and the Emberwright's two predicted figures confirmed or
  refused
- [ ] **Instrument B carries a log** — a run past stage 35, the Ordeals it was dealt, the body count
  under Swarm, the frame times, and what the Claiming bought
- [ ] **Row 4 carries a decision about what the assertion means**, not a tally of how often it failed
- [ ] Every ledger row struck or carried, with the *As built* that closed it named, and **every
  carried row naming an unmerged owner** (rule 2)
- [ ] **Rule 10's three lines each have a sentence**: one ruled, two refused with owners
- [ ] `PROGRESS.md` updated, M6 entries archived, Current State pointing at **M7-00a**, and rule 5's
  rolling treatment applied under the measured 10 000-byte cap
- [ ] Rule 11's two passes done, and the parking-lot line updated with the sixth milestone
- [ ] `m6` exists on `main` — **the owner's to make**, with rule 7's message

## Out of scope

- **Balancing anything.** **M8-05**, and this task is the instrument it opens with. A number may move
  under rule 1; a *curve* may not.
- **The UI redesign.** [Ledger row 3](../ROADMAP.md#carry-forward-into-m6), unmoved for three
  milestones by the owner's standing ruling. M7-04 brings the nodes and M7-05/06 the art; **M8-01** is
  the earliest honest owner, and it is where the feel verdict is re-taken.
- **Fixing what [M6-10](M6-10-the-rest-of-localisation.md)'s pseudo-locale pass finds.** A clipped
  label is a layout defect and belongs to row 3 with the rest. This task records the list.
- **The Revenant, Fracture, Echo, the Choirmother's deed and cosmetics.** Each was refused in writing
  by a merged spec with an owner named; rule 10 confirms the owners rather than re-opening them.
- **Tether, Rot Nova, the nine Keystones and the remaining forty-five nodes.** M7-04.
- **A second language.** [M6-10](M6-10-the-rest-of-localisation.md)'s refusal, which waits on GD
  §21.1's business-model decision rather than on a task.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
