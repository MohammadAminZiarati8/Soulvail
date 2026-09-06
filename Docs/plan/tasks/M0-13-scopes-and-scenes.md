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

- [ ] Manual steps verified
- [ ] Zero errors, zero new analyzer warnings
- [ ] Every new asset has its `.meta`; deleted assets' metas are gone (hook enforces)
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Menu UI — M0-17. Run scene contents — M0-16. Camera behaviour — M0-18.
- Scene transitions with fades or loading screens.

## As built

_Filled at merge._
