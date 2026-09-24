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
- **Pact variants** (GD §13.2, M6-05b) — a corrupted node has no appearance yet, and a tree cell is a fifth place that would need one.
- **Banished nodes** (M6-02b; the fourth `NodeState` is M6-03b's) — `Available` will gain a second `bool[]` and this screen will want a fourth `NodeState`. One line, later, by the task that adds the mechanic.
- **The Android back gesture** — rule 11.
- **A `Palette` file** — M3-13, ledger row 6. Rule 8 adds the fifth reader and no more.

## As built

**Built as specced**, with four deviations, one of which is a ruling on a contradiction between this
spec and a shipped decision.

**1 — `Open(Action onClosed)` ships, and M3-09b's poll stays underneath it as the net.** The spec's
rule 5 and M3-09b's *As built* disagreed on the record: M3-09b rejected a callback for the Skills
screen because a screen that is destroyed, never dressed, or closed by something the caller did not
ask gives the panel back anyway, where a callback leaves dead buttons over a paused run with no way
out. **Both are right about different halves, because two doors is a different problem from one.** A
poll knows the screen went down and *not which way it came in*; a callback knows which door and
nothing about a door that is never told. So the callback answers *which*, the poll guarantees *a*,
and neither is load-bearing alone:

- `TreeViewPresenter.Open` stores the callback, `Close` hides first and then invokes it, clearing it
  before the call. A second `Open` is **refused** rather than re-pointed — replacing a live callback
  would strand whichever door is currently waiting.
- `PausePresenter` hands in `OnTreeClosed`, which is `RefreshPanel` — the same method `Update`
  already calls once a frame with `treeUp` beside `skillsUp`. So here the callback buys only that
  the panel comes back on the frame Close was tapped rather than the next one, which is the argument
  `OpenSkills` already makes about a second tap in one `EventSystem` pass.
- `LevelUpPresenter` hands in `OnTreeClosed`, which unveils, and `Update` carries the net:
  `if (_veiled && (_treeScreen == null || !_treeScreen.IsOpen)) OnTreeClosed();`. There the poll is
  not a nicety — a level-up screen left veiled is a stopped game showing nothing at all.
- `Close_GivesTheDoorBackEvenWithNoCallback` is the row that reddens if either half is dropped: it
  nulls `_onClosed` by reflection and closes the screen anyway, from both doors.
- **Every shipped comment that claimed the other was rewritten.** `PausePresenter.Update`'s remark
  now says the poll is the net under a callback rather than the mechanism instead of it, which is
  the M3-09a Deviation 2 mistake M3-09c had to go and fix, not repeated.

**2 — the canvas sorts at 110, and it did not have to.** `LevelUpPresenter` drops its root's alpha
**and** its `blocksRaycasts` before opening this, so an alpha-0 canvas at 100 draws nothing and takes
no touches: 70 would have worked today. It sorts at 110 anyway, because that would have been a
promise made in another file about its *runtime state* where a sorting order is a fact about *this
asset*, and the failure it would produce — a tree drawn under the cards on a stopped game — is the
family this project keeps refusing. The rule it keeps is one sentence rather than six numbers: **a
screen sits above every screen it can be opened from.** HUD 0 · hint 40 · pause 50 · Skills 60 ·
level-up 100 · tree 110.

**3 — `HideScreen` split, and nothing redraws on the way back.** Rule 6's *hidden, not destroyed*
needed the half of `LevelUpPresenter.HideScreen` that drops alpha and raycasts without the half that
hides every card, so it is now `Veil()` plus a card loop. Coming back is `ShowScreen()` and
**explicitly not `Draw`** — repainting would re-arm cards that a tap in the same frame had
deliberately killed and would relatch `_choosing`, which is M3-08b rule 5's failure arriving through
a new door. `LevelUp_OpensAndReturns` asserts the same ids and the same `interactable` flags across
the round trip, and that `OfferPresented` was published exactly once.

**4 — three files outside the Files table, each named rather than absorbed.**

- **`Tests/Core/Progression/SkillTreeTests.cs`** took the three `State_*` rows. There is no
  `RunStateTests`, and M3-09b put its `RunState` row in `SkillRunnerTests` beside the thing it
  delegates to; availability delegates to `SkillTree`, so this is the same rule applied again. A test
  cannot build a `RunState` — the constructor is `internal` with no `InternalsVisibleTo` (AR §18.2) —
  so the rows go through a real `RunSession` over a restored `RunSnapshot`, which meant the fixture
  grew a `StartRun`, a `Husk()` and a `Mode()` it had never needed.
- **`Tests/Game/Presentation/PausePresenterTests.cs`**: `Panel_HasResumeSkillsAndQuit` was **renamed
  in place** to `Panel_HasResumeSkillsTreeAndQuit`, its two counts moved to **4 and 5**, and the
  comment M3-09a wrote pointing at the wrong task (and M3-09b corrected) now reads as history rather
  than as an obligation. It neither adds nor removes a row — M3-09b's own precedent.
- **`Scenes/Run.unity`** was dressed: the prefab instance, the `SceneRoots` entry, and **three**
  serialized references rather than one — `RunScope._treeViewPresenter`, `PausePresenter._treeScreen`
  and `LevelUpPresenter._treeScreen`. 170 insertions, zero deletions, where the last three tasks were
  110 for the same reason at one reference.

**Two things ruled inside the Files table that are worth reading before the next screen.**

**The float doors fall back rather than leaving the authored layout alone, and they fall back
differently from each other.** `PausePresenter.Place` and `LevelUpPresenter.Place` both leave the
prefab's layout alone on a non-finite dp field; that answer is unavailable here, because every cell
is a clone made on first open and *leave it alone* is twenty-seven cells stacked on the template's
rect. So `_cellHeightDp` takes `DefaultCellHeightDp` (`FirstActiveHint.Dwell`'s answer) because a
cell height has no honest zero, and `_cellGapDp` takes 0 because cells touching is a layout rather
than a fault. `Layout_IgnoresANonFiniteDpField` asserts the fallback produces the *identical* rects,
not merely finite ones. **The third door the spec's parenthetical mentioned — column width — does not
exist**: the three columns are authored `RectTransform`s on the prefab, which is what
`_branchColumns` being a serialized array means, so nothing here does horizontal arithmetic.

**`TreeNodeView.Frame` throws on an unknown `NodeState` and `TreeNodeView.Tint` does not.** The two
enums differ: `SkillKind` is CH §4's closed four, validated at authoring time and shared with
`OfferCard`, so a fallback there is one answer to a question settled elsewhere. `NodeState` is this
file's own and **M6-02's Banish is already named as the task that adds a member** — a new state with
no colour would draw as *locked*, which is a node the player owns reading as one they cannot reach,
silently. `PlayerStats.Resolve`'s rule, and `Show_RefusesNothingToDrawAndNoStateToDrawItIn` is the row.

**Rule 8's seven colours.** `TreeNodeView` carries three state colours of its own plus CH §4's four
kind tints **copied value for value from `OfferCard`**, and `Tree_TintsByKind` asserts the two files
agree field for field — so ledger row 6's owner inherits one answer per kind rather than two that
have quietly drifted. Nine predicted readers is now seven predicted and two real.

**Tests: 19 named rows + 3 implied guards + 1 the ruling owes = 23, and the suite moved 1 528 → 1 551
exactly.** 16 rows in `TreeViewPresenterTests`, 3 in `SkillTreeTests`; the guards are a null row on
the one public constructor, `TreeNodeView.Show`'s two refusals, and one non-finite row covering both
`float` doors; the twenty-third is `Close_GivesTheDoorBackEvenWithNoCallback`, which exists because
deviation 1 is a ruling and a ruling with no red row is a paragraph.

**Ledger rows 9, 6 and 4 all move and none closes** — recorded in the ROADMAP's table and in
PROGRESS → *Where the rules live*, rather than left for M3-15 to recount.
