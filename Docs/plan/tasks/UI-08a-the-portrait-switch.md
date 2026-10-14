# UI-08a — The portrait switch

**Size:** S · **Depends on:** UI-07b · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16, CC §2 · **Ledger rows:** none

## Goal

The build runs portrait only, and every doc that says landscape says portrait — the owner's ruling of 2026-09-29, applied.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `ProjectSettings/ProjectSettings.asset` | — | orientation: Portrait, no autorotation (staged with `ALLOW_PROJECT_SETTINGS=1`) |
| `Assets/_Project/Tests/Game/Build/OrientationTests.cs` | Tests.Game | reads `PlayerSettings` |
| *small edits* | | `CLAUDE.md`'s header line; GD's five landscape mentions; CC's one; the ROADMAP UI section's "Landscape today, portrait next" bullet |

## Behaviour

1. **`PlayerSettings.defaultInterfaceOrientation` is `Portrait`**, and autorotation is off on all four sides — not `AutoRotation` limited to portrait, because a phone held upside down must not flip the HUD under the thumbs.
2. **`CLAUDE.md` says "Android, portrait"**, and GD's and CC's landscape sentences are rewritten for portrait; a number derived from a landscape screen (the ~432 dp safe height, the stick's left 45 %) is rewritten from the 9:20 phone or marked for the UI-08 task that re-derives it.
3. **Nothing else changes here.** Screens that misfit portrait stay misfit until UI-08b–e; this task is the switch and the words, so each screen's fix is a task of its own and a capture of its own.

## Tests

| Test | Given / When / Then |
|---|---|
| `Orientation_IsPortraitOnly` | rule 1 |

## Manual verification

1. **[Editor]** The Device Simulator, on a portrait phone, shows the game upright and does not rotate it when the device is rotated.
2. **[device]** The APK opens in portrait and stays there.

## Out of scope

Any layout, camera or canvas change: UI-08b to UI-08e. The application identifier (M8-06).

## As built

**Built.** `ProjectSettings.asset`: `defaultScreenOrientation` Portrait, all four `allowedAutorotateTo*` off (set through `PlayerSettings` in the Editor; `useOSAutorotation` left at 1, inert with every side off). `OrientationTests.Orientation_IsPortraitOnly` reads them. Words: `CLAUDE.md` header; GD §1 platform, §5.2, §7.2, §16.1 headings and open question 2 (now the ruling); CC §2 ergonomic line; the ROADMAP UI bullet. The 45 % stick number (CC §2.1, GD §5.2) and the landscape diagram/HUD positions are marked for UI-08c, not re-derived.

**Judgement calls.** GD §5.2 and §16.1 keep their landscape diagram and table, flagged in the heading, because re-laying them is UI-08c. No Device Simulator was opened: manual step 1 and the device step are owed to the owner.
