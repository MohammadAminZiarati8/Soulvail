# UI-12d — The page shows it

**Size:** M · **Depends on:** UI-12c · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** owner's ruling of 2026-10-15 (the hero page shows everything, stats as numbers; supersedes UI-05's "no stat bars"), UI-05c (the page), UI-08d (the page in portrait) · **Ledger rows:** none

## Goal

The character page keeps its name, role and live 3D model at the top and its one button at the bottom, and between them scrolls through the class's hero sheet: the description, the five stats as numbers, the basic attack, the special move, and all three branches with every rank. The skill strip leaves the page.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HeroSheetView.cs` | Game | the scrolling sheet: builds its rows in code, draws a `HeroSheet` |
| `Game/Presentation/CharacterPage.cs` | Game | the new stack: band, preview, dots, sheet, button row; the columns layout and the strip go |
| `Tests/Game/Presentation/HeroSheetViewTests.cs` | Tests.Game | the sheet's rows |
| `Tests/Game/Presentation/CharacterPageTests.cs` | Tests.Game | the page's layout rows, rewritten |
| *small edits* | | `ClassSelectPresenter.cs`: builds `HeroSheet.Build(...)` for the model where it built the strip; the `_strip` list goes. `ClassSelect.prefab`: the `SkillStrip` object is replaced by a `HeroSheetView` object (its `ScrollRect`, viewport with `RectMask2D`, content), wired to `CharacterPage._sheet`, with `_frame` = `CharacterPage_SkillFrame`, `_placeholder` = `Icon_Placeholder`, `_font` = Lilita One. **Deleted:** `SkillStrip.cs`, `SkillStripTests.cs` and their `.meta`s — `HeroSheet.Build` supersedes `SkillStrip.Build`, and nothing else reads either. *Ripple:* `ClassSelectPresenterTests` rows that read `ClassPageModel.Strip` read `Sheet`. |

## Public API

```csharp
// ClassPageModel — the last parameter changes:
public ClassPageModel(CharacterSpec spec, int index, int count, PageButton button, int price,
    string deedLine, RoleKind role, HeroSheet sheet = null);
public HeroSheet Sheet { get; }   // replaces Strip; StripEntry goes with SkillStrip

// CharacterPage
[SerializeField] private HeroSheetView _sheet;   // replaces _strip
public HeroSheetView Sheet => _sheet;
public const float PreviewShare = 0.36f;         // the preview's share of the page's height
public const float PreviewMaxDp = 300f;          // PreviewMinDp (120) stays
// removed: LayoutColumns, ColumnGapDp, StripWidthDp, StackDp

// HeroSheetView — MonoBehaviour, block namespace (Traps §5).
public sealed class HeroSheetView : MonoBehaviour
{
    public const float SideDp = 16f;        // the sheet's left and right margin
    public const float HeadingDp = 20f;     // a heading's text
    public const float BodyDp = 14f;        // descriptions and stat labels
    public const float TitleDp = 17f;       // an entry's name, a stat's number
    public const float IconDp = 48f;        // an entry's framed icon
    public const float StatRowDp = 30f;
    public const float SectionGapDp = 18f;
    public const float RowGapDp = 8f;

    [SerializeField] private ScrollRect _scroll;
    [SerializeField] private Sprite _frame;          // CharacterPage_SkillFrame
    [SerializeField] private Sprite _placeholder;    // Icon_Placeholder
    [SerializeField] private TMP_FontAsset _font;

    public ScrollRect Scroll => _scroll;
    public IReadOnlyList<Row> Rows { get; }          // in draw order, top to bottom; inactive rows excluded
    public float ContentHeight { get; }              // canvas units

    public void Show(HeroSheet sheet, ILocalizer localizer);   // null sheet: hides
    public void Hide();
    public void Layout(float px);

    public enum RowKind { Description, Heading, Stat, BranchName, Entry }

    public sealed class Row
    {
        public RowKind Kind { get; }
        public RectTransform Root { get; }
        public Image Icon { get; }        // Entry only
        public TMP_Text Title { get; }    // heading text, stat label, branch name, entry name
        public TMP_Text Value { get; }    // stat number; entry's "Rank n"; else null
        public TMP_Text Body { get; }     // description text; null for headings and stats
    }
}
```

## Behaviour

1. **The page is one portrait stack, top to bottom:** the name band with the role (unchanged), the preview between its arrows, the dots row (only when there are several classes; otherwise no space is kept), the sheet, then the button row and the deed line, fixed at the bottom. **The preview is `PreviewShare` of the page's height, between `PreviewMinDp` and `PreviewMaxDp`**; the sheet takes every dp left between the preview (or the dots) and the button row.
2. **The button row does not scroll**: Choose, Unlock or the dead button, the price pill and the deed line stay where UI-08d put them, whatever the sheet's scroll. The sheet's viewport ends above the button row and is clipped (`RectMask2D`), so no row draws over the button.
3. **The sheet draws, in order:** the description; **Stats** and its five rows; **Basic attack** and its entry (omitted with its heading when `HasAttack` is false); **Special move** and its entry (likewise); **Skill tree**, then for each branch its name and one entry per rank. Every text is the localiser's, never a literal.
4. **A stat row** is the label on the left (`BodyDp`, `Palette.HudCaption`) and the number on the right (`TitleDp`, `Palette.HudText`), the number formatted `localizer.Format(stat.Format, stat.Value)`: the Ranger reads Health 90, Damage 13, Attacks/s 2.2, Range 10 m, Speed 3.2 m/s.
5. **An entry row** is the icon in `_frame` (`IconDp`; `_placeholder` when the entry's icon is null) on the left, and beside it the name (`TitleDp`, `Palette.HudText`), "Rank n" right-aligned on the name's line for a tree rank (`BodyDp`, `Palette.HudCaption`; none for the attack and the special move), and the description under them (`BodyDp`, `Palette.HudText`, wrapping). **A row is as tall as its text needs, never shorter than its icon**, measured with TMP's preferred height at the row's width.
6. **No text is under 12 sp, nothing is clipped sideways**, and each text wraps within the sheet's width less `SideDp` each side.
7. **The sheet opens at the top**: `Show` on a class, including stepping to another one, resets the scroll to the top. Scrolling is vertical only, clamped (no elastic overshoot past the content), with inertia.
8. **A drag that starts on the sheet scrolls it and does nothing else**: it neither steps the class nor turns the model. The band still steps and the preview still turns (UI-05c's one-gesture rule, with a third region).
9. **Rows are built once and reused**: a second `Show` reuses the first's row objects and only adds rows when a sheet needs more; rows past the sheet's count go inactive.
10. **The skill strip is gone**: the page has no `SkillStrip` and the presenter builds none. `ClassPageModel.Sheet` is `HeroSheet.Build(spec, catalog, look, icons)` for the class shown.
11. **The columns layout is gone.** It existed for a landscape phone 320 dp tall (UI-07a finding 1); the game has been portrait-only since UI-08a, so the page lays out as the stack at every size.
12. **It fits 9:20 at 411 × 914 and 360 × 780 dp**: the band, the preview, the sheet's viewport and the button row do not overlap, the preview is at least `PreviewMinDp`, and the sheet's viewport is at least 160 dp tall at 360 × 780.

## Tests

| Test | Given / When / Then |
|---|---|
| `Page_Stack_BandPreviewSheetButton_NoOverlap` | both phone sizes / `Layout` / the four rects in that order top to bottom, disjoint (rules 1, 12) |
| `Page_Preview_TakesItsShare_Clamped` | page heights giving under, inside and over the clamp / `Layout` / preview height = clamp(`PreviewShare` × height) (rule 1) |
| `Page_NoDots_KeepsNoSpace` · `Page_Dots_SitUnderThePreview` | count 1, count 3 (rule 1) |
| `Page_ButtonRow_StaysPut_WhenTheSheetScrolls` | a long sheet, scrolled to the bottom / the button's and deed's rects are unchanged, and the viewport's bottom is above the button row's top (rule 2) |
| `Sheet_Ranger_RowsInOrder` | the Ranger's sheet / `Show` / description, Stats heading, five stats, attack heading and entry, special-move heading and entry, tree heading, then three × (branch name, three ranks) (rule 3) |
| `Sheet_NoAttack_OmitsItsHeading` | a sheet with `HasAttack` false / no attack heading or entry (rule 3) |
| `Sheet_Stat_LabelLeft_NumberRight_Formatted` | the Ranger / the texts read "Health"/"90" … "Speed"/"3.2 m/s" (rule 4) |
| `Sheet_Entry_IconNamesRankAndWraps` | a rank entry with a long description / the icon is the entry's, "Rank 2" shown, the row at least `IconDp` and as tall as the description's preferred height (rule 5) |
| `Sheet_Entry_NullIcon_DrawsThePlaceholder` | rule 5 |
| `Sheet_TextAtLeastTwelveSp_NothingWider` | both sizes / every text's font size ≥ 12 sp and its rect inside the viewport's width (rule 6) |
| `Sheet_Show_ResetsToTheTop` · `Sheet_Scroll_VerticalClamped` | rule 7 |
| `Gesture_DragOnTheSheet_NeitherStepsNorTurns` | a drag starting on the viewport / no step, no rotate callback (rule 8; `Gesture_SwipeOnTheBand…` stays for the other two) |
| `Sheet_SecondShow_ReusesRows` | two `Show`s / the same `Row` instances, extras inactive (rule 9) |
| `Presenter_Model_CarriesTheSheet` (in `ClassSelectPresenterTests`) | the Ranger / the shown model's `Sheet` is non-null and has three branches (rule 10) |
| `Page_LookPassSize_Fits` | removed with the columns layout (rule 11); `Page_Portrait_StacksWithoutOverlap` becomes the first row |

## Manual verification

1. **[Editor]** Play the Menu, Heroes, tap the Ranger. `Soulvail/Capture Look` at **411 × 914** and **360 × 780**: the name and role, the Ranger turning on the pedestal, the description and the stats under it, Choose at the bottom.
2. **[Editor]** Scroll the sheet to the bottom: Basic attack (Longbow), Special move (Roll), and the three branches with their nine ranks, each with its icon, name, "Rank n" and description. Choose does not move. Drag the model: it turns; drag the sheet: it only scrolls.
3. **[device]** Deferred (PROGRESS lists it): scroll feel and inertia under a thumb.

## Out of scope

- **A stat bar, a comparison, or a live number**: numbers only, base values (UI-12c rule 2).
- **Tapping a rank for more**: the description is on the row already.
- **The tree screen in a run** (`TreeView`): unchanged; it keeps its own detail card.
- **Section art** (a ribbon per heading, a panel behind the sheet): the sheet is text on the page's background in this task. If the capture shows it needs a panel, the art is a later row with a Gemini sheet.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`). Deviations from the above with their reasons, or "as specified"._

_2026-10-16._ Built to the Files table.

**Deviations**

1. **`CharacterPage.Show` lays the page out again when the dots row appears or goes.** The presenter lays out before it shows (`PlaceChrome`, then `DrawPage`), so a page that only learned the class count in `Show` kept the space of the count before it: rule 1's "no space is kept" needed it. `Page_NoDots_KeepsNoSpace` covers the first case and `Page_Dots_SitUnderThePreview` the second.
2. **The viewport carries a transparent `Image`** as well as its `RectMask2D`: rows draw nothing that takes a raycast, so without it a drag between two rows lands on nothing and the sheet does not scroll. `Sheet_Scroll_VerticalClamped` asserts it.
3. **`Row` is one pooled shape for every kind**: frame, icon, title, value and body, the parts a kind does not draw switched off, and the public getters null for them as the API says. One pool rather than one per kind, so rule 9 is a single list.
4. **Colours the spec left open:** a heading is `HeadingDp` in `Palette.HudText`, a branch name `TitleDp` in `Palette.HudCaption`, the description `BodyDp` in `HudText`.
5. **`ClassSelectPresenterTests` gains a `CatalogWithTrees()` helper** (and `using System.Linq`) for `Presenter_Model_CarriesTheSheet`, which reads the model through the sheet's rows (three branch names) because the page exposes no model. No existing row read `Strip`, so the named ripple was empty.
6. **`CharacterPageTests`' phones are the spec's 411 × 914 and 360 × 780 dp**, not UI-08d's three; `Page_Preview_NeverUnder120Dp` folds into `Page_Preview_TakesItsShare_Clamped`, whose 300 dp page is the under-the-clamp case. `CharacterPage._buttonX` went with the columns layout, where it was the only thing that set it.
7. **`HeroSheet.cs`'s summary still names `SkillStrip.Build`** as what it superseded: history, outside the table, left.
8. **Test count:** −9 `SkillStripTests`, +10 `HeroSheetViewTests`, +2 net in `CharacterPageTests`, +1 in the presenter's: **+4**.

**The look.** A throwaway render (temporary camera, canvas and `CharacterStage`, destroyed in the same command) at 1079 × 2399 px (411 × 914 dp, 420 dpi) and 720 × 1560 px (360 × 780 dp, 320 dpi): the preview is 300 dp (the clamp) and 255 dp, the sheet's viewport 328 dp and 239 dp. The name, role, Ranger, description and stats read at both sizes; scrolled to the bottom, the three Stride ranks with "Rank n" right on the name's line, and Choose did not move. The body stands in its bind pose because nothing ticks its animator in an edit-time render, and the icons are the placeholder because the render passed no icon book. **The sheet reads on the page's background without a panel**, so the *Out of scope* panel row is not raised.
