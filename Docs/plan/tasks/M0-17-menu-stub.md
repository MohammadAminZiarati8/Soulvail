# M0-17 — `MenuScope`, `MenuPresenter`, Descend button, PlayMode smoke test

**Size:** S · **Depends on:** M0-13 · **Branch:** `m0-17-menu-stub`
**Design refs:** AR §3, §7, §15

## Goal

The player can get from a cold start into a run by tapping one button, and a PlayMode test proves the Boot → Menu handoff so nobody ships a build that opens on a black screen.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Composition/MenuScope.cs` | Game | Menu `LifetimeScope`; registers the presenter |
| `Game/Presentation/MenuPresenter.cs` | Game | Descend button → `PendingRun` → load Run |
| `Tests/PlayMode/Soulvail.Tests.PlayMode.asmdef` | Tests.PlayMode | Non-editor test assembly; refs Core, Game, VContainer, test runner, nunit |
| `Tests/PlayMode/BootSmokeTests.cs` | Tests.PlayMode | Boot reaches Menu |
| **Assets** | | |
| `Scenes/Menu.unity` | — | *modified:* `MenuScope`, Canvas with title label and a **Descend** button |

## Public API

```csharp
namespace Soulvail.Game.Composition;

public sealed class MenuScope : LifetimeScope
{
    [SerializeField] private MenuPresenter _menu;
    protected override void Configure(IContainerBuilder b) => b.RegisterComponent(_menu);
}
```

```csharp
namespace Soulvail.Game.Presentation;

public sealed class MenuPresenter : MonoBehaviour
{
    [SerializeField] private Button _descend;
    [Inject] public void Construct(PendingRun pending, ContentCatalog catalog, SceneLoader loader);
}
```

## Behaviour

1. On `Descend`: `pending.Set(catalog.Characters[0].Id, seed: Environment.TickCount)`; disable the button (no double-tap); `await loader.LoadAsync(SceneLoader.Run)`.
2. The presenter subscribes to the button in `OnEnable` and unsubscribes in `OnDisable`.
3. The seed is wall-clock derived for now. When Daily mode exists, the mode supplies it.
4. Menu UI: title "Soulvail" (TMP), one button labelled "Descend", ≥ 48 dp tall, centred. Raw strings are acceptable **only** in this stub; M6-10 replaces them with `LocKey`s and this is recorded in the parking lot.
5. The PlayMode test loads `Boot` by name, then polls up to 5 s until `SceneManager.GetActiveScene().name == "Menu"`. It runs only in the Editor's PlayMode runner (scenes must be in build settings, which M0-13 guarantees).

## Tests

| Test | Given / When / Then |
|---|---|
| `Boot_ReachesMenu_WithinFiveSeconds` | `SceneManager.LoadScene("Boot")` / yield until active == Menu or timeout / active scene is `Menu`; no errors logged (`LogAssert.NoUnexpectedReceived`) |
| `RootScope_SurvivesSceneLoad` | in Menu / — / a `BootScope` instance exists and is in the `DontDestroyOnLoad` scene |

## Manual verification (Editor)

1. Play from `Boot` → Menu appears with the Descend button.
2. Tap Descend → Run loads; capsule and grey box present; stick/WASD moves the capsule.
3. Tap Descend twice quickly → only one load happens (button disabled after the first).

## Acceptance

- [ ] PlayMode tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked
- [ ] Parking lot notes the two raw strings to localise in M6-10

## Out of scope

- Class select (M5-07), options, unlocks — later. This menu is one button.
- Any styling beyond legibility.

## As built

_Filled at merge._
