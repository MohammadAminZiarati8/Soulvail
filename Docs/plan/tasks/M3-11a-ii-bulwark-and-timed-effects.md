# M3-11a-ii — Bulwark, and the clock that takes a cast effect back

**Size:** M (five code files) · **Depends on:** **M3-11a-i** (the pool it grants into), M3-06 (the runner that casts it), M3-05 (the registry it applies through), M3-02a (`ActiveSpec`) · **Branch:** `m3-11a-ii-bulwark-and-timed-effects`
**Design refs:** CC §6.4, §7; CH §3.1, §4, §4.2; GD §12.4, §16.2; AR §5, §14, §18.1, §18.2, §18.3; ADR-0008, ADR-0009 · **Ledger rows:** 1 (Bulwark is survivability rather than damage, so it moves the *other* side of the TTK question — rule 11), 4 (a new sixteen-entry per-frame walk joins the unmet `GC.Alloc` row)
**Split from:** [M3-11a](M3-11a-bulwark-and-timed-effects.md). The pool is [M3-11a-i](M3-11a-i-granted-shield-pool.md).

## Goal

The first active in the game: CC §6.4's *"an enemy projectile is inbound"* becomes a shield that is there before the bolt lands and gone a few seconds later — and with it, the one piece of machinery every timed cast effect after it will use.

## Four corrections to the parent spec, made before a line is written

1. **`ShieldGranted` does not exist in code and this task creates it.** M3-00d's *Verified* row claims *"`EnemyDamaged.HpFraction` and `ShieldGranted.Total` already exist, so neither treatment needs an event change."* The first half is true (`EnemyEvents.cs:102`); **the second is not** — `Core/Events/CombatEvents.cs` holds eight events, of which the only shield-shaped one is `PlayerShieldChanged`, the Aegis's quiet refill. M3-00d read the **spec set**, where M3-11a's *Public API* block declares the struct, and recorded *"a spec declares it"* as *"code holds it"*. **M3-11c and M3-13b inherit both events from here rather than finding them.**
2. **`IClock` cannot produce this deadline and is not taken.** `IClock` is one member, `DateTimeOffset UtcNow`, and its own remarks say *"nothing in `Soulvail.Core.Run` takes an `IClock`"*. The handler is `GrantShieldHandler(Health, TimedEffects, IDomainEvents)` and `Apply` is handed the **simulated** `now` — the same `RunState.Time` that `SkillRunner.Tick(dt, now)` already carries. **The reason belongs in the class remarks:** a wall clock would drain a shield through a level-up screen at `timeScale` 0 and through a pause, which is the *opposite* of `OverflowToast`'s unscaled dwell — because a shield is simulation and a toast is presentation.
3. **The tick site is one line, and it is immediately after `RunSession.cs:750`.** `Tick` runs `State.Time += Dt` (705) → `Combat.Tick` (726) → `State.Skills.Tick(Dt, State.Time)` (750) → `Enemies.Tick` (774) → `Projectiles.Tick` (787). Rule 3 wants it after the runner and above the projectile step, so it goes directly after 750.
4. **No asset ships, so no run in this build can cast Bulwark.** Rule 10 says every number is in the asset and the Files table ships none — `Bulwark.asset` is **M3-12b's**. Every `Bulwark_*` row builds its `SkillSpec` in the fixture. This is the **sixth consecutive task whose feature needs M3-12**, and the handover says so plainly rather than implying a playable shield.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/TimedEffects.cs` | Core | the clock a cast effect expires on, and the only caller of `EffectRegistry.Remove` in a live run |
| `Core/Effects/GrantShield.cs` | Core | the primitive and its handler, one file per pair (M3-05's shape) |
| `Game/Authoring/GrantShieldDefinition.cs` | Game | its Inspector half — **block namespace** (Traps §5) |
| `Tests/Core/Combat/TimedEffectsTests.cs` | Tests.Core | expiry, ordering, capacity, allocation |
| `Tests/Core/Effects/GrantShieldTests.cs` | Tests.Core | the handler, the events, and Bulwark cast over a real `RunSession` |
| *small edits* | | `Core/Events/CombatEvents.cs` + `ShieldGranted` and `ShieldGrantExpired` (rule 9) — **created, not found**; `Core/Run/RunSession.cs` — builds `TimedEffects` and the handler in `Start`, one tick line after `:750`; `Tests/Game/Authoring/SkillAuthoringTests.cs` gains the two `Definition_*` rows; **AR §18.1** gains the expiry's ordering row |

The two `Definition_*` rows go in the **existing** `SkillAuthoringTests.cs`, beside `ModifyStatDefinition`'s. A new file there would be a sixth counted file.

## Public API

```csharp
namespace Soulvail.Core.Combat;

/// What a cast put on the player that has to come off again. The source owns the clock
/// (M3-05 rule 6) and this is the clock it owns: one list, one absolute expiry each.
public sealed class TimedEffects
{
    public const int Capacity = 16;

    public TimedEffects(EffectRegistry effects);

    public int Count { get; }

    /// <summary>Applies nothing — the caster already did. Remembers to take it back.</summary>
    public void Hold(IEffect effect, object source, float expiresAt);

    /// <summary>Removes everything whose time is up, oldest expiry first. Allocates nothing.</summary>
    public void Tick(float now);

    /// <summary>Takes one back early, whether or not it was due. False when it is not held.</summary>
    public bool Release(IEffect effect, object source);

    public void Clear();      // run's end; removes nothing, forgets everything (rule 8)
}
```

```csharp
namespace Soulvail.Core.Effects;

/// CC §6.4's Bulwark: shield points that sit on top of the Aegis and expire.
public sealed class GrantShield : IEffect
{
    public GrantShield(float amount, float duration);   // both finite and > 0

    public float Amount { get; }
    public float Duration { get; }
}

public sealed class GrantShieldHandler : IEffectHandler<GrantShield>
{
    public GrantShieldHandler(Health health, TimedEffects timed, IDomainEvents events);

    // Apply:  health.GrantShield(effect.Amount, source); timed.Hold(effect, source, now + Duration)
    // Remove: health.RemoveGrantedShield(source)
}
```

```csharp
namespace Soulvail.Core.Events;

/// A cast put shield points on the player. Carries the new total, so a view needs no read.
public readonly struct ShieldGranted { public readonly float Amount; public readonly float Total; public readonly float Duration; }

/// They came off. Total is what is left — another grant may still be running.
public readonly struct ShieldGrantExpired { public readonly float Removed; public readonly float Total; }
```

## Behaviour

**The clock**

1. **`TimedEffects` remembers; it does not apply.** The caster applies — `SkillRunner.Cast` walks `ActiveSpec.OnCast` through the registry (M3-06 rule 8) — and this class is handed the same `(effect, source)` pair with a deadline. Splitting it that way keeps the registry's one-line dispatch intact: an effect that expires is an ordinary effect plus a note, not a second kind of effect. M3-06 rule 8's promise — *"it owns its own clock, and `EffectRegistry.Remove` is the door it calls"* — lands here, once, rather than in every handler that ever needs a duration.
2. **Absolute expiries against the simulated clock**, `Weapon`'s and `ChargeSkill`'s shape and M3-06 rule 4's: no accumulator to drift, so a 30 fps phone and a 120 fps one hold a shield for the same five seconds. A fixed array of `Capacity`, so nothing allocates on a cast; a seventeenth hold throws rather than growing, because sixteen simultaneous timed effects is a bug and a silently growing list on a per-frame path is only ever found on a phone.
3. **Ticked immediately after `SkillRunner.Tick`, and that ordering is the mechanic's.** The runner may cast this frame, so expiring first would let a grant made last frame outlive one made this frame by a tick; expiring *after* means a cast and its expiry are never resolved on the same frame in the wrong order. It sits above the projectile step for the reason the runner does (M3-06 rule 7): a shield that expired **after** this tick's bolts were resolved would have absorbed a hit it was no longer entitled to. An **AR §18.1** row.
4. **Expiry is oldest-first and removal goes through the registry**, so a handler is the only thing that knows what undoing its own effect means (ADR-0009). **This is the first production caller of `EffectRegistry.Remove` in a live run** — M3-05 rule 10's other half, finally arriving. The door is already open and already specified: `IEffectHandler<T>.Remove` declares that a source holding nothing is not an error (`Stat.RemoveAll`'s rule), so nothing here widens it. `Release` exists for the case nothing in M3 has — a skill cancelled early — and is specified now so the next task does not invent a second way out.

**Bulwark**

5. **Authored, not hard-coded: 35 points for 5 s on an 8 s cooldown, and every number is in the asset.** One Aegis-and-a-bit (CC §7's 30), held about as long as a Spitter volley, available a little under half the time. A first pass in exactly the sense M2-03's five numbers were — the owner playtests and retunes them in `Bulwark.asset` (M3-12b), not in a task. CC §6.4's trigger is `IncomingProjectiles AtLeast 1`, which is the clause M3-06 rule 7 ordered the whole frame around: read *before* the projectile step, it counts bolts still in the air, so the shield goes up before the hit rather than over the wound.
6. **Two events, both after the state moved.** `ShieldGranted` carries the amount, the new total and the duration so a view can size a ring and start a countdown without a read; `ShieldGrantExpired` carries what came off and what is left, because a second grant may still be running. M3-11c draws both; nothing in core listens.
7. **What it does to [ledger row 1](../ROADMAP.md#carry-forward-into-m3), said plainly: nothing.** Row 1 is a time-to-kill question and Bulwark deals no damage. It moves the survivability side — GD §12.4's guardrails and the death horizon — and M3-12's TTK test must not count it. Named because a task that adds power to the player is easy to mistake for a task that closes row 1, and this one does not.
8. **The handler holds three run-scoped objects and takes no clock.** `ModifyStatHandler` takes only `PlayerStats`; this one takes `Health`, `TimedEffects` and `IDomainEvents`, which is exactly what M3-05 rule 1 means by *"the handler is built per run and holds the run's live objects."* Registered in `RunSession.Start` beside `ModifyStatHandler`, after the objects exist and before `RunStarted`. The deadline comes from the simulated `now` it is handed — see correction 2.

## Tests

| Test | Given / When / Then |
|---|---|
| `Timed_HoldsAndExpires` | hold at t=0 for 5 s / `Tick(4.9)`, `Tick(5.0)` / not removed, then `Remove` called once with that effect and source (rules 1, 2) |
| `Timed_AppliesNothing` | a counting handler / `Hold` / its `Apply` never ran (rule 1) |
| `Timed_ExpiresOldestFirst` | hold A for 5 s, B for 3 s / `Tick(6)` / B removed before A (rule 4) |
| `Timed_ReleaseTakesOneBackEarly` | hold for 5 s / `Release` at t=1 / removed once, `Count` 0, and `Tick(6)` removes nothing again (rule 4) |
| `Timed_ReleaseUnknown_IsFalse` | nothing held / `Release` / false, no removal (rule 4) |
| `Timed_CapacityThrows` | sixteen held / a seventeenth / throws naming the capacity (rule 2) |
| `Timed_ClearForgetsWithoutRemoving` | two held / `Clear` / `Count` 0 and the registry was asked to remove nothing (rule 4) |
| `Timed_AllocatesNothing` | eight held, none due, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 (rule 2) |
| `Timed_TicksAfterTheRunnerAndBeforeProjectiles` | a Bulwark cast this tick and a bolt arriving this tick / one `RunSession.Tick` / the grant is on **before** the bolt is resolved (rule 3) |
| `Effect_Guards` | amount 0, −1, NaN, ∞; duration 0, −1, NaN, ∞ / ctor / throws each (rule 5) |
| `Handler_ApplyGrantsAndHolds` | `GrantShield(35, 5)` / `Apply(source)` / `GrantedShield` 35, one hold due at now+5, one `ShieldGranted(35, 35, 5)` (rules 6, 8) |
| `Handler_RemoveTakesItBack` | the row above / `Remove(source)` / 0, one `ShieldGrantExpired(35, 0)` (rules 4, 6) |
| `Handler_ExpiryRunsThroughTheRegistry` | a cast Bulwark / 5 s of `RunSession.Tick` / the shield is gone and `ShieldGrantExpired` published once (rules 1, 3) |
| `Handler_ExpiryReportsWhatIsLeft` | two grants, one expiring / its expiry / `Total` is the other's amount, not 0 (rule 6) |
| `Handler_TakesNoClock` | reflection over `GrantShieldHandler`'s constructor / — / no `IClock` parameter (correction 2) |
| `Bulwark_CastsOnAnInboundBolt` | a Spitter bolt in the air, Bulwark owned and ready / `Tick` / `SkillCast` then the grant, **before** `PlayerDamaged` (rules 3, 5) |
| `Bulwark_DoesNotCastWithNothingInTheAir` | no bolts / `Tick` / no cast (rule 5) |
| `Bulwark_RefiresAfterItsCooldown` | cast, 8 s, a bolt / `Tick` / a second cast; and at 7.9 s, none (rule 5) |
| `Cast_AllocatesNothing` | Bulwark owned, `SilentEvents`, warm-up / 10 000 × cast-and-expire / allocated-bytes delta == 0 |
| `Definition_ToEffect_RoundTrip` | 35 and 5 through `SerializedObject` / `ToEffect` / a `GrantShield` with both (M3-02b rule 2) |
| `Definition_Invalid_NamesTheAsset` | duration 0 / `ToEffect` / `ArgumentException` starting with the asset name |
| `Definition_IsLinkedToAMonoScript` | `MonoScript.FromScriptableObject` / — / non-null (M3-02b's actual check, Traps §5) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

### Two things that will cost a cycle if they are not read first

- **`Timed_TicksAfterTheRunnerAndBeforeProjectiles` is an EditMode row.** The parent spec says *"`FrameOrderTests`' shape"* but the Files table puts it in `Tests.Core`, which can reach `RunSession`. If it ends up in `FrameOrderTests` instead, **PlayMode goes 16 → 17** and that is a deviation to name, not a surprise to report.
- **Prove the ordering the way M3-06 proved its two rows:** each was shown **RED under its own swap** and **GREEN under the other's**, because a row asserting *"both happened"* passes against the wrong order.
- **`Bulwark_CastsOnAnInboundBolt` is the row that will cost a cycle, and M3-06 already paid for it once.** `EnemySense.HasLineOfSight` is false by default in a core-only test, so a Spitter never begins a wind-up, and M3-06's first version of two rows died at *"the fixture failed to land a bolt"* after 3 000 ticks. The fixture has to play `SnapshotBuilder`. `ProjectileSystem.cs:401` is the only writer of `CombatBlackboard.IncomingProjectiles` and it runs **after** combat, which is the whole reason the runner sits where it does — read M3-06 rule 7 before ordering anything.
- **`GrantShieldDefinition` takes a block namespace** (Traps §5) and is a `ScriptableObject`, so run M3-02b's actual check — `MonoScript.FromScriptableObject` answering non-null — because that is the failure mode that reports nothing anywhere.

## Manual verification (Editor / device)

**Steps 1–4 all need a hand-authored tree holding Bulwark, which does not ship until M3-12.** They are listed as what to run *then*, not as what is checkable at merge.

1. **[Editor]** Descend with a hand-authored tree holding Bulwark. Take it, leave it on Auto, and walk into a Spitter's line. The `DebugOverlay`'s `shield` readout — added by M3-11a-i — shows granted shield appearing the instant a bolt is in the air, absorbing the hit, and dropping to zero five seconds later.
2. **[Editor]** Stand where no Spitter can see you. Nothing casts, for the whole stage (rule 5).
3. **[Editor]** Let two bolts arrive in quick succession. The second is absorbed by what is left of the same grant, not by a fresh one — the cooldown is what limits it (M3-11a-i rule 2).
4. **[Editor]** Clear a stage with a grant running. It expires on its own clock across the boundary; the Aegis behaves exactly as it always has.
5. **[device]** [Ledger row 4](../ROADMAP.md#carry-forward-into-m3): whether a sixteen-entry walk per frame shows on the Profiler's `GC.Alloc` line, with the rest.
6. **[device]** The real question and it is a feel one: **does a shield that appears before the bolt read as the skill working, or as nothing happening?** There is no view until M3-11c, so this is deferred twice — once to that task and once to a phone.

## Out of scope

- **The pool itself** — [M3-11a-i](M3-11a-i-granted-shield-pool.md), which ships first and wired to nothing.
- **Consecrate** — M3-11b, which uses this clock and adds a zone to put under it.
- **Any view** — M3-11c. Until then Bulwark is a number on the overlay, which is the same bargain M3-06 made.
- **`Bulwark.asset`** — M3-12b. Nothing in this build can cast it.
- **Drawing the granted shield on the HUD** — M3-13b, GD §16.2's health-bar treatment, which also inherits M3-11a-i rule 8's note about a fully absorbed hit.
- **Blocking a projectile outright.** Ruled at M3-00c: Bulwark is points, not a wall.
- **A second timed primitive** — each arrives with its own task (M3-05's rule). This one builds the clock they share.

## As built

**Merged 2026-09-17. 1 653 EditMode / 0 / 0 twice consecutively on the final code; PlayMode 16 / 16, unmoved. Eight `.cs` files across four assemblies — `Soulvail.Core` ×4, `Soulvail.Game` ×1, `Soulvail.Tests.Core` ×2, `Soulvail.Tests.Game` ×1 — no prefab, no scene, no `ProjectSettings`.**

### Four deviations, and the first one is a decision

1. **`GrantShieldHandler` takes four constructor arguments, not three, and the fourth is a clock — because the spec's own two signatures could not both be true.** `IEffectHandler<T>.Apply` is `void Apply(TEffect effect, object source)` — two parameters, no time — while this spec's `TimedEffects.Hold` takes an **absolute** `expiresAt`. Something has to compute `now + Duration` and nothing in `Apply` can. Three ways out were weighed and the third was taken:
   - *Widen `Apply`.* Changes every handler, both halves of `EffectRegistry`'s dispatch and `ModifyStatHandler`, which wants no clock at all. Rejected: a signature change to the whole open set to serve one primitive is the opposite of what ADR-0009 buys.
   - *`TimedEffects` remembers `_now` from its own `Tick`* — the house pattern (`Health._now`, `SkillRunner._now`, `ChargeSkill._now`). Rejected, and the reason is precisely why the house pattern does not transfer: **those objects tick *before* their readers and this one ticks *after* its caster.** A cast at `:750` would read the clock `TimedEffects` was handed at the end of the previous frame, so **every grant would be one frame stale** — a consistent bias, not a rounding error, and exactly the kind of "two systems, two nows, one frame" mistake the whole of AR §18.1 exists to prevent.
   - **Taken: a one-field run-scoped `SimulatedClock`**, declared beside `TimedEffects` in the same file, held by the handler, and written on **one line beside `State.Time += Dt`** at `RunSession.cs:705`. `Hold` keeps the absolute deadline the Public API block declares, nothing else in the effects module moves, and **rule 3's tick site is still one line** after the runner. The cost is named rather than hidden: `RunSession.Tick` gains **two** lines, the tick site and the clock line, and the clock line is not part of the tick site.
   The class remarks carry the argument, and **AR §18.1's new row** carries the half that the code depends on. `Handler_TakesNoClock` is renamed **`Handler_TakesNoWallClock`**, because the claim that survives is the one correction 2 actually makes: no `IClock`, ever, because a shield is simulation and would otherwise drain through a level-up screen at `timeScale` 0.

2. **`Bulwark_CastsOnAnInboundBolt` asserts the opposite order to the one this spec wrote, and the spec is wrong about the code.** The row says *"`SkillCast` then the grant"*. `SkillRunner.Fire` applies the cast effects, starts the cooldown and **then** publishes `SkillCast` (M3-06 rule 9, and its comment says why), so one tick actually holds **`ShieldGranted` → `SkillCast` → `PlayerDamaged`** and that is what the row asserts. Its *"before `PlayerDamaged`"* half needed a second ruling: a hit an absorbing grant swallows **whole publishes no event at all** — `DamageResult.Applied` is `ToShield + ToHp` and M3-11a-i deliberately kept `toGranted` off both, so `PlayerCombat.cs:409` returns early. It survives because the fixture's class carries **no Aegis** and a Spitter bolt is 70 against a 35-point grant: the bolt spills, so there is an event to be before. The row also proves the grant *paid*, which an event order cannot: the same bolt is measured against the same fixture with the trigger switched off, and it costs exactly 35 more.

3. **The frame-order row is two rows, not one**: `Timed_TicksAfterTheRunner` and `Timed_TicksBeforeTheProjectileStep`. The spec asks for M3-06's proof — *each RED under its own swap and GREEN under the other's* — and **one row spanning both halves cannot be GREEN under either swap.** Split, each behaves exactly as asked, measured both ways: the tick moved above `SkillRunner.Tick` fails the first alone (12 passed, 1 failed), moved below `ProjectileSystem.Tick` fails the second alone (12 passed, 1 failed). Both rows measure their own timings in a first pass and then arrange the coincidence they need — an expiry falling due on exactly the tick a bolt lands, and on exactly the tick a skill recasts — with **half a frame of margin on either side**, because a deadline computed to land *on* an accumulated float is a coin toss.

4. **One rule the spec did not state, and a recast depends on it: `Hold` refreshes a pair rather than queuing a second note.** `GrantedShieldPool.Grant` *sets* a source's contribution instead of adding to it (M3-11a-i rule 6), so a second note against the same `(effect, source)` would come due on the **first** cast's clock and take the **refreshed** shield back early — a Bulwark recast that left the player *less* protected than not recasting at all. One row, `Timed_HoldRefreshesRatherThanStacking`, and one paragraph in `Hold`'s remarks.

### Smaller things, named because they are outside the table

- **`SimulatedClock` is a second public type in `TimedEffects.cs`** — `ModifyStat.cs`' and `EffectRegistry.cs`' precedent, and no sixth file.
- **The `RunSession.cs` edit is three sites, not the two the Files table lists**: `Start` (the clock, the table and the `Register` line), the tick line after `:750`, and **one `_timed.Clear()` in `End`**, beside `State.Projectiles.Clear()` and for its sentence — rule 8's *"run's end"* has to be somewhere.
- **The rows landed in two fixtures rather than the Files table's one-to-one split.** `TimedEffectsTests` holds the ten that need only a clock and a recording handler; **`GrantShieldTests` holds the thirteen that need a live `RunSession`**, including both frame-order rows, because duplicating M3-06's two-hundred-line Spitter fixture into a second file to hold two rows is the worse trade.
- **Three `GrantShield_*` rows went into `SkillAuthoringTests`, not two.** The Files table says two; this spec's own Tests table lists three `Definition_*` rows. They are named `GrantShield_*` to match the file's existing `ModifyStat_*` convention. `Effect_IsPolymorphic` was deliberately **left untouched** even though its comment invites a second primitive: it is a green row from another task.
- **`Timed_CapacityThrows` is built on a counting fake and never on `GrantShieldHandler`**, exactly as warned: sixteen is this clock's ceiling and eight is `GrantedShieldPool`'s, so the obvious version throws out of `Health` at the **ninth** hold and passes while testing the wrong refusal.
- **`Apply` holds before it grants.** Both calls can fail with a capacity refusal; a note against a source that never granted is a removal that does nothing, while a grant with no note is a permanent shield.
- **The fixture's class carries 100 000 hit points** and says why in its own doc comment. `Bulwark_RefiresAfterItsCooldown` has to stand in the open for a whole eight-second cooldown, and the arena is not one Spitter for long — the director keeps composing waves. At 140 the run ends on the third bolt; at 900, at about six seconds.

### The Tests table, reconciled

22 listed rows (`Effect_Guards` counted as **one** row naming eight throws) **+ 1** for the frame-order split **+ 3** implied-guard rows this spec's own *"guard rows are implied, not listed"* owes — `Timed_Guards`, `Handler_Guards`, and `Timed_HoldRefreshesRatherThanStacking`, which is a rule rather than a guard and is deviation 4 — = **26**. 1 627 + 26 = **1 653**.

### Ledger

**Row 1: nothing, and the row now says so.** Bulwark deals no damage; it moves the survivability side, and M3-12's TTK test must not count it. **Row 4: three rows longer** — a sixteen-entry per-frame walk beside the unmet `GC.Alloc` line, and the feel question deferred **twice**, to M3-11c and to a phone. **Rows 6 and 9 move neither**, the second task running: no view, no `LocKey`, no colour.

### Manual steps

All four Editor steps need a hand-authored tree holding Bulwark, and **that does not ship until M3-12** — the sixth consecutive task in that position. Steps 5 and 6 are the device's.
