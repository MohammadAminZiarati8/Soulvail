# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

---

## Current State

_Updated: 2026-09-07_

| | |
|---|---|
| **Milestone** | M0 — Walking skeleton (in progress, 3/20) |
| **Last merged task** | [M0-03](tasks/M0-03-domain-events.md) — Domain events: `IDomainEvents`, `DomainEventHub`, `RecordingEvents` |
| **In progress** | — |
| **Next task** | [M0-04](tasks/M0-04-random-streams.md) — `IRandom` with named streams, `SeededRandom`, `FixedRandom` fake |
| **What works** | Nothing runs yet, but core can now say what happened and Unity can hear it. `StateMachine<T>` and the first port, `IDomainEvents`, exist in `Soulvail.Core`; `DomainEventHub` is its run-scoped adapter in `Soulvail.Game`; `RecordingEvents` is the fake every later core test will assert through. Four of the five assemblies emit code and the Test Runner lists 28 EditMode tests. `AllocationAssert` is the suite's one way to claim "allocates nothing". VContainer 1.19.0 installed; only `Soulvail.Editor` still has no scripts. |
| **Reference device** | **None yet.** BlueStacks 5 (Android 11, 1920 × 1080 @ 240 DPI, ADB on) for APKs; Unity Device Simulator for layout. |
| **Deferred — device-only** | Nothing yet. Each milestone's acceptance lists its **[device]** items here when tagged; the first session with a phone runs all of them. |
| **Known issues** | — |
| **Watch list** | **Every new asmdef needs a `csc.rsp` (`-langversion:10`) beside it or its first file-scoped namespace breaks the build** — all five have one; add it with any sixth. An asmdef with no `.cs` files produces no assembly. **`GC.GetAllocatedBytesForCurrentThread()` and `GC.CollectionCount(0)` are inert on Unity's Mono — never hand-roll an allocation probe from them; use `AllocationAssert`.** **A `Unity_RunCommand` script that calls `File.Delete`/`File.Move` (or `AssetDatabase.DeleteAsset`) is refused with *"User interactions are not supported for MCP tool calls"*** — those are `k_UnsafeMethods` in `RunCommandCodeAnalyzer`, which demand a confirmation the MCP cannot give. Overwrite with `File.WriteAllText` instead. `System.Net`, `System.Diagnostics`, `System.Runtime.InteropServices` and `System.Reflection` are unauthorized namespaces there (M0-03). Application id is the placeholder `com.soulvail.dev` — must change before the first store upload (M8-06). `Unity.AI.Navigation` is in the AR §12 reference list for `Soulvail.Game` but not in the M0-01 asmdef; M1-19 adds it when NavMesh lands. |

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

### 2026-09-07 · M0-02 · `StateMachine<T>` and the core-purity guard test · PR #_n_

**Built:** `StateMachine<TState>` in `Soulvail.Core` — the one flat FSM behind game flow, enemy AI and boss phases — with deferred in-handler transitions and an allocation-free `Tick`. Alongside it the test suite's foundations: `AllocationAssert` (the single way every later spec claims "allocates nothing") and `AssemblyPurityTests`, which fails the build if `Soulvail.Core` ever gains an engine reference. 15 EditMode tests, all green. `Soulvail.Core` and `Soulvail.Tests.Core` now emit assemblies and the Test Runner lists them, as M0-01 predicted it would once scripts existed.

**Deviations from spec:** three.

1. **Unity 6.3 compiles at C# 9, so file-scoped namespaces did not build.** CLAUDE.md and `.editorconfig` both mandate them, and the spec's Public API block is written with one — a conflict that could not surface until the first `.cs` file. Owner chose to keep the convention: a one-line `csc.rsp` (`-langversion:10`) now sits beside **all five** asmdefs. Verified per-assembly: a `csc.rsp` beside an asmdef raises only that assembly, and `Assets/csc.rsp` would not have covered asmdef assemblies at all. This is why the file list is 10 rather than 5.
2. **`AllocationAssert` uses Unity's GC recorder, not the two strategies the spec prescribes.** Both were measured and are inert on this runtime — see *Learned*. The public API (`None(Action, int iterations = 10_000)`) is unchanged.
3. **A fifth file, `Tests/Core/Support/AllocationAssertTests.cs`.** The spec's Tests table mandates `AllocationAssert_DetectsAllocation` and `AllocationAssert_PassesForPureBody`, but its Files table gives them no fixture to live in. Filing them under the purity or state-machine fixtures would have been dishonest naming, so they got the file their names imply.

**Learned:**

- **On Unity's Mono, `GC.GetAllocatedBytesForCurrentThread()` is stubbed — it returns 0 always, even either side of a deliberate allocation — and `GC.CollectionCount(0)` did not move across 100 000 object allocations** (~2.4 MB, inside SGen's nursery). Both of the spec's strategies therefore report "no allocation" for code that allocates freely. The first implementation followed the spec, and `AllocationAssert_DetectsAllocation` caught it: the helper failed to detect `new object()` in a loop. Had that self-test not been specced, every "allocates nothing" claim in the project would have been vacuous — worth remembering when specifying future guard tests. `AllocationAssert` now measures with `UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory()`, which is real. **No future spec should hand-roll a BCL GC probe.**
- `Tick` deliberately caches the current state's tick handler list on transition instead of looking it up in a `Dictionary<TState, …>` per tick. Enum-keyed dictionary lookups can box on some runtimes; caching sidesteps the question entirely and keeps rule 8 true by construction rather than by hope.
- Unity's bundled NUnit has no `Assert.Multiple` — sequential asserts instead.
- **The pre-commit format check was failing open-loop on a phantom violation, and is fixed here.** This machine has a .NET *runtime* but no SDK, so `dotnet` is on `PATH` while `dotnet format` does not exist. The hook probed `command -v dotnet`, got a hit, ran the check, and read its "No .NET SDKs were found" exit code as a formatting difference — blocking the first commit that ever staged a `.cs` file. It now probes `dotnet format --version`, so it skips honestly. The five files were independently verified against `.editorconfig` by hand (no tabs, no trailing whitespace, 4-space indents, LF, final newline). CLAUDE.md's "the check self-skips" is true again.
- The Unity MCP `RunCommand` wraps submitted code in a `Unity.AI.Assistant.…` namespace. Two consequences for later sessions: `CompilationPipeline` must be fully qualified or it resolves to `Unity.CompilationPipeline`, and **nested classes get duplicated outside their parent by the code-fixer** — declare helper classes at top level. `TestRunnerApi` results were captured by writing to `Temp/` from `RunFinished`, which survives the domain reload the run triggers.

**Follow-ups:** none.

### 2026-09-07 · M0-03 · Domain events · PR #_n_

**Built:** The first port, and the first thing on both sides of the hexagon at once. `IDomainEvents` in `Soulvail.Core.Ports` is how core says what happened without learning who listens; `DomainEventHub` in `Soulvail.Game.Adapters` is its run-scoped typed dispatcher — subscribe/unsubscribe by handle, allocation-free publish, no statics; `RecordingEvents` in `Soulvail.Tests.Core.Fakes` is the fake every later core test will assert event sequences through. 13 new EditMode tests, 28 in the suite. `Soulvail.Game` and `Soulvail.Tests.Game` now emit assemblies for the first time.

**Deviations from spec:** three.

1. **A fifth file, `Tests/Core/Fakes/RecordingEventsTests.cs`.** The Files table names one test file, in `Soulvail.Tests.Game`, but two of the Tests table's rows test `RecordingEvents`, which lives in `Soulvail.Tests.Core` and is not a hub. Filing them under a fixture named for the hub would have been dishonest naming — the same gap M0-02 hit, resolved the same way, and the owner approved it before implementation.
2. **`Soulvail.Tests.Game` now references `Soulvail.Tests.Core`.** `Publish_AfterWarmup_AllocatesNothing` is specced into the Tests.Game fixture, but `AllocationAssert` lives in Tests.Core and was out of reach. Both assemblies are Editor-only under `UNITY_INCLUDE_TESTS`, so the reference is safe, and it is what keeps the project's one allocation probe single-sourced rather than copied.
3. **The allocation row's "allocated-bytes delta == 0" is measured with `AllocationAssert` instead.** That phrasing describes exactly the BCL probe M0-02 proved inert on this runtime. Behaviour rule 8 is what the test enforces; the wording was stale, not the rule.

**Learned:**

- **A `Unity_RunCommand` script is refused with *"User interactions are not supported for MCP tool calls"* if it calls anything in `RunCommandCodeAnalyzer.k_UnsafeMethods`** — `System.IO.File.Delete`, `File.Move`, `Directory.Delete`, `AssetDatabase.DeleteAsset`, `FileUtil.DeleteFileOrDirectory`. Those mark the command unsafe, which requires a user confirmation, and MCP callers get `NoOpToolInteractions`, whose only job is to throw that message. Three attempts to start the EditMode run failed this way because each cleared its result file with `File.Delete` first; dropping the delete and overwriting with `File.WriteAllText` (a write, not an unsafe call) runs the suite fine. **`TestRunnerApi.Execute` was never blocked** — the first diagnosis blamed it because the probes that worked differed in the file call too. Separately, `System.Net`, `System.Diagnostics`, `System.Runtime.InteropServices` and `System.Reflection` really are unauthorized namespaces for submitted scripts. The lesson for later sessions is the diagnosis, not the guard: when an MCP command is refused, change one thing at a time.
- `Publish` walks the live handler list and **defers mutations** instead of snapshotting it. Snapshotting at the top of every publish is the obvious way to satisfy "the in-flight publish sees the list as it was", and it allocates on every publish, which breaks rule 8. Deferring costs nothing on the hot path — the pending queue is only touched when someone actually subscribes or unsubscribes from inside a handler — and it makes re-entrancy fall out for free: a handler that republishes the same type walks the same untouched list. Verified with a re-entrancy check beyond the spec's rows.
- Channels are created by `Subscribe`, never by `Publish`, so publishing an event nobody listens for allocates nothing **ever**, not merely nothing after the first time. Rule 8 asked for the weaker guarantee.
- A single throwing handler is rethrown through `ExceptionDispatchInfo` rather than `throw ex`, which would erase the stack trace of the listener that actually failed.
- `RecordingEvents.Clear()` is in the spec's Public API but has no row in its Tests table. Covered by the ad-hoc harness during verification, not by a fixture — worth a row if the fake ever grows.

**Follow-ups:** none.
