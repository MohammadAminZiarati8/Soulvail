# M3-09a — The pause screen: the gate's second holder, and "pause anywhere, instantly"

**Size:** S · **Depends on:** M3-08a (`RunPause`, `PauseReason.Menu`) · **Branch:** `m3-09a-pause-screen`
**Design refs:** GD §5.2, §7.3, §11.4, §16.1; AR §7, §8, §18.1, §18.2; ADR-0004 · **Ledger rows:** 4 (whether a small top-right target is reachable mid-fight with the left thumb on the stick is device-only), 8 (a Menu pause is the *second* thing that makes play time and stopwatch time diverge — rule 10)

## Goal

GD §7.3's *"pause anywhere, instantly"*: a top-right icon stops the run dead, a panel offers the way back out, and `PauseReason.Menu` gets the first caller M3-08a built it for.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/PausePresenter.cs` | Game | the icon, the panel, both halves of the pause, and the way back to the Menu — **block namespace** (Traps §5) |
| `Tests/Game/Presentation/PausePresenterTests.cs` | Tests.Game | over a real `RunSession` and `RunPause`, `LevelUpPresenterTests`' shape |
| `Prefabs/UI/Pause.prefab` | — | the icon and the panel. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Game/Composition/RunScope.cs` + `_pausePresenter` and its `RegisterComponent`, `_levelUpPresenter`'s shape; `Scenes/Run.unity` dressed with the prefab; `InstallerTests` if the field is required |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class PausePresenter : MonoBehaviour
    {
        // [SerializeField] Button _icon; CanvasGroup _panel; Button _resume, _quit;
        // [SerializeField] float _iconSizeDp = 44f; Vector2 _iconMarginDp = new(16f, 16f);

        [Inject] public void Construct(IRunSession session, RunPause pause,
                                       DomainEventHub hub, SceneLoader loader);

        /// <summary>Raises the Menu pause and shows the panel. The icon's own handler.</summary>
        public void Open();

        /// <summary>Lowers it and hides the panel. Resume's handler, and M3-09b/09c's way back.</summary>
        public void Close();

        /// <summary>True while this screen holds the pause — M3-09b and M3-09c's gate.</summary>
        public bool IsOpen { get; }
    }
}
```

## Behaviour

1. **A uGUI `Button`, not the Input System, and that is the opposite of the death overlay's choice for the same reason.** `HudPresenter` reads the tap that dismisses death through `InputAdapter` because *"tap anywhere"* has to include the stick and the Charge button, which a uGUI raycast would have to be layered above. A pause icon is the exact inverse: GD §5.2 puts it top-right and calls it *"a small target, but non-critical and out of the way"*, and the one thing it must never be is tappable anywhere. A `Button` gives that for free, and it leaves `HudPresenter`'s standing claim — the only class in `Soulvail.Game` that reads the Input System — intact.
2. **It holds both halves of the pause, in this file**, exactly as M3-08b rule 3 holds both halves of its own: `RunPause.Pause(PauseReason.Menu)` in `Open`, `Resume(PauseReason.Menu)` in `Close`. A pause raised in one class and lowered in another is how a screen ends up closed over a frozen game, and this is the second screen to make the promise.
3. **It does not gate the tick and must not try to.** `RunTicker` already returns while the pause is held (M3-08a rule 4), so this file raises a flag and draws a panel. Nothing here touches `Time.timeScale` or `targetFrameRate` either — `RunPause` owns both globals (M3-08a rule 13), and a second writer is how one of them stops being restored.
4. **The icon is interactable only when nothing holds the pause.** `RunPause.Pause` throws on a second reason (M3-08a rule 12) and that guard is deliberately loud, so this screen must never be the thing that trips it: the icon's `interactable` follows `!pause.IsPaused`, re-checked in `Open` before the call. In practice M3-08b's screen is a full-screen canvas above the HUD and takes the touches anyway — belt and braces, and the braces are the ones that hold when a later screen forgets its scrim.
5. **No fade, in or out.** GD §7.3 says *instantly*, and `Time.timeScale` is 0 the moment the pause is held, so a scaled animation would freeze half-played and an unscaled one is a second clock (M3-08b rule 4's argument, and the same answer).
6. **The panel is Resume and Quit in this task, and grows by one button per screen.** M3-09b adds **Skills** and M3-09c adds **View Tree**, each as a small edit to this prefab and this file — a button that does nothing is worse than a button that is not there yet, and shipping three dead buttons now would make two later tasks look like they did nothing. Named here so the growth is planned rather than discovered.
7. **Quit lowers the pause before it asks for the scene**, and `RunPause.Dispose` is the backstop rather than the mechanism (M3-08b rule 12's reasoning): VContainer orders no two disposals, so this file must not be the only thing that restores a zeroed `timeScale`, and it must not skip doing so either. `SceneLoader.Menu`, `MenuPresenter.Descend`'s `async void` shape, with the button's interactability taken away first so a second tap cannot start a second load.
8. **Quitting does not delete the run, and that is checkable rather than hopeful.** `SaveWriter` clears the file on `PlayerDied` and deliberately not on `RunEnded` (`SaveWriter.cs:128`), so leaving through this panel ends the session and leaves the last boundary's snapshot on disk — `Continue` resumes from it. This task adds no deletion and no new write. Stated because it is the first way out of a run that is not dying, and a player who quits to the menu and finds their run gone would have no way to tell that from a bug.
9. **It renders one event and otherwise polls nothing.** `PlayerDied` closes the panel and takes the icon away for the rest of the run — death is the one thing that can happen underneath a screen that stops the world, since the tick that killed the player completes before the pause takes effect (M3-08a rule 11), and a pause icon over a death overlay is two screens arguing about whose tap it was. Subscribed in `Construct`, not `OnEnable` (`HudPresenter`'s reason: `RunScope` builds its container from its own `Awake` and Unity orders no two of those). Dropped in `OnDestroy`.
10. **What this costs ledger row 8, said once:** a Menu pause contributes no `Dt` for the same reason a level-up does (M3-08a rule 11), so `RunState.Time` still counts play seconds only and a stopwatch now diverges from it by *two* kinds of interruption rather than one. M3-15 quotes one number and says which; this task adds the second term to the gap.
11. **Placed in dp, like every other touch target.** `SkillButton`'s argument: a Scale-With-Screen-Size canvas measures in reference pixels, and a 44 dp icon authored as 44 of those is a different physical size on every phone. Anchored to the parent's top-right so `SafeAreaFitter` insets it — in landscape the cutout eats exactly one of the two top corners.

## Tests

| Test | Given / When / Then |
|---|---|
| `Icon_OpensAndHoldsTheMenuPause` | a running run / tap the icon / `RunPause.Holder` is `Menu`, the panel is on, `IsOpen` (rules 2, 4) |
| `Open_StopsTheRun` | the row above / the next frame / `RunState.Time` does not advance — the tick is gated by `RunTicker`, not by this file (rules 3, 10) |
| `Resume_LowersItAndTheRunContinues` | paused / tap Resume / panel off, `IsPaused` false, `Time.timeScale` 1, and `RunState.Time` advances again (rules 2, 3) |
| `Icon_IsNotInteractableWhileTheLevelUpScreenHolds` | an offer presented / — / the icon is non-interactable, and `Open()` called directly raises nothing and throws nothing (rule 4) |
| `Icon_ReturnsWhenTheLevelUpCloses` | the row above / choose a card / the icon is interactable again |
| `Open_SetsNoGlobalsItself` | timeScale 1 / `Open` / the presenter wrote neither `Time.timeScale` nor `targetFrameRate` — `RunPause` did, and the values are its (rule 3) |
| `Open_ShowsInstantly` | the panel hidden / `Open` / alpha 1 on the same call; the component carries no `Animator` and starts no coroutine (rule 5) |
| `Panel_HasResumeAndQuitOnly` | `Pause.prefab` / read the fields back after a save (Traps §5) / the icon, the panel and exactly two buttons dressed — the row M3-09b and M3-09c each extend by one (rule 6) |
| `Quit_LowersThePauseThenLoads` | paused / tap Quit / `IsPaused` false and `timeScale` 1 **before** the load is asked for; `SceneLoader` asked for `Menu` (rule 7) |
| `Quit_SecondTapStartsNoSecondLoad` | paused / tap Quit twice in one frame / exactly one load (rule 7) |
| `Quit_LeavesTheRunOnDisk` | a run past one boundary / Quit / `ClearRun` was never called on the store, and the file still decodes — the contrast with death (rule 8) |
| `Death_ClosesAndRetiresTheIcon` | paused, then the player dies / `PlayerDied` / panel off, pause lowered, the icon inactive for the rest of the run (rule 9) |
| `Subscribes_FromConstruct` | a presenter constructed after `RunStarted` / publish `PlayerDied` / it reacts — an `OnEnable` subscription would have missed the hub (rule 9) |
| `Destroy_DropsSubscriptions` | a constructed presenter / `OnDestroy`, then publish / nothing throws, nothing renders |
| `Destroy_WhilePaused_LeavesTheAppRunnable` | the panel up / destroy, dispose the scope / `Time.timeScale` 1 (rule 7) |
| `Icon_IsPlacedInDpFromTheSafeArea` | a canvas with a known scale / `Start` / the rect is `_iconSizeDp × pxPerDp` across and anchored top-right (rule 11) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. Tap the top-right icon mid-wave. Everything stops — enemies frozen mid-step, no telegraph filling, no animation — and the panel appears instantly. Tap Resume: the fight continues from exactly where it stood.
2. **[Editor]** Pause, then Quit. The Menu appears, and **`Continue` is still offered** — the run is on disk (rule 8). Take it: the run comes back at the last boundary.
3. **[Editor]** Level up, and while the three cards are up, try to reach the pause icon. It is not there to tap (rule 4).
4. **[Editor]** Pause, then Stop Play. Press Play again: the Editor's timeline is running and the Menu is not frozen (rule 7).
5. **[device]** Ledger row 4: whether a 44 dp target in the top-right corner is reachable at all mid-fight with the left thumb planted on the stick, and whether *instantly* survives the 30 fps drop `RunPause` asks for. Deferred with the rest.

## Out of scope

- **The Skills list** — M3-09b, which adds its button to this panel.
- **The tree view** — M3-09c, which adds the other one.
- **Options, audio sliders, the accessibility toggles** (GD §18) — M8-03. The panel is Resume and Quit, and the two screens above.
- **Pausing from a hardware back button or the Android lifecycle.** `OnApplicationPause` is a different event with a different contract, and the app has never run outside the Editor (ledger row 4).
- **A confirmation on Quit.** The run survives (rule 8), so there is nothing to confirm; the day quitting forfeits something is the day it needs a dialog.
- **Anything that writes a save.** Quitting ends the session; the last boundary already wrote the file.

## As built

**Built as specced, with four deviations.** `Game/Presentation/PausePresenter.cs` (block namespace), `Tests/Game/Presentation/PausePresenterTests.cs`, `Prefabs/UI/Pause.prefab`, plus the two named small edits — `RunScope._pausePresenter` with its optional `RegisterComponent`, and `Scenes/Run.unity` dressed with the prefab. **`InstallerTests` needed no change, and the Files table's *"if the field is required"* is answered no**: that fixture builds `RunInstaller` (the static half), never the `RunScope` MonoBehaviour, and the field is optional on the HUD's terms.

**Deviation 1 — `SceneLoader` is no longer `sealed`, and `LoadAsync` is `virtual`.** Three Tests-table rows — `Quit_LowersThePauseThenLoads`, `Quit_SecondTapStartsNoSecondLoad`, `Quit_LeavesTheRunOnDisk` — all tap Quit, and `SceneManager.LoadSceneAsync` cannot be driven from an EditMode test without taking the Editor's open scene with it. That is `ResumeFlowTests`' recorded finding and the reason its own two taps are still the owner's manual step, and it would have made all three rows unwritable here. **What made the change necessary rather than convenient is the word *before* in rule 7**: *"`IsPaused` false and `timeScale` 1 **before** the load is asked for"* is not observable from outside the call — everything the presenter does before the load is synchronous and everything after it happens in a scene that no longer exists — so the only place to read it is inside a loader that records instead of loading. `PausePresenterTests.RecordingLoader` captures `Time.timeScale` and `RunPause.IsPaused` at the call. **Rejected: an `ISceneLoader` port** for one method with one adapter, which is a port that exists to be mocked; AR §6's ports all have a second implementation in view and this one never will. The class remarks say all of this in place.

**Deviation 2 — `ResumeFlowTests`' class remarks corrected with it.** They said *"`SceneLoader`, which is `sealed` with no port behind it"*, which stopped being true one deviation ago. The clause was replaced and a paragraph added naming what changed and that those rows were deliberately left alone: they are about what the Menu decides, not about the load.

**Deviation 3 — the panel's two labels are `LocKey`s, not English.** `ui.pause.resume` and `ui.pause.quit` are typed into the prefab, so a player sees a key rather than the word. **The alternative was to type "Resume" and "Quit"**, and it was rejected on a claim already shipped in code: `HudPresenter`'s remarks call raw English in a prefab *"the second and last place in the project where it is allowed"* (its own death overlay, and `MenuPresenter`), and a third would have made a shipped comment false. `OfferCard` set the precedent one task ago for the same reason (ADR-0012, ledger row 9). `Panel_HasResumeAndQuitOnly` asserts both labels start with `ui.pause.` and contain no space, so a later task that types English has to argue with a red row. **It is one prefab edit to reverse** if the owner would rather read words on a device before M3-14a lands.

**Deviation 4 — a temporary Editor helper existed during the session, was deleted before handover, and should never have been written.** `Assets/_Project/Editor/SoulvailTestHarness.cs` wrapped `TestRunnerApi` because three snippets that started the suite came back *"User interactions are not supported for MCP tool calls"*. **The refused call was `File.Delete`, not `TestRunnerApi.Execute`** — [Traps §4](../../Traps.md#4-the-unity-mcp) says the check is pre-execution, the message names neither line nor reason, and *"the innocent-looking call nearest the bottom gets the blame"*, and M3-01b's bullet names `File.Delete` by name. Both "isolating" probes dropped the delete **and** the test call in the same step, so the bisect proved nothing and the wrong conclusion was written down. Re-run with `File.WriteAllText(path, "RUNNING")` — which is exactly what that section tells you to do — the direct `Execute` ran clean, and the last two EditMode runs and the final Console sweep were taken that way, on the tree without the helper. **The helper is not in the handover tree**; no test assembly references `Soulvail.Editor`, so it cannot have changed a row either way. Recorded here rather than quietly dropped because the lesson is not about the MCP — it is that a two-variable bisect is not a bisect.

**Two rules were implemented in a way the spec did not spell out, and both are load-bearing.** **Rule 4's *"follows `!pause.IsPaused`"* is a poll**, one bool per frame in `Update`, and the re-check inside `Open` is not belt-and-braces about a *screen* but about a *frame*: `Update` sets `interactable` once a frame, and `RunTicker.LevelUpPhase` raises the pause mid-frame, so the two genuinely disagree on the frame a level-up opens. Polling the holder rather than subscribing to `OfferPresented`/`LevelUpClosed` is what makes a future third holder retire the icon without this file learning its events. **Rule 2's ordering inside `Close` is pause-then-panel**: if a line between them ever failed, the app is left running with a panel over it, which a player can tap out of, rather than frozen behind a screen that is already gone.

**One row is stronger than the table asked for.** `Quit_LeavesTheRunOnDisk` runs a **real `LocalJsonSaveStore`** in a temp directory behind a counting wrapper, so *"the file still decodes"* is a file on disk decoded through the real adapter rather than a field that was not nulled — **and it then publishes `PlayerDied` on the same store and watches `ClearRun` fire**. Without that control the row is green against a store that never clears anything, which is the failure M3-03's *"green for the wrong reason"* rows were rewritten for.

**Tests: 1 465 EditMode / 0 / 0, twice consecutively on the handover tree**, against M3-08b's 1 447 — **18 new rows**, being the table's 16 names plus two implied guards (the null row, and one non-finite row covering both dp Inspector doors). No new spec type, so no validation row. The non-finite row walks NaN, both infinities, zero and a negative through `_iconSizeDp`, and the same list bar zero through `_iconMarginDp`, **because a zero margin is a legal layout and a zero size is not**. Five runs in all; **one came back 1 464 / 1 and the failure was the MCP's own websocket** caught by `SaveWriterTests.Writer_ObservesEveryFault`, which arms the process-wide `TaskScheduler.UnobservedTaskException` — filed at [Traps §7](../../Traps.md#7-tests-nunit-and-measuring-allocation). PlayMode 14/15 then 15/15 — the known intermittency, in PROGRESS → Known issues. Console sweep taken after the last EditMode run: 59 entries, all known families, zero `CS`/`UNT`/`IDE` diagnostics, zero mentions of this task's types. `ProjectSettings/TimeManager.asset` turned up dirty (Unity's `Fixed Timestep` re-serialisation, `m_TimeScale` correctly 1) and was reverted.

**Manual verification is the owner's** — the five steps above, of which step 5 is deferred with the rest of ledger row 4.
