# M2-05 — `SpawnDirector`: waves that overlap, spawns that telegraph, and paths that keep up

**Size:** M · **Depends on:** M2-04 · **Branch:** `m2-05-spawn-director`
**Design refs:** GD §7.1 (arrival, telegraph, clear), §7.3 (25 % overlap), §12.4 (spawn safety), §11.3; AR §18.1, §18.2, §18.4; ADR-0011 · **Ledger rows:** 5 (the fix), 9

## Goal

A composed stage actually happens: waves start, overlap at 25 % remaining, every body announces itself with a ring 0.8 s before it exists, nothing appears within 6 m of the player or on top of another spawn — and the pathfinder keeps up with the population that makes possible.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Director/SpawnDirector.cs` | Core | Wave pacing, telegraphs, positions, concurrency |
| `Core/Events/SpawnEvents.cs` | Core | `WaveStarted`, `SpawnTelegraphed`, `WaveCleared` (grouped) |
| `Tests/Core/Director/SpawnDirectorTests.cs` | Tests.Core | Every rule below |
| `Game/Adapters/PathRefreshBudget.cs` | Game | The per-frame refresh allowance, pure and testable (**row 5**) |
| `Tests/Game/Adapters/PathRefreshBudgetTests.cs` | Tests.Game | Its arithmetic at 30 and 60 fps |
| *small edits* | | `SpawnPlan` + `SpawnPoints`; `RunScope` fills them from the positions it already dresses and sets `keepAlive` to 0; `NavPathSense` takes its budget from `PathRefreshBudget` instead of `MaxRefreshesPerFrame` and exposes `StalePathCount`; `DebugOverlay` shows wave, alive-vs-cap and stale paths; `RunSession` composes the opening stage, builds the director and ticks it; `EnemySystem.LivingCount` reachable by the director |
| *ripple* | | `RunSessionTests`' ordering rows gain the director's step; `NavPathSense`'s `MaxRefreshesPerFrame` constant disappears and anything quoting it goes with it |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Events;

public readonly struct WaveStarted      { public readonly int Stage; public readonly int Wave; public readonly int WaveCount; }
public readonly struct WaveCleared      { public readonly int Stage; public readonly int Wave; }
public readonly struct SpawnTelegraphed { public readonly ContentId SpecId; public readonly Vector3 Position; public readonly float FiresAt; }
```

```csharp
namespace Soulvail.Core.Director;

public sealed class SpawnDirector
{
    public const float TelegraphTime = 0.8f;        // GD §7.1
    public const float MinPlayerDistance = 6f;      // GD §12.4
    public const float MinSpawnSeparation = 2f;     // row 9
    public const float OverlapFraction = 0.25f;     // GD §7.3
    public const float SpawnInterval = 0.35f;       // spacing between bodies inside one wave

    public SpawnDirector(
        EnemySystem enemies,
        IDomainEvents events,
        IReadOnlyList<Vector3> spawnPoints);

    public int Stage { get; }
    public int Wave { get; }                        // 1-based; 0 until the first wave starts
    public int PendingCount { get; }                // telegraphed, not yet standing
    public bool IsStageComplete { get; }            // M2-10 reads this

    public void Begin(WavePlan plan, float now);
    public void Tick(float now, Vector3 playerPosition, IRandomStream spawn);
    public void Clear();
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class PathRefreshBudget
{
    public PathRefreshBudget(float refreshHz, int maxPerFrame);

    /// How many paths may be recomputed this frame: ceil(activeCount · refreshHz · dt), at least 1,
    /// never more than maxPerFrame.
    public int ForFrame(int activeCount, float dt);
}

// NavPathSense (changed)
/// Enemies whose path is overdue by more than one full refresh period. Zero when it is keeping up.
public int StalePathCount { get; }
```

```csharp
namespace Soulvail.Core.Run;

// SpawnPlan (added)
/// Where the director may put a body. Empty for an arena with no spawning surface, which makes
/// the director inert rather than being an error — see rule 12.
public IReadOnlyList<Vector3> SpawnPoints { get; }
```

## Behaviour

**Waves**

1. `Begin` adopts a `WavePlan`, records its stage, and starts wave 1 immediately. GD §7.1's 2 s **Arrival** beat is M2-10's, and inserting a delay here that nothing seals or announces would be a pause with no cause.
2. Starting a wave publishes `WaveStarted(stage, wave, waveCount)` and queues the wave's bodies **round-robin across its entries**, so a mixed wave arrives interleaved rather than as nine Husks followed by two Bloaters. `WavePlan` says what; this decides when.
3. Bodies leave the queue no faster than one per `SpawnInterval`. A wave that dumps eleven rings in one frame is unreadable, which GD §11.3's readability rule and P1 both refuse.
4. **Wave *i+1* begins when wave *i* has 25 % or fewer of its bodies still alive** (GD §7.3), and only once wave *i* has emptied its queue and has nothing pending. Rounded up, so a wave of 3 overlaps at 1 rather than at 0.
5. `WaveCleared(stage, wave)` is published when the last body of that wave dies — which is often *after* the next wave has started, and that is the point of overlapping.
6. `IsStageComplete` is true when the last wave has started, the queue is empty, nothing is pending, and every body the stage spawned is dead. Nothing is published for it: **the stage is M2-10's to end**, and a `StageCleared` here would be a second answer to the same question.

**Spawns**

7. **A body is telegraphed before it exists.** When a spawn is due, the director picks a position, publishes `SpawnTelegraphed(specId, position, now + TelegraphTime)` and records it as pending; when the telegraph elapses it calls `EnemySystem.Spawn` at exactly that position. **A telegraph is never cancelled or moved** — a ring the player dodged that produced nothing, or produced something elsewhere, is worse than no ring at all.
8. **The concurrency cap counts the pending.** A telegraph is only issued while `LivingCount + PendingCount < plan.Concurrency`, so the cap is never exceeded, rather than being exceeded by however many rings are in flight (three, at these constants). A spawn refused this way is deferred, not dropped: the queue keeps it.
9. **Position choice makes one draw, whatever it then finds.** `spawn.NextInt(0, spawnPoints.Count)`, then walk the list from there, taking the first point that is at least `MinPlayerDistance` from the player (GD §12.4, XZ — AR §18.4) **and** at least `MinSpawnSeparation` from every claimed point. If none qualifies, nothing is telegraphed this tick and the next tick tries again — with another single draw. Stream consumption that depended on where the player was standing is the one thing a seed cannot survive (M1-19's rule, kept).
10. **A point is claimed from the moment it is telegraphed until `TelegraphTime` after its body appears** — ledger row 9's answer. Two spawns can no longer land on one point, nor within two metres of each other while the ring is up, and the claim expires on a clock rather than being held for the enemy's life, because a Husk walks away from where it arrived.
11. The director makes **at most `Concurrency` registry lookups a tick** to count its own wave's survivors, and allocates nothing: the id lists and the claim ring are sized from the plan at construction.

**Wiring**

12. **`SpawnPlan.SpawnPoints` empty means an inert director**, not an error. M0's empty grey box and every core test that starts a run without caring about spawning both mean it, and `SpawnPlan.Empty` must keep working. The cost — an arena dressed without a spawn ring is silent — is bought back in the Editor: `DebugOverlay` shows `director: —` when there are no points, and the manual step below is the check.
13. `RunSession.Start` composes stage `config.StageIndex` into a `WavePlan` built once for the run, constructs the director, and calls `Begin` — after the content validation of M2-02 rule 7 and after `SpawnAll`, so an arena's dressed-in enemies are standing before wave 1 arrives.
14. **`RunSession.Tick` gains one step, after the death check**: `… → enemy behaviours → (dead? end) → director → motor → intent`. A run that ended this tick spawns nothing, and the director sees the same simulated `now` and the same player position everything else did. This is an addition to AR §18.1's ordering table.
15. `RespawnPolicy` stays in the codebase but stops being used: `RunScope`'s `keepAlive` goes to 0, so exactly one thing spawns in any given run. **It is retired by M2-10**, when stage flow makes it unreachable — deleting it here would remove the only spawner the grey-box scene has for the length of one PR. A scene value, so no test asserts it; manual step 1 is where a second spawner would show up.
16. `Clear` forgets the plan, the wave, the pending telegraphs and the claims, and publishes nothing — `EnemySystem.Clear`'s reasoning, for the same moment: the scope is going away and with it every subscriber an event could reach.

**The path budget — ledger row 5**

17. `NavPathSense`'s fixed `MaxRefreshesPerFrame = 4` is replaced by `PathRefreshBudget.ForFrame(activeCount, dt)` = `ceil(activeCount · refreshHz · dt)`, floored at 1 and capped at `maxPerFrame` (16). At 28 enemies and 10 Hz that is **5 paths a frame at 60 fps and 10 at 30 fps**, where the old constant sustained 24 enemies at 60 fps and only 12 at 30 — below GD §11.1's *low* tier, let alone M2-04's cap of 28.
18. The cap of 16 is what stops a frame hitch turning into a hundred `NavMesh.CalculatePath` calls in one frame, which would extend the hitch it is reacting to. When the budget clamps, the shortfall shows up in rule 19 rather than disappearing.
19. **`StalePathCount` is the number of enemies overdue by more than one full period**, recomputed each frame, and `DebugOverlay` shows it — checked by manual step 2 rather than by a test, because the count only means anything against a baked NavMesh. Row 5's actual complaint was not the number 4 — it was that nothing reported the ceiling, so enemies walked into pillars and the arena looked stupid for no visible reason.
20. `ForFrame` is pure and takes no Unity type, so its arithmetic is tested without a NavMesh; `NavPathSense` itself stays Editor/device-verified as M1-19 left it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Begin_StartsWaveOne` | a 3-wave plan / `Begin` / `Wave` 1, one `WaveStarted(stage, 1, 3)` |
| `Wave_InterleavesItsEntries` | a wave of 4 Husks and 2 Bloaters / `Tick` through the whole wave / the telegraphed order alternates rather than grouping (rule 2) |
| `Session_ComposesAndBeginsOpeningStage` | a run started at stage 4 with spawn points / `Start`, `Tick` / the director's `Stage` is 4 and wave 1 has started, after the plan's own enemies were spawned (rule 13) |
| `Spawn_IsTelegraphedFirst` | one body due / `Tick` / a `SpawnTelegraphed`, **nothing registered**; `Tick` at +0.8 s / the enemy exists at the telegraphed position |
| `Spawn_TelegraphNotCancelledByCap` | telegraphed, then the cap fills / +0.8 s / the body still appears (rule 7) |
| `Spawn_RespectsInterval` | 3 bodies due / `Tick` ×3 in one instant / one telegraph; at +0.35, +0.70 / one more each |
| `Spawn_StopsAtConcurrency` | cap 4, 3 alive, 1 pending / `Tick` / nothing telegraphed (rule 8) |
| `Spawn_ResumesWhenRoomAppears` | as above, then one dies / `Tick` / one telegraphed |
| `Spawn_RefusesInsidePlayerClearance` | points [3 m from player, 12 m], draw picks the near one / `Tick` / telegraphed at the far one |
| `Spawn_NoSafePoint_TriesAgainNextTick` | every point within 6 m / `Tick` / nothing telegraphed, no throw; player moves, `Tick` / telegraphed |
| `Spawn_OneDrawPerAttempt` | `FixedRandom` scripted, every point refused / `Tick` ×3 / exactly 3 draws from `Spawn` (rule 9) |
| `Spawn_UsesSpawnStreamOnly` | `Spawn` scripted to the last index, other streams to 0 / `Tick` / the last point |
| `Spawn_NeverReusesAClaimedPoint` | two points 1 m apart, both safe / two spawns due / the second is refused while the first is claimed (**row 9**) |
| `Spawn_ClaimExpires` | one point, two bodies due / `Tick`, +0.8 s (body), +1.6 s / the second body spawns at the same point |
| `Wave_OverlapsAtQuarterRemaining` | wave 1 of 8 bodies, 3 alive / `Tick` / no wave 2; 2 alive / `Tick` / `WaveStarted(…, 2, …)` |
| `Wave_OverlapRoundsUp` | wave of 3, 1 alive / `Tick` / wave 2 starts (rule 4) |
| `Wave_WaitsForItsOwnQueue` | wave 1 at 25 % but two bodies still queued / `Tick` / no wave 2 |
| `Wave_ClearedWhenLastBodyDies` | wave 1 done, wave 2 running / kill wave 1's last / one `WaveCleared(stage, 1)`, published after `WaveStarted(…, 2, …)` |
| `Stage_CompleteOnlyWhenEmpty` | last wave, one alive / — / `IsStageComplete` false; it dies / true |
| `Stage_PublishesNoStageEvent` | a whole stage cleared / — / no event beyond the wave ones (rule 6) |
| `Tick_Deterministic` | same plan, same seed, two directors / identical telegraph positions and times |
| `Tick_AllocatesNothing` | a 28-body stage, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 |
| `Clear_ForgetsEverything` | mid-stage with two telegraphs pending / `Clear` / `Wave` 0, `PendingCount` 0, `IsStageComplete` false, no events (rule 16) |
| `Clear_ReleasesClaims` | a claimed point, then `Clear`, then a new stage / `Tick` / the point is available again |
| `Session_TicksDirectorAfterDeathCheck` | a player who dies this tick with a spawn due / `Tick` / `RunEnded` published, nothing telegraphed (rule 14) |
| `Session_NoSpawnPoints_DirectorInert` | `SpawnPlan.Empty` / `Start`, `Tick` ×100 / no throw, nothing spawned (rule 12) |
| `Budget_ScalesWithPopulationAndDt` | 10 Hz / `ForFrame(28, 1/60)`, `ForFrame(28, 1/30)` / 5, 10 |
| `Budget_AtLeastOne` | `ForFrame(1, 1/60)` / — / 1 |
| `Budget_ClampsAtMax` | max 16 / `ForFrame(28, 0.5f)` / 16 |
| `Budget_ZeroActive_IsOne` | `ForFrame(0, 1/60)` / — / 1, no divide and no throw |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play the Run scene: a wave arrives as a trickle, not a dump; nothing appears within 6 m of you or on top of another spawn; the next wave starts while a few of the last are still alive.
2. **[Editor]** `DebugOverlay` shows the wave number, alive-versus-cap, and `stale 0` while enemies are pathing normally. Stand behind a pillar with a full wave up and it stays at 0 — that is row 5's whole point.
3. **[Editor]** Clear a dressed arena of its spawn points and Play: the overlay reads `director: —` and nothing spawns, which is rule 12 being visible rather than mysterious.
4. **[device]** Whether a 0.8 s ring reads as *fair warning* or as *too late* at 28 enemies on a real screen. Deferred with the rest of the device list.

## Out of scope

- **Drawing the telegraph rings, and the off-screen threat arrows** — M2-12. The events exist here; nothing subscribes yet.
- **Arrival, seal, clear, gate, and advancing the stage** — M2-10. `IsStageComplete` is the handover.
- **Arena spawn-point authoring** — M2-11. Until then the points are the ones `RunScope` already dresses.
- **Retiring `RespawnPolicy`** — M2-10 (rule 15).
- **The 8-second stall** GD §7.3 describes, when fewer than three survivors hide — M2-12's arrow is the answer, and no wave timeout is introduced here to paper over it.

## As built

**Built as specified**, with the five files of the table, and **seven deviations** — one of which
changes a decision. Verified at **625 EditMode green (0 failed, 7.6 s)** and **3 PlayMode green**,
zero errors, zero new analyzer warnings, no `ProjectSettings/` drift. 589 + 36 = 625 exactly, so no
existing row was quietly replaced by a new one.

### Deviations

1. **`RunSession` gained a sixth constructor parameter, `deviceEnemyCap`** — and this is the one
   that changes a decision, because it rippled **24 positional `new RunSession(...)` sites across
   six test fixtures** that the spec's ripple row did not predict. The director's concurrency comes
   from `ThreatBudget.Concurrency(stage)`, which needs GD §11.1's device cap; core cannot read
   `BootInstaller`, and the cap cannot be derived from `enemyCapacity` (64 against 28 — one is what
   the snapshot can carry, the other what the phone can draw). **Ruled by the owner** against the
   alternative of a settable property with a default (the `EnemySystem.Depth` shape, which needs no
   ripple): a cap that arrives by constructor fails while the scope is being built, where a property
   a scope forgets to set composes a whole run at the wrong difficulty in silence. It is guarded
   twice — non-positive, and *above the capacity*, which is the check that matters, since a stage
   allowed more bodies than the snapshot can carry is a stage core is partly blind to.
2. **`ScalingSpec.cs` gained one line: `WaveCurve.Max`.** A run sizes its single `WavePlan` once, at
   the wave curve's ceiling, and that number had no accessor. The alternative was `At(someDeepStage)`,
   which gives the right answer today by accident and stops the day the curve gains a shape.
3. **`RunInstaller.cs`** passes the new cap by name beside `enemyCapacity`, for the reason that file
   already gives about `WithParameter<int>`.
4. **`NavPathSense` builds its own `PathRefreshBudget`** from its own `refreshHz` rather than taking
   one injected. Two sources for one cadence is two numbers that can disagree, and the arithmetic is
   still tested standalone — which is all the spec asked the class to be for. It gained a third
   constructor parameter, `maxRefreshesPerFrame`, defaulted to `PathRefreshBudget.DefaultMaxPerFrame`.
5. **`RunScope.PrewarmCount()` now includes the device cap**, so the pool builds 29 bodies while the
   scene loads. **Ruled by the owner.** With `keepAlive` at 0 (rule 15) the director is the only
   spawner and it may ask for up to 28; sized to the dressed dummies instead, the pool would
   `Instantiate` through the whole of wave 1 — a handful of hitches at exactly the moment the first
   telegraph rings have to be read, which is what `EnemyViews`' own prewarm promise exists to stop.
6. **`DebugOverlay` shows `enemies n/28` against the *device cap*, not the stage's composed
   concurrency.** **Ruled by the owner.** The cap is a composition constant both sides already agree
   on, it is the number the path budget is asked about, and reading the stage's own `C(n)` would mean
   dragging core's state out through a new `RunState` read — which is the thing the overlay's class
   remarks argue against everywhere except the charge line. The cost is stated in the code: an early
   stage whose cap is below 28 looks emptier than it is allowed to be.
7. **The three `Session_*` rows live in `SpawnDirectorTests`**, not in `RunSessionTests`. They need a
   catalog, a mode and a composed stage — a director fixture's furniture — and `RunSessionTests`'
   `Start_OrderUnchanged` still passes untouched, because its mode has an empty roster.

### Things the spec left open, and how they were decided

- **A mode with an empty roster is not composed at all**, and its director is built anyway and never
  given a plan. `WaveComposer` refuses such a mode loudly and correctly (M2-04), and M2-02 ruled the
  empty roster legal — so the two are reconciled by not asking. Every core fixture that starts a run
  builds exactly such a mode, which is why 589 existing rows were unaffected.
- **Composition happens inside `Start`'s validation block, before `RunStarted`**, not after
  `SpawnAll` where rule 13 puts the director. `WaveComposer` throws for a mode that introduces
  nothing at or before the stage, and composing after the announcement would strand it — ledger row
  3's failure, re-made. The plan and composer are held in locals until the run is built. The cost,
  stated in the code: a composition that succeeds and a `Start` that then fails leaves the `Spawn`
  stream advanced. Accepted, because the run it was drawn for does not exist, and the alternative is
  a second copy of the composer's eligibility rule.
- **A wave with no bodies clears the moment it opens.** Only the *stage* is guaranteed a body
  (M2-04 rule 8); a late wave whose allowance cannot reach the cheapest archetype is empty, and
  saying so at `StartWave` rather than leaving it to the next tick's sweep keeps the event order
  honest.
- **`WaveCleared` is published for every uncleared wave, not only the current one.** The overlap
  means a wave three back can still be holding a survivor. The per-wave id lists are compacted as
  bodies die, so the sweep costs one registry lookup per id *still being tracked* — bounded by the
  living population, which the cap bounds in turn (rule 11).
- **The claim expires at `firesAt + TelegraphTime`**, i.e. 1.6 s after the ring goes up. Rule 10
  says "until `TelegraphTime` after its body appears"; this is that, spelled once.
- **`RunSession.End` clears the director**, at the same moment and with the same silence as
  `State.Enemies.Clear()`.

### What PlayMode caught that EditMode could not

`NavPathSense`'s new third parameter broke the run's composition outright: **VContainer resolves
every constructor parameter from the container or a `WithParameter` and never falls back to a C#
default**, so the container went looking for a registration of `System.Int32` and the whole
`RunScope` failed to build. 625 EditMode rows were green at the time. The trap is already written
down in `RunScope`'s own comment about `refreshHz` — this is the second time it has fired, and the
comment now names the incident. **The spec's instruction to re-run PlayMode is what found it.**

### Manual verification — not run, and why

The four Editor/device steps are the owner's. Note for step 1: the Run scene still dresses **eight
dummies at the eight positions that are now also the spawn points**, so the arena opens with eight
standing and stage 1's C(1) is 10 — the director tops up by two and then waits for kills. That is
the spec's own wiring (rule 15 changes `keepAlive`, not the dressing), and M2-11 is what separates
"where an arena is dressed" from "where a wave may arrive".
