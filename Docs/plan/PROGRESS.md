# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

---

## Current State

_Updated: 2026-09-07_

| | |
|---|---|
| **Milestone** | M0 — Walking skeleton (in progress, 1/20) |
| **Last merged task** | [M0-01](tasks/M0-01-project-skeleton.md) — Project skeleton |
| **In progress** | — |
| **Next task** | [M0-02](tasks/M0-02-state-machine.md) — `StateMachine<T>` + core-purity guard test |
| **What works** | Nothing runs yet. Repo has design docs, architecture + ADRs, git hooks, Unity analyzer, M0 + M1 specs. The five assemblies exist with their dependency arrows and VContainer 1.19.0 is installed — no code in them yet. |
| **Reference device** | **None yet.** BlueStacks 5 (Android 11, 1920 × 1080 @ 240 DPI, ADB on) for APKs; Unity Device Simulator for layout. |
| **Deferred — device-only** | Nothing yet. Each milestone's acceptance lists its **[device]** items here when tagged; the first session with a phone runs all of them. |
| **Known issues** | — |
| **Watch list** | An asmdef with no `.cs` files produces no assembly — a spec that asserts "the Test Runner lists assembly X" only holds once that assembly has a script in it. Application id is the placeholder `com.soulvail.dev` — must change before the first store upload (M8-06). `Unity.AI.Navigation` is in the AR §12 reference list for `Soulvail.Game` but not in the M0-01 asmdef; M1-19 adds it when NavMesh lands. |

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

---

## Log

### 2026-09-07 · M0-01 · Project skeleton · PR #5

**Built:** The five assembly definitions exist under `Assets/_Project/` with the dependency arrows `Game → Core`, `Editor → Game, Core`, `Tests.Core → Core`, `Tests.Game → Game, Core`, and VContainer 1.19.0 resolves from its git URL. No `.cs` files yet — every later task now has a place to put them.

**Deviations from spec:** two, both in the spec rather than the build.

1. Manual verification step 3 ("Test Runner lists `Soulvail.Tests.Core` and `Soulvail.Tests.Game` with zero tests") cannot pass as written. Unity emits no assembly for an asmdef containing no scripts, so the Test Runner lists neither. Both asmdefs import cleanly and are correctly formed; they appear the moment M0-02 adds the first test file. Not a defect — the check was unsatisfiable at this task's scope.
2. `Soulvail.Game` does not reference `Unity.AI.Navigation`, which AR §12 lists for that assembly. Followed the spec's Files table, which is authoritative for scope. M1-19 owns NavMesh and adds the reference there.

**Learned:**

- Because there are no scripts, nothing in the compile validates the asmdef reference *names* — a typo would sit undetected until the first task that writes code against it. Verified all six by name against `CompilationPipeline.GetAssemblies` instead: `VContainer`, `Unity.InputSystem`, `UnityEngine.UI`, `Unity.TextMeshPro`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` all resolve. TMP ships inside `com.unity.ugui` 2.0.0 in Unity 6 — no separate package entry is needed.
- The first import of a git-URL package logs a one-time `"assets located in immutable packages were unexpectedly altered"` warning listing every file in the package. It does not recur on later refreshes and is not actionable.
- VContainer 1.19.0 was re-verified as the latest tag at implementation time; the pin was already correct and the watch-list item is retired.

**Follow-ups:** none.
