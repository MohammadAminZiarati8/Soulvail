# UI-07a — The look pass's specs

**Size:** S · **Depends on:** UI-06b · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

What the 2026-10-14 look pass found in the Device Simulator becomes written tasks: UI-07, UI-08 and UI-10 in the ROADMAP, a spec for each, and the Simulator's traps in [Traps §9](../../Traps.md#9-rendering-canvas-and-tmp).

## Files

| Path (under `Docs/`) | Purpose |
|---|---|
| `plan/ROADMAP.md` | the UI-07, UI-08, UI-09 and UI-10 rows and their paragraph |
| `plan/tasks/UI-07a…UI-10b` (11) | this spec and ten task specs |
| `Traps.md` | five §9 bullets: the Simulator, its captures and the Run scene's layers |
| `plan/PROGRESS.md` | Current State and the Log entry |

Docs only. No code, asset or `ProjectSettings/` file changes.

## Behaviour

1. **The look pass, as found.** Device Simulator, 2658 × 960 px at 480 dpi (a landscape phone 320 dp tall), 2026-10-14:
   1. the character page does not fit 320 dp: the preview's rect has a negative height and the Ranger never draws, the skill strip overlaps the name band, and the caption overlaps the role label → **UI-08d**;
   2. Menu (title, both buttons), the whole TreeView, Pause and RunEnd draw in LiberationSans SDF, not `LilitaOne_SDF`, which UI-04b asked for → **UI-07c**;
   3. the tree's branch names (Draw, Volley, Stride) are clipped by the detail card's top edge, and the class block sits in the left 55 % with the right side empty → **UI-07d** (landscape), **UI-08e** (portrait);
   4. the menu's background is inside `Safe`, so the notch strip is bare; the class page's covers the full canvas → **UI-08d**;
   5. `MenuPresenter` lays out once, on enable, and never again when the screen size or dpi changes → **UI-08d**;
   6. the `DebugOverlay`'s text draws over the XP strip in development builds → **UI-08c**.
2. **Already known, now owned:** the telegraph rings are squares → **UI-10a**; there are two player cyans → **UI-10b**, which carries the owner's question.
3. **UI-09, the Pause and Run-end art, is a title row** until its Gemini sheet exists.
4. **Every finding is re-checked with the tool UI-07b builds**, not by eye in the Game view, whose `Screen.dpi` is the monitor's ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)).

## Tests

None: docs only.

## Out of scope

Fixing anything; the tasks above do.

## As built

As specified.
