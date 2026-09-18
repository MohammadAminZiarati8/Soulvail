# M3-07a — Auto/Manual, the four manual slots, and the two commands

**Size:** S (no new production files; `SkillRunner` and its fixture substantially extended) · **Depends on:** M3-06 · **Branch:** `m3-07a-auto-manual-and-slots`
**Design refs:** CC §6.1, §6.2, §6.3, §6.5; CH §4.3; GD §5.2, §6.3, §16.1; AR §6, §8, §18.2, §18.3; ADR-0003, ADR-0010 · **Ledger rows:** none — row 2's second bump is M3-07b's, deliberately its own review (rule 11)

## Goal

Every owned active carries CC §6.1's switch: Auto fires itself, Manual never does and takes one of four slots the player casts from. The two commands exist, the fifth slot is refused in a way a screen can turn into CC §6.2's question, and nothing is written down yet.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| *small edits* | | `Core/Ports/IPlayerCommands.cs` — `CastSkill(int slot)` and `SetAutoCast(ContentId, bool)`, the two members AR §6 has listed as "with M3-06/07" since M0-09; `Core/Run/RunSession.cs` — both, through `RequireRunning`, `MovementSkill`'s shape; `Core/Combat/SkillRunner.cs` — an `IsAuto` flag on the entry, a four-long slot table, `SetAutoCast`, `SlotAt`, `CastSlot`, and the skip in `Tick`'s walk (rule 5); `Core/Events/SkillEvents.cs` — `SkillAutoCastChanged`; `Core/Run/RunState.cs` — three reads; `Game/Presentation/DebugOverlay.cs` — the occupied slot count |
| *tests* | Tests.Core | `SkillRunnerTests` gains the toggle, the slots and the ceiling; `RunSessionTests` gains the two commands' running-guard and forwarding rows |

Only these files change. Anything else is a deviation: say so in *As built*. **`RunSnapshot.CurrentVersion` stays 2** — M3-07b is the bump.

## Public API

```csharp
// IPlayerCommands (added) — the port grows a member when the mechanic lands (AR §6)

/// <summary>Casts the skill in a manual slot. S1 is slot 0.</summary>
void CastSkill(int slot);

/// <summary>Moves a skill between CC §6.1's two states. Manual takes the lowest free slot.</summary>
void SetAutoCast(ContentId skillId, bool auto);
```

```csharp
// SkillRunner (added)
public const int MaxManualSlots = 4;          // CC §6.2: the ergonomic ceiling for one thumb

public int ManualSlotCount { get; }           // occupied slots, 0..4
public bool IsAuto(ContentId skillId);        // true for every owned active until it is switched
public ContentId SlotAt(int slot);            // default(ContentId) for an empty slot
public IReadOnlyList<ContentId> Slots { get; } // always MaxManualSlots long; M3-07b writes this

public void SetAutoCast(ContentId skillId, bool auto);   // throws at the ceiling (rule 2)
public bool CastSlot(int slot, float now);               // false while it is cooling
```

```csharp
namespace Soulvail.Core.Events;

/// A skill's Auto/Manual switch moved. Slot is −1 for Auto.
public readonly struct SkillAutoCastChanged
{
    public readonly ContentId SkillId; public readonly bool IsAuto; public readonly int Slot;
}
```

```csharp
// RunState (added) — reads, never the handle (AR §18.2)
public int ManualSlotCount { get; }
public ContentId ManualSlotAt(int slot);
public bool IsAutoCast(ContentId skillId);
```

## Behaviour

1. **Auto is the default and it is a flag on the entry, not a separate table.** Cooldown, trigger and whether a skill fires itself are three facts about one skill, and M3-06 already built the object that owns the first two; a second class holding the third would need the first one's index to mean anything and would be handed the same `Add` twice. Every active is Auto the moment it is added (CC §6.1: *"Every skill defaults to Auto. A player who never opens the menu has a complete, playable game with one button"*).
2. **Four slots, and the fifth is refused loudly.** `SetAutoCast(id, auto: false)` with four occupied throws `InvalidOperationException` naming the ceiling — `LevelTracker.SpendLevelUp`'s precedent, M3-01a rule 8's *"spending a pick nobody earned is a bug in the caller and not a state"*. **The UI is required to ask `ManualSlotCount` first**, and CC §6.2's *"Manual slots full — which skill goes back to auto?"* is a prompt shown **instead of** sending the command; taking the answer is two ordinary commands, the victim to Auto and then the requested one to Manual. That is how *"never silently refuse, and never silently swap"* is met with no mechanism of its own: core refuses a state it cannot reach, and the screen does the asking. The prompt itself is M3-09's.
3. **A slot is a position, not a place in a list.** Switching to Manual takes the **lowest free** slot; switching back to Auto empties that slot and **nothing else moves**. CC §6.2 draws four fixed thumb positions and says unused slots are not drawn, so compacting would slide S3's skill under the thumb that had learned S2 — a silent re-bind of muscle memory as the reward for dropping a skill. `SlotAt` answers `default(ContentId)` for an empty one, which is the same "nobody" every id in this project spells that way.
4. **`CastSkill(slot)`** forwards to `CastSlot`, which is M3-06's direct `Cast` with the trigger ignored. Out of range throws `ArgumentOutOfRangeException`; an **empty** slot throws `InvalidOperationException`, because CC §6.2 does not draw a button for one and a command from a button that is not on screen is a wiring mistake — `IPlayerCommands`' own standing rule, *"a silent no-op would hide it"*. A slot that is merely **cooling** is not an error and returns false: that is an ordinary early tap, and CC §6.2 answers it with 40 % opacity and no tap response rather than with a buffer (M3-06's Out of scope says why the movement skill's buffer does not generalise).
5. **A Manual skill never auto-casts**, which is the whole of the toggle: M3-06 rule 6's walk skips an entry whose `IsAuto` is false, before it asks the cooldown or the trigger. Asked in that order deliberately — a Manual skill's trigger is never evaluated, so switching a skill to Manual is also how a player stops paying for a condition they have decided to judge themselves (CC §6.5).
6. **The movement skill is not in this and never counts against the four** (CC §6.1). `MovementSkill()` stays its own command against `ChargeSkill`, which has no `SkillSpec`, no trigger and no entry here. CC §6.2's *"4 manual slots, plus the always-present movement button"* is therefore true by construction rather than by an exemption written somewhere.
7. **Only an owned Active has a switch.** `SetAutoCast` for an id the runner does not hold throws `KeyNotFoundException` naming it; a Passive, an Upgrade and a Keystone never reach the runner at all (M3-06 rule 5), so *"passive skills have no toggle and no button"* needs no check of its own.
8. **Changeable any time, including mid-run** (CH §4.3, CC §6.3), and switching costs nothing: a skill moved to Manual mid-cooldown **keeps the cooldown it was on**, and moving it back does not restart it. The tree is acquired during play, so the management screen has to work during play, and a switch that reset a cooldown would make opening that screen a tactical decision — which is the opposite of what CC §6.3 wants it to be.
9. **`SkillAutoCastChanged` is published after the change**, carrying the slot (−1 for Auto) so that M3-09's list and M3-10's buttons redraw from the event and need no second read. Published only when something moved: `SetAutoCast(id, true)` on a skill already Auto is a no-op with no event, because AR §8 says an event describes what happened.
10. **`RunState` hands out three reads and never the runner** — the same seal M3-06 rule 13 put on it, and now with a sharper reason: `SetAutoCast` and `CastSlot` are public, so a handle would let a view fire the player's skills *and* rearrange their thumb.
11. **Nothing here is written to disk.** The four slots and the flags live for the run and die with it — until M3-07b, which is the bump and is deliberately a separate review (ledger row 2's whole point, and the seam M3-01a/M3-01b was split at). A build stopped between the two tasks loses a loadout on a kill-from-recents and that is a known, named gap rather than an omission.

## Tests

| Test | Given / When / Then |
|---|---|
| `Added_SkillIsAuto` | one active / `Add` / `IsAuto` true, `ManualSlotCount` 0, `SlotAt(0)` default (rule 1) |
| `SetManual_TakesTheLowestFreeSlot` | three actives / manual A, then C / `SlotAt(0)` A, `SlotAt(1)` C, `ManualSlotCount` 2 (rule 3) |
| `SetAuto_EmptiesTheSlotAndMovesNothing` | A in 0, B in 1, C in 2 / auto B / `SlotAt(1)` default, `SlotAt(2)` still C, `ManualSlotCount` 2 (rule 3) |
| `SetManual_RefillsTheHole` | the state above / manual D / D lands in slot 1 (rule 3) |
| `SetManual_FifthThrowsNamingTheCeiling` | four occupied / manual a fifth / `InvalidOperationException`, message names 4 and `ManualSlotCount`; nothing moved, no event (rule 2) |
| `SetManual_AfterFreeingASlot_Succeeds` | four occupied / auto one, then manual the fifth / it takes the freed slot — CC §6.2's answer, as two commands (rule 2) |
| `SetAutoCast_UnknownSkill_Throws` | an id the runner does not hold / — / `KeyNotFoundException` naming it (rule 7) |
| `SetAutoCast_Idempotent_PublishesNothing` | an Auto skill / `SetAutoCast(id, true)` / no event, no change (rule 9) |
| `SetAutoCast_PublishesTheSlot` | manual A, then auto A / — / two `SkillAutoCastChanged`: `(A, false, 0)` then `(A, true, −1)` (rule 9) |
| `Manual_NeverAutoCasts` | one active, trigger permanently met / manual it, then 100 × `Tick` / no `SkillCast` (rule 5) |
| `Manual_TriggerIsNotEvaluated` | a counting `TriggerSpec` fake / manual it, 100 × `Tick` / zero evaluations (rule 5) |
| `Auto_StillCastsWhileAnotherIsManual` | two actives, both triggered, one manual / `Tick` / the Auto one fires (rules 1, 5) |
| `Switch_KeepsTheRunningCooldown` | cast an 8 s skill, at +2 s switch to Manual and back / — / ready at +8 s both times, `CooldownFraction` continuous (rule 8) |
| `CastSlot_FiresRegardlessOfTheTrigger` | manual, trigger not met, ready / `CastSlot(0, now)` / true, one `SkillCast` with `WasAuto` false (rule 4) |
| `CastSlot_WhileCooling_IsFalse` | manual, cooling / `CastSlot` / false, no event, nothing applied (rule 4) |
| `CastSlot_Empty_Throws` · `CastSlot_OutOfRange_Throws` | slot 2 empty; slot −1 and slot 4 / `CastSlot` / `InvalidOperationException`; `ArgumentOutOfRangeException` (rule 4) |
| `Slots_AreAlwaysFourLong` | nothing manual / `Slots` / four entries, all default (rule 3 — the shape M3-07b writes) |
| `SetAutoCast_AllocatesNothing` | warm-up / 10 000 × manual-then-auto, `SilentEvents` / allocated-bytes delta == 0 |
| `Commands_ThrowWhenNoRunIsRunning` | a session before `Start` and after `End` / `CastSkill(0)`, `SetAutoCast` / `InvalidOperationException` each — `IPlayerCommands`' standing rule, `RunSessionTests` |
| `Commands_ForwardToTheRunner` | a run holding one manual active / `CastSkill(0)` through the port / one `SkillCast` — the port is wired to the same instance `IRunSession` is (`RunInstaller`) |
| `MovementSkill_DoesNotCountAgainstTheSlots` | four manual actives / `MovementSkill()` / the dash still fires; `ManualSlotCount` still 4 (rule 6) |
| `Snapshot_CarriesNoLoadout` | reflection over `RunSnapshot`; a run with two manual skills / a boundary `Take` / `CurrentVersion` is still 2 and no member carries a slot — the pin that says M3-07b is a separate review rather than a forgotten line (rule 11) |
| `State_HandsOutNoRunner` | *the existing row* / — / unchanged; the three new members are reads (rule 10) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None visible. Nothing owns an active until M3-11 and M3-12, and no screen sends either command until M3-09 and M3-10 — the honest check is the suite and six assemblies. `DebugOverlay` showing `slots 0/4` is what makes the first manual switch visible as a number before a button exists.

## Out of scope

- **Persisting any of it** — M3-07b, rule 11.
- **The Skills screen** — M3-09: the list, the Auto/Manual switch, CC §6.3's plain-language trigger line, CC §6.2's full-slots prompt, and CC §6.3's one-time hint the first time a level-up grants an active.
- **The buttons** — M3-10: S1–S4, 60 dp, radial fills, and the 40 % opacity that makes rule 4's *false* visible.
- **Reordering.** CC §6.2's drag-to-reorder needs one more command (`SetSkillSlot(skillId, slot)`) and a drag handler; a port grows a member when the mechanic lands (AR §6), and the mechanic lands in M3-09 or not at all in V1.
- **Precision mode** (CC §3.8) and anything else that changes how a *cast* is aimed. A slot says when; nothing here says where.
- **A cast queue or buffer.** M3-06's Out of scope argues it; nothing changes here.

## As built

**Built as specced, with four deviations and one ruling the spec asked for.**

### The four deviations

1. **Two files outside the Files table had to gain the two members: `Tests/PlayMode/FrameOrderTests.cs` (`RecordingCore`) and `Tests/Game/Composition/ResumeFlowTests.cs` (`RecordingSession`).** Both are test doubles implementing `IPlayerCommands`, and growing a port breaks every implementer — the compiler found exactly these two and nothing else. `RecordingCore` records both like its other three commands; `RecordingSession`'s are empty like its other three. **Neither fixture sends either command**, so no row changed behaviour. This is the unavoidable cost of AR §6's *"a port grows a member when the mechanic lands"* and is named here rather than absorbed.
2. **`Docs/Architecture.md` §6, one row, one word.** The `IPlayerCommands` row has said `CastSkill(slot)` and `SetAutoCast(skillId, bool)` *"with M3-06/07"* since M0-09; it now says `(M3-07a)` beside the other three. M3-06's precedent — it edited AR §5's `Combat` row to add `SkillRunner` and `CooldownRules` in the same shape. A promise that has come true and still reads as a promise is the one kind of doc rot nobody re-reads.
3. **`Docs/Traps.md` §7 gained the throwing-clause technique**, on the owner's ruling, generalised past triggers: *to prove a sealed, non-virtual collaborator was never asked, hand it an input that throws the moment it is used* — with the warning that it needs its control or it proves nothing. See rule 5's row below.
4. **`SkillRunnerTests.StartSession` gained an `int actives = 1` parameter.** `RunSessionResumeTests.BuildWithActiveTree`'s shape and its reason. Inside a file the table names, so a change rather than a deviation in the strict sense — recorded because it altered a helper three M3-06 rows depend on, and all three stayed green.

**`RunSnapshot.CurrentVersion` is still 2**, and `Snapshot_CarriesNoLoadout` is the row that pins it.

### Where the rows live, which the spec got wrong in one place

**`RunSessionTests` cannot host `Commands_ForwardToTheRunner`.** Its catalog is the three-argument `ContentCatalog` overload (`RunSessionTests.cs:91`) — no skills, no trees — so `RunState.Tree` is null in every row and `OwnedActiveCount` is 0. Only the running-guard row went there, where it belongs: the guard is reached *before* either argument is looked at, which that fixture proves by having neither a slot nor an owned id.

**Everything else went to `SkillRunnerTests`, and no third file was touched.** That fixture already starts a whole `RunSession` holding an Active — `StartSession`, built by M3-06 for the two ordering rows — so `Commands_ForwardToTheRunner`, `MovementSkill_DoesNotCountAgainstTheSlots` and `Snapshot_CarriesNoLoadout` all had a live run to hand. `RunSessionResumeTests` was **not** touched; `BuildWithActiveTree` was the model for `StartSession`'s new parameter rather than a second home.

### The rule 2 question the owner asked: does M3-06's ruling carry? **No, and the distinction is the reachability of the state.**

At M3-06 the owner ruled *against* a throw at the thirteenth `Add`, and moved the refusal to `RunSession.Start`. It looks identical to this one from a distance and it is not, on three counts:

- **M3-06's was an authoring mistake; this is a player action.** A tree holding more Actives than the runner can own is a fact about a `SkillTreeSpec` a designer typed. Nobody can reach it by playing, and no screen can explain it, so the only honest place to refuse it is before the run is announced. A fifth manual skill is a state the player walks into by owning five Actives and liking four of them.
- **This refusal has a designed prompt and M3-06's had none.** CC §6.2 writes the words — *"Manual slots full — which skill goes back to auto?"* — and M3-09 owns the screen that shows them. The throw is what makes that prompt mandatory: a screen that skips it gets an exception rather than a silent swap, which is exactly *"never silently refuse, and never silently swap"* met with no mechanism of its own.
- **The timing argument that settled M3-06 does not apply.** There, the throw landed *mid-`ChooseOffer`* — after `Take`, after the effects, after `NodeTaken`, after the pick was spent — so it left the run dirty. `SetAutoCast` has nothing upstream of it: it throws before it writes, `SetManual_FifthThrowsNamingTheCeiling` asserts that nothing moved and nothing was published, and the caller can simply not send it.

`LevelTracker.SpendLevelUp`'s precedent is the right one, not `Add`'s: *spending a pick nobody earned is a bug in the caller and not a state.*

### The shapes the three inherited constraints forced

- **The skip is the first statement in `Tick`'s loop**, above both existing `continue`s. Rule 5's ordering is the whole of CC §6.5, and it is invisible to a behavioural row — `Manual_NeverAutoCasts` stays green with the skip placed *below* the trigger test, because the skill still never fires. That is why `Manual_TriggerIsNotEvaluated` is a separate row.
- **`IsAuto` is `bool[MaxActives]` beside the three; `_slots` is `ContentId[MaxManualSlots]`.** Not a fifth array of twelve: a slot is a position on a screen, so *"what is in S3?"* is one read rather than a scan for the entry claiming 3.
- **`_slotsView` is wrapped once in the constructor.** `TriggerSpec._clausesView`'s idiom. `Slots_IsTheSameInstanceEveryCall` is the identity pin and `SetAutoCast_AllocatesNothing` was **widened** to read `Slots`, `ManualSlotCount`, `SlotAt` and `IsAuto` inside the measured body — the spec's version wrote only, so a per-call `Array.AsReadOnly` would have sailed through it. Both, rather than one: the probe catches the allocation, the `ReferenceEquals` says out loud that it is the same object.

### The rows worth naming as evidence rather than as counts

- **`Manual_TriggerIsNotEvaluated` has a control, and without it the row is worthless.** A `DoesNotThrow` over 100 ticks is green against a runner that never reached the trigger *for any reason* — a cooldown, a bad index, an empty walk. So the same unreadable spec is first left **Auto** and asserted to throw on the first tick. The pair is airtight because a Manual skill never casts, so `_readyAt` stays `0f` and the cooldown `continue` never fires: across all 100 ticks the `IsAuto` skip is the only thing that can explain the silence.
- **`SetManual_Idempotent_TakesNoSecondSlot` is an implied guard row and it is the dangerous half of rule 9.** The rule names only the idempotent `true` case. `SetAutoCast(id, false)` on an already-Manual skill must not take a second slot, or one skill occupies two, `ManualSlotCount` reaches 4 with two skills owned, and the ceiling throws for a reason no screen can explain.
- **`Auto_StillCastsWhileAnotherIsManual` fails on `return` where it should `continue`.** A is first in the walk order and Manual; a skip that ended the walk would leave B silent for ever.
- **`SetAuto_EmptiesTheSlotAndMovesNothing` and `SetManual_RefillsTheHole` are one rule from both ends.** The first fails on a runner that compacts, the second on one that appends at `ManualSlotCount` — and only the pair pins *"the lowest free slot, and nothing else moves"*.

### The non-finite row: trusted, not guarded

`CastSlot(int slot, float now)` is a new `float` door, so the row is owed and `CastSlot_NonFiniteNow_IsFalse` pays it. **M3-06's answer followed exactly**: no guard, with the safety in the spelling — `Cast`'s `!(now >= _readyAt[index])` makes an unreadable clock take the refusing branch. The snapshot is the door (AR §18.2) and every other `Tick` in core trusts the clock it is handed.

### Ledger

**M3-07a names no rows, and all nine were checked row by row rather than trusted to the header.** **Row 2 was honoured by writing nothing for the fourth time** — `RunSnapshot.CurrentVersion` stays 2, no field, no step, no fixture, and `Snapshot_CarriesNoLoadout` is the row that makes that a decision rather than an omission; **M3-07b is the bump.** The open question M3-06 handed forward — a resumed run's cooldowns come back at zero — is **M3-07b's and was deliberately not answered here.** Rows 3 and 7 are closed. **Row 1** unmoved (no content, no damage). **Row 4** gains nothing measurable: the `Tick` skip makes a Manual skill *cheaper* per frame, not dearer, and the Profiler line stays unmet on a phone like the rest — but **M3-10a's multi-touch row now has its core half built**, so the one device row that risks a feature rather than a verdict is that much closer to being answerable. **Row 5** unmoved (no Animator). **Row 6** unmoved (nothing drawn, no `Color` added — the overlay line is text). **Row 8** unmoved (nothing paused). **Row 9** unmoved: what this hands out is `ContentId`s, never a `LocKey`.
