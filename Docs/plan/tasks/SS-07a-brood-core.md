# SS-07a — The brood in core: a swelling corpse that drops three scorplings

**Size:** M · **Depends on:** — · **Branch:** `ss-07a-brood-core`
**Design refs:** GD §8.7, §8.1, §12.2

## Goal

A `BroodSpec` enemy dies as usual, swells 0.5 s, then drops its children round the corpse into its wave.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BroodSpec.cs` | Core | |
| `Core/Ai/EnemySystem.cs` | Core | queue, burst |
| `Core/Director/SpawnDirector.cs` | Core | wave hold, cap weight |
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
// EnemySpec(..., BroodSpec brood = null) last; EnemySpec.Brood
// EnemyAgent: bool BroodPending { get; }  ReadOnlySpan<int> Brood { get; }
// readonly struct EnemyBroodSwelling { Id; Duration; }  EnemyBroodBurst { Id; Position; }
```

## Behaviour

1. No `BroodSpec`: every death unchanged.
2. `ContentCatalog` refuses a child that is unknown, a boss, an Elite or a brood (one generation).
3. It dies through the one door as built, then publishes `EnemyBroodSwelling(id, BurstDelay)`; `BroodPending` set.
4. A death with `explode: false` (taken by its boss, JG-26d) drops nothing.
5. `BurstDelay` later, after the walk, before deferred despawns: `EnemyBroodBurst`, then `Count` children at `DropRadius`, evenly spaced from its last non-zero facing (else forward), into `Brood`.
6. Children of a summon are summons (`SpawnSummon`); else they pay their own XP. Never Elites.
7. The director holds a pending id in its wave, then adopts its `Brood`. Id stride grows by `Count` per brood body.
8. A pending corpse stays registered until it bursts, even if one tick passes `BurstDelay` and `CorpseTime`.
9. Cap weight: a brood body alive or telegraphed `1 + Count`, pending `Count`, others 1; the director waits until the next body fits.
10. Stage reset or unload drops pending broods silently.
11. Burst and adoption allocate nothing beyond `Spawn`'s first use of an agent.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoBrood_DeathUnchanged` | scorpion / kill / no swelling |
| `Catalog_BadChild_Throws` | unknown, boss, Elite, brood / build / throws |
| `Death_PaysThenSwells` | mother / kill / XP, `EnemyDied` then `EnemyBroodSwelling` 0.5 |
| `BeforeDelay_NoChildren` | killed / 0.45 s / pending, none |
| `AfterDelay_DropsThreeRound` | killed / 0.5 s / burst, 3 `EnemySpawned` 1.2 m out, 120° apart |
| `Children_TakeThreeFanSlots` | burst / ids / three distinct `SlotOffsetDegrees` |
| `Children_PayOwnXp` | kill all / bank / 3 × child XP |
| `TakenByBoss_DropsNothing` | summon mother, boss dies / 1 s / no swelling |
| `SummonMother_ChildrenAreSummons` | hero kills it / burst / `IsSummon`, no XP |
| `EliteMother_ChildrenPlain` | Elite / burst / children not Elite |
| `Wave_HeldWhilePending` | last body a mother / kill, 0.3 s / no `WaveCleared` |
| `Wave_ClearsOnLastChild` | burst / kill children / cleared after the last |
| `LongTick_StillBursts` | killed / one 1 s tick / burst, adopted |
| `Cap_CountsBroodWeight` | cap 10, 7 alive / queue a mother / waits; at 6 admits |
| `Burst_NeverExceedsCap` | mothers at the cap / kill all / living ≤ cap every tick |
| `Reset_DropsPending` | pending / reset / nothing spawns |
| `Burst_NoAlloc` | warmed / kill → burst → adopt × 3 / `AllocationAssert.None` |

## Out of scope

- Assets, views, roster (SS-07b–d). Prewarming agents.
- A second generation; a burst that hurts.

## As built

_Commit body._
