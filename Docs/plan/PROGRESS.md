# Soulvail — Progress

**The log.** Where we are right now, and what each merged task actually did. Updated **in the same PR** as the implementation — never in a separate "update docs" commit.

The map is [ROADMAP.md](ROADMAP.md). The specs are in [tasks/](tasks/).

---

## Current State

_Updated: 2026-09-07_

| | |
|---|---|
| **Milestone** | M0 — Walking skeleton (in progress, 6/20) |
| **Last merged task** | [M0-06](tasks/M0-06-intents.md) — `PlayerMoveIntent`, `IIntentSink`, `IntentBuffer` |
| **In progress** | — |
| **Next task** | [M0-07](tasks/M0-07-player-motor.md) — `MovementSpec`, `PlayerMotor`: accel/decel, no inertia, facing |
| **What works** | Nothing runs yet, but every wire of ADR-0003's boundary now exists in shape: core can say what happened, be told where things are, be told what to do the body with, and have a run reproduced from a seed. `StateMachine<T>`, three ports — `IDomainEvents`, `IRandom`, `IIntentSink` — and the two payloads `WorldSnapshot`/`EnemySense` (in) and `PlayerMoveIntent` (out) exist in `Soulvail.Core`; `DomainEventHub`, `SeededRandom`, `IntentBuffer` and the `Num` vector conversions are the Unity side; `RecordingEvents` and `FixedRandom` are the fakes every later core test will assert through. Nothing fills a snapshot, draws a random number or writes an intent yet — the shapes exist so `RunSession` (M0-10) and `SnapshotBuilder`/`PlayerView` (M0-16) can use them. Four of the five assemblies emit code and the Test Runner lists 57 EditMode tests. `AllocationAssert` is the suite's one way to claim "allocates nothing". VContainer 1.19.0 installed; only `Soulvail.Editor` still has no scripts. |
| **Reference device** | **None yet.** BlueStacks 5 (Android 11, 1920 × 1080 @ 240 DPI, ADB on) for APKs; Unity Device Simulator for layout. |
| **Deferred — device-only** | Nothing yet. Each milestone's acceptance lists its **[device]** items here when tagged; the first session with a phone runs all of them. |
| **Known issues** | — |
| **Watch list** | **Every new asmdef needs a `csc.rsp` (`-langversion:10`) beside it or its first file-scoped namespace breaks the build** — all five have one; add it with any sixth. An asmdef with no `.cs` files produces no assembly. **`GC.GetAllocatedBytesForCurrentThread()` and `GC.CollectionCount(0)` are inert on Unity's Mono — never hand-roll an allocation probe from them; use `AllocationAssert`.** **A `Unity_RunCommand` script that calls `File.Delete`/`File.Move` (or `AssetDatabase.DeleteAsset`) is refused with *"User interactions are not supported for MCP tool calls"*** — those are `k_UnsafeMethods` in `RunCommandCodeAnalyzer`, which demand a confirmation the MCP cannot give. Overwrite with `File.WriteAllText` instead. `System.Net`, `System.Diagnostics`, `System.Runtime.InteropServices` and `System.Reflection` are unauthorized namespaces there (M0-03). **`SeededRandom`'s stream indices — Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4 — are part of what a seed means. Never reorder or renumber them; a new stream takes the next free index** (M0-04). **`WorldSnapshot.Clear()` does not zero `Enemies` — every reader must stop at `EnemyCount`, because anything past it is last frame's enemies.** **A `ref`-returning member needs `ref` on both sides of the assignment or the caller silently gets a copy; a test that forgets it passes while proving nothing** (M0-05). **`IntentBuffer.Clear()` lowers `HasPlayerMove` and leaves the stored intent alone — a view that reads `PlayerMove` without checking the flag applies last frame's velocity and slides. Every intent reader checks its flag first** (M0-06). Application id is the placeholder `com.soulvail.dev` — must change before the first store upload (M8-06). `Unity.AI.Navigation` is in the AR §12 reference list for `Soulvail.Game` but not in the M0-01 asmdef; M1-19 adds it when NavMesh lands. |

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

### 2026-09-07 · M0-04 · Random streams · PR #_n_

**Built:** The second port, and the thing that makes a seeded run mean something. `IRandom` + `IRandomStream` in `Soulvail.Core.Ports` expose five independent streams — Spawn, Offers, Affixes, Drops, Misc — so adding a mechanic that rolls dice in one concern cannot shift another's sequence. `SeededRandom` in `Soulvail.Game.Adapters` is the real generator: PCG32 per stream, each seeded from `(seed, streamIndex)` through SplitMix64, allocation-free on every draw. `FixedRandom` in `Soulvail.Tests.Core.Fakes` is the scripted counterpart every later core test will roll through. 14 new EditMode tests, 42 in the suite. Nothing consumes randomness yet — that is M0-10 onward.

**Deviations from spec:** five. The first two were put to the owner and approved before implementation.

1. **A fifth file, `Tests/Core/Fakes/FixedRandomTests.cs`.** The Files table names one test file, in `Soulvail.Tests.Game`, but two of the Tests table's rows test `FixedRandom`, which lives in `Soulvail.Tests.Core`. Third task running into the same gap (M0-02, M0-03), resolved the same way. Five files is still size M, so no split was triggered.
2. **`FixedRandom.SetStream(string name, …)` became five named setters** — `SetSpawn`, `SetOffers`, `SetAffixes`, `SetDrops`, `SetMisc`, each returning `this` so they chain. Compile-time checked, no new type anywhere, and shorter at the call site. The decisive point was timing: nothing in the project draws a random number yet, so the specced signature had zero callers and changing it cost nothing. After M2's director it would have been spread across every spawn and offer test.
3. **A test beyond the Tests table: `FixedRandom_SetStream_OverridesOnlyThatStream`.** The Public API's "every stream is the same scripted stream unless set individually" is a behaviour with no row, and it is the half of the fake that deviation 2 reshaped. It also pins the consequence worth knowing: the un-overridden streams are *one instance*, so a draw from `Offers` advances `Drops` too.
4. **`Draw_AllocatesNothing` is measured with `AllocationAssert`, and covers all four draws.** The row's "allocated-bytes delta == 0" describes the BCL probe M0-02 proved inert here — same stale wording M0-03 hit. Rule 8 says *no allocation per draw*, and `NextInt` is where a boxed comparer or a widening conversion would most easily creep in, so `NextFloat`, `NextInt`, `Range` and `Chance` are each measured.
5. **`FixedRandom.Seed` returns 0.** `IRandom` requires the property; the spec gives the fake no seed constructor. Nothing reads it.

**Learned:**

- **Per-stream seeds go through SplitMix64 rather than the raw seed, and that is what makes rule 3 true rather than merely hoped-for.** Seeding several PCG generators from adjacent values (seed+0, seed+1, …) correlates their early output — the streams would drift apart eventually but not on the first few draws, which is exactly where a wave composer looks. SplitMix64 is an avalanche mix, so one bit of difference scatters the whole 64-bit state. Both derived words are used: the first becomes the generator's state, the second its increment, which selects *which* of PCG's 2^63 sequences the stream walks. `DifferentSeed_DifferentSequence` deliberately tests seeds 1 and 2, the hard adjacent case, not 1 and 9999.
- **The stream indices are part of what a seed means.** Renumbering Spawn from 0 to 1 would silently change every sequence every recorded seed produces, invalidating every Daily and every bug repro. On the watch list now, because M2-04/M2-05 are the first tasks likely to want a sixth stream — it takes index 5, and nothing moves.
- **`Chance` needs no special-casing to satisfy its rule, and must not have any.** Because `NextFloat` is `[0, 1)`, the naive `NextFloat() < probability` already gives "0 never, 1 always" for free. The tempting optimisation — return early for a certain outcome without drawing — would skip a draw and desync every seeded run the moment a designer tuned a probability to 0 or 1. `Chance_ConsumesOneDraw` is the test that would catch it.
- **`NextInt` maps from the raw 32 bits, not through `NextFloat`.** Lemire's multiply-shift takes the high half of a 64-bit product: one multiply, no division, no branch, no rejection loop, and uniform to within one part in 2^32. Routing through `NextFloat` would have thrown away the range's resolution to a 24-bit mantissa and rounded twice. Rule 9's float mapping is specced for the *fake*, where a hand-written script is the point; it is not a description of the real generator.
- **`Range(a, b)` is documented as `[a, b]` but actually produces `[a, b)`** — `b` is unreachable, because `NextFloat` excludes 1. Not a defect (the specced interval contains the result set, and `Range_WithinBounds` passes) but any future spec that needs `b` genuinely reachable — an inclusive integer range, say — has to say so rather than assume it.
- The two interfaces share one file, which the Files table calls for explicitly against CLAUDE.md's one-class-per-file rule. Right call: `IRandomStream` has no meaning apart from `IRandom`, and reading them together is how the contract is understood.

**Follow-ups:** none.

### 2026-09-07 · M0-05 · World snapshot · PR #_n_

**Built:** The payload of the one inbound channel that runs every frame. `WorldSnapshot` and `EnemySense` in `Soulvail.Core.Run` are everything core is ever told about where things are — preallocated to a capacity, refilled in place through `Clear` then one `AddEnemy` per enemy, allocating nothing on that cycle. `Num` in `Soulvail.Game.Adapters` is the boundary conversion between `UnityEngine` and `System.Numerics` vectors, including the XZ flatten that turns a world vector into a ground-plane direction. 9 new EditMode tests, 51 in the suite. Nothing fills a snapshot yet — `SnapshotBuilder` (M0-16) does, and `RunSession` (M0-10) is the first thing with a reason to read one.

**Deviations from spec:** three, all small; the Files table was built exactly.

1. **`WorldSnapshot` is a class, where AR §4.2 sketched a struct — and `Architecture.md` is corrected here.** Not a deviation from the spec, whose Public API says `sealed class`; a deviation from the architecture document, which is what later sessions read first. The shape follows from `AddEnemy` returning `ref EnemySense` and advancing `EnemyCount`: passed by value, the builder would fill a copy the ticker never sees. AR §4.2's sketch predated the ref-returning fill. No superseding ADR — ADR-0003 decides that a snapshot is the tick's payload, not what storage kind carries it, so the decision stands and only its illustration was wrong. §4.2 now shows the built shape and states the two rules the sketch could not: the array outlives every frame, and every reader stops at `EnemyCount`. §12's adapters list gained `Num`. Outside the Files table, and done at the owner's explicit direction rather than deferred.
2. **Two specced test rows carry an extra assertion each.** `Ctor_ZeroCapacity_Throws` also asserts a negative capacity, because rule 6 is `<= 0` while the row only names 0, and `new EnemySense[-1]` would otherwise be the thing that reports it. `Clear_ResetsScalarsAndCount_KeepsArrayInstance` also asserts that a stale slot survives, because rule 3's second sentence — `Clear` does **not** zero the array contents — has no row of its own and is the half of `Clear` a future maintainer is most likely to "fix". Both are assertions inside rows that already own the behaviour, not new rows, so the Tests table did not grow.
3. **The allocation row's "allocated-bytes delta == 0" is measured with `AllocationAssert`.** Fourth task running into the same stale wording (M0-02 proved the BCL probe inert on this runtime). Rule 4 is what the test enforces.

**Learned:**

- **`System.Numerics` resolves inside `Soulvail.Core` with no asmdef change and no added reference.** It ships in the .NET Standard profile the project targets (`apiCompatibilityLevel: 6`), and `AssemblyPurityTests` stays green because it is not a `UnityEngine*` assembly. First time core has depended on anything beyond `System` and `System.Collections.Generic`, so it was worth confirming rather than assuming.
- **A `ref`-returning `AddEnemy` makes the obvious test vacuous.** `var e = snapshot.AddEnemy(); e.Id = 7;` compiles, binds a copy, and every assertion *about `e`* passes while the snapshot stays empty — a green test proving nothing. `ref` is required on both sides, and the assertions go through `snapshot.Enemies[0]` rather than through the local, so a future copy fails them. M0-06's intent buffer is the next preallocated buffer that will hand out references; same care applies there.
- **`Clear()` is O(1) because it deliberately leaves `Enemies` alone**, which is what makes refilling free, and which means stale enemies sit past `EnemyCount` indefinitely. Correct as specced and a live trap: any reader that iterates `Enemies` instead of `Enemies[0..EnemyCount)` sees last frame's dead. On the watch list. `EnemyCapacity` is computed from `Enemies.Length` rather than stored, so capacity and array length cannot drift apart.
- **The public mutable fields on both types are a deliberate exception to the project's no-public-fields rule.** That rule exists to stop Unity components leaking their innards; this is a transfer buffer with one writer and one reader, where properties would cost a call per field per enemy per frame and hide nothing. No analyzer objects — `.editorconfig` states the ban as a comment and enforces only `IDE0044`, which is private-field-only — so convention and code disagree only on paper. Documented in the type's remarks so the next reader does not "fix" it.
- **`Num` fully qualifies every vector rather than importing either namespace**, because both declare `Vector2` and `Vector3`: with a `using`, a conversion that silently returned its input would read exactly like a correct one. The tests do the same, for the same reason.

**Follow-ups:** none. The AR §4.2 correction that would have been one is in this change.

### 2026-09-07 · M0-06 · Intents · PR #_n_

**Built:** The other outbound channel, and the last piece of the tick's plumbing. `PlayerMoveIntent` in `Soulvail.Core.Run` is what core wants done with the player this frame — a velocity and a facing, immutable, carried by `in`; `IIntentSink` in `Soulvail.Core.Ports` is the port it goes out through, one named method per intent kind rather than a generic push; `IntentBuffer` in `Soulvail.Game.Adapters` is the per-frame mailbox core fills during `Tick` and the views empty straight after, allocation-free on that cycle. 6 new EditMode tests, 57 in the suite. Nothing writes an intent yet — `RunSession` (M0-10) is the first producer and `PlayerView` (M0-16) the first reader; between them they now have both halves of the boundary they need.

**Deviations from spec:** two, both put to the owner and approved before implementation.

1. **A fifth file, `Tests/Core/Run/PlayerMoveIntentTests.cs`.** The Files table names one test file, in `Soulvail.Tests.Game`, but behaviour rule 5 is about `PlayerMoveIntent`, a Core type, and has no row in the Tests table at all. Fourth task running into the same gap (M0-02, M0-03, M0-04), resolved the same way — filing a Core type's contract under a fixture named for the Game-side buffer would have been the dishonest naming M0-03 rejected. Five files is still size M, so no split was triggered. Only the testable half of rule 5 is covered: immutability is enforced by `readonly struct`, where a mutation is a compile error and a test would restate the language; "does not normalise or validate" has no enforcement and gets the test.
2. **No debug assertion that `Facing` is unit-length**, which rule 5 permits. Cost was not the deciding factor — `System.Diagnostics.Debug.Assert` is `[Conditional("DEBUG")]` and Unity defines `DEBUG` only for the Editor and Development Builds, so the call is not emitted at all in a release player. The objection is that nothing constructs a `PlayerMoveIntent` yet, so the assertion would guard no producer while silently deciding a question this task does not own: whether a zero `Facing` is legal at spawn or while standing still. M0-10/M0-16 answer that with a producer in hand, and the guard belongs there.

**Learned:**

- **The `PlayerMove` property and the `IIntentSink.PlayerMove` method coexist because an explicit implementation's name is `IIntentSink.PlayerMove`** — it never enters the class's declaration space, so CS0102 cannot fire. Verified by compiling, not assumed. The collision is worth keeping rather than working around: it makes the type enforce the direction of the boundary. Core holds an `IIntentSink` and can only write; a view holds an `IntentBuffer` and can only read. Neither can do the other's job by accident, and no naming convention had to be invented to say so.
- **`Clear()` lowers the flag and deliberately leaves the stored intent alone**, the same bargain `WorldSnapshot.Clear` makes with its enemy array — and the spec's "undefined if `!HasPlayerMove`" is what licenses it. `Clear_ResetsFlag` therefore asserts the flag and says nothing either way about the leftover: pinning it would turn an implementation detail into a promise and forbid a future zeroing the spec explicitly allows. A view that skips the flag check applies last frame's velocity and slides; that is the cost of the cheap clear, and it is on the watch list.
- **The Tests table's allocation row is measured with `AllocationAssert`.** Fifth task running into the same stale "allocated-bytes delta == 0" wording. Worth fixing in the remaining M0 specs rather than deviating from it once per task.
- The `readonly struct` + `in` pairing is the same one `IDomainEvents` uses, for the same reason, and it is now the project's default shape for anything crossing the boundary 60 times a second. Neither the interface call nor the by-value property read allocates; `WriteAndClear_AllocateNothing` measures the whole write-then-clear cycle rather than either half.
- The Public API's by-value `PlayerMove` property means a local copy is a faithful reading today, so M0-05's ref-returning trap does not literally bite here. The tests still assert through the buffer, because the assertion that names the buffer is the one that keeps testing the buffer if the accessor ever changes shape.

**Follow-ups:** none.
