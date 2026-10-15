# UI-11h — The home screen's art

**Size:** M, **split into three before it starts** — UI-11h-a (the art, imported, and the tab bar wearing it), UI-11h-b (the place cards), UI-11h-c (the hero cards) · **Depends on:** UI-11g · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 (the colour law: cyan is the player, no red or gold buttons), owner's UI style ruling of 2026-09-29 (Clash of Clans / Clash Royale) · **Ledger rows:** none

## Goal

The home screen stops being flat `Palette` rectangles: the tab bar is a stone plate with three icon tiles, a place card is a picture in a stone frame with a lock on a locked one, and a hero card sits in a matching portrait frame.

## The art, and where each piece goes

Cut from the owner's six Gemini sheets in `Soulvail_GeminiSamples/UI 4/` (`1.jpg` plate, `2.jpg` place frames, `3.jpg` tab tiles, `4.jpg` hero frames, `5.jpg` Jungle, `6.jpg` unknown place) by a new `UI_v4/cut_sheets.py` beside `UI_v4/png/`, in `cut_sheets.py`'s style (`key_magenta`, then despill; the pictures are not keyed). **Thirteen pieces, all under `Art/UI/Home/`.** None goes under `Icons/`: that folder holds bare glyphs, and a tab tile is a plate with its glyph on it.

| Piece (size as cut) | From | Worn by | Wired to |
|---|---|---|---|
| `TabBar_Plate` (1000×240; 9-slice `x/0/x/0`, ≈ 270 each end, set at the cut by eye against the rail's first plain pixel) | `1.jpg` | the bar's own `Image` | `Menu.unity`: the `Image` on the TabBar object, its sprite; `TabBar._plate` |
| `TabBar_Heroes` · `_Play` · `_Places` (off) and `…On` (lit), 6 pieces, one shared canvas per pair | `3.jpg` | each tab's tile `Image` | `TabBar._icons[0..2]` (off), `TabBar._iconsOn[0..2]` (lit) |
| `PlaceCard_Frame` and `PlaceCard_FrameSelected` (≈ 1000×300 incl. glow; 9-slice on all four sides, ≈ 80, set at the cut) | `2.jpg` | `PlaceList` cards: `Frame` and `Rim` | `PlaceList._frame`, `PlaceList._frameSelected` |
| `PlaceCard_Jungle` · `PlaceCard_Soon` (1200×492, opaque, no border) | `5.jpg`, `6.jpg` | the card's picture (`Plate`) | `Jungle.asset` `_picture` (`ModeDefinition`); `PlaceList._soonPicture` |
| `HeroCard_Frame` and `HeroCard_FrameSelected` (≈ 500×555 incl. glow; 9-slice all four sides, ≈ 110, set at the cut) | `4.jpg` | `HeroGrid` cards: `Frame` and `Rim` | `HeroGrid._frame`, `HeroGrid._frameSelected` |
| **No new lock.** `CharacterPage_LockBadge` (exists) | — | the padlock on a locked place card, 36 dp, top right, as on a hero card | `PlaceList._lockSprite` (new field); `HeroGrid._lockSprite` is already wired |

**A pair shares one canvas.** The selected tile and selected frames carry a cyan glow that spills past the body; the off/normal piece is cut into the same canvas size, body centred, transparent padding, so swapping the sprite moves nothing.

---

## UI-11h-a — The art, imported, and the tab bar

### Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Home/*.png` (13) | — | the pieces above, with hand-built `.meta`s from `Button_Blue.png.meta` |
| `Game/Presentation/TabBar.cs` | Game | wears the plate and the tiles; `HeightDp` 72 |
| `Tests/Game/Art/HomeArtTests.cs` | Tests.Game | the import rules |
| `Tests/Game/Presentation/HomeScreenTests.cs` | Tests.Game | the tab bar rows (TabBar has no test file of its own) |
| *small edits* | | `Menu.unity`: the bar's plate `Image`, each tab's tile `Image` and label placement, the new `TabBar` fields; the four `_blue…_greenPressed` fields go |

### Public API

```csharp
// TabBar — block namespace, MonoBehaviour (Traps §5). Signatures only.
public const float HeightDp = 72f;        // was 64; HeroGrid and PlaceList read it
public const float TextDp = 12f;          // was 18; the label sits under the tile
public const float IconDp = 44f;          // the tile's height, glow included

[SerializeField] private Image _plate;                         // the bar's own Image, sprite TabBar_Plate
[SerializeField] private Sprite[] _icons = new Sprite[3];      // off: Heroes, Play, Places
[SerializeField] private Sprite[] _iconsOn = new Sprite[3];    // lit, same order
// removed: _blue, _bluePressed, _green, _greenPressed
```

`Show`, `Configure`, `Layout`, `Selected` and `Current` keep their signatures.

### Behaviour

1. **`Art/UI/Home/` holds exactly the thirteen pieces** and nothing else.
2. **Each piece is byte for byte what `UI_v4/cut_sheets.py` wrote** (ignored when the samples folder is absent), edges despilled, not edited by hand.
3. **Each imports as one full-rect sprite at 100 PPU**, clamped, bilinear, alpha as transparency, no mipmaps. UI pieces ASTC 4×4 on Android; the two pictures ASTC 6×6, no alpha, `maxTextureSize` 2048.
4. **Borders:** the plate `x/0/x/0`; both place frames and both hero frames carry their border on all four sides; tiles and pictures none.
5. **Each on/off pair has the same pixel size.**
6. **The plate is the bar's background**, stretched to the bar's width with its end caps at their natural proportion: `Image.type` Sliced, `pixelsPerUnitMultiplier` = plate height ÷ bar height in canvas units (the formula `Dress` already uses for the old button).
7. **The three tabs sit between the caps**, three equal columns of the width left once both caps are taken (a cap's width is the plate's border × the bar's height ÷ the plate's height). **A tab's tile is 44 dp tall, `preserveAspect`, centred in its column, with its label under it** (12 sp, at least the 12 sp UI-11e rule 6 asks for). **A tab's tap area is still its whole column and the full bar height** (`Graphic.raycastPadding` on the tile), never less than 48 dp either way.
8. **The lit tab wears its `…On` tile and `Palette.HudText`; the others wear the off tile and `Palette.Neutral`.** `Show` and `Layout` both redress.
9. **A press tints the tile** (`Button.transition` ColorTint, pressed ≈ 0.8 grey); there is no pressed sprite.
10. **`TabBar.HeightDp` is 72**, and `HeroGrid` and `PlaceList` already read it, so their scroll areas end above the taller bar.
11. **It fits 9:20 at 411 × 914 and 360 × 780 dp**: three tiles and labels do not overlap each other or a cap.

### Tests

| Test | Given / When / Then |
|---|---|
| `HomeArt_IsTheThirteenPieces` | rule 1 |
| `HomeArt_IsByteForByteTheCut` | rule 2, per piece |
| `HomeArt_ImportsAsAUiSprite` | rule 3, per piece |
| `HomeArt_HasItsBorders` | rule 4 |
| `HomeArt_PairsShareACanvas` | rule 5, three tile pairs and two frame pairs |
| `TabBar_Plate_ScalesItsCapsToTheBarHeight` | rule 6 |
| `TabBar_Tabs_SitBetweenTheCaps_TileAndLabelFit` | rule 7, both phone sizes |
| `TabBar_Tap_StillTheWholeColumn` | rule 7, `raycastPadding` reaches the column and is at least 48 dp |
| `TabBar_Show_LightsOneTab` | rule 8 |
| `TabBar_Press_Tints_NotSwaps` | rule 9 |
| `TabBar_Height_ScrollAreasEndAboveIt` | rule 10: `HeroGridTests`' and `PlaceListTests`' existing viewport rows hold at 72 |
| `TabBar_FitsPortrait_BothSizes` | rule 11 |

### Manual verification

1. **[Editor]** Play the Menu, `Soulvail/Capture Look` at **411 × 914** and at **360 × 780**: the home screen with the plate and the three tiles, Play lit. Look for the end caps crowding a tile.
2. **[Editor]** Tap Heroes, then Places, then Play: the lit tile moves and the Heroes and Places pages end above the bar.
3. **[device]** Deferred (PROGRESS lists it): thumb size on a real phone.

---

## UI-11h-b — The place cards

### Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/PlaceLookBook.cs` | Game | place id → picture; `CharacterLookBook`'s shape |
| `Game/Presentation/PlaceList.cs` | Game | wears the frame, the picture, the lock |
| `Tests/Game/Authoring/PlaceLookBookTests.cs` | Tests.Game | the book's rows |
| `Tests/Game/Presentation/PlaceListTests.cs` | Tests.Game | the card rows |
| *small edits* | | `ModeDefinition.cs`: `[SerializeField] private Sprite _picture` and a `Picture` read; `Jungle.asset`: `_picture` = `PlaceCard_Jungle`; `BootInstaller.cs`: builds and registers the `PlaceLookBook` from the mode definitions, as `BuildCharacterLookBook` does; `MenuPresenter.cs`: `Construct` takes `PlaceLookBook places = null` and passes it to `PlaceList.Configure`; `Menu.unity`: `PlaceList._frame`, `_frameSelected`, `_lockSprite`, `_soonPicture`. *Ripple:* the `MenuPresenter.Construct` and `PlaceList.Configure` callers in tests take the new optional argument. |

### Public API

```csharp
// PlaceLookBook — plain C#, file-scoped namespace, no UnityEngine.Object base.
public sealed class PlaceLookBook
{
    public PlaceLookBook(IReadOnlyDictionary<ContentId, Sprite> pictures);
    public Sprite For(ContentId place);   // null for a place with no picture
}

// PlaceList
public void Configure(ContentCatalog catalog, ILocalizer localizer, PlaceLookBook pictures = null);
[SerializeField] private Sprite _frame;
[SerializeField] private Sprite _frameSelected;
[SerializeField] private Sprite _lockSprite;     // CharacterPage_LockBadge
[SerializeField] private Sprite _soonPicture;    // PlaceCard_Soon

public const float FrameBorderDp = 20f;   // the frame's drawn border, however thick the sprite's own
public const float GlowPadDp = 6f;        // the frame is drawn this far outside the card, for the glow
public const float PictureTuckDp = 12f;   // the picture runs this far under the frame, so no gap shows
public const float ScrimAlpha = 0.6f;

// PlaceList.Card gains:
public Image Frame { get; }      // the normal frame, always on
public Image Lock { get; }       // the padlock; on for a locked card
// Rim stays: it is now the SELECTED frame, drawn over Frame while this is the pick.
// Plate stays: it is now the picture (flat colour when there is none).
```

### Behaviour

1. **An open card shows its place's picture**, filling the frame's opening: `Plate` carries the sprite from `PlaceLookBook.For(id)`, white, and runs `PictureTuckDp` under the frame.
2. **The frame is `_frame` over every card, 9-slice with a drawn border of `FrameBorderDp`** (`pixelsPerUnitMultiplier` = the sprite's border ÷ `FrameBorderDp` × px), outset by `GlowPadDp`. The card's 140 dp slot is unchanged: the outset overlaps the gap and the margin, never the neighbour.
3. **The picked place wears `_frameSelected`** on `Rim`, over `Frame`, white (the sprite carries the cyan, `Palette.Player`). Every other card's `Rim` is off.
4. **A locked card's picture is tinted `Palette.NodeLocked`, `Lock` is on at the top right (36 dp), and the status line is as today** ("Finish {0} to open"). It is still not interactable and shows no pressed state.
5. **The "more to come" card shows `_soonPicture` at `SoonAlpha`**, no lock, normal frame, not interactable.
6. **A place with no picture** (a null from the book, or no book) **falls back to today's flat plate colour.** It never throws.
7. **The words sit on a scrim**: a `Palette.Ink` strip at `ScrimAlpha` behind the three text bands, drawn over the picture and under the frame, so a bright picture never costs readability. The words themselves, their order and their colours are unchanged.
8. **`PlaceLookBook`** returns the picture it was given for an id and null for an id it was not; its constructor refuses a null dictionary and a `default(ContentId)` key, and copies the dictionary.
9. **It fits 9:20 at 411 × 914 and 360 × 780 dp**; a card is still 140 dp tall.

### Tests

| Test | Given / When / Then |
|---|---|
| `OpenCard_ShowsItsPicture` | rule 1 |
| `Card_Frame_IsSlicedAndOutset` | rule 2 |
| `OpenCard_Picked_ShowsSelectedFrame` · `OpenCard_Unpicked_RimOff` | rule 3 (the existing `OpenCard_Picked_HasRim` becomes the first) |
| `LockedCard_TintedAndLocked_NotTappable` | rule 4 (extends `LockedCard_NamesOpener_NotTappable`, same test-only second mode) |
| `SoonCard_ShowsSoonPicture_NoLock` | rule 5 |
| `Card_NoPicture_FallsBackToFlat` | rule 6, with and without a book |
| `Card_Scrim_SitsBetweenPictureAndFrame` | rule 7 |
| `Book_ReturnsPicture_NullForUnknown` · `Book_Guards` | rule 8 |
| `List_FitsPortrait` (existing, both sizes) · `Card_StaysOneHundredFortyDp` | rule 9 |

The existing rows that read `Plate.color` change to the new values (open white, locked `NodeLocked`, soon at `SoonAlpha`); that is the same rule under new art, not a new rule.

### Manual verification

1. **[Editor]** `Soulvail/Capture Look` on the Places tab at **411 × 914** and **360 × 780**: the Jungle card shows its picture in the frame, wears the selected frame when it is the pick, and the "More places coming" card reads as fog under it. Check the white name text against the picture and the scrim.
2. **[Editor]** The locked card **cannot be seen with one place**: it is covered by its test only, until a second place exists. To look at it early, temporarily set the Jungle's `_unlockedBy` to a place the profile has not finished, then undo.
3. **[Editor]** The glow on the selected frame is not clipped at the screen edge or under the title.

---

## UI-11h-c — The hero cards

### Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HeroGrid.cs` | Game | wears the portrait frame |
| `Tests/Game/Presentation/HeroGridTests.cs` | Tests.Game | the card rows |
| *small edits* | | `Menu.unity`: `HeroGrid._frame`, `HeroGrid._frameSelected` |

### Public API

```csharp
// HeroGrid
[SerializeField] private Sprite _frame;           // HeroCard_Frame
[SerializeField] private Sprite _frameSelected;   // HeroCard_FrameSelected
public const float FrameBorderDp = 18f;
public const float GlowPadDp = 6f;

// HeroGrid.Card gains: public Image Frame { get; }
// Rim stays, and is now the SELECTED frame over Frame, as on PlaceList. RimDp goes.
```

### Behaviour

1. **Every card wears `_frame`**, 9-slice with a drawn border of `FrameBorderDp`, outset by `GlowPadDp`; the card's size (`CardRatio` 1.3) is unchanged.
2. **The picked card wears `_frameSelected` on `Rim`**, white, over `Frame`; the others have `Rim` off.
3. **The plate keeps its flat colour** behind the emblem, inside the frame; the emblem, the lock, the name, the price, the deed and the "picked" line keep their order and colours, inset to clear the frame's opening (`FrameBorderDp`, where `rim * 2` was).
4. **A locked card is as today** (emblem greyed at `LockedAlpha`, padlock on, price and deed) inside the normal frame.
5. **It fits 9:20 at both sizes**: two columns, no overlap.

### Tests

| Test | Given / When / Then |
|---|---|
| `Card_Frame_IsSlicedAndOutset` | rule 1 |
| `Card_Picked_ShowsSelectedFrame` · `Card_Unpicked_RimOff` | rule 2 (the existing rim row becomes the first) |
| `Card_Plate_AndContents_ClearTheFrame` | rule 3 |
| `LockedCard_Unchanged_InsideTheFrame` | rule 4 |
| `Grid_FitsPortrait` (existing, both sizes) | rule 5 |

### Manual verification

1. **[Editor]** `Soulvail/Capture Look` on the Heroes tab at **411 × 914** and **360 × 780**: the Ranger's card in the selected frame, the emblem inside the opening.
2. **[Editor]** **A locked hero card cannot be seen today**: the focus build lists the Ranger alone ([focus build switches](../PROGRESS.md)). Rule 4 is covered by its test only, until the roster is widened.

---

## Out of scope

- **A second place's picture, or a Descent picture.** Descent is unlisted; a place added later authors `_picture` on its own `ModeDefinition` and nothing else.
- **A lock cut from the sheets.** `CharacterPage_LockBadge` is reused; a place-specific lock is a later art pass.
- **A heroes' picture per class** beyond the emblem each card already shows.
- **Rewriting `HomeScreen`'s own place and class cards** (UI-11e): they are not on the list and keep the flat look until the owner says.
- **Changing any `Palette` colour**, or the Menu background.
- **Audio, animation or a press effect on a card.**

## Rulings (owner, 2026-10-15)

1. **Three tasks, not one**: a, b, c as above; c can wait.
2. **`TabBar.HeightDp` 64 -> 72**, as recommended. The Capture Look decides whether it stays.
3. **The plate stays, caps and all.** If the Capture Look shows them crowding the tiles, the fallback is the tiles without a plate (`_plate` null), not a recut.
4. **The existing `CharacterPage_LockBadge` is the place lock.**
5. **The Heroes tile stays a knight helmet**, though the Ranger is the only listed class.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`). Deviations from the above with their reasons, or "as specified"._

### UI-11h-a

Built as specified, with these facts and three deviations.

**The pieces, as cut** (`Soulvail_GeminiSamples/UI_v4/cut_sheets.py`, `key_magenta` and `despill` copied from `UI_v3`; the pictures resized, not keyed). The biggest blobs of each sheet are taken, so a stray speck is not a piece.

| Piece | Size | 9-slice (L/B/R/T) |
|---|---|---|
| `TabBar_Plate` | 1000×243 | 270 / 0 / 270 / 0 |
| `TabBar_Heroes` · `…On` | 320×349 | none |
| `TabBar_Play` · `…On` | 320×348 | none |
| `TabBar_Places` · `…On` | 320×350 | none |
| `PlaceCard_Frame` · `…Selected` | 1000×295 | 100 on all four sides |
| `HeroCard_Frame` · `…Selected` | 500×555 | 120 on all four sides |
| `PlaceCard_Jungle` · `…Soon` | 1200×492 | none |

1. **The place frame's border is 100, not the spec's ≈ 80, and the hero frame's 120, not ≈ 110.** Set by eye against the cut: the corner stones, with the selected frame's glow, end 100 and 120 px in. A smaller border stretches the stone's last pixels along the rail. Both pairs share their border (a test row), so a swap slices alike. UI-11h-b and -c read the border off the sprite, as the spec's formulas do, so nothing downstream names a number.
2. **Rule 8's colours are the label's, not the tile's.** `Palette.HudText` and `Palette.Neutral` colour the tab's label; the tile stays white so the press tint (rule 9) multiplies a clean sprite. The off tab's label is `Palette.Neutral`, `#6E6A63`, a dark grey on the blue plate: legible in the capture at 411 but faint, and the owner's call whether to brighten it.
3. **The Look was a throwaway EditMode test, not `Soulvail/Capture Look`:** the overlay canvas put in world space at the phone's dp size, an orthographic camera at 2×, `Logs/look_<w>x<h>.png`, deleted after. The Home page's render-texture emblem drew white, so only the bar is a judgement.

**The metas** are hand-built from `Button_Blue.png.meta` (Unity rewrote them on import; those are committed): full rect, 100 PPU, clamped, bilinear, no mipmaps, ASTC 4×4; the pictures ASTC 6×6, `alphaUsage` none, 2048.

**The tab bar's geometry.** A cap is 80 dp; the column is 83.7 dp at 411 and 66.7 at 360. The tile is 44 dp tall with its 12 dp label in an 18 dp band under it. `Layout` sets the tile's rect itself. Menu.unity gained the bar's `Image`, the sprites and ColorTint; the four `_blue…_greenPressed` fields went.

**The Look, 411 × 914 and 360 × 780:** the plate reads as a stone bar, three tiles between its two cyan-ringed caps, Play lit with a cyan glow that stays inside the plate's top edge. **No overlap at either size. At 360 the caps are close to the outer tiles**: the Heroes and Places tiles stand about 10 dp from the cap's inner stone. That is crowding by a small margin, not a collision; nothing was recut (ruling 3). At 411 there is room to spare.

**Tests:** `HomeArtTests` (5 rows) and eight `TabBar_…` rows replace `TabBar_OnSafeBottom_PlaySelected`'s 64 dp asserts. 4 226 EditMode: 1 failed (`CharacterPreviewTests.Resize_…`, D3D12), 1 inconclusive, +51. **Not done:** manual steps 2 and 3.

### UI-11h-b

As specified, with four deviations.

1. **`Descent.asset` is a sixth file**: `Descent_EveryYamlKeyBindsToAField` reserialises it and failed until the new `_picture: {fileID: 0}` line was in it. Unity wrote it.
2. **`Card.Scrim` and `PlaceList.LockDp` (36) are public additions**; `RimDp` is gone.
3. **`MenuPresenter.Construct` is an `[Inject]` method**, and `PlaceLookBook` is registered at the root, so the `= null` only serves tests ([Traps §6](../../Traps.md) concerns constructors).
4. **The text bands rise `PictureTuckDp` + 2 dp and sit `PictureTuckDp` further in**: the frame covers 14 dp of the card's edge, so the old 6 dp bands would sit under stone.

**Geometry (dp):** frame outset 6, border drawn 20, so the opening is 14 in; the picture is 12 under it. `pixelsPerUnitMultiplier` = `border.x ÷ (20 × px)` (5 at 1×). Padlock 36, 18 in from the corner.

**The Look, 411 × 914 and 360 × 780** (throwaway test; deleted). Jungle: the picture fills the frame, the name readable on the scrim. Picked: cyan glow clear of the edge and title. "More to come": a fogged ruin, dim but legible. A test-only locked card: greyed, padlock top right. Not recut: the picture stretches about 15 % wide at 411.

**Tests:** 2 book rows, 8 new and 2 rewritten card rows. **4 236 EditMode: 4 234 passed, 1 failed (`CharacterPreviewTests.Resize_…`, D3D12), 1 inconclusive** (+10).

### UI-11h-c

As specified, with two notes.

1. **The plate is inset by `FrameBorderDp − GlowPadDp` (12 dp), not left at the card's edge.** The frame is outset 6 and draws 18 thick, so it covers the card's outer 12 dp; the plate starts where the stone ends, and the emblem, words and padlock sit 4 dp inside it (was `rim × 2` and `× 3`). `Card.Frame` is public, `RimDp` gone. Draw order: `Plate`, `Frame`, `Rim` (the rim was behind the plate).
2. **`Card_Frame_IsSlicedAndOutset` compares offsets per component with a tolerance:** `Is.EqualTo(Vector2)` failed on `-6.00` vs `-6.00` (float noise).

**The Look, 411 × 914 and 360 × 780** (throwaway test, deleted). The Ranger's card, one card at the left: the cyan selected frame over the stone one, the glow clear of the screen edge and the title, the dark flat plate in the opening, "Ranger" white and "Picked" cyan inside it, clear of the stone. **No emblem drew**: the harness passed no `CharacterLookBook`, so the opening is empty and the emblem inside it is still unseen; the owner's Play shows it. Not recut: the stone's bottom edge shows a faint dashed line under the frame.

**Tests:** 5 rows replace `Card_Picked_HasRimAndLabel`. **4 240 EditMode: 4 238 passed, 1 failed (`CharacterPreviewTests.Resize_…`, D3D12), 1 inconclusive** (+4).
