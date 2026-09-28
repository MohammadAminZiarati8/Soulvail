# M7-05d — An arena for a span of stages

**Size:** M · **Depends on:** M7-05c · **Branch:** `m7-05d-an-arena-for-a-span-of-stages`
**Design refs:** GD §7.2, GD §8.2, GD §12.4 · **Ledger rows:** none

The first of six tasks cut from the owner's go of 2026-09-27: *"smaller arenas for a place's first
stages … today ModeSpec.ArenaFor picks any rostered arena at any stage, so arenas need a stage
window, like the roster's `_introducedAtStage`."* The room is M7-05e; the mode that uses the
window is M7-05i.

## Goal

A mode's arena roster says, row by row, which stages each room may be used at, and `ArenaFor`
picks only among the rooms open at the stage it is asked about.

## Why this shape

- **The window belongs to the mode, not the arena.** A room is small or large; *when* a place uses
  it is the place's statement, as *when* an archetype arrives is (`RosterEntry`). So the window is
  on the roster row, and an `ArenaView` learns nothing.
- **A first and a last stage, not a first alone.** A small room opens a place and must also close
  to it, because GD §12.4's concurrency grows with depth: 10 bodies at stage 1, 15 at stage 10.
- **The walk stays a walk.** `ArenaFor` is a pure function of the seed and the stage, replayed from
  stage 1 so no room repeats. It now hashes into the rooms open at each stage rather than into all
  of them. With every row open, the rooms open at a stage are all of them, so every shipped seed
  lands where it did before.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ModeSpec.cs` | Core | `ArenaEntry`; `Arenas` becomes entries; `ArenaFor` walks the open rows; the coverage check |
| `Game/Authoring/ModeDefinition.cs` | Game | `ArenaRow` replaces the `string` row |
| `Tests/Core/Content/ModeSpecTests.cs` | Tests.Core | rules 1–6; the arena rows' call sites follow |
| `Tests/Game/Authoring/ModeDefinitionTests.cs` | Tests.Game | rule 7 |
| *small edits* | | `Data/Modes/Descent.asset`: its four ids become four open rows (rule 8) |
| *ripple* | | `RunSessionResumeTests` builds its mode with entries; `ContentValidationTests` and `ArenaViewTests` read `.ArenaId` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

public readonly struct ArenaEntry
{
    public const int NoLastStage = int.MaxValue;
    public ArenaEntry(ContentId arenaId, int firstStage = 1, int lastStage = NoLastStage);
    public ContentId ArenaId { get; }
    public int FirstStage { get; }
    public int LastStage { get; }                 // NoLastStage: open to the end
    public bool IsOpenAt(int stage);
}

// ModeSpec
public ModeSpec(..., IReadOnlyList<ArenaEntry> arenas = null, ...);   // was IReadOnlyList<ContentId>
public IReadOnlyList<ArenaEntry> Arenas { get; }
public ContentId ArenaFor(int stage, int seed);                       // signature unchanged

// ModeDefinition (private, serialized)
[Serializable] private struct ArenaRow { string _arenaId; int _firstStage; int _lastStage; }  // 0 = no last stage
[SerializeField] private ArenaRow[] _arenas;
```

## Behaviour

1. **A row names a room and a span of stages.** `firstStage` is at least 1 and `lastStage` is at
   least `firstStage`. `lastStage` is `NoLastStage` when the room is open to the end. Omitted, the
   span is every stage. `default(ArenaEntry)` names no room and is refused where the roster is
   copied, as `default(RosterEntry)` is.
2. **`ArenaFor` picks among the rooms open at the stage.** At each stage of the walk, the hash picks
   one of the open rows, in authored order. The no-repeat step moves to the next open row,
   wrapping. A stage with one open room gets that room, repeating if it must, as a roster of one
   always has.
3. **A roster whose rows are all open picks what it picked before this task.** Pinned against
   values recorded from the pre-window code: rosters of 2, 3 and 8, five seeds, stages 1–16.
4. **Every stage a mode has must have a room.** A non-empty roster that leaves a stage the mode has
   with no open row is refused at construction, and the message names the first such stage. For
   an endless mode that means some row stays open to the end. For a finite mode it means only
   `startingStage` to `finalStage`. An empty roster stays legal, and `ArenaFor` answers `default`.
5. **The existing rules hold.** The same room twice is refused, a stage below 1 throws, and
   `ArenaFor` allocates nothing, with windows as without.
6. **`Arenas` hands out the rows**, wrapped so they cannot be written through, in authored order.
7. **The asset's row is the entry's.** `ArenaRow` carries `_arenaId`, `_firstStage` (at least 1,
   default 1) and `_lastStage`, where 0 means no last stage. `ToSpec` converts each row, and a bad
   row, or a gap in the stages covered, throws `ArgumentException` naming the asset.
8. **Descent's rooms are four open rows**, the same four in the same order, so a Descent run
   raises the rooms it raised before (rule 3).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `ArenaEntry_OmittedSpanIsEveryStage` | `new ArenaEntry(id)` / read / first 1, last `NoLastStage`, open at 1 and at 10 000 (rule 1) |
| `ArenaEntry_RefusesABadSpan` | first 0; last before first / constructed / `ArgumentOutOfRangeException` (rule 1) |
| `Ctor_DefaultArenaEntry_Throws` | a roster holding `default(ArenaEntry)` / constructed / `ArgumentException` (rule 1) |
| `ArenaFor_OnlyPicksRoomsOpenAtTheStage` | A 1–4, B open, C from 5; 40 seeds / stages 1–12 / A never after 4, C never before 5 (rule 2) |
| `ArenaFor_NeverRepeatsAcrossAWindowEdge` | the roster above / 40 seeds × stages 1–30 / no stage repeats the one before it where two rooms were open (rule 2) |
| `ArenaFor_ALoneOpenRoomRepeats` | A 1–3, B from 4 / stages 1–3 / A each time (rule 2) |
| `ArenaFor_AnOpenRosterPicksWhatItPickedBefore` | open rosters of 2, 3, 8; five seeds / stages 1–16 / the recorded strings (rule 3) |
| `Ctor_AGapBetweenWindowsThrows` | A 1–4, B from 6 / constructed / `ArgumentException` naming stage 5 (rule 4) |
| `Ctor_AnEndlessModeWhoseRoomsCloseThrows` | endless, A 1–10 only / constructed / `ArgumentException` naming stage 11 (rule 4) |
| `Ctor_AFiniteModeNeedsOnlyItsOwnStages` | finite 1–10, A 1–10 / constructed / legal (rule 4) |
| `ArenaFor_WithWindows_AllocatesNothing` | the rule 2 roster / 10 000 calls / `AllocationAssert.None` (rule 5) |
| `Ctor_CopiesArenas` | *(rewritten)* a list of entries / constructed / count 2, not the caller's array (rule 6) |
| `Definition_ArenaRowsCarryTheirSpans` | a definition with rows (a, 1, 4) and (b, 3, 0) / `ToSpec` / 1–4 and 3–open (rule 7) |
| `ToSpec_ArenaGap_ThrowsNamingAsset` | rows (a, 1, 4) and (b, 6, 0) / `ToSpec` / `ArgumentException` naming the asset (rule 7) |
| `Descent_RostersItsArenas` | *(unchanged in meaning)* Descent / read / its four ids, every row open (rule 8) |
| `Descent_EveryYamlKeyBindsToAField` | *(unchanged)* now covers the three new keys (rule 7) |

## Manual verification (Editor / device)

None of its own. Nothing that plays changes (rule 8). M7-05i is the first task a player can see it
in.

## Out of scope

- The small room (M7-05e) and the mode that windows it (M7-05i).
- Weighting rooms, or a room's chance by depth. A window is on or off.
- A stage editor ([parking lot](../ROADMAP.md#parking-lot)).

## As built

**Deviations.** *(1)* **The ripple is wider than the Files table says.** Eighteen fixtures build a
mode with rooms by position rather than by `arenas:` name: five in Tests.Core (`SkillRunnerTests`,
`GrantShieldTests`, `SpawnHealZoneTests`, `SkillTreeTests`, `SplashFlowTests`) and thirteen in
Tests.Game (`SkillButtonTests` and twelve presenter and view fixtures). Each is one line, a
`ContentId` wrapped in `new ArenaEntry(…)`, and none changes what the fixture says.
*(2)* **`FirstStageWithNoArena` guards its own progress.** It returns the stage it is on when no
open row reaches past it (`reach < stage`), which also covers the empty case. With a correct
`IsOpenAt` every pass moves forward anyway. The guard was added after a red check that forced
`IsOpenAt` true made the sweep loop forever on `Ctor_AnEndlessModeWhoseRoomsCloseThrows`, on the
Editor's main thread. It hung the Editor twice before the cause was found. *(3)* **`ArenaRow` passes
its first stage through**, as `RosterRow` does, so a 0 is refused by `ArenaEntry` rather than read
as 1. *(4)* A row beyond the table, `Ctor_AModeStartingDeepNeedsNoRoomBeforeIt`: a mode entered at
stage 5 is not refused for stages 1–4, and the walk carries no room through them.

**Finding — an unspanned roster picks what it picked before.** The fifteen golden cases were
recorded from the pre-span code by an Editor probe before a line changed. They cover rosters of 2,
3 and 8, seeds 1, 7, 99, −5 and 123456, and stages 1–16. All fifteen pass unchanged, so Descent's
rooms, and any saved run's, are where they were.
[`ArenaFor_AnOpenRosterPicksWhatItPickedBefore`](../../../Assets/_Project/Tests/Core/Content/ModeSpecTests.cs)

**Red-checked.** With the walk ignoring the spans (every row counted open, raw index), the fixture
ran 57 / 3. The three failures were exactly `ArenaFor_OnlyPicksRoomsOpenAtTheStage`,
`ArenaFor_ALoneOpenRoomRepeats` and `Ctor_AModeStartingDeepNeedsNoRoomBeforeIt`, and the golden
rows stayed green.

**Learned.** **A red-check mutation must not be able to take a loop's progress away.** An EditMode
test runs on the Editor's main thread, so a mutation that turns a sweep into `while (true)` is an
Editor to kill, not a red row. Spent (stays here).

**Verified**, in one run with M7-05e to M7-05i in the tree. EditMode 3 406: 3 405 passed, 0 failed,
1 inconclusive (the animator clock row); +89 on M7-05c's 3 317, of which this task's rows are 28.
PlayMode 67 / 67. On this task alone, the five arena and mode fixtures ran 186 / 186. Format check
clean over the 25 files; zero compiler or analyzer warnings.
