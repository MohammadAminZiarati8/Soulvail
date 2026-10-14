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
| *small edits* | | `CharacterLook` and `CharacterLookBook` gain `SpaceIcon`; `Ranger`'s look gets `Icon_Roll`; `ClassSelect.prefab` gets the strip; `CharacterPage` calls `Show`; the Ranger's Space move gets a name and description key (`character.ranger.space.name`, `.description`) |
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

_Filled at merge._
