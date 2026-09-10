# M1-21 — M1 acceptance: CC §8 on device, tuning, tag `m1`

**Size:** S · **Depends on:** everything in M1 · **Branch:** `m1-21-acceptance`
**Design refs:** CC §8 (the full checklist), CC §1, GD §12.4 (guardrails that now apply)

## Goal

Answer the M1 question on evidence — *is moving and swinging, with nothing else around it, fun for two minutes straight?* — record the numbers, and tag `m1`.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, the feel verdict, tuned numbers, learnings for M2 |
| `Docs/plan/ROADMAP.md` | M1 ticked; M2 specs written and linked (M2 detailing is triggered at ~75 % of M1, so it should already exist) |
| `Docs/CoreCombat.md` §7 | Updated **only if** tuning changed a shipped number — doc and asset never disagree |
| `Assets/_Project/Data/…` | Only if tuning changed |

No code. A bug found here becomes `M1-21a…` with its own PR.

## Checklist (owner) — [CC §8](../../CoreCombat.md) in full

Items marked **[device]** cannot be verified on an emulator (one finger, no haptics, meaningless frame rate). They are deferred and tracked in PROGRESS until a phone exists — and `m1`'s *feel verdict* is explicitly provisional until they've been run on hardware.

Ticked at two grains, and the entry says which is which: **[play]** rows are covered by the owner's single playtest verdict rather than attested one at a time; unmarked rows were verified statically against the assets and the code.

- [x] **[play]** Stick spawns under the thumb anywhere in the left 45 %
- [x] **[play]** Dynamic recentering — reversal is immediate
- [ ] **[device]** Movement and buttons work simultaneously (stick + Charge)
- [x] **[play]** Character strafes: faces the target while moving independently
- [x] **[play]** Auto-target picks the sensible enemy and **doesn't jitter** between two of them
- [x] **[play]** Reticle always visible under the current target
- [x] **[play]** Tap-to-focus locks on, and the lock is obvious; tapping ground releases
- [x] Attacking never slows movement — structural: `Weapon` holds no reference to the motor or to velocity
- [x] **[play]** Charge feels reliable — i-frames land, buffered taps register, wall stops are clean
- [x] **[play]** Focus: standing still visibly speeds swings; any input cancels instantly
- [x] A Husk dies in exactly 3 swings (36 HP ÷ 13 → 13/26/39); a Charge finishes a 15-HP one (20)
- [x] **[play]** Chasers telegraph before striking; stepping away during the windup avoids damage
- [x] Shield absorbs first (30 / 4 s / 15 per s, EditMode-covered); **[play]** HP ghost trail reads correctly
- [x] **[play]** Dying returns to Menu; Descend starts a fresh run cleanly
- [x] Respawns never within 6 m (`RespawnPolicy.IsSafe`, squared XZ, refuses rather than compromises); enemies path around pillars (`NavMeshGreyBox.asset` + `NavPathSense`)
- [ ] **[device]** Haptics: hit / hurt / charge distinct; off switch works
- [ ] **[device]** Sustained 60 fps with 12 chasers (overlay)
- [ ] Profiler shows no per-frame GC.Alloc in a 60 s session — **not run**; the Editor was unfocused throughout, which freezes play mode after ~40 frames. Core's `Tick` paths are covered by `AllocationAssert` rows in the suite; the 60 s session is not, and it joins the first hands-on list
- [ ] **Out-of-range focus tap** (parking lot, M1-09) — **no verdict.** Re-felt as the note asked once M1-18's chasers existed, but the playtest answered on the whole rather than on this row. Stays open, unchanged

Guardrails now in force (GD §12.4) — verify, don't assume:
- [x] One-shot rule: no single hit exceeds 35 % of max HP — Husk strike 8 / 140 = **5.7 %**, ceiling would allow 49
- [x] TTK invariant: basic enemy dies in 3–5 hits — **3**, the fast edge of the band
- [x] Spawn safety: 6 m — `Run.unity` authors `_minSpawnDistance: 6`; nothing spawns when no position is safe

**The feel question** (record the answer and *why*): playing for two minutes with no goal — kiting, planting, charging through the pack — is it fun? If not, which number is wrong: speed, swing rate, cone width, charge distance/cooldown, chaser speed, windup, or the stick band?

## Behaviour

1. Tuning edits go to the ScriptableObjects (`Oathbound.asset`, `Husk.asset`) and `StickShaper` constants only; CC §7 / GD §8.1 numbers are updated in the same PR.
2. When every box is ticked: owner merges `dev` → `main` and tags `m1`. Claude does not tag.
3. The PROGRESS entry lists what M2 must know: anything about intents, facts, the enemy system, or the snapshot that turned out different from the specs — especially the cost of `AlliesNearby` (O(n²)) and path refresh at 12 enemies, as inputs to the M2 director's concurrency planning.

## Acceptance

- [x] Checklist ticked; feel verdict recorded — **"good for now"**, owner, Editor. Two rows deliberately not ticked and named above: the 60 s GC.Alloc session and the out-of-range focus tap
- [x] `PROGRESS.md` updated; Current State points at **M2-00** (the spec-writing task M2-01 now waits on)
- [ ] `m1` tag exists on `main` — **the owner's to make.** Tag message should say *Editor evidence, device rows deferred*, so it is not read later as a hardware sign-off

## Out of scope

- Any stage, wave, or progression logic — M2 and M3 exist so that this milestone stays about *feel*.

## As built

**No code, and no tuning — the absence of tuning being the result.** Behaviour rule 1 anticipated edits to `Oathbound.asset`, `Husk.asset` and `StickShaper`; none were needed, because every number in CC §7 already matched the asset shipping it, field for field across all five blocks. So `Docs/CoreCombat.md` §7 and `Assets/_Project/Data/…` — rows 3 and 4 of the Files table, both conditional — were correctly left untouched, and the doc-and-asset rule held with no intervention.

**Files actually changed: two of the four.** `Docs/plan/PROGRESS.md` (entry + Current State) and `Docs/plan/ROADMAP.md` (M1-21 ticked, M1 status paragraph, M2-00 added, M2 count 15 → 16).

**Deviation — the Files table's second row is not honoured here.** It expects the M2 specs to exist already, on the ~75 % detailing trigger; the trigger never fired, and writing fifteen specs into this PR would have made it nineteen files, which the 5-file rule says to split before continuing rather than after. They became **M2-00**, ahead of M2-01, and the ROADMAP now points there. Behaviour rule 3's "what M2 must know" is in the PROGRESS entry as four measured findings, two of them ceilings that nothing reports at runtime: `AlliesNearby` costs `n² − n` comparisons a frame over the *registered* count, and path refresh saturates its 4-per-frame budget against a 10 Hz cadence at **24 concurrent enemies**, above which routes go stale silently.

**Verification:** 452 EditMode + 3 PlayMode green, six assemblies compiled, zero errors, zero analyzer warnings, one expected Console warning (the direct-Play unseeded-run notice). Working tree clean — no `ProjectSettings/` re-serialisation, which is the third session running that the M1-17 trap has not recurred.

**Behaviour rule 2 is outstanding by design:** the owner merges `dev` → `main` and tags `m1`. Claude does not tag.
