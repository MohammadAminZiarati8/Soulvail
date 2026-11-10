# SS-07a — The brood in core: a swelling corpse that drops three scorplings

**Size:** M · **Depends on:** — · **Branch:** `ss-07a-brood-core`
**Design refs:** GD §8.7, §8.1, §12.2

## Goal

A `BroodSpec` enemy dies as usual, swells 0.5 s, then drops its children round the corpse into its wave.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BroodSpec.cs` | Core | child id, count, delay, radius |
| `Core/Ai/EnemySystem.cs` | Core | queue, burst |
| `Core/Director/SpawnDirector.cs` | Core | pending brood holds its wave |
| `Tests/Core/Ai/BroodDropTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec`, `EnemyAgent`, `EnemyEvents.cs`, `ContentCatalog` |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class BroodSpec
{
    public BroodSpec(ContentId child, int count, float burstDelay, float dropRadius); // scorpling, 3, 0.5, 1.2
    public const int MaxCount = 4;
    public ContentId Child { get; }   // valid
    public int Count { get; }         // [1, MaxCount]
    public float BurstDelay { get; }  // (0, EnemySystem.CorpseTime)
    public float DropRadius { get; }  // > 0
}
// EnemySpec(..., BroodSpec brood = null) last; EnemySpec.Brood { get; }
// EnemyAgent: public bool BroodPending { get; }  public ReadOnlySpan<int> Brood { get; }  // ids dropped
// public readonly struct EnemyBroodSwelling { Id; Duration; }  // at death, the tell
// public readonly struct EnemyBroodBurst { Id; Position; }     // at burst, before the children's EnemySpawned
```

## Behaviour

1. No `BroodSpec`: every death unchanged.
2. `ContentCatalog` refuses a child that is unknown, a boss, or has a `BroodSpec` (one generation).
3. A brood enemy dies through the one door as built (XP, kill, `EnemyDied`), then publishes `EnemyBroodSwelling(id, BurstDelay)` and sets `BroodPending`.
4. A death with `explode: false` (taken by its boss, JG-26d) drops nothing.
5. `BurstDelay` after death, after the behaviour walk and before deferred despawns: `EnemyBroodBurst`, then `Count` children spawn at `DropRadius` round the corpse, evenly spaced from its facing; `Brood` holds their ids, `BroodPending` clears.
6. Children of a summon are summons (`SpawnSummon`); otherwise they pay their own XP. They are never Elites.
7. `SpawnDirector` keeps a dead id in its wave while `BroodPending`, then adopts its `Brood` into that wave, which clears only when they die. The id stride grows by `Count` per brood body a wave can buy.
8. The drop ignores the concurrency cap.
9. Stage reset or unload drops pending broods silently.
10. Burst and adoption allocate nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoBrood_DeathUnchanged` | scorpion / kill / no swelling |
| `Catalog_ChildUnknownBossOrBrood_Throws` | three catalogs / build / throws |
| `Death_PaysThenSwells` | mother / kill / XP banked, `EnemyDied` then `EnemyBroodSwelling` 0.5 |
| `BeforeDelay_NoChildren` | killed / 0.45 s / pending, nothing spawned |
| `AfterDelay_DropsThreeRound` | killed / 0.5 s / `EnemyBroodBurst`, 3 `EnemySpawned` 1.2 m out, 120° apart |
| `Children_TakeThreeFanSlots` | burst / ids / three distinct `SlotOffsetDegrees` |
| `Children_PayOwnXp` | kill all three / bank / 3 × child XP |
| `TakenByBoss_DropsNothing` | summon mother, boss dies / 1 s / no swelling |
| `SummonMother_ChildrenAreSummons` | summon mother killed by hero / burst / children `IsSummon`, no XP |
| `EliteMother_ChildrenPlain` | Elite mother / burst / children not Elite |
| `Wave_HeldWhilePending` | last body a mother / kill, 0.3 s / no `WaveCleared` |
| `Wave_ClearsOnLastChild` | burst / kill children / `WaveCleared` after the last only |
| `Drop_IgnoresCap` | at cap / burst / three spawn |
| `Reset_DropsPending` | pending / stage reset / nothing spawns |
| `Burst_NoAlloc` | kill → burst → adopt / 300 ticks / `AllocationAssert.None` |

## Out of scope

- Assets, views, roster (SS-07b–d).
- A second generation; a burst that hurts.

## As built

_Commit body._
