# M0-13 — `BootScope`, `RunScope`, `SceneLoader`, `BootFlow`; Boot/Menu/Run scenes

**Size:** M · **Depends on:** M0-12 · **Branch:** `m0-13-scopes-and-scenes`
**Design refs:** AR §3, §7; ADR-0002

## Goal

The game has an entry point: a persistent root scope created before any scene, three scenes in the build, and a loader that moves between them — so pressing Play from *any* scene works and the Boot scene hands off to Menu on its own.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Composition/BootScope.cs` | Game | Root `LifetimeScope`; wraps `BootInstaller` |
| `Game/Composition/RunScope.cs` | Game | Run `LifetimeScope`; wraps `RunInstaller` (scene refs added in M0-16) |
| `Game/Composition/SceneLoader.cs` | Game | Async single-scene loads by name |
| `Game/Composition/BootFlow.cs` | Game | `IStartable`: frame-rate, Boot → Menu |
| **Assets** | | |
| `Prefabs/Composition/BootScope.prefab` | — | The root scope prefab, referenced by VContainer settings |
| `Settings/VContainerSettings.asset` | — | `RootLifetimeScope = BootScope.prefab` |
| `Scenes/Boot.unity`, `Scenes/Menu.unity`, `Scenes/Run.unity` | — | Build order 0, 1, 2 |

**Removed:** `Assets/Scenes/SampleScene.unity`, `Assets/Settings/SampleSceneProfile.asset` (and metas); the empty `Assets/Scenes/` folder.

## Public API

```csharp
namespace Soulvail.Game.Composition;

public sealed class BootScope : LifetimeScope
{
    [SerializeField] private CharacterDefinition[] _characters;
    protected override void Configure(IContainerBuilder b)
    {
        BootInstaller.Install(b, _characters);
        b.Register<SceneLoader>(Lifetime.Singleton);
        b.RegisterEntryPoint<BootFlow>();
    }
}

public sealed class RunScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder b) => RunInstaller.Install(b);
}

public sealed class SceneLoader
{
    public const string Boot = "Boot", Menu = "Menu", Run = "Run";
    public string Active { get; }                       // SceneManager.GetActiveScene().name
    public Task LoadAsync(string sceneName);            // LoadSceneMode.Single; completes when loaded
}

public sealed class BootFlow : IStartable
{
    public BootFlow(SceneLoader loader);
    public void Start();
}
```

## Behaviour

1. `BootScope.prefab` is set as `RootLifetimeScope` in `VContainerSettings.asset`. VContainer instantiates it before the first scene loads and marks it `DontDestroyOnLoad`. Every scene `LifetimeScope` without an explicit parent gets it as parent automatically — this is what makes "press Play in the Run scene" work.
2. `BootScope` serializes the `CharacterDefinition[]` (just `Oathbound.asset` for now). The prefab is the *only* place content assets are referenced for the container.
3. `BootFlow.Start`: sets `Application.targetFrameRate = 60`; then **only if** `loader.Active == "Boot"**, calls `loader.LoadAsync(SceneLoader.Menu)`. From any other scene it does nothing — so Play-in-Run is not hijacked.
4. `SceneLoader.LoadAsync` wraps `SceneManager.LoadSceneAsync(name, Single)` in a `TaskCompletionSource`, completing on `AsyncOperation.completed`. Loading an unknown scene faults the task with `ArgumentException`.
5. `Boot.unity`: a camera and a Canvas with a TMP label "Soulvail" so a cold build shows something for the frame before Menu loads. No scope in the scene (the root comes from settings).
6. `Menu.unity`: camera + empty Canvas (content in M0-17). `Run.unity`: camera + a `RunScope` GameObject (content in M0-16).
7. Build settings list exactly Boot (0), Menu (1), Run (2), all enabled.

## Tests

None in this task — everything here needs scenes. The PlayMode smoke test "Boot reaches Menu" lands in M0-17 once Menu exists as a real screen.

## Manual verification (Editor)

1. Press Play in `Boot` → within a second the active scene is `Menu`; Console has no errors; a `BootScope` object exists under `DontDestroyOnLoad`.
2. Press Play in `Run` → stays in `Run`; `BootScope` still exists; `RunScope` built without errors.
3. Stop Play; `SampleScene` is gone from the Project window and from Build Settings.

## Acceptance

- [ ] Manual steps verified — **owner, in Play mode; not verifiable from the MCP**
- [x] Zero errors, zero new analyzer warnings
- [x] Every new asset has its `.meta`; deleted assets' metas are gone (hook enforces)
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Menu UI — M0-17. Run scene contents — M0-16. Camera behaviour — M0-18.
- Scene transitions with fades or loading screens.

## As built

Files are under `Assets/_Project/` per this table's header — so the new `Settings/` and `Scenes/`
folders sit beside `Core/`, `Game/`, `Data/`, not beside the URP `Assets/Settings/`.

**Deviations, behaviour first:**

1. **`SceneLoader` validates against the build list, not `Application.CanStreamedLevelBeLoaded`.**
   That API is the obvious one for rule 4 and is inert here: it answered `false` for all three
   scenes, by name *and* by full path, with `SceneManager.sceneCountInBuildSettings == 3` and the
   paths correct. A guard built on it rejects every load there is. `IsInBuild` walks
   `SceneUtility.GetScenePathByBuildIndex` instead and compares both forms.
2. **`LoadAsync` accepts a scene's name or its full asset path**, as `SceneManager` itself does.
   Name-only would have made the "not in Build Settings" message a lie for a path.
3. **`BootFlow` observes the load task** with a `ContinueWith(…, OnlyOnFaulted)` that logs, rather
   than discarding it. A discarded faulted task surfaces from the finalizer thread, if at all.
4. **`ProjectSettings.asset` gains `VContainerSettings` in `preloadedAssets`.** Not in the table,
   and rule 1 is false without it: `VContainerSettings.Instance` is set from `OnEnable`, which in
   the Editor only fires via `LoadInstanceFromPreloadAssets` at `BeforeSceneLoad`. The package's
   own Create menu item does this; `AssetDatabase.CreateAsset` alone leaves an asset that looks
   right and does nothing.
5. **`Mobile_RPAsset` and `PC_RPAsset` repointed to `DefaultVolumeProfile.asset`** (owner
   approved). Both used `SampleSceneProfile` as `m_VolumeProfile`, so this task's removal would
   have left two dangling references in the render pipeline assets. Costs the template's look
   (Bloom 0.25, Vignette 0.2, Neutral tonemapping → neutral/off); irrelevant to a grey-box
   skeleton, and M2+ art's call.
6. **TMP Essential Resources imported** — 44 assets under `Assets/TextMesh Pro/` (owner approved).
   `TMP_Settings.instance` was null, so rule 5's label could not exist. One-time project setup
   M0-17 and M1-17 need regardless; M0-13 is just the first task to hit it. This forced a
   seventh file outside the table: **`.gitattributes` gains `"Assets/TextMesh Pro/**"
   -whitespace`**, because TMP's shaders carry trailing whitespace and space-before-tab indents
   that made the pre-commit hook refuse the commit. Editing vendored files to satisfy the hook
   would be reverted by the next TMP reimport.
7. **`BootScope` and `RunScope` use block namespaces**, against this file's Public API block —
   the M0-11 rule for every `UnityEngine.Object`-derived type.
8. Beyond "a camera and a Canvas": Boot's camera clears to solid black (it is a splash), and both
   Canvases are `ScaleWithScreenSize` at 1920×1080, match 0.5 — the landscape-mobile default,
   without which the Boot label renders at a fixed pixel size on a phone.

**Not deviations, but worth knowing:** no `Directional Light` in any scene (nothing is lit yet;
M0-16 owns the Run scene's contents), and no `EventSystem` on either Canvas (nothing is
interactive yet; M0-17 owns it).

**Verified:** compile clean, 131/131 EditMode green, meta integrity clean, `LoadAsync` rejection
paths and `Active` exercised through the real object, prefab and settings YAML checked for the
M0-11 null-`m_Script` failure. **Not verified:** anything requiring Play mode — rules 1, 2, 3 and
the Boot → Menu hand-off are the owner's manual steps below.
