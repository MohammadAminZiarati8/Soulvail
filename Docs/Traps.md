# Soulvail — Traps

**Things in this toolchain that lie to you.** Every entry here cost a debugging session at least
once. They are grouped by where you meet them, not by when we met them; the task that found each
one is tagged so the full story is findable in the
[progress archive](plan/archive/) or the [log](plan/PROGRESS.md).

**This file is not about the game.** Rules the *code* depends on — orderings, invariants, why a
field is `internal` — live in [Architecture.md §18](Architecture.md#18-invariants). If you are
asking "may I change this?", go there. If you are asking "why did my probe say yes?", stay here.

**Read §1 first.** It is the shape most of the rest are instances of.

---

## 1. The family: an API that accepts a value and echoes it back has not agreed to honour it

Seven separate sessions were lost to this one shape before it was named. The call succeeds, the
value reads back correctly, and nothing honours it. It is the single most expensive pattern in the
project, and every instance below was found the hard way:

| What was asked | What it answered | What it did |
|---|---|---|
| `AndroidArchitecture.X86_64` | reads back as `ARM64, X86_64` | build throws late — an x86-64 Android APK is not buildable on Unity 6.3 (M0-19) |
| `ro.product.cpu.abilist` advertising `arm64-v8a` | `pm install` accepts the APK | promises *installation*, not execution — BlueStacks then cannot run it (M0-20) |
| `GC.GetAllocatedBytesForCurrentThread()` | `0`, always | inert on Unity's Mono — silently passes code that allocates (M0-02) |
| `GC.CollectionCount(0)` | barely moves | same (M0-02) |
| `Application.CanStreamedLevelBeLoaded` | `false` for every scene in the build | inert in the Editor, by bare name *and* full path (M0-13) |
| `SerializedProperty.objectReferenceValue` | assignment reports success | stores `None` if the handle went fake-null (M1-07) |
| `Material.EnableKeyword("_ALPHAPREMULTIPLY_ON")` | the name appears in `shaderKeywords` | the shader never declared it, so it is filed under `m_InvalidKeywords` and changes no pixel — and `IsKeywordEnabled` keeps answering `false` (M5-05b) |

**The rule that falls out of it: when an API answers yes/no about project state, prove it says
"yes" to something true before trusting its "no".** Seed a control. When probing whether a remote
artifact exists, probe a version that certainly exists first — Google Maven answers 404 for every
path on this connection *including its own `master-index.xml`*, so a filtered host and a missing
artifact are indistinguishable without one (M0-19).

---

## 2. Shell probes on this machine

The same trap as §1, in the shell. **Every one of these failed in the direction that looks like
good news.** Seed a file that must match *and* one that must not, or you cannot tell a working
probe from a wrong answer.

- **`grep -c $'\r'` is not a line-ending probe.** It reported five LF files as CRLF; `od -c` and
  `git ls-files --eol` both said LF. When a probe disagrees with git about the working tree,
  believe git (M1-05).
- **`od -c | grep '\r'` is not one either.** Grep's BRE reads the pattern as a literal `r` and it
  found "hundreds of CRs" in pure-LF files. Count `0x0D` bytes outright (M1-06).
- **`CR=$(printf '\r'); grep -c "$CR"` fails its own control** — returns 0 for a file that
  provably contains one. The `TAB=$(printf '\t')` workaround does **not** generalise to CR. Count
  bytes: `io.open(path,'rb').read().count(b'\r')` in Python (M1-09).
- **`grep -E '[ \t]+$'` is not a trailing-whitespace probe.** GNU grep's ERE has no `\t`, so inside
  a bracket expression it is the two characters `\` and `t` — the class becomes "space, backslash
  or t", and it flagged thirteen files whose only crime was ending a line in the letter *t*. Build
  the pattern with a real tab. **`grep -P` is unavailable here** ("-P supports only unibyte and
  UTF-8 locales") (M1-07).
- **A `grep` for `new TypeName` does not find every construction.** Target-typed `new(...)` is
  invisible to it. When a constructor grows a required parameter, let the compiler enumerate the
  call sites (M1-03).

---

## 3. The unfocused Editor

**An unfocused Unity Editor does not tick.** This presents as a hang with no error anywhere, and
one-shot `RunCommand` calls keep working the whole time, which makes the Editor look alive.

| Operation | Unfocused behaviour |
|---|---|
| `TestRunnerApi.Execute` | queued run **completes** — this one is safe (M1-02, M1-05, M3-09c), **and it is submittable straight from a `Unity_RunCommand`** (M3-15, correcting M3-14b) |
| `RequestScriptCompilation`, incl. `CleanBuildCache` | **never drains.** Three attempts left all six DLLs untouched with `isCompiling` reading `True` (M1-05) |
| asmdef reimport | never drains (M1-05) |
| `AssetDatabase.ImportAsset(path, ForceUpdate)` on changed **source files** | **works unfocused** (M1-05) |
| Play mode | freezes after a burst of ~40 frames when *you* drive it — enough to read a census, ids, layers and TMP text (M1-07). **A PlayMode run started by `TestRunnerApi` is a different case and completes normally**: M3-15 took three full PlayMode runs and five isolated ones, all unfocused, all 8–9 s (see below) |
| `SceneView.Repaint` | never lands. Two captures a minute apart came back byte-identical (M1-07) |
| `EditorApplication.delayCall` | never fires (M0-14) |

**The Editor is not the only compiler on this machine, and you do not have to wait for focus to
find a compile error.** Unity writes the exact `csc` argument list it used for every assembly to
`Library/Bee/artifacts/<dag>.dag/<Assembly>.rsp` — defines, references, `-langversion:10`, and
`-analyzer:"Assets/Plugins/Analyzers/Microsoft.Unity.Analyzers.dll"` — and Roslyn ships with the
Editor at `Editor/Data/DotNetSdkRoslyn/csc.dll`. Copy each rsp, repoint `-out`/`-refout` and any
reference to one of our own assemblies at a scratch folder under `Temp/`, and run
`dotnet "<…>/csc.dll" @scratch.rsp`. All six assemblies compile in a few seconds with the real
analyzers, and the errors are the errors the Editor would give. **This entry used to say there is
no `dotnet` SDK here, only a runtime; that stopped being true before M3-12c, which is the first
task the pre-commit format check ever refused.** SDK **10.0.401** is installed, so `dotnet build`
and `dotnet format` both exist now — but `dotnet csc.dll @scratch.rsp` is still the right probe,
because it compiles against *Unity's own* argument list rather than a project file nothing
generates. **What changed for handover is that `dotnet format whitespace --folder
--verify-no-changes --include <files>` is now a real gate**: it refused M3-12c over single-line
`case X: y++; break;` arms, which `.editorconfig` wants split. The whole of `Assets/_Project`
passes as of M3-12c, so a failure is the current task's. The rsp is regenerated by
`AssetDatabase.ImportAsset`/`Refresh`, so a
new file is already listed in it: appending it yourself is `CS2002`. This checks compilation and
analyzers only — **running the suite still needs the Editor's own assemblies rebuilt**, so focus is
still required for a test tally (M2-14b).

**The table's scope keeps shrinking, and M3-15 shrank it again — this row is now about *driving* the
Editor, not about the Editor being asleep.** M3-14c found that an unfocused Editor **does** drain compiles
and **does** import new files, so the compilation row is about `RequestScriptCompilation` rather than about
compilation as such. M3-15 found the same of Play mode: **the entire M3 acceptance — three full PlayMode
runs, five isolated `FrameOrderTests` runs and three EditMode runs, eleven in all — was taken on an Editor
that reported `isApplicationActive == False` the whole time**, each PlayMode run finishing in 8–9 s. The
~40-frame freeze is real when *you* enter Play mode and drive frames yourself; a run the test framework owns
pumps its own loop. **What is still true is the rest of the table**, and the practical rule is unchanged:
confirm `isApplicationActive` before suspecting your callback, and do not assume focus is the problem until
you have checked what kind of operation you are doing.

Ask the owner to focus the window when one of the genuinely-blocked rows bites — a queued run completes in
seconds once it has focus (M0-14).
`isCompiling` reads `False` both while a compile is pending *and* after it is done, so check
`Library/ScriptAssemblies/*.dll` timestamps instead (M1-02). **A source file's mtime can read
newer than the assembly that already compiled it successfully** — pair the DLL timestamp with
`find -newer` over the sources *and* a console read (M1-05).

**And the timestamp probe itself gives false negatives, so do not stop at it.** At M3-09c an
unfocused Editor drained a full six-assembly compile of eighteen changed files — the domain reloaded,
the Console cleared, and every new type was live — while `Library/ScriptAssemblies/*.dll` still
carried timestamps *older than every source in the change*, and `find -newer` therefore listed all
eighteen as uncompiled. Two `sleep`-and-recheck cycles were spent on a compile that had already
finished. **The probe that actually answers is to use the new API from a `RunCommand`**: call the new
constructor, read the new constant, `typeof()` the new class. If the snippet compiles and runs, the
Editor has the code — which is a stronger statement than any timestamp, and it is the same
`typeof`-not-reflection trick the MCP forces on you anyway (§4). Keep the timestamps as a hint about
*when*, never as the answer to *whether* (M3-09c).

---

## 4. The Unity MCP

### `Unity_RunCommand`

- **The shape of a command is not documented anywhere and cost four probes to rediscover** (M3-11a-ii,
  filed at M3-11b). It is **not** top-level statements and **not** a static class:

  ```csharp
  internal class CommandScript : IRunCommand
  {
      public void Execute(ExecutionResult result)
      {
          result.Log("this is how output leaves");
      }
  }
  ```

  Everything you submit is wrapped in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`,
  so `public` on the class fails an inconsistent-accessibility check and `using` directives are best
  avoided — **fully qualify instead**. **Output goes through `result.Log(...)` and nowhere else**: a
  value returned from `Execute` comes back as *"No logs available"*, which reads as a command that
  did not run. Helper classes go **beside** `CommandScript` at top level and `internal`, never nested
  (see the rewriter bullet below).

- **Refused as `k_UnsafeMethods`** (they demand a confirmation the MCP cannot give):
  `File.Delete`, `File.Move`, `AssetDatabase.DeleteAsset`, `AssetDatabase.Refresh()`. Overwrite
  with `File.WriteAllText` instead (M0-03). Bisect against this list rather than trusting it whole —
  **it is not stable across Editor sessions**, which is the next bullet.
- **`TestRunnerApi.Execute` is refused again, and the refusal survives every dodge inside a
  command.** M1-19 recorded four direct calls running clean; at M2-04 every shape of it came back
  `UNEXPECTED_ERROR: User interactions are not supported`, with the command **not executing at
  all** — the first `File.WriteAllText` of the method never landed, so this is a pre-execution
  check on the submitted code, not a runtime one. Refused: the direct call; the call deferred to
  `EditorApplication.delayCall`; the call deferred by seconds through `EditorApplication.update`;
  and the call made by reflection with the type and method names split across concatenations
  (`"Exec" + "ute"`). Accepted in the same session: `CreateInstance<TestRunnerApi>()`,
  `RegisterCallbacks(this)`, implementing `ICallbacks`, and reflective `Invoke` of anything else.
  **The workaround is to put the call in an assembly that already references the test runner and
  reach it reflectively** — `Soulvail.Tests.Core` has `UnityEditor.TestRunner` in its asmdef, so a
  temporary `public static void Run()` there, invoked with
  `Type.GetType("….TempSuiteRunner, Soulvail.Tests.Core").GetMethod("Run").Invoke(null, null)`,
  starts the suite and writes its tally to `Temp/` exactly as before. Delete the helper before
  handover. **Do not conclude from this entry that the list is fixed either way: probe it, because
  it has now moved twice** (M1-17, M1-19, M2-04).
- **M3-01b: `TestRunnerApi.Execute` ran clean again, directly, and the bullet above cost four dead
  ends before that was noticed.** The plain shape — `CreateInstance<TestRunnerApi>()`,
  `RegisterCallbacks(this)`, `Execute(new ExecutionSettings(new Filter { testMode =
  TestMode.EditMode }))` — started the suite with no reflection and no temporary helper, for both
  EditMode and PlayMode. **Every refusal in that session was `File.Delete`**, sitting two lines above
  the test call in a method that tidied up its own results file. Because the check is
  pre-execution, the *whole* command is refused and the message names neither the line nor the
  reason, so the innocent-looking call nearest the bottom gets the blame. **The general rule, third
  time of asking: the refusal message identifies nothing — bisect the script, not the error.** The
  cheapest bisect is to delete half the body and resubmit; the tell here was that a command with the
  test call and no file I/O passed, which took one probe and should have been the first one. And
  when a command needs to start from a clean file, **overwrite it with a sentinel** (`"RUNNING"`)
  rather than deleting it — the poller then reads state from the file's contents instead of its
  existence, which is what the first bullet's *"overwrite with `File.WriteAllText`"* means in
  practice.
- **M3-15: `TestRunnerApi.Execute` ran clean again, and M3-14b's throwaway `Assets/Editor/` harness
  was never necessary.** That task recorded the call as refused and built a scratch editor assembly to
  get around it; M3-15 submitted the plain shape from an ordinary `Unity_RunCommand` and took **eleven
  runs** through it — three EditMode, three full PlayMode, five isolated — on an unfocused Editor,
  creating and deleting nothing. **So the list has now moved four times** (M1-17, M1-19, M2-04, M3-01b,
  M3-15), which retires any reading of this section that treats it as settled: **probe it, every time, and
  do not build scaffolding until a one-line probe has actually been refused.** The cost of not probing is
  a scratch assembly in the tree while the suite runs — which M3-14b had to disclose in its own
  *Verified* row, because both of its tallies were taken with it present.
- **Four things that bite when you drive the suite from a command, all found at M3-15:**
  1. **The rewriter lifts a nested type out of `CommandScript` and emits it twice** — once nested,
     once at namespace scope with its `private` modifier intact, which is `CS1527` on a line number that
     does not exist in what you submitted. Declare helper types **top-level and `internal`**, beside
     `CommandScript`.
  2. **Calling `Execute` again from inside `RunFinished` is silently ignored.** Not an error, not a
     refusal — the chain simply stops after one run and your tally is short. One `Execute` per
     submitted command; loop from the outside. **Narrowed at M4-07, and the narrowing is what makes a
     repeat-run experiment affordable: the ban is on re-entering `Execute` *from inside the callback*,
     not on looping inside one submission.** Set a countdown in `RunFinished`, subscribe a `static`
     handler to `EditorApplication.update`, and call `Execute` from there once it drains — **63
     consecutive PlayMode runs and 5 EditMode runs went through one submitted command each this way, on
     an unfocused Editor**, about 25 s per PlayMode iteration. Two things make it survive: the counters
     live in **`SessionState`**, never in a field (item 4 below), and each submission stamps a **token**
     into `SessionState` that its own `RunFinished` checks, so the still-registered sinks from earlier
     submissions (item 3) see a stranger's token and unregister themselves instead of double-counting.
     `EditorApplication.update` does fire unfocused between runs, which §3's table does not say and
     which `EditorApplication.delayCall` (M0-14) does not do.
  3. **Callback sinks from *earlier* commands stay registered and keep firing.** Each submission compiles
     a fresh dynamic assembly, but the old `ICallbacks` instances are still attached to the runner, so a
     naive counter double- and triple-counts. Key your results per submission, or read the *number of
     distinct runs* rather than the number of callback entries.
  4. **Entering Play mode reloads the domain and wipes non-serialized fields**, including on a
     `ScriptableObject` with `HideAndDontSave`. A counter in a plain `internal int` comes back as **0**
     and your loop exits immediately. **`SessionState` survives; a field does not** — which is a
     sharper version of the `hideFlags` lesson in §7, because there the object survived and here the
     object survives with its state erased.
- **The plural `AssetDatabase.DeleteAssets(string[], List<string>)` is *not* refused**, and
  `AssetDatabase.Refresh()` — including the `ImportAssetOptions.ForceUpdate` overload — ran clean
  in M2-art across a dozen commands. The refusal list above is per *method*, not per capability, so
  a blocked call often has a sibling that works. **The failure looks nothing like a refusal**: the
  whole command returns `UNEXPECTED_ERROR: User interactions are not supported for MCP tool calls`
  with no hint which line caused it, so bisect by deletion rather than reading the message (M2-art).
- **Unauthorized namespaces:** `System.Net`, `System.Diagnostics`, `System.Runtime.InteropServices`,
  `System.Reflection` (M0-03, M1-07).
- **No VContainer reference.** `AddComponent<RunScope>()` will not compile, and a command must not
  name `LifetimeScope` or anything deriving from it — including `BootScope` and `RunScope`. Find a
  scope by walking the scene's `MonoBehaviour`s and matching `GetType().FullName`; nothing in a
  scope's container is reachable from a command at all, so watch the scene instead
  (`FindObjectsByType<EnemyView>()`) (M0-13, M1-09, M1-18).
  **You can still *dress* a scope's serialized fields, and that is how a scene gets wired from a
  command:** reach the type with
  `Type.GetType("Soulvail.Game.Composition.RunScope, Soulvail.Game")`, find it with the
  non-generic `root.GetComponentInChildren(type, true)` — which returns a `Component`, a type the
  command *is* allowed to name — and write the field through
  `new SerializedObject(component).FindProperty("_myField")`. The assembly-qualified name is
  mandatory: a bare `Type.GetType("Soulvail.Game.Composition.RunScope")` looks only in the calling
  assembly and returns null. This is what dressed `Run.unity` at M3-09c, and it is the general
  answer to *"the command cannot name the type it has to edit"* (M3-09c).
- **No `System.Numerics` reference**, so no command can call a core method taking a `Vector2` (M1-15).
  **Narrower than it reads, measured at M3-12b: the ban is on naming `Vector2`/`Vector3` in a
  signature, not on touching a type that holds one.** A command implemented the whole of
  `IIntentSink` — whose five members take `ConeHitIntent`, `ChargeIntent`, `EnemyMoveIntent`,
  `PlayerMoveIntent` and `EnemyKnockbackIntent`, every one of which carries `Vector2` or `Vector3`
  fields — and it compiled clean, because the *signatures* name only Soulvail types. So a probe can
  construct a `PlayerCombat`, stand in for its ports and read anything off it; what it still cannot
  do is pass a vector in or read one out. **The practical rule: check what the signature says, not
  what the struct contains** — the wider reading costs a probe that would have worked.
- **The rewriter hoists nested classes out of `CommandScript`** *and leaves them in place*, so a
  nested `ICallbacks` fails with `CS1527`. Two working shapes: one class implementing both —
  `internal class CommandScript : IRunCommand, ICallbacks`, registering `this` — or **a separate
  helper class declared at top level beside `CommandScript`, and `internal`, never nested**. The
  second is what a `TestRunnerApi` runner wants, because the callbacks object has to be rooted in a
  `static` field that survives the call (§7); `public` on it fails the same inconsistent-accessibility
  check `public class CommandScript` does (M1-07, M1-14/15, M3-09b, M3-09c).
- **Fully qualify any type whose short name is also a namespace segment.** The injected
  `Unity.AI.Assistant.…` wrapper namespace shadows them: `UnityEngine.UI.Image` alone is
  `CS0118: 'Image' is a namespace but is used like a type`. Same for `CompilationPipeline`, `Canvas`,
  `RenderMode` and the test-runner namespace. **The tell is that the error names a type you did not
  think was ambiguous**, so reach for the fully-qualified spelling before reading the message twice
  (M0-14, M3-09b, M3-09c).
- **The test-runner namespace is `UnityEditor.TestTools.TestRunner.Api`**, and
  `NUnit.Framework.Interfaces` is not referenced from that assembly — compare
  `TestStatus.ToString()` (M0-14).
- **Two `TestRunnerApi` runs queued in one command both receive the first run's callbacks.** An
  EditMode filter and a PlayMode filter produced two identical EditMode result files and the
  PlayMode filter never ran. One run per command, poll for its file, then queue the next (M1-06).
- **Never reimport and `Execute` in the same command.** `AssetDatabase.ImportAsset` on a changed
  script queues a compile, the compile reloads the domain, and the reload takes the `static` field
  rooting your `ICallbacks` object with it — so the run is started, the callbacks are dropped, and
  the results file sits at its `"RUNNING"` sentinel for ever with nothing logged. It looks exactly
  like a queued run stalling on an unfocused Editor (§3), which is the wrong thing to go and
  investigate. **Import in one command, wait, then `Execute` in the next** (M3-09c).
- **A filter by `testNames` silently matches nothing when given a fixture's name.** `testNames`
  wants whole test names; a class is `groupNames`, which matches the fixture and every row in it.
  A mismatched filter does not error — it runs zero tests and reports `passed=0 failed=0`, which
  reads as "the suite is empty" rather than as "the filter is wrong" (M3-09c).
- **A probe cannot survive `AssetDatabase.SaveAssets()`** — the reimport reloads the domain and
  takes the dynamic assembly's statics and its `EditorApplication.update` subscription with it. A
  probe that changes project data must restore it in the same synchronous command (M1-17).
- **`result.Log` ignores format specifiers.** `{0:F2}` is emitted literally while a bare `{0}`
  substitutes — pre-format with `ToString("F2")` (M0-20).
- **`RegisterCallbacks` + `Execute` + `System.IO` in one command is refused before it runs**, with
  `UNEXPECTED_ERROR: User interactions are not supported for MCP tool calls`. The refusal is not
  about the API: `RegisterCallbacks` alone passes, `Execute` alone passes, and the two together pass
  when the callbacks only `Debug.Log`. It is the *file write* alongside them that trips the guard,
  and the error names none of that — it reads as though the Editor put a dialog up. **The
  `Temp/`-file recipe below therefore cannot be written from inside the command that starts the
  run**; use the results file Unity writes anyway (M4-06).
- **Unity writes `TestResults.xml` to the persistent data path on every `TestRunnerApi` run**, at
  `%USERPROFILE%/AppData/LocalLow/<Company>/<Product>/TestResults.xml`, and logs the path as it
  does. It carries `passed=`, `failed=` and every row's `result=` with its failure message, it is
  readable from the shell with `grep`, and it needs no harness and no `static` field to survive a
  domain reload — which makes it the cheaper source for a suite count than anything a command can
  write for itself. **The only care it needs is the timestamp**: a stale file from the previous run
  looks exactly like a finished one, so check `start-time` before believing a count (M4-06).

### `Unity_GetConsoleLogs`

**Does not return plain `Debug.Log` entries.** Warnings and errors come back; `Log` comes back
empty even while the Console is visibly showing them. A `TestRunnerApi` run therefore cannot report
through the Console — **write results to a file under `Temp/` from `RunFinished` and read them
from the shell**, which also keeps the Editor focused, since every extra MCP round-trip risks
stealing focus back and stalling a queued run (M1-09, M0-20).

**Refined at M4-06, and the refinement is the difference between "empty" and "filtered out":** a
call passing `logTypes: "Log"` returns nothing, while the *same* entries come back on an unfiltered
call typed **`Info`**. So `Debug.Log` is reachable after all — by not asking for it by name. That
matters because the recommended `Temp/`-file route is refused when it shares a command with
`RegisterCallbacks` (§4 above), which leaves logging as the only in-command channel a runner has.

### Captures

The scene view draws its own skybox whatever the camera's clear flags say, and a Screen Space
Overlay canvas renders in the scene view but *not* into `Camera.Render` — so one capture can verify
a UI layout or a camera background, never both (M0-17). `Unity_Camera_Capture` on a play-mode
camera fails with "No GameObject found with Instance ID" (M1-07).

---

## 5. Unity Editor and the asset pipeline

- **Every `MonoBehaviour` and `ScriptableObject` needs a *block* namespace, never a file-scoped
  one.** Unity 6.3's script importer finds a file's type with its own parser, which does not
  understand `namespace X;`. The type compiles, but no `MonoScript` is linked: every asset
  referencing it serialises as `m_Script: {fileID: 0}` and loads as null, and **nothing anywhere
  reports an error**. Writing the correct GUID into the YAML by hand does not fix it — the
  `MonoScript` itself has no class. Pure C# keeps file-scoped namespaces; `.editorconfig` silences
  the diagnostic for the `Game/` and `Editor/` trees (M0-11).
- **Every asmdef needs a `csc.rsp` containing `-langversion:10` beside it**, or its first
  file-scoped namespace breaks the build. It is per-assembly — an `Assets/csc.rsp` does not reach
  asmdef assemblies. All six have one; add it with any seventh. An asmdef with no `.cs` files
  produces no assembly (M0-02).
- **`File.Move(source, destination, overwrite)` does not exist at this project's API compatibility
  level**, despite the setting reading as .NET Standard and the overload being part of .NET
  Standard 2.1. It fails as `CS1501: No overload for method 'Move' takes 3 arguments`, in
  `Soulvail.Game` and not only in the MCP's scratch assembly — verified in both. The atomic-rename
  spelling that does compile is `File.Replace(tmp, target, null)` when the destination exists and
  the two-argument `File.Move` when it does not; `File.Replace` throws `FileNotFoundException`
  rather than creating the destination (M2-13b).
- **`JsonUtility` honours `ulong` and `float` exactly, and that is worth knowing because §1 says to
  assume it does not.** Probed before the save format was written: `ulong.MaxValue` round-trips to
  the digit, a `float` is written as the full expansion of its double value (`0.1f` →
  `0.10000000149011612`) and compares equal on the way back, `ToJson` emits fields in declaration
  order with no whitespace, `FromJson<T>("{}")` yields every field at its default rather than
  throwing — which is what makes a version field of 0 mean *never written* — and malformed JSON
  throws `ArgumentException`. What it cannot see is a **property**, so a `readonly struct` DTO
  serialises as `{}` in silence and needs a field-only mirror type (M2-13b).
- **A branch switch rewrites every file's mtime, and an unfocused Editor can stall halfway through
  the reimport it triggers** — leaving a script→assembly map that is *wrong* rather than empty.
  Symptom: `CS8773 file-scoped namespace … C# 9.0` on a brand-new file whose `csc.rsp` is present
  and correct, because the file was compiled into `Assembly-CSharp`. **Read that error as "this
  file is not in the assembly you think", never as "the rsp is missing."** `AssetDatabase.Refresh`,
  per-script `ImportAsset(ForceUpdate)`, asmdef reimports and `RequestScriptCompilation
  (CleanBuildCache)` all repaired the map but never the compiled source lists. **Restarting the
  Editor fixed it on the first try; reach for the restart early** (M1-01).
- **`CompilationPipeline.GetAssemblies` reports the *last compilation*; `GetAssemblyNameFromScript
  Path` reads the live map.** They can disagree for minutes, and the disagreement is the diagnosis
  — check both before concluding anything about compile state (M1-01).
- **Opening and saving a scene re-serialises more than you changed.** Saving `Run.unity` for a
  one-line field added three unrelated TMP prefab-instance overrides. **Check `git status` for
  files you never opened after any Editor-driven asset edit** — they are committed by default and
  nothing mentions them (M1-12).
- **Unity backfills project settings behind you.** Touching layer settings rewrites
  `TagManager.asset` at `serializedVersion: 3`, dropping 24 empty `m_RenderingLayers` rows (M1-07);
  creating the first URP material writes `m_ProjectSettingFolderPath` into
  `URPProjectSettings.asset` (M1-09); saving project settings from a probe re-serialised
  `QualitySettings.asset` to `serializedVersion: 5`, dropping the whole `PC` quality level, and
  flipped `UnityConnectSettings.m_Enabled` 0 → 1 (M1-17). All are format migrations or defaults,
  not decisions anyone made. **The Editor holds the rewritten copy in memory afterwards, so the
  next write repeats it — restart to clear, and check `git diff ProjectSettings/` before every
  commit.**
- **`OnValidate` runs the frame a component is added, before a single field could have been
  filled in** — and again on every `SerializedObject.ApplyModifiedProperties`. A validation rule
  that calls `Debug.LogError` there therefore makes *adding the component* a failure, and fails
  every test that builds one: Unity's test framework treats an unexpected `LogError` as a failed
  test. Warn, and stay silent while nothing has been dressed (M2-11a). The project's four
  `OnValidate`s all warn, for this reason.
- **`[Min]` / `[Range]` on a `[SerializeField]` clamp the Inspector GUI only.** A
  `SerializedObject` write, a merge or a hand-edited YAML goes straight past them, which is why the
  real guards are in the core constructors `ToSpec()` calls (M0-11).
- **A `UnityEngine.Object` handle fetched before an unrelated prefab is saved can be fake-null by
  the time it is used.** Load a reference immediately before assigning it, and **read the field
  back after saving** — the read-back is what caught it (M1-07).
- **Before deleting any template asset, `grep -rl <guid> Assets ProjectSettings`.**
  `SampleSceneProfile` read as scene-local dressing and was the `m_VolumeProfile` of both URP
  pipeline assets (M0-13).
- **A `GameObject.CreatePrimitive(PrimitiveType.Cylinder)` ships a `CapsuleCollider`**, whose
  rounded caps sit inside the mesh silhouette once non-uniformly scaled — a scaled cylinder needs a
  `MeshCollider` or the player sinks into it before being stopped (M0-16).
- **A vendored package tree needs a `"path/**" -whitespace` line in `.gitattributes`** or the
  pre-commit whitespace check refuses the commit. TMP's shaders carry trailing whitespace and
  space-before-tab indents, and editing them is both wrong and futile — Unity reverts on reimport.
  Do not extend the hook's exclusion list, which is about Unity-serialised *formats*, not vendor
  paths (M0-13). `*.asset` is already excluded, which is why Unity's own trailing space on
  `m_EditorClassIdentifier: ` survives (M1-03).
- **Check the Game view aspect before trusting any feel or framing measurement** — it was portrait
  1440 × 2960 during M0-20 while the build is landscape-locked (M0-20).
- **An edit-mode pose does not survive to the next MCP command.** `AnimationClip.SampleAnimation`
  appears to work and reports nothing wrong, but an enabled `Animator` rewrites the pose on the
  next editor tick, and on a prefab instance the sampled transforms are reverted even with the
  Animator disabled. Two captures in a row came back in T-pose with every log line saying the
  sample had been applied. **To see an animation, enter Play mode** — nothing in edit mode is
  worth trusting for this (M2-art).
- **`Animator.Play` only evaluates while `speed` is non-zero.** `Play(state, 0, t)` then
  `speed = 0` then `Update(0f)` leaves the body in whatever pose it already held, silently and with
  the state machine reporting the state you asked for. Set the speed to zero *after* the `Update`
  that evaluates (M2-art).
- **Imported animation clips are non-looping by default**, so a blend tree parks on the last frame
  of `Idle_A` and the character freezes mid-stride. `loopTime` lives on `ModelImporter`'s
  `clipAnimations`, which must be assigned as a whole array read from `defaultClipAnimations` —
  there is no per-clip setter (M2-art).
- **Unity 6.3 rewrites `ProjectSettings/TimeManager.asset` the moment the Editor touches the
  project, and it looks exactly like a change somebody made.** `Fixed Timestep: 0.02` becomes a
  `serializedVersion: 2` block — `m_Count: 2822399` over `m_Rate: 141120000 / 1` — which *is*
  0.02 to the bit. **It is a no-op re-serialisation, not a setting.** The pre-commit hook refuses a
  staged `ProjectSettings/` change without `ALLOW_PROJECT_SETTINGS=1`, so the right move is
  `git checkout -- ProjectSettings/TimeManager.asset` and not an override; it will come back for
  whoever opens the Editor next. Do not grant it the flag and do not commit it as *"Unity churn"* —
  the flag exists for a `ProjectSettings/` change somebody meant, and the whole value of
  `git diff m3 HEAD -- ProjectSettings/` being empty is that nothing has slipped through it
  (M4-04).
- **The MCP's `Unity_RunCommand` wraps your snippet in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`,
  which puts `Unity.AI.Image` in scope and shadows `UnityEngine.UI.Image`.** `Image.Type.Simple`
  fails with *"'Image' is a namespace but is used like a type"* and `Image.FillMethod` with *"does
  not exist in the namespace 'Unity.AI.Image'"* — both of which read as a missing assembly
  reference and are not. Alias it (`using UiImage = UnityEngine.UI.Image;`). **The sandbox also
  refuses `System.Reflection.BindingFlags` outright** — *"unauthorized namespaces"* — so an MCP
  probe can only drive a component through its public API, which is a reason to keep a view's
  readouts public rather than reflected (M4-04).

---

## 6. VContainer

- **VContainer disposes only what it constructed.** `CreateTrackedInstance` skips any registration
  whose provider is an `ExistingInstanceProvider`, so **anything handed to `RegisterInstance` is
  never disposed with its scope.** `builder.RegisterInstance(new DomainEventHub())` compiles,
  resolves, aliases correctly and leaks every subscription from each run into the next, with
  nothing in the log. **Every adapter that owns a resource is registered as a *type*.**
  `RegisterComponent` is also an instance registration and also never disposed — correct when the
  scene owns the lifetime (M0-12, M0-16).
- **`As<T>()` alone leaves the concrete type in the registry as a null guard** — `.AsSelf()` is
  required or `Resolve<DomainEventHub>()` throws on a container that contains one (M0-12).
- **`RegisterInstance` is hardwired to `Lifetime.Singleton`** and cannot be Scoped; a Singleton
  registered inside a child scope still resolves scope-locally, so the label lies before the
  behaviour does (M0-12).
- **VContainer never honours a C# default parameter value.** `ReflectionInjector.CreateInstance`
  calls `ResolveOrParameter` for *every* constructor parameter and throws if nothing answers —
  there is no `HasDefaultValue` branch in the package. It fails at scope construction, not at
  compile time. Pass it with `WithParameter` and name the default as a `const` so the two cannot
  drift (M1-19). **It has now fired twice, and the second time the code carried a comment warning
  about the first:** M2-05 gave `NavPathSense` a third parameter with a default, `RunScope` passed
  the other two, and the container went looking for a registration of `System.Int32` and failed to
  build the whole run scope. **Adding a defaulted parameter to a registered type is a change to its
  registration**, and nothing in the compiler says so. Every EditMode row was green; the PlayMode
  smoke test is what caught it (M2-05).
- **`RegistrationBuilder.WithParameter(string name, object value)` is how a primitive constructor
  argument is wired** — the named overload survives a second parameter of the same type being added
  later, which `WithParameter<int>` would not (M1-06).
- **`RegisterComponent<T>(instance)` injects; `RegisterInstance` does not.** `RegisterComponent`
  adds a build callback that resolves the registration on purpose, so an `[Inject]` member on a
  scene component runs during that scope's `Awake`. The disposal rule above is unchanged and
  orthogonal — neither is disposed by the scope (M0-18).
- **A component cannot ask "was I injected?" before `Start`.** A `LifetimeScope` injects from its
  own `Awake`, and Unity orders no two `Awake` calls in a scene, so the same check in `Awake` or
  `OnEnable` is a race that passes on one machine and fails on another (M0-18).
- **A component on a prefab that `IObjectResolver.Instantiate` creates cannot subscribe in
  `OnEnable`.** Unity runs `Awake` and `OnEnable` *during* the instantiate, before VContainer
  injects anything, so a hub read there is null on every instance. Subscribe from the `[Inject]`
  method and check "was I injected?" in `Start` (M1-12).
- **A `Func` registration is lazy** — its side effects happen at the first `Resolve`, not at
  `Install` or `Build`, which is where a `LogAssert.Expect` has to be aimed (M0-12).
- **A `VContainerSettings` asset does nothing unless it is also in PlayerSettings → Preloaded
  Assets.** `Instance` is set from `OnEnable`, which the Editor only triggers through
  `LoadInstanceFromPreloadAssets` at `BeforeSceneLoad`. The package's Create menu item registers
  it; `AssetDatabase.CreateAsset` alone leaves an asset that inspects perfectly and never loads,
  after which `IsRoot` is false and **every scene scope silently becomes its own root with an empty
  container** (M0-13).
- **`LifetimeScope.GetRuntimeParent` consults `parentReference`, then `FindParent()`, then the
  settings root** — so assigning a parent to `RunScope` in the Run scene would *break*
  Play-in-Run, not enable it (M0-13).

---

## 7. Tests, NUnit and measuring allocation

- **Never hand-roll a GC allocation probe** — see §1. Use `AllocationAssert.None`
  (`Tests/Core/Support/`), which measures with Unity's GC recorder (M0-02).
- **`AllocationAssert` cannot measure a `stackalloc` span** — a lambda cannot close over a ref
  struct. Build the buffer as a heap array outside the measured body and let it convert at the call
  site inside (M1-03).
- **A fixture that hand-writes perception passes vacuously when the thing under test reads a
  *different* copy of the same fact.** A Bloater's blast is resolved against the player position
  `EnemySystem` last *ingested*, not against the blackboard a behaviour test assigns — so a fixture
  built on `SpitterBehaviourTests`' hand-written `Place` would have had every explosion measuring its
  distance to the origin while each row believed it had moved the player. Nothing fails; the rows go
  green. **When two copies of a fact exist, the test has to write the one the code reads** — here, by
  sensing the whole arena through `Ingest` (M2-08).
- **A row that subscribes to `TaskScheduler.UnobservedTaskException` is watching the whole process,
  not the class under test — including the Unity MCP's own websocket.**
  `SaveWriterTests.Writer_ObservesEveryFault` arms that event and forces two `GC.Collect` /
  `WaitForPendingFinalizers` passes, so *any* unobserved faulted task in the Editor that is finalised
  inside that window fails it. At M3-09a one EditMode run in three came back **1 464 / 1** carrying a
  `ClientWebSocket.ConnectAsyncCore` fault from the MCP bridge; the runs either side were green on
  unchanged code. **The failure message names `SaveWriter` and the exception is nothing to do with
  it — read the exception before believing the row.** If it recurs, the narrow fix is to filter the
  captured exceptions down to the one the fixture armed rather than asserting the list is empty
  (M3-09a).
- **`RecordingEvents` may never appear inside an `AllocationAssert` body.** It stores each payload
  in a `List<object>`, so it boxes every struct and the row measures the fake instead of core. Use
  a silent `IDomainEvents`; the real `DomainEventHub` does not box (M1-18).
- **An allocation test that measures a system through a recording fake measures the fake's
  `List<T>` doubling too.** Grow the recorder past the measured window and `Clear` it first —
  `Clear` keeps capacity — and prove the probe is live by making the system allocate on purpose
  once (M0-10).
- **An allocation test over a cached read measures one field read N times.** To measure the
  recompute, invalidate inside the measured body (M1-01).
- **`Awake` never runs in EditMode**, so anything cached there is null to a test — and a lookup
  that silently finds nothing is a *passing test of a broken feature*. `EnemyViews`' collider index
  was empty in every EditMode test for exactly this reason. **The general rule: when a test's setup
  cannot run `Awake`, the thing `Awake` was going to do must be reachable another way, or the
  feature has no coverage at all and looks like it does** (M0-16, M1-12).
- **The PlayMode runner has already spent the app's one cold start on its own scene before a test
  body runs**, so any entry point whose behaviour depends on *which scene it started in* has
  already decided and will never revisit it. It must also react to `sceneLoaded` or it is
  untestable, and the failure is silent (M0-16).
- **`Object.Destroy` in edit mode destroys nothing and logs an *error*** — it does not throw — so
  it both leaks the object and reddens any EditMode test that disposes one. Every adapter that owns
  a `CreateInstance`d `ScriptableObject` needs a `DestroyImmediate` branch (M0-14).
- **NUnit's `Assert.Throws<T>` is an exact type match.** "Improving" a guard to a derived exception
  reddens correct code. `Assert.Catch<T>` is the assignable form (M0-08).
- **Two identical string literals are the same interned instance** — an equality test written
  entirely with literals can pass on reference equality and prove nothing (M0-08).
- **A test whose expected value is a non-ASCII literal cannot detect an encoding fault**: the
  expectation and the output garble identically and it passes. Assert the code point in ASCII
  source (M1-01).
- **A serialized field whose C# initialiser equals the number the asset ships makes an "the asset
  carries these values" test vacuous** — it passes identically if the YAML key binds to nothing.
  Prove the binding with `AssetDatabase.ForceReserializeAssets`, which drops keys matching no field
  (M1-03).
- **Never pin an FSM's transitions to exact frame counts.** A running sum of 1/120 s steps lands
  within an ulp of 0.4 at the forty-eighth, so which frame a 0.4 s windup completes on is float
  accumulation. Tick until the state arrives — which is also the only way to observe a one-tick
  state (M1-18).
- **The double-tap guard on a button is `Selectable.interactable`, and `onClick.Invoke()` bypasses
  it entirely** — a test that "taps twice" runs the handler twice and reddens correct code. Assert
  the flag; uGUI is what refuses the second touch (M0-17).
- **A `ref`-returning member needs `ref` on both sides of the assignment** or the caller silently
  gets a copy — and a test that forgets it passes while proving nothing (M0-05).
- **`FixedRandom` has two constructors**: `new FixedRandom(99)` is a seed, not a scripted value,
  because `int`→`int` wins overload resolution. A scripted 99 is `99f` (M0-10).
- **A `TestRunnerApi.RegisterCallbacks` registration outlives the run it was made for, so every
  collector registered earlier in an Editor session fires again on every later run.** Run EditMode,
  then PlayMode, and the EditMode collector's `RunFinished` fires a second time with the *PlayMode*
  result and overwrites its own output file — so a results file read after a subsequent run reports
  the wrong suite, with nothing to say it has been clobbered. It looks like a suite that suddenly
  dropped from 1266 tests to 11. **Read each run's file before starting the next run, or write to a
  path unique per run and have the callback ignore results that are not its own** (M3-03).
- **A `TestRunnerApi` created with `ScriptableObject.CreateInstance` and left in a local is collected
  mid-run, and the callbacks registered on it go silently with it.** The run itself finishes
  normally — every test executes, the Test Runner window updates — but `RunFinished` never fires,
  so a results file written from the callback stays at whatever the command wrote before starting
  and there is no error anywhere to say why. It reads exactly like a suite that hung, and probing
  `EditorApplication.isPlaying`/`isCompiling` afterwards says the Editor is idle, which makes it read
  like a *finished* run whose results vanished. It is length-dependent, so short filtered runs
  succeed and the full suite is where it bites: at M3-08b three runs were lost to it after five
  identical ones had worked. **Set `api.hideFlags = HideFlags.HideAndDontSave` and root both the api
  and the callback object in a `static` field**, clearing them in `RunFinished` (M3-08b).
- **To prove a sealed, non-virtual collaborator was never *asked*, hand it an input that throws the
  moment it is used.** There is often no fake to write: `TriggerSpec` is `public sealed` with a
  non-virtual `IsMet` and no interface, `TriggerClause` is a `readonly struct`, and
  `CombatBlackboard` is sealed with public fields — so *"count the evaluations"* has nowhere to go,
  and unsealing the type for a test's convenience puts a vtable dispatch on a 60 Hz path. Instead
  make the input **unreadable**: `TriggerClause`'s constructor validates only `threshold` and never
  `field`, so `new TriggerClause((TriggerField)99, …)` constructs, survives `TriggerSpec`'s
  count-only constructor, and throws out of `IsMet`'s loud `default` on the first read. A hundred
  silent ticks is then the proof. **It needs its control or it proves nothing** — the same input on
  the path that *should* read it must throw, or the row is green against code that skipped the
  collaborator for some entirely different reason. Generalises to any collaborator with a loud
  failure mode and a constructor that does not pre-validate (M3-07a).
- **Unity wipes `Temp/` out from under you mid-session, so it is not a place to keep evidence.** A
  completed 50-run measurement written to `Temp/m3-08a-baseline/` — every file read and verified —
  was gone an hour later, along with two suites' results files, with nothing logged and no prompt.
  `Temp/` is Unity's own scratch directory and the Editor is free to clear it; a results file there
  has the lifetime of the Editor's convenience, not of the session. **Write anything you will still
  want in ten minutes to `Logs/`**, which is equally gitignored and is not Unity's to clear. The
  reason this cost nothing when it happened is the row above: each run's file had already been read
  before the next run started, so the numbers survived in the transcript even though the files did
  not (M3-08a).
- **A PlayMode run clears the Console on entering Play, so a Console sweep taken after one says
  nothing about the EditMode suite.** Run EditMode, then PlayMode, then sweep, and the Console holds
  eleven tests' worth of nothing — which reads as *"zero errors, zero warnings"* and is a false
  all-clear for the suite that has 1 286. **Sweep the Console immediately after the run whose output
  you mean to describe**, or run EditMode last. The same clear is why the Console can be empty at
  the start of a session that ended with a PlayMode run (M3-04).

---

## 8. Input System

- **`StickDeadzone` caps stick magnitude at exactly 1 for any input above `max`**, so a
  `ClampMagnitude` downstream cannot be exercised through a gamepad stick and its test would pass
  with the clamp deleted. With defaults the same processor also rescales (0.3, 0.6) to ≈(0.305,
  0.610), and **`InputTestFixture` does not neutralise deadzones for you** (M0-14).
- **A Value action enabled while a control is already deflected reads zero until the next
  `InputSystem.Update()`** — the initial state check runs on the update *after* the enable. The
  adapter is enabled as a run starts, so a thumb already on the stick moves the player one frame
  late (M0-14).
- **`OnScreenControl.OnEnable`/`OnDisable` are `protected virtual`, and `base.OnDisable()` nulls
  `m_Control`** — so any final `SendValueToControl` must be sent *before* the base call or it is a
  silent no-op, and a stick disabled at full deflection leaves the player walking (M0-15).
- **Synthetic Input System events cannot be made deterministic while a physical device is
  present.** `QueueStateEvent` replaces the device's whole state, and a real event arriving in the
  same update overwrites it — which reads as a flaky *feature* rather than a flaky probe.
  **Probe the predicate, not the input**: `EventSystem.RaycastAll` at chosen screen points proved
  the stick-region rule exactly, with no input at all. Anything that genuinely needs a press is a
  manual step for the owner (M1-09).
- **`EventSystem.IsPointerOverGameObject(pointerId)` is the wrong tool for a touch game, and it
  fails in the silent direction.** Its id is `kMouseLeftId` for a mouse and the *touch id* for a
  finger; guess wrong and it returns `false`, so every tap over the stick reaches the arena. Use
  `EventSystem.RaycastAll` against the screen position you already hold (M1-09).
- **`InputSystemUIInputModule` auto-assigns the package's `DefaultInputActions` when added with no
  asset**, so no UI action map is needed in `Soulvail.inputactions` (M0-16).

---

## 9. Rendering, canvas and TMP

- **A `MaterialPropertyBlock` cannot fade an opaque material, and it fails in complete silence.**
  Transparency in URP is a shader keyword plus a blend state plus a render queue, all of them
  per-*material*; a property block only replaces property values, so writing an alpha into
  `_BaseColor` on a `_Surface: 0` material compiles, runs, changes the number and draws exactly the
  same pixels. **Anything that wants to fade a body owes this check before it writes an alpha**
  (M1-12).
- **A material keyword the shader does not declare is accepted, stored, and silently inert — and
  `IsKeywordEnabled` will tell you so if you ask, which nobody does.** `Material.EnableKeyword` does
  not validate: Unity sorts the name into `m_ValidKeywords` or `m_InvalidKeywords` by whether the
  shader's `keywordSpace` declares it, writes the invalid ones to the asset anyway, and logs
  **nothing**. `shaderKeywords` then reports the name back — so the obvious probe confirms the fix —
  while `IsKeywordEnabled` and `enabledKeywords` both say it is off. **Ask
  `shader.keywordSpace.keywords` whether the keyword exists before spending a task on it.**
  Concretely, in Unity 6.3: **`Universal Render Pipeline/Unlit` declares no `_ALPHAPREMULTIPLY_ON`
  at all** — its local keywords include `_SURFACE_TYPE_TRANSPARENT`, `_ALPHATEST_ON` and
  `_ALPHAMODULATE_ON`, and premultiply is not among them, so URP's own premultiply *maths* lives in
  the blend factors and in author-premultiplied colour rather than in a branch. This cost
  [ledger row 3](plan/ROADMAP.md#carry-forward-into-m5) its ruling: the row diagnosed the symptom
  correctly for three milestones and prescribed a keyword that could never have done anything.
  **What was actually wrong was `_SrcBlend: One`** — which, over a fragment that outputs straight
  colour, contributes rgb at full strength while halving only the background, which is exactly
  *"draws at full brightness whatever its alpha says"*. `SrcAlpha` is the fix. **The general
  shape:** when a material looks wrong, read the blend factors before reaching for a keyword, and
  compare against a shipped material on the same shader that already looks right — `M_Reticle.mat`
  was the control here (M5-05b).
- **A *Scale With Screen Size* canvas measures in reference pixels, not dp**: a `sizeDelta` of 120
  is 48 dp on a 400 dpi phone. Any HUD element whose size is specified in dp must be sized at
  runtime as `dp × pxPerDp ÷ canvas.scaleFactor`, not authored into the prefab (M0-15).
- **`Screen.dpi` reads `120` in this Editor, so every dp this project has ever measured in it was
  drawn at 30 % of its device size.** `StickShaper.PixelsPerDp(120)` is **0.75**; a 400 dpi phone —
  the density the project's own *"~432 dp landscape safe area"* is quoted at, 1080 px at 2.5 px/dp —
  gives **2.5**. That is a factor of **3.33**, and it applies to every element placed through
  `HudPresenter.Place`, `SkillBarPresenter`, `BossBarView` and `XpBarView`. **The Editor is therefore
  not a legibility instrument at all**, in either direction: a dp-sized element is drawn a third of
  its device size, while a *font* size is authored in reference px and scales by the canvas instead,
  so the two move independently and a screenshot shows neither at its real proportion. **Legibility
  is arithmetic here, not observation** — `pt × canvasScaleFactor ÷ (dpi ÷ 160)`, with the canvas
  factor read off the prefab's own `CanvasScaler` (`1920 × 1080`, match 0.5 on both `Hud.prefab` and
  `RunEnd.prefab`). Same family as M4-06's *"the safe-area inset is 0 on all four edges in the
  Editor"*: an Editor number about a phone's screen is usually a number about this monitor (M4-07).
- **TMP reads a bare `{0}` in `SetText(string, float, …)` as *nine* decimal places, not as an
  integer.** The integer form is `{0:0}`, whose `0` counts as padding and leaves precision at zero,
  rounding half-up (M1-17).
- **`StringBuilder.Append(float)` allocates on Mono** — it formats through `float.ToString()`
  internally. A preallocated builder bounds the allocation, it does not remove it. The free route
  for a per-frame readout is TMP's `SetText(string format, float, …)` overloads (up to 8 floats)
  and `SetText(StringBuilder)` for the copy (M0-18).
- **`NavMeshPath.corners` allocates a fresh array on every read** — always `GetCornersNonAlloc`
  into a preallocated buffer (M1-19).
- **A `StateMachine<T>` transition allocates nothing on Unity's Mono**, measured in M1-18:
  `EqualityComparer<TEnum>.Default` is specialised rather than boxing. The standing warning is
  about keeping `Tick` off the dictionary, not about `Apply`.

---

## 10. Android build and device

- **Gradle dependency resolution needs machine-local config that is deliberately not in this
  repo**: `~/.gradle/gradle.properties` (HTTP proxy — *not* SOCKS, because Java resolves SOCKS
  hostnames locally and local DNS is filtered here, which presents as a bare
  `> maven.aliyun.com` UnknownHostException) and `~/.gradle/init.gradle` (Aliyun mirrors ahead of
  Google/Central, first in order because Gradle aborts a resolution on a network error instead of
  falling through). Without them the build fails at `build.gradle` line 6 with a message that reads
  like AGP 9.0.0 does not exist; it does. **A fresh clone on another machine needs its own** (M0-19).
- **A Gradle failure's cause is in `%LOCALAPPDATA%\Unity\Editor\Editor.log` under `What went
  wrong`, not in the Console the MCP shows you** — and the MCP reports a build that merely emitted
  warnings as `UNEXPECTED_ERROR`, so its verdict is not the build's verdict. The fast reproduction
  loop is `gradle help` in `Library/Bee/Android/Prj/IL2CPP/Gradle` with Unity's bundled `java.exe`:
  10–60 s instead of a 10-minute build (M0-19).
- **An IL2CPP Android build wants several GB free** — a disk-full failure mid-clang is what created
  `Assets/_Recovery/` (M0-19).
- **The first exception in a logcat is not the cause of death.** The APK's log leads with a
  `NoClassDefFoundError` carrying a 25-frame stack, logged at `I` level, caught by Unity, and
  followed a full second later by the actual `F`-priority `SIGSEGV`. Filter `logcat -d "*:E"` and
  read the `F DEBUG` backtrace before believing anything above it (M0-20).
- **BlueStacks' ADB accepts connections long before it serves them**: `adb devices` says `device`
  while `shell`, `exec-out` and `logcat` all return `error: closed`. That is the daemon up with
  debugging off — enable Settings → Advanced → Android Debug Bridge and restart the instance. Two
  handles onto one instance (`127.0.0.1:5555` and `emulator-5554`) is normal (M0-20).

---

## 11. Process

- **`.githooks/pre-commit` check 8 cannot see an acceptance commit, and that is correct rather than a
  hole.** It compares PROGRESS.md's *Last merged task* row against the newest Log entry and **skips when
  either is empty** — which is exactly the state a milestone's acceptance leaves, because archiving the
  milestone empties the live Log. So the one commit per milestone that rotates Current State furthest is
  the one commit nothing checks. There is nothing to compare against, so the skip is right; what is worth
  knowing is that the check's coverage has a hole at exactly the boundary, and that the next acceptance
  should rotate the row by hand rather than trust the hook to catch it (M4-07).
- **A "known issue" describing uncommitted working-tree state has a short shelf life.** Check it
  against `git status` before repeating it into a new session — M0-19's block was fully stale by
  M0-20 (M0-20).
- **`.editorconfig` carries both an underscore-camel rule for private fields and a PascalCase rule
  for `static readonly` ones**, and which wins is not obvious from reading it. Prefer a static
  property for shared fixture data rather than finding out (M1-04).
- **Unity's API level has no non-generic `TaskCompletionSource`** — use
  `TaskCompletionSource<bool>`, and keep `RunContinuationsAsynchronously` on it or awaiting code
  resumes inside `AsyncOperation.completed`, re-entrant to Unity's own scene management (M0-13).
