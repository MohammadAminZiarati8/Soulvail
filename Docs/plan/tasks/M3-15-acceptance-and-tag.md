# M3-15 — M3 acceptance: does a run get better as it goes? The ledger closed, and tag `m3`

**Size:** S · **Depends on:** everything in M3 · **Branch:** `m3-15-acceptance`
**Design refs:** GD §7.3, §12.4, §12.5, §13.1, §16.2, §16.4; CH §4.1, §5, §5.1, §5.2; CC §6.1–6.4, §7; AR §18; ADR-0008, ADR-0009, ADR-0012 · **Ledger rows:** **all nine — this is where [M3's carry-forward table](../ROADMAP.md#carry-forward-into-m3) is closed and M4's is opened.** Rows 1, 4, 8 and 9 are this task's own to rule on

## Goal

Answer the M3 question on evidence — *does a run get meaningfully stronger as it descends, and is choosing how still a two-second decision?* — prove every ledger row left the table, and tag `m3`.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, the feel verdict, tuned numbers, Current State pointing at M4-01 |
| `Docs/plan/archive/PROGRESS-M3.md` | M3's log entries, moved — **on time, for the second milestone running** (rule 5) |
| `Docs/plan/ROADMAP.md` | M3 ticked; the M3 ledger closed and **carry-forward into M4** opened; M4's rows promoted from titles where this checklist demands it |
| `Docs/GameDesign.md` · `Docs/Characters.md` · `Docs/CoreCombat.md` | Only if tuning changed a shipped number, or a flagged contradiction is resolved (rules 1, 9) |
| `Assets/_Project/Data/…` | Only if tuning changed — `Descent.asset`, the twelve nodes, `English.asset` |

No code. **A bug found here becomes `M3-15a…` with its own PR** — the M2-15a precedent, which is exactly what that task was and which found a four-task-old bug.

## Checklist (owner)

Ticked at two grains, and the entry says which is which: **[play]** rows are covered by the owner's playtest verdict rather than attested one at a time; unmarked rows are verified statically against the assets, the code and the suite. **[device]** rows cannot be answered in the Editor and are deferred to the first session with a phone (rule 7).

**The loop got a tree** — the ROADMAP's *ends when* for M3, read literally

- [ ] **[play]** Kills pay XP, a strip fills, a level arrives. The first level lands inside the first stage
- [ ] XP is `3 × threat cost` and nothing else, so a stage's XP is a function of its budget rather than of what the seed drew (M3-01a rule 2)
- [ ] **[play]** A level-up **stops the world** — enemies frozen mid-step, no telegraph filling, no animation — and resumes exactly where it stood (GD §11.4, M3-08a rule 10)
- [ ] **[play]** Three cards, drawn from what is *available*, never free-picked from the whole tree (CH §5.1, GD §13.1)
- [ ] **[play]** A double level-up is **one screen that re-draws**, and the second offer can hold what the first pick unlocked (M3-08a rule 6)
- [ ] **[play]** View Tree from both doors, read-only, and looking costs no pick (M3-09d rules 1, 6)
- [ ] **[play]** Both Actives fire on their own triggers with no input, and their cooldowns are visible while they do (GD §16.1, CC §6.1)
- [ ] **[play]** A skill set to Manual leaves the auto-cast row, gains a button, and fires on a thumb (M3-10a, M3-10b rule 5)
- [ ] Cooldown reduction floors at 40 % of the authored value, wherever it comes from (CH §4.1, M3-06 rule 1)
- [ ] **[play]** Past the twelfth node a level is Overflow: no screen, a toast, a running total (CH §5.2, M3-08a rule 7)
- [ ] Every number is in an asset — the twelve nodes, both Actives' triggers, the curve, Overflow's 2 % (ADR-0006)
- [ ] `Auto`/`Manual` and slot positions survive a kill from recents; the chain runs v1 → v2 → v3 (M3-07b)
- [ ] **[play]** The Skills screen says what each Active is waiting for, in words (M3-09b, M3-14a)

**Ledger row 1 — the TTK band, the row this milestone exists to close**

- [ ] **[play]** **3 / 4 / 5 hits on a basic enemy at stages 1 / 15 / 30.** M3-12c rule 8's table is arithmetic against a stationary Husk; this row is a played run agreeing with it or not. GD §12.4 wants 3–5 at **every** depth
- [ ] `TimeToKillTests` green, including the `Ttk_UnlevelledStageThirtyStillFails` control — the row that proves the test measures the right thing (M3-12c rule 9)
- [ ] **Record the warning, whatever the verdict:** the band holds at stage 30 because of **29 Overflow levels against twelve nodes**, not because of the tree. M7-04's full twenty-seven shifts the balance back, and a *"row 1 closed"* that does not say this hands M7 a surprise
- [ ] One-shot rule still holds at both depths — no non-boss hit above 35 % of max HP, with `MaxHp` now moving under Overflow and two nodes (GD §12.4)
- [ ] GD §12.5's shape: tree power roughly linear against quadratic scaling. **If a twelve-node build is unkillable at stage 30, the row is not closed** — it is inverted, and that is worse

**Ledger row 8 — the stage band, and which number it is**

- [ ] **[play]** Stages 1, 5 and 10 **timed, not estimated** — the row M2-15 carried rather than invented
- [ ] **Both numbers quoted, and labelled.** `RunState.Time` counts *play* seconds: a gated frame contributes no `Dt`, so level-ups and pause menus cost it nothing (M3-08a rule 11, M3-09a rule 10). A stopwatch counts play **plus** every screen. **The band is measured against the stopwatch** — GD §7.3's *"a stage is a commute unit… a player can always finish the stage they're in"* is a claim about a person on a bus, and a player reading three cards is still in the stage. Play time is recorded beside it as what the simulation believes, and the gap between them is the interruption budget GD §13.1 spends
- [ ] **CH §5.2's exponent, promoted here by the [parking lot](../ROADMAP.md#parking-lot).** M3-01a rule 9 shipped 1.4 as authored and said only a stopwatch could settle it: the curve fills a 27-node tree by stage 20 rather than 30, and its own table says 8 / 13 / 22 / 30 at stages 5 / 10 / 20 / 30 against the arithmetic's 8 / 14 / 27 / 42. Check the observed level at stages 5 and 10 against both. **The fix is one number in `Descent.asset` (≈1.6) and one CH §5.2 line** — and it is a *tuning* change, so it lands here rather than becoming a task

**Ledger row 9 — node text, six readers closed and one not**

- [ ] **[play]** **GD §13.1's two-second rule, judged for the first time in the project's history**, on twelve real English descriptions (M3-14a). A description needing two lines at card width is an effect to redesign, not a font to shrink
- [ ] **[play]** CH §5.1's whole case — *"random from available"* works because reading three cards is fast — stands or does not
- [ ] Every key in every asset resolves (`M3-14b`'s `EveryLocKey_ResolvesInEnglish`), and no English is typed into a prefab (AR §11.5 finally true)
- [ ] **The seventh reader is recorded as open, not closed:** M3-10b rule 8's 24 dp auto-cast cells hold neither a key nor a word, so which skill a cell shows is not communicated at all. **That is art, not localisation**, and M6-10 cannot fix it. Parking lot, promoted by M7's art pass — or by this playtest, if the row is unreadable without icons (M3-10a rule 9 has the same gap on the slot buttons, where position at least distinguishes them)

**Ledger rows closed by their owners — verified, not assumed** (rule 2)

- [ ] **Row 2** — three format bumps shipped with their migrations: `RunSnapshot` v2 (M3-01b), v3 (M3-07b, the first two-step chain), `PlayerProfile` v2 (M3-09c, the first profile step ever to run). `Chain_IsUnbrokenFromOldestToCurrent` loops over three versions rather than one, and both v1 fixtures still decode
- [ ] **Row 5** — `PlayerAnimatorView` has the suite it shipped without (M3-11c rule 8), written **without changing its behaviour**; anything it found is in that task's *As built*
- [ ] **Row 6** — one `Palette` file, nine readers, no serialized `Color` left on any of them (M3-13a), and `#FF4A1F` is a rule a test enforces
- [ ] **Rows 3 and 7** — closed before M3's specs were written (M2-15a, M3-00a). Confirm neither has regressed: `FrameOrderTests` green three runs consecutively, and the `ProjectSettings` hook still refuses a staged change

**Ledger row 4 — the device debt, re-stated rather than listed** (the owner's ruling at M3-00d)

- [ ] `m3` is accepted on **Editor evidence**, like `m0`, `m1` and `m2`, and the tag message says so (rule 7)
- [ ] **Name the one row that is not a feel question:** M3-10a's multi-touch — a thumb on the stick **and** a thumb on S3, read at the same time. If Android delivers only one, **manual casting does not work at all**, and that is a shipped feature being wrong rather than a number being off. Every other device row risks a verdict; this one risks a feature
- [ ] The rest re-stated and carried: haptics and M1-20's 100 ms coalescing window, touch latency, real frame rate, thermal, landscape flip, sustained fps at the concurrency cap, **the Profiler's no-per-frame-`GC.Alloc` check unmet since M1-21**, `Physics.SyncTransforms()`' per-frame cost (M2-15a), kill-from-recents mid-stage, and whether the M0–M2 feel verdicts survive leaving the Editor
- [ ] M3's own additions: a 4 dp XP strip beside a notch, a 24 dp radial fill, twelve tree cells in a landscape safe area with English in them, a 3 dp enemy bar, whether the damage tint reads in peripheral vision (GD §16.2's entire claim for it), and whether `timeScale` 0 at 30 fps actually recovers thermal headroom

**Performance and hygiene** (GD §11.1, AR §14)

- [ ] `AllocationAssert` rows green across every new `Tick` path — `SkillRunner`, `TimedEffects`, `ZoneSystem`, `LevelUpFlow`, `EffectRegistry`, `TableLocalizer.Get`
- [ ] **[play]** No hitch when a level-up screen opens or closes, and none when a zone or a shell spawns
- [ ] Six assemblies, zero compile errors, zero analyzer warnings, the full suite green through `TestRunnerApi` — **EditMode and PlayMode both, and PlayMode run more than once** (M2-15's qualified tick, and M2-15a's lesson that proof of an intermittent fix is repetition)
- [ ] `git diff m2 HEAD -- ProjectSettings/` shows **nothing**, or one line committed on purpose with `ALLOW_PROJECT_SETTINGS=1` and named here (row 7's hook)

**The feel question** (record the answer and *why*): **does a run get better as it goes?** Descending with a twelve-node tree — do the picks change how you play, or do they change a number? If it does not land, which is wrong: the XP curve, the offer weighting, the twelve nodes' values, their **mix** (six stat lines of twelve — M3-12c counts it honestly and GD §13.1 says *"ship 81 nodes where half are stat lines and we've built a spreadsheet with a shooter attached"*), Overflow's 2 %, the 40 % cooldown floor, or the two Actives' triggers.

## Behaviour

1. **Tuning edits go to the ScriptableObjects and the named constants only**, and GD, CH and CC are updated in the same PR. Doc and asset never disagree — M2-15 rule 1, which is also what licensed that task to fix two GD numbers nothing had tuned.
2. **The ledger is closed row by row, and a row is only closed by its owner's *As built*.** Nine rows; for each, either the owning task's footer says it is answered — in which case it is struck — or it is carried into M4's table with the task that will answer it. **A row silently dropped is the failure the ledger exists to prevent** (M2-00a), and M2-15 closed fourteen this way without dropping one.
3. **The M4 ledger is opened in the same pass**, from three sources: rows carried forward, parking-lot entries that have acquired an owner, and this checklist's own misses. M4-00's specs are written against it.
4. **The PROGRESS entry lists what M4 must know.** Especially: anything about the tree, the runner, `TimedEffects` or the three formats that came out different from its spec; **whether `SkillRunner`'s twelve-Active cap and `TimedEffects`' sixteen-entry capacity held**, since a boss with adds and phases is the next thing to press on both; and the state of `EnemySpawned.IsElite` (M3-13b rule 6), which M4's Warden is the first body to arrive as something other than an archetype.
5. **The milestone is archived here, on time — and the second time is what makes it a rule.** M0's and M1's logs were archived a milestone late at 381 KB between them; M2's was the first done on time. Promote durable lessons out of the entries first — Traps.md, AR §18, the M4 ledger — then move them.
6. **When every box is ticked: the owner merges `dev` → `main` and tags `m3`. Claude does not tag.**
7. **The grain is in the tag message**, M1-21's and M2-15's shape: *Editor evidence, device rows deferred* — **and multi-touch named**, so `m3` is never read later as a hardware sign-off for a feature no hardware has run.
8. **A structural miss becomes an M4 task or a ledger row, not a late edit to this branch.** A *number* can move here (rule 1); a mechanism cannot. The one exception is a **bug**, which becomes `M3-15a` with its own PR and its own review.
9. **Two flagged design contradictions are resolved here or carried with a reason.** M3-13b rule 3 found **GD §16.2's *"shifts toward red"* against GD §16.4's *"nothing else, ever"***, and shipped §16.4's reading; M3-02a rule 7 flagged **CH §5's *"8 (7 + 1 Keystone)"* for 9**. Both are one-line doc edits the owner owns. **The older one is not:** the owner's retune puts the Oathbound at 3 m/s while GD §6.1's 5.4–6.2 band and CH §3's `140 / 5.4` row still carry the old numbers — that is a statement about two unbuilt classes and stays parked until **M5-02**, which is the first task that cannot avoid it.

## Acceptance

- [ ] Checklist ticked; feel verdict recorded, with the Editor-versus-device grain stated as M1-21's and M2-15's were
- [ ] **Rows 1, 8 and 9 each carry a number or a sentence, not a shrug** — hits at three depths, two labelled timings, and a two-second verdict on twelve real descriptions. These three are the reason this task is not a formality
- [ ] Every ledger row struck or carried, with the *As built* that closed it named
- [ ] `PROGRESS.md` updated, M3 entries archived, Current State pointing at **M4-00** (or M4-01, if M4's specs need no group of their own — decided here)
- [ ] `m3` exists on `main` — **the owner's to make**, with rule 7's message

## Out of scope

- **The first boss, phases, the Warden of Ash, death → Shard payout** — M4. M3 exists so this milestone stays about *getting stronger*: a tree tested against three archetypes is the honest test of whether the picks matter, and a boss would make an unkillable build look fine.
- **The boss health bar.** GD §16.2's segmented bar moved to **M4-01** at M3-00d, with the phases that drive it. Its absence is not a miss on this checklist.
- **The remaining fifteen nodes and the three keystones** — M7-04. Row 1's verdict is about twelve, and the checklist says so where it matters (*"Overflow rather than the tree"*).
- **The second class** — M5. One class with a tree is what M3 promised; `Data/Trees/` holding one asset is correct.
- **The Sanctum, Veilrot, Pacts, Ordeals, Reroll, Banish** — M6. `IProgressionCommands` has two members and AR §6 lists five; three arrive with their mechanics.
- **A second language, a locale setting, the localisation pipeline** — M6-10. M3-14a pulled one table forward and rule 2 of that spec is the line.
- **Settings: the enemy-health-bar toggle, HUD opacity, accessibility** — M8-03. GD §16.2 wants the toggle watched in playtests, and this checklist asks whether anyone wants one, which is all it can.
- **Fixing what the feel verdict finds**, unless it is a number (rule 8).

## As built

_Filled at merge._
