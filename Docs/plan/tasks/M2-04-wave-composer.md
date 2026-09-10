# M2-04 — `WaveComposer`: a stage's budget becomes waves, under a cap that is priced rather than guessed

**Size:** M · **Depends on:** M2-03 · **Branch:** `m2-04-wave-composer`
**Design refs:** GD §12.1–12.2 (budget, waves, concurrency), §11.1–11.2 (device tiers, device independence), §8.1 (threat costs), §8.2 (introduction schedule), §8.3; ADR-0011 · **Ledger rows:** 4, 5 (the number; M2-05 owns the fix)

## Goal

A stage's threat budget turns into a fixed list of waves — what, how many, in which wave — deterministically from the seed, inside a concurrency cap chosen against what the frame actually costs.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Director/WavePlan.cs` | Core | `WavePlan` (preallocated, refilled per stage) + `WaveEntry` |
| `Core/Director/WaveComposer.cs` | Core | Budget → composition |
| `Tests/Core/Director/WaveComposerTests.cs` | Tests.Core | Every composition rule |
| `Tests/Core/Director/WavePlanTests.cs` | Tests.Core | The container's own rules and its reuse |
| *small edits* | | `EnemySpec` + `ThreatCost` (GD §8.1); `EnemyDefinition` + its serialized field; `Husk.asset` = 4; `BootInstaller` + `DeviceEnemyCap = 28` |
| *ripple* | | every `new EnemySpec(...)` in Tests.Core and Tests.Game — a required parameter, so the compiler enumerates them |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Director;

/// One archetype's share of one wave.
public readonly struct WaveEntry
{
    public WaveEntry(ContentId specId, int count);
    public ContentId SpecId { get; }
    public int Count { get; }
}

/// A stage's composition. Allocated once per run and refilled per stage — never per wave.
public sealed class WavePlan
{
    public WavePlan(int maxWaves, int maxEntriesPerWave);

    public int Stage { get; }
    public int WaveCount { get; }
    public int Concurrency { get; }        // C(n): the most bodies that may be alive at once
    public float UnspentThreat { get; }    // what the cap refused to let it buy — M7-02's Elites spend it

    public int EntryCount(int wave);                 // wave is 1-based
    public WaveEntry Entry(int wave, int index);
    public int BodyCount(int wave);                  // Σ Count over the wave
}

public sealed class WaveComposer
{
    public WaveComposer(ContentCatalog catalog, ThreatBudget budget);

    /// Fills `destination` with stage `stage`'s waves. Deterministic given the stream's position.
    public void Compose(int stage, ModeSpec mode, WavePlan destination, IRandomStream spawn);
}

// EnemySpec (added)
public int ThreatCost { get; }     // GD §8.1: Husk 4, Spitter 7, Bloater 8 …
```

## Behaviour

**The cap, and what it costs — ledger rows 4 and 5**

1. **The device cap is 28**, `BootInstaller.DeviceEnemyCap`, GD §11.1's mid tier, until M8-03 detects it. A record of arithmetic rather than a behaviour — what it constrains is rules 6 and 7 — but it is chosen against two measured numbers rather than inferred, which is what ledger rows 4 and 5 ask for:
   - **`EnemySystem`'s `AlliesNearby` is `n² − n` XZ comparisons a frame** over the registered count (row 4). At 28 that is **756 a frame**; at GD's high tier of 40, **1,560**; at the registry's capacity of 64, **4,032**. 756 squared-distance comparisons is not a cost worth restructuring for, so the O(n²) stays and this line is the record of the arithmetic. **A cap above 40 needs a spatial hash first** — noted in the ROADMAP parking lot, owned by M8-03 if a high tier ever ships.
   - **The path refresh cannot currently sustain 28** (row 5). `NavPathSense` refreshes at most `MaxRefreshesPerFrame = 4` enemies a frame against a 10 Hz cadence, so it sustains **24 at 60 fps and 12 at 30 fps** — below this cap and below GD's low tier of 18. Nothing reports it; routes simply go stale and enemies walk into pillars. **M2-05 makes that budget scale with population and frame time**, and this task depends on it landing: 28 is the number both tasks are written against.
2. `WavePlan.Concurrency` is `budget.Concurrency(stage)`, which is already `min(10 + n/2, deviceCap)` — so the cap binds from stage 36 upward and before that the curve is what limits the screen.

**Composition**

3. `Compose` reads `W = budget.Waves(stage)` and `B = budget.Budget(stage)`, and splits `B` across the waves **rising triangularly**: wave *i* of *W* gets `B · i / (W(W+1)/2)`. **What a wave cannot spend carries into the next one**, so a stage spends its whole budget rather than losing a fraction of an archetype's cost at every boundary — at stage 1 that is the difference between GD §12.1's ten Husks and nine. Only the last wave's remainder survives, as `UnspentThreat`. A stage therefore opens gently and closes hard, which is GD §4.2's shape, and the split needs no randomness — two runs from one seed differ in *what* arrives, never in how much.
4. The eligible vocabulary is `mode.RosterFor(stage, …)` (GD §8.2). Costs come from `catalog.Enemy(id).ThreatCost`. **The composer never sees an archetype the mode did not list**, which is what makes a Boss Rush a data change.
5. **The stage's introduction goes first.** If `mode.TryGetIntroduction(stage, …)` answers, wave 1 buys one of that archetype before anything else — GD §8.2's *"in a wave where they're the only new thing"*, which the mode's one-introduction-per-stage rule (M2-02 rule 2) is the other half of.
6. Otherwise a wave is filled by repeated draws: pick uniformly among the eligible archetypes the remaining wave budget can afford, buy one, subtract its cost. Draws come from the **`Spawn` stream and no other** (ADR-0011). A wave stops when nothing is affordable **or** when its body count reaches `Concurrency`.
7. **Quality over quantity, GD §11.2.** When a wave is at its body cap with budget left, the composer stops adding bodies and starts **upgrading**: replace the cheapest planned body with the most expensive affordable archetype, repeat while an upgrade is affordable and strictly more expensive. That is the whole mechanism by which a stage-60 wave differs from a stage-30 one on a phone that cannot draw more of them. What is left when no upgrade is affordable is `UnspentThreat`, reported rather than discarded — **M7-02's Elites are what eventually spend it** (an Elite is 2.5× cost for one body, GD §8.3, which is upgrading by another name).
8. **A wave is never empty while its budget can afford the cheapest eligible archetype.** The first buy ignores the body cap for exactly one body, because a cap of zero would otherwise be spelled as a stage with nothing in it.
9. `Compose` **allocates nothing after the first call**: `WavePlan` is built once per run with `maxWaves = 5` (the wave curve's ceiling) and `maxEntriesPerWave` = the roster's length, and `Compose` overwrites it. The composer keeps one stack-allocated eligibility buffer per call.
10. `WavePlan` refuses a wave index outside `1..WaveCount` and an entry index outside its wave, rather than returning a zeroed `WaveEntry` — a `default(WaveEntry)` has no spec id and would surface one layer down as the catalog's "no enemy with id ''".
11. Entries are **aggregated per archetype**: nine Husks are one entry with `Count = 9`, not nine entries. The director spaces them out (M2-05); the plan says what, not when.
12. `ThreatCost` is a required `EnemySpec` parameter and must be at least 1. A free archetype would let rule 6 loop for ever, and there is no such thing in GD §8.1.

## Tests

| Test | Given / When / Then |
|---|---|
| `Compose_WaveCountFromCurve` | stage 1, stage 15 / `Compose` / `WaveCount` 2, then 5 |
| `Compose_BudgetRisesAcrossWaves` | stage 10, one archetype / — / each wave's total cost is ≥ the one before, and the last is the largest |
| `Compose_ConservesBudget` | stage 5, Husk only (cost 4) / — / Σ(cost of every body) + `UnspentThreat` == `B(5)` = 102.4, within 0.01 |
| `Compose_StageOne_TenHusks` | stage 1, Husk only / — / 10 bodies across 2 waves, `UnspentThreat` 0 — GD §12.1's own "rough composition", and the row that fails if a wave's remainder is dropped instead of carried (rule 3) |
| `Compose_OnlyEligibleArchetypes` | roster Husk 1, Spitter 2; stage 1 / — / no Spitter anywhere |
| `Compose_IntroductionAppearsInWaveOne` | Spitter introduced at 2, stage 2 / — / wave 1 contains ≥ 1 Spitter (rule 5) |
| `Compose_Deterministic` | same seed, same stage / `Compose` twice into two plans / identical entry-for-entry |
| `Compose_SeedChangesComposition` | two seeds, stage 10, mixed roster / — / the compositions differ |
| `Compose_UsesSpawnStreamOnly` | `FixedRandom` with `Spawn` scripted and the shared stream elsewhere / — / the composition follows `Spawn` |
| `Compose_NeverExceedsConcurrency` | stage 60, deviceCap 28 / — / every wave's `BodyCount` ≤ 28 |
| `Compose_CapBinding_SpendsOnQuality` | stage 60, roster Husk 4 and Bloater 8, cap 28 / — / bodies == 28 and the mean cost per body is above the Husk's 4 (rule 7) |
| `Compose_CapBinding_ReportsUnspent` | as above with nothing affordable to upgrade to / — / `UnspentThreat > 0` and total spent + unspent == `B(60)` |
| `Compose_NoUpgradeAvailable_LeavesUnspent` | one archetype only, cap binding / — / bodies == cap, unspent is the remainder |
| `Compose_TinyBudget_StillBuysOne` | a mode whose stage-1 budget is below the cheapest cost / — / one body, not zero (rule 8) |
| `Compose_AllocatesNothing` | warm-up, one `WavePlan` / 1 000 × `Compose` / allocated-bytes delta == 0 |
| `Compose_ReusesPlan` | compose stage 1 then stage 9 into the same plan / — / no trace of stage 1 (`Stage`, `WaveCount`, entries all restated) |
| `Plan_WaveOutOfRange_Throws` | `WaveCount` 2 / `EntryCount(3)`, `EntryCount(0)` / both throw |
| `Plan_EntryOutOfRange_Throws` | wave 1 with 2 entries / `Entry(1, 2)` / throws |
| `Plan_BodyCountSumsEntries` | entries 5 Husks + 2 Bloaters / `BodyCount(1)` / 7 |
| `Spec_ThreatCostBelowOne_Throws` | cost 0 / `new EnemySpec` / throws (rule 12) |
| `Husk_CostMatchesDesign` | `Husk.asset` / load / `ThreatCost == 4` (GD §8.1) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None yet — nothing spawns from a plan until M2-05. `Compose_StageOne_TenHusks` is the check that the numbers land where GD §12.1 says they should.

## Out of scope

- **Spawning any of it**, wave pacing, telegraphs, positions — M2-05.
- **Elites and affixes** — M7-02, funded by `UnspentThreat` (rule 7).
- **Bosses**, which GD §12.1 says are additive and never paid from this budget — M4.
- **Device-tier detection** — M8-03. The cap is a constant here, and rule 1 is the argument for its value.
- **The path-refresh fix** — M2-05, though this task's cap is what makes it necessary (row 5).

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._
