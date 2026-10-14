# UI-05d — The skill strip

**Size:** S · **Depends on:** UI-05c · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** CH §5, §6 · **Ledger rows:** none

## Goal

Under the preview, four icon tiles show what the class can do; a tap names one and says what it does in a line.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/SkillStrip.cs` | Game | four `SkillFrame` tiles, a selection and a caption |
| `Tests/Game/Presentation/SkillStripTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `CharacterLook` and `CharacterLookBook` gain `SpaceIcon`; `Ranger`'s look gets `Icon_Roll`; `ClassSelect.prefab` gets the strip; `CharacterPage` calls `Show`; the Ranger's Space move gets a name and description key (`character.ranger.space.name` "Roll", `.description` "A quick roll that slips past attacks.") |
| *ripple* | | the fakes that build a `CharacterLook` |

## Public API

```csharp
// SkillStrip (MonoBehaviour):
//   void Show(IReadOnlyList<StripEntry> entries, ILocalizer localizer);   // up to 4
//   int Selected { get; }
//   void Hide();
// StripEntry (readonly struct): Sprite Icon, LocKey NameKey, LocKey DescriptionKey.
```

## Behaviour

1. **Which four.** The tier-1 node of each branch, in branch order, then the class's Space move: for the Ranger Quick Draw, Volley, Fleet Foot and the Roll. A class with no tree draws only the Space move; one with no Space move (`MovementSkillKind.None`) draws no fourth tile. Never more than four.
2. **Tiles** are `CharacterPage_SkillFrame` around the icon, 56 dp square; the selected one wears `CharacterPage_SkillFrameSelected`. **The first is selected on open.**
3. **A tap selects**, and the caption under the strip shows the name and the description, resolved through the localizer. It sends no command and opens nothing.
4. **The caption is at most three lines at 14 sp**, and it clears 12 sp.
5. **A node's icon is `SkillIconBook.For(id)`**, the placeholder when it has none; the Space move's is `CharacterLook.SpaceIcon`.
6. **Changing class redraws the strip** and selects the first tile again.
7. **It fits 9:20** between the preview and the button.

## Tests

| Test | Given / When / Then |
|---|---|
| `Strip_Ranger_IsThreeNodesAndTheRoll` | rule 1 |
| `Strip_NoTree_OnlyTheSpaceMove` | rule 1 |
| `Strip_NoSpaceMove_HasNoFourthTile` | rule 1 |
| `Select_FirstOnOpen_TapMoves` | rules 2, 3 |
| `Caption_NameAndDescription` | rule 3 |
| `Caption_ClearsTheFloor_AtMostThreeLines` | rule 4 |
| `Icons_FromTheBookOrThePlaceholder` | rule 5 |
| `ClassChange_ResetsTheSelection` | rule 6 |
| `Strip_FitsPortrait` | rule 7 |

## Manual verification

1. **[Editor]** Descend: four tiles under the Ranger; tapping each changes the caption and the ring.

## Out of scope

- A skill's animation on the preview (nothing authors one). The tree's other nodes: "View Tree" is the tree screen's, and linking it from here is a later row.

## As built

As specified, with the differences below. Batch-mode EditMode: **4 045 run, 4 043 passed, 1 failed, 1 inconclusive**; the failure is `CharacterPreviewTests.Resize_MakesTheTextureCappedAndFreesTheOld`, which fails the same on a clean `dev` (depth 32, not 24) and is not this task's. Not seen in the Editor.

1. **The Space move's words are on the look, not the spec.** Core's `MovementSkillSpec` carries numbers and no text, so `CharacterLook` gained `SpaceIcon`, `SpaceNameKey` and `SpaceDescriptionKey` (trailing optional constructor arguments, so no fake needed a ripple), and `CharacterDefinition` three fields to author them. The Ranger's asset sets `Icon_Roll` and `character.ranger.space.name` / `.description`; English gained "Roll" and "A quick roll that slips past attacks."; `Pseudo.asset` was regenerated.
2. **Files beyond the table.** `ContentValidationTests.EveryAuthoredKey` now walks the look's two Space keys, because the orphan sweep reads authored keys through the spec and would call the new rows unnamed. `ClassSelectPresenter` takes `SkillIconBook` as a trailing optional parameter and builds the entries; `ClassPageModel` gained an optional `Strip`.
3. **`SkillStrip.Build` is static and takes the catalog, the book and the look**, so rule 1 is tested without a prefab. A node's icon is null from the book when it has none and the strip draws its own `Icon_Placeholder`.
4. **Layout.** The strip is 118 dp tall (a 56 dp tile, 8 dp, three 14 sp lines at 1.25) and sits between the dots and the body, so the body gives up that much plus a gap. Tiles are 12 dp apart, centred. A class whose Space key is not authored still gets a fourth tile with an empty caption; the Oathbound, Gravecaller and Emberwright are in that case and are off the focus build.
5. **`ClassSelect.prefab` was edited in place** by a throwaway batch-mode script, deleted afterwards; the root, its `CanvasGroup` and the presenter keep their file IDs (checked against the Menu scene's references).

Not done: the strip has not been seen in the Editor or on a device. If it crowds the body or the button, adjust `TileDp`, `TileGapDp` and `CaptionGapDp` in `SkillStrip` and `RowGapDp` in `CharacterPage`.
