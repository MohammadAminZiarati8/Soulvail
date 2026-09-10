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

- [ ] **[play]** A run goes Boot → Menu → Descend → stage 1 → door → stage 2 → … without a seam that reads as a load
- [ ] **[play]** Arrival seals and announces; nothing spawns during it. Clear drops the barrier and opens the door. The gate never times out
- [ ] **[play]** Walking into the door is the only way onward, and the next room is never visible before you are in it
- [ ] Descent is an asset, not an assumption — `Data/Modes/Descent.asset` carries the roster and the schedule, and no code says "endless" or "stage 1" (GD §4.5)
- [ ] **[play]** Waves overlap at 25 % remaining; the "chase the last enemy" gap is gone (GD §7.3)
- [ ] **[play]** All three archetypes are tellable apart at a glance, and each is doing its own job — Husk closes, Spitter kites and will not shoot through a pillar, Bloater makes you walk away (GD §8.1)
- [ ] Introduction schedule holds: Husk from 1, Spitter from 2, Bloater from 4, one new thing at a time (GD §8.2)
- [ ] **[play]** Every spawn telegraphs 0.8 s before it exists, and nothing arrives within 6 m of you or on top of another spawn
- [ ] **[play]** A threat you cannot see has an edge arrow; a focus you tapped out of range reads as *held*, not as a miss — the CC §8 row M1 could not tick

**Difficulty, at depth rather than at stage 1** (GD §12)

- [ ] Threat budget matches GD §12.1's table at stages 1, 10, 20, 30 and 40 — **check the arithmetic against the formula, not against the table**, which was wrong by 105 at stage 40 when M2-00b checked it (parking lot)
- [ ] One-shot rule: no single hit exceeds 35 % of max HP, at stage 1 **and** at stage 30 (GD §12.4)
- [ ] TTK invariant: a basic enemy still dies in 3–5 hits at stage 30 with an unlevelled class — or the miss is recorded as the input M3's tree must close (GD §6.2, §12.3)
- [ ] Concurrency never exceeds the cap, and the cap is the one M2-04 priced against the pathfinder rather than the one GD §12.2 wants
- [ ] **[play]** Stage length lands in GD §7.3's 40–75 s band at stages 1, 5 and 10 — timed, not estimated

**Persistence** (GD §7.3, ADR-0007)

- [ ] `run.json` exists from the run's first frame and says the stage the player is about to play
- [ ] It is rewritten at every boundary and deleted on death
- [ ] **[play]** `Continue` appears only when there is a run, and resuming lands in the same arena with the same waves and the health the player had
- [ ] A corrupt or unknown-version save does not stop the app launching — it is deleted and the button is simply absent
- [ ] The v1 fixtures still decode, and the migration chain test is green (AR §11.6)
- [ ] Haptics persist through `ISaveStore`, not `PlayerPrefs`, and no `PrefsKey` remains
- [ ] **[device]** Kill from recents mid-run and relaunch: `Continue` restores the stage
- [ ] **[device]** An incoming call mid-stage — GD §7.3's actual scenario, and the reason the whole feature exists

**Performance and hygiene** (GD §11.1, AR §14)

- [ ] **[play]** No hitch at a stage boundary, where an arena is instantiated behind a 0.3 s fade
- [ ] `AllocationAssert` rows green across every `Tick` path — director, flow, projectiles, recorder
- [ ] **[device]** Sustained frame rate at the concurrency cap with three archetypes and projectiles in the air
- [ ] **[device]** Profiler shows no per-frame `GC.Alloc` in a 60 s session — **carried unmet from M1-21**, where the unfocused Editor made it unrunnable (Traps §3)
- [ ] Six assemblies, zero errors, zero analyzer warnings, the full suite green through `TestRunnerApi`
- [ ] `git diff ProjectSettings/` shows the `Cover` layer (M2-11a rule 9) and **nothing else**

**The feel question** (record the answer and *why*): descending for ten minutes with no tree and no boss — is the stage loop worth repeating? If not, which number is wrong: the budget curve, the wave count, the concurrency cap, the arrival and clear beats, the telegraph, or the archetype mix?

## Behaviour

1. Tuning edits go to the ScriptableObjects and the named constants only; GD §7.1, §8.1 and §12's numbers are updated in the same PR. Doc and asset never disagree.
2. **The ledger is closed row by row, and a row is only closed by its owner's *As built*.** Fourteen rows, each naming a task; for each, either the task's footer says it is answered — in which case the row is struck — or it is carried into M3's table with the task that will answer it. A row silently dropped is the failure mode the ledger was built to prevent (M2-00a).
3. **The M3 ledger is opened in the same pass**, from three sources: rows carried forward, the parking lot's entries that now have an owner, and whatever this checklist's misses produce. M3-00a will write M3's specs against it, which is the rule M2-00a's *Follow-ups* established.
4. The PROGRESS entry lists what M3 must know: anything about the director, the flow, the snapshot format or the streams that turned out different from the specs — especially **any field M3's tree will have to add to `RunSnapshot`**, since that is the first real migration and M2-13b's chain test is what will catch it being skipped.
5. **The milestone is archived here, on time.** M0's and M1's logs were archived by M2-00a, one milestone late and at 381 KB; PROGRESS's *"at the end of a milestone"* rule is followed at the end of this one. Promote durable lessons out of the entries first — Traps.md, AR §18, the M3 ledger — then move them.
6. When every box is ticked: the owner merges `dev` → `main` and tags `m2`. **Claude does not tag.**

## Acceptance

- [ ] Checklist ticked; feel verdict recorded, with the Editor-versus-device grain stated as M1-21's was
- [ ] Every ledger row struck or carried, with the *As built* that closed it named
- [ ] `PROGRESS.md` updated, M2 entries archived, Current State pointing at **M3-00a**
- [ ] `m2` exists on `main` — **the owner's to make.** The tag message says *Editor evidence, device rows deferred*, so it is not read later as a hardware sign-off

## Out of scope

- **Levelling, XP, the tree, offers** — M3. M2 exists so that this milestone stays about the *loop*: an unlevelled class descending is the honest test of whether the stage structure carries itself.
- **The first boss** — M4. Every fifth stage being a boss (GD §7.1) is not part of this checklist and no stage 5 special-case is added to make it look closer.
- **The Sanctum** — M6. GD §7.1 puts it between Clear and Gate; M2-10 rule 7 left `Clear` a state rather than an instant so it can land there without a rewrite.
- **Fixing what the feel verdict finds**, unless it is a number. A structural miss becomes an M3 task or a ledger row, not a late edit to this branch.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._
