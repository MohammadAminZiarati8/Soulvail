# M3-10a — S1–S4: four buttons that are not the Charge button, and the tap that becomes a command

**Size:** M · **Depends on:** M3-07a (the slots and `CastSkill`), M3-06 (the fills it polls) · **Branch:** `m3-10a-skill-slot-buttons`
**Design refs:** CC §5, §6.1, §6.2, §6.5; GD §5.2, §16.1; AR §4.3, §6, §18.1, §18.2 · **Ledger rows:** 4 (60 dp, the cluster's reach and multi-touch against the stick are all device-only — and multi-touch is the one M1 never answered), 8 (nothing; named as untouched)

## Goal

CC §6.2's four thumb positions: a button per occupied manual slot, a radial fill on each, 40 % opacity and **no tap response** while it is cooling — and the press arriving in core at the same known point in the frame every other command does.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Controls/ManualSkillButton.cs` | Game | one slot: its fill, its dimming, its press — **block namespace** (Traps §5) |
| `Game/Presentation/SkillBarPresenter.cs` | Game | the four: which are drawn, where they sit, what each is showing — **block namespace** |
| `Game/Adapters/SkillSlotInput.cs` | Game | the one-frame buffer between a uGUI tap and `CommandPhase` (rule 3) — `TapToFocusAdapter`'s shape |
| `Tests/Game/Presentation/SkillBarPresenterTests.cs` | Tests.Game | the four buttons, the buffer and the frame order in one fixture |
| *small edits* | | `Core/Run/RunState.cs` — `SlotCooldownFraction(int slot)` and `IsSlotReady(int slot)` (rule 2); `Game/Composition/RunTicker.cs` — `_skillSlots.Poll()` in `CommandPhase`, beside `_tapToFocus.Poll()` (rule 3); `Game/Composition/RunInstaller.cs` registers `SkillSlotInput`; `Game/Composition/RunScope.cs` + `_skillBar`; `Prefabs/UI/Hud.prefab` gains four buttons beside the Charge; **AR §18.1**'s command-phase row gains its second poller |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// RunState (added) — reads by slot, because a slot is what a button is (rule 2)
public float SlotCooldownFraction(int slot);   // 0 for an empty slot
public bool IsSlotReady(int slot);             // false for an empty slot
```

```csharp
namespace Soulvail.Game.Adapters;

/// What a thumb asked for this frame, held until the frame asks. One int, not a queue (rule 3).
public sealed class SkillSlotInput
{
    public SkillSlotInput(IPlayerCommands commands);

    /// <summary>Called by a button's uGUI handler, whenever in the frame that happens.</summary>
    public void Press(int slot);

    /// <summary>Called from RunTicker.CommandPhase. Sends at most one CastSkill and clears.</summary>
    public void Poll();
}
```

```csharp
namespace Soulvail.Game.Controls
{
    public sealed class ManualSkillButton : MonoBehaviour
    {
        // [SerializeField] Image _radialFill; CanvasGroup _group; Button _button; TMP_Text _label;
        // [SerializeField] float _sizeDp = 60f;                        // CC §6.2

        public int Slot { get; }
        public void Bind(int slot, SkillSlotInput input, IRunSession session);
        public void ShowEmpty();                                        // not drawn at all (rule 4)
        public void Show(SkillSpec spec);
    }
}

namespace Soulvail.Game.Presentation
{
    public sealed class SkillBarPresenter : MonoBehaviour
    {
        // [SerializeField] ManualSkillButton[] _buttons (4); Vector2[] _offsetsDp (4);
        // [SerializeField] Vector2 _anchorMarginDp = new(96f, 96f);    // the Charge button's corner

        [Inject] public void Construct(IRunSession session, SkillSlotInput input,
                                       DomainEventHub hub, ContentCatalog catalog);
    }
}
```

## Behaviour

1. **A sibling of `SkillButton`, not a generalisation of it, and the one axis they differ on is the one that matters.** `SkillButton` stays tappable while it is cooling, deliberately and against CC §6.2's generic rule, because CC §5 gives the movement skill a 0.15 s input buffer and *"the buffer only exists if the early press actually reaches core"* — its own remarks say so. A slot button is the case that rule was carved out of: CC §6.2's *"unavailable: 40 % opacity, no tap response"* applies to it exactly. Sharing a base class would put the single behaviour that differs behind a virtual and invite the next reader to unify them; sharing `StickShaper.PixelsPerDp` is all the sharing there is to do.
2. **It polls, and it polls *by slot*, which needs two reads that do not exist.** `SkillButton`'s bargain — a fill slides continuously for seconds and an event carrying it would be an event per frame — so `Update` reads one number. But `RunState`'s fills are addressed by **runner index** (`SkillCooldownFraction(int index)`, M3-06 rule 13) and a button knows only its **slot**; mapping one to the other means `ManualSlotAt(slot)` then a search for the id's index, which is a presentation layer re-deriving what `SkillRunner.TryIndexOf` already answers. So `RunState` gains `SlotCooldownFraction` and `IsSlotReady`, addressed the way a button is. An empty slot answers 0 and false and never throws — unlike `CastSlot`, which throws for one (M3-07a rule 4), because a *read* of an empty slot is what a button does on every frame it is not drawn.
3. **The tap becomes a command in `CommandPhase`, never in uGUI's event.** `RunTicker`'s own remarks are explicit about why command adapters are not `ITickable`s: *"the order commands land in — and whether they land before or after the snapshot — would be whatever order `RunScope` happened to register them in, decided by an edit somewhere else entirely and invisible until something went subtly wrong."* A uGUI `Button` firing `IPlayerCommands.CastSkill` directly has exactly that defect, because Unity gives no order between the EventSystem's `Update` and this object's. `SkillSlotInput` is therefore `TapToFocusAdapter`'s shape: the button writes a slot, `CommandPhase` polls it, and *"a tap became a command"* happens at the one point in the frame that is written down. It costs one small class and no Input System changes — four more actions on the virtual gamepad was the alternative and it is a generated asset for a press that needs no buffer.
4. **One press per frame, and the last one wins.** `SkillSlotInput` holds a single slot rather than a queue: two slots tapped in one frame is two thumbs or a bug, and casting both would spend two cooldowns on an input the player did not distinguish. A queue would also outlive the frame, which is the cast buffer CC §6.2 explicitly answers with dimming instead (M3-06's Out of scope). Cleared on `Poll` whether or not it sent anything, and cleared on `Reset` when the run ends.
5. **An empty slot is not drawn**, CC §6.2 literally: *"Unused slots are not drawn — a player with zero manual skills sees exactly one button, and one with two sees three."* The object is deactivated rather than made transparent, so it takes no touches and costs no canvas rebuild. That is the state of all four in every run until M3-11 and M3-12, and the reason rule 2's reads have to tolerate an empty slot rather than refuse one.
6. **Redrawn from `SkillAutoCastChanged`**, which carries the id, the state and the slot (M3-07a rule 9) — the only event that can change which buttons exist. `RunStarted` draws the opening state, for `HudPresenter`'s reason: a resumed run comes back with slots already filled (M3-07b) and there is no event describing a frame that has not happened yet.
7. **40 % opacity *and* `interactable` false while cooling**, which is what makes M3-07a rule 4's *false* visible without it ever being returned. The button suppresses the tap, so core's tolerant answer — a cooling `CastSlot` returns false rather than throwing — stays the backstop for the one-frame race where a cooldown starts between the tap and the poll. Both halves exist on purpose: the screen says *no* and core does not punish a thumb that was a frame early.
8. **Laid out from the Charge button's corner, in dp, as serialized offsets.** CC §6.2's diagram is a cluster — S1 low and left of the Charge, S2/S3/S4 arcing above it — and the four offsets are fields so the owner can move the whole arc on a real phone without a rebuild. 60 dp across with 12 dp minimum spacing (CC §6.2's table); the arithmetic is `SkillButton.Place`'s, divided by the canvas scale for the same reason, and anchored bottom-right so `SafeAreaFitter` insets it away from the gesture bar.
9. **The label is the skill's `LocKey`, and it is a placeholder that says so.** There are no skill icons and GD §16.1 wants *"small icons"*; a 60 dp circle with an unresolved key in it is not a design, it is an honest gap — **and it is the only one of ledger row 9's readers where the key does not fit**. M3-13 or M7's art pass gives these buttons icons; until then the button is distinguishable by position, which is what a thumb uses anyway. Named rather than dressed up.
10. **It sends nothing when the run is not running.** `IPlayerCommands` throws outside a run (M3-07a's `Commands_ThrowWhenNoRunIsRunning`), and `CommandPhase` is already behind `RunTicker`'s `IsRunning` guard — so the poll cannot reach a dead session. The buttons are left drawn where they are when the player dies, like the capsule.
11. **Nothing here knows about Auto.** An auto-cast skill has no button (CC §6.1) and this class never asks whether one exists; GD §16.1's *"auto-cast skills need visible cooldowns"* is a different readout in a different corner, and it is **M3-10b's**.

## Tests

| Test | Given / When / Then |
|---|---|
| `State_SlotReadsAnswerByPosition` | A manual in S1, cooling; S2 empty / `SlotCooldownFraction(0)`, `(1)`; `IsSlotReady(0)`, `(1)` / >0, 0; false, false (rule 2) |
| `State_SlotReadsWithNoTree` | a class with no tree / all four slots / 0 and false, no throw (rule 2) |
| `State_SlotReadsOutOfRange_Throw` | slot −1 and 4 / — / `ArgumentOutOfRangeException` each |
| `Bar_DrawsOnlyOccupiedSlots` | A in S1, C in S3 / `RunStarted` / buttons 0 and 2 active, 1 and 3 inactive (rule 5) |
| `Bar_DrawsNoneWhenNothingIsManual` | every skill Auto / — / all four inactive; the Charge button is untouched (rule 5) |
| `Bar_RedrawsFromTheEvent` | nothing manual / `SkillAutoCastChanged(A, false, 0)` / button 0 becomes active showing A's key (rule 6) |
| `Bar_HidesOnTheWayBack` | A in S1 / `SkillAutoCastChanged(A, true, −1)` / button 0 inactive, nothing else moved (rules 5, 6) |
| `Bar_DrawsTheOpeningStateOnRunStarted` | a resumed run with A in S1 and C in S3 / `RunStarted` / two buttons, no event needed (rule 6) |
| `Button_FillTracksTheSlot` | A in S1, cast it / the next frames / `fillAmount` moves from 0 toward 1 as the cooldown runs (rule 2) |
| `Button_DimsAndGoesDeadWhileCooling` | A cooling / — / `alpha` 0.4 and `interactable` false; ready / `alpha` 1 and interactable (rule 7) |
| `Button_CoolingTapSendsNothing` | A cooling / tap / `SkillSlotInput` holds nothing, no `CastSkill` reaches core (rule 7) |
| `Button_WritesOnlyOnChange` | A ready and idle / 60 frames / `fillAmount` was assigned once — `SkillButton`'s canvas-rebuild argument (rule 2) |
| `Input_PressIsSentInCommandPhase` | a ready slot / tap, then one `RunTicker.Tick` / `CastSkill(0)` arrived, and it arrived **before** the snapshot was built (rule 3) |
| `Input_PressOutsideATickIsHeld` | tap with no tick / — / no command yet; the next `Tick` sends it (rule 3) |
| `Input_TwoPressesInOneFrameSendOne` | tap S1 then S3 in one frame / `Poll` / one `CastSkill`, for S3 (rule 4) |
| `Input_PollClearsEvenWhenEmpty` | nothing pressed / `Poll` twice / no commands, no throw (rule 4) |
| `Input_PressDoesNotOutliveTheFrame` | tap, `Poll`, then a second `Poll` / — / exactly one command (rule 4) |
| `Input_NoCommandAfterTheRunEnds` | the player dies / tap, `Tick` / nothing sent, no throw — `RunTicker`'s guard (rule 10) |
| `Frame_SlotPollSitsBesideTapToFocus` | a recording ticker / one `Tick` / the slot poll ran inside `CommandPhase`, above the snapshot build — `FrameOrderTests` (rule 3) |
| `Button_IsPlacedInDp` | a canvas with a known scale / `Start` / each drawn button is `60 × pxPerDp` across, at its serialized offset from the bottom-right (rule 8) |
| `Button_SpacingMeetsTheMinimum` | the four serialized offsets / — / no two centres closer than `60 + 12` dp (rule 8) |
| `Bar_LabelsAreKeys` | A keyed `skill.oathbound.consecrate` / — / the label is that string (rule 9) |
| `Bar_KnowsNothingAboutAuto` | reflection over `SkillBarPresenter` / — / it never calls `IsAutoCast`; an Auto skill produces no button by virtue of holding no slot (rule 11) |
| `Prefab_IsDressed` | `Hud.prefab` / read the fields back after a save (Traps §5) / four buttons present, each with its fill, group, button and label, and the Charge button unchanged |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with a hand-authored tree. Take one Active — **no new button**, because it is on Auto (rule 5). Pause → Skills, set it to Manual, Resume: one 60 dp button appears beside the Charge.
2. **[Editor]** Tap it. The skill fires, the fill empties and refills over its cooldown, and the button is dim and unresponsive the whole time (rule 7).
3. **[Editor]** Set four skills to Manual: four buttons in CC §6.2's arc. Set one back to Auto and its button disappears **without the others moving** — the hole is the point (M3-07a rule 3 from the screen's side).
4. **[Editor]** Clear a stage with a manual skill cooling. Across the boundary the fill keeps running (M3-06 rule 11) — a door is not a free cooldown.
5. **[device]** Ledger row 4, and this task adds the milestone's sharpest multi-touch question: **can a thumb on the stick and a thumb on S3 both be read at once?** Multi-touch has been deferred since M1 and four new buttons is where it stops being theoretical.
6. **[device]** Whether 60 dp with 12 dp spacing is hittable in an arc without looking, and whether the cluster collides with the Charge button in a real hand. Rule 8's offsets are fields for exactly this.

## Out of scope

- **The XP bar, the level number, the auto-cast cooldown row and the Overflow toast** — **M3-10b**. Different corner, no input, and rule 11 says why they are not this class's business.
- **Icons** — rule 9. M7's art pass, or M3-13 if the palette work reaches this far.
- **Drag-to-reorder** — not in V1 (M3-09b's Out of scope carries the ruling and the reason it costs no format bump later).
- **A cast buffer or queue** — rule 4, and M3-06's Out of scope argues it at length.
- **Precision mode** (CC §3.8) — a slot says *when*; nothing here says *where*.
- **Haptics on cast.** M1-20's listener reacts to events; `SkillCast` is one, and adding it is a line in `HapticsListener` — which is M3-13's or M8's, not a button's.
- **Generalising `SkillButton`** — rule 1.

## As built

**Built as specced, with one named deviation and five files the table does not list.**

**The deviation is rule 4's `Reset`.** The rule's prose says the buffer is *"cleared on `Poll` whether
or not it sent anything, and cleared on `Reset` when the run ends"*, and the **Public API** block
directly above it lists two members. The API block shipped: there is no `Reset`. Nothing in the Files
table could call one — `RunTicker.Dispose` is not among the named edits — so it would be public API
with no reader, and both halves of what it was for are already true: `Poll` clears unconditionally,
and a press made after the run ended is never polled at all, because `CommandPhase` sits below
`RunTicker`'s `IsRunning` guard (rule 10, and `Input_NoCommandAfterTheRunEnds` is the row). The class
remarks say this out loud rather than leaving the absence to be noticed.

**Five files outside the table, and three of them were forced by the compiler.** `RunTicker`'s
constructor grew a twenty-first argument, and every fixture that builds one had to grow with it:
`Tests/PlayMode/FrameOrderTests.cs` (named in the Tests table but not the Files table) and
`Tests/Game/Composition/ResumeFlowTests.cs` (named nowhere — one line, and no row in it presses a
slot). The three `State_*` rows went into `Tests/Core/Combat/SkillRunnerTests.cs`, **beside the object
the read delegates to**, which is M3-09b's precedent for `State_ExposesCooldownSeconds` and M3-09d's
for its three. `Scenes/Run.unity` was dressed with `RunScope._skillBar` — **12 insertions and zero
deletions**, one reference rather than a prefab instance, against M3-09d's 170. And `Docs/plan/`
carries a **doc fix that rode in from `dev`**: one uncommitted line in PROGRESS → Next task correcting
*"first task since M3-08b to change `Hud.prefab`"* to M2-12a. It is the owner's, not this task's.

**The layout answer, picked per field and the same for all three.** `_sizeDp`, the four `_offsetsDp`
and `_anchorMarginDp` all take `PausePresenter.Place`'s answer — **a nonsense value leaves the authored
layout alone** — where `TreeViewPresenter.Layout` falls back per field to a constant. The difference is
that these four buttons *are* authored on `Hud.prefab` at a real size in a real corner, so there is
something honest to fall back to; a tree cell is a runtime clone that starts life on its template's
rect, so leaving twenty-seven of those alone is twenty-seven cells stacked on each other. An unusable
anchor skips the whole cluster, an unusable offset or size skips its own button, because three buttons
where the owner put them and one where the prefab put it is strictly more legible than four in a heap.
A margin of **0 is legal** and is honoured: a button flush against the safe area's corner is a layout.

**The shipped arc is forced rather than chosen, and that is the device question.** 140 dp of radius,
S1 level with the Charge and to its left, S4 straight above it. CC §6.2's 60 dp buttons with 12 dp of
clearance put a hard floor of 72 dp between centres, and four of those over a quarter turn need
`72 / (2 · sin 15°) ≈ 139` — so this is the **tightest** cluster CC §6.2 permits, not a comfortable
one, and its top button sits ~236 dp above the safe area's bottom edge.
`Button_SpacingMeetsTheMinimum` checks all six pairs *and* each button against the Charge's own
72 dp — which is the row that reddens if the arc is tuned too tight on a phone.

**`Frame_SlotPollSitsBesideTapToFocus` retired this fixture's standing exemption.** `FrameOrderTests`'
class remarks had said since M2-11b that the commands phase is *"the one step not observed here"*,
because its two members reach core only through the Input System and that assembly does not reference
it. `SkillSlotInput` needs none — a button writes an `int` — so AR §18.1's first row is now asserted
end to end. **The row asserts ordering and nothing about physics** (M3-08a's lesson, quoted in the
spec): it reads the snapshot's `PlayerPosition` from inside `CastSkill` and requires it to still be
zero while the player stands a kilometre out. `FrameOrderTests` went from 8 rows to 9 and PlayMode
from 15 to **16**.

**Ledger rows.** Row **4** moves and gains the milestone's sharpest device question — a thumb on the
stick and a thumb on S3 at once, deferred since M1, now the one row that risks a *feature* rather than
a verdict — plus the reach of a forced 140 dp arc. Row **9** moves and gains its fifth real reader,
**and corrects how this row described that reader's closure**: the ledger says M3-14a closes the slot
buttons because *"the key never fitted and a word does"*, and the button now exists — a 60 dp circle
with an auto-sizing 8–18 pt label — so *"a word does"* is a **device** claim rather than a table one.
Row **8** is untouched: nothing here changes a clock or what a stage is measured with. **None of the
three closes.**

**Verified.** Six assemblies, zero compile errors, zero new analyzer warnings, the compile confirmed by
**calling the new API from a `RunCommand`** rather than by reading a DLL timestamp (Traps §3). EditMode
**1 577 / 0 / 0, three times**, two of them consecutively on the final code, against M3-09d's 1 551 —
**26 new rows, and the arithmetic lands to the row for the twelfth time this milestone.** The spec's
Tests table is **24 lines and 24 names**, all written; one of them,
`Frame_SlotPollSitsBesideTapToFocus`, is **PlayMode**, so 23 of the 24 are EditMode. Plus **3** more:
the two implied guards this task owes — one null row covering `SkillSlotInput`'s single public
constructor *and* `Construct`'s four arguments, and **one** non-finite row covering all three `float`
doors, because they take the same answer — and one the rule-1 ruling owes by name,
`Button_SharesNoBaseClassWithTheCharge`, which is what would redden if somebody ever extracted the
base class rule 1 refuses. 23 + 3 = **26**, and 1 551 + 26 = 1 577. No new spec type, so no validation
row. The rows sit in three files: **23** in `SkillBarPresenterTests`, **3** in `SkillRunnerTests`,
**1** in `FrameOrderTests`. PlayMode **16 / 16**, actually run
— the first task since M3-09a to run it — and `Ticker_RunsTheStepsInOrder` was green, against a
pre-edit baseline of **8 / 8** taken before a line was written. All eleven touched `.cs` files confirmed
in their intended assembly through `GetAssemblyNameFromScriptPath`: `Soulvail.Core` ×1,
`Soulvail.Game` ×6, `Soulvail.Tests.Core` ×1, `Soulvail.Tests.Game` ×2, `Soulvail.Tests.PlayMode` ×1
— **five** assemblies and no strays. `Hud.prefab` and `Run.unity` were both **read back off disk after
saving** (Traps §5). `ProjectSettings/TimeManager.asset` turned up dirty again and was reverted —
**six of the last seven tasks**.
