# M6-06a — What an Ordeal is, the loop that deals one, and the sixth stream that does not exist

**Size:** M · **Depends on:** M6-02b, M6-04 · **Branch:** `m6-06a-ordeal-draw`
**Design refs:** GD §3, §7.1, §12.1, §12.2, §13.4, §19; AR §10.1, §13, §18.1, §18.2, §18.3; ADR-0006, ADR-0011 · **Ledger rows:** [7](../ROADMAP.md#carry-forward-into-m6) — this task's share of M6's strings

## Goal

From the depth a mode says, every loop of its biomes deals a permanent modifier the run keeps and the
save remembers — and after this task not one of them does anything.

## Why the half that does nothing ships first

[M6-01a](M6-01a-essence-wallet-and-drops.md)'s bargain, third time: a wallet nothing spends, a phase
nothing sells in, and now a set of modifiers nothing reads. It buys the same thing each time — this
PR's review is about **when an Ordeal is dealt and what a save does with it**, and
[M6-06b](M6-06b-four-ordeals-and-two-refusals.md)'s is about what four of them *do* to four different
systems. Counted, the two together are `OrdealSpec`, `Ordeals`, `OrdealDefinition`, two fixtures and
edits to `StageFlow`, `LevelUpFlow`, `Veilrot`, `WaveComposer` and `RunRecorder`, which is a task
nobody can review in one sitting. It is also [M6-01b](M6-01b-save-format-v4.md)'s third placeholder
filled: `RunSnapshot.OrdealIds` has had a field and no writer since v4 was cut.

## The stream, decided here because [M6-01b](M6-01b-save-format-v4.md) refused to open a sixth

That task ruled `RandomState` keeps its five, *"because a sixth member would be a v5"*, and named
`Affixes` the candidate with `Spawn` the alternative. **Grepped, `Affixes` is drawn by nothing at
all**: `WaveComposer` and `SpawnDirector` draw `Spawn`, `OfferGenerator` draws `Offers`,
`RisePassive` draws `Drops`, `WardenBehaviour` draws `Misc`, and `IRandom.Affixes` has no reader in
the project. So an Ordeal draw on it shifts nothing that exists, and an Ordeal is the same kind of
thing as an affix — *a modifier on what you fight*.

**The cost is stated rather than waved at.** **M7-02**'s Elites will be that stream's second drawer,
and every seed's affix rolls from that milestone onward will be offset by however many Ordeals the
run has taken by then. That is cheap **now** — no seed has ever been replayed against an affix,
because none exists — and it is the cheapest it will ever be. `Spawn` stays refused for the reason
M6-01b gave and `WaveComposer`'s own remarks repeat: *"an offer reroll or a drop must never shift
what a stage is made of"*, and an Ordeal draw on `Spawn` would make a stage's composition depend on
how many Ordeals the run had been dealt. `Misc` is refused too, quietly until now: `WardenBehaviour`
draws it, so an Ordeal would shift a boss's jitter, and GD §13.4's Ordeals are not cosmetic.

## What GD §13.4 asks for, and what a mode can say

GD §13.4 is *"from stage 25, each new biome loop"*; GD §3 is *"biome changes every 10 stages"*. There
is no biome in the build — `ModeSpec` holds arenas and a roster and knows nothing about layers — so
the schedule is **two authored numbers**, `firstStage` and `everyNStages`, and `Descent.asset`
carries 25 and 10. That gives Ordeals at stages **25, 35, 45, 55…**, which is GD §13.4 read against
GD §3 with the arithmetic written down instead of inferred. A mode that authors no schedule deals
none, which is every mode but Descent and every mode M6 does not ship.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/OrdealSpec.cs` | Core | One Ordeal as authored data: what it is called, and which of five dials it turns |
| `Core/Run/Ordeals.cs` | Core | What this run has been dealt, the draw at the boundary, and the five accumulated answers |
| `Game/Authoring/OrdealDefinition.cs` | Game | The Inspector half, and `Data/Ordeals/` |
| `Tests/Core/Run/OrdealsTests.cs` | Tests.Core | The spec's guards, the schedule, the draw, the stacking rules and the restore |
| *small edits* | Core, Game | `Core/Content/ModeSpec.cs` — `OrdealScheduleSpec` and the pool, optional and last (rule 1); `Game/Authoring/ModeDefinition.cs` — an `Ordeals` foldout; `Data/Modes/Descent.asset` — 25 / 10 and four entries; `Data/Ordeals/*.asset` — four; `Core/Events/OrdealEvents.cs` — `OrdealApplied`; `Core/Stage/StageFlow.cs` — a required `Ordeals` and one call in `Advance` (rule 5); `Core/Run/RunState.cs` — `OrdealIds` forwards; `Core/Run/RunSession.cs` — builds it, restores it below the tree; `Core/Save/RunRecorder.cs` — `Take` passes the set |
| *ripple* | Tests.Core, Tests.Game | **11 `new StageFlow(...)` sites across 2 files** — compiler-guided, [M6-01a](M6-01a-essence-wallet-and-drops.md)'s wallet again; `ModeSpecTests`, `ModeDefinitionTests` and `ContentValidationTests` gain the block; `SaveDtoTests`' Ordeal rows stop being about a field nothing writes |

The spec's own guards share `OrdealsTests` rather than taking a fixture of their own: `OrdealSpec` is
an immutable block of five dials with no behaviour, and splitting one mechanism across two files
would put the schedule's rules and the numbers they read in different places.

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// One of GD §13.4's deep-run modifiers, as authored data (ADR-0006): a name, a description, and
/// whichever of five dials it turns.
/// </summary>
/// <remarks>
/// <b>Five named fields and no kind enum</b>, which is rule 2. A <c>switch (ordeal.Kind)</c> at four
/// call sites is ADR-0009's ban arriving under a different name; five neutral dials mean each
/// consumer reads one number and nothing anywhere dispatches. <see cref="ScalingSpec"/>'s shape.
/// </remarks>
public sealed class OrdealSpec
{
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/>, <paramref name="nameKey"/> or <paramref name="descriptionKey"/> is a
    /// default; <paramref name="threatCostTarget"/> is named without a multiplier or the reverse;
    /// or every dial is neutral, which is an Ordeal that does nothing (rule 3).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A multiplier is not finite and above zero; <paramref name="offerCount"/> is negative;
    /// <paramref name="concurrencyBonus"/> is negative.
    /// </exception>
    public OrdealSpec(
        ContentId id,
        LocKey nameKey,
        LocKey descriptionKey,
        float essenceMultiplier = 1f,
        int offerCount = 0,
        int concurrencyBonus = 0,
        float veilrotMultiplier = 1f,
        ContentId threatCostTarget = default,
        float threatCostMultiplier = 1f);

    public ContentId Id { get; }
    public LocKey NameKey { get; }
    public LocKey DescriptionKey { get; }

    /// <summary>What a stage clear pays, times this. Famine's 0.6 — GD §13.4.</summary>
    public float EssenceMultiplier { get; }

    /// <summary>How many nodes a level-up offers, or 0 for "unchanged". Vigil's 2.</summary>
    public int OfferCount { get; }

    /// <summary>What to add to GD §12.2's concurrency cap. Swarm's 8.</summary>
    public int ConcurrencyBonus { get; }

    /// <summary>What a Veilrot gain is multiplied by. Hunger's 1.5.</summary>
    public float VeilrotMultiplier { get; }

    /// <summary>Whose threat cost moves, or <c>default</c>. Swarm names the Husk.</summary>
    public ContentId ThreatCostTarget { get; }

    /// <summary>And by how much. Swarm's 0.5.</summary>
    public float ThreatCostMultiplier { get; }
}

/// <summary>When a mode starts dealing Ordeals, and how often. GD §13.4 against GD §3.</summary>
public readonly struct OrdealScheduleSpec
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="firstStage"/> is below 1, or <paramref name="everyNStages"/> is below 1.
    /// </exception>
    public OrdealScheduleSpec(int firstStage, int everyNStages);

    public int FirstStage { get; }   // 25 in Descent
    public int EveryNStages { get; } // 10 in Descent

    /// <summary>Whether <paramref name="stage"/> is one this schedule deals on.</summary>
    public bool DealsAt(int stage);
}

public sealed class ModeSpec
{
    // ... M6-01a's `essence` and M6-02b's `sanctum`, then, optional and last (rule 1):
    //     OrdealScheduleSpec ordealSchedule = default
    //     IReadOnlyList<OrdealSpec> ordeals = null

    /// <summary>All zeroes for a mode that deals none — rule 1.</summary>
    public OrdealScheduleSpec OrdealSchedule { get; }

    /// <summary>The pool, in authored order. Empty is ordinary.</summary>
    public IReadOnlyList<OrdealSpec> Ordeals { get; }
}
```

```csharp
namespace Soulvail.Core.Run;

/// <summary>
/// GD §13.4's Ordeals, for one run: which have been dealt, and what they add up to.
/// </summary>
public sealed class Ordeals
{
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public Ordeals(ModeSpec mode, IDomainEvents events);

    /// <summary>What has been dealt, in the order it was — what the save carries.</summary>
    public IReadOnlyList<ContentId> Applied { get; }

    /// <summary>How many are still in the pool. Zero is ordinary and means the run is done (rule 4).</summary>
    public int Remaining { get; }

    /// <summary>
    /// Deals one if <paramref name="stage"/> is a boundary the mode schedules and the pool is not
    /// empty. Called once per stage entered, above the composition — rule 5.
    /// </summary>
    /// <param name="affixes">The run's <see cref="IRandom.Affixes"/> stream and no other.</param>
    /// <exception cref="ArgumentNullException"><paramref name="affixes"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public void OnStageEntered(int stage, IRandomStream affixes);

    // ---- The five accumulated answers. Read by nothing until M6-06b (rule 6). ----

    /// <summary>Product of every dealt Ordeal's. 1 for a run with none — rule 3.</summary>
    public float EssenceMultiplier { get; }

    /// <summary>The smallest non-zero one dealt, or 0 for "unchanged" — rule 3.</summary>
    public int OfferCount { get; }

    /// <summary>Sum of every dealt Ordeal's.</summary>
    public int ConcurrencyBonus { get; }

    /// <summary>Product.</summary>
    public float VeilrotMultiplier { get; }

    /// <summary>Product over the Ordeals naming <paramref name="enemyId"/>. 1 for the rest.</summary>
    public float ThreatCostMultiplier(ContentId enemyId);

    /// <summary>What a resumed run comes back holding. Silent — rule 7.</summary>
    internal void Restore(IReadOnlyList<ContentId> applied);
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>One more Ordeal is in force. Carries the depth, because GD §13.4's whole point is how deep.</summary>
public readonly struct OrdealApplied
{
    public OrdealApplied(ContentId id, int stage, int count);

    public readonly ContentId Id;
    public readonly int Stage;

    /// <summary>How many are now in force, this one included.</summary>
    public readonly int Count;
}
```

## Behaviour

1. **The schedule and the pool are authored on the mode, optional and last, and an unscheduled mode
   deals none.** `new ModeSpec(...)` has **63 call sites across 44 files**, so this is the third
   optional-and-last block of the milestone and costs the same as the other two: nothing.
   **The pool is the mode's rather than the catalog's**, which is `WaveComposer`'s own rule — *"the
   vocabulary is the mode's, never the catalog's… which is what makes a Boss Rush or a Trial a data
   change rather than a branch"* — and it is also what keeps `ContentCatalog`'s six lists at six.
   `ContentValidationTests.EveryShippedMode_SchedulesWhatItStocks` refuses the two halves
   disagreeing: a schedule with an empty pool deals nothing for ever, and a pool with no schedule is
   content nobody can reach.
2. **Five named dials, never a kind enum.** GD §13.4's six Ordeals turn four different systems, and
   the shape that reads them with a `switch` on a kind is AR §13's ban with the word *effect* removed
   — the same argument `EffectDefinition`'s remarks make about a flat union struct. Five neutral
   fields mean a consumer asks for the one number it cares about and no code anywhere asks *which
   Ordeal is this*. The cost is that a shipped Ordeal leaves four dials at their neutral value, which
   is what `ScalingSpec` and `OverflowSpec` already look like, and the gain is that
   [M6-06b](M6-06b-four-ordeals-and-two-refusals.md) is four one-line reads instead of four
   dispatches.
3. **A neutral Ordeal is refused, and each accumulation rule is written for a pool that can exist
   rather than the one that does.** An `OrdealSpec` with all five dials neutral is a row in the table
   that does nothing, which is M2-06 rule 11's silence refused at the authoring door.
   `EssenceMultiplier` and `VeilrotMultiplier` are **products**; `ConcurrencyBonus` is a **sum**;
   `ThreatCostMultiplier` is a product over the Ordeals naming that id; and `OfferCount` is the
   **smallest non-zero** — because two Ordeals that each shrink an offer must not compose to one
   card, and `OfferGenerator.Draw` throws for a `count` below 1 anyway. **Nothing stacks with itself
   in V1**, because rule 4 draws without replacement, so every one of these rules is about content
   M7 might add; they are written now because the alternative is four guesses made in four files by
   whoever adds the fifth Ordeal.
4. **The draw is without replacement, and an exhausted pool is silent.** GD §13.4 says *"permanent
   stacking"* and lists six distinct rows; dealing Famine twice would be −64 % income from one table
   entry, which is a different design. So `OnStageEntered` picks uniformly from what is left — one
   `NextInt` on `Affixes`, and **no draw at all when the pool is empty or the stage is not a
   boundary**, which is `OfferGenerator`'s consumption rule applied here: consumption is a function
   of how many boundaries with stock the run has crossed, and never of what was drawn. With four
   Ordeals shipping and Descent's 25/10, the pool empties after stage 55 and every boundary after
   that is silent.
5. **It is dealt in `StageFlow.Advance`, above `_composer.Compose`, and that ordering is the whole
   reason it is not somewhere tidier.** Swarm changes what a stage is made of
   ([M6-06b](M6-06b-four-ordeals-and-two-refusals.md)), and `Advance` composes the next stage's plan
   in the same method — so an Ordeal dealt after the composition would be invisible for the stage it
   arrived on and the player would meet it a stage late, once, with nothing to say why.
   `StageFlow`'s `Ordeals` is a **required** constructor argument for
   [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 5's reason and at its price, 11 sites over.
   A run's **first** stage is composed in `RunSession.Start` instead, and needs no draw: Descent's
   first boundary is 25 and a resumed run restores rather than deals (rule 7).
6. **Every accumulator ships with no reader, and that is the task.** Nothing in `Soulvail.Core` asks
   `Ordeals` for a number after this PR. `Ordeals_NothingReadsTheDialsYet` sweeps the assembly for
   callers the way `PaletteTests` sweeps for colour readers, so the day one appears it is
   [M6-06b](M6-06b-four-ordeals-and-two-refusals.md)'s diff rather than a surprise.
7. **Restoring is silent and happens below the tree's restore.** `EssenceWallet.Restore`'s rule
   ([M6-01a](M6-01a-essence-wallet-and-drops.md) rule 8): a resume is not news, and an
   `OrdealApplied` published inside `RunSession.Start` would reach a HUD that has not subscribed. It
   drops an id the mode's pool no longer holds, silently — `SkillRunner.Restore`'s rule, and the
   symptom of refusing instead would be a resume that fails because a designer renamed an asset. **A
   v4 save written before this task restores empty and the next boundary deals one**, which is
   recorded here rather than discovered: `RunSnapshot.OrdealIds` has been `Array.Empty` since M6-01b.
8. **Nothing here allocates on a tick.** The five accumulators are fields recomputed when one is
   dealt — at most four times a run — a `List<ContentId>` that grows at most once per deal, and a
   `bool[]` sized with the pool marking what is gone. `ThreatCostMultiplier` walks the dealt list,
   which is at most four entries and is called once per archetype per composition, not per frame.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_CarriesItsDials` | every field set / — / all read back |
| `Spec_HasNoKindAndNothingDispatchesOnOne` | `typeof(OrdealSpec)` / reflection / no `enum` member, and `Ordeals` contains no `switch` over one — rule 2 |
| `Spec_RefusesANeutralOrdeal` | every dial at its default / — / throws, naming the id — rule 3 |
| `Spec_RefusesAHalfNamedThreatTarget` | a target with no multiplier, then a multiplier with no target / — / throws either way |
| `Spec_RefusesANonPositiveOrNonFiniteMultiplier` | 0, −1, NaN, +∞ on each of the three / — / throws in each case |
| `Spec_RefusesANegativeOfferCountOrBonus` | −1 on each / — / throws; 0 is legal and means *unchanged* |
| `Schedule_DealsOnItsOwnStages` | 25 / 10 / `DealsAt` true at 25, 35, 45; false at 24, 26, 30, 34 |
| `Schedule_RefusesAZeroPeriod` | `(25, 0)` / — / throws — a period of zero deals on every stage and none |
| `Schedule_DefaultDealsNever` | `default(OrdealScheduleSpec)` / `DealsAt(25)` / false, no throw — rule 1 |
| `Mode_CarriesItsPoolAndSchedule` | `Descent.asset` converted / — / 25, 10 and four entries in authored order |
| `Mode_WithoutOneIsUnchanged` | a `ModeSpec` built without them / — / empty pool, `default` schedule, every other property what it was — rule 1 |
| `Content_EveryShippedModeSchedulesWhatItStocks` | every `ModeDefinition` under `Data/` / converted / a schedule and a pool, or neither — rule 1 |
| `Deal_NothingBeforeTheFirstStage` | a run ticked to stage 24 / — / `Applied` empty, **no draw on `Affixes`**, nothing published — rule 4 |
| `Deal_OneAtTheFirstBoundary` | entering stage 25 / — / one id, one `OrdealApplied(id, 25, 1)`, one draw |
| `Deal_OneEveryPeriod` | a run to stage 55 / — / four dealt at 25, 35, 45, 55, `Count` 1…4 |
| `Deal_NeverTheSameTwice` | the same run / — / four distinct ids — rule 4 |
| `Deal_AnEmptyPoolIsSilent` | entering stage 65 with four dealt / — / nothing published and **no draw** — rule 4 |
| `Deal_DrawsOnAffixesAndNothingElse` | a counting `IRandom` / a run to stage 45 / three draws on `Affixes`, zero on the other four — the ruling, asserted |
| `Deal_ConsumptionDoesNotDependOnWhatWasDrawn` | two seeds with the same boundary count / — / the same number of draws — rule 4 |
| `Deal_HappensBeforeTheComposition` | a spy composer / advancing into 25 / `OnStageEntered` ran above `Compose` — rule 5 |
| `Stage_RefusesANullSet` | `new StageFlow(..., ordeals: null)` / — / `ArgumentNullException` — rule 5 |
| `Stack_EssenceAndVeilrotMultiply` | two Ordeals at 0.6 and 0.5 restored / — / `EssenceMultiplier` 0.3 — rule 3 |
| `Stack_ConcurrencySums` | 8 and 4 / — / 12 |
| `Stack_OfferCountTakesTheSmallest` | 2 and 1, plus one that leaves it alone / — / 1 — rule 3 |
| `Stack_ThreatCostMultipliesPerTarget` | two naming the Husk at 0.5, one naming the Spitter / — / 0.25 for the Husk, that one's for the Spitter, **1** for the Bloater |
| `Stack_ARunWithNoneIsNeutral` | nothing dealt / — / 1, 0, 0, 1, and 1 for every id — rule 3 |
| `Ordeals_NothingReadsTheDialsYet` | `typeof(Ordeals)`'s five members / a sweep of `Soulvail.Core` / no caller but the tests — rule 6 |
| `Restore_ComesBackSilently` | a save naming two / resumed / both in force, in order, **nothing published** — rule 7 |
| `Restore_DropsOneThisBuildNoLongerStocks` | a save naming `ordeal.deleted` / resumed / dropped, no throw, the other kept — rule 7 |
| `Restore_AnOlderSaveComesBackEmpty` | a v4 snapshot written before this task, at stage 30 / resumed / empty, and entering 35 deals one — rule 7's stated cost |
| `Recorder_WritesWhatWasDealt` | a run at stage 45 / `Take` / `OrdealIds` holds three, in order — [M6-01b](M6-01b-save-format-v4.md) rule 7's second placeholder replaced |
| `State_OrdealIdsForwards` | a live run / — / `RunState.OrdealIds` tracks `Applied`, and `RunState` exposes no `Ordeals` — AR §18.2 |
| `Deal_AllocatesNothing` | 100 000 boundary calls on an empty pool and 100 000 dial reads / `AllocationAssert.None` / zero — rule 8 |

**Guard rows are implied, not listed:** nulls to both constructors, a stage below 1, and
`Enum.IsDefined` where there is an enum — there is not one here, which is rule 2's other half.

## Manual verification (Editor / device)

1. **[Editor]** With a debug command that jumps the run to stage 24, walk through the door. The
   overlay names one Ordeal on arrival at 25 and nothing else changes — no wave is different, no
   price moves, no offer shrinks. That is rule 6, looked at.
2. **[Editor]** Keep going to 55. Four distinct names accumulate; stage 65 adds nothing.
3. **[Editor]** Quit at stage 40 and press `Continue`. The same names are in force and nothing is
   announced. Delete `run.json`'s `ordealIds` array by hand and `Continue` again: the run comes back
   with none and picks one up at the next boundary — rule 7's stated cost.

## Out of scope

- **Any Ordeal doing anything.** [M6-06b](M6-06b-four-ordeals-and-two-refusals.md), which also
  refuses two of GD §13.4's six in writing.
- **Biomes.** GD §3's five layers are art and arena content (**M7-05/06**); this task turns *"each
  new biome loop"* into two authored numbers and says so.
- **A sixth random stream.** [M6-01b](M6-01b-save-format-v4.md)'s ruling, and the reason this task
  had a choice to make at all.
- **Removing an Ordeal.** GD §13.4 says *permanent*. There is no `Cleanse` for these.
- **A per-stage authored Ordeal.** The [parking-lot line](../ROADMAP.md#parking-lot) on a stage
  editor names M6-04 as a possible promoter *"if Ordeals need per-stage authoring"* — **they do
  not**: the schedule is a period and the pool is a mode's list, so nothing here wants a specific
  stage authored. That half of the line is answered and the rest stays.
- **Showing them on a screen.** The debug overlay names them; a player-facing readout is
  **M6-11**'s call against six screens of strings ([ledger row 7](../ROADMAP.md#carry-forward-into-m6)).

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
