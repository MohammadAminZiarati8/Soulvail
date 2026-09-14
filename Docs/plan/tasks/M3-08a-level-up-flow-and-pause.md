# M3-08a — `IProgressionCommands`, the lazy offer, Overflow, and the gate that idles the simulation

**Size:** M · **Depends on:** M3-04 (the generator), M3-03 (the tree), M3-06 (the runner a new Active is handed to), M3-05, M3-01a · **Branch:** `m3-08a-level-up-flow-and-pause`
**Design refs:** GD §5.2, §7.3, §11.4, §13.1; CH §5.1, §5.2; AR §4.3, §5, §6, §7, §8, §18.1, §18.2, §18.3; ADR-0003, ADR-0004, ADR-0011 · **Ledger rows:** 1 (Overflow is the second source of the power GD §12.5 asks for, and rule 8 does its arithmetic), 4 (the frame-rate drop and `Time.timeScale` are device-only), 8 (rule 11 says what the pause does and does not do to the number M3-15 has to measure)

## Goal

A pick that is owed becomes three cards' worth of state, drawn at the moment the screen opens and not a tick earlier; a pick with nothing left to offer becomes CH §5.2's Overflow; and the run stops being ticked while the player is choosing, exactly as GD §11.4 says — *idle the simulation*. No screen yet.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/IProgressionCommands.cs` | Core | the third inbound command port (AR §6), with the two members M3 needs and none of M6's |
| `Core/Progression/LevelUpFlow.cs` | Core | the generator, the offer buffer, `Open`, `Choose`, and Overflow — `StageFlow`'s shape, one milestone later |
| `Game/Composition/RunPause.cs` | Game | the gate: who holds the pause, and the two globals GD §11.4 asks for |
| `Tests/Core/Progression/LevelUpFlowTests.cs` | Tests.Core | opening, choosing, Overflow, the empty tree, and the draw's position against the boundary |
| `Tests/Game/Composition/RunPauseTests.cs` | Tests.Game | one holder at a time, and the globals restored on every exit |
| *small edits* | | `Core/Events/ProgressionEvents.cs` + `OfferPresented`, `LevelUpClosed`, `OverflowGranted`; `Core/Run/RunSession.cs` — builds the flow in `Start` beside the tree, forwards both port members through `RequireRunning`, and derives Overflow on resume (rule 9); `Core/Run/RunState.cs` + `internal LevelUpFlow LevelUp` and three reads; `Game/Composition/RunTicker.cs` — `LevelUpPhase()` above `CommandPhase` and the pause return (rule 4); `Game/Composition/RunInstaller.cs` registers `RunPause` and `RunSession` `.As<IProgressionCommands>()`; `Game/Composition/BootFlow.cs` sets `Time.timeScale = 1f` beside its frame-rate line (rule 13); **AR §6**'s `IProgressionCommands` row gains `OpenLevelUp` and loses its M6 members' claim to this milestone; **AR §18.1** gains the level-up phase's row |
| *ripple* | | `RunTicker`'s constructor gains two arguments — `Tests/PlayMode/FrameOrderTests.cs` is the one fixture that builds one (eighteen arguments today, six of them scene objects) |

Only these files change. Anything else is a deviation: say so in *As built*. **`RunSnapshot.CurrentVersion` stays 3** — Overflow is derived, not stored (rule 9).

## Public API

```csharp
namespace Soulvail.Core.Ports;

/// What the player asks of their own progression, as opposed to of their character (IPlayerCommands)
/// or of the run (IRunSession). AR §6's third inbound port; RunSession implements all three.
public interface IProgressionCommands
{
    /// <summary>Draws the offer for the pick that is owed, or spends it as Overflow.</summary>
    void OpenLevelUp();

    /// <summary>Takes the offered node at <paramref name="index"/> and spends the pick.</summary>
    void ChooseOffer(int index);
}
```

```csharp
namespace Soulvail.Core.Progression;

public sealed class LevelUpFlow
{
    public const float OverflowDamage = 0.02f;   // CH §5.2, PercentAdd
    public const float OverflowMaxHp  = 0.02f;   // CH §5.2, PercentAdd

    public LevelUpFlow(SkillTree tree, LevelTracker progression, SkillRunner runner,
                       EffectRegistry effects, IDomainEvents events);

    public bool HasOffer { get; }
    public IReadOnlyList<ContentId> Offer { get; }   // a view over one buffer; empty when none
    public int OverflowLevels { get; }

    public void Open(IRandomStream offers);          // draws, or grants Overflow, or does nothing
    public void Choose(int index, IRandomStream offers);
    internal void GrantOverflow(int times);          // silent; the resume path (rule 9)
}
```

```csharp
namespace Soulvail.Core.Events;

/// An offer is on the table. The ids are RunState.Offer; this says how many and what it is for.
public readonly struct OfferPresented { public readonly int Count; public readonly int PicksOwed; }

/// Nothing more is owed. The screen closes.
public readonly struct LevelUpClosed { public readonly int Level; }

/// A pick was spent with nothing left to offer (CH §5.2).
public readonly struct OverflowGranted { public readonly int Level; public readonly int Total; }
```

```csharp
// RunState (added)
internal LevelUpFlow LevelUp { get; }                  // null for a class with no tree (M3-03 rule 10)
public bool HasOffer { get; }
public IReadOnlyList<ContentId> Offer { get; }
public bool IsLevelUpPending { get; }                  // a pick is owed, no offer is open, and a tree exists
```

```csharp
namespace Soulvail.Game.Composition;

public enum PauseReason { LevelUp, Menu }               // M3-09 is the second member's first caller

public sealed class RunPause : IDisposable
{
    public bool IsPaused { get; }
    public PauseReason? Holder { get; }

    public void Pause(PauseReason reason);              // throws when another reason holds it
    public void Resume(PauseReason reason);             // throws when that reason does not hold it
}
```

## Behaviour

**The offer**

1. **The draw is lazy, and that is the whole of M3-04's inherited rule.** `Open` is called on the frame **after** the tick that earned the level, never inside it. The boundary snapshot is taken on entering `Clear` — the same tick as a stage's last kill, which is the kill most likely to level (AR §18.1, M2-14a) — so it captures the `Offers` stream **before** any draw, and a run killed with a pick owed rolls **the same three nodes** on resume. Drawn in-tick instead, the capture would record a position the draw had already advanced and killing the app would be a free reroll, which is exactly the hole M3-04 names. This task owes the test M3-04 named: `Offer_IsDrawnAfterTheBoundarySnapshot`.
2. **`Open` asks `Draw`, and the answer decides everything** — `IsFull` is deliberately not the test. M3-04 rule 1 writes nothing and makes no draw when nothing is available, and "nothing available" is *not* the same as "tree full": a branch whose remaining nodes are Upgrades of an untaken parent can be blocked while the tree is half empty, and all three blocked at once is content this build cannot yet refuse (M3-14 is where a tree is checked for it). So: **`count > 0` → publish `OfferPresented(count, PendingLevelUps)` and stop; `count == 0` → Overflow.** The honest condition is what the generator returned, not what the tree looks like.
3. **`Open` loops while picks are owed**, because a grant can cross several thresholds (M3-01a rule 4) and the first two may both be Overflow. It stops at the first pick that draws something, so at most one offer is on the table at a time, and `PicksOwed` on the event is what CH §5.1's screen shows as *"pick 1 of n"*.
4. **`RunTicker` decides *when*, and it is three lines in the file that already owns the frame.** `LevelUpPhase()` sits above `CommandPhase`, below the `IsRunning` guard: if `State.IsLevelUpPending`, call `OpenLevelUp()`; then, if the pause is held, return. This is `CommandPhase`'s own argument — an ordering decision belongs in the one file that writes the frame down, not in whatever order a presenter's `Update` happens to run, and AR §18.1's last row already says a view that answers core cannot own its own `Update`. It is also what makes rule 1 true: the call happens between ticks, with nothing half-decided.
5. **A run with no tree never reaches any of it.** `IsLevelUpPending` is false when `State.Tree` is null (M3-03 rule 10), so the level is **banked** — `PendingLevelUps` climbs and nothing draws, nothing pauses, nothing throws. That is every run between here and M3-12, and it is why the read is a single question rather than the presenter asking three.
6. **`ChooseOffer(index)` is one ordered sequence**: `SkillTree.Take(id)` (which publishes `NodeTaken` with the effects already applied — M3-03 rule 4), then `LevelTracker.SpendLevelUp()`, then — **if the node is an Active — `SkillRunner.Add(spec)`, directly**, because core does not subscribe to its own events (M3-03 rule 7). Then the offer is cleared and `Open` runs again: another pick draws a fresh offer over the **new** tree state, so the second card of a double level-up can be the node the first one just unlocked; no pick left publishes `LevelUpClosed`. Guards: no offer open throws `InvalidOperationException`, an index outside `[0, Count)` throws `ArgumentOutOfRangeException`. There is **no `OfferChosen` event** — `NodeTaken` already says what happened, and AR §8 asks events to describe rather than duplicate.

**Overflow**

7. **Overflow is silent and instant: no pause, no screen.** CH §5.2 makes it a consolation for a level with nowhere to go, and GD §13.1's pause exists to let someone *choose*. Pausing a fight to show a card with one button would tax the player for the game having run out of nodes. It is announced instead — `OverflowGranted` carries the level and the running total, so M3-10's HUD or M3-13 can toast it — which is what keeps CH §5.2's *"levelling never stops meaning something"* visible without stopping the game.
8. **Two `ModifyStat`s per Overflow level, `PercentAdd`, under one source for the whole run.** `WeaponDamage` +2 % and `MaxHp` +2 % (CH §5.2), applied through the registry with the flow itself as the source (M3-05 rule 7). `PercentAdd` and one source means they pool: ten Overflow levels is **×1.20**, not 1.02¹⁰ — GD §13.1's *"additively within a family"* and ADR-0008's order. Raising `MaxHp` is **not a heal** (M3-05's `Handler_MaxHpMovesHealthLive`). **The arithmetic, against ledger row 1:** a 27-node tree is full at level 28, which M3-01a rule 9 puts at about stage 20; by stage 30 the same curve gives level ≈ 42, so **14 Overflow levels — +28 % damage and +28 % max HP**. Row 1 says a Husk at stage 30 needs +52 % to die in five hits, so **Overflow alone supplies more than half of it**, and the flagged early-filling curve is partly self-correcting rather than simply wrong. M3-12 still owes the other half and M3-15 still measures.
9. **Overflow is derived on resume and no field is added.** Every pick a run has earned is spent on a node, spent on Overflow, or unspent, so `Overflow = Level − 1 − TakenNodeCount − PendingLevelUps` — the exact mirror of M3-03 rule 6, which says the *pending* count cannot be derived. `RunSession.Start` computes it after the tree is restored and calls `GrantOverflow(n)` silently, **above `Health.Restore`** because the `MaxHp` modifiers have to be on before absolute hit points are clamped (M3-03 rule 5's reason, for a second writer). A negative result is a save whose picks do not add up and throws `ArgumentException` naming the four numbers: it is arithmetic, not content, so it cannot be rescued by clamping. **Anything that later spends a pick without taking a node owes this identity or owes a field** — M6-02's Banish does not, since it removes a node from the pool rather than a pick from the player.

**The pause**

10. **Gate the tick; do not tick with a frozen clock.** GD §11.4 says the pause menu *"idles the simulation"*, and the literal reading is also the correct one: `RunTicker.Tick` returns before the snapshot is built, so core is not ticked at all and AR §18.1's frame order is untouched — that order is a statement about the inside of a tick, and skipping whole frames reorders nothing. A `Dt = 0` tick would still walk the entire pipeline: targeting re-resolves, the director and the flow are asked, and a `Weapon` whose next swing was already due fires once on the frame the screen opens. It would also spend a snapshot build per frame on the one screen GD §11.4 wants cheap.
11. **The flag is raised inside a tick and read at the top of the next one, and the tick that earned the level finishes.** `OfferPresented` is published from `Open`, which runs between ticks (rule 4) — but the *level* was published mid-tick, and nothing may cut that tick short: its intents are applied, its cone is answered and its boundary snapshot is taken, all below `session.Tick` in `RunTicker`. **What this means for ledger row 8:** `RunState.Time` sums each tick's `Dt`, and a gated frame contributes none, so **the level-up screen costs zero simulated seconds** and GD §7.3's 40–75 s band measured from `RunState.Time` is *play* time. A stopwatch measures play + screens. M3-15 must say which number it is quoting, and after this task the two are no longer the same.
12. **`RunPause` holds one reason at a time and refuses a second.** `Pause` with another `PauseReason` holding it throws — a pause menu must not open over a level-up, and the alternative to refusing is a counter, which silently makes "resume" mean "resume once". M3-09 is the second member's first caller and gets a working answer rather than a race.
13. **Two globals, captured on `Pause` and restored on `Resume` and on `Dispose`.** `Time.timeScale` → 0 so that Animators and particles stop with the simulation — a gated tick freezes positions but not clips, and a Husk finishing its wind-up animation on the spot is a telegraph that is no longer a promise. `Application.targetFrameRate` → 30, which is the other half of GD §11.4's sentence. Both are read before they are written and put back to what they were, the `Screen.sleepTimeout` precedent in the same class; **`Dispose` restores unconditionally**, because leaving the Run scene while paused must not hand the Menu a frozen clock. `BootFlow` writes `Time.timeScale = 1f` beside its existing frame-rate line for the same reason a baseline is stated once: with domain reload disabled, a Play session ended mid-pause must not start the next one at zero.
14. **The pause return is above `CommandPhase`**, so a tap that lands on the screen cannot also focus an enemy or spend the Charge. The Input System stays enabled and the stick keeps reading; with the tick gated it moves nobody, and M3-08b's full-screen canvas takes the touches anyway.
15. **`RunState` hands out reads and never the flow** (AR §18.2, the seventh time). `Choose` takes a node and `Open` draws from the run's stream; a public handle would let a view grant the player a skill and consume `Offers` draws the simulation is counting on. `Offer` is a view over **one buffer that the next draw rewrites** — safe because it is read on a frame that is not ticking, and named here for the reason `WorldSnapshot`'s reuse is named everywhere else.

## Tests

| Test | Given / When / Then |
|---|---|
| `Open_DrawsThreeAndPublishes` | one pick owed, a fresh 27-tree / `Open` / `HasOffer`, `Offer.Count` 3, one `OfferPresented(3, 1)` (rules 2, 3) |
| `Open_WithNoPickDoesNothing` | none owed / `Open` / no offer, no event, zero draws on the `Offers` stream |
| `Open_TwiceLeavesOneOffer` | an offer open / `Open` again / the same three ids, no second event, no extra draw (rule 3) |
| `Open_DrawsFromOffersAndNoOtherStream` | a counting `IRandom` / `Open` / `Offers` drew 3, the other four 0 (ADR-0011) |
| `Choose_TakesSpendsAndCloses` | one pick, an offer / `Choose(1)` / that node taken, `PendingLevelUps` 0, `NodeTaken` then `LevelUpClosed`, `HasOffer` false (rule 6) |
| `Choose_TellsTheRunnerAboutAnActive` | an offer holding an Active / `Choose` / `OwnedActiveCount` 1, ready, no `SkillCast` (rule 6) |
| `Choose_PassiveDoesNotReachTheRunner` | an offer holding a Passive / `Choose` / `OwnedActiveCount` 0 |
| `Choose_RedrawsForTheNextPick` | two picks owed / `Choose` / a second `OfferPresented(3, 1)`, **no** `LevelUpClosed`, and the drawn set excludes the node just taken (rule 6) |
| `Choose_SecondOfferSeesTheNewState` | a tier-1 node chosen that unlocks a tier-2 node / the second draw's candidate set / includes it (rule 6) |
| `Choose_NoOffer_Throws` · `Choose_IndexOutOfRange_Throws` | — / `Choose` / `InvalidOperationException`; `ArgumentOutOfRangeException` (rule 6) |
| `Choose_PublishesNoOfferChosen` | reflection over `Soulvail.Core.Events` / — / no such type — `NodeTaken` is the event (rule 6) |
| `Open_FullTree_GrantsOverflow` | every node taken, one pick owed / `Open` / no offer, no draw, one `OverflowGranted(level, 1)`, damage ×1.02, `MaxHp` ×1.02 (rules 2, 7, 8) |
| `Open_BlockedButNotFull_GrantsOverflow` | a tree with nodes left and none available / `Open` / Overflow — the condition is `Draw` returning 0, not `IsFull` (rule 2) |
| `Overflow_PoolsAdditively` | ten Overflow levels / — / damage ×1.20 exactly, not 1.02¹⁰; `ModifierCount` 10 on each stat under one source (rule 8) |
| `Overflow_MaxHpIsNotAHeal` | 100 of 140 hp / one Overflow / `PlayerMaxHp` 142.8, `PlayerHp` 100 (rule 8) |
| `Overflow_ClearsEveryPendingPick` | three picks owed, a full tree / `Open` / three `OverflowGranted`, `PendingLevelUps` 0, no offer (rules 3, 7) |
| `Overflow_AnnouncesTheRunningTotal` | the row above / — / `Total` 1, 2, 3 in order (rule 7) |
| `NoTree_BanksTheLevel` | a class with no tree, two picks owed / `IsLevelUpPending`, `Open` / false; nothing drawn, nothing granted, `PendingLevelUps` still 2 (rule 5) |
| `Offer_IsDrawnAfterTheBoundarySnapshot` | a stage's last kill levels the player / the boundary capture, then `Open` / the captured `RandomState` is the pre-draw `Offers` position; a run resumed from it and opened draws **the same three ids** — **the test M3-04 named** (rule 1) |
| `Offer_IsNotDrawnOnTheTickTheLevelWasEarned` | a levelling kill / `Tick` / zero `Offers` draws during the tick, `IsLevelUpPending` true when it returns (rules 1, 4) |
| `Resume_DerivesOverflow` | level 42, 27 nodes taken, 0 pending / `StartResumed` / `OverflowLevels` 14, damage ×1.28, `MaxHp` ×1.28, **no** `OverflowGranted` published (rule 9) |
| `Resume_DerivesZeroForAFreshShape` | level 5, 4 nodes, 0 pending / `StartResumed` / `OverflowLevels` 0 |
| `Resume_OverflowRunsBeforeHealthRestore` | 14 Overflow levels and hp 170 on a 140 class / `StartResumed` / `PlayerHp` 170 — 140 with the lines swapped (rule 9) |
| `Resume_ArithmeticThatDoesNotAddUp_Throws` | level 3, 5 nodes taken / `Start` / `ArgumentException` naming level, nodes, pending and the result; nothing announced (rule 9) |
| `Commands_ThrowWhenNoRunIsRunning` | before `Start`, after `End` / `OpenLevelUp`, `ChooseOffer(0)` / `InvalidOperationException` each |
| `State_HandsOutNoFlow` | reflection over `RunState` / — / `LevelUp` is not public (rule 15) |
| `Open_AllocatesNothing` | warm-up, `SilentEvents` / 10 000 × open-and-choose / allocated-bytes delta == 0 (M3-04 rule 5 from this side) |
| `Pause_HoldsOneReason` | idle / `Pause(LevelUp)` / `IsPaused`, `Holder` LevelUp (rule 12) |
| `Pause_SecondReason_Throws` · `Resume_WrongReason_Throws` · `Resume_Unheld_Throws` | held by LevelUp / `Pause(Menu)`; `Resume(Menu)`; `Resume` when idle / throws each (rule 12) |
| `Pause_SetsBothGlobals` | timeScale 1, target 60 / `Pause` / 0 and 30 (rule 13) |
| `Resume_RestoresWhatItFound` | timeScale 0.5, target 45 / pause, resume / 0.5 and 45 — restored, not assumed (rule 13) |
| `Dispose_RestoresWhilePaused` | paused / `Dispose` / timeScale 1, target 60, `IsPaused` false (rule 13) |
| `Boot_StatesTheTimeScaleBaseline` | timeScale 0 before boot / `BootFlow` / 1 — `InstallerTests` (rule 13) |
| `Frame_LevelUpPhaseRunsAboveCommands` | a pick owed and a held focus tap queued / one `Tick` / `OpenLevelUp` ran, the tick was skipped, the tap was **not** applied — `FrameOrderTests` (rules 4, 14) |
| `Frame_TheLevellingTickCompletes` | a stage's last kill levels the player / that frame / the cone was still answered and the boundary snapshot still written, and the pause takes effect on the **next** frame (rule 11) |
| `Frame_PausedFrameDoesNotTickCore` | a recording session, paused / 60 frames / zero `Tick` calls, zero snapshot builds — *idled*, not clocked at zero (rule 10) |
| `Frame_PausedTicksCostNoSimulatedTime` | paused for 60 frames / — / `RunState.Time` unchanged (rule 11, ledger row 8) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with no tree authored. Kill things past several levels. Nothing pauses, `DebugOverlay` shows the picks banking, and the game plays exactly as it did at M3-01a (rule 5).
2. **[Editor]** With a hand-authored tree, level once. The game stops dead: enemies frozen mid-step, no animation, no telegraph filling. `DebugOverlay` shows an offer of 3 and a level-up pending; nothing resumes, because no screen exists to choose from and nothing else may lower the pause. **Stop Play to leave** — and check the Editor's own timeline is running afterwards, which is what rule 13's `Dispose` and `BootFlow` lines are for.
3. **[device]** Ledger row 4: whether 30 fps and a zero `timeScale` actually recover thermal headroom, and whether the drop is visible as a hitch on entering and leaving the screen. Deferred with the rest.

## Out of scope

- **The screen** — M3-08b, which turns `OfferPresented` into three cards and owns both halves of the pause it raises.
- **The View Tree toggle** (CH §5.1, GD §13.1) — **M3-09**, which builds the tree view for pause anyway; M3-08b opens the same view rather than a second one, and until then the toggle does not exist. Named rather than silently dropped.
- **A screen for Overflow** — rule 7 argues it. The toast is M3-10's or M3-13's, from `OverflowGranted`.
- **Reroll, Banish, Vigil, Pacts** — M6-02, M6-05, M6-06. AR §6 lists `Reroll()`, `Banish(skillId)`, `BuyHeal()` and `BuyCleanse()` on this port; a port grows a member when the mechanic lands, so none of the four is written here.
- **Saving the offer.** It is not on the snapshot and does not need to be: rule 1's ordering means a resumed run re-draws the identical three from the same stream position, which is a stronger guarantee than storing them and one that survives a content change.
- **Pausing from anywhere** (GD §5.2's top-right icon, GD §7.3's *"pause anywhere, instantly"*) — M3-09 holds `PauseReason.Menu`. This task builds the gate and uses one of its two reasons.

## As built

**Three new production files as specced** — `Core/Ports/IProgressionCommands.cs`,
`Core/Progression/LevelUpFlow.cs`, `Game/Composition/RunPause.cs` — plus the small edits.
**1 423 EditMode / 0 / 0** against M3-07b's 1 373, which is **50 new rows and it lands to the row**:
29 in `LevelUpFlowTests`, 12 in `RunPauseTests`, 7 in `RunSessionResumeTests`, 1 in
`RunSessionTests` and 1 in `ResumeFlowTests`. **PlayMode 15/15**, up from 11 — the four `Frame_*`
rows. `RunSnapshot.CurrentVersion` is **still 3**.

### The eleven deviations

1. **`IProgressionCommands` ships with FOUR members, not the spec's two.** `IsLevelUpPending` and
   `HasOffer` are on the port as reads. Rule 4 says the phase tests `State.IsLevelUpPending`, and
   that is not implementable: `RunTicker` reads `_session.State` **nowhere** today (zero hits), and
   `FrameOrderTests.RecordingCore.State` returns `null` *by design* — `RunState`'s constructor is
   `internal` with no `InternalsVisibleTo` (AR §18.2), so no test assembly can build one. Written as
   specced, all four `Frame_*` rows the Tests table asks for would be unwritable. `RunState` carries
   the same reads for the screens; the port carries them for the object that writes the frame down.
2. **`Tests/Game/Composition/ResumeFlowTests.cs` is in the change and the Files table does not imply
   it.** The *ripple* row says `FrameOrderTests` "is the one fixture that builds one". It is **two**:
   `ResumeFlowTests.cs:606` builds a `RunTicker` as well. Eighteen arguments became twenty across two
   fixtures in **two assemblies** — `Soulvail.Tests.PlayMode` and `Soulvail.Tests.Game`. Both fakes
   (`RecordingCore`, `RecordingSession`) had to implement the new port; the compiler found both, which
   is M3-07a's lesson arriving on schedule.
3. **`RunState.OverflowLevels` is a fourth read**, beyond the spec's three. `Resume_DerivesOverflow`
   asserts the count and `Soulvail.Tests.Core` cannot reach the `internal` flow to ask it.
4. **`Boot_StatesTheTimeScaleBaseline` is in `ResumeFlowTests`, not `InstallerTests`** — that is where
   `RunBootFlow` lives, and `InstallerTests` has no way to run a `BootFlow`.
5. **`Open_BlockedButNotFull_GrantsOverflow` could not be built, and the reason is a finding rather
   than a shortcut.** Over a tree `TreeRules` accepts, "nodes left and none available" is
   **unreachable**: an ordinary node needs `tier − 1` taken *in its branch*, so every branch always
   offers its whole first tier; a Keystone needs `NodeCount − 1`, satisfied exactly when the rest of
   its branch is taken; and an Upgrade needs a parent in the same branch at a **lower** tier, which is
   therefore always reachable first. Replaced by `Open_DrawsWhateverIsAvailableRatherThanThree`.
   **Rule 2's code is still written on `Draw`'s count rather than `IsFull`** and should stay that way —
   M6-02's Banish removes nodes *from the pool*, which is the first thing that can create the state.
   **M3-14b's "all three blocked" check has nothing to catch yet**, and that is worth it knowing.
6. **`Open_AllocatesNothing` could not be written as specced either.** 10 000 open-and-choose cycles
   at zero bytes needs a 10 000-node tree, and every choose puts a modifier on a `Stat` whose backing
   `List<Modifier>` grows monotonically — so the row would have been measuring M3-03's list doubling.
   Replaced by two honest rows: `Reads_AllocateNothing` (the per-frame surface — `HasOffer`, `Offer`
   and indexing it, which is what `RunTicker` and M3-08b poll) and `Draw_AllocatesNothingBeyondTheTake`
   (the draw on its own, M3-04's shape).
7. **`NoTree_BanksTheLevel` is in `RunSessionResumeTests`**, where a snapshot can put picks on the
   clock without reaching for an internal, with a sibling `NoTree_OpensNothingAndThrowsNothing` in
   `RunSessionTests`.
8. **`Offer_IsDrawnAfterTheBoundarySnapshot` ships as two rows** — `Offer_IsNotDrawnUntilItIsOpened`
   (neither `Start` nor any `Tick` draws; only `OpenLevelUp` does) and `Offer_ResumesToTheSameThree`
   (two whole compositions from one saved file draw the identical cards), which is the guarantee the
   ordering exists to buy, stated as the player would feel it.
9. **Seven pre-existing rows went red and were fixtures, not regressions** — and the guard finding
   them is the point. `SkillRunnerTests`' resume helper and two `RunSessionResumeTests` rows built
   snapshots at **level 1 with nodes already taken**, which rule 9's identity says cannot happen. Each
   now states a level that accounts for its nodes. `Start_RestoresNodesBeforeHealth` also gained
   `level: 3` so its maximum stays exactly 160 rather than picking up an Overflow level's +2 %.
10. **`Traps.md` §7 gained a bullet** the Files table does not list: **Unity wiped `Temp/` mid-session**
    and took a finished 50-run measurement with it. Nothing was lost only because each run's file had
    already been read before the next started — the Traps row directly above it. Evidence now goes to
    `Logs/`.
11. **`Commands_ThrowWhenNoRunIsRunning` was extended in place** rather than duplicated, and now also
    asserts that the two *reads* answer `false` outside a run instead of throwing.

### Two things the tests corrected

**"Damage ×1.02" means ×1.02 of the *base*, not of the current value**, and the first draft of three
rows asserted the latter. `PercentAdd` pools: over the 27-node tree the stat already sits at +180 %,
so one Overflow level takes it from ×2.80 to ×2.82 — a ×1.007 change. The rows now assert the
**delta against the base**, and `Overflow_PoolsAdditively` runs on a clean stack so its ×1.20 reads as
arithmetic rather than as a tolerance. **And `Overflow_MaxHpIsNotAHeal` was asserting its way past
CC §7's 30-point Aegis** — a flat 40 damage on a 140 class leaves 130, not 100. The shield is read
rather than assumed.

### Non-finite rows: none owed, and that is said out loud

Neither new class has a `float` door. `Open`, `Choose` and `GrantOverflow` take a stream, an `int`
and an `int`; `Pause`/`Resume` take a `PauseReason`. `OverflowDamage` and `OverflowMaxHp` are
**compile-time constants**, not doors — no caller can pass a value through them. The one float that
moves is `Time.timeScale`, which `RunPause` *round-trips*, and that is covered by
`Resume_RestoresWhatItFound` at a deliberately odd 0.5 / 45 — a row written from the defaults would
pass identically against a class that hard-coded them.

### The pin rows, and what each holds a place against

- **`Choose_PublishesNoOfferChosen`** sweeps `Soulvail.Core.Events` and fails if an `OfferChosen`
  type ever appears. It holds a place against the obvious-looking addition: `NodeTaken` already
  carries the id, kind, branch, tier and count, so an `OfferChosen` would be a second event with a
  subset of the first's fields (AR §8). The task that adds one must **delete a red row** and argue it.
- **`State_HandsOutNoFlow`** fails if `RunState.LevelUp` is ever made public, and also asserts the
  four reads beside it exist — so "make it public" cannot be justified by the reads being missing.
  Same family as M3-07a's `Snapshot_CarriesNoLoadout`, which M3-07b inverted in place when the format
  genuinely did gain the field.
