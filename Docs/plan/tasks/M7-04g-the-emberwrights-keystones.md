# M7-04g — What the Emberwright's Keystones need

**Size:** M · **Depends on:** M7-04f · **Branch:** `m7-04g-the-emberwrights-keystones`
**Design refs:** CH §3.3, §4.1, §5.2; GD §13.1; AR §18.1, §18.3; ADR-0006, ADR-0008, ADR-0009 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m7) — one device row

## Goal

Kindling can keep part of its ramp through a hit. Every *n*th cast can be free and instant. A pool
that kills can leave a pool behind. And the zone table stops throwing when the floor gets busy. The
Keystone nodes are authored in [M7-04j](M7-04j-the-emberwrights-twenty-seven.md).

## The three, read against the code

| Keystone | CH §3.3 | What it is in this build |
|---|---|---|
| **Wildfire** *(Ember)* | *"Kindling no longer fully resets — you lose half your stacks instead"* | `ModifyStat(KindlingRetained, Flat, +0.5)` |
| **Surge** *(Arcana)* — CH's *"Overflow"*, renamed | *"Every 5th ability cast is free and instant"* | `ModifyStat(FreeCastEvery, Flat, +5)` |
| **Scorched Vail** *(Ash)* | *"Your fire pools spread to any enemy that burns in them"* | `SpreadBurns(0.5, 2)` |

**Renamed, because the word is taken.** *Overflow* is CH §5.2's post-tree levelling bonus, and in
player-facing text it is exactly one row: `ui.overflow.granted`, the toast. The code has `OverflowSpec`,
`OverflowGranted`, `LevelUpFlow.OverflowLevels` and `OverflowToast`. A Keystone card saying *Overflow*
beside a toast saying *Overflow* would be two meanings on one screen. **Surge** says what it does. CH §3.3
is this project's draft, and M7-04j edits its row.

**What counting found.**

- **`Kindling.OnPlayerDamaged` sets `Stacks = 0`.** Wildfire is that line, and it publishes the literal
  `0`, which has to become the count.
- **Nothing counts casts.** `SkillRunner.PaidCasts` counts bought ones only. **"Free and instant" has
  two seams**:
  - `Fire`, where a cooldown starts;
  - `TryBuy`, where the Veilrot is spent and the clock is left alone (M6-07c).

  **The ruling:** *instant* is the first and *free* is the second. The fifth cast starts no cooldown if
  it was an ordinary one, and costs no Veilrot if it was bought. An Active has no cast time, so
  *instant* cannot mean a shorter one.
- **A burn's kill is known in one place.** Inside `ZoneSystem.Burn`, the `DamageResult`, the body's
  position and the zone's source are in hand together. Outside it a listener sees `ZoneBurned` and
  `EnemyDied` with neither the killer nor the zone.
- **The zone table throws on a ninth.** `ZoneSystem.Capacity` is 8. `Spawn` throws past it, and
  `SpawnBurnZoneHandler.Apply` and `SpawnHealZoneHandler.Apply` call `Spawn` unguarded. Only
  `PlayerCombat.DropPool` checks first.
  - The Emberwright's full tree places Emberfall, Cinder Nova and its extension, Firestorm, Flame
    Wave's extension, up to three Blink pools, and Scorched Vail's children.
  - A ninth is a crash from inside a cast, which is `SkillTree`'s guarantee broken from the other end.
- **`ChargeSkill.PoolRadius` is a `Stat` no node reaches.** `PlayerStat`'s remarks hold it for
  *"M7-04"*, and M7-04j's Spreading Ash is its node.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/SkillRunner.cs` | Core | **Substantial.** The cast counter, and the free, instant *n*th |
| `Core/Combat/ZoneSystem.cs` | Core | **Substantial.** `TrySpawn`, a capacity of 24, and a kill that spreads |
| `Core/Effects/SpreadBurns.cs` | Core | **New.** The primitive and its handler |
| `Game/Authoring/SpreadBurnsDefinition.cs` | Game | **New.** Two numbers |
| `Tests/Core/Combat/EmberwrightKeystoneTests.cs` | Tests.Core | **New.** Every rule below |
| *small edits* | Core | `Core/Combat/Kindling.cs` — `Retained`, and `OnPlayerDamaged` keeps that fraction (rule 1); `Core/Combat/PlayerCombat.cs` — `FreeCastEvery`, a `Stat` at base 0 (rule 2); `Core/Effects/PlayerStat.cs` — `KindlingRetained`, `FreeCastEvery` and `PoolRadius` appended (rules 1, 2, 7); `Core/Effects/SpawnBurnZone.cs` and `SpawnHealZone.cs` — `Apply` calls `TrySpawn`, and their capacity `<exception>` docs go (rule 5); `Core/Run/RunSession.cs` — one `Register` line, and the runner handed `combat.FreeCastEvery`; `Game/Composition/RunScope.cs` — the prewarm's *"eight is the most zones"* comment |
| *ripple* | Tests.Core, Tests.Game | `EmberwrightTreeTests.Stats_NoPoolRadiusAddressExists` becomes `Stats_PoolRadiusIsTheBlinks`. `BurningGroundTests.Pool_TheThreeNumbersAreStats` and `KindlingTests.Kindling_BothNumbersAreStats` pin the members whose names contain *Pool* and *Kindling*, and each gains its new one. `BurningGroundTests.Burn_AllocatesNothing` fills `ZoneSystem.Capacity` zones against a body sized for eight, so it pins eight rather than following the constant. `StatBlockTests.NotAnOathbounds` gains `KindlingRetained` and `PoolRadius`. `ModifyStatTests.Stats_ResolveEveryMember` gains three expectation lines. `ZoneSystemTests`' capacity rows follow `ZoneSystem.Capacity`, and `RunScope`'s prewarm follows it too, so 24 decals are prewarmed |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Combat;

public sealed class Kindling
{
    /// <summary>The fraction of stacks a hit leaves standing — base 0, clamped into [0, 1] where it is read.</summary>
    public Stat Retained { get; }
}

public sealed class PlayerCombat
{
    /// <summary>Every how many Active casts one is free and instant — base 0, below 2 meaning never (rule 2).</summary>
    public Stat FreeCastEvery { get; }
}

public sealed class SkillRunner
{
    // ... after cooldownScale (M7-04d), optional and last:  Stat freeCastEvery = null

    /// <summary>Casts since the last free one — the counter rule 2 reads. Not saved.</summary>
    public int CastsSinceFree { get; }

    /// <summary>How many casts this run were free and instant.</summary>
    public int FreeCasts { get; }
}

public sealed class ZoneSystem
{
    public const int Capacity = 24;

    /// <summary><c>Spawn</c>, or −1 and nothing placed when the table is full (rule 5).</summary>
    public int TrySpawn(float radius, float duration, float amountPerPulse, float pulseInterval,
                        float now, object source, ZoneSide side = ZoneSide.HealsThePlayer, Vector3? at = null);

    /// <summary>How many placements were refused for a full table, this run.</summary>
    public int Dropped { get; }

    public void SetSpread(float radiusScale, float seconds, object source);
    public void ClearSpread(object source);
}
```

```csharp
namespace Soulvail.Core.Effects;

/// <summary>
/// A burning zone that kills leaves a smaller one where the body fell — once (M7-04g rule 6).
/// </summary>
public sealed class SpreadBurns : IEffect
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radiusScale"/> not in (0, 1]; <paramref name="seconds"/> not finite and above zero.
    /// </exception>
    public SpreadBurns(float radiusScale, float seconds);
    public float RadiusScale { get; }
    public float Seconds { get; }
}

public sealed class SpreadBurnsHandler : IEffectHandler<SpreadBurns>
{
    public SpreadBurnsHandler(ZoneSystem zones);
    public void Apply(SpreadBurns effect, object source);   // zones.SetSpread(...)
    public void Remove(SpreadBurns effect, object source);  // zones.ClearSpread(source)
}

public enum PlayerStat
{
    // ... MinionBlastRadius (M7-04f), then — appended:

    /// <summary><c>Kindling.Retained</c>. Only a class with Kindling has one.</summary>
    KindlingRetained,

    /// <summary><c>PlayerCombat.FreeCastEvery</c>. Every class has one.</summary>
    FreeCastEvery,

    /// <summary><c>ChargeSkill.PoolRadius</c>. Only a Blink has one.</summary>
    PoolRadius,
}
```

## Behaviour

1. **A hit leaves `Retained` of the ramp standing.**
   - `OnPlayerDamaged` sets `Stacks = floor(Stacks × r)`, where `r` is `Retained.Value` clamped into
     [0, 1] and a NaN reads as 0 — a full reset, M6-07a's behaviour, so an unreadable number costs
     the Keystone rather than granting it.
   - It re-applies the modifier and publishes `KindlingChanged(Stacks, Cap(), Bonus)` with the count it
     left, not a literal 0.
   - **Nothing moves, nothing is said.** At 0 stacks, or when the floor leaves the count unchanged
     (`r` 1), it returns before the event, as the zero case does today.
   - Wildfire's +0.5 takes 30 stacks to 15 and 1 stack to 0.
   - `PlayerStat.KindlingRetained` resolves to it, and `Has` answers `Kindling is not null`.
2. **Every `n`th cast is free and instant, and `n` below 2 is off.**
   - `n` is `floor(FreeCastEvery.Value)`, read at each cast; a value that is not finite is off.
   - **Every cast counts toward it**: an ordinary `Fire`, auto or manual, and a `TryBuy`, across every
     Active, into `CastsSinceFree`. A bought cast is a cast, which is M6-11b's finding.
   - **On the `n`th:**
     - **a `Fire`** leaves `_readyAt` where it was and publishes `SkillCast(id, 0, auto)`, so the skill
       is ready again the next tick;
     - **a `TryBuy`** spends no Veilrot and publishes `CastBought(id, 0, value)`, and the governor
       latch is still set. The free buy is decided before `TryBuy`'s spend check, so it asks only
       that the class can buy at all (`InstantCastCost` above 0) and not `CanSpend`: a free cast
       needs no Rot on the meter. It counts in `PaidCasts` and in `FreeCasts`.
   - The counter returns to 0 and `FreeCasts` rises.
   - **`n` of 1 would be every cast free, which is a skill with no cooldown**, `MovementSkillSpec`'s
     refusal. So below 2 is off, and M7-04j authors 5.
   - **M3-06 rule 6 still holds**: one cast a tick across every Active, so *instant* means *the next
     tick*, never twice in one.
3. **The counter is not saved.** A resumed run counts from zero. The cost is at most four casts of
   progress, against a format bump for an integer.
4. **`FreeCastEvery` lives on `PlayerCombat` and reaches the runner as `SkillCooldown` does**
   ([M7-04d](M7-04d-an-upgrade-that-adds-to-a-cast.md) rule 7's reason: `PlayerStats` exists before the
   runner can). Every class has the number; only the Emberwright's tree names it, and a Keystone is
   never lent.
5. **The zone table holds twenty-four, and a placement past it is dropped rather than thrown.**
   - `TrySpawn` returns −1 and counts `Dropped` when full. `Spawn` keeps its throw as the backstop
     every existing test asserts.
   - `SpawnBurnZoneHandler.Apply`, `SpawnHealZoneHandler.Apply` and the spread (rule 6) use `TrySpawn`.
     `PlayerCombat.DropPool` keeps its own check.
   - **Twenty-four is counted**: seven burning casts and extensions, three Blink pools, the rest for
     spread. A full table losing a spread is a stage that is already on fire.
   - `ZoneViews` prewarms `ZoneSystem.Capacity` decals through `RunScope`, so the view follows with no
     edit.
6. **A burning zone that kills leaves a smaller one where the body fell — once.**
   - **The condition.** With a spread set, a `Burn` whose `DamageResult` is `Killed`, from a zone that
     is not itself a spread, calls `TrySpawn` with the parent's radius × `RadiusScale`, for `Seconds`,
     at the parent's damage per pulse and interval. The spread's source is its own, `SideAt`
     `BurnsEnemies`, at the body's position.
   - **A spread child does not spread.** That is one generation, the Weaver's rule and M7-04f's
     blast's.
   - **The child is placed the moment the body dies**, inside `Burn`. That walk is over the enemy
     span, not the zones, so an appended row is safe. `Tick`'s zone loop reaches it later in the same
     tick, but its first pulse is one interval away (`Spawn`'s rule), so a spread never hurts on the
     tick it is placed. No buffer of pending children is needed.
   - **A zone knows it is a spread** through a `bool` array parallel to the zone rows, shifted with
     them when a zone retires.
   - **Every burning zone spreads**, a Blink's pool included. CH's *"your fire pools"* is the
     Emberwright's fire, and the Keystone is only ever the Emberwright's.
   - `SetSpread` from a second source throws, and from the same source it is a no-op; `ClearSpread`
     clears.
   - **Registered for every class**, on the line after `SpawnBurnZone`'s, because every run has the
     zones; only the Emberwright's tree carries the Keystone, and a Keystone is never lent.
7. **`PoolRadius` is the Blink's, and `Has` says so** — `_leavesPool`, like `PoolDamage`'s. The
   *"deliberately not here"* list in `PlayerStat`'s remarks loses its last entry.
8. **Nothing allocates on a tick.** A counter, a clamp and a floor; the table is fixed; a child is one
   more row of arrays already allocated.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Wildfire_AHitKeepsHalf` | an Emberwright at 30 stacks, `KindlingRetained Flat 0.5` / a hit that lands / 15 stacks, `KindlingChanged(15, …)` — rule 1 |
| `Wildfire_RoundsDown` | 1 stack, then 7 / a hit each / 0, then 3 — rule 1 |
| `Wildfire_UnreadableIsAFullReset` | the stat forced to NaN / a hit / 0 — rule 1 |
| `Wildfire_WithoutItNothingChanges` | no modifier / a hit at 20 / 0, and the event says 0 — rule 1 |
| `Wildfire_OnlyKindlingHasIt` | an Oathbound / `Has(KindlingRetained)` / false — rule 1 |
| `Surge_TheFifthFireStartsNoCooldown` | `FreeCastEvery` 5, one Active on a 10 s cooldown, trigger always met / five casts / the fifth's `SkillCast` says 0 and the skill is ready the next tick — rule 2 |
| `Surge_TheFifthBoughtCastIsFree` | an Emberwright at 50 Rot, the fifth cast a `TryBuy` / — / the meter unchanged, `CastBought(id, 0, 50)` — rule 2 |
| `Surge_CountsAcrossEveryActive` | two Actives alternating / five casts / the fifth is free whichever skill it was — rule 2 |
| `Surge_StillOneCastATick` | a free cast / the same tick / no second cast — rule 2 |
| `Surge_BelowTwoIsOff` | `FreeCastEvery` 1, then NaN, then +∞ / ten casts each / none free — rule 2 |
| `Surge_AFreeBuyNeedsNoRot` | an Emberwright at 0 Rot, the fifth cast due while on cooldown / — / bought, the meter still 0, `PaidCasts` and `FreeCasts` 1 — rule 2 |
| `Surge_TheCounterIsNotSaved` | four casts, then a resume / one cast / not free — rule 3 |
| `Surge_EveryClassHasTheNumber` | a run of each class / `Has(FreeCastEvery)` / true — rule 4 |
| `Zones_TwentyFourStandAndAPlacementPastIsDropped` | 24 zones / `TrySpawn` / −1, `Dropped` 1, still 24 — rule 5 |
| `Zones_ABurnCastIntoAFullTableDoesNotThrow` | 24 zones, an Emberfall cast; then a Consecrate cast / — / no throw, `Dropped` 2 — rule 5 |
| `Spread_AKillLeavesAPool` | `SpreadBurns(0.5, 2)`, a 4 m burn kills a Husk at (3, 0, 0) / the pulse / a second zone at (3, 0, 0), radius 2, 2 s, the same damage per pulse — rule 6 |
| `Spread_OnlyOnce` | a spread child kills / — / nothing further placed — rule 6 |
| `Spread_TheChildWaitsAnInterval` | a child placed / the same tick / it has not pulsed — rule 6 |
| `Spread_ABlinkPoolSpreadsToo` | a Blink's pool kills / — / a child — rule 6 |
| `Spread_AFullTableLosesTheChild` | 24 zones, a kill / — / no throw, `Dropped` 1 — rules 5, 6 |
| `Spread_ASecondSourceIsRefused` | set by one / set by another; then by the first again / `InvalidOperationException`; then nothing — rule 6 |
| `Spread_ClearStopsIt` | set, then cleared / a burn kills / no child — rule 6 |
| `Spread_IsRegisteredForEveryClass` | a run of each class / — / `CanApply(new SpreadBurns(…))` true — rule 6 |
| `PoolRadius_IsTheBlinks` | an Emberwright / `ModifyStat(PoolRadius, PercentAdd, 0.33)`, a blink / the pool's `ZoneSpawned.Radius` 3.99 within 0.001 — rule 7 |
| `Keystones_AllocateNothing` | 10 000 hits, casts and spreads / `AllocationAssert.None` / zero — rule 8 |

**Guard rows are implied, not listed:** `SpreadBurns`' refusals; the handlers' nulls;
`SpreadBurnsDefinition.ToEffect`'s rewrap.

## Manual verification (Editor / device)

_None this task._ [M7-04j](M7-04j-the-emberwrights-twenty-seven.md) authors the three nodes, and its
steps 5 to 7 play them. **[device]** Twenty-four decals and a spreading floor under Swarm — one row
for row 1, measured there.

## Out of scope

- **A HUD for Kindling or for Surge's count.** [Row 3](../ROADMAP.md#carry-forward-into-m7)'s redesign.
- **Saving the counter.** Rule 3.
- **A spread that spreads.** Rule 6.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
