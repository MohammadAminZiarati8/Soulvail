# M6-02a — The sixth phase, and the boss stage that was never over

**Size:** S · **Depends on:** M6-01a · **Branch:** `m6-02a-sanctum-phase`
**Design refs:** GD §7.1, §13.3, §14.1; AR §5, §18.1 · **Ledger rows:** [5](../ROADMAP.md#carry-forward-into-m6) — discharged here

## Goal

A cleared stage ends in a room instead of a door, the room is untimed, and a boss stage is not over
while one of its adds is still swinging at you.

## Why the two halves are one task

They are the same defect seen from two sides. GD §13.3 calls the Sanctum *"a small safe room,
untimed"*, and [ledger row 5](../ROADMAP.md#carry-forward-into-m6) says a boss stage completes the
moment the boss falls **whatever its adds are doing** — witnessed at [M5-08](M5-08-acceptance-and-tag.md),
where two Husks were still being killed **six seconds after the stage completed**. Putting an
untimed room in that gap does not expose the bug; it makes it permanent. A player shopping with a
live Husk in the arena is the clearest possible statement that the room is not safe, and no amount
of screen work in [M6-03a](M6-03a-the-sanctum-screen.md) can fix it.

So the row is discharged by the task whose own feature breaks on it, which is the placement
[M5-00a](../ROADMAP.md#carry-forward-into-m5) could not make: that spec weighed two fixes and
refused both *on the grounds that nobody had observed the failure.* It has been observed, and now
something needs it.

## What the row said about the cost, and what the code says

The row claims requiring the adds cleared *"moves boss-stage pacing **and** the depth the
`ShardPayout` reads."* **Grepped rather than inherited, the second half is not true.**
`ShardPayout.For(deepestStage, mode)` and `ShardPayout.BossesKilled(deepestStage, mode)` are pure
functions of `RunState.StageIndex`, and `StageIndex` moves in `StageFlow.Advance` — when the player
walks through the door. Delaying `Clear` delays the *door*, so it delays when the depth can next
increase; it does not change what the depth **is** at any moment, and no payout is recomputed
differently for a stage anybody actually reached.

What does change, and it is the correct direction: a player who kills the boss, walks to the gate
and is killed by a surviving add now dies at stage *n* rather than at *n + 1* — **10 Shards**, for a
stage they did not clear. The row's other refused fix, despawning the adds, stays refused for its
own reason, unchanged: a despawn is not a kill, so `EnemySystem`'s XP path never runs and the player
is silently robbed of the experience for bodies they were already fighting.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Stage/StageFlow.cs` | Core | **Substantial.** `StagePhase.Sanctum`, the `Clear → Sanctum → Gate` path, and the one command that leaves it |
| `Core/Director/SpawnDirector.cs` | Core | **Substantial in meaning, small in lines.** `IsStageComplete` on a boss stage (rule 5) |
| `Tests/Core/Stage/StageFlowTests.cs` | Tests.Core | **Substantial.** Every existing phase-order row now runs through six phases |
| *small edits* | Core | `Core/Events/StageEvents.cs` — `SanctumOpened` beside `StageCleared`; `Core/Ports/IProgressionCommands.cs` — `IsSanctumOpen` and `LeaveSanctum` (rule 4); `Core/Run/RunSession.cs` — both members, and the throw when no run is running; `Core/Run/RunState.cs` — the `IsSanctumOpen` read |
| *ripple* | Tests.Core | `SpawnDirectorTests` gains the boss-with-adds rows; `RunSessionTests` and `ResumeFlowTests` walk one more phase per stage |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Stage;

public enum StagePhase
{
    Arrival,
    Waves,
    Clear,

    /// <summary>
    /// GD §13.3's economy moment: the arena is empty, the shop is open, and nothing is counting
    /// down. Left by a command and by nothing else — rule 3.
    /// </summary>
    Sanctum,

    Gate,
    Transition,
}

public sealed class StageFlow
{
    /// <summary>
    /// Leaves the Sanctum and opens the door. The one way out of <see cref="StagePhase.Sanctum"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The flow is not in the Sanctum — a view reporting a tap on a screen that is not up.
    /// </exception>
    public void LeaveSanctum(float now);
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>
/// The shop is open. Carries the stage and what the player has to spend, so a screen that opens on
/// this event does not have to read the run to draw its first frame (AR §8).
/// </summary>
public readonly struct SanctumOpened
{
    public SanctumOpened(int stage, int essence);
    public readonly int Stage;
    public readonly int Essence;
}
```

```csharp
namespace Soulvail.Core.Ports;

public interface IProgressionCommands
{
    // ... the eight members that exist, then:

    /// <summary>Whether the shop is up — what a pause would be held against. False with no run.</summary>
    bool IsSanctumOpen { get; }

    /// <summary>Closes the shop and opens the door.</summary>
    /// <exception cref="System.InvalidOperationException">No run is running, or the shop is not open.</exception>
    void LeaveSanctum();
}
```

## Behaviour

1. **The phase goes exactly where `StageFlow` has said it would since M2-10.** That file's own
   remarks read: *"`Clear` is a state rather than an instant because the Sanctum (GD §7.1, M6-02)
   lands between it and `Gate`: when there is an economy to spend, a sixth phase goes in that gap
   without moving anything either side of it."* So `Clear` still lasts `ClearTime`, `Gate` still
   waits for the player's feet, and the only edited transition is the one at the end of `Clear`.
   **`StageCleared` is still published on the edge into `Clear`**, which keeps M2-14a's boundary
   snapshot exactly where it is: the save is taken before the player spends anything, so a run
   killed inside the shop resumes with the Essence it walked in with rather than the state it was
   halfway through changing.
2. **A mode with no next stage never enters it.** `Clear` already parks for ever when
   `IsModeComplete`, and that branch is untouched: a run that has finished its mode has nothing to
   buy and nowhere to go. Descent is endless, so this is inert in V1 and is written anyway, for the
   reason M2-10 rule 14 wrote the original.
3. **It is left by a command and never by a clock**, which is `Gate`'s rule rather than `Clear`'s.
   GD §13.3 says *untimed*, and the honest reading of untimed is that `StageFlow.Tick` has no case
   for this phase at all — it falls through, exactly as `Gate` does until the player's feet arrive.
   **The run is still ticking**: the arena is empty by rule 5, cooldowns recover, and `RunState.Time`
   advances. Whether the *screen* raises a `RunPause` is [M6-03a](M6-03a-the-sanctum-screen.md)'s
   ruling and not core's — **taken there, and the answer is yes**, which makes this sentence's
   *"cooldowns recover"* false in the shipped game and correct in this object (that task's rule 4); what core promises is that nothing spawns, nothing is owed and nothing
   expires while the player reads four prices.
4. **`LeaveSanctum` is a command on the progression port, not on `IRunSession`.** It is a tap on a
   screen that exists for ten seconds, which is the exact distinction `IProgressionCommands`' own
   remarks draw — *"an input adapter holds the first for the whole run, and a screen that exists for
   two seconds holds the second."* It **throws** when the shop is not open, for that port's stated
   reason: a command arriving for a control nobody is drawing is a wiring mistake and a silent no-op
   would hide it. `IsSanctumOpen` is the read beside it, answering false with no run, so the frame
   loop can ask before it acts — `HasOffer`'s shape, one screen over, and
   [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 7's predicate-then-invariant pairing again.
5. **A boss stage is complete when the boss is down *and* nothing else is breathing.**
   `SpawnDirector.IsStageComplete`'s boss branch becomes `_bossCleared` **and** no living agent in
   the registry. It walks `Registry.Alive` for `IsAlive` rather than reading `AliveCount`, and the
   difference is load-bearing: `AliveCount` is *registered*, not breathing (AR §18.4), so a corpse
   waiting out `EnemySystem.CorpseTime` would hold the stage open for another 0.6 s and the fight
   would stop ending on the kill. The walk is at most the concurrency cap, once per tick, on boss
   stages only — the ordinary branch's walk over waves is the same order and has run every frame
   since M2-05.
6. **Nothing about an ordinary stage changes.** The non-boss branch of `IsStageComplete` already
   required every wave cleared, and a wave is cleared when its bodies are dead — so this rule only
   ever moves a boss stage, and `Stage_AnOrdinaryStageIsUnchanged` is the row that says so rather
   than the reviewer having to reason it out.
7. **Nothing here allocates.** One more enum member, one more `case` in a switch over it, a `bool`
   read, and a walk over a span the director already holds.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Stage_ClearLeadsToTheSanctum` | a cleared stage / ticked past `ClearTime` / `Phase` is `Sanctum`, and one `SanctumOpened` carrying the stage and the wallet — rule 1 |
| `Stage_TheSanctumHasNoTimeout` | in the Sanctum / ticked 600 s / still `Sanctum`, nothing published, nothing spawned — rule 3 |
| `Stage_LeavingOpensTheDoor` | in the Sanctum / `LeaveSanctum` / `Phase` is `Gate`, and walking into the door still advances |
| `Stage_LeavingTwiceThrows` | left once / `LeaveSanctum` again / `InvalidOperationException` — rule 4 |
| `Stage_LeavingFromAnotherPhaseThrows` | `Waves` / `LeaveSanctum` / throws, and the phase is unmoved |
| `Stage_AFinishedModeNeverEntersIt` | a finite mode on its last stage / cleared and ticked / parks in `Clear`, no `SanctumOpened` — rule 2 |
| `Stage_TheBoundarySnapshotIsStillTakenOnTheClearEdge` | a run cleared / — / `RunSnapshotTaken` fires on the `Clear` edge, **before** `SanctumOpened` — rule 1 |
| `Stage_TheOrderIsArrivalWavesClearSanctumGateTransition` | *(existing, extended)* a full stage / — / six phases in that order, each once |
| `Boss_IsNotCompleteWhileAnAddBreathes` | a boss stage, boss dead, two adds alive / — / `IsStageComplete` false; kill both / true — [row 5](../ROADMAP.md#carry-forward-into-m6) |
| `Boss_IsCompleteOnTheKillWhenNothingElseStands` | a boss stage with no adds / the boss dies / complete on that tick, not 0.6 s later — rule 5 |
| `Boss_ACorpseDoesNotHoldTheStageOpen` | boss dead, one add killed this tick and still registered / — / complete, because the walk asks `IsAlive` rather than `AliveCount` — rule 5 |
| `Boss_AnAddCannotHitThePlayerThroughTheSanctum` | boss down, one add alive / ticked / the flow stays in `Waves`, no `SanctumOpened`, and the add is still fighting — the failure M5-08 witnessed, asserted |
| `Boss_TheDepthIsUnchanged` | a boss stage cleared the slow way / — / `StageIndex`, `ShardPayout.For` and `BossesKilled` are what they were before this task — the row's claim, corrected |
| `Stage_AnOrdinaryStageIsUnchanged` | stages 1–4 / cleared / `IsStageComplete` behaves exactly as at M2-05 — rule 6 |
| `Run_IsSanctumOpenTracksThePhase` | a live session / through a stage / true only while the phase is `Sanctum`, and false with no run — rule 4 |
| `Run_LeaveSanctumWithoutARunThrows` | no run / `LeaveSanctum` / `InvalidOperationException` |
| `Director_CompletionAllocatesNothing` | 100 000 reads on a boss stage with 28 bodies / `AllocationAssert.None` / zero — rule 7 |

**Guard rows are implied, not listed:** a non-finite `now` to `LeaveSanctum`, and every existing
`StageFlow` guard firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 5, kill the Warden, and leave one add alive. The barrier stays up, the
   door does not open, and the debug overlay's phase reads `Waves`. Kill the add: `Clear`, then
   `Sanctum`. This is [row 5](../ROADMAP.md#carry-forward-into-m6) closed by looking at it.
2. **[Editor]** Clear an ordinary stage and stand in the Sanctum for two minutes. Nothing spawns,
   the HP bar does not move, and the phase does not change. Press the debug leave command: the door
   opens.

## Out of scope

- **Anything a player can buy.** [M6-02b](M6-02b-four-things-essence-buys.md).
- **Any screen.** No prefab, no presenter, no `PauseReason`, no localisation row. The debug overlay
  is what shows the phase until [M6-03a](M6-03a-the-sanctum-screen.md).
- **A Sanctum *room*.** GD §13.3 says *"a small safe room"*, and this ships a phase in the arena the
  player already cleared. A second arena at every boundary is an `ArenaPool` swap, a second load and
  a stage of its own; M7-05/06's art pass is the earliest task that could make one worth having.
- **Skipping the Sanctum when there is nothing affordable.** GD §13.3 says *after every stage*, and a
  room that appeared only when the player was rich would make the economy invisible exactly when it
  matters most.
- **Saving that the player is in the shop.** The snapshot is taken on the `Clear` edge (rule 1), so a
  run killed in the Sanctum resumes at the stage it was about to start — the same answer a run
  killed in `Gate` already gets.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
