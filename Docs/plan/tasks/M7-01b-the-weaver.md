# M7-01b — The Weaver, and a stage that waits for its children

**Size:** S · **Depends on:** M7-01a · **Branch:** `m7-01b-the-weaver`
**Design refs:** GD §8.1, §8.2, §11.1, §12.2, §12.4, §15; AR §18.1, §18.3, §18.4 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — two bodies added to its instrument; M6 row 5's rule, extended from boss stages to every stage (rule 6)

## Goal

GD §8.1's fifth archetype exists — a body that becomes two when it dies — and a stage is not over
while either of them is standing.

## The reading of GD §8.1, ruled at M7-00a

*"On death splits into 2 smaller Weavers (2 generations)"* is read as **two generations in all: the
Weaver and its two children, who do not split again** — three bodies from one purchase, not seven.
The document is ours and the sentence admits both; three reasons choose:

- **GD §11.1's cap cannot see a split.** Children are born outside `WaveComposer`'s body count, so
  every one is a body over what the device tier was measured at. At 1 → 2 a death adds **one**; at
  1 → 2 → 4 a single Weaver is **seven**, and a wave of four is twenty-eight bodies the composer never
  counted — the whole mid-tier cap from one archetype.
- **P1's readability.** Seven bodies from one tell is a swarm the player did not see coming.
- **It is one number.** Rule 2 makes the split a data block; a playtest that wants more attrition
  authors a child that splits, and rule 4 is the one line that would have to move.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SplitSpec.cs` | Core | **New.** What a body becomes when it dies: an archetype and a count |
| `Tests/Core/Ai/WeaverSplitTests.cs` | Tests.Core | **New.** Every rule below |
| `Core/Ai/EnemySystem.cs` | Core | `Split(ReadOnlySpan<EnemyDeath>)` — the children stand up where the corpse fell (rules 3–5) |
| *small edits* | Core, Game, Data | `Core/Content/EnemySpec.cs` — a `split` block optional and last (rule 2); `Core/Run/RunSession.cs` — one call beside `Rise.OnDeaths` (rule 3) and one walk in `RequireAuthored` (rule 4); `Core/Director/SpawnDirector.cs` — `IsStageComplete` asks the census on an ordinary stage too (rule 6); `Game/Authoring/EnemyDefinition.cs` — two fields under a *Split* header; `Data/Enemies/Weaver.asset` and `Data/Enemies/Weaverling.asset` — **new**, rule 8's numbers; `Data/Modes/Descent.asset` — the Weaver at stage 8; `Prefabs/Composition/BootScope.prefab` — `_enemies` gains both; `Data/Localisation/English.asset` — two names; `Data/Localisation/Pseudo.asset` — regenerated |
| *ripple* | Tests.Core, Tests.Game | `EnemyDefinitionTests.Enemy_ShippedXpIsThreeTimesCost` becomes family-aware (rule 7); the M7-01a pins count five; `SpawnDirectorTests` rows that complete a stage run with a quiet registry and are unchanged (rule 6) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// What a body becomes when it dies: <see cref="Count"/> of <see cref="Into"/>, standing where it
/// fell. <see cref="ExplosionSpec"/>'s shape — resolved off the spec, never off the kind.
/// </summary>
public sealed class SplitSpec
{
    /// <param name="into">The archetype the children are. Must itself carry no split (rule 4).</param>
    /// <param name="count">How many. At least 1. 2 for the Weaver (GD §8.1).</param>
    /// <exception cref="ArgumentException"><paramref name="into"/> is <c>default(ContentId)</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    public SplitSpec(ContentId into, int count);

    public ContentId Into { get; }
    public int Count { get; }
}

public sealed class EnemySpec
{
    // ... then, after M7-01a's lunge, defaulted and last:
    //     SplitSpec split = null

    /// <summary>What it becomes when it dies, or <see langword="null"/> on an archetype that does not split.</summary>
    public SplitSpec Split { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemySystem
{
    /// <summary>Metres from the corpse each child stands, on a ring. 0.6 — rule 5.</summary>
    public const float SplitSpacing = 0.6f;

    /// <summary>
    /// Stands up the children of every death in <paramref name="deaths"/> whose archetype splits.
    /// Called once a tick by <c>RunSession</c>, on the line that offers the same deaths to Rise.
    /// </summary>
    public void Split(ReadOnlySpan<EnemyDeath> deaths);
}
```

## Behaviour

1. **A Weaver is a Chaser that carries a split block, and it needs no behaviour of its own.** GD
   §8.1's *"attrition"* is what it does when it *dies*; alive it walks in and strikes like a Husk.
   So there is no `EnemyBehaviourKind.Weaver`: the asset authors `Chaser`, and the split is a
   property of the spec resolved by the census — **AR §18.1's explosion row, one block over**: *"an
   explosion is triggered by the spec carrying an `ExplosionSpec`, never by the behaviour kind"*, so
   *"splits on death"* is true however it died, and true of anything that ever authors the block.
2. **`SplitSpec` is optional and last on `EnemySpec`**, after M7-01a's `lunge`, for rule 2 of that
   task and the same 62 sites.
3. **The split is pulled from the death drain, never pushed from `ApplyDamage`.** `RunSession.Tick`
   already drains the tick's deaths into a buffer it owns and offers them to Rise on one line (AR
   §18.1); `State.Enemies.Split(...)` is offered the same span on the next. **Not inside the kill
   branch**, for three reasons that each hold alone: `ApplyDamage` is reached from `ReportConeHits`
   *between* ticks (ADR-0003), where spawning would put a body into the arena in the middle of the
   fact phase; it is reached from inside three span walks (`LandOnEnemies`, `ZoneSystem.Burn`, the
   behaviour pass), and a spawn inside one is safe only by an argument about append order nobody
   should have to re-make; and core does not act on its own events. The cost: a Weaver killed between
   ticks splits on the next tick, one frame late — the lag every fact already has.
4. **A child may not split, and a missing child is refused before the run is announced.**
   `RunSession.RequireAuthored` already walks the roster and the boss bodies so an unauthored
   archetype is loud at `Start` rather than forty seconds in; it now takes one step down each
   rostered archetype's split, through the same `RequireArchetype`, and refuses a child that itself
   carries a split — naming both ids. That is the whole enforcement of the ruling above, and it is
   the line a playtest that wants 1 → 2 → 4 deletes.
5. **The children stand on a ring around the corpse, placed without a draw.** Child *k* of *n* at
   `SplitSpacing` from `EnemyDeath.Position`, at angle `2πk / n` on XZ — two children stand either
   side of where the Weaver fell. **No draw**, because a position chosen from a stream would make a
   seeded run's later spawns depend on how many Weavers died (AR §18.3's one-draw rule, applied by
   not drawing at all). Each is spawned through `EnemySystem.Spawn`, so depth, the Veilrot bonus and
   `EnemySpawned` all reach it exactly as they reach a composed body. **Never an Elite**, whatever the
   parent was — [M7-02a](M7-02a-what-an-elite-is.md) rule 7 inherits this line. **A full registry
   stops the split rather than throwing**: `EnemyRegistry` holds 64 against a device cap of 28, so
   this is unreachable while the director keeps the cap, and a crash from a bookkeeping limit mid-run
   is the worse failure.
6. **A stage is complete when its waves are cleared *and* nothing is breathing** — M6-02a rule 5,
   which made that true of boss stages for their adds, made true of every stage. The director tracks
   the ids *it* spawned; a child is not one of them, so without this the last composed body dying
   ends the stage with two Weaverlings standing beside the player for the whole of an untimed
   Sanctum — ledger row 5's failure exactly, one door over. `IsStageComplete` asks
   `AnythingBreathes()` after the wave check passes, so the walk runs only on the ticks a stage is
   about to end. **The overlap rule is untouched**: wave *n + 1* still opens when a quarter of wave
   *n*'s *tracked* bodies stand, so children are pressure that carries into the next wave rather
   than a reason to hold it — which is what attrition is.
7. **A Weaver's family is worth three times its cost, split between them.** `EnemySpec.XpValue`'s
   convention — a stage's experience is `3 · B(n)` whatever the seed drew — holds *per purchase*, so
   the parent pays **18** and each child **9**: 18 + 2 × 9 = 36 = 3 × 12.
   `Enemy_ShippedXpIsThreeTimesCost` walks families rather than files, and a child, which is on no
   roster, is checked as part of its parent's sum and not alone.
8. **The two assets.** GD §8.1 publishes the Weaver's HP, cost and priority; everything else is ours.

   | Field | Weaver | Weaverling | Why |
   |---|---|---|---|
   | `_maxHp` | **50** | **20** | GD §8.1; a child is two Censer hits |
   | `_threatCost` | **12** | **1** | GD §8.1; a child is never composed — 1 is `EnemySpec`'s floor and is read by nothing |
   | `_targetPriority` | **2** | **2** | GD §8.1, both |
   | `_xpValue` | **18** | **9** | rule 7 |
   | `_moveSpeed` | **2.0** | **2.8** | both under the slowest class's 3.0 |
   | `_contactDamage` | **9** | **5** | inside the Husk–Bloater envelope (M7-01a rule 10) |
   | `_reach` / `_windupTime` / `_recoverTime` | **1.3 / 0.5 / 0.7** | **1.0 / 0.4 / 0.5** | a heavier Husk; a quicker, weaker one |
   | `_behaviour` | **Chaser** | **Chaser** | rule 1 |
   | `_splitInto` / `_splitCount` | **`enemy.weaverling`** / **2** | — | the ruling |
   | `_tint` / `_bodyScale` | **(0.74, 0.70, 0.58)** / **1.1** | the same / **0.7** | a pale sand, and the family reads as one colour at two sizes; M7-01d owns the look |

9. **Nothing here allocates on a tick.** `Split` walks a span it is handed, reads each dead
   archetype through the catalog once, and spawns through the door every spawn uses.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Weaver_DeathStandsUpTwoChildren` | a Weaver killed / the tick's drain / two `enemy.weaverling` registered, two `EnemySpawned` — rules 1, 3 |
| `Weaver_ChildrenStandEitherSideOfTheCorpse` | a Weaver dead at (4, 0, 7) / split / children at (4.6, 0, 7) and (3.4, 0, 7) — rule 5 |
| `Weaver_ChildrenAreScaledToTheDepth` | depth 20 / a split / each child's max HP is 20 × h(20) — rule 5 |
| `Weaver_AChildDoesNotSplit` | a Weaverling killed / the drain / nothing spawned — the ruling |
| `Weaver_SplitsHoweverItDied` | killed by a cone, a bolt, a burn, a Wight, and a Bloater's blast / — / two children every time — rule 1 |
| `Weaver_KilledBetweenTicksSplitsOnTheNext` | a kill through `ReportConeHits` / the next tick / the children appear then, not during the report — rule 3 |
| `Weaver_SplittingDrawsNothing` | two runs from one seed, one killing a Weaver / — / the `Spawn` stream's position is identical afterwards — rule 5 |
| `Weaver_AFullRegistryStopsTheSplit` | a registry one short of capacity / a Weaver dies / one child, no throw — rule 5 |
| `Weaver_ChildrenAreNeverElite` | an Elite-shaped parent (a spec authored `isElite`) / split / both children `IsElite` false — rule 5 |
| `Stage_WaitsForTheChildren` | the last composed body is a Weaver / it dies / `IsStageComplete` false until both children are dead — rule 6 |
| `Stage_TheOverlapDoesNotWaitForChildren` | wave 1 down to its last tracked body, two children standing / — / wave 2 opens on the tracked count alone — rule 6 |
| `Stage_AQuietRegistryCompletesAsBefore` | every existing completion scenario with no split in it / — / completes on the same tick it did — rule 6 |
| `Start_RefusesAChildNobodyAuthored` | a rostered archetype splitting into an absent id / `Start` / `KeyNotFoundException` naming both, nothing announced — rule 4 |
| `Start_RefusesAChildThatSplits` | a child carrying its own split / `Start` / refused, naming both — rule 4 |
| `Spec_TheSplitBlockIsOptionalAndLast` | the constructor as 62 sites call it / — / `Split` null — rule 2 |
| `Split_AllocatesNothing` | 10 000 drains of an empty span and of a span with no splitter / `AllocationAssert.None` / zero — rule 9 |
| `Weaver_AssetsCarryTheNumbers` | both assets / converted / rule 8's table |
| `Weaver_TheFamilyIsWorthThreeTimesItsCost` | both assets / — / 18 + 2 × 9 = 3 × 12 — rule 7 |
| `Descent_RostersTheWeaverAtEight` | `Descent.asset` / — / `enemy.weaver` at 8, `enemy.weaverling` on no roster — GD §8.2 |

**Guard rows are implied, not listed:** `SplitSpec`'s default id and zero count.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 8. *Expected: one Weaver in wave 1; killing it leaves two smaller,
   quicker copies where it fell.*
2. **[Editor]** Kill every composed body in the last wave but leave a Weaverling standing. *Expected:
   no gate and no Sanctum until it dies — rule 6.*
3. **[Editor]** As the Gravecaller, kill a Weaver with Rise ready. *Expected: two Weaverlings and a
   Wight all stand up from the one corpse.*

## Out of scope

- **A child that splits, or a third generation.** The ruling; one line in rule 4.
- **The children's look beyond a scale.** [M7-01d](M7-01d-three-archetypes-a-player-can-read.md).
- **Any change to how the composer prices a Weaver.** Its cost is GD §8.1's 12 and the children are
  free; whether a family that fills the arena is worth more is ledger row 2's instrument, M8-05.
