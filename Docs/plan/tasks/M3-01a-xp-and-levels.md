# M3-01a — `XpCurve`, `LevelTracker`, and the kill that pays for a level

**Size:** M · **Depends on:** — · **Branch:** `m3-01a-xp-and-levels`
**Design refs:** CH §5.2, §5.3; GD §4.5, §12.1, §12.5, §13.1, §15; AR §5, §9, §10.1, §11.1, §18.1, §18.2, §18.3; ADR-0006, ADR-0008, ADR-0011 · **Ledger rows:** 1 (the curve half — M3-12 owns the nodes, M3-15 gates both), 2 (this task names nothing on disk; M3-01b is the bump)

## Goal

A kill is worth XP, XP crosses thresholds into levels, and a level is a pick the player is owed — all as data on the mode and the archetype, decided on the tick, and announced through two events a bar and a screen can be built on. Nothing is saved and nothing is offered yet.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/XpCurve.cs` | Core | CH §5.2 as a `readonly struct`, beside `ScalingSpec`'s five |
| `Core/Progression/LevelTracker.cs` | Core | XP into the level, the level, the picks owed, and the `XpGain` stat |
| `Core/Events/ProgressionEvents.cs` | Core | `LeveledUp`, `XpChanged` — the module's vocabulary, `RunEvents.cs`' precedent |
| `Tests/Core/Content/XpCurveTests.cs` | Tests.Core | the formula, its guards, and the pacing arithmetic against the shipped numbers |
| `Tests/Core/Progression/LevelTrackerTests.cs` | Tests.Core | granting, levelling, events, the tick's drain |
| *small edits* | | `EnemySpec` + `XpValue` (required, after `threatCost`); `EnemyDefinition` + `_xpValue`; `Husk.asset` 12, `Spitter.asset` 21, `Bloater.asset` 24; `ModeSpec` + `Xp` (required, after `scaling`); `ModeDefinition` + three fields behind a foldout; `Descent.asset` 20 / 12 / 1.4; `EnemySystem` + `PendingXp` and `DrainXp()`; `RunState` + `internal LevelTracker Progression` and the reads `Level`, `XpFraction`, `PendingLevelUps`; `RunSession.Start` builds the tracker, `RunSession.Tick` drains (rule 6); `DebugOverlay` shows level, fraction and picks owed; `EnemyDefinitionTests` / `ModeDefinitionTests` pin the assets; **AR §5**'s `Progression` row, which lists `XpCurve` there — corrected to `Content`, M2-02's shape; **AR §18.1** gains the drain's row |
| *ripple* | | **21 `new EnemySpec(...)` sites across 14 files and 33 `new ModeSpec(...)` across 17** — mechanical, compiler-guided, M2-02's nineteen `RunConfig` sites again |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// CH §5.2: XP to reach level N ≈ base + perLevel · N^exponent. Level 1 costs nothing.
public readonly struct XpCurve
{
    public XpCurve(float @base, float perLevel, float exponent);   // Descent: 20, 12, 1.4

    public bool IsAuthored { get; }                                  // ScalingSpec's curves' member, same reason
    public float ToReach(int level);                                 // 0 for level <= 1; allocates nothing
}

// EnemySpec (added): `float xpValue` after `threatCost`
public float XpValue { get; }            // > 0, finite. Husk 12, Spitter 21, Bloater 24

// ModeSpec (added): `XpCurve xp` after `scaling`
public XpCurve Xp { get; }               // refused when not IsAuthored, like a default ScalingSpec curve
```

```csharp
namespace Soulvail.Core.Progression;

public sealed class LevelTracker
{
    public LevelTracker(XpCurve curve, IDomainEvents events);

    public Stat XpGain { get; }             // base 1: a multiplier on every grant (ADR-0008)
    public int Level { get; }               // from 1
    public float Xp { get; }                // into the current level, [0, XpToNext)
    public float XpToNext { get; }          // curve.ToReach(Level + 1)
    public float XpFraction { get; }        // Xp / XpToNext, in [0, 1)
    public int PendingLevelUps { get; }     // earned and not yet spent

    public void Grant(float baseXp);        // × XpGain; may cross several thresholds in one call
    public void SpendLevelUp();             // PendingLevelUps − 1; throws at 0
}
```

```csharp
namespace Soulvail.Core.Events;

/// A threshold was crossed. Once per level, in order, before the XpChanged that settles the bar.
public readonly struct LeveledUp { public readonly int Level; public readonly int PendingLevelUps; }

/// XP moved. Once per Grant that changed anything, after any LeveledUp — the state a bar draws.
public readonly struct XpChanged { public readonly float Gained; public readonly int Level; public readonly float Fraction; }
```

```csharp
// EnemySystem (added)
public float PendingXp { get; }          // what the deaths since the last drain are worth
public float DrainXp();                  // returns PendingXp and zeroes it

// RunState (added) — reads, never the handle (AR §18.2)
internal LevelTracker Progression { get; }
public int Level => Progression.Level;
public float XpFraction => Progression.XpFraction;
public int PendingLevelUps => Progression.PendingLevelUps;
```

**`XpCurve` lives in `Core/Content/`, not `Core/Progression/`.** AR §5's module table puts it under `Progression`; it is authored data on the mode, the same kind of thing as GD §12's five curves, and `ModeSpec` (Content) has to name it — placing it in `Progression` would make `Content` depend on `Progression` while `Progression` depends on `Content` for M3-02a's specs. §10.1's "specs are immutable records in the catalog" wins, and §5's row is corrected in this change — a sketch is fixed when it misleads (AR §5's preamble). A placement, not a behaviour; nothing below tests it.

## Behaviour

**The data**

1. **The curve is the mode's** — GD §4.5 makes a mode a data object and levelling pace is the second-largest thing after difficulty that distinguishes one; a Boss Rush levels differently or not at all. `ToReach(level)` is `0` for `level <= 1` and `base + perLevel · level^exponent` otherwise. Guards: `base` finite and ≥ 0, `perLevel` finite and > 0, `exponent` finite and > 0. `IsAuthored` is `perLevel > 0`, because a zeroed curve answers `0` for every level and a tracker fed it would level on every grant without end — the silent failure `ScalingSpec.Require` exists for, and `ModeSpec` refuses the default the same way.
2. **XP is a fact about the creature, on `EnemySpec` beside `ThreatCost`**, and required rather than defaulted: a kill worth nothing is silent in exactly the way a free archetype is (M2-04 rule 12). Shipped as **3 × threat cost** — Husk 12, Spitter 21, Bloater 24 — one rule an author can hold in their head, and one with a property worth having: **a stage's XP is then a function of its budget alone**, `3 · B(n)` less whatever the composer could not spend, whatever mix the seed drew. Pacing cannot be rerolled by killing the app, and the arithmetic in rule 9 needs no composer to check.
3. **Every death pays, whoever caused it.** `EnemySystem.ApplyDamage` adds `agent.Spec.XpValue` to `PendingXp` on the call that reports `Killed` — the one door a death comes through (AR §18.2) — so a Bloater that lights its own fuse, a Censer, a Charge and M5-04's Wights all pay the same. A rule about *who* killed it would need a second mechanism the day minions exist, and GD §15 says only that XP is granted on kill.

**The tracker**

4. **`Grant(baseXp)`** multiplies by `max(0, XpGain.Value)`; a result that is not greater than zero — a non-positive grant, NaN, or a stack that drove the gain negative — does nothing and publishes nothing (`!(amount > 0f)`, AR §18.3). Otherwise `Xp += amount`, then **while `Xp >= XpToNext`**: `Xp -= XpToNext`, `Level++`, `PendingLevelUps++`, publish `LeveledUp(Level, PendingLevelUps)`. Then exactly one `XpChanged(gained, Level, XpFraction)`. Several thresholds in one grant is legal and ordinary: a Bloater at stage 1 is a quarter of a level.
5. **Order: every `LeveledUp` first, `XpChanged` last.** The bar wants the settled state; an `XpChanged` carrying a fraction above 1 would be a bar asked to draw a number it cannot, and a screen that pauses on `LeveledUp` (M3-08) wants the level before the bar is redrawn under it.
6. **XP is granted on the tick, never on the fact.** `RunSession.Tick` drains `State.Enemies.DrainXp()` into the tracker **after the death check and before the director**: after, so a run that ended this tick levels nobody and publishes `LeveledUp` into no scope that could show it; before the director and the flow, so a `LeveledUp` earned by a stage's last kill precedes that tick's `StageCleared` and the boundary snapshot — the write carries the level (M3-01b). A kill reported between ticks by `ReportConeHits` accrues and is paid on the next tick, at most one frame late, which is the lag every fact already has (ADR-0003). An **AR §18.1** row.
7. **`EnemySystem.Clear()` zeroes `PendingXp`.** At a boundary the drain has already run this tick, upstream of the flow (rule 6); at `End` there is nobody left to pay. Nothing is lost either way, and the row is here so that the day something clears mid-tick the loss is a documented one.
8. **Level-ups are banked, not spent.** `PendingLevelUps` counts picks the player is owed; `SpendLevelUp` is called by whoever hands one out — M3-08's `ChooseOffer`, or an overflow level once the tree is full (CH §5.2) — and throws at zero, because spending a pick nobody earned is a bug in the caller and not a state. Two levels in one grant are two picks, and the screen shows twice.
9. **The design arithmetic, written down (ledger row 1).** Under the shipped numbers — `B(n)` from GD §12.1, XP = `3 · B(n)`, CH §5.2's 20 / 12 / 1.4 — the level at the end of stage 5 is **8**, at 10 is **14**, at 20 is **27**, at 30 is **42** and at 35 about **50**; CH §5.2's table says 8 / 13 / 22 / — / 30. The first two agree and the "a level every 25–35 s" claim early on holds (stage 1 is two levels in ten Husks). **Past stage 10 the curve and its own table cannot both be true** against a quadratic budget with flat XP per kill: the tree fills around stage 20 rather than 30. **Ruled by the owner at M3-00a: ship as authored and flag** — the exponent is one number in one asset (≈1.6 fits 5 / 10 / 20; the table's own shape) and M3-15 measures before anyone tunes. What M3-12 authors against follows from the same arithmetic: **about 1.3 picks a stage from 1 to 30**, so GD §12.5's +8–12 % effective DPS a stage means **the average pick is worth ≈ +6–9 % eDPS** — or, if half the tree touches damage at all, **each of those nodes ≈ +12–18 %**. A Husk at stage 15 has 66 HP against a 13-damage swing (six hits; +2 % for five, +70 % for three); at stage 30, 99 HP (+52 % for five, +153 % for three). M3-12 owes a TTK test at stages 1 / 15 / 30 under a stated median path, and M3-15 gates on it.
10. **`RunState` hands out reads, never the tracker.** `Grant` and `SpendLevelUp` are public on the tracker, so a public handle would let a view level the player (AR §18.2's question, asked for the fifth time). `Level`, `XpFraction` and `PendingLevelUps` are what a HUD, the overlay and M3-08 need.
11. **`XpGain` is a `Stat` with base 1** and is the address M3-05's `PlayerStat.XpGain` resolves to; nothing here puts a modifier on it. A stack that drives it to zero silences XP reversibly, which is rule 4.

## Tests

| Test | Given / When / Then |
|---|---|
| `Curve_LevelOneIsFree` | 20 / 12 / 1.4 / `ToReach(1)`, `ToReach(0)`, `ToReach(-3)` / 0, 0, 0 |
| `Curve_MatchesCharacters52` | 20 / 12 / 1.4 / `ToReach` at 2, 8, 13, 22, 30 / 51.7, 240.5, 455.2, 929, 1423 — each within 0.5 |
| `Curve_Guards` | base −1; perLevel 0; exponent 0; each of the three NaN and ∞ / ctor / throws each |
| `Curve_DefaultIsUnauthored` | `default(XpCurve)` / `IsAuthored` / false — and `ModeSpec`'s ctor refuses it (rule 1) |
| `Curve_AllocatesNothing` | warm-up / 10 000 × `ToReach(17)` / allocated-bytes delta == 0 |
| `Pacing_LevelEightByStageFive` | shipped `BudgetCurve(40, 12, 0.9)` and curve / a tracker granted `3 · B(n)` for n = 1…5 / `Level == 8` (rule 9's early half — the band the 25–35 s claim rests on) |
| `Pacing_TreeFullByStageTwenty` | same over n = 1…20 / — / `Level` in [26, 28] — **the divergence, pinned so a retune is a visible diff rather than a silent drift** (rule 9) |
| `Enemy_XpValueGuards` | 0; −1; NaN; ∞ / `EnemySpec` ctor / throws each (rule 2) |
| `Enemy_ShippedXpIsThreeTimesCost` | `Husk`, `Spitter`, `Bloater` assets / `ToSpec` / `XpValue == 3 × ThreatCost` for all three — `EnemyDefinitionTests` |
| `Descent_CarriesCharacters52` | `Descent.asset` / `ToSpec` / `Xp` is 20 / 12 / 1.4 — `ModeDefinitionTests`, beside `Descent_CarriesDesignScaling` |
| `Grant_AddsXp` | fresh, 20 / 12 / 1.4 / `Grant(10)` / `Xp` 10, `Level` 1, `Pending` 0, events `[XpChanged(10, 1, 10 / 51.67)]` |
| `Grant_LevelsOnce` | fresh / `Grant(60)` / `Level` 2, `Xp` 8.33, `Pending` 1, events `[LeveledUp(2, 1), XpChanged(60, 2, 8.33 / 75.86)]` (rules 4, 5) |
| `Grant_LevelsTwiceInOneGrant` | fresh / `Grant(140)` / `Level` 3, `Pending` 2, events `[LeveledUp(2, 1), LeveledUp(3, 2), XpChanged]` |
| `Grant_NonPositiveIsSilent` | fresh / `Grant(0)`, `Grant(-5)`, `Grant(NaN)` / nothing moved, no events |
| `Grant_ScaledByXpGain` | `XpGain` +50 % PercentAdd / `Grant(10)` / `Xp` 15, `XpChanged.Gained` 15 |
| `Grant_NegativeXpGainGrantsNothing` | `XpGain` −100 % PercentMult, then −150 % / `Grant(10)` / `Xp` 0, no event, both times (rule 4) |
| `Spend_DecrementsPending` | `Pending` 2 / `SpendLevelUp` / 1 |
| `Spend_AtZeroThrows` | fresh / `SpendLevelUp` / `InvalidOperationException` (rule 8) |
| `Grant_AllocatesNothing` | warm-up, `SilentEvents` / 10 000 × `Grant(1)` / allocated-bytes delta == 0 |
| `Kill_AccruesPendingXp` | `EnemySystem`, a Husk worth 12 / a killing `ApplyDamage` / `PendingXp` 12; `DrainXp()` returns 12 and leaves 0 |
| `Kill_ByItsOwnFusePays` | a Bloater that detonates itself / — / `PendingXp` 24 (rule 3) |
| `Clear_DropsPendingXp` | `PendingXp` 12 / `Clear()` / 0 (rule 7) |
| `Tick_GrantsAfterTheDeathCheck` | a Bloater blast that kills the player and the Bloater on one tick / `Tick` / `RunEnded` published, **no** `LeveledUp`, **no** `XpChanged` (rule 6) |
| `Tick_LeveledUpPrecedesStageCleared` | a stage's last body dies with enough XP banked to level / `Tick` / event order `… EnemyDied, LeveledUp, XpChanged, StageCleared …` (rule 6) |
| `Tick_FactKillIsPaidNextTick` | `ReportConeHits` kills between ticks / before the next `Tick`: `PendingXp` 12, `State.Level` 1, no event; after it: `XpChanged` |
| `State_HandsOutNoTracker` | reflection over `RunState` / — / `Progression` is not public, like `Combat` (rule 10) |
| `Session_StartBuildsTheTrackerFromTheMode` | a mode with 20 / 12 / 1.4 / `Start` / `State.Level` 1, `XpFraction` 0, `PendingLevelUps` 0 |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. Kill the first wave. `DebugOverlay` shows the level climbing past 1 and the fraction moving on every kill; two picks owed by the end of stage 1 (rule 9).
2. **[Editor]** Let a Bloater walk in and go off without hitting it. The overlay's fraction moves — its death paid (rule 3).

## Out of scope

- **Writing any of this to disk** — M3-01b, which is the bump ledger row 2 is about and is deliberately a separate review.
- **The tree, offers, and spending a pick** — M3-02a…M3-04 and M3-08. `SpendLevelUp` exists so the shape is fixed; nothing calls it.
- **The XP bar** (GD §16.1's thin strip) — an M3-00c screen task. `XpChanged` carries what it draws.
- **Overflow** — CH §5.2's +2 % damage and +2 % max HP per level past a full tree is M3-08's, when a level arrives with nothing to offer; it is two `ModifyStat`s (M3-05) with a per-level source.
- **Pausing on level-up** — M3-08. This task publishes; nothing stops the clock.
- **Any "+XP" node** — M3-12, through `PlayerStat.XpGain`.
- **Retuning the exponent.** Rule 9 flags it; the owner ruled it stays 1.4 until M3-15 has measured a run.

## As built

_Filled at merge._
