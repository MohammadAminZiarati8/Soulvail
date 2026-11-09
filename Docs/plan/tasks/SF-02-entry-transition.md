# SF-02 — Entry: cyan flash, walk-in, door shuts, a beat

**Size:** M · **Depends on:** SF-01 · **Branch:** `sf-02-entry-transition`
**Design refs:** GD §7.1–7.2

## Goal

Cyan-white flash; hero walks in from the door opposite the gate; it shuts, a beat, first wave.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Stage/StageFlow.cs` | Core | rules 1–4 |
| `Game/Arena/ArenaView.cs` | Game | rule 6 |
| `Game/Arena/ArenaPool.cs` | Game | rule 5 |
| `Game/Views/EntryDoorView.cs` | Game | rule 7 |
| `Game/Views/GateFlashView.cs` | Game | rules 8–9 |
| `Tests/Core/Stage/StageFlowEntryTests.cs` | Tests.Core | |
| `Tests/Game/Arena/ArenaEntryTests.cs` | Tests.Game | |
| *small edits* | | `StageFlowTuning`, `StageEvents`, `WorldSnapshot`, `SnapshotBuilder`, `RunSession` (rule 2), `RunScope`, `Run.unity`; five arena prefabs get `_entry` (0.5 m inside the wall behind the start, in line with the gate) + door slab |

Allows `.meta` files and test count/coverage updates.

## Public API

```csharp
// StageFlowTuning ctor params, as SF-01
public float EntryWalkMaxTime { get; }   // 1.5
public float EntryArriveRadius { get; }  // 0.3
public float EntryBeatTime { get; }      // 1.0
public readonly struct StageEntrySealed { public readonly int Stage; }
public bool TryEntryInput(WorldSnapshot snapshot, out Vector2 input);  // StageFlow
public bool HasEntry; public Vector3 PlayerStart;                     // WorldSnapshot
public Vector3 EntryPosition { get; } public bool HasEntry { get; }   // ArenaView
public void CloseEntry(); public void OpenEntry();                    // ArenaView
```

## Behaviour

1. Arrival with `HasEntry` walks in until within `EntryArriveRadius` of `PlayerStart` or `EntryWalkMaxTime`, then publishes `StageEntrySealed` once; without, it seals on the arrival tick.
2. While walking, `TryEntryInput` is true with the unit XZ direction to `PlayerStart`; `RunSession`'s motor uses it over the stick.
3. Waves begin `EntryBeatTime` after the seal, never before `ArrivalTime` after arrival (no entry keeps 2 s). Arrival ticks allocate nothing.
4. Arena choice unchanged: `ArenaFor` already refuses a repeat (SF-03: rooms).
5. `ArenaPool` on `StageArrived` teleports to `EntryPosition` if `HasEntry`, else `PlayerStart`, and calls `OpenEntry`; on `StageEntrySealed`, `Active.CloseEntry()`.
6. `DescribeFaults` reports an `_entry` outside `PlayBounds` or within `EntryArriveRadius` of the start.
7. `EntryDoorView` drops its slab from `_openHeightM` 3 to 0 in `_closeS` 0.25, `Palette.Player` rim; `OpenEntry` lifts it.
8. `GateFlashView`, its own overlay canvas sorted above `Hud`: on `StageTransitionStarted` ramps to opaque over `Duration`, `Palette.Player` → `Palette.HitFlash` at the peak; on `StageArrived` fades over `_outS` 0.45, refused below `StageFlow.FadeTime` so no black shows.
9. The flash runs on unscaled time; `Update` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arrival_WithEntry_WalksToStart` | 3 m south / tick / (0,1) |
| `Arrival_ReachStart_SealsOnce` | 0.2 m off / tick ×2 / one Sealed, input false |
| `Arrival_WalkTimesOut_Seals` | stuck / 1.5 s / Sealed |
| `Arrival_NoEntry_WavesAt2s` | no entry / ticks / Sealed at 0, Waves at 2.0 |
| `Arrival_WavesAfterBeat` | sealed 1.4 s / ticks / Waves at 2.4 |
| `Arrival_AllocatesNothing` | warm / `AllocationAssert.None` |
| `Pool_Arrived_TeleportsToEntry` | Hollow / Arrived / at entry |
| `Pool_Sealed_ClosesEntry` | Sealed / 0.25 s / slab 0 |
| `Arenas_EntryValid` | five prefabs / `DescribeFaults` / empty |
| `Flash_RampsAndClears` | events / unscaled / α 1, 0 by 0.45 s |
| `Flash_OutBelowFade_Refused` | 0.2 s / validate / fault |

## Manual verification (Editor / device)

1. **[Editor]** Gate: cyan-white flash, no black; walk-in from the south, door drops, beat, spawn.
2. **[Editor]** Pets follow.
3. **[device]** Phone.

## Out of scope

- Stage card, strip, HUD black fade (UI rework); arenas (SF-03).
