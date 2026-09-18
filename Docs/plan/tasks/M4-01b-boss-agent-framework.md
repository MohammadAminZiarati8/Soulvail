# M4-01b — A boss is a combatant with phases: 66/33, an invulnerable beat, and adds

**Size:** M · **Depends on:** M4-01a, M2-05, M2-06, M2-12b · **Branch:** `m4-01b-boss-agent-framework`
**Design refs:** GD §9.1 (all seven rules), §9.2, §7.1; AR §18.1, §18.4; ADR-0006 · **Ledger rows:** [M4 row 1](../ROADMAP.md#carry-forward-into-m4) is inherited rather than answered — see *Out of scope*

## Goal

A stage can be a **boss stage**: the director spawns one authored boss instead of waves, it crosses 66 % and
33 % of its health into a new phase with a brief invulnerable beat that clears the adds, and it can summon
more. The Warden's actual attacks are M4-02's; this is the thing they hang on.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BossSpec.cs` | Core | Phases, their thresholds, the beat's length, and what each phase may summon |
| `Core/Ai/BossBehaviour.cs` | Core | `IEnemyBehaviour` that owns the phase machine and delegates the rest |
| `Core/Ai/BossPhases.cs` | Core | The threshold crossing, the beat, and the add-clear — pure, and the thing the tests drive |
| `Tests/Core/Ai/BossPhasesTests.cs` | Tests.Core | Crossings, the beat, hysteresis, and the orderings |
| `Tests/Core/Content/BossSpecTests.cs` | Tests.Core | Authoring refusals, and the roster lookup |
| *small edits* | Core | `ModeSpec` gains a boss roster (rule 1); `SpawnDirector` branches once on *"is this a boss stage"*; `EnemySpawned` gains nothing — rule 7 |
| *ripple* | Tests.Core | `ModeSpec`'s constructor gains a parameter: `ModeDefinition` and the mode fixtures |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Content/BossSpec.cs
public sealed class BossSpec
{
    public BossSpec(ContentId id, ContentId enemySpecId, IReadOnlyList<BossPhaseSpec> phases,
                    float beatSeconds);
    public ContentId Id { get; }
    /// <summary>The EnemySpec the boss's body, health and contact damage come from (rule 2).</summary>
    public ContentId EnemySpecId { get; }
    public IReadOnlyList<BossPhaseSpec> Phases { get; }
    public float BeatSeconds { get; }
}

public sealed class BossPhaseSpec
{
    /// <param name="entersBelow">Health fraction at or under which this phase begins. 1.0 for the first.</param>
    public BossPhaseSpec(float entersBelow, IReadOnlyList<AddWave> summons);
    public float EntersBelow { get; }
    public IReadOnlyList<AddWave> Summons { get; }
}

public readonly struct AddWave
{
    public AddWave(ContentId specId, int count);
    public ContentId SpecId { get; }
    public int Count { get; }
}

// Core/Ai/BossPhases.cs — pure; no agent, no events
public sealed class BossPhases
{
    public BossPhases(BossSpec spec);
    public int Current { get; }               // 0-based
    public bool IsInBeat { get; }
    /// <returns>true on the tick a new phase begins — the caller clears adds and telegraphs.</returns>
    public bool Tick(float hpFraction, float dt);
}

// Core/Events/BossEvents.cs
public readonly struct BossPhaseChanged   { public BossPhaseChanged(int enemyId, int phase, int ofPhases); }
public readonly struct BossBeatStarted    { public BossBeatStarted(int enemyId, float seconds); }
public readonly struct BossBeatEnded      { public BossBeatEnded(int enemyId); }
```

## Behaviour

1. **Which stages hold a boss is authored on the mode, not computed and not hardcoded.** `ModeSpec` gains a
   boss roster with the same shape its enemy roster already has — `(bossId, everyNStages)` — so
   `Descent.asset` says *"every 5th"* in a field rather than a `stage % 5 == 0` anywhere in code. **The owner
   ruled this at M4-00a**: *"we should be able to select which stages what enemies should have."* A stage
   editor is the eventual shape and is a [parking-lot](../ROADMAP.md#parking-lot) line; the field is what makes
   it cheap later and what stops M7's second boss being a code change.
2. **A boss is an `EnemyAgent` with a `BossBehaviour`, not a new kind of object.** It spawns through
   `EnemySystem`, takes damage through `Health`, is targeted by the same `Targeter` and is pooled by the same
   `EnemyRegistry` — which is why `BossSpec` names an `EnemySpecId` rather than restating a body. **The
   alternative was weighed at M4-00a and refused**: a parallel boss system means two spawn paths and two damage
   paths, which is the duplication M2 spent three tasks removing, and every later system (Elites, Ordeals,
   affixes) would have to know about both.
3. **Phases enter at a health fraction and never leave.** `EntersBelow` is 1.0, 0.66, 0.33 for the Warden.
   **Crossing is one-way and latched** — healing a boss above 66 % does not put it back in phase 1 — because
   the beat clears adds and resets pressure, and a boss oscillating across a threshold would clear the arena
   every few seconds. That is hysteresis by construction rather than by a tolerance.
4. **A phase change opens a beat, and during the beat the boss is invulnerable and does not act.**
   `EnemyAgent.IsVulnerable` already exists (`EnemyAgent.cs:180`) and is what the beat drives — so the damage
   path needs no edit at all, which is rule 2 paying for itself. The beat is `BeatSeconds` long, authored, and
   **GD §9.1 rule 3's *"clears adds"* happens on the beat's first tick**, not its last: the point is to reset
   pressure *now*, and a player who has just been told a phase is changing should see the arena empty
   immediately.
5. **Adds are summoned through the existing spawn path and count against the concurrency cap.** An `AddWave`
   is `(specId, count)` and goes through whatever `SpawnDirector` already uses, so a summoned Husk is a Husk in
   every way that matters. **A summon that would exceed the device cap is clamped, not queued** — a boss that
   banks summons and releases twenty at once on a cheap phone is a frame-rate bug wearing a design hat.
6. **`BossPhases` is pure and takes `(hpFraction, dt)`.** No agent, no events, no registry — so the whole of
   GD §9.1 rule 3 is testable without a world, which is how M3-01a's `LevelTracker` and M3-08a's `LevelUpFlow`
   were built and is why both have exhaustive suites. `BossBehaviour` is the thin part that reads
   `Health`, calls `Tick`, and publishes.
7. **`EnemySpawned` gains nothing and there is no `IsBoss` flag on it.** M3-13b added `IsElite` as a defaulted
   field and that was right for a *look*; a boss is not a look. **What a view needs is the phase count**, which
   arrives on `BossPhaseChanged` with `ofPhases` — so M4-04's segmented bar learns its segment count from the
   first event rather than from a flag, and no existing event signature moves. Re-examined here rather than
   copied, because the precedent is nearby and does not apply.
8. **Boss health is authored on the `EnemySpec` like every other body** (ADR-0006), and **a test asserts the
   fight lands in GD §9.1 rule 5's 75–120 s** against expected player DPS at stage 5, computed from the shipped
   assets rather than typed. **The owner delegated this at M4-00a and the reasoning is M3-12c's:** deriving HP
   from DPS at spawn would put a balance number in code, which is exactly what ADR-0006 exists to stop and what
   M3-15 just flagged Overflow's 2 % for. The test is the thing that keeps the authored number honest, and it
   **names what it excludes** the way `Ttk_ExcludesSurvivabilityAndReach` does.
9. **Ordering: the phase check runs after damage is applied and before the behaviour acts**, so a hit that
   crosses 33 % opens the beat on the same frame it landed rather than one later. Recorded in
   [AR §18.1](../../Architecture.md#181-ordering) if it survives the build.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Phase_StartsAtZero` | a three-phase spec / constructed / `Current` is 0 and `IsInBeat` is false |
| `Phase_EntersAtTheThreshold` | 0.66 / hp driven 0.67 → 0.66 / `Tick` returns true exactly once |
| `Phase_IsLatchedAgainstHealing` | phase 1 entered / hp healed to 0.9 / `Current` stays 1 and no crossing is reported |
| `Phase_SkipsWhenOneHitCrossesTwo` | hp 0.7 → 0.2 in one hit / ticked / lands in the **last** phase it passed and reports **one** crossing, not two beats |
| `Beat_HoldsForTheAuthoredSeconds` | beat 1.5 s / ticked at 0.1 / `IsInBeat` true for 15 ticks and false on the 16th |
| `Beat_MakesTheBossInvulnerable` | a crossing / during the beat / `IsVulnerable` is false, and damage applied in it changes no health |
| `Beat_ClearsAddsOnItsFirstTick` | four adds alive / a crossing / all four are gone on that tick, not at the beat's end (rule 4) |
| `Beat_BossDoesNotActDuringIt` | a crossing / during the beat / the delegated behaviour is never ticked |
| `Summon_GoesThroughTheSpawnPath` | phase 2 with `(enemy.husk, 3)` / entered / three Husks exist and are ordinary agents — same registry, same pool |
| `Summon_ClampsToTheConcurrencyCap` | cap nearly full / a phase wanting six / spawns what fits, drops the rest, and **does not queue them** (rule 5) |
| `Boss_SpawnsOnAnAuthoredStage` | roster *every 5th* / stage 5 / one boss and no wave; stage 4 and 6 / waves and no boss |
| `Boss_StageRuleIsAuthoredNotHardcoded` | a mode authored *every 3rd* / stages 1–9 / bosses at 3, 6, 9 — rule 1, asserted against a value no shipped asset uses |
| `Boss_FightLengthIsInsideTheBand` | expected player DPS at stage 5 from the shipped assets / `Warden.asset`'s HP / between 75 and 120 s, and the row **names its exclusions** (rule 8) |
| `Phase_TickAllocatesNothing` | 10 000 ticks across crossings / `AllocationAssert.None` / zero — it is a per-frame path |
| `Order_CrossingIsSeenTheFrameTheHitLands` | a hit crossing 33 % / one frame / the beat is open in that frame's events, not the next (rule 9) |

**Guard rows are implied, not listed:** a phase list that is empty or out of order, `EntersBelow` outside
(0, 1], a non-finite beat, a summon count below 1, and an `EnemySpecId` no catalog knows.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 5. *Expected: no wave — one large body, and the debug overlay's enemy count is 1.*
2. **[Editor]** Damage it past 66 %. *Expected: it stops moving and stops taking damage for the authored beat,
   any adds vanish, then it resumes.*
3. **[Editor]** Past 33 %. *Expected: the same again, and the phase readout on the overlay reads 2 of 3.*
4. **[device]** Whether the beat reads as *"something is about to change"* or as *"the game froze"* — GD §9.1
   rule 3's entire claim, and unanswerable in an Editor at 30 fps. Deferred to
   [M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4).

## Out of scope

- **The Warden's actual attacks** — shield-slam, fissures. M4-02. This task spawns a body that changes phase
  and summons; what it *does* in a phase is content.
- **The arena hazard and the boss's view** — M4-03.
- **The segmented health bar** — M4-04, which has owned it since M2-15 and reads its segment count from
  rule 7's event.
- **A boss holding a skill or a buff.** M4-01a built the seam; **nothing here walks through it**, because the
  Warden needs none. M7's Archon is the first boss that does, and it will find the seam already built.
- **Archons, biome transitions, stage 20** — GD §9's *"every 20th is an Archon"* is M7.
- **Ordeals stacking on repeat bosses** — M6.
- **M4 ledger row 1 — the unreadable HUD.** This task adds no readout. **It inherits the row rather than
  escaping it**: M4-04's bar lands on the same HUD, and M4-07's acceptance will be asked the same readability
  question M3-15 could not answer.

## As built

_Filled at merge._
