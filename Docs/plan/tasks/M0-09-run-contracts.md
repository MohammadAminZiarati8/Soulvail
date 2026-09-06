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

_Filled at merge._
