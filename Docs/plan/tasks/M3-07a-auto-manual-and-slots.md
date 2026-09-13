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

_Filled at merge._
