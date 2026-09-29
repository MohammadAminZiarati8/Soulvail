# M7-04e — What the Oathbound's Keystones need

**Size:** M · **Depends on:** M7-04d · **Branch:** `m7-04e-the-oathbounds-keystones`
**Design refs:** CH §3.1, §5; GD §12.4, §13.1; AR §18.1, §18.3, §18.4; ADR-0006, ADR-0008, ADR-0009 · **Ledger rows:** none

## Goal

The three mechanisms CH §3.1's Keystones name exist in core:
- **an Aegis that refills at a rate a node can move**;
- **a burst when the Aegis breaks**, sized by what it absorbed;
- **a swing that can be a ring**, shoving outward.

The Keystone nodes themselves are authored in [M7-04h](M7-04h-the-oathbounds-twenty-seven.md).

## The three, read against the code

| Keystone | CH §3.1 | What it is in this build |
|---|---|---|
| **Unbroken** *(Oath)* | *"Aegis recharges 2× faster, and breaking it emits a 6 m knockback"* | `ModifyStat(ShieldRefill, PercentMult, +1.0)` and `AegisBreakBurst(6, 0, 4)` |
| **Wide Censure** *(Censure)* | *"The cone becomes a full 360° ring at 60 % damage"* | `ModifyStat(WeaponConeAngle, Flat, +300)` and `ModifyStat(WeaponDamage, PercentMult, −0.4)` — data, once a ring is legal (rule 7) |
| **Martyr** *(Judgment)* | *"When Aegis breaks, deal damage equal to everything it absorbed to all enemies within 10 m"* | `AegisBreakBurst(10, 1.0, 0)` |

**What counting found.**

- **Nothing notices the Aegis breaking.** `Health` publishes nothing (AR §18.4). `PlayerDamaged`
  carries `ToShield` and `ShieldFraction` but no break, and nothing keeps a running total of what the
  Aegis absorbed.
- **The code already points here.** `DamageResult`'s remarks say summing `ToShield` *"is the whole
  implementation"* of Martyr, and `Health.ApplyDamage`'s comment leaves `toGranted` out of the result
  on purpose, so a Bulwark never feeds it.
  The one door every hurt passes through is `PlayerCombat.ApplyDamage`.
- **`ShieldSpec.RefillPerSecond` is a plain float.** `PlayerStat`'s remarks hold it back as
  *"Unbroken's keystone … should arrive whole rather than half-reachable"*, and
  `PlayerStatCoverageTests.Health_ShieldMaxAndRateAreNotAddressable` asserts no member is named like
  *refill*. This is the task that row was waiting for. **The maximum stays unaddressable**, for the
  trap `Handler_MaxHpMovesHealthLive` records.
- **A break can happen inside a damage pass.** A Bloater's blast reaches the player from inside
  `EnemySystem.ApplyDamage`. So the burst waits for a step of its own (rule 4), which is
  [M7-04b](M7-04b-a-burst-around-you.md) rule 6's rule obeyed.
- **A 360° cone is nearly legal already, and is broken in two places.**
  - **The legal part.** `PlayerCombat.ConeAngle` clamps to [0, 360] and `ConeHitIntent.AngleDeg`
    carries the full angle.
  - **The hit test.** `ConeOverlapQuery.IsInCone` compares a normalised cosine with `>=` against
    `cos(180°) ≈ −1`. Rounding in that ratio can undershoot −1 for a body dead behind the player, at
    any facing length, so the body falls out by an ulp.
  - **The shove.** `ResolveConeHits` pushes every body along the swing's facing, so a ring throws the
    enemies behind the player *through* them.
- **The swing is drawn at a fixed 60°.** `PlayerView`'s `_swingAngleDeg` is a serialised placeholder
  and `PlayerAttacked` carries only `FacingXZ`, so Broad Censure's 90° has never been drawn either.
  Drawing the live angle is [M7-04h](M7-04h-the-oathbounds-twenty-seven.md)'s, the task that ships
  the ring. This one puts the angle on the event.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/PlayerCombat.cs` | Core | **Substantial.** The absorbed tally, the break, the pending release, the break bursts a node registers, and the radial cone shove |
| `Core/Effects/AegisBreakBurst.cs` | Core | **New.** The primitive and its handler |
| `Game/Authoring/AegisBreakBurstDefinition.cs` | Game | **New.** Three numbers |
| `Tests/Core/Combat/OathboundKeystoneTests.cs` | Tests.Core | **New.** Every rule below |
| *small edits* | Core, Game, Docs | `Core/Combat/Health.cs` — `ShieldRefillPerSecond`, a `Stat` seeded from the spec, read by `Tick` (rule 1); `Core/Effects/PlayerStat.cs` — `ShieldRefill` appended, `Resolve` and `Has` lines (rule 1); `Core/Events/CombatEvents.cs` — `PlayerAttacked` gains `AngleDeg` and `Range`, optional and last (rule 8); `Game/Adapters/ConeOverlapQuery.cs` — a full circle short-circuits (rule 7); `Core/Run/RunSession.cs` — one `Register` line and the release line (rule 4); `Docs/Architecture.md` — one §18.1 row (rule 4) |
| *ripple* | Tests.Core, Tests.Game | `PlayerStatCoverageTests.Health_ShieldMaxAndRateAreNotAddressable` becomes `Health_ShieldMaxIsNotAddressable` and asserts the rate is; `StatBlockTests`' `NotAnOathbounds` gains `ShieldRefill` and `PlayerStatCoverageTests`' counts move by one; `ConeOverlapQueryTests` gains the full-circle rows; `ModifyStatTests.Stats_ResolveEveryMember` runs on a fixture class with no `ShieldSpec`, so it names `ShieldRefill` as refused there beside `ContactDamage`, and a second row resolves it on an Oathbound (rule 1); of the 20 `new PlayerAttacked(...)` sites across 4 files, 19 are untouched and `PlayerCombat`'s one is rule 8's |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Combat;

public sealed class Health
{
    /// <summary>Aegis refilled per second once the delay has passed — a Stat since M7-04e. Base: the spec's.</summary>
    public Stat ShieldRefillPerSecond { get; }
}

public sealed class PlayerCombat
{
    /// <summary>The most break bursts one run may carry.</summary>
    public const int MaxAegisBreakBursts = 4;

    /// <summary>What the Aegis has absorbed since it was last full or last broke (rule 2).</summary>
    public float AegisAbsorbed { get; }

    /// <summary>Whether the Aegis broke since the last release (rule 3).</summary>
    public bool IsAegisBreakPending { get; }

    /// <summary>What the Aegis had absorbed when it broke — what the pending release will pay out.</summary>
    public float PendingBreakAbsorbed { get; }

    public void AddAegisBreakBurst(AegisBreakBurst burst, object source);
    public void RemoveAegisBreakBurst(AegisBreakBurst burst, object source);

    /// <summary>Fires every registered break burst for the break that is pending, then clears it — rule 4.</summary>
    public void ReleaseAegisBreak(EnemySystem enemies, float now);
}
```

```csharp
namespace Soulvail.Core.Effects;

/// <summary>
/// When the Aegis breaks: a burst of <see cref="Radius"/> for <see cref="AbsorbedMultiple"/> × what it
/// absorbed, shoving <see cref="Knockback"/> metres outward. Registered on take, for the run.
/// </summary>
public sealed class AegisBreakBurst : IEffect
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// radius not finite and above zero; multiple or knockback negative or not finite; both zero.
    /// </exception>
    public AegisBreakBurst(float radius, float absorbedMultiple, float knockback);

    public float Radius { get; }
    public float AbsorbedMultiple { get; }
    public float Knockback { get; }
}

public sealed class AegisBreakBurstHandler : IEffectHandler<AegisBreakBurst>
{
    public AegisBreakBurstHandler(PlayerCombat combat);
    public void Apply(AegisBreakBurst effect, object source);   // combat.AddAegisBreakBurst
    public void Remove(AegisBreakBurst effect, object source);  // combat.RemoveAegisBreakBurst
}

public enum PlayerStat
{
    // ... SkillCooldown (M7-04d), then — appended:

    /// <summary>Aegis refilled per second — <c>Health.ShieldRefillPerSecond</c>. Only a class with an Aegis has one.</summary>
    ShieldRefill,
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct PlayerAttacked
{
    // ... facingXZ, then optional and last:
    public PlayerAttacked(Vector2 facingXZ, float angleDeg = 0f, float range = 0f);
    public readonly float AngleDeg;   // the clamped full angle; 0 = not stated
    public readonly float Range;      // metres; 0 = not stated
}
```

## Behaviour

1. **The refill is a `Stat`, and only an Aegis has one.**
   - `Health.ShieldRefillPerSecond` is `new Stat(shield?.RefillPerSecond ?? 0f)`.
   - `Health.Tick` reads `.Value` where it read `_shield.RefillPerSecond`. A live value that is not
     finite and above zero refills nothing, which is a stuck ring on screen rather than a silence.
   - `PlayerStat.ShieldRefill` resolves to it through a `RequireShield` helper, `RequireKindling`'s
     shape: `Resolve` throws for a class with no Aegis, though every `Health` holds the `Stat` at base
     0. `Has` answers `Health.HasShield`. A borrowed branch naming it is refused to a class with no
     Aegis (M6-08 rule 8's sweep).
   - Unbroken's `PercentMult +1.0` takes the Oathbound's 15/s to 30/s: 30 points in one second
     where it took two.
2. **The tally counts what the Aegis took since it was last full or last broke.**
   - `PlayerCombat.ApplyDamage` adds `result.ToShield` to `AegisAbsorbed` on every call that applied
     something. That is the Aegis alone: `DamageResult` omits the granted pool by design.
   - `PlayerCombat.Tick` sets it to 0 when `Health.Shield >= Health.ShieldMax` after `Health.Tick`.
   - A break sets it to 0 after reading it (rule 3).
   - It is not saved: a resumed run starts at 0, and a stated half-run of Martyr is the cost of not
     bumping the save format for a number the next full Aegis zeroes anyway.
3. **A break is noticed at the one door.**
   - **What a break is:** in `PlayerCombat.ApplyDamage`, after the tally, `Health.HasShield &&
     result.ToShield > 0 && Health.Shield <= 0`.
   - **What it does:** records `IsAegisBreakPending = true` and `PendingBreakAbsorbed`, then zeroes
     the tally. `PlayerCombat.Tick` leaves both alone, so a break from a cone or Charge report
     resolved between ticks (`RunSession.ReportConeHits`, `ReportChargeHits`) is released on the next
     tick's step.
   - **Once per break.** A second hit on an empty Aegis moves nothing into it, so it cannot break
     twice.
   - **It fires nothing here**, for rule 4's reason.
4. **The release is one step of the tick, after the projectiles and before the dead are drained.**
   - `RunSession.Tick` calls `State.Combat.ReleaseAegisBreak(State.Enemies, State.Time)` on the line
     after `State.Projectiles.Tick`. Every pass that can hurt the player this tick is above it, and a
     kill the burst makes is drained, risen and paid on this tick.
   - **Inside it:** it reads `PendingBreakAbsorbed` and clears both it and the flag **first**. Then, for
     each registered burst in the order registered, it calls `Burst(enemies,
     Blackboard.PlayerPosition, radius, absorbedMultiple × absorbed, knockback, now)`.
   - **A break during the release waits a tick.** A Bloater the burst kills can hurt the player, and
     if that breaks an Aegis a zero delay has refilled, it sets the flag after the clear and is
     released on the next tick, never dropped.
   - **Nothing registered means nothing fires**, and the flag still clears.
   - **AR §18.1 gains the row**: *the Aegis-break release sits after the projectile step and above
     `DrainDeaths`, and no burst is called from inside `EnemySystem.ApplyDamage`.* Every pass inside
     the tick that can hurt the player is above it. A break from the fact phase between ticks waits for
     the next release.
5. **Both Keystones taken are two bursts, as authored.** Unbroken and Martyr sit in two branches, so
   a full tree carries both. One break is then a 6 m shove and a 10 m hit, in take order: two rings,
   two `BurstReleased`.
6. **The burst is registered on take and kept for the run.**
   - `AegisBreakBurstHandler.Apply` adds `(effect, source)` to a fixed table of
     `MaxAegisBreakBursts`. A fifth throws: the backstop, with at most two in any tree.
   - `Remove` takes the pair back, which [M7-04k](M7-04k-a-full-tree-still-tempts.md)'s corruption
     would call on a Keystone if one ever carried a Pact. None does (M7-04a rule 2).
   - **Registered for every class**, and inert without an Aegis: `Health.Shield` never reaches the
     break test.
7. **A full circle hits everything in range, and a cone shoves outward.**
   - **The hit test.** `ConeOverlapQuery.IsInCone` answers true for any point within range when
     `angleDeg >= 360`, before the cosine.
   - **The shove.** `ResolveConeHits` pushes each body along the XZ unit vector from the swing's origin
     to it, which it reads through `enemies.Registry.TryGet`. It keeps the origin as
     `_pendingConeOrigin` beside `_pendingConeFacingXZ` at `ThrowCone`. A body within `1e-4` m of the
     origin takes the facing.
   - **The one visible change to a shipped node.** For a 60° cone the radial direction is at most 30°
     off the facing, so Crashing Censure's shove bends outward by that much at the cone's edge and not
     at all in its middle.
   - **Wide Censure is then data**: `Flat +300` lifts any authored angle past the clamp, and
     `PercentMult −0.4` is ×0.6 whatever else pools (CH §3.1's *"at 60 % damage"*).
8. **The swing says how wide it was.** `PlayerAttacked` is published in `TickWeapon` on the tick a
   swing starts, not in `ThrowCone` on its damage frame. That is its own remarks' *"at the moment it
   begins — not when its damage lands"*, and the animation starts from it. At that site a cone weapon
   publishes `PlayerAttacked(facing, ConeAngle(Weapon.ConeAngleDeg.Value), ConeRange(Weapon.Range.Value))`,
   and a projectile swing states neither: 0 and 0, *"not stated"*. The optional arguments keep every
   existing site compiling and meaning what it meant. `PlayerView` reads them in M7-04h.
9. **Nothing allocates on a tick.** The tally and flag are fields, the table is fixed, and the release
   is M7-04b's walk.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Refill_IsAStat` | an Oathbound / `ModifyStat(ShieldRefill, PercentMult, 1.0)` applied / an emptied Aegis past its delay is full in 1 s, not 2 — rule 1 |
| `Refill_AnUnreadableRateRefillsNothing` | the live rate forced to NaN, then −1 / ticks past the delay / the Aegis stays where it was — rule 1 |
| `Refill_OnlyAnAegisHasIt` | a Gravecaller / `Has(ShieldRefill)` / false, and `Resolve` throws — rule 1 |
| `Tally_CountsTheAegisAlone` | a Bulwark grant of 35 standing, then a 50-point hit on a full 30 Aegis / — / `AegisAbsorbed` 15 (35 went to the grant, 15 to the Aegis) — rule 2 |
| `Tally_ClearsWhenFull` | 10 absorbed, then the Aegis refills to full / tick / 0 — rule 2 |
| `Break_IsNoticedOnce` | two hits in one tick, the first emptying the Aegis / — / pending once, `PendingBreakAbsorbed` = what the Aegis took, `AegisAbsorbed` 0 — rule 3 |
| `Break_BetweenTicksIsReleasedNextTick` | a cone report between ticks kills a Bloater whose blast breaks the Aegis / the next `RunSession.Tick` / pending survives `PlayerCombat.Tick`, and the release fires — rules 3, 4 |
| `Tally_IsNotSaved` | 20 absorbed, then a snapshot and a resume / — / `AegisAbsorbed` 0 — rule 2 |
| `Break_AFullGrantIsNotABreak` | a hit the granted pool took whole / — / not pending — rule 3 |
| `Break_FiresNothingInsideTheDamage` | a break from a Bloater's blast inside `EnemySystem.ApplyDamage` / — / no `BurstReleased` until the release step — rules 3, 4 |
| `Release_SitsAfterTheProjectilesAndAboveTheDeathDrain` | a Spitter bolt breaks the Aegis, Martyr registered, a one-hit Husk in 10 m / one `RunSession.Tick` / the Husk dies and its XP is drained on that tick — rule 4 |
| `Release_UnbrokenShovesAndDoesNotHurt` | `AegisBreakBurst(6, 0, 4)`, Husks at 3 m and 7 m / a break, then the release / one intent of 4 m for the near Husk, no `EnemyDamaged` — rules 4, 5 |
| `Release_MartyrHitsForWhatWasAbsorbed` | `AegisBreakBurst(10, 1, 0)`, 30 absorbed / the release / every Husk within 10 m takes 30 — rule 4 |
| `Release_BothKeystonesAreTwoBursts` | both registered / one break / two `BurstReleased`, Unbroken's first — rule 5 |
| `Release_NothingRegisteredStillClears` | no burst / a break and the release / nothing fires, `IsAegisBreakPending` false — rule 4 |
| `Release_ABreakDuringItWaits` | a zero recharge delay, and the release kills a Bloater whose blast breaks the refilled Aegis / the release, then the next tick / pending after the release, released on the next tick — rule 4 |
| `Table_AFifthIsABackstop` | five `Add`s / — / `InvalidOperationException` — rule 6 |
| `Table_RemoveTakesItBack` | one registered, removed / a break and the release / nothing fires — rule 6 |
| `Break_AClassWithoutAnAegisNeverBreaks` | a Gravecaller with a burst registered / hits / never pending — rule 6 |
| `BreakBurst_IsRegisteredForEveryClass` | a run of each class / — / `CanApply(new AegisBreakBurst(…))` true — rule 6 |
| `Cone_ARingHitsTheBodyBehind` | *(in `ConeOverlapQueryTests`)* angle 360, a point dead behind at 5 m of an 8 m range, a facing of length 0.9999 / `IsInCone` / true — rule 7 |
| `Cone_ARingDoesNotReachPastRange` | angle 360, a point at 8.1 m / — / false — rule 7 |
| `Cone_ShovesOutward` | `SwingKnockback` 2, Husks ahead-left and behind / a ring swing resolves / each intent's direction points from the origin to the body — rule 7 |
| `Cone_AFacingCentredHitStillGoesStraight` | a 60° cone, a Husk dead ahead / — / the shove is the facing, as before — rule 7 |
| `WideCensure_IsData` | an Oathbound, `WeaponConeAngle Flat +300` and `WeaponDamage PercentMult −0.4` / a swing / `ConeHitIntent.AngleDeg` 360, each body takes 0.6 × the swing — rule 7 |
| `Swing_SaysHowWideItWas` | Broad Censure taken / a swing starts / `PlayerAttacked.AngleDeg` 90 and `Range` the live reach, published on the start tick, not the damage frame — rule 8 |
| `Swing_AProjectileStatesNeither` | an Emberwright / a shot starts / `AngleDeg` 0, `Range` 0 — rule 8 |
| `Keystones_AllocateNothing` | 10 000 breaks and releases / `AllocationAssert.None` / zero — rule 9 |

**Guard rows are implied, not listed:** the primitive's refusals; the handler's nulls;
`AegisBreakBurstDefinition.ToEffect`'s rewrap.

## Manual verification (Editor / device)

_None this task._ The Keystones are nodes in [M7-04h](M7-04h-the-oathbounds-twenty-seven.md), whose
steps 5 to 7 play each one. This task changes one thing a player could see — rule 7's shove — and
M7-04h's step 2 watches Crashing Censure for it.

## Out of scope

- **Drawing the live swing angle.** M7-04h, which ships the ring.
- **An addressable Aegis maximum.** The trap the enum's remarks name.
- **Saving the tally.** Rule 2.
- **A mark for a break.** `BurstFlashes` draws the burst; a crack on `ShieldRingView` is
  [row 3](../ROADMAP.md#carry-forward-into-m7)'s and M8-01's.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
