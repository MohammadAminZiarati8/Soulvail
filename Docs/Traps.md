# Soulvail — Traps

**Things in this toolchain that lie to you.** One entry per trap: the symptom, why, what to do.
Rules the *code* depends on live in [Invariants.md](Invariants.md) (AR §18); this file
is about probes, tools and the Editor. **Read §1 first** — most of the rest are instances of it.

---

## 1. An API that accepts a value and echoes it back has not agreed to honour it

| Asked | Answered | Did |
|---|---|---|
| `AndroidArchitecture.X86_64` | reads back set | build throws late — Unity 6.3 cannot build x86-64 Android |
| ABI list advertising `arm64-v8a` | `pm install` succeeds | install ≠ run; BlueStacks crashes on launch |
| `GC.GetAllocatedBytesForCurrentThread()` / `GC.CollectionCount(0)` | 0 / barely moves | inert on Unity's Mono |
| `Application.CanStreamedLevelBeLoaded` | `false` for every scene | inert in the Editor |
| `SerializedProperty.objectReferenceValue = x` | success | stores `None` if the handle went fake-null |
| `Material.EnableKeyword(k)` for an undeclared `k` | `shaderKeywords` lists it | filed under invalid keywords, changes nothing |

**Rule: before trusting a probe's "no", prove it says "yes" to something true.** Seed a control.
For remote artifacts, probe one that certainly exists first (Google Maven 404s everything on this
connection, including its own index).

---

## 2. Shell probes on this machine

Seed a file that must match *and* one that must not. These all failed toward good news.

- **CR probes:** `grep -c $'\r'`, `od -c | grep '\r'` and `CR=$(printf '\r'); grep "$CR"` are all
  wrong. Count bytes in Python (`open(p,'rb').read().count(b'\r')`) or ask `git ls-files --eol`.
- **`grep -E '[ \t]+$'`** — no `\t` in GNU ERE; it means `\` or `t`. Use a literal tab. `grep -P` is
  unavailable.
- **`grep "new TypeName("` misses target-typed `new(`.** Use grep to scope; let the compiler list
  call sites.
- **Python on Windows rewrites LF as CRLF** via `read_text`/`write_text`. Use bytes, or
  `newline="\n"`. `dotnet format whitespace --verify-no-changes` catches it.

---

## 3. The unfocused Editor

**An unfocused Editor does not tick, yet one-shot `RunCommand`s keep working — it looks alive.**

| Operation | Unfocused |
|---|---|
| `TestRunnerApi.Execute` (EditMode and PlayMode) from `Unity_RunCommand` | **completes** — the reliable route |
| Normal compile/import after file changes | drains |
| `RequestScriptCompilation`, asmdef reimport | never drains |
| Play mode you drive yourself | freezes after ~40 frames; `EditorApplication.Step()` doesn't advance it — drive from a PlayMode test |
| `SceneView.Repaint`, `EditorApplication.delayCall` | never fire (`EditorApplication.update` does) |
| Unity CLI `run_tests` | can run nothing, time out, and wedge the pipeline server until a recompile |

- **Did it compile?** `isCompiling` and DLL timestamps both lie. Use the new API from a
  `RunCommand` (`typeof(NewType)`); if it compiles and runs, the Editor has the code.
- **Compiling without the Editor:** copy `Library/Bee/artifacts/<dag>.dag/<Assembly>.rsp`, repoint
  `-out`/`-refout` and own-assembly references to `Temp/`, run
  `dotnet "<Editor>/Data/DotNetSdkRoslyn/csc.dll" @scratch.rsp`. Real analyzers, real errors.
  Running tests still needs the Editor.
- **Format gate:** `dotnet format whitespace --folder --verify-no-changes --include <files>`. A
  folder needs a trailing slash or it checks 0 files and exits 0 — read *Formatted n of m*.
- **Edit-mode `Time.time` reads 0 when unfocused.** No test may assert it either way; `Assume` it
  and write the sibling row.
- When a blocked row bites, ask the owner to focus Unity.

---

## 4. The Unity MCP

### `Unity_RunCommand`

- **Shape:**
  ```csharp
  internal class CommandScript : IRunCommand
  {
      public void Execute(ExecutionResult result) { result.Log("output"); }
  }
  ```
  Wrapped in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`: no `public` on the
  class, avoid `using`, fully qualify. Output only through `result.Log` (which ignores format
  specifiers — pre-format). Helper types go **top-level and `internal`**, never nested (the rewriter
  duplicates nested types → `CS1527`).
- **The wrapper namespace shadows short names**: `Image` (→ `Unity.AI.Image`), `Canvas`,
  `RenderMode`, `CompilationPipeline`. Fully qualify or alias.
- **Refusals (`UNEXPECTED_ERROR: User interactions are not supported`) name no line and the list
  shifts between sessions.** Seen refused: `File.Delete`, `File.Move`, `AssetDatabase.DeleteAsset`,
  and `RegisterCallbacks` + `Execute` + `System.IO` in one command. `DeleteAssets` (plural) and
  `Refresh` usually pass. **Bisect by deleting half the body.** Overwrite files with a sentinel
  instead of deleting them. Don't build scaffolding until a one-line probe is refused.
- **Unauthorized namespaces:** `System.Net`, `System.Diagnostics`, `System.Runtime.InteropServices`,
  `System.Reflection` (incl. `BindingFlags`). Drive components through public API.
- **No VContainer and no `System.Numerics` reference.** Can't name `LifetimeScope`/`RunScope` or a
  signature with `Vector2/3` (types merely *containing* them are fine). To dress a scope field:
  `Type.GetType("Soulvail.Game.Composition.RunScope, Soulvail.Game")` (assembly-qualified),
  `root.GetComponentInChildren(type, true)`, then `SerializedObject.FindProperty`.
- **Driving the test runner:**
  - One `Execute` per command; re-entering `Execute` from `RunFinished` is silently ignored. To
    loop, count down in `SessionState` and call `Execute` from an `EditorApplication.update` handler.
  - Two runs queued in one command both get the first run's callbacks.
  - Never reimport and `Execute` in one command — the reload drops your callbacks.
  - Old `ICallbacks` sinks stay registered and fire again on later runs; key results per submission.
  - Entering Play reloads the domain: fields reset, `SessionState` survives.
  - `CreateInstance<TestRunnerApi>()` in a local is collected mid-run and `RunFinished` never
    fires: `hideFlags = HideAndDontSave` and root api + callbacks in a `static`.
  - `testNames` wants full test names; a fixture is `groupNames`. A wrong filter runs 0 and reports
    green.
  - **Easiest tally:** Unity writes
    `%USERPROFILE%/AppData/LocalLow/<Company>/<Product>/TestResults.xml` every run. Check
    `start-time` — a stale file looks finished.
- **`AssetDatabase.SaveAssets()` reloads the domain**; a probe that changes data must restore it in
  the same command.

### `Unity_GetConsoleLogs`

Filtering `logTypes: "Log"` returns nothing; plain logs come back typed `Info` on an unfiltered
call. A PlayMode run clears the Console — sweep right after the run you mean to describe.

### Captures

- Scene view draws its own skybox; Screen Space Overlay canvases don't render into `Camera.Render`.
- `Unity_Camera_Capture` on a play-mode camera fails ("No GameObject found").
- CLI `capture_game_view --save_path` resolves relative paths under `Assets/` (delete the `.meta`).
- `ScreenCapture` reads the Game view, not the Device Simulator. With both open, `Screen.*` mixes
  their values — close the Game view while measuring in the Simulator.
- URP render-to-texture: `RenderPipeline.SubmitRenderRequest` with
  `UniversalRenderPipeline.SingleCameraRequest`. UI rendered over a transparent clear reads back
  premultiplied.
- An edit-mode animation pose doesn't survive to the next command. To see an animation, use Play.

---

## 5. Unity Editor and the asset pipeline

- **`MonoBehaviour`/`ScriptableObject` need block namespaces.** File-scoped compiles but links no
  `MonoScript`: every referencing asset loads null, silently.
- **Every asmdef needs `csc.rsp` with `-langversion:10`** (per assembly). An asmdef with no `.cs`
  produces no assembly.
- **`CS8773 file-scoped namespace … C# 9.0` on a new file** means it compiled into
  `Assembly-CSharp` (stale script map after a branch switch). Restart the Editor.
- `CompilationPipeline.GetAssemblies` = last compile; `GetAssemblyNameFromScriptPath` = live map.
- **No `File.Move(src, dst, overwrite)`** at this API level. Use `File.Replace(tmp, target, null)`
  when the target exists, else `File.Move`.
- **`JsonUtility`**: `ulong`/`float` exact; `{}` → all defaults; malformed → `ArgumentException`;
  properties are invisible (a `readonly struct` serialises as `{}`).
- **Saving a scene/prefab re-serialises things you didn't touch.** Check `git status` after any
  Editor-driven edit.
- **Unity backfills `ProjectSettings/`.** `TimeManager.asset` re-serialises whenever the Editor
  opens the project (a no-op): `git checkout -- ProjectSettings/TimeManager.asset`, never
  `ALLOW_PROJECT_SETTINGS`. Check `git diff ProjectSettings/` before every commit.
- **Opening the Device Simulator adds a package to `Packages/manifest.json`.** Revert it (and
  `packages-lock.json`).
- **`OnValidate` runs on add, before fields are filled**, and on every `ApplyModifiedProperties`.
  Warn, never `LogError` (tests fail on unexpected errors). Reload-time `Transform` refs are
  fake-null.
- `[Min]`/`[Range]` clamp the Inspector only; real guards live in core constructors.
- **A `UnityEngine.Object` handle can go fake-null across an unrelated save.** Load right before
  assigning; read back after saving.
- **After a reimport, an asset field and `LoadAssetAtPath` can be two wrappers of one object:**
  `Is.SameAs` fails with the same name both sides. Compare with `==`, or `RequestScriptReload`.
- Before deleting a template asset: `grep -rl <guid> Assets ProjectSettings`.
- A primitive cylinder ships a `CapsuleCollider`; scaled, it needs a `MeshCollider`.
- Vendored packages need `"path/** -whitespace"` in `.gitattributes`.
- **Animation:** imported clips don't loop by default (set `loopTime` via a whole
  `clipAnimations` array from `defaultClipAnimations`). `Animator.Play` evaluates only while
  `speed ≠ 0` — zero the speed *after* the evaluating `Update`.
- **NavMesh:** `BuildNavMesh()` in a preview scene bakes but doesn't register — bake in a temporary
  additive scene. `SamplePosition` at an obstacle finds a phantom floor under walkable tops; test
  carving with `NavMesh.Raycast` or `CalculatePath`. `Collider.bounds` is zero outside a physics
  scene.
- **URP strips `_EMISSION` from a material whose GI flags have no emissive bit.** Use
  `RealtimeEmissive`.
- **Blender FBX ids use Python's `hash()`**: run Blender with `PYTHONHASHSEED=0` or every re-export
  diffs.

---

## 6. VContainer

- **Only what it constructed is disposed.** `RegisterInstance` / `RegisterComponent` are never
  disposed — register resource-owning adapters as *types*.
- `As<T>()` alone hides the concrete type; add `.AsSelf()`.
- `RegisterInstance` is always Singleton (still scope-local in a child scope).
- **C# default parameter values are never honoured** — scope build throws. Adding a defaulted
  parameter is a registration change: `WithParameter("name", value)` with the default as a `const`.
- `RegisterComponent` injects; `RegisterInstance` doesn't.
- Injection order vs `Awake` is unordered: check "was I injected?" in `Start`. Prefabs from
  `IObjectResolver.Instantiate` get `Awake`/`OnEnable` before injection — subscribe in `[Inject]`.
- A `Func` registration is lazy (side effects at first `Resolve`).
- `VContainerSettings` must be in Preloaded Assets or every scope silently becomes its own root.
- Don't give `RunScope` a parent reference in the Run scene — it breaks Play-in-Run.

---

## 7. Tests, NUnit, allocation

- **Allocation:** use `AllocationAssert.None` (`Tests/Core/Support/`), never a hand-rolled probe.
  It can't capture a `stackalloc` span (use a heap array). No `RecordingEvents` inside it (boxes);
  grow and `Clear` recording fakes first; invalidate caches inside the measured body; prove the
  probe live once.
- **UI draw paths can't be allocation-free** (`ToString`, `TMP_Text.text`). TMP `SetText(fmt,
  float…)` allocates in the Editor only — measure frames where nothing is written.
- **Two copies of a fact: write the one the code reads**, or the fixture passes vacuously.
- **`Awake` never runs in EditMode** — make what it does reachable another way, or there's no
  coverage. `Object.Destroy` in edit mode logs an error; use `DestroyImmediate`.
- **PlayMode runner has already cold-started its own scene**; entry points must react to
  `sceneLoaded`.
- `Assert.Throws<T>` is exact-type; `Assert.Catch<T>` is assignable. `Has.Count` reflects on the
  runtime type (arrays fail) — assert `.Count` directly.
- Identical string literals are interned (equality proves nothing); non-ASCII expected literals
  can't catch encoding faults.
- A serialized field whose initialiser equals the shipped value makes "asset carries X" vacuous —
  prove binding with `ForceReserializeAssets`.
- **`Soulvail.Tests.Core` can't read assets.** Pin the number as a `private const` there and open
  the asset in `Soulvail.Tests.Game`; the pair meets at the number.
- **Content sweeps:** assert supersets over your own ids, never totals.
- Don't pin FSMs to frame counts — tick until the state arrives.
- `onClick.Invoke()` bypasses `interactable`; assert the flag.
- `ref` returns need `ref` on both sides. `new FixedRandom(99)` is a seed; `99f` is a scripted value.
- **Prove a sealed collaborator wasn't asked** by feeding it an input that throws when read, with a
  control on the path that must read it.
- **Process-wide hooks**: `TaskScheduler.UnobservedTaskException` sees the MCP's websocket faults —
  read the exception before believing a red `SaveWriterTests` row.
- `Temp/` is wiped by Unity mid-session; keep evidence in `Logs/`.
- **Known flakes, re-run before bisecting:**
  - A save-store row on a Windows file lock (`IOException … file to be replaced`) — re-run alone.
  - One animator-clock row is inconclusive by construction.
  - `RangerSandboxTests.Loop_ShootsOnlyStandingStill` fails when Unity isn't the active
    application (see §8).
- `RenderTexture.depth` may report 32 when the constructor requested 24 on D3D12; test the
  required minimum depth rather than an exact backend allocation (`CharacterPreviewTests.Resize_…`).
- Assembly-level `[PrebuildSetup]`/`[PostBuildCleanup]` only work in assemblies referencing
  `UnityEditor.TestRunner`; put them per fixture.
- **PlayMode never touches real saves:** `SaveShelter` moves `run.json`/`profile.json` to
  `persistentDataPath/PlayModeShelter/` and back. A killed pass leaves them there until the next
  PlayMode run restores them.

---

## 8. Input System

- `StickDeadzone` caps magnitude at 1 and rescales; `InputTestFixture` doesn't neutralise it.
- A Value action enabled while already deflected reads zero until the next `InputSystem.Update()`.
- `OnScreenControl.OnDisable`'s base nulls the control — send the final value *before*
  `base.OnDisable()`.
- Synthetic events aren't deterministic with a physical device present. Probe the predicate
  (`EventSystem.RaycastAll`), leave real presses to the owner.
- Don't use `IsPointerOverGameObject(id)` for touch (wrong id → `false`); use `RaycastAll`.
- `InputSystemUIInputModule` auto-assigns `DefaultInputActions`.
- **Gamepad-holding PlayMode rows lose the stick when Unity isn't the foreground app.** The
  fixture uses `IgnoreFocus` and disables `Keyboard`/`Pointer` for the row. It still fails when
  `isApplicationActive` is false (BlueStacks/VS Code steal focus). Queue the final pass from an
  `EditorApplication.update` handler that waits for `isApplicationActive`, while the owner clicks
  into Unity. If it fails, bisect against `dev` — passing alone proves nothing.

---

## 9. Rendering, canvas and TMP

- **A `MaterialPropertyBlock` can't fade an opaque material** — transparency is per material.
- **Before using a keyword, check `shader.keywordSpace.keywords`.** URP Unlit has no
  `_ALPHAPREMULTIPLY_ON`. When a material looks wrong, read the blend factors first (straight
  colour wants `_SrcBlend: SrcAlpha`), and compare to a material that looks right.
- **Scale With Screen Size canvases measure reference px, not dp.** Size dp elements at runtime:
  `dp × pxPerDp ÷ canvas.scaleFactor`.
- **`Screen.dpi` reads 120 in the Editor vs ~400 on a phone** — dp-sized elements draw at 30 %.
  The Editor is not a legibility instrument; compute it. The safe area is 0 in the Editor.
- TMP: bare `{0}` in `SetText(string, float)` is nine decimals; integers are `{0:0}`.
  `StringBuilder.Append(float)` allocates.
- `NavMeshPath.corners` allocates; use `GetCornersNonAlloc`.
- The Run scene's canvases are on `Default`; captures separate UI from the world by disabling
  renderers.

---

## 10. Android build and device

- **Gradle needs machine-local config not in the repo:** `~/.gradle/gradle.properties` (HTTP
  proxy, not SOCKS — local DNS is filtered) and `~/.gradle/init.gradle` (Aliyun mirrors first).
  Without them, the failure looks like AGP doesn't exist.
- The real Gradle error is in `%LOCALAPPDATA%\Unity\Editor\Editor.log` under *What went wrong*.
  The MCP calls a build with warnings `UNEXPECTED_ERROR`. Fast loop: `gradle help` in
  `Library/Bee/Android/Prj/IL2CPP/Gradle` with Unity's `java.exe`.
- IL2CPP builds need several GB free disk.
- In logcat, the first exception isn't the cause; filter `*:E` and read the `F DEBUG` backtrace.
- BlueStacks ADB says `device` while refusing `shell` until ADB is enabled in its settings.

---

## 11. Process

- A "known issue" about working-tree state goes stale fast — check `git status` before repeating it.
- `.editorconfig`'s field-naming rules collide on `static readonly`; prefer a static property.
- No non-generic `TaskCompletionSource`; use `<bool>` with `RunContinuationsAsynchronously`.
