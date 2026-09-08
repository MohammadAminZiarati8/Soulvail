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

- [x] PlayMode tests green
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked
- [x] Parking lot notes the two raw strings to localise in M6-10

## Out of scope

- Class select (M5-07), options, unlocks — later. This menu is one button.
- Any styling beyond legibility.

## As built

**Files, seven** — five from the table plus two the table could not skip:

| Path | Note |
|---|---|
| `Game/Composition/MenuScope.cs` | as specced, **block** namespace (M0-11) |
| `Game/Presentation/MenuPresenter.cs` | as specced, **block** namespace; new folder |
| `Tests/PlayMode/Soulvail.Tests.PlayMode.asmdef` | non-editor, `UNITY_INCLUDE_TESTS`, refs Core / Game / VContainer / UnityEngine.UI / UnityEngine.TestRunner + `nunit.framework.dll` |
| `Tests/PlayMode/csc.rsp` | **not in the table.** Sixth asmdef, so `-langversion:10` or its file-scoped namespace does not build (M0-02) |
| `Tests/PlayMode/BootSmokeTests.cs` | the table's two tests plus one more (deviation 2) |
| `Scenes/Menu.unity` | *modified:* `MenuScope`, `EventSystem`, title, Descend button, camera to solid black |
| `Game/Composition/BootFlow.cs` | **not in the table.** *modified:* see deviation 1 — the specced test cannot pass without it |

**Verified.** 153 EditMode tests passed in 2.2 s (unchanged count — nothing here is reachable from EditMode). 3 PlayMode tests passed in 2.3 s, re-run green after the last scene edit:

| Test | |
|---|---|
| `Boot_ReachesMenu_WithinFiveSeconds` | passed, 0.63 s |
| `RootScope_SurvivesSceneLoad` | passed, 0.72 s |
| `Descend_StartsARun_AndRefusesASecondTap` | passed, 0.83 s — beyond the Tests table |

Console: 0 errors, 0 new warnings (the one remaining warning is `com.unity.ai.assistant` failing to refresh an auth token). Scene wiring verified in the saved YAML rather than the Inspector: both `m_Script` GUIDs resolve, `_descend` and `_menu` point at real objects, `parentReference: {TypeName: }` is empty.

**Manual verification.** Steps 1–3 are all covered by `Descend_StartsARun_AndRefusesASecondTap` plus `Boot_ReachesMenu_WithinFiveSeconds`, which is why the third test was added — the run reaches the Menu from Boot, the tap loads Run, and the button is non-interactable before the load finishes. Layout confirmed by scene-view capture: title and button centred, both legible. **Left for the owner:** an actual thumb on an actual touchscreen, and a look at the Boot → Menu → Run transition at real speed.

**Button size:** 480 × 160 reference px, not dp. Rule 4's ≥ 48 dp holds by computation across screen shapes — 53.3 dp at 1280 × 720 / 320 dpi (worst case), 68.1 dp at 1600 × 720 / 280 dpi, 71.6 dp at 2400 × 1080 / 400 dpi, 106.7 dp on the BlueStacks reference.

**Deviations:** six, listed in the PROGRESS entry. The load-bearing one is `BootFlow`.
