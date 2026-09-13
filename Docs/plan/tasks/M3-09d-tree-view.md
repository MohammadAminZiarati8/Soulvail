# M3-09d — The tree view: three branches, your path, and the two doors into it

**Size:** M · **Depends on:** M3-09a (the panel), M3-08b (the toggle it adds), M3-03 (the tree it reads), M3-02a (the shape) · **Branch:** `m3-09d-tree-view`
**Design refs:** CH §5, §5.1; GD §13.1, §16.4; AR §18.2; ADR-0012 · **Ledger rows:** **9** (every node on it is an unresolved `LocKey` — its fourth reader and the densest, twelve at once), 6 (three node states want three colours and there is still no `Palette` file — rule 8), 4 (whether twenty-seven cells fit a landscape phone is device-only)

## Goal

CH §5.1's *"View Tree"*: the whole tree with the player's path highlighted, opened from pause or from the level-up screen, read-only — for the player who wants to plan, and never in the way of the one who does not.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeViewPresenter.cs` | Game | the screen: resolves the tree, lays out three branches, knows which door it came in by — **block namespace** (Traps §5) |
| `Game/Controls/TreeNodeView.cs` | Game | one cell: its keys, its state, its kind — **block namespace** |
| `Tests/Game/Presentation/TreeViewPresenterTests.cs` | Tests.Game | shape, states, both doors, `LevelUpPresenterTests`' shape |
| `Prefabs/UI/TreeView.prefab` | — | the screen and its cell template. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Core/Run/RunState.cs` — `IsNodeAvailable(ContentId)` (rule 3); `Game/Presentation/PausePresenter.cs` + the View Tree button (M3-09a rule 6); `Prefabs/UI/Pause.prefab` gains it; `Game/Presentation/LevelUpPresenter.cs` + the View Tree toggle and its return (rule 5) — **the line M3-08b's Out of scope reserved**; `Prefabs/UI/LevelUp.prefab` gains the toggle; `Game/Composition/RunScope.cs` + `_treeViewPresenter` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// RunState (added) — a read, never the handle (AR §18.2)
public bool IsNodeAvailable(ContentId skillId);   // false when there is no tree, and for a stranger
```

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class TreeViewPresenter : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; RectTransform[] _branchColumns (3);
        // [SerializeField] TreeNodeView _cellTemplate; TMP_Text[] _branchLabels (3); Button _close;

        [Inject] public void Construct(IRunSession session, ContentCatalog catalog);

        /// <summary>Shows the tree. <paramref name="onClosed"/> is how each door gets itself back.</summary>
        public void Open(Action onClosed);

        public void Close();
        public bool IsOpen { get; }
    }
}

namespace Soulvail.Game.Controls
{
    public enum NodeState { Locked, Available, Taken }

    public sealed class TreeNodeView : MonoBehaviour
    {
        // [SerializeField] TMP_Text _name, _description; Image _frame, _kindStrip;
        // [SerializeField] Color _locked, _available, _taken;          // rule 8

        public void Show(SkillSpec spec, NodeState state);
    }
}
```

## Behaviour

1. **Read-only, and that is the design rather than a simplification.** CH §5.1 chose random-from-available *over* free-pick precisely so a level-up is a two-second decision; a tree you can tap to buy from would be the free-pick screen the design refused, arriving through the back door. Nothing here sends a command, and the class takes no `IProgressionCommands` — a screen that cannot spend a pick cannot be argued into spending one.
2. **The shape comes from the catalog, the state from the run.** `ContentCatalog.TryGetTreeFor(state.CharacterId)` gives `SkillTreeSpec` — three branches of tiers of ids (M3-02a) — and each id resolves through `Skill(id)` for its keys and kind. A class with no tree (M3-03 rule 10) shows an empty screen with a line saying the class has no tree, and the View Tree buttons are not offered at all: that is every run before M3-12, and a button onto a blank screen is worse than no button.
3. **Three states per node, and the middle one needs a new read.** `Taken` is `TakenNodeIds`; `Available` is `SkillTree.IsAvailable`, which lives behind an `internal` handle (M3-03 rule 8) and has no read — so `RunState.IsNodeAvailable(id)` is added, the same narrow-read pattern M3-09b adds `SkillCooldownSeconds` by. The alternative was this screen re-implementing CH §5's gating from `TakenNodeIds` and the tree's shape, which is a second copy of `TreeRules` in the presentation layer and would drift the first time a keystone rule changed. Everything else is `Locked`.
4. **Laid out as CH §5 draws it: three columns, tier 1 at the top.** A tier holds one or more nodes (M3-02a rule 7), so a tier is a row within its column and a column is as tall as its branch. The branch's `NameKey` heads its column. Twenty-seven cells is the shape this has to survive (M3-12's v1 is twelve), and the cells are a pooled template instantiated on first open.
5. **Two doors, one screen, and each gets itself back.** `Open(onClosed)` takes the callback, so the pause panel and the level-up screen each hand in their own return: from pause, Close restores the panel; from the level-up screen, Close restores the three cards with the pause **still held** by M3-08b. The toggle on the level-up screen is the line M3-08b's Out of scope reserved for this task. **The level-up screen does not lower its pause to show this**, and must not: `RunPause` holds one reason at a time (M3-08a rule 12), and a tree view that resumed the game underneath itself would be the opposite of what a planning screen is for.
6. **From the level-up screen the cards are hidden, not destroyed, and no pick is spent by opening it.** The offer is state on `LevelUpFlow` (M3-08a) and nothing here touches it; coming back shows the same three cards, still interactable, still the same ids. A player who plans and then picks has lost nothing, which is the whole promise of *"opt-in, so it never slows down anyone who doesn't care"*.
7. **Every node's name and description are drawn as keys** (`spec.NameKey.Key`), unresolved, exactly as M3-08b's card draws them — and this is where ledger row 9 stops being abstract: a card shows three keys for two seconds, and this screen shows **twenty-seven at once**. GD §13.1's *"readable in under 2 seconds"* is judged on a card; whether a *tree* of keys is navigable at all is a question only M6-10 can answer. Said here so M3-15 rules on the whole of row 9 rather than on the cards alone.
8. **Three state colours and four kind tints, all serialized, and still not a palette.** M3-08b rule 8's argument, one screen later and one reader worse: `TreeNodeView` carries `_locked`, `_available`, `_taken` for the frame and reuses M3-08b's four kind tints for the strip. Ledger row 6's owner (M3-13) now inherits **five** readers rather than four, and that is the number to size the `Palette` file against. Inventing it here would be M3-13 arriving early and unspecified, which is the mistake row 6 says is the worse of the two.
9. **No scroll and no zoom in V1.** Three columns of at most eight tiers is a fixed grid that either fits a landscape safe area or does not, and finding out is a device question (ledger row 4). A pinch-zoom tree is real work and M7-04's eighty-one nodes are when it earns its keep — until then, cells shrink to fit and the honest failure mode is *small*, not *clipped*.
10. **It renders no events at all.** The tree cannot change while this is up: the tick is gated on both doors, and the one thing that could change it — taking a node — is the screen underneath, which is hidden. Built on open from reads, like M3-09b's list and for the same reason.
11. **Close is a button and the Android back gesture is not wired.** The app has never run outside the Editor (ledger row 4) and `InputAdapter` carries no back binding; adding one for this screen alone would be the first of three inconsistent answers. Named rather than forgotten.

## Tests

| Test | Given / When / Then |
|---|---|
| `State_AnswersAvailability` | a fresh 27-tree / `IsNodeAvailable` on a tier-1 and a tier-3 id / true, false; take the tier-1 pair and the tier-2 node turns true (rule 3) |
| `State_AvailabilityWithNoTree` | a class with no tree / `IsNodeAvailable(anything)` / false, no throw (rules 2, 3) |
| `State_AvailabilityForAStranger` | an id not in the tree / — / false, no throw |
| `Tree_DrawsEveryNode` | the 27-tree / `Open` / 27 cells across three columns, column heights 9/9/9, tier 1 first (rule 4) |
| `Tree_DrawsAPartialTree` | M3-12's three branches of 2/2 / `Open` / 12 cells, four per column (rule 4) |
| `Tree_StatesAreThree` | two taken, two available, the rest locked / `Open` / the cells carry `Taken`, `Available`, `Locked` accordingly (rule 3) |
| `Tree_TintsByKind` | one of each kind / `Open` / four different strip colours (rule 8) |
| `Tree_FrameColoursByState` | one of each state / `Open` / three different frame colours, from the serialized fields (rule 8) |
| `Tree_DrawsKeysNotEnglish` | a node keyed `skill.oathbound.consecrate` / `Open` / that exact string — ledger row 9's fourth reader (rule 7) |
| `Tree_NoTreeShowsTheEmptyLine` | a class with no tree / `Open` / no cells, the line on, and both View Tree buttons inactive (rule 2) |
| `Tree_SendsNothing` | reflection over `TreeViewPresenter` / — / it takes no `IProgressionCommands` and no `IPlayerCommands`; tapping a cell sends nothing (rule 1) |
| `Tree_BuildsOnceOnOpen` | the screen open / 60 frames / each cell's text was written once (rule 10) |
| `Pause_OpensAndReturns` | the pause panel up / View Tree, then Close / the tree shows, then the pause panel is back and `RunPause.Holder` is still `Menu` (rule 5) |
| `LevelUp_OpensAndReturns` | an offer of 3 / View Tree, then Close / the cards hide then come back, the **same three ids**, still interactable (rules 5, 6) |
| `LevelUp_KeepsThePauseThroughout` | the row above / at every step / `RunPause.Holder` is `LevelUp`, held exactly once, no throw (rule 5) |
| `LevelUp_ViewingSpendsNoPick` | two picks owed, an offer / View Tree, Close, then choose / `PendingLevelUps` down by exactly one (rule 6) |
| `LevelUp_ShowsTheNewStateAfterAPick` | choose a tier-1 node that unlocks a tier-2 one / View Tree on the second offer / the taken node reads `Taken` and the unlocked one `Available` (rules 3, 10) |
| `Close_FromEitherDoorRunsTheRightCallback` | opened from each / Close / only that door's callback ran (rule 5) |
| `Prefab_IsDressed` | `TreeView.prefab` / read the fields back after a save (Traps §5) / root, three columns, three labels, the cell template and Close present |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with M3-12's tree. Pause → View Tree: three columns of four, the two nodes you have taken framed as taken, the next tier framed as available, the rest dim. Close returns to the pause panel.
2. **[Editor]** Level up. Tap View Tree on the card screen: the cards vanish, the tree appears, the game is still stopped. Close: the same three cards, still tappable. Take one — the pick was not spent by looking (rule 6).
3. **[Editor]** Level up twice in one grant. Open the tree on the second offer: the node taken a moment ago reads as taken and anything it unlocked reads as available (rule 10).
4. **[Editor]** Descend with no tree authored. Neither View Tree button is offered (rule 2).
5. **[device]** Ledger row 4: whether twelve cells — let alone twenty-seven — fit a landscape safe area at a readable size, and whether rule 9's no-scroll bet survives contact with a 6-inch screen. This is the row most likely to send the task back.
6. **[device]** Ledger row 9 at its worst: twenty-seven `LocKey`s on one screen. Whatever M3-15 rules for the cards, this is the screen that makes the cost of waiting for M6-10 obvious.

## Out of scope

- **Taking a node from here** — rule 1. Not a V1 feature and not a V2 one either; CH §5.1 chose against it.
- **Pinch-zoom, scrolling, or a minimap** — rule 9. M7-04's eighty-one nodes are when a tree needs navigating.
- **Showing what a node *costs*.** One level is one node (CH §5) and there is no point economy to display.
- **Pact variants** (GD §13.2, M6-05) — a corrupted node has no appearance yet, and a tree cell is a fifth place that would need one.
- **Banished nodes** (M6-02) — `Available` will gain a second `bool[]` and this screen will want a fourth `NodeState`. One line, later, by the task that adds the mechanic.
- **The Android back gesture** — rule 11.
- **A `Palette` file** — M3-13, ledger row 6. Rule 8 adds the fifth reader and no more.

## As built

_Filled at merge._
