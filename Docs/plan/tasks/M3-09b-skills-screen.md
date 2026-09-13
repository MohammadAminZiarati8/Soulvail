# M3-09b — The Skills screen: every active you own, its trigger in words, and the switch

**Size:** M · **Depends on:** M3-09a (the panel it hangs off), M3-07a (the toggle and the two commands), M3-06 (the runner it reads) · **Branch:** `m3-09b-skills-screen`
**Design refs:** CC §6.1, §6.2, §6.3, §6.4, §6.5; CH §4, §4.2, §4.3; GD §13.1; AR §6, §8, §13, §18.2; ADR-0010, ADR-0012 · **Ledger rows:** **9** (its second reader, and the sharper one — a trigger line is a key *and* a number, so the composition has to survive M6-10), 4 (whether a row is legible and its switch tappable on a phone is device-only)

## Goal

CC §6.3's *"Pause → Skills"*: a list of every active the player owns, each with its cooldown, its auto-cast condition written out, and the Auto/Manual switch — and CC §6.2's full-slots question asked by the screen rather than refused by core.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/TriggerText.cs` | Core | `TriggerUnit`, and the two tables that turn a `TriggerClause` into a key and a kind of number — beside `TriggerSpec.cs`, whose enums it is addressed by |
| `Game/Presentation/SkillsPresenter.cs` | Game | the screen: builds the list, sends both commands, owns the prompt — **block namespace** (Traps §5) |
| `Game/Controls/SkillRow.cs` | Game | one row: name, cooldown, trigger line, switch — **block namespace** |
| `Tests/Core/Content/TriggerTextTests.cs` | Tests.Core | both tables walked over their enums |
| `Tests/Game/Presentation/SkillsPresenterTests.cs` | Tests.Game | over a real `RunSession` and hub, `LevelUpPresenterTests`' shape |
| `Prefabs/UI/Skills.prefab` | — | the panel and its row template. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Core/Combat/SkillRunner.cs` — `public float EffectiveCooldownOf(int index)` (rule 3); `Core/Run/RunState.cs` — the `SkillCooldownSeconds` read; `Game/Presentation/PausePresenter.cs` + the Skills button and its handler; `Prefabs/UI/Pause.prefab` gains that button (M3-09a rule 6); `Game/Composition/RunScope.cs` + `_skillsPresenter` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// What kind of number a TriggerField's threshold is, so a screen can format it without a
/// switch of its own. Closed: a field is one of these or the table is wrong (ADR-0010).
public enum TriggerUnit { Fraction, Count, Points, Seconds }

/// The vocabulary a CC §6.3 trigger line is written from. Keys and kinds, never text —
/// resolving a key is ILocalizer's job and it has no adapter until M6-10 (ledger row 9).
public static class TriggerText
{
    /// <summary>The key for one clause. One per (field, comparison) pair: word order is a
    /// localisation decision, so the threshold is the only substitution.</summary>
    public static LocKey KeyFor(TriggerField field, TriggerComparison comparison);

    public static TriggerUnit UnitOf(TriggerField field);
}
```

```csharp
// SkillRunner (added) — the seconds behind the fraction (rule 3)
public float EffectiveCooldownOf(int index);   // CooldownRules.Effective(authored, live)

// RunState (added) — a read, never the handle (AR §18.2)
public float SkillCooldownSeconds(int index);
```

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class SkillsPresenter : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; ScrollRect _scroll; SkillRow _rowTemplate;
        // [SerializeField] TMP_Text _emptyLabel; GameObject _prompt; SkillRow[] _promptRows (4);

        [Inject] public void Construct(IRunSession session, IPlayerCommands commands,
                                       DomainEventHub hub, ContentCatalog catalog);

        public void Open();
        public void Close();
    }
}

namespace Soulvail.Game.Controls
{
    public sealed class SkillRow : MonoBehaviour
    {
        // [SerializeField] TMP_Text _name, _cooldown, _trigger; Toggle _autoManual; TMP_Text _slot;

        public void Show(SkillSpec spec, float cooldownSeconds, bool isAuto, int slot,
                         Action<ContentId, bool> onSwitched);
        public void Hide();
    }
}
```

## Behaviour

**The trigger line**

1. **A key per (field, comparison) pair, and the threshold is the only substitution.** `trigger.hpFraction.below`, `trigger.incomingProjectiles.atLeast` — eighteen keys for nine fields, resolved by M6-10 into a format string with one placeholder: *"Player HP below {0}"*. A key per field with the comparison bolted on separately was the alternative and it is wrong for the reason word order is: *"below"* lands in different places in different languages, and a screen that concatenates three fragments has decided English's order for all of them. `KeyFor` is a switch over the pair with a loud `default`, the `PlayerStats.Resolve` shape (M3-05 rule 4), and `Trigger_EveryPairHasAKey` walks the cross product so a `TriggerField` added without its two keys fails in the suite.
2. **Core says what kind of number it is; the screen formats it.** `UnitOf` answers `Fraction` for `HpFraction` and `ShieldFraction`, `Count` for the three enemy counts, `IncomingProjectiles` and `FocusRampLevel`, `Points` for `Veilrot`, `Seconds` for `StationaryTime`. Turning 0.6 into *"60 %"* needs a culture and a format string, which is presentation; deciding that 0.6 **is** a fraction is content. The split is the same one `LocKey` makes everywhere else — core owns the identity of the thing, Game owns how it looks.
3. **The row shows the cooldown in seconds, and that needs one new read.** `RunState` today exposes `SkillCooldownFraction` and `IsSkillReady` (M3-06 rule 13) and neither is a duration. CC §6.3 asks for *"its cooldown"*, and on a screen where the tick is gated a radial fill is a frozen ring saying nothing — the number is the readable form. `SkillRunner.EffectiveCooldownOf` returns `CooldownRules.Effective(authored, live)` and `RunState.SkillCooldownSeconds` forwards it, so the screen shows **what the wait actually is** after the 40 % floor. The alternative was the screen applying the floor itself, which is the second copy of `CooldownRules` that M3-06 rule 1 exists to prevent.

**The list**

4. **Built on open, from four reads, and never polled.** `OwnedActiveCount`, `SkillIdAt(i)`, `IsAutoCast(id)`, `ManualSlotAt(slot)`, plus rule 3's seconds; `ContentCatalog.Skill(id)` carries the name, the kind and the trigger. Nothing changes underneath this screen while it is up — the tick is gated (M3-08a rule 10) — so a per-frame poll would redraw an unchanging list sixty times a second on the one screen GD §11.4 wants cheap. `HudPresenter`'s bargain, one screen further on.
5. **It redraws from `SkillAutoCastChanged`**, which M3-07a rule 9 publishes carrying the id, the state and the slot precisely so a list needs no second read. The only thing that can change while the screen is open is what the screen itself did, and hearing it back as an event is what keeps the row and the runner from disagreeing.
6. **Only owned Actives appear.** The runner holds nothing else (M3-06 rule 5), so *"passive skills have no toggle and no button"* (CC §6.1) is true by construction and needs no filter. A run owning none shows one line — a `LocKey` saying so — rather than an empty panel, because an empty panel and a broken panel look identical. **That is every run until M3-11 and M3-12 author an active**, which is what makes this rule worth a test rather than a remark.
7. **A `ScrollRect`, because `MaxActives` is twelve and landscape fits about five.** CH §4 puts ~25 % of 27 nodes at Active, so a full tree is about seven and the runner's ceiling is twelve (M3-06's `MaxActives`); a landscape phone's safe area holds roughly five 56 dp rows. A list that silently clipped the sixth would hide a skill the player owns. The rows themselves are a pooled template, `ViewPool`'s shape at a much smaller scale — twelve at most, instantiated once on first open.

**The switch and the prompt**

8. **The switch sends `SetAutoCast(id, value)` and asks nothing else.** `IPlayerCommands`, through the port M3-07a added; the row does not move a slot, count one, or decide anything — it reports a tap and redraws when the event comes back (rule 5).
9. **The full-slots question is asked by this screen, and it is the whole of M3-07a rule 2's other half.** Before sending `SetAutoCast(id, false)` the presenter reads `ManualSlotCount`: at four it shows CC §6.2's prompt — *"Manual slots full — which skill goes back to auto?"* with the four current occupants — **instead of** sending the command. Picking one sends **two ordinary commands in order**: the victim to Auto, then the requested skill to Manual. Cancelling sends none and leaves the switch where it was. Core never sees a fifth request, so its `InvalidOperationException` (M3-07a rule 2) stays unreachable in a live build and stays loud for the caller that forgets to ask — which is exactly the bargain `SpendLevelUp` and `ChooseOffer` already make.
10. **The prompt is modal within the screen and holds no pause of its own.** `RunPause` is already held by M3-09a and would throw on a second reason (M3-08a rule 12); a prompt raised over a screen raised over a paused game is one gate, not three. It takes the Skills panel's interactability away rather than adding a canvas.
11. **The slot a skill sits in is shown, and it is a position rather than a rank.** *"S1"*, *"S3"* — the number is `ManualSlotAt`'s index plus one, and a hole is legal and ordinary (M3-07a rule 3). A row showing *"2nd manual skill"* would be the compaction the whole slot design refuses.
12. **Nothing here writes a save.** The toggles are persisted by M3-07b at the next boundary, from the runner's own state; a screen that saved on close would be a second writer for a field whose only author is the recorder.

## Tests

| Test | Given / When / Then |
|---|---|
| `Trigger_EveryPairHasAKey` | for each `(TriggerField, TriggerComparison)` / `KeyFor` / a non-default `LocKey`, all eighteen distinct — a field added without its keys fails here (rule 1) |
| `Trigger_KeyShape` | `HpFraction`, `Below` / `KeyFor` / `trigger.hpFraction.below` |
| `Trigger_EveryFieldHasAUnit` | for each `TriggerField` / `UnitOf` / no throw; `HpFraction` Fraction, `EnemiesWithin6m` Count, `Veilrot` Points, `StationaryTime` Seconds (rule 2) |
| `Trigger_UnknownField_Throws` | `(TriggerField)99` / `KeyFor`, `UnitOf` / `ArgumentOutOfRangeException` each (rules 1, 2) |
| `Runner_EffectiveCooldownIsFloored` | an 8 s skill driven to 0.5 s by a stack / `EffectiveCooldownOf` / 3.2, not 0.5 (rule 3) |
| `State_ExposesCooldownSeconds` | a run holding a 2.5 s active / `SkillCooldownSeconds(0)` / 2.5; reflection says the runner is still not public (rule 3) |
| `List_ShowsOneRowPerOwnedActive` | a run owning two actives / `Open` / two rows, each naming its spec's key, its seconds and its trigger key (rules 1, 4) |
| `List_ShowsNoPassives` | a tree with two Passives taken and one Active / `Open` / one row (rule 6) |
| `List_EmptyRunShowsTheEmptyLine` | a run owning none / `Open` / no rows, the empty label on — the state of every run before M3-11 (rule 6) |
| `List_DoesNotPollWhileOpen` | the screen open / 60 frames / the row's text was written once (rule 4) |
| `Row_ShowsTheSlotAsAPosition` | A in S1, C in S3 / `Open` / the rows read *"S1"* and *"S3"*, not *"1"* and *"2"* (rule 11) |
| `Switch_SendsSetAutoCast` | an Auto skill / flip its switch / `SetAutoCast(id, false)` once, no other command (rule 8) |
| `Switch_RedrawsFromTheEvent` | the row above / — / the row shows Manual and its slot after `SkillAutoCastChanged` arrives, and not before (rule 5) |
| `Switch_AtFourShowsThePromptAndSendsNothing` | four manual, a fifth Auto skill / flip the fifth / the prompt is on listing the four, **zero** commands sent (rule 9) |
| `Prompt_ChoosingSendsTwoCommandsInOrder` | the row above / pick the S2 occupant / `SetAutoCast(victim, true)` then `SetAutoCast(requested, false)`, in that order; the requested skill lands in the freed slot (rule 9) |
| `Prompt_CancelSendsNothingAndRestoresTheSwitch` | the prompt up / cancel / no commands, the fifth skill still Auto, its switch back where it was (rule 9) |
| `Prompt_RaisesNoSecondPause` | the prompt up / — / `RunPause.Holder` is still `Menu`, held once, no throw (rule 10) |
| `Switch_ToAutoFreesTheSlotAndMovesNothing` | A in S1, B in S2, C in S3 / switch B to Auto / the rows show S1 and S3 occupied, S2 empty (rule 11, M3-07a rule 3 from this side) |
| `Open_FromThePausePanel` | the pause panel up / tap Skills / the Skills root on, the panel's other buttons non-interactable (M3-09a rule 6) |
| `Close_ReturnsToThePausePanel` | the Skills screen up / Close / the Skills root off, the pause panel interactable, `RunPause.Holder` still `Menu` (rule 10) |
| `Screen_ScrollsPastFive` | a run owning eight actives / `Open` / eight rows exist and the content rect is taller than the viewport (rule 7) |
| `Screen_WritesNoSave` | the screen opened, two switches flipped, closed / — / the store was asked for no write (rule 12) |
| `Card_DrawsTheKeyNotEnglish` | a spec with `skill.oathbound.consecrate` and an `HpFraction Below 0.6` trigger / `Open` / the row reads that id and `trigger.hpFraction.below` with *"60 %"* — ledger row 9's second reader, visible rather than a surprise at M3-15 (rules 1, 2) |
| `Subscribes_FromConstruct` | a presenter constructed after `RunStarted` / publish `SkillAutoCastChanged` / it redraws (rule 5) |
| `Destroy_DropsSubscriptions` | a constructed presenter / `OnDestroy`, then publish / nothing throws |
| `Prefab_IsDressed` | `Skills.prefab` / read the fields back after a save (Traps §5) / root, scroll rect, row template, empty label, prompt and its four rows present |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with no tree authored. Pause → Skills. One line saying nothing is owned, and the screen is obviously working rather than obviously broken (rule 6).
2. **[Editor]** With a hand-authored tree holding two Actives, take both. Pause → Skills: two rows, each showing its key, its cooldown in seconds and its trigger key. Flip one to Manual — the row gains *"S1"* immediately.
3. **[Editor]** Take four Actives, set all four to Manual, then try a fifth. The prompt appears listing the four; pick one and the fifth takes the freed slot. Cancel instead, and nothing moves (rule 9).
4. **[Editor]** Resume from the pause panel. The skill you set to Manual no longer auto-casts (M3-07a rule 5), which is the whole point of the screen.
5. **[device]** Ledger row 4: whether a 56 dp row with a switch at one end is comfortably tappable, and whether the trigger line fits one line on a phone once M6-10 makes it words. Deferred with the rest.
6. **[device]** Ledger row 9's real question, and it cannot be answered here: whether *"Player HP below 60 %"* is readable in the two seconds GD §13.1 allows. Today the row says `trigger.hpFraction.below`, and M3-15 has to rule on that.

## Out of scope

- **The first-active hint** (CC §6.3) — **M3-09c**, which is where the per-install flag and the profile bump that carries it live.
- **The tree view** — M3-09d.
- **Drag-to-reorder** (CC §6.2) — **not in V1**, ruled at M3-00c. M3-07a framed it as *"M3-09 or not at all in V1"*; the v3 save already carries slot **positions** rather than a set (M3-07b rule 1), so adding `SetSkillSlot(skillId, slot)` later costs one port member and a drag handler and **no format bump**. Parking lot.
- **Editing a trigger condition.** CC §6.4 is explicit: conditions are authored, never configured, because the alternative is shipping a rules-engine UI on a phone.
- **Resolving a `LocKey`** — M6-10, ledger row 9. Rule 1 builds the key and rule 2 the number beside it.
- **A cooldown fill on the row.** The tick is gated, so it would be frozen; rule 3 shows the number instead. M3-10 draws the live fills on the buttons, where the game is running.
- **Showing a skill's effects.** The row is name, cooldown, trigger and switch — CC §6.3's four. What a skill *does* is the tree view's (M3-09d) and the offer card's (M3-08b).

## As built

_Filled at merge._
