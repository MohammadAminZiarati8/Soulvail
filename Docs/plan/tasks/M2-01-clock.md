# M2-01 — `IClock` + `UnityClock`: wall-clock, for persistence only

**Size:** M · **Depends on:** — · **Branch:** `m2-01-clock`
**Design refs:** AR §6 (ports), §7, §10.3, §18.2; ADR-0003, ADR-0007 · **Ledger rows:** none

## Goal

Core can ask what time it is *in the world* — the number a save file is stamped with — through a port, without a clock ever entering the simulation.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/IClock.cs` | Core | The port: `UtcNow`, and nothing else |
| `Game/Adapters/UnityClock.cs` | Game | The device clock behind it |
| `Tests/Core/Fakes/FixedClock.cs` | Tests.Core | A clock that only moves when told |
| `Tests/Core/Fakes/FixedClockTests.cs` | Tests.Core | The fake's own rules — `FixedRandomTests`' precedent |
| `Tests/Game/Adapters/UnityClockTests.cs` | Tests.Game | The adapter forwards and does not cache |
| *small edits* | | `BootInstaller` registers `UnityClock` as `IClock`, singleton; **AR §6's `IClock` row** — `Now` (core time, seconds) is wrong and has been since M0-10 settled it, corrected to `UtcNow` |
| *ripple* | | none — nothing implements or consumes `IClock` yet, which is the whole reason this is one task and not a paragraph inside M2-13 |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Ports;

/// Wall-clock. Never simulated time — see AR §18.2.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class UnityClock : IClock
{
    public DateTimeOffset UtcNow { get; }   // => DateTimeOffset.UtcNow, read every time
}
```

```csharp
namespace Soulvail.Tests.Core.Fakes;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset start);
    public DateTimeOffset UtcNow { get; }
    public void Set(DateTimeOffset instant);
    public void Advance(TimeSpan by);        // negative is legal: device clocks go backwards
}
```

`UnityClock` is a plain class, not a `MonoBehaviour`, so it keeps a file-scoped namespace. It touches no `UnityEngine` API at all — see rule 2 for why it lives in `Game` regardless.

## Behaviour

1. **`UtcNow` is UTC**, offset zero, and is the *world's* clock: when a save was written, how long the app was away. It is not seconds, not since anything, and not comparable with `RunState.Time`.
2. **`UnityClock` reads `DateTimeOffset.UtcNow` on every call and caches nothing.** Two saves in one session must not share a timestamp. It is in `Game` because it is the adapter side of a port (AR §6) and because the day a device clock needs correcting — an NTP offset, a server time — that correction has somewhere to live that core cannot see.
3. **No member beyond `UtcNow`.** A monotonic elapsed reading is the obvious second one and is deliberately absent: nothing needs it, `Time.realtimeSinceStartup` behaves differently across a suspend on Android and nothing here has ever run outside the Editor to say how. AR §6's rule — *a port grows a member when the mechanic that needs it lands* — is the whole content of this decision.
4. **`IClock` promises nothing about monotonicity.** A user changing the date, DST, or an NTP correction all move it backwards. Anything subtracting two timestamps owes a negative-difference branch, and that guard belongs to the code that compares them (M2-13, M2-14) rather than to the port.
5. **Nothing in `Soulvail.Core.Run` takes an `IClock`** — AR §18.2. Simulated time is the sum of each tick's `Dt`. This is asserted by reflection over `RunSession`'s constructors rather than left to review, because the invariant's failure mode is a plausible-looking parameter that nobody questions.
6. `FixedClock` starts where it is told and moves only when told, so a migration fixture can say *"written three days ago"* without sleeping. `Advance` accepts a negative span on purpose — rule 4's case has to be reachable in a test.
7. `BootInstaller` registers `UnityClock` as `IClock`, singleton, in `BootScope` — beside `IRandom` and for the same reason: it is a device the whole app shares, not something a run owns.

## Tests

| Test | Given / When / Then |
|---|---|
| `UtcNow_IsUtc` | a `UnityClock` / read / `Offset == TimeSpan.Zero` |
| `UtcNow_AgreesWithSystemClock` | — / read / within 5 s of `DateTimeOffset.UtcNow` |
| `UtcNow_IsNotCached` | — / read, `Thread.Sleep(50)`, read / second is strictly later |
| `IClock_HasExactlyOneMember` | reflection over `typeof(IClock)` / — / one member, `UtcNow` — the rule-3 decision, pinned rather than remembered |
| `RunSession_TakesNoClock` | reflection over `typeof(RunSession).GetConstructors()` / — / no parameter of type `IClock` (AR §18.2) |
| `FixedClock_StartsWhereTold` | start = 2026-01-01T00:00Z / — / `UtcNow` is that instant |
| `FixedClock_AdvanceMoves` | start, `Advance(3 days)` / — / `UtcNow` is three days later |
| `FixedClock_AdvanceBackwards` | start, `Advance(-1 hour)` / — / `UtcNow` is an hour earlier, no throw |
| `FixedClock_SetReplaces` | `Set(instant)` after an advance / — / `UtcNow` is exactly `instant` |
| `Container_ResolvesClock` | a built `BootScope` container / `Resolve<IClock>()` / a `UnityClock`, and the same instance twice |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None. Nothing is visible, nothing renders, and no behaviour changes — the one honest check is that the container still builds, which `Container_ResolvesClock` makes.

## Out of scope

- Any monotonic or elapsed-time member (rule 3).
- Save timestamps and the DTO that carries them — M2-13.
- *"How long were you away"* and its backwards-clock guard — M2-14.
- Pause-aware or background-aware time. Simulated time already stops when the game does, because it is the sum of ticks nobody is taking.

## As built

As specified in every rule. Three notes, one of them a correction to this spec's own prose.

**1 — Three test rows had no file in the Files table, and went into the fixtures that already own
their kind of assertion rather than into a sixth file.** The table names `UnityClockTests.cs`
("the adapter forwards and does not cache", rows 1–3) and `FixedClockTests.cs` ("the fake's own
rules", rows 6–9); rows 4, 5 and 10 are neither. They landed as additive edits:

- `IClock_HasExactlyOneMember` and `RunSession_TakesNoClock` → `Tests/Core/AssemblyPurityTests.cs`,
  the existing reflection-over-`Soulvail.Core` fixture. Deliberately **not** `Tests.Game`: an AR
  §18.2 invariant about core must not become hostage to the Game assembly compiling, and M2-14a's
  widening lands here too. `RunSession_TakesNoClock` is written as a loop over a `Type[]` holding
  one entry, so that widening replaces one literal and leaves the loop alone.
- `Container_ResolvesClock` → `Tests/Game/Composition/InstallerTests.cs`, which already owns
  `BuildBoot` and every other "this registration exists and is a singleton" row. Kept under the
  spec's name rather than renamed to the fixture's `Boot_` convention, so the spec ↔ test mapping
  stays literal.

**2 — Rule 7's *"beside `IRandom`"* is wrong about `IRandom`, and the registration follows the
Files table instead.** `IRandom` is registered **`Lifetime.Scoped` in `RunInstaller`**, not in
`BootScope`: a seed *is* a run. `UnityClock` is registered where the table says — `BootInstaller`,
`Lifetime.Singleton` — and the comment there argues from the vibrator (one shared device) and
names `IRandom` as the contrast rather than the precedent.

**3 — No implied guard row applies.** There is no new spec type to validate, no `float` door, and
`FixedClock`'s only constructor parameter is a `DateTimeOffset` — a struct, so there is no null to
refuse. Said out loud because "the implied rows were considered" and "the implied rows were
forgotten" look identical in a diff.

**Verified:** 462 EditMode green, 0 failed, 8.6 s (2026-09-11, through `TestRunnerApi`); baseline
452 + the ten rows above. Zero errors, zero analyzer warnings, `git diff ProjectSettings/` empty.
