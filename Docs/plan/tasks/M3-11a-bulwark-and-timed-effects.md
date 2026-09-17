# M3-11a — Bulwark, and the clock that takes a cast effect back

**Size:** M (five code files, and `Health` gains a damage step — a substantial edit, not an additive one) · **Depends on:** M3-06 (the runner that casts it), M3-05 (the registry it applies through), M3-02a (`ActiveSpec`) · **Branch:** `m3-11a-bulwark-and-timed-effects`
**Design refs:** CC §6.4, §7; CH §3.1, §4, §4.2; GD §12.4, §16.2; AR §5, §14, §18.1, §18.2, §18.3; ADR-0008, ADR-0009 · **Ledger rows:** 1 (Bulwark is survivability rather than damage, so it moves the *other* side of the TTK question — rule 11), 4 (a new per-frame walk joins the unmet `GC.Alloc` row)

## Goal

The first active in the game: CC §6.4's *"an enemy projectile is inbound"* becomes a shield that is there before the bolt lands and gone a few seconds later — and with it, the one piece of machinery every timed cast effect after it will use.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/TimedEffects.cs` | Core | the clock a cast effect expires on, and the only caller of `EffectRegistry.Remove` in a live run |
| `Core/Effects/GrantShield.cs` | Core | the primitive and its handler, one file per pair (M3-05's shape) |
| `Game/Authoring/GrantShieldDefinition.cs` | Game | its Inspector half — **block namespace** (Traps §5) |
| `Tests/Core/Combat/TimedEffectsTests.cs` | Tests.Core | expiry, ordering, capacity, allocation |
| `Tests/Core/Effects/GrantShieldTests.cs` | Tests.Core | the pool, the absorption order, and what expiry does to a partly spent shield |
| *small edits* | | `Core/Combat/Health.cs` — the granted pool, its step in `ApplyDamage`, `GrantShield`/`RemoveGrantedShield`, `Reset` (rules 4, 5) — **substantial**; `Core/Events/CombatEvents.cs` + `ShieldGranted` and `ShieldGrantExpired` (rule 9); `Core/Run/RunSession.cs` — builds `TimedEffects` and the handler in `Start`, ticks it in `Tick` (rule 3); `Core/Run/RunState.cs` + `PlayerGrantedShield`; `Tests/Core/Combat/HealthTests.cs` gains the absorption rows; **AR §18.1** gains the expiry's ordering row |

Only these files change. Anything else is a deviation: say so in *As built*. **If `Health`'s pool grows past one field and one branch, the split is the pool (`M3-11a-i`) and the primitive (`M3-11a-ii`) — decided before continuing, never after.**

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
    public GrantShieldHandler(Health health, TimedEffects timed, IClock clock, IDomainEvents events);
    // Apply:  health.GrantShield(effect.Amount, source); timed.Hold(effect, source, now + Duration)
    // Remove: health.RemoveGrantedShield(source)
}
```

```csharp
// Health (added)
public float GrantedShield { get; }                       // absorbed before the Aegis (rule 4)
public void GrantShield(float amount, object source);     // stacks per source, never per call
public bool RemoveGrantedShield(object source);

// RunState (added)
public float PlayerGrantedShield { get; }
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

1. **`TimedEffects` remembers; it does not apply.** The caster applies — `SkillRunner.Cast` walks `ActiveSpec.OnCast` through the registry (M3-06 rule 8) — and this class is handed the same `(effect, source)` pair with a deadline. Splitting it that way is what keeps the registry's one-line dispatch intact: an effect that expires is an ordinary effect plus a note, not a second kind of effect. M3-06 rule 8's promise — *"it owns its own clock, and `EffectRegistry.Remove` is the door it calls"* — lands here, once, rather than in every handler that ever needs a duration.
2. **Absolute expiries against the simulated clock**, `Weapon`'s and `ChargeSkill`'s shape and M3-06 rule 4's: no accumulator to drift, so a 30 fps phone and a 120 fps one hold a shield for the same five seconds. A fixed array of `Capacity`, so nothing allocates on a cast; a seventeenth hold throws rather than growing, because sixteen simultaneous timed effects is a bug and a silently growing list on a per-frame path is the kind of thing that is only found on a phone.
3. **Ticked immediately after `SkillRunner.Tick`, and that ordering is the mechanic's.** The runner may cast this frame, so expiring first would let a grant made last frame outlive one made this frame by a tick; expiring *after* means a cast and its expiry are never resolved on the same frame in the wrong order. It sits above the projectile step for the reason the runner does (M3-06 rule 7): a shield that expired **after** this tick's bolts were resolved would have absorbed a hit it was no longer entitled to. An **AR §18.1** row.
4. **Expiry is oldest-first and removal goes through the registry**, so a handler is the only thing that knows what undoing its own effect means (ADR-0009). `Release` exists for the case nothing in M3 has — a skill cancelled early — and is specified now so the next task does not invent a second way out.

**The pool**

5. **Granted shield is a third pool, spent before the Aegis, and it is deliberately not the Aegis.** The Aegis is the Oathbound's signature — 30 points, a 4 s delay, 15/s refill, *"the only regeneration in the game"* (CH §3.1) — and its `ShieldSpec` numbers are what `ShieldRingView` draws and what M3-13 will treat. Raising `ShieldMax` instead would have meant a `Stat` on the spec's maximum (M3-05 rule 5's move) **and** a separate fill, because raising a maximum is not filling it (M3-05's `Handler_MaxHpMovesHealthLive`) — two changes to the class's signature mechanic to express a temporary one. So: `ApplyDamage` spends `GrantedShield` first, then the Aegis, then hit points, and the Aegis's own numbers and its ring mean exactly what they meant before.
6. **It stacks per source, not per call.** Two casts of Bulwark before the first expires is one source (the `ActiveSpec` — M3-06 rule 8), so the second refreshes rather than doubles: `GrantShield(amount, source)` sets that source's contribution to `amount` and resets nothing else. Per-call stacking would make a floored cooldown (CH §4.1's 40 %) into permanent immunity, which is the exact failure mode the floor exists to prevent. Two *different* sources — Bulwark and some later node — stack, because they are two different things.
7. **It does not recharge and it is not healed.** The Aegis refills on its own clock and this does not: it is a cast, and it is gone when the timer says. `Health.Heal` does not touch it, for the reason `Heal` does not touch the Aegis today.
8. **Expiry clamps what is left and never goes negative.** A grant of 35 that absorbed 20 has 15 left; expiry removes **what remains of that source**, not 35, so the pool cannot go below zero and a player who spent the shield loses nothing extra when it lapses. `Clear` at the run's end forgets without removing, because there is nothing left to remove from.
9. **Two events, both after the state moved.** `ShieldGranted` carries the amount, the new total and the duration so a view can size a ring and start a countdown without a read; `ShieldGrantExpired` carries what came off and what is left, because a second grant may still be running. M3-11c draws both; nothing in core listens.

**Bulwark**

10. **Authored, not hard-coded: 35 points for 5 s on an 8 s cooldown, and every number is in the asset.** One Aegis-and-a-bit (CC §7's 30), held about as long as a Spitter volley, available a little under half the time. These are a first pass in exactly the sense M2-03's five numbers were — the owner playtests and retunes them in `Bulwark.asset` (M3-12b), not in a task. CC §6.4's trigger is `IncomingProjectiles AtLeast 1`, which is the clause M3-06 rule 7 ordered the whole frame around: read *before* the projectile step, it counts bolts still in the air, so the shield goes up before the hit rather than over the wound.
11. **What it does to ledger row 1, said plainly: nothing.** Row 1 is a time-to-kill question and Bulwark deals no damage. It moves the survivability side — GD §12.4's guardrails and the death horizon — and M3-12's TTK test must not count it. Named because a task that adds power to the player is easy to mistake for a task that closes row 1, and this one does not.
12. **The handler needs the clock and is the first that does.** `ModifyStatHandler` takes only `PlayerStats`; this one takes `Health`, `TimedEffects`, `IClock` and `IDomainEvents`, which is a handler holding four run-scoped objects and is exactly what M3-05 rule 1 means by *"the handler is built per run and holds the run's live objects."* Registered in `RunSession.Start` beside `ModifyStatHandler`, after the objects exist and before `RunStarted`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Timed_HoldsAndExpires` | hold at t=0 for 5 s / `Tick(4.9)`, `Tick(5.0)` / not removed, then `Remove` called once with that effect and source (rules 1, 2) |
| `Timed_AppliesNothing` | a counting handler / `Hold` / its `Apply` never ran — the caster applies (rule 1) |
| `Timed_ExpiresOldestFirst` | hold A for 5 s, B for 3 s / `Tick(6)` / B removed before A (rule 4) |
| `Timed_ReleaseTakesOneBackEarly` | hold for 5 s / `Release` at t=1 / removed once, `Count` 0, and `Tick(6)` removes nothing again (rule 4) |
| `Timed_ReleaseUnknown_IsFalse` | nothing held / `Release` / false, no removal (rule 4) |
| `Timed_CapacityThrows` | sixteen held / a seventeenth / throws naming the capacity (rule 2) |
| `Timed_ClearForgetsWithoutRemoving` | two held / `Clear` / `Count` 0 and the registry was asked to remove nothing (rule 8) |
| `Timed_AllocatesNothing` | eight held, none due, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 (rule 2) |
| `Timed_TicksAfterTheRunnerAndBeforeProjectiles` | a Bulwark cast this tick and a bolt arriving this tick / one `RunSession.Tick` / the grant is on **before** the bolt is resolved — `FrameOrderTests`' shape (rule 3) |
| `Health_GrantedShieldAbsorbsFirst` | 140 hp, Aegis 30 full, granted 35 / 20 damage / granted 15, Aegis 30, hp 140 (rule 5) |
| `Health_SpillsIntoTheAegisThenHp` | the same, 80 damage / — / granted 0, Aegis 0, hp 125 (rule 5) |
| `Health_AegisNumbersAreUnchanged` | granted 35 on a full Aegis / — / `ShieldMax` 30, `ShieldFraction` 1, `Shield` 30 — the ring still means the Aegis (rule 5) |
| `Health_GrantStacksPerSource` | grant 35 from A, then 35 from A again / — / `GrantedShield` 35, not 70 (rule 6) |
| `Health_TwoSourcesStack` | 35 from A, 20 from B / — / 55 (rule 6) |
| `Health_RefreshDoesNotRestoreSpentPoints` | 35 from A, spend 20, grant 35 from A / — / 35, and the test says so out loud (rule 6) |
| `Health_GrantIsNotRecharged` | granted 35, spend 20 / 10 s of `Tick` / still 15 — no refill (rule 7) |
| `Health_HealDoesNotTouchIt` | granted 15, hp 100 of 140 / `Heal(30)` / hp 130, granted 15 (rule 7) |
| `Health_RemoveTakesWhatIsLeft` | 35 from A, spend 20 / `RemoveGrantedShield(A)` / `GrantedShield` 0, hp unchanged, never negative (rule 8) |
| `Health_RemoveUnknownSource_IsFalse` | nothing from A / `RemoveGrantedShield(A)` / false, no change |
| `Health_ResetClearsThePool` | granted 35 / `Reset` / 0 |
| `Health_GrantedShieldIsInvulnerableSafe` | invulnerable, granted 35 / damage / nothing spent — i-frames come first, as they do for the Aegis |
| `Effect_Guards` | amount 0, −1, NaN, ∞; duration 0, −1, NaN, ∞ / ctor / throws each (rule 10) |
| `Handler_ApplyGrantsAndHolds` | `GrantShield(35, 5)` / `Apply(source)` / `GrantedShield` 35, one hold due at now+5, one `ShieldGranted(35, 35, 5)` (rules 9, 12) |
| `Handler_RemoveTakesItBack` | the row above / `Remove(source)` / 0, one `ShieldGrantExpired(35, 0)` (rules 8, 9) |
| `Handler_ExpiryRunsThroughTheRegistry` | a cast Bulwark / 5 s of `RunSession.Tick` / the shield is gone and `ShieldGrantExpired` published once (rules 1, 3) |
| `Handler_ExpiryReportsWhatIsLeft` | two grants, one expiring / its expiry / `Total` is the other's amount, not 0 (rule 9) |
| `Bulwark_CastsOnAnInboundBolt` | a Spitter bolt in the air, Bulwark owned and ready / `Tick` / `SkillCast` then the grant, **before** `PlayerDamaged` (rules 3, 10) |
| `Bulwark_DoesNotCastWithNothingInTheAir` | no bolts / `Tick` / no cast (rule 10) |
| `Bulwark_RefiresAfterItsCooldown` | cast, 8 s, a bolt / `Tick` / a second cast; and at 7.9 s, none (rule 10) |
| `Cast_AllocatesNothing` | Bulwark owned, `SilentEvents`, warm-up / 10 000 × cast-and-expire / allocated-bytes delta == 0 |
| `Definition_ToEffect_RoundTrip` | 35 and 5 through `SerializedObject` / `ToEffect` / a `GrantShield` with both (M3-02b rule 2) |
| `Definition_Invalid_NamesTheAsset` | duration 0 / `ToEffect` / `ArgumentException` starting with the asset name |
| `State_ExposesTheGrantedShield` | granted 35 / `PlayerGrantedShield` / 35; reflection says `Combat` is still not public |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with a hand-authored tree holding Bulwark. Take it, leave it on Auto, and walk into a Spitter's line. The `DebugOverlay` shows granted shield appearing the instant a bolt is in the air, absorbing the hit, and dropping to zero five seconds later.
2. **[Editor]** Stand where no Spitter can see you. Nothing casts, for the whole stage (rule 10).
3. **[Editor]** Let two bolts arrive in quick succession. The second is absorbed by what is left of the same grant, not by a fresh one — the cooldown is what limits it (rule 6).
4. **[Editor]** Clear a stage with a grant running. It expires on its own clock across the boundary; the Aegis behaves exactly as it always has (rules 5, 7).
5. **[device]** Ledger row 4: whether a sixteen-entry walk per frame shows on the Profiler's `GC.Alloc` line, with the rest.
6. **[device]** The real question and it is a feel one: **does a shield that appears before the bolt read as the skill working, or as nothing happening?** There is no view until M3-11c, so this is deferred twice — once to that task and once to a phone.

## Out of scope

- **Consecrate** — M3-11b, which uses this clock and adds a zone to put under it.
- **Any view** — M3-11c. Until then Bulwark is a number on the overlay, which is the same bargain M3-06 made.
- **Drawing the granted shield on the HUD** — M3-13, GD §16.2's health-bar treatment. `RunState.PlayerGrantedShield` is the read it will want.
- **Making the Aegis's numbers addressable** (`ShieldMax`, `RechargeDelay`, `RefillPerSecond` as `Stat`s) — M3-05 rule 5 says each arrives with the node that wants it, and rule 5 above says why Bulwark is not that node. **Unbroken** (CH §3.1's Oath keystone, *"Aegis recharges 2× faster"*) is, and it is not in v1's twelve.
- **Blocking a projectile outright.** Ruled at M3-00c: Bulwark is points, not a wall. A barrier is a world object with its own collision and would have made `ProjectileSystem` test every bolt against it.
- **A second timed primitive** — each arrives with its own task (M3-05's rule). This one builds the clock they share.

## As built

**Split before continuing, under this spec's own pre-declared tripwire. Not built as one task.**

> *"If `Health`'s pool grows past one field and one branch, the split is the pool (`M3-11a-i`) and the primitive (`M3-11a-ii`) — decided before continuing, never after."*

It grew past it, and **the proof is in this spec's own Tests table rather than in any implementation.** `Health_TwoSourcesStack` puts two sources in the pool at once, and rule 8 says an expiry removes *what remains of that source*. Take A granting 35 and B granting 20, then 20 points of damage: the total is 35 either way, but A's expiry removes **15** if the damage came out of A and **35** if it came out of B. One float cannot tell those apart, so the attribution has to be kept and the spend needs a stated order — a table.

**Every shape of the whole task breaches a stated limit, so the split is forced rather than chosen:**

| Shape | `Health` | Files | Breach |
|---|---|---|---|
| Table inside `Health` | ~3 fields, a distributing walk, two methods, ~479 → ~640 lines | 5 | **the tripwire above** |
| Table in its own file | one field, **zero** new branches | 6 | **the five-file ceiling** (protocol rule 6) |

**The two halves, and the dependency runs one way:**

- **[M3-11a-i](M3-11a-i-granted-shield-pool.md)** — the granted shield pool. Size **S**, one code file. Rules 4–8, the `Health_*` rows and `State_ExposesTheGrantedShield`. **Built and merged first**, wired to nothing — which is M3-04's and M3-05's bargain exactly.
- **[M3-11a-ii](M3-11a-ii-bulwark-and-timed-effects.md)** — `TimedEffects`, `GrantShield`, its handler, its Inspector half and the two events. Size **M** at five files. Rules 1–3 and 9–12.

**Four things in this spec are wrong and are corrected in the children rather than here**, so that the corrections travel with the tasks that act on them:

1. **`ShieldGranted` does not exist in code.** M3-00d's *Verified* row says it does; `grep` over `Assets/_Project` returns zero hits for `ShieldGranted`, `ShieldGrantExpired`, `GrantedShield` and `GrantShield`. `CombatEvents.cs` holds eight events and the only shield-shaped one is `PlayerShieldChanged`, the Aegis's refill. M3-00d read the **spec set** — this file's own *Public API* block — and recorded *"a spec declares it"* as *"code holds it"*. `EnemyDamaged.HpFraction`, the other half of that row, **is** real. M3-11a-ii creates both events; M3-11c and M3-13b inherit them.
2. **`GrantShieldHandler` cannot take an `IClock`.** That port is one member, `DateTimeOffset UtcNow`, and cannot produce `now + Duration`. Rule 2's *"simulated clock"* is `RunState.Time`, the same `now` `SkillRunner.Tick(dt, now)` already carries, and the handler takes it as an argument instead. A wall clock would drain a shield through a level-up screen at `timeScale` 0 and through a pause — the opposite of `OverflowToast`'s unscaled dwell, because a shield is simulation and a toast is presentation.
3. **`Health_RefreshDoesNotRestoreSpentPoints` contradicted its own expectation and is renamed.** "35 from A, spend 20, grant 35 from A → 35" **is** the spent 20 restored, which is exactly what rule 6's *"sets that source's contribution to `amount`"* produces. The number was the ruling and the name was wrong; it ships as **`Health_RefreshSetsTheSourceRatherThanStacking`**, whose claim is that a refresh does not stack to 50.
4. **Manual step 1 was not runnable as written.** `DebugOverlay.cs` lives in `Game/Presentation/`, shows `lvl`, `actives` and `slots`, has no shield readout, and is in no Files table. Under the owner's ruling it gains **one `Append`** in M3-11a-i — a small additive edit by the [ROADMAP's counting rule](../ROADMAP.md#how-to-read-this) — so the step becomes runnable the moment M3-12 authors a tree. **Steps 1–4 all need that tree either way**, and M3-11a-ii's copy says so where the steps are listed.
