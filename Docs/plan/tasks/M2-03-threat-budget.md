# M2-03 — `ScalingSpec` + `ThreatBudget`, and depth scaling that a recycled enemy forgets

**Size:** M · **Depends on:** M2-02 · **Branch:** `m2-03-threat-budget`
**Design refs:** GD §12.1–12.4 (the math), §11.1–11.2 (device tiers), §8.3; AR §11.1, §18.3; ADR-0008 · **Ledger rows:** 2

## Goal

GD §12's five curves exist as authored data with one implementation each, and an enemy spawned at depth *n* wears exactly that depth's scaling — including after it has been recycled from a Husk that died at a different one.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ScalingSpec.cs` | Core | `BudgetCurve`, `WaveCurve`, `ConcurrencyCurve`, `StatCurve`, `ScalingSpec` — grouped, `EnemyEvents.cs`' precedent |
| `Core/Director/ThreatBudget.cs` | Core | The curves bound to one device cap; what M2-04 and M2-05 depend on |
| `Core/Ai/DepthScaling.cs` | Core | Applies h(n), d(n), s(n) to an agent as modifiers from one source |
| `Tests/Core/Director/ThreatBudgetTests.cs` | Tests.Core | Every curve against GD §12's own numbers |
| `Tests/Core/Ai/DepthScalingTests.cs` | Tests.Core | Applied, and forgotten on recycle (row 2) |
| *small edits* | | `Stat` + `RemoveAll()` (no argument); `EnemyAgent` + `MoveSpeed` and `ContactDamage` as `Stat`s, cleared and re-based in `Initialise`; `ChaserBehaviour` reads those two stats instead of `Spec`; `EnemySystem` holds the run's depth and scales what it spawns; `ModeSpec` + `Scaling`; `ModeDefinition` + its serialized block; `Descent.asset` + GD §12's numbers |
| *ripple* | | `ChaserBehaviourTests` and `EnemySystemTests` fixtures (speed and contact damage now come from stats, not from the spec); every `new ModeSpec(...)`; `StatTests` gains the new overload's rows |

Only these files change. Anything else is a deviation: say so in *As built*.

**If `EnemyAgent`'s two new stats turn out to be more than an additive edit when built — if `ChaserBehaviour` needs restructuring rather than two changed reads — split into `M2-03a` (the curves) and `M2-03b` (applying them) before continuing, never after.**

## Public API

```csharp
namespace Soulvail.Core.Content;

/// B(n) = base + linear·(n−1) + quadratic·(n−1)²  — GD §12.1
public readonly struct BudgetCurve
{
    public BudgetCurve(float @base, float linear, float quadratic);
    public float At(int stage);
}

/// W(n) = clamp(base + stage/stagesPerStep, min, max)  — GD §12.2
public readonly struct WaveCurve
{
    public WaveCurve(int @base, int stagesPerStep, int min, int max);
    public int At(int stage);
}

/// C(n) = min(base + stage/stagesPerStep, deviceCap)  — GD §12.2
public readonly struct ConcurrencyCurve
{
    public ConcurrencyCurve(int @base, int stagesPerStep);
    public int At(int stage, int deviceCap);
}

/// A capped linear multiplier: 1 + perStep · ((stage − offset) / stageStep), clamped to cap. GD §12.3.
public readonly struct StatCurve
{
    public StatCurve(float perStep, float cap, int stageStep, int stageOffset);
    public float At(int stage);
}

public sealed class ScalingSpec
{
    public ScalingSpec(
        BudgetCurve budget,
        WaveCurve waves,
        ConcurrencyCurve concurrency,
        StatCurve hp,
        StatCurve damage,
        StatCurve speed);

    public BudgetCurve Budget { get; }
    public WaveCurve Waves { get; }
    public ConcurrencyCurve Concurrency { get; }
    public StatCurve Hp { get; }
    public StatCurve Damage { get; }
    public StatCurve Speed { get; }
}

// ModeSpec (added)
public ScalingSpec Scaling { get; }
```

```csharp
namespace Soulvail.Core.Director;

public sealed class ThreatBudget
{
    public ThreatBudget(ScalingSpec scaling, int deviceCap);

    public int DeviceCap { get; }

    public float Budget(int stage);
    public int   Waves(int stage);
    public int   Concurrency(int stage);
    public float HpMultiplier(int stage);
    public float DamageMultiplier(int stage);
    public float SpeedMultiplier(int stage);
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class DepthScaling
{
    /// Takes the curves, not a ThreatBudget: scaling an enemy needs h, d and s and knows nothing
    /// about how many of them a phone can draw. The device cap is chosen in M2-04, where the
    /// arithmetic that prices it lives, and a ThreatBudget is not constructed in a run until then.
    public DepthScaling(ScalingSpec scaling);

    /// Adds one PercentMult modifier to each of MaxHp, ContactDamage and MoveSpeed, from this
    /// instance's own source. Refills the agent's health afterwards, so it arrives at the scaled
    /// maximum rather than at the unscaled one.
    public void Apply(EnemyAgent agent, int stage);
}

// Stat (added)
/// Removes every modifier, whoever added it. Returns the count.
public int RemoveAll();

// EnemyAgent (added)
public Stat MoveSpeed { get; }
public Stat ContactDamage { get; }

// EnemySystem (added)
/// The depth everything spawned from now on is scaled to. Set by RunSession.Start from
/// RunConfig.StageIndex, and by M2-10 at each stage boundary.
public int Depth { get; set; }
```

## Behaviour

**The curves**

1. `BudgetCurve.At` is GD §12.1's `B(n) = 40 + 12·(n−1) + 0.9·(n−1)²`, with the three coefficients authored. **The formula is authoritative and GD §12.1's own table disagrees with it at stage 40** — the table says 1,772 where the formula gives 1,876.9 (stages 5, 10 and 20 all match to a rounding). The table row, and the "44×" in the prose beneath it, are the error; the tests below assert the formula and quote the three rows that agree. *Flagged for the owner: GD §12.1 wants a one-line correction that this task does not make.*
2. `WaveCurve.At` is `clamp(2 + n/5, 2, 5)` — integer division, so stage 1–4 give 2 and stage 15 upward give 5.
3. `ConcurrencyCurve.At` is `min(10 + n/2, deviceCap)`. The cap is a *device* number (GD §11.1: 18 / 28 / 40), never a difficulty one — GD §11.2's device-independence rule is that surplus budget becomes quality, which is M2-04's to spend.
4. `StatCurve.At` is `min(1 + perStep · ((n − offset) / stageStep), cap)`, integer division. The offset exists because GD §12.3's three formulas are not the same shape: h and d step every stage from `n−1` (offset 1, step 1), while s steps on `floor(n/5)` (offset 0, step 5) — so s(5) is already 1.02. Two spellings of one arithmetic rather than three functions.
5. Every curve refuses `stage < 1` and every constructor refuses a cap below 1, a negative rate, a non-positive step, or an offset that is neither 0 nor 1. A curve that returns a multiplier below 1 would make deep enemies *weaker*, silently.
6. `ThreatBudget` binds a `ScalingSpec` to one `deviceCap` so nothing downstream has to carry both, and it is the type M2-04 and M2-05 depend on. It computes; it holds no run state.

**Depth scaling on the agent**

7. **`EnemyAgent` gains `MoveSpeed` and `ContactDamage` as `Stat`s**, alongside the `MaxHp` it already has through `Health` — **ruled by the owner at M2-00b**, against applying only h(n) now and leaving two-thirds of GD §12.3 for M2-06. This is the architecture's rule — *every gameplay number is a `Stat` with a modifier stack* — and the alternative, a scalar multiplier the behaviour multiplies by, has no answer for M7-02's Hasted affix (+45 % move speed) other than a second mechanism. `ChaserBehaviour` reads `_agent.MoveSpeed.Value` and `_agent.ContactDamage.Value`; `EnemySpec`'s floats stay what a designer typed and seed the stats' bases.
8. **`Apply` adds three `PercentMult` modifiers** of `h(n) − 1`, `d(n) − 1` and `s(n) − 1`, all from the `DepthScaling` instance's own source object, so `Stat.Value`'s `Π(1 + PercentMult)` yields exactly the multiplier. `PercentMult` rather than `PercentAdd` because depth must multiply with an Elite's 2.2× rather than pool with it (GD §8.3).
9. **`Apply` refills health after re-basing**, for `EnemyAgent.Initialise`'s reason (AR §18.1): `Health.Reset()` fills `Current` from `MaxHp.Value`, so a scaled maximum applied afterwards would leave a stage-20 Husk at stage-1 hit points with nothing reporting it.
10. **`EnemyAgent.Initialise` calls `RemoveAll()` on all three stats before re-basing them** — ledger row 2's answer. The no-argument overload, not a source token: a token covers only the source that owns it, and the next thing to put a modifier on an enemy (M7-02's affixes, M3's player-inflicted debuffs) would each need enumerating at the recycle point, which is exactly the kind of list that gets one entry too short. **A recycled agent forgets everything**; the rule is one line and it cannot be half-applied. `RemoveAll()` raises `Changed` once if anything was removed, and returns the count.
11. `Stat.RemoveAll()` is a second overload beside `RemoveAll(object)`, not a replacement: taking a source back when a buff ends is a different question from wiping a rental clean, and one call site must never be able to mean the other by omission.
12. **`EnemySystem.Spawn` scales what it spawns**, from `Depth`, so nothing can spawn an unscaled enemy by forgetting to. `Depth` defaults to `RunConfig.StageIndex` at `Start` and is set by M2-10 at each boundary. `DepthScaling` is constructed in `RunSession.Start` from `mode.Scaling`, and handed to the `EnemySystem` there — nothing else in the run holds one.
13. Spawning allocates only what the three stat stacks store, and **`EnemySystem.Tick` still allocates nothing** — the two new `Stat`s are read per frame by `ChaserBehaviour`, and a lazy cache that starts allocating on read would put a GC spike behind every enemy in the arena (M1-01's laziness rule).

## Tests

| Test | Given / When / Then |
|---|---|
| `Budget_MatchesDesignTable` | GD's coefficients / `At(1, 5, 10, 20)` / 40, 102.4, 220.9, 592.9 (within 0.05) |
| `Budget_Stage40_FollowsFormulaNotTable` | as above / `At(40)` / 1876.9 — with the comment naming GD §12.1's stale row (rule 1) |
| `Budget_StageZero_Throws` | — / `At(0)` / throws |
| `Waves_ClampsBothEnds` | — / `At(1)`, `At(5)`, `At(15)`, `At(40)` / 2, 3, 5, 5 |
| `Concurrency_RisesThenCaps` | deviceCap 28 / `At(1)`, `At(20)`, `At(36)`, `At(80)` / 10, 20, 28, 28 |
| `Concurrency_HonoursLowTierCap` | deviceCap 18 / `At(40)` / 18 |
| `Hp_MatchesDesign` | GD's h / `At(1)`, `At(40)`, `At(51)`, `At(99)` / 1.0, 3.34, 4.0, 4.0 |
| `Damage_CapsAtThree` | GD's d / `At(1)`, `At(20)`, `At(99)` / 1.0, 1.665, 3.0 |
| `Speed_StepsEveryFifthStage` | GD's s / `At(4)`, `At(5)`, `At(9)`, `At(10)`, `At(99)` / 1.0, 1.02, 1.02, 1.04, 1.3 |
| `Curve_NegativeRate_Throws` | perStep −0.1 / ctor / throws |
| `Curve_CapBelowOne_Throws` | cap 0.5 / ctor / throws |
| `Apply_ScalesAllThree` | stage 20, Husk 36 HP / `Apply` / `MaxHp.Value` 36·h(20), `ContactDamage` 8·d(20), `MoveSpeed` 3.5·s(20) |
| `Apply_RefillsToScaledMax` | stage 20 / `Apply` / `Health.Current == Health.MaxHp.Value` (rule 9) |
| `Apply_StageOne_ChangesNothingMeasurable` | stage 1 / `Apply` / all three at their base values |
| `Apply_MultipliesWithAnotherPercentMult` | a +120 % source already on `MaxHp`, stage 20 / `Apply` / value is base × 2.2 × h(20) (rule 8) |
| `Recycle_ForgetsPreviousScaling` | agent scaled at stage 40, despawned, recycled at stage 1 / `Initialise` / all three back at the Husk's authored numbers (**row 2**) |
| `Recycle_ForgetsAForeignModifier` | a modifier from an unrelated source on `MoveSpeed` / despawn, recycle / gone (rule 10) |
| `RemoveAll_ReturnsCountAndRaisesOnce` | 3 modifiers, one subscriber / `RemoveAll()` / returns 3, `Changed` fired once |
| `RemoveAll_Empty_RaisesNothing` | no modifiers / `RemoveAll()` / returns 0, no event |
| `Spawn_AppliesDepth` | `EnemySystem.Depth = 10` / `Spawn(husk, …)` / the agent's `MaxHp.Value` is 36·h(10) (rule 12) |
| `Chaser_WalksAtScaledSpeed` | stage 40 Husk / one `Tick` / the `EnemyMoveIntent`'s speed is 3.5·s(40) |
| `Chaser_StrikesForScaledDamage` | stage 40 Husk in reach / windup elapses / the player loses 8·d(40) |
| `Tick_StillAllocatesNothing` | 32 scaled enemies, warm-up / 10 000 × (`Ingest`, `Tick`) / allocated-bytes delta == 0 (rule 13) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Set the Run scene's starting depth to 20 and Play: Husks take visibly more hits and hit harder, and the HP bar starts full rather than part-filled (rule 9's failure is exactly a bar that starts short).
2. **[Editor]** Kill a scaled Husk, let it be replaced, and check the replacement's bar is the same length as its predecessor's — a recycled agent wearing the previous life's scaling is what row 2 describes and what step 1's depth change makes visible.

## Out of scope

- **Spending the budget.** B(n) is a number here; turning it into a composition is M2-04, and spawning it is M2-05.
- **Elites and affixes** (GD §8.3) — M7-02. `PercentMult` in rule 8 is the whole preparation.
- **Device-tier detection** — M8-03. `deviceCap` is a constant chosen in M2-04.
- **The TTK invariant and the one-shot rule** (GD §12.4). Both are acceptance checks against these curves, not code — M2-15.
- The `GD §12.1` table correction itself: flagged in rule 1, owned by the owner.

## As built

**As specified in every rule and every test row, with six deviations and two things the spec left open.** 554 EditMode green (up from 508 at M2-02) and 3 PlayMode green, zero errors, zero analyzer warnings, no `ProjectSettings/` drift. The split rule did not fire: `ChaserBehaviour` needed exactly the two changed reads the Files table predicted (`_agent.MoveSpeed.Value`, `_agent.ContactDamage.Value`) and no restructuring, so M2-03 stayed one task.

**Deviations.**

1. **`EnemySystem` takes the `DepthScaling` as a *constructor* argument, not a settable property.** Rule 12 says "handed to the `EnemySystem` there"; a constructor makes rule 12's own claim — *"nothing can spawn an unscaled enemy by forgetting to"* — structurally true rather than a convention, and it is core's stated style (constructor injection). The price is the fifth parameter reaching **fourteen call sites**, two of them in fixtures the ripple row did not name: `ConeHitsToDamageTests` and `RespawnPolicyTests`. Both took a one-line `Scaling()` helper. The alternative — an optional argument defaulting to null — fails in the silent direction, which is the whole shape this task exists to close.
2. **A sixth file: `Tests/Core/Support/Scalings.cs`.** `ScalingSpec` became a required `ModeSpec` argument and **nine fixtures build a mode**; eight of them are about targeting, cone hits, the Charge, the catalog or a session's lifecycle and their answer to "what should the scaling be" is "anything valid". Eight copies of GD §12's twenty-one numbers in test code is exactly the drift this codebase keeps warning about. `ThreatBudgetTests` and `DepthScalingTests` deliberately do **not** use it — a test of the arithmetic against a shared helper is a test of a copy against itself.
3. **Three test files outside the table's two, all pre-existing fixtures.** `ModeDefinitionTests` gained `Descent_CarriesDesignScaling` plus two authoring-guard rows and a dotted-path `SetFloat` helper (the curves live in private nested `[Serializable]` types, so `FindProperty("_scaling._hp._cap")` is the only route in). `EnemySystemTests` gained `Spawn_AppliesDepth`, `Spawn_ScaledBeforeAnnounced`, `Depth_DefaultsToOne_AndRefusesLess` and `Depth_SurvivesClear`. `ChaserBehaviourTests` gained the two `Chaser_*` rows and renamed `Tick_AllocatesNothing` → `Tick_StillAllocatesNothing`, now measured with the crowd at **depth 40** so the per-frame reads go through a *modified* stack rather than a bare base — which is the actual content of rule 13.
4. **Each curve carries one member the Public API block does not list: `IsAuthored`.** `ScalingSpec` reads it and nothing else does. AR §18.3's rule — a struct with an invariant needs the check at both ends — bites hard here: a zeroed `StatCurve` caps every multiplier at 0 and gives **every enemy in the game no hit points**, a zeroed `WaveCurve` divides by zero on the first stage composed, and a zeroed `BudgetCurve` affords nothing at every depth. All three fail in silence. Exposing one purpose-named bool per struct was cheaper than exposing every coefficient so the spec could inspect them.
5. **`EnemySystem.Depth` guards its setter and defaults to 1.** The API block spells it `{ get; set; }`. Unguarded, a `Depth = 0` surfaces one frame and one call stack later as an `ArgumentOutOfRangeException` from inside a curve, pointing at the spawn rather than at the assignment. `Clear()` deliberately leaves it alone — M2-10 clears an arena *at* a stage boundary and the next stage's depth is the point of the transition, so zeroing it here would put the two in an order `Clear` could not state. Both are pinned by rows.
6. **A shared `internal static class CurveGuard` sits in `ScalingSpec.cs`** rather than five copies of `Positive` / `NonNegative` / `AtLeast` / `Stage`. Grouped in one file on `EnemyEvents.cs`' precedent, which the Files table already invokes for the five types.

**Two things the spec left open, decided here.** (a) **`Apply` uses `Health.Reset()`** for rule 9's refill rather than a `Heal` to the new maximum: `Reset` is what `EnemyAgent.Initialise` uses for the same arithmetic one line earlier, and `Apply` is a spawn-time operation in every caller. (b) **The authoring block exposes `perStep`, `cap`, `stageStep` and `stageOffset` per stat curve**, not just the two tuning knobs — so the asset can state GD §12.3's two *shapes* rather than having one of them hard-coded in C#. `stageOffset` is `[Range(0, 1)]` in the Inspector and refused outside `{0, 1}` by `StatCurve`.

**Rule 1, flagged and not fixed.** `B(40)` is **1,876.9**; GD §12.1's table says 1,772 and the "44×" in the prose beneath it follows from the same wrong number (the real ratio is ~46.9×). Stages 1, 5, 10 and 20 all agree with the formula to a rounding, which is what makes the row the error. The formula is asserted in `ThreatBudgetTests.Budget_Stage40_FollowsFormulaNotTable` and again off the shipped asset in `ModeDefinitionTests.Descent_CarriesDesignScaling`, and **[AR §18.3](../../Architecture.md#183-numbers-and-identity) now says not to "fix" the code to match the table.** GameDesign.md is untouched: the correction is the owner's.

**Ledger row 2 is closed.** `Stat.RemoveAll()` — the no-argument overload, beside `RemoveAll(object)` and never replacing it — called by `EnemyAgent.Initialise` on all three stats *before* it re-bases them. The rejected alternative is recorded in the code: a source token covers only its own source, so M7-02's affixes and M3's debuffs would each have to be listed at the recycle point, and that list gets one entry short. `Recycle_ForgetsAForeignModifier` is the row that makes the distinction real rather than a comment.

**Three orderings and three number rules went to [AR §18](../../Architecture.md#18-invariants)**, and AR §11.1's `Stat` sketch gained the new overload — it had become a sketch that misled. `Core/Director/` needed no architecture change: AR §5's module table already listed `Director` with `ThreatBudget` in it.

**One toolchain trap re-earned, no new entry needed.** The `new ModeSpec(` sweep missed `RunSessionTests`' target-typed `new(...)` — [Traps §2](../../Traps.md) already says a grep for `new TypeName` does not find every construction and to let the compiler enumerate the call sites. It did, in one error.

**The asset's YAML was hand-written and verified rather than assumed:** `ForceReserializeAssets` on `Descent.asset` left the file byte-identical, so all twenty-one new keys bind, and `ToSpec()` read back `B(1)=40`, `B(40)=1876.9`, `W(15)=5`, `C(80,28)=28`, `h(40)=3.34`, `d(20)=1.665`, `s(5)=1.02` off the shipped asset.
