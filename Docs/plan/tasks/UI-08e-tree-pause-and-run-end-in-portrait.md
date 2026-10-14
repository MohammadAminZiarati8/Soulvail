# UI-08e — The tree, Pause and Run end in portrait

**Size:** M · **Depends on:** UI-08a, UI-07c, UI-07d · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** — · **Ledger rows:** none

## Goal

The three overlay screens a Jungle run shows — the tree, Pause and Run end — fit a portrait phone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeViewPresenter.cs` | Game | the card below the block, both full-width, in portrait |
| `Game/Presentation/PausePresenter.cs` | Game | its column sized from the safe width |
| `Game/Presentation/RunEndPresenter.cs` | Game | its column sized from the safe width |
| `Tests/Game/Presentation/TreeLayoutTests.cs` | Tests.Game | the portrait rows added to UI-07d's fixture |
| `Tests/Game/Presentation/OverlayLayoutTests.cs` | Tests.Game | Pause and Run end |
| *small edits* | | `TreeView.prefab`, `Pause.prefab`, `RunEnd.prefab`: `CanvasScaler` 1080 × 1920 |

## Behaviour

1. **Each screen's canvas is referenced 1080 × 1920**, match 0.5.
2. **The tree in portrait:** the popup takes the safe width less 16 dp a side; the class block above, the detail card below it, Learn under the card. UI-07d's rules hold — the block centred, the branch names 8 dp clear of the card, nothing overlapping.
3. **Pause and Run end** lay out as one centred column no wider than the safe width less 16 dp a side; every button 48 dp tall or more and inside the safe area.
4. **All three fit 1080 × 2400 @ 420 dpi and 720 × 1600 @ 320 dpi**; text clears 12 sp.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_Portrait_StacksBlockCardLearn` | rule 2, both sizes |
| `Tree_Portrait_KeepsUi07dRules` | rule 2, both sizes |
| `Pause_Portrait_FitsTheSafeWidth` | rule 3, both sizes |
| `RunEnd_Portrait_FitsTheSafeWidth` | rule 3, both sizes |
| `Overlays_Canvas_IsReferencedPortrait` | rule 1 |
| `Overlays_Portrait_TextClears12Sp` | rule 4 |

## Manual verification

1. **[Editor]** UI-07b's captures of each screen at both sizes.

## Out of scope

The Pause and Run-end art (UI-09), and the screens switched off in the focus build (Sanctum, Skills, Splash, LevelUp).

## As built

_Filled at merge._
