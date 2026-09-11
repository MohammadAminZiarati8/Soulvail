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

**Files: 4 new, as specified** — `WavePlan.cs`, `WaveComposer.cs`, `WaveComposerTests.cs`, `WavePlanTests.cs` — plus the four small edits the table named (`EnemySpec.ThreatCost`, `EnemyDefinition._threatCost`, `Husk.asset` = 4, `BootInstaller.DeviceEnemyCap` = 28) and the ripple. Docs: PROGRESS, ROADMAP, this footer, plus [Traps §4](../../Traps.md) and [AR §18.3](../../Architecture.md#183-numbers-and-identity), which the protocol points durable lessons at.

**Six deviations, one that changes a decision.**

1. **Rule 9's "stack-allocated eligibility buffer" is not available for this type, so the buffers are pooled fields.** `RosterEntry` holds a `ContentId`, which holds a `string`, so it is a managed type and `stackalloc RosterEntry[n]` does not compile — `ModeSpec.RosterFor`'s own doc comment promising a `stackalloc` caller was wrong when it was written. Four parallel arrays (roster, costs, counts, affordable indices) are grown on the first `Compose` with a roster that size and reused after, which is what rule 9's *"allocates nothing after the first call"* asks for either way. `Compose_AllocatesNothing` passes at 1,000 iterations.
2. **A stage's spend is tracked as `int`, and a wave's allowance is the cumulative share minus it — not a running `float` remainder.** *This one changes a decision*, because rule 3's two sentences read as a running carry and a running carry gets rule 3's own headline number wrong: stage 1 composes **nine** Husks, not ten. `B·1/3` = 13.333334 less the 12 it buys, carried onto `B·2/3` = 26.666667, is 27.999999 — and the tenth Husk costs 4 of the 4 that are not quite there. Costs are whole numbers, so the integer form is exact, the last wave's cumulative fraction is exactly 1, and `UnspentThreat` conserves the budget to the point. Filed as an invariant (AR §18.3) because anything else that divides a budget across steps owes the same shape.
3. **Rule 8's forced buy is once per *stage*, not once per wave**, and it may overspend by exactly one body. The rule's own sentence is conditional (*"while its budget can afford the cheapest"*) while its test row is not (*"one body, not zero"* for a budget below the cheapest cost), and the two can only be reconciled by letting one buy ignore the budget. Per wave it would turn a mis-authored budget into a trickle of free enemies; `UnspentThreat` clamps at 0 so the overspend is reported as no surplus rather than as a debt. Rule 8's cap clause is implemented too and is unreachable in practice — `ConcurrencyCurve` guarantees `C(n) ≥ 1` — so it is belt-and-braces, labelled as such.
4. **Two guards the spec did not name.** An **empty roster** throws (legal content per M2-02 — a mode whose population comes from its spawn plan — so it is refused at the composer, which is the thing that cannot proceed), and a stage that **introduces nothing at or before itself** throws, because rule 8 has no cheapest archetype to reach for and the alternative is the empty arena rule 8 exists to prevent.
5. **`WavePlan`'s write side is `internal`** (`Begin`/`SetWave`/`Complete`, plus `MaxWaves`/`MaxEntriesPerWave` for the composer's capacity check), so the public surface is exactly the spec's. `Soulvail.Tests.Core` has no `InternalsVisibleTo`, so `WavePlanTests` arranges a two-entry wave by *scripting the draws* rather than by poking the plan — which is also M2-05's only route.
6. **`BootInstaller.DeviceEnemyCap` is the constant and nothing more.** Rule 1 calls it *"a record of arithmetic rather than a behaviour"* and the Files table names no installer wiring, so nothing in a live run constructs a `ThreatBudget` or a `WaveComposer` yet — M2-05 does, with the director that spawns from a plan. PROGRESS's framing of M2-04 as *"the first caller to construct a `ThreatBudget` in a live run"* is the thing that moved.

**Four test rows had no file in the table** and went into the fixtures that already own their kind of assertion, on M2-01's precedent: `Spec_ThreatCostBelowOne_Throws` into `EnemyRegistryTests` (which owns `EnemySpec`'s validation rows), and `Husk_CostMatchesDesign` into `EnemyDefinitionTests.Husk_ToSpec_MatchesGameDesign`. `Compose_ReusesPlan` went into `WavePlanTests`, whose stated purpose is the container's reuse. Two rows were **added**: `Compose_ConcurrencyFromCurveAndCap` (rule 2 had no row) and `Compose_ShrinkingStage_ForgetsTheExtraWave` (the direction `Compose_ReusesPlan` cannot catch — a plan that cleared only the waves it was about to write would pass the shallow→deep row and fail the deep→shallow one). `Husk_EveryYamlKeyBindsToAField` was added because `_threatCost` initialises to 4 and ships 4, which makes the value row vacuous (Traps §7).

**The ripple was nearly twice what a grep predicted, in the way Traps §2 says.** Eight `new EnemySpec(...)` sites in Tests.Core by grep; **fifteen** once the compiler had its say, because seven use target-typed `new(...)`, which no `grep` for `new EnemySpec` finds — eleven Tests.Core files in all. **Tests.Game has none**: it builds every spec through `EnemyDefinition.ToSpec()`, so the table's "and Tests.Game" was one assembly too many. Every site failed loudly rather than silently, and it could have gone the other way — `threatCost` sits next to `targetPriority` and both are `int`, so a purely positional site would have transposed them quietly. What saved it is that each one passed `isElite:` as a non-trailing named argument, which moves out of position and fails `CS8323`. All fifteen now name `threatCost:`, and the Husk's 1-against-4 makes a transposition a red asset test rather than a balance mystery.

**Verification:** **589 EditMode green, 0 failed, 0 skipped, 7.0 s** through `TestRunnerApi` — M2-03's 554 plus exactly 35 new rows (22 + 12 + 1). Zero errors, zero analyzer warnings, **no `ProjectSettings/` drift**, and the nine expected Console warnings and nothing else. The new YAML key was **proved to bind rather than assumed**: `ForceReserializeAssets` on `Husk.asset` left it byte-identical and `ToSpec()` read `ThreatCost == 4` back off the shipped asset. PlayMode was not re-run — nothing on the run boot path changed; `WaveComposer` has no caller yet.

**One piece of scaffolding, created and deleted:** `Tests/Core/Support/TempSuiteRunner.cs`, because `TestRunnerApi.Execute` is refused from an MCP command again (Traps §4 said it was not, and now says otherwise). Gone before handover; `git status` shows no trace.
