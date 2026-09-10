# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

**Closed milestones are archived, not deleted:** [M0](archive/PROGRESS-M0.md) · [M1](archive/PROGRESS-M1.md). The log below covers the milestone in progress only. This file used to be 381 KB and could not be opened whole by any tool; keeping it to one milestone is what stops that happening again.

---

## Current State

_Updated: 2026-09-10_

| | |
|---|---|
| **Milestone** | **M0 — Walking skeleton: complete, tagged `m0`. M1 — Combat feel: complete (21/21), acceptance passed on Editor evidence; `m1` is the owner's to tag.** Now **M2 — Stage loop**. |
| **Last merged task** | [M1-21](tasks/M1-21-acceptance-and-tag.md) — M1 acceptance: **no code, no tuning, and the fact that no tuning was needed is the result.** Every number in CC §7 already matched the asset shipping it, so doc and asset never disagreed. Ten checklist rows settled statically; three are device-only; **one row is deliberately unresolved rather than passed** — the out-of-range focus tap, still in the parking lot. |
| **In progress** | — |
| **Next task** | **[M2-00a](ROADMAP.md#m2--stage-loop)** — plan hygiene (this restructure), then **M2-00b…e** write the fifteen M2 specs, then **M2-01**. M2's ~75 % detailing trigger passed unfired during M1, so its tasks are still titles. The [carry-forward ledger](ROADMAP.md#carry-forward-into-m2) names what each spec must absorb. |
| **What works** | A run plays end to end from a cold start, and the fight is two-sided. Boot → Menu → tap Descend → a run with a player, eight Husks, a HUD and a death screen. **Core owns every outcome**; Unity reports facts and renders events. Concretely: `RunSession` is the only `IRunSession`; `PlayerMotor` + `PlayerCombat` decide movement, targeting, damage and the Charge; `EnemySystem` + `EnemyRegistry` + `ChaserBehaviour` own the enemies; `Weapon` swings on a clock and asks the body for a cone, which answers through `ReportConeHits`; `ViewPool` + `RespawnPolicy` keep twelve alive and pooled; `NavPathSense` routes them round the pillars; `HudPresenter`, `ReticleView` and `EnemyHitFeedback` render it; `HapticsListener` buzzes it. Composition is `BootScope` → `RunScope`/`MenuScope`, no statics anywhere. **Detail for any of it is in the [M0](archive/PROGRESS-M0.md) and [M1](archive/PROGRESS-M1.md) archives**, task by task. |
| **Verified** | **452 EditMode + 3 PlayMode green** (2026-09-10), six assemblies clean, zero errors, zero analyzer warnings, one expected Console warning (the direct-Play unseeded-run notice). Working tree clean, no `ProjectSettings/` drift. |
| **Reference device** | **None, and the emulator is not a fallback.** BlueStacks 5 (Android 9 / SDK 28, x86_64) **installs the APK and crashes on launch** in its own Vulkan driver via `libhoudini.so` — an emulator-bridge fault on a development-build Vulkan path, not game code. The APK is ARM64-only and always will be: Unity 6.3 cannot build x86-64 for Android. **Nothing outside the Editor has ever run.** [M0-20a](tasks/M0-20a-apk-runs-on-bluestacks.md) owns it; a phone removes the question. Unity Device Simulator still serves for layout. |
| **Deferred — device-only** | Multi-touch (stick + button); landscape flip on rotation; touch latency; real frame rate; thermal; kill-from-recents and relaunch; whether the M0/M1 **feel** verdicts survive leaving the Editor. **M1-20's haptics are the largest single block** — the mapping, the off switch, and above all whether the 100 ms coalescing window reads as *late*. Nothing in the Editor can answer any of it: the container resolves `NullVibrator` there by construction. The first session with a phone runs all of them. |
| **Known issues** | **None outstanding in the working tree.** The one real open issue is the APK crash, which has its own task. Disk sits near full — an IL2CPP Android build wants several GB of headroom. **Before every commit, check `git diff ProjectSettings/`**: Unity backfills defaults and re-serialises settings files behind you, and the Editor holds the rewritten copy in memory until restarted (see [Traps.md §5](../Traps.md)). Three sessions running it has not recurred. |
| **Where the rules live** | The watch list is gone, and that is deliberate — it had reached 49 KB on **one line** and was read in full every session. It was split by kind: **[Traps.md](../Traps.md)** for things in the toolchain that lie to you (the "an API that echoes your value back has not agreed to honour it" family, the shell probes, the unfocused Editor, the MCP, VContainer, test measurement), and **[Architecture.md §18](../Architecture.md#18-invariants)** for rules the code depends on — orderings, boundary rules, why a field is `internal`, and a **soft-spots table** naming the task each one bites at. Spent milestone incidents stayed in the archives. **Read Traps.md before debugging a probe; read §18 before changing an ordering.** |

---

## How to write an entry

Append one entry per merged task, newest at the bottom. Keep it factual; the spec has the intent, this has what happened.

```markdown
### YYYY-MM-DD · M0-03 · Domain events · PR #n
**Built:** one line — what exists now that didn't.
**Deviations from spec:** none | list them, with why.
**Learned:** anything that should change a later spec or the architecture. Link the follow-up if one was created.
**Follow-ups:** new tasks created, or "none".
```

After appending, update the Current State table above. If a deviation changes a decision in `Architecture.md`, it needs a superseding ADR — say so here and link it.

**A durable lesson does not go in Current State.** A toolchain trap goes in [Traps.md](../Traps.md); a rule the code now depends on goes in [Architecture.md §18](../Architecture.md#18-invariants); a thing a future task must handle goes in the [carry-forward ledger](ROADMAP.md#carry-forward-into-m2) or the ROADMAP parking lot. The entry says *what happened*, and links.

**At the end of a milestone**, move its entries to `archive/PROGRESS-M<n>.md`, promote anything durable out of them first, and leave this Log holding only the milestone in progress.

---

## Log

_M0 and M1 are archived: [M0](archive/PROGRESS-M0.md) (20 entries) · [M1](archive/PROGRESS-M1.md) (21 entries)._

### 2026-09-10 · M2-00a · Plan hygiene: archive, split the watch list, carry-forward ledger · PR #_n_

**Built:** the planning docs are readable again, and every durable lesson has a home that is not a table cell. **`PROGRESS.md` went from 381 KB to 6.5 KB** — it could not previously be opened whole by any tool, including the Read tool's 256 KB cap, so reading Current State meant shelling out to `awk` and `fold`. The M0 and M1 logs moved verbatim to `archive/PROGRESS-M0.md` (20 entries) and `archive/PROGRESS-M1.md` (21 entries), links rewritten one level up and each one verified to resolve; a checksum of the rejoined archives against `HEAD` differs from the original by exactly two blank lines and nothing else.

**The watch list is gone, and that is the point.** It had reached **49,395 bytes on a single line** — ~12 k tokens loaded every session inside a Current State that itself cost ~23 k — and it mixed three incompatible kinds of thing. Split by kind rather than by milestone, because a milestone cut would have archived live rules: **[Traps.md](../Traps.md)** takes the toolchain lies (the "an API that accepts a value and echoes it back has not agreed to honour it" family, now with all six instances in one table; the seven-deep shell-probe family; the unfocused Editor; the MCP; VContainer; allocation measurement), **[Architecture.md §18](../Architecture.md#18-invariants)** takes the rules the code depends on — orderings, boundary rules, why a field is `internal` — plus a **soft-spots table naming the task each one bites at**, and spent incidents stayed with their milestone in the archive. Current State is now ~1 k tokens, and its longest line is 948 characters rather than 49,394.

**Deviations from spec:** no spec — this was directed conversationally by the owner after the M0+M1 audit, and the ROADMAP row was written as part of it. **Eight files, which is past the five-file rule**; it is a documentation restructure with no code in it, and splitting it would have left the docs cross-referencing files that did not exist yet. Called out rather than hidden.

**Learned:** **a table cell is not a filing system.** The watch list grew one entry per task for 41 tasks without anyone deciding it should become the project's institutional memory, and the failure mode was silent — nothing warns you that a doc has become unreadable, it just gets more expensive every session until a tool refuses it. The fix that mattered was not deleting anything; it was noticing the file held three kinds of thing and only one of them was milestone-scoped. **The new rule is in "How to write an entry": a durable lesson goes to Traps.md or §18, a future obligation goes to the ledger, and the log entry says what happened and links.** At the end of a milestone, promote first, then archive.

**Follow-ups:** [carry-forward ledger](ROADMAP.md#carry-forward-into-m2) — eleven rows, each naming the M2 task that must absorb it, ranked by cost-of-later. Row 1 (random streams expose no state, so a resumed run restarts every stream at draw 0 — and once M3-04's offers ride that stream, killing the app is a free reroll) is the one with no prior home in any doc or ADR and the only free moment to fix it is M2-13's format v1. `CoreCombat.md` §8 renamed M0 → M1, which is what it always described. **M2-00b…e** write the fifteen specs.

_Next M2 entry below._
