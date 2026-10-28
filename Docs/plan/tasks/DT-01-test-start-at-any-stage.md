# DT-01 — Test start at any stage

**Size:** M · **Depends on:** — · **Branch:** `dt-01-test-start`
**Design refs:** CH §5.2, AR §18.1 (`RunSession.Start` restore order)

## Goal

A development-only menu panel starts a run at a chosen stage with the XP stages before it would
have paid, banked as unspent points, so the tree opens for picks on the first frame.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Save/TestStart.cs` | Core | builds the `RunSnapshot` for stage N |
| `Tests/Core/Save/TestStartTests.cs` | Tests.Core | rules 1–5 |
| `Game/Presentation/TestStartPanel.cs` | Game | IMGUI stepper + Start, behind a switch |
| *small edits* | | `MenuScope` (optional `_testStart` field, registered when set); `Menu.unity` (one `TestStart` object) |

## Public API

```csharp
public sealed class TestStart
{
    public TestStart(ContentCatalog catalog);
    public float XpBefore(ContentId modeId, int stage);
    public RunSnapshot At(ContentId modeId, ContentId characterId, int seed, RandomState random, int stage);
}
// Game: TestStartPanel : MonoBehaviour, [SerializeField] bool _on; [Inject] Construct(PendingRun,
// SceneLoader, ContentCatalog, MenuPresenter).
```

## Behaviour

1. `XpBefore(mode, n)` sums stages `StartingStage … n−1`: a scripted stage pays the `XpValue` of
   every listed spawn × count; any other stage pays `3 × Scaling.Budget.At(stage)`.
2. `At` turns that XP into level, leftover XP and pending points exactly as `LevelTracker.Grant`
   does against `mode.Xp`; `pendingLevelUps = level − 1`; no nodes taken.
3. `At` writes stage n, the given seed and random state, HP and shield `float.MaxValue` (clamped to
   full by `Health.Restore`), run time 0, `default` economy, four empty Manual slots, empty lists.
4. Stage outside `[StartingStage, FinalStage]` throws `ArgumentOutOfRangeException`.
5. `At(mode, c, s, r, StartingStage)` is a fresh run: level 1, XP 0, nothing pending.
6. The panel draws only when `_on` is ticked **and** `Debug.isDebugBuild`; otherwise it does nothing.
   Start hands `At(...)` for the menu's selected place and class to `PendingRun.Resume` and loads Run.
7. `MenuScope` with `_testStart` empty behaves as before.

## Tests

| Test | Given / When / Then |
|---|---|
| `XpBefore_ScriptedStage_PaysItsSpawns` | mode with stage 1 scripted 3 × enemy(xp 10) / `XpBefore(2)` / 30 |
| `XpBefore_OpenStage_PaysThreeTimesBudget` | unscripted stages 1–2 / `XpBefore(3)` / 3·(B(1)+B(2)) |
| `At_LevelsLikeTheTracker` | XP from stages 1–9 / `At(10)` / level, XP and pending equal a `LevelTracker` granted the same |
| `At_WritesAFullHealthEmptyRun` | / `At(5)` / stage 5, seed, HP max, no nodes, 4 empty slots |
| `At_OutsideTheMode_Throws` | / stage 0 and `FinalStage + 1` / throws |
| `At_FirstStage_IsAFreshRun` | / `At(StartingStage)` / level 1, XP 0, pending 0 |

That such a snapshot restores with the tree open is `RunSessionResumeTests.Offer_IsNotDrawnUntilItIsOpened`
(level 3, 2 pending, no nodes); no new session row.

## Manual verification (Editor / device)

1. **[Editor]** Menu: panel top-left; pick 12, Start → stage 12, tree open with points.
2. **[Editor]** Untick `_on` on `TestStart` → no panel; Play/Continue unchanged.

## Out of scope

- Simulated Essence, Veilrot, Ordeals or picks; profile isolation (a test run saves like any run).

## As built

As specified, with two resolutions:

- The Tests table's session row was dropped before building: `RunSessionResumeTests.Offer_IsNotDrawnUntilItIsOpened` already starts the same shape (level 3, 2 pending, no nodes) with the tree open.
- `Constructor_RefusesANullCatalog` is the implied guard row.

The panel is IMGUI with English labels, `DebugOverlay`'s bargain; the localisation sweep counts `LocKey`s only, so it is untouched. A test run is an ordinary run: it overwrites `run.json` and can record bests and Shards.

Editor probe: Jungle stage 12 started via the panel's `Begin` → `STAGE 12`, level 14, 13 owed, HP 90/90, tree badge 13, the level-14 splash up first. EditMode 4 528 pass, 0 fail, 1 inconclusive.
