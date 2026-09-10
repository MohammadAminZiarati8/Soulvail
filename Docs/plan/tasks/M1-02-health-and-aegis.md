# M1-02 — `Health`, `ShieldSpec`, `DamageResult`: HP, Aegis recharge, hit i-frames

**Size:** M · **Depends on:** M1-01 · **Branch:** `m1-02-health-and-aegis`
**Design refs:** CC §2.5, §6.1, §7 (survivability); CH §3.1 (Aegis); GD §6.1

## Goal

One health component serves player and enemies: shield-before-HP, recharge after quiet time, hit i-frames, death — pure, timed by the caller, and event-free so its owner decides what to publish.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ShieldSpec.cs` | Core | Aegis numbers |
| `Core/Combat/DamageResult.cs` | Core | What a damage call did |
| `Core/Combat/Health.cs` | Core | The component |
| `Tests/Core/Combat/HealthTests.cs` | Tests.Core | Every rule |
| *small edits* | | `CharacterSpec` + `Shield`, `HitIFrames`; `CharacterDefinition` + fields; `Oathbound.asset` (30 / 4 / 15 / 0.5); `CharacterDefinitionTests` asserts them |

## Public API

```csharp
namespace Soulvail.Core.Content;

public sealed class ShieldSpec
{
    public float Max             { get; }   // 30
    public float RechargeDelay   { get; }   // 4 s without taking damage
    public float RefillPerSecond { get; }   // 15
    public ShieldSpec(float max, float rechargeDelay, float refillPerSecond);   // all > 0
}
```

```csharp
namespace Soulvail.Core.Combat;

public readonly struct DamageResult
{
    public readonly float ToShield;
    public readonly float ToHp;
    public readonly bool  Blocked;      // i-frames or external invulnerability
    public readonly bool  Killed;       // this call took HP to 0
    public float Applied => ToShield + ToHp;
    public static DamageResult None { get; }
}

public sealed class Health
{
    public Health(Stat maxHp, ShieldSpec shield /* nullable */, float hitIFrames);

    public Stat  MaxHp     { get; }
    public float Current   { get; }            // [0, MaxHp.Value]
    public float Fraction  { get; }            // Current / MaxHp.Value
    public bool  HasShield { get; }
    public float Shield    { get; }            // 0 when no shield spec
    public float ShieldMax { get; }
    public float ShieldFraction { get; }
    public bool  IsDead    { get; }
    public bool  IsInvulnerable { get; }       // hit i-frames active OR external flag

    public DamageResult ApplyDamage(float amount, float now);
    public float Heal(float amount);           // returns amount actually healed
    public void  SetExternalInvulnerable(bool on);   // Charge i-frames; independent of hit i-frames
    public void  Tick(float dt, float now);    // shield recharge
    public void  Reset();                      // full HP, full shield, not dead, no i-frames
}
```

## Behaviour

1. `amount <= 0`, `IsDead`, or `IsInvulnerable` → `DamageResult.None` / `Blocked = true` respectively; nothing changes. (`IsDead` returns `None`, not `Blocked`.)
2. Damage goes to shield first, remainder to HP. `Killed` is true when this call takes `Current` to 0.
3. Any call that applied damage (`Applied > 0`) starts hit i-frames until `now + hitIFrames` (0 → none) and resets the shield recharge delay (shield won't refill until `now + RechargeDelay`).
4. `Tick(dt, now)`: if `HasShield`, not dead, `Shield < ShieldMax`, and `now >= lastDamageAt + RechargeDelay` → `Shield += RefillPerSecond × dt`, clamped to max.
5. `Heal` raises `Current` up to `MaxHp.Value`; no effect when dead; returns the actual amount.
6. `MaxHp.Changed` (M1-01): if `MaxHp.Value` drops below `Current`, `Current` clamps down; if it rises, `Current` is unchanged (no free heal). `Fraction` always uses the live max.
7. `SetExternalInvulnerable(true)` blocks damage regardless of hit i-frames; `false` clears only the external flag.
8. `Reset` restores everything, including clearing external invulnerability.
9. No allocation in `ApplyDamage`, `Tick`, `Heal`.

Oathbound values: MaxHp 140, Shield 30 / 4 s / 15 per s, HitIFrames 0.5 s. Enemies (M1-05) construct with `shield = null`, `hitIFrames = 0`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Damage_HitsShieldFirst` | 140/30 / ApplyDamage(20, now 0) / ToShield 20, ToHp 0, Shield 10, Current 140 |
| `Damage_OverflowsToHp` | shield 10 / ApplyDamage(25) / ToShield 10, ToHp 15, Current 125 |
| `Damage_NoShield_HitsHp` | shield null / ApplyDamage(30) / ToHp 30 |
| `Damage_Zero_IsNone` | — / ApplyDamage(0) / `None`, no i-frames started |
| `Damage_StartsHitIFrames` | hitIFrames 0.5 / hit at 1.0, hit at 1.4, hit at 1.6 / second Blocked, third applied |
| `Damage_Kills_AtZero` | Current 10, no shield / ApplyDamage(10) / Killed, IsDead, Current 0 |
| `Damage_WhenDead_IsNone` | dead / ApplyDamage(5) / `None`, still 0 |
| `Shield_RechargesAfterDelay` | shield 0, last damage at 0 / Tick to now 3.9 / Shield 0; Tick(0.5) at now 4.4 / Shield 6 (0.4 s × 15) |
| `Shield_RechargeDelayResetsOnDamage` | recharging / damage at now 4.5 / Shield refill stops until 8.5 |
| `Shield_ClampsAtMax` | Shield 29 / Tick(1) past delay / Shield 30 |
| `Shield_NoRechargeWhenDead` | dead, shield 0 / Tick past delay / Shield 0 |
| `Heal_ClampsAndReturnsActual` | Current 130/140 / Heal(20) / returns 10, Current 140 |
| `Heal_WhenDead_Zero` | dead / Heal(50) / returns 0, still dead |
| `MaxHpDrop_ClampsCurrent` | 140/140 / MaxHp.Base = 100 / Current 100 |
| `MaxHpRise_KeepsCurrent` | 100/100 / MaxHp.Base = 140 / Current 100, Fraction ≈ 0.714 |
| `ExternalInvulnerable_Blocks` | external on / ApplyDamage(10) / Blocked; off / applied |
| `ExternalInvulnerable_IndependentOfHitIFrames` | external on, no hit i-frames / — / `IsInvulnerable` true; external off / false |
| `Reset_RestoresAll` | damaged, dead, external on / Reset / full HP, full shield, not dead, not invulnerable |
| `ApplyAndTick_AllocateNothing` | warm-up / 10 000 × (ApplyDamage, Tick) / allocated-bytes delta == 0 |
| `Oathbound_ToSpec_HasShieldAndIFrames` *(Tests.Game)* | asset / ToSpec / Shield 30/4/15, HitIFrames 0.5 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Publishing events — the owner (`PlayerCombat` M1-08, `EnemySystem` M1-11) does that from `DamageResult`.
- Damage types, resistances, tags (ADR-0010) — M7-02 affixes are the first consumer.

## As built

Four files as specified plus the four small edits, and 25 tests, not 20. **193 EditMode tests pass in 4.52 s** (168 before), the 3 PlayMode tests still pass in 2.71 s, zero errors, zero warnings.

1. **Rule 4 contradicts its own test row, and the row is right.** The rule says `Shield += RefillPerSecond × dt` once past the deadline; `Shield_RechargesAfterDelay` ticks 0.5 s onto `now = 4.4` with the deadline at 4.0 and expects **6**, showing its arithmetic as `0.4 s × 15`. `Tick` credits the intersection of the step `[now - dt, now]` with `[lastDamageAt + RechargeDelay, ∞)`. Rule 4's formula is what that reduces to a frame later, in the steady state. The whole-step reading would hand out up to one frame of free shield at every recharge, sized by the frame rate — 0.75 points at `Dt`'s 50 ms clamp. The step's start is `now - dt` rather than a remembered value, so a single `Tick` is self-consistent regardless of what happened between it and the last one.
2. **`now` and `dt` must be finite, and `dt` non-negative** — `ArgumentOutOfRangeException`, the same door M1-01 closed one level down. A run's time is the sum of each tick's `Dt`, both deadlines this class keeps are derived from `now`, and every comparison against NaN is false: one NaN would mean i-frames that never fire and an Aegis that never returns, for the rest of the run, with nothing logged. A NaN *amount* is deliberately not an exception — rule 1's `!(amount > 0f)` already answers "no damage".
3. **`ShieldSpec` refuses infinity as well as zero and negatives**, and `hitIFrames` is refused when negative, NaN or infinite in both `Health` and `CharacterSpec`. Infinity gets its own check because it passes a `>= 0` test while meaning permanent invulnerability, which is a state `SetExternalInvulnerable` owns and arithmetic should never reach.
4. **`CharacterSpec`'s new parameters are optional** (`ShieldSpec shield = null, float hitIFrames = 0f`). Required ones would have forced edits to seven call sites in `ContentTests.cs`, outside the Files table — and null is the honest default anyway, since the Aegis is the Oathbound's signature and every other class and every enemy has none.
5. **A `_shieldMax` of 0 means "no shield"**, yielding a null `CharacterSpec.Shield`. Preferred to a "has shield" toggle, which would permit a state the spec cannot hold — on, with a max of zero — and would need two fields kept in agreement by hand. `DamageResult`'s constructor is `internal`: it is a report, not a request.
6. **Five rows beyond the Tests table**, one per decision above — `ShieldSpec_RefusesNonPositiveNumbers`, `Constructor_RefusesNullMaxHpAndInvalidIFrames`, `Time_MustBeFinite`, `ToSpec_InvalidHitIFrames_ThrowsNamingAsset`, `ToSpec_ZeroShieldMax_MeansNoShield`. `MaxHpDrop_ClampsCurrent` covers all three ways a `Stat` moves (`Base`, `Add`, `RemoveAll`), because rule 6 must hold for each and the `RemoveAll` half is what pins "restoring the maximum never restores the HP". `ApplyAndTick_AllocateNothing` also measures `Reset`, a second `Tick` past the delay and `Heal` — rule 9 names `Heal`, and one tick returns before the refill arithmetic runs.
7. **`IsInvulnerable` is a property, so `Health` remembers the last `now` it was handed.** `Tick` and `ApplyDamage` both record it. Fresh for a caller in the frame loop, which is the contract; stale for anything that stops ticking. `IsInvulnerable(float now)` was the alternative and was not taken, because the property is the shape M1-17's HUD wants.
8. **Rule 6 has an unnamed hole: a maximum driven to zero kills silently.** `Current` clamps to 0 and `IsDead` turns true with no `DamageResult` to carry `Killed`. Documented on `OnMaxHpChanged` rather than guarded — a `Health` cannot publish, and nothing in V1 removes max HP. The first effect that does checks `IsDead` after the change.
9. **`HasShield` means "has a spec", not "the shield is up"** — rule 4 requires it, since a depleted shield must still recharge. **Overkill reports what landed, not what was swung**: `ToHp` is `Min(Current, remainder)`, which is what a damage-numbers view should float and what makes a sum of `ToShield` the whole implementation of CH §3.1's Martyr.
