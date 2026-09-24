# M7-02b — Who buys an Elite, and a full arena that keeps its mix

**Size:** M · **Depends on:** M7-02a · **Branch:** `m7-02b-who-buys-an-elite`
**Design refs:** GD §8, §8.2, §8.3, §11.2, §12.1, §12.2, §15; AR §18.1, §18.3; ADR-0011 · **Ledger rows:** [11](../ROADMAP.md#carry-forward-into-m7) — **discharged here**, diagnosed at M7-00a; the Echo parking-lot line — its figure moves (rule 10)

## Goal

The composer buys Elites from stage 8, and when the arena is full it spends what is left by
promoting the bodies it already drew — so a capped wave at stage 30 is the same mix of archetypes it
was at stage 15, harder, and never twenty-eight of one thing.

## Row 11, diagnosed

M6-11's instrument logged *"from stage ~21 waves 4–5, and from ~26 waves 3–5, are Bloaters only,
20–28 each"*, and the row said *"the cause is not diagnosed here."* It is `WaveComposer`'s rule 7,
working exactly as written. When a wave reaches the concurrency cap with allowance left,
`WaveComposer.Upgrade` swaps the cheapest planned body for the most expensive affordable archetype,
and repeats until nothing is affordable — which, with surplus to spare, is **every** body. At stage
21 on the mid tier `C = 20`; wave 5's allowance is `B(21) · 5/15 = 213`; twenty bodies drawn from
Husk 4 / Spitter 7 / Bloater 8 cost about 126, and the remaining 87 buy twenty swaps to the dearest
archetype, the Bloater. The arithmetic reproduces the log to the wave.

**So adding archetypes does not answer it** — which the row assumed when it named M7-01. It moves the
collapse to whatever is dearest; with M7-01c's Warden at 14 on the roster, a capped wave becomes
twenty Wardens. The fix is the rule, and GD §8's own sentence is what it restores: *"each archetype
constrains the player's space in a different way."* A wave that is one archetype constrains it in
one.

## The ruling: quality is Elites, not a different crowd

GD §11.2 lists three ways surplus becomes quality — *"Elites, expensive archetypes, extra affixes."*
**The middle one is refused, and row 11 is the evidence**: it is the only one of the three that
replaces the mix the draw chose rather than upgrading it. Surplus is spent by **promoting** drawn
bodies to Elites (rule 5); what is left after every body is one waits for
[M7-02c](../ROADMAP.md#m7--content-pass)'s second affix, and until then is `UnspentThreat`. The
arithmetic says there is room: at stage 40 wave 5 is **626** threat over 28 bodies whose drawn mix
averages about 9, and promoting all of them costs 1.5 × 9 × 28 ≈ **378** on top of the 252 drawn —
**630**. Elites absorb the budget almost exactly to stage 40, and GD §12.1's table asks for
*"multi-affix Elites"* past it. GD §11.2's line is amended in this PR to say so.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Director/WaveComposer.cs` | Core | **Substantial.** Elite purchases from `FirstStage`; `Promote` replaces `Upgrade` (rules 2–7) |
| `Core/Director/WavePlan.cs` | Core | `WaveEntry.EliteCount`, and the stage's total (rule 8) |
| `Core/Director/SpawnDirector.cs` | Core | Each queued body knows whether it is an Elite, and the spawn says so (rule 9) |
| `Tests/Core/Director/EliteCompositionTests.cs` | Tests.Core | **New.** Every rule below, and row 11's instrument (rule 11) |
| *small edits* | Core, Docs | `Core/Content/ModeSpec.cs` — `EssenceSpec.ForStageClear` gains `elites`, defaulted (rule 8); `Core/Stage/StageFlow.cs` — the clear pays it; `Docs/GameDesign.md` — §11.2's list, amended; `Docs/Architecture.md` — one §18.3 row |
| *ripple* | Tests.Core | `WaveComposerTests.Compose_CapBinding_SpendsOnQuality` **rewritten** — it pins the collapse (rule 12); `Compose_CapBinding_ReportsUnspent`'s comment; every other composer, plan, director and flow row runs unedited on fixtures that author no `EliteSpec` (rule 4) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Director;

public readonly struct WaveEntry
{
    /// <param name="eliteCount">How many of <paramref name="count"/> are Elites. 0..count.</param>
    public WaveEntry(ContentId specId, int count, int eliteCount = 0);

    /// <summary>How many of <see cref="Count"/> are Elites (GD §8.3). Never more than <see cref="Count"/>.</summary>
    public int EliteCount { get; }
}

public sealed class WavePlan
{
    /// <summary>How many Elites the whole stage holds — what GD §15's per-Elite term is paid on.</summary>
    public int EliteCount { get; }
}
```

```csharp
namespace Soulvail.Core.Content;

public readonly struct EssenceSpec
{
    /// <param name="elites">How many Elites the stage held. At least 0. Defaulted: a boss stage and every existing caller pass none.</param>
    public int ForStageClear(int stage, bool bossStage, int elites = 0);
}
```

## Behaviour

1. **An Elite costs the ceiling of its archetype's cost times `CostMultiplier`.** Husk 10, Spitter
   **18**, Bloater 20, Lunger **23**, Weaver 30, Warden 35. Whole numbers, because the composer's
   spend is whole (AR §18.3's `int` row), and the ceiling rather than a rounding so an Elite is never
   cheaper than GD §8.3's 2.5×. Under Swarm the archetype's discounted cost is multiplied, so an
   Ordeal that cheapens Husks cheapens Elite Husks in proportion (M6-06b rule 4, one step on).
2. **From `FirstStage`, each purchase makes one extra draw — a `Chance(Elites.Chance)` — and makes it
   whether or not it is used.** The draw picks the archetype exactly as today; then the chance decides
   whether it is bought as an Elite, if the Elite price fits what the wave has left, and as itself
   otherwise. **One draw per purchase, always, from `FirstStage` on** — `IRandomStream.Chance`'s own
   rule, so a stream's position depends on how many bodies a stage bought and never on which of them
   happened to be affordable as Elites. Below `FirstStage` no draw is made at all, so every stage 1–7
   composes exactly as it did before this task.
3. **The draw is `Spawn`'s, not `Affixes`'.** Whether a body *is* an Elite is *what arrives* — the
   `Spawn` stream's whole remit (ADR-0011) and the only stream `WaveComposer` has ever drawn. The
   affix rolls on the `Affixes` stream are [M7-02c](../ROADMAP.md#m7--content-pass)'s and happen at the
   door, not here.
4. **A mode that authors no Elites composes byte-for-byte as before**, draws included. Every composer,
   plan, director and flow fixture authors none, so every row that predates this task runs unedited —
   except the one that pins the defect (rule 12).
5. **When the cap binds, surplus promotes rather than replaces.** `Promote` walks the wave's planned
   *plain* bodies cheapest-first, ties in roster order, and promotes one at a time while the
   promotion's extra cost — Elite price minus plain price — fits the allowance left. **No draw**, for
   `Upgrade`'s own reason: which bodies become Elites is forced by the budget, and a seed must not
   change a capped stage's difficulty (GD §11.2). **Cheapest-first**, because it spreads the upgrade
   across the mix — a Husk promotes for 6 and a Warden for 21, so a capped wave gains the most Elites
   the budget can buy rather than the fewest. **No archetype count changes**, by construction: a
   promotion edits `EliteCount`, never `Count`, which is rule 5's whole claim and row 11's whole fix.
6. **`Upgrade`, `CheapestPlanned` and `BestUpgrade` are deleted, not kept behind a flag.** A capped
   wave on a mode with no Elites leaves its surplus unspent, reported as it always was — which is
   `Compose_NoUpgradeAvailable_LeavesUnspent`'s existing claim, unchanged.
7. **The introduction is never an Elite, and neither is the stage's forced body.** Rule 5 of the
   composer buys GD §8.2's introduced archetype first in wave 1 with no draw, and it stays plain and
   drawless: the first time a player meets an archetype is not the moment to meet its 2.2× version.
   Rule 8's *"a stage is never empty"* body is plain for the same reason it is forced.
8. **The plan carries the Elites per entry and per stage, and the clear pays for them.**
   `WaveEntry.EliteCount` is written by `SetWave` from a parallel buffer; `WavePlan.EliteCount` is the
   stage's total, zeroed in `Begin` with every other field (AR §18.4's pooled-object rule).
   `StageFlow.EnterClear` pays `ForStageClear(Stage, boss, boss ? 0 : _plan.EliteCount)` — **15 per
   Elite on top of `20 + 4·n`**, GD §15, inside Famine's wrapper so a stacked Famine reduces the whole
   clear. **A boss stage pays no Elite term**: its plan is still composed, for the stream's sake, and
   none of its bodies is ever spawned. **Paid at the clear, never on the kill** — *"Essence drops
   from events, not bodies"* — which also keeps the boundary snapshot exact: every Elite the stage
   held is dead by the edge the snapshot is taken on.
9. **The director spawns the Elites the plan bought.** `StartWave` queues each entry's bodies
   round-robin as today, and marks the **last `EliteCount`** of each entry's bodies Elite — so a wave's
   Elites arrive after its plain bodies of the same archetype, which is the order a player can learn
   from. The flag rides the queue and the pending arrays beside the id, and `FireDueTelegraphs` calls
   `Spawn(spec, position, elite)`. **`SpawnTelegraphed` is unchanged**: whether a ring can say
   *"Elite"* is [M7-02e](../ROADMAP.md#m7--content-pass)'s question, and the event can grow then.
10. **Echo's figure moves, and the parking-lot line says so.** M6-11 recorded that from stage 26
    *"waves 3, 4 and 5 are the same composition"*, which made Echo-as-*replace* a no-op. With the mix
    kept, the three waves differ again — so replacing wave 4 with wave 3 removes 6.7 % of the budget
    as the triangular split first said. **Echo itself is not built here**; the line is re-aimed to
    M7-08's instrument, which re-measures per-wave composition after this lands.
11. **Row 11's instrument is a row.** `Compose_ACappedDeepWaveKeepsItsMix` composes stages 20–35 on a
    Descent-shaped fixture — the shipped roster to stage 11 and Descent's `EliteSpec` — from a real
    seeded stream, and asserts that **no capped wave is a single archetype** and that every capped
    wave's archetype counts are what the fill loop bought. It is the claim M6-11's log refuted, now
    checked on every run.
12. **The row that pinned the defect is rewritten, not deleted.**
    `Compose_CapBinding_SpendsOnQuality` asserts *"every Husk should have been upgraded into a
    Bloater"* — row 11 as a requirement. It becomes a mode that authors Elites, scripted to draw
    Husks, asserting that a capped wave's mean cost rises **and every body is still a Husk**. Its
    message names row 11, so the next reader knows which way the rule used to point.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Compose_AnEliteCostsItsCeiling` | a Spitter at 7 / elite price / 18, and a Husk's is 10 — rule 1 |
| `Compose_SwarmDiscountsElitesInProportion` | Swarm at 0.75 on Husks / elite price / `ceil(3 × 2.5)` = 8 — rule 1 |
| `Compose_BuysElitesFromTheFirstStage` | Descent's `EliteSpec`, a stream scripted to pass every chance / stage 8 / Elites bought; stage 7 / none — rule 2 |
| `Compose_OneDrawPerPurchaseFromTheFirstStage` | stage 8, a wave of *n* purchases / — / the stream advanced by exactly 2*n* whatever the chance answered — rule 2 |
| `Compose_BelowTheFirstStageDrawsAsBefore` | stages 1–7 / the same seed with and without an `EliteSpec` / identical plans and identical stream positions — rule 2 |
| `Compose_AnUnaffordableEliteIsBoughtPlain` | a passing chance with 12 left for a Spitter / — / a plain Spitter — rule 2 |
| `Compose_AModeWithoutElitesIsUnchanged` | a fixture with no `EliteSpec` / stages 1, 8, 20, 60 / plans identical to a recording taken before this task — rule 4 |
| `Compose_TheIntroductionIsNeverAnElite` | stage 8 introducing the Weaver, every chance passing / wave 1 / the introduced Weaver is plain — rule 7 |
| `Compose_ACappedWavePromotesCheapestFirst` | a capped wave of Husks and Spitters with 30 surplus / — / five Husks promoted (6 each), no Spitter — rule 5 |
| `Compose_PromotionChangesNoArchetype` | any capped wave / — / every entry's `Count` equals the fill loop's; only `EliteCount` moved — rule 5 |
| `Compose_PromotionDrawsNothing` | the same capped stage composed twice from streams at one position / — / identical plans, identical positions — rule 5 |
| `Compose_EveryBodyEliteLeavesTheRestUnspent` | a capped stage with more surplus than promoting every body costs / — / every body Elite, the remainder in `UnspentThreat`, spend + unspent = B(n) — rules 5, 6 |
| `Compose_ACappedDeepWaveKeepsItsMix` | the Descent-shaped fixture, a seeded stream / stages 20–35 / no capped wave holds one archetype — rule 11, **row 11's instrument** |
| `Compose_CapBinding_SpendsOnQuality` | *(rewritten)* Elites authored, draws scripted to Husks, stage 60 / — / mean cost above a Husk's, and every body still a Husk — rule 12 |
| `Plan_CarriesElitesPerEntryAndPerStage` | a composed stage / — / Σ `WaveEntry.EliteCount` equals `WavePlan.EliteCount` — rule 8 |
| `Plan_ForgetsTheLastStagesElites` | a plan reused from a stage with Elites to one without / — / `EliteCount` 0 — rule 8 |
| `Entry_RefusesMoreElitesThanBodies` | `new WaveEntry(husk, 2, 3)` / — / throws — rule 8 |
| `Director_SpawnsTheElitesThePlanBought` | an entry of 3 Husks, 1 Elite / the wave / three spawns, the third `IsElite` — rule 9 |
| `Clear_PaysFifteenPerElite` | a stage-10 plan holding 3 Elites / the clear / 20 + 40 + 45 = 105 — rule 8 |
| `Clear_FamineReducesTheWholeClear` | the same under one Famine / — / `round(105 × 0.6)` = 63 — rule 8 |
| `Clear_ABossStagePaysNoElites` | a boss stage whose composed plan holds Elites / the clear / 20 + 4·n + 60, no Elite term — rule 8 |
| `Essence_RefusesNegativeElites` | `ForStageClear(5, false, -1)` / — / throws |

**Guard rows are implied, not listed.**

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 8. *Expected: now and then a body with a bar that never fades and takes
   twice as long to kill; the clear pays 15 more for each one — the Essence float-up says so.*
2. **Not an Editor step, deliberately.** Row 11 lives past stage 21, a run of twenty minutes, and the
   overlay has no composition readout. `Compose_ACappedDeepWaveKeepsItsMix` is the evidence here;
   **M7-08's instrument** — M6-11's per-wave composition probe, rebuilt — is what witnesses it in
   Play, which is the rule M5-08 set: *a row that wants a number is discharged by an instrument.*

## Out of scope

- **Affixes, and the second one that spends what is left past stage 40.** [M7-02c](../ROADMAP.md#m7--content-pass).
- **Building Echo.** Rule 10; the parking-lot line is re-aimed, not discharged.
- **An Elite telegraph ring.** Rule 9; M7-02e.
- **Retuning `Chance`.** 0.1 is ours, and M7-08's instrument counts Elites per stage against GD §12.1's
  *"first Elites … several Elites"* — the number a tuning pass would start from.
