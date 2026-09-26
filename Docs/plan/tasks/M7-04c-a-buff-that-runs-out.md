# M7-04c — A buff that runs out

**Size:** S · **Depends on:** M7-04b · **Branch:** `m7-04c-a-buff-that-runs-out`
**Design refs:** CH §2, §4, §4.2; GD §13.1; AR §11.2, §18.1, §18.3; ADR-0006, ADR-0008, ADR-0009 · **Ledger rows:** none

## Goal

An Active can put a number on the player for a few seconds and take it off again: *"+40 % attack
speed for 6 s"* as data. And every sweep that asks what a node reaches now looks inside an effect
that carries another.

## What was already built for it

- **`TimedEffects.Hold(effect, source, expiresAt)`** is M3-11a-ii's clock for anything a cast puts on
  and takes off: one table sorted by deadline, capacity 16, and `EffectRegistry.Remove` called when the
  deadline passes. **A second `Hold` of the same pair replaces the deadline** rather than adding a
  row. That is what a recast wants, and `GrantShieldHandler` has used it since M3-11a-ii.
- **`SkillRunner.ApplyCast` casts with the `ActiveSpec` as source**, never the `SkillSpec`, so a buff's
  modifier and the node's own take effects are two sources. `Stat.RemoveAll(source)` taking the buff
  back cannot take a Pact's downside with it.

## What counting found

**Every class-shaped sweep asks `effect is ModifyStat`.** That is `SplashFlow.AimsAtAMissingAddress`,
the Minions clause of `SplashFlow.TryRefusal` and `RequireInstallable`, and `RunSession`'s
`RequireNoMinionTarget` and `RequireOwnAddresses`. A buff carries its `ModifyStat` inside it, so an
`Empower` naming `KindlingPerStack` on a lendable branch would pass all four and throw at its first
cast. [M7-04d](M7-04d-an-upgrade-that-adds-to-a-cast.md)'s `ExtendCast` carries a whole effect inside
it, which is the same gap and also `SkillTree.RequireHandlers`'s: `CanApply` asked of the wrapper says
nothing about what it wraps. So this task names the shape once, `IWrapsEffect`, and every sweep
unwraps it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Empower.cs` | Core | **New.** The primitive and its handler |
| `Game/Authoring/EmpowerDefinition.cs` | Game | **New.** `_stat`, `_kind`, `_value` and `_seconds` — no target (rules 4, 8) |
| `Tests/Core/Effects/EmpowerTests.cs` | Tests.Core | **New.** The window, the recast, the sources, the sweeps |
| *small edits* | Core | `Core/Effects/EffectRegistry.cs` — `IWrapsEffect` (rule 5); `Core/Progression/SkillTree.cs` — `RequireHandlers` asks `CanApply` of each wrapped effect too; `Core/Progression/SplashFlow.cs` — its three clauses read each wrapped effect too; `Core/Run/RunSession.cs` — one `Register` line, unconditional, and the two own-tree sweeps read each wrapped effect too (rule 5) |
| *ripple* | — | None. No node casts one until [M7-04h](M7-04h-the-oathbounds-twenty-seven.md) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Effects;

/// <summary>An effect that carries another, which every sweep must look inside (rule 5).</summary>
public interface IWrapsEffect
{
    IEffect Inner { get; }
}

/// <summary>
/// A <see cref="ModifyStat"/> on the player for <see cref="Seconds"/> simulated seconds, then gone.
/// Recast while it runs, it runs again from the recast.
/// </summary>
public sealed class Empower : IEffect, IWrapsEffect
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seconds"/> not finite and above zero; <paramref name="stat"/> or
    /// <paramref name="kind"/> not a member (checked here, because <see cref="ModifyStat"/>'s
    /// constructor deliberately leaves the stat to <c>ModifyStatDefinition.ToEffect</c>); a
    /// <paramref name="value"/> that is not finite.
    /// </exception>
    public Empower(PlayerStat stat, ModifierKind kind, float value, float seconds);

    /// <summary>The modifier it holds — built once, aimed at <see cref="StatTarget.Player"/>.</summary>
    public ModifyStat Modify { get; }
    public float Seconds { get; }
    public IEffect Inner { get; }   // => Modify
}

public sealed class EmpowerHandler : IEffectHandler<Empower>
{
    public EmpowerHandler(EffectRegistry effects, TimedEffects timed, SimulatedClock clock);

    /// <summary>Holds it to now + Seconds, then takes a running copy off and puts it on again.</summary>
    public void Apply(Empower effect, object source);

    /// <summary>Takes the modifier off — what <c>TimedEffects</c> calls when the deadline passes.</summary>
    public void Remove(Empower effect, object source);
}
```

## Behaviour

1. **An `Empower` is a `ModifyStat` held for `Seconds`.** `EmpowerHandler.Apply(effect, source)` runs
   three calls, in this order:
   - `timed.Hold(effect, source, clock.Now + effect.Seconds)`;
   - `effects.Remove(effect.Modify, source)`;
   - `effects.Apply(effect.Modify, source)`.

   **The hold comes first, `GrantShieldHandler.Apply`'s order and its reason**: the way out is
   reserved before the way in. A `Hold` that throws at capacity leaves nothing on. A modifier applied
   first and then refused a hold would be a buff nothing ever takes off. A refreshing `Hold` of a pair
   already held never throws.

   When the deadline passes, `TimedEffects.Tick` calls `EffectRegistry.Remove(effect, source)`. That is
   `EmpowerHandler.Remove`, which is `effects.Remove(effect.Modify, source)`. **The modifier and the
   hold always leave together.**
2. **A recast refreshes rather than stacks.** The `Remove` before the `Apply` takes a running copy off.
   `ModifyStatHandler.Remove` is `Stat.RemoveAll(source)` on that one stat, and a source with nothing on
   is not an error. `Hold` replaces the pair's deadline. So Litany recast at 4 s of 6 runs to 10, not
   6 and 10, and its fire rate is +40 % throughout, never +80 %.
3. **Two Actives on one stat stack, and a node's own effects are never touched.**
   - **Different Actives are different sources**, since the cast's source is its `ActiveSpec`. Two
     buffs on `FireRate` are two modifiers pooled by kind (ADR-0008).
   - **A taken node's modifiers are the `SkillSpec`'s**, a different object. So a buff expiring never
     takes a Pact's `FireRate` or `MoveSpeed` with it.
4. **Player only.** `Empower` builds its `ModifyStat` with `StatTarget.Player`, and there is no target
   argument.
   - **A timed `Minions` modifier would land on the recipe** and reach only Wights not yet raised
     (M5-06a rule 4), so it would be a buff nobody standing gets.
   - **A timed `Self` has no self** outside an enemy scope.
   - `EmpowerDefinition` has no target field.
5. **`IWrapsEffect`, and every sweep reads through it.**
   - **What unwraps.** Wherever a sweep walks a node's effects, it asks its question of the effect and
     then of `Inner` while the effect is an `IWrapsEffect`: `SkillTree.RequireHandlers`,
     `SplashFlow`'s three clauses in both `TryRefusal` and `RequireInstallable`, and `RunSession`'s
     `RequireNoMinionTarget` and `RequireOwnAddresses`.
   - **What each sweep then asks.** A handler for the inner type, no `Minions` target on a minionless
     class, and no address `PlayerStats.Has` denies.
   - **The message names the outer type and the inner one**, e.g. *"… casts an 'Empower' carrying a
     'ModifyStat' aimed at KindlingPerStack …"*.
   - **Depth is walked, not assumed.** `ExtendCast(Empower(…))` is two levels, and the sweeps walk
     until `Inner` is not a wrapper.
6. **Registered for every class, unconditionally**: `new EmpowerHandler(effects, _timed, _clock)` on
   the line after `GrantShield`'s, because `_timed` and `_clock` exist from there. Every run has a
   player and a clock, so a buff is lendable wherever its stat is.
7. **The table has room, and the count is stated.** `TimedEffects.Capacity` is 16. By M7-04j the
   worst run holds seven timed pairs at once: the Oathbound's Bulwark, Bastion, Litany and Rending
   Sever's buff, plus a borrowed Rot's Plague Step, Fever and Blight Nova's buff. A
   seventeenth stays the throw it is: *"a bug rather than a build"*.
8. **`EmpowerDefinition` is `ModifyStatDefinition` with a clock.**
   - Its fields are `_stat`, `_kind`, `_value` and `_seconds`.
   - `_stat` is serialised by ordinal, like every `PlayerStat` field (the enum's append-only rule).
   - `ToEffect` rewraps a refusal naming the asset.
   - `OnValidate` warns on a non-positive `_seconds` or a `_stat` that is not a member.
9. **Nothing allocates on a tick.** One `ModifyStat` per authored asset at boot, a struct `Modifier` per
   apply, and `Hold`'s shift inside a fixed array.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Empower_PutsItOnForItsSeconds` | `Empower(FireRate, PercentAdd, 0.4, 6)` applied at t = 10 / tick to 15.9, then 16 / `Weapon.FireRate` is ×1.4 at 15.9 and base at 16 — rule 1 |
| `Empower_TheModifierAndTheHoldLeaveTogether` | an Empower expired / — / the stat carries no modifier from the source and `TimedEffects.Count` is 0 — rule 1 |
| `Empower_ARecastRefreshes` | cast at 10, recast at 14 / tick to 19.9, then 20 / exactly one modifier throughout, ×1.4 at 19.9, base at 20 — rule 2 |
| `Empower_TwoActivesStack` | two Actives, each `Empower(FireRate, PercentAdd, 0.2, 5)` / both cast / ×1.4 while both run — rule 3 |
| `Empower_LeavesTheNodesOwnModifierAlone` | an Active taken as a Pact whose downside is `MoveSpeed −0.10`, its cast `Empower(MoveSpeed, PercentAdd, 0.4, 4)` / cast and expire / the −0.10 is still on — rule 3 |
| `Empower_WaitsOutAPause` | a buff with 2 s left, then 30 s of paused frames (no ticks) / resume one tick / still on — rule 1, AR §18.1's `LevelUpPhase` row |
| `Empower_AimsAtThePlayer` | any `Empower` / — / `Modify.Target` is `Player` — rule 4 |
| `Empower_CastByAnActive` | an Active whose `OnCast` is an Empower, trigger met / skills tick, then `Seconds` of ticks / on, then off; `SkillCast` published once — rules 1, 6 |
| `Empower_IsRegisteredForEveryClass` | a run of each class / — / `CanApply` true — rule 6 |
| `Sweep_AnOwnBuffAtAMissingAddressRefusesTheRun` | an Oathbound whose own Active casts `Empower(KindlingPerStack, …)` / `Start` / refused, the message naming `Empower` and `KindlingPerStack` — rule 5 |
| `Sweep_ABorrowedBuffAtAMissingAddressIsDead` | a lender branch whose Active casts it / `BranchesOf` for an Oathbound / not borrowable, `ui.splash.refused.address` — rule 5 |
| `Sweep_UnwrapsAllTheWayDown` | a fixture wrapper holding a wrapper holding a `RaiseMinions`, on a minionless class / `new SkillTree(...)` / `KeyNotFoundException` naming `RaiseMinions` — rule 5 |
| `Empower_RefusesAnImpossibleBuff` | seconds 0, −1, NaN, ∞; value NaN, ∞; an undefined stat; an undefined kind / construct / throws naming the field — rule 1 |
| `Empower_AFullTableLeavesNothingOn` | `TimedEffects` holding 16 other pairs / a new Empower cast / `InvalidOperationException`, and the stat carries no modifier from the source — rule 1 |
| `Empower_TheWorstRunFitsTheTable` | seven held pairs — three grants and four buffs from seven sources / all cast / no throw, `Count` 7 — rule 7 |
| `Sweep_UnwrapsTheMinionsTarget` | a minionless class whose own tree carries a fixture `IWrapsEffect` holding `ModifyStat(…, Minions)`; the same wrapper on a lender's branch / `Start`; `BranchesOf` / the run refused naming both types; the branch dead with `ui.splash.refused.minions` — rule 5 |
| `EmpowerDefinition_BuildsItsNumbers` | *(in `SkillAuthoringTests`)* an asset with each field set / `ToEffect` / the four numbers, `Player` target, and a bad `_seconds` rewrapped naming the asset — rule 8 |
| `Empower_AllocatesNothing` | 10 000 casts and expiries / `AllocationAssert.None` / zero — rule 9 |

**Guard rows are implied, not listed:** the handler's nulls; `EmpowerDefinition.ToEffect`'s rewrap.

## Manual verification (Editor / device)

_None this task._ No node casts one until [M7-04h](M7-04h-the-oathbounds-twenty-seven.md), whose
Litany is the first; its step 3 watches the swing quicken for six seconds.

## Out of scope

- **What a running buff looks like.** Nothing draws one. The stat's own readers are the tell — the
  swing's cadence and the walk. A mark is ledger [row 3](../ROADMAP.md#carry-forward-into-m7)'s
  redesign, and M8-01's feel pass is where *"did I notice"* is answered.
- **A timed buff on the Wights.** Rule 4.
- **An enemy-side timed effect — a curse, a slow.** GD's *"Grave-Work (weapon, curses)"* gets no curse
  verb in V1; [M7-04i](M7-04i-the-gravecallers-twenty-seven.md) states what Grave-Work is instead.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
