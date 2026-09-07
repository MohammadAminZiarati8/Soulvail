# M0-09 — `IRunSession`, `RunConfig`, `RunState`, run events

**Size:** M · **Depends on:** M0-05, M0-06, M0-07, M0-08 · **Branch:** `m0-09-run-contracts`
**Design refs:** AR §4.1, §5 (`Run` module), §6 (inbound ports), §8

## Goal

The inbound façade the body talks to, the state a run carries, and the first domain events — as contracts, so M0-10 can implement against them and M0-12 can register them.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/IRunSession.cs` | Core | Inbound port: the body drives the brain through this |
| `Core/Run/RunConfig.cs` | Core | What a run is started with |
| `Core/Run/RunState.cs` | Core | Live state of the run; core is the source of truth |
| `Core/Events/RunEvents.cs` | Core | `RunStarted`, `RunEnded` (event structs are grouped per module — the one accepted exception to one-type-per-file) |

## Public API

```csharp
namespace Soulvail.Core.Ports;

public interface IRunSession
{
    bool     IsRunning { get; }
    RunState State     { get; }          // null before Start
    void Start(RunConfig config);
    void Tick(WorldSnapshot snapshot);
    void End();
}
```

```csharp
namespace Soulvail.Core.Run;

public sealed class RunConfig
{
    public ContentId CharacterId { get; }
    public RunConfig(ContentId characterId);
}

public sealed class RunState
{
    public ContentId     CharacterId { get; }
    public int           Seed        { get; }
    public CharacterSpec Character   { get; }
    public PlayerMotor   Motor       { get; }
    public float         Time        { get; internal set; }      // seconds of simulated run time
    public Vector3       PlayerPosition { get; internal set; }   // last reported by the body

    internal RunState(ContentId characterId, int seed, CharacterSpec character, PlayerMotor motor);
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct RunStarted { public readonly ContentId CharacterId; public readonly int Seed; }
public readonly struct RunEnded   { public readonly float Time; }
```

## Behaviour

Contracts only. The rules live in M0-10's spec; this task establishes:

1. `RunState` is constructed only by core (`internal` constructor). `Time` and `PlayerPosition` are settable only inside Core (`internal set`).
2. Events are `readonly struct` with public readonly fields and a constructor — no properties, no logic.
3. `RunConfig` holds the character to play. The seed is **not** here; it comes from `IRandom.Seed`, which the composition root creates from `PendingRun` (M0-12). One source of truth for the seed.

## Tests

None here — pure contracts. `RunSessionTests` (M0-10) exercises all of them.

## Acceptance

- [ ] Compiles; Core purity test still green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- `IPlayerCommands`, `IProgressionCommands` — no commands exist in M0 (movement arrives through the snapshot). First command is tap-to-focus in M1-08.
- Stage flow, enemies, XP — their milestones. `RunState` grows fields as they arrive.

## As built

Four files exactly as the Files table names them, no tests, suite unchanged at 95. Three deviations, plus two documentation changes the owner approved after the build: `Architecture.md` corrected to the built port shape, and `Config_DefaultId_Throws` added to M0-10's Tests table.

1. **`RunConfig` rejects `default(ContentId)` with `ArgumentException`.** A behaviour rule in a task whose Behaviour section says "contracts only" — taken deliberately. `default(ContentId)` is the one id no constructor can prevent, and `RunConfig` is the only place outside `CharacterSpec` where a `ContentId` is built from outside core (M0-12's `PendingRun`). Leaving it to the catalog would still fail loudly, but as `KeyNotFoundException: No character with id ''` — which reads as missing content when the actual fault is that nobody chose a class. Every *other* unknown id stays the catalog's question, so rule 2 of M0-10 is untouched. **M0-10's Tests table gains `Config_DefaultId_Throws`**, added here along with a note on why that row's behaviour lives in this task rather than that one.
2. **`RunState`'s `internal` constructor takes no argument guards.** Rule 1 only asks that it be `internal`; the house style would add null checks on `character` and `motor`, and they are deliberately absent. Such a guard defends against a bug inside `Soulvail.Core` itself, and — being unreachable from `Soulvail.Tests.Core`, which has no `InternalsVisibleTo` — could only be covered by opening internals to prove a check against my own mistake. The type's remarks say so, and say that the guards return if the constructor ever goes public.
3. **The Public API here diverged from AR §4.1 and §6, and `Architecture.md` is corrected to the built shape** — at the owner's direction, in M0 rather than deferred. Built as specced: `Tick(WorldSnapshot)` rather than `Tick(dt, snapshot)` (`Dt` is a field on the snapshot, per AR §4.2, and two ways to say how much time passed is one too many), and `Start`/`Tick`/`End` rather than §6's `Start(mode, class)`, `EndRun` and the three `Report*` facts. §4.1's diagram and channel table, §4.3's frame order and §6's port row now say what the code says; §6 keeps the `Report*` methods on the record as M1's, under a new line stating the rule the table could not: **a port grows a member when the mechanic that needs it lands, not before** — a port written ahead of its callers is a guess, and an unused method is one nobody can test. No superseding ADR: ADR-0003 decides that the tick is a channel carrying a snapshot, not how many parameters spell it. Same call as M0-05's §4.2 correction.

Verified with the Editor open: clean recompilation with zero errors and zero warnings, all five types present in `Soulvail.Core`, `AssemblyPurityTests` green, EditMode suite 95/95. `Soulvail.Tests.Core` still has no `InternalsVisibleTo` — confirmed against M0-10's fourteen rows, every one of which only reads `RunState`.
