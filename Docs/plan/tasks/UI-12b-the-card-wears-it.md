# UI-12b — The card wears it

**Size:** S · **Depends on:** UI-12a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** owner's ruling of 2026-10-15 (every hero card gets an avatar), UI-11h-c (the portrait frame) · **Ledger rows:** none

## Goal

A hero card on the Heroes tab shows its class's portrait inside the frame where the emblem was; a locked card greys it under the padlock, and a class with no portrait still shows its emblem.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HeroGrid.cs` | Game | the portrait in the opening; the emblem as its fallback |
| `Tests/Game/Presentation/HeroGridTests.cs` | Tests.Game | the portrait rows |
| *small edits* | | none: the portrait arrives on `CharacterLook` through the `CharacterLookBook` `HeroGrid.Configure` already takes |

## Public API

```csharp
// HeroGrid.Card
public Image Emblem { get; }   // unchanged name; now draws the portrait when there is one, the emblem when not
public bool ShowsPortrait { get; internal set; }

// HeroGrid
public const float PortraitTopDp = 4f;   // the portrait stops this far under the frame's opening
```

No new `[SerializeField]`, so `Menu.unity` does not change.

## Behaviour

1. **A card whose class has a portrait draws it in the `Emblem` image**: `CharacterLook.Portrait`, white, `preserveAspect`, filling the opening's width (no `inner` inset at the sides) and standing on the name band — its rect runs from the name band's top to `PortraitTopDp` under the opening's top, and its pivot and the sprite's alignment are bottom-centre, so the shoulders sit on the name and a square portrait in a taller rect leaves its spare height above the head. `ShowsPortrait` is true.
2. **A class with no portrait** (a null `Portrait`, or no `CharacterLookBook`) **draws today's emblem exactly as UI-11h-c lays it out**, and `ShowsPortrait` is false. No `Emblem` at all leaves the image off, as today. It never throws.
3. **A locked card's portrait is greyed** — `Palette.NodeLocked` at `LockedAlpha`, the colour today's locked emblem wears — **and the padlock is on, over it**, at the top right as today. The price and deed lines are unchanged. The draw order is plate, portrait, padlock, frame, rim, so the padlock is never under the frame's stone and the portrait never over it.
4. **The picked card is as UI-11h-c drew it**: `_frameSelected` on `Rim`, "Picked" in cyan. The portrait does not change with the pick.
5. **The words do not move**: the name, "Picked", the price and the deed keep their bands; the portrait only takes the space the emblem had, widened to the opening.
6. **It fits 9:20 at 411 × 914 and 360 × 780 dp**: two columns, no portrait outside its card's opening.

## Tests

| Test | Given / When / Then |
|---|---|
| `Card_WithPortrait_ShowsIt_StandingOnTheName` | a look with a portrait / `Show` / `Emblem.sprite` is the portrait, white, `ShowsPortrait`; its rect's bottom is the name band's top and its sides are the opening's (rule 1) |
| `Card_NoPortrait_FallsBackToTheEmblem` | a look with an emblem and no portrait, and no book at all / `Show` / the emblem, laid out as before; `ShowsPortrait` false; no throw (rule 2) |
| `LockedCard_PortraitGreyed_PadlockOver` | a locked class with a portrait / `Show` / `Emblem.color` is `NodeLocked` at `LockedAlpha`, `Lock` on, and `Lock`'s sibling index is above `Emblem`'s (rule 3) |
| `Card_Picked_ShowsSelectedFrame` (existing) | holds with a portrait (rule 4) |
| `Card_Plate_AndContents_ClearTheFrame` (existing) | holds; the name and price bands are where they were (rule 5) |
| `Grid_FitsPortrait` (existing, both sizes) | plus: every `Emblem` rect lies inside its card's opening (rule 6) |

## Manual verification

1. **[Editor]** Play the Menu, open Heroes, `Soulvail/Capture Look` at **411 × 914** and **360 × 780**: the Ranger's head and shoulders fill the frame's opening over its name, in the cyan selected frame. Look for the hood touching the frame's top stone, and for the name reading cleanly under the shoulders.
2. **[Editor]** **A locked hero card cannot be seen today**: the focus build lists the Ranger alone. Rule 3 is covered by its test only, until the roster is widened; to look early, widen the class-select list `BootInstaller.BuildClassSelectRoster` reads for the check and put it back.

## Out of scope

- **The home screen's own class card** (UI-11e, drawn by `MenuPresenter`): it keeps its emblem. Putting the avatar there is a one-line follow-up if the owner wants it.
- **The character page**: it keeps its live 3D model (UI-12d).
- **A scrim under the name**, as the place cards have: the plate is flat and dark, so the white name reads on it. If the capture shows the shoulders running into the name, a scrim is the fix and the *As built* says so.

## As built

_2026-10-16._ Built to the Files table; `Menu.unity` unchanged. As specified, with these notes.

**Notes**

1. **Draw order needed no change.** `Emblem` and `Lock` were already children of the plate (emblem made first), and `Frame` and `Rim` siblings after it, so plate, portrait, padlock, frame, rim holds by construction; the test asserts the padlock above the portrait.
2. **The portrait's bottom-centre alignment is the rect's pivot**, `(0.5, 0)`: `Image.preserveAspect` places a sprite by its rect's pivot, so the sprite's import alignment does not matter. The emblem fallback sets the pivot back to centre.
3. **Tests.** Three new rows (`Card_WithPortrait_ShowsIt_StandingOnTheName`, `Card_NoPortrait_FallsBackToTheEmblem`, `LockedCard_PortraitGreyed_PadlockOver`); `Card_Picked_ShowsSelectedFrame`, `Card_Plate_AndContents_ClearTheFrame` and `Grid_FitsPortrait` now run with portraits, and `Grid_FitsPortrait` also asserts every portrait inside its card's opening. The fixture's `Enable` takes an optional `CharacterLookBook`; the portraits are generated 8 × 8 sprites, so the rows do not depend on the baked PNGs.

**Manual verification (by eye, in the open Editor)**

A throwaway test rendered the real `Menu.unity` Heroes tab to a PNG at 411 × 914 and 360 × 780 (2x), with all four classes on the roster and the real baked portraits; it was removed after. `Capture Look` itself needs Play mode and the Device Simulator, not driven here.

1. **The Ranger's portrait** sits in the cyan selected frame, head and shoulders over its name, the bow's limb entering at the right, "Picked" below. The name reads cleanly; the shoulders end on the name band's top edge with no overlap.
2. **A locked class** (the Gravecaller and the Emberwright, rendered with the roster widened in the harness only) shows the Knight's portrait as a dark grey ghost, the padlock over it at the top right, then the name, deed and price.
3. **The Knight's helmet edge reads.** The peak sits a few dp under the opening's top (`PortraitTopDp` 4 gives a clear gap), not touching the stone; no scrim under the name was needed, so none was added.
4. **Not seen:** the home screen's own class card (kept its emblem, out of scope), and any of this on a phone.
