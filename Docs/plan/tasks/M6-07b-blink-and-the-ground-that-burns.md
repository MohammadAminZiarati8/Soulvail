# M6-07b — Blink, and the first ground in this game that burns

**Size:** S · **Depends on:** M6-07a · **Branch:** `m6-07b-blink-and-fire-pools`
**Design refs:** CH §3.3; CC §5; GD §2, §7.2, §16.4; AR §9, §18.1, §18.3, §18.4; ADR-0008 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m6) — one device row; [3](../ROADMAP.md#carry-forward-into-m6) — not touched, and the reason is stated

## Goal

`MovementSkillKind.Blink` stops being an enum member: a teleport leaves fire where it left, and
`ZoneSystem` learns to act on somebody other than the player.

## Why the ROADMAP predicted this split, and what the prediction was right about

The row says *"M6-07's Blink leaves a fire pool and `ZoneSystem` heals the player and nothing else"*.
Grepped, that is exactly right and the class says so itself: `ZoneSystem`'s own remarks read

> **The player is the only thing it heals in V1, and the class says so rather than implying it.** It
> takes `Health` — the player's — and not an `EnemySystem`. M5-04's Wights are the first allies and
> are the task that widens this; an enemy-healing zone is an affix (M7-02) and would be **a faction
> on the entry**, decided then.

**This is that task, one milestone early and from the other direction**: not an affix that heals
enemies, a player skill that burns them. The widening the remark names is the one this spec makes,
and the shape it names — *a faction on the entry* — is the shape rule 1 takes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ZoneSystem.cs` | Core | **Substantial.** A zone acts on one side or the other, and a burn is a pulse that damages |
| `Tests/Core/Combat/BurningGroundTests.cs` | Tests.Core | The burn, the walk, the refusals, and the heal left exactly as it was |
| *small edits* | Core, Game | `Core/Content/MovementSkillSpec.cs` — three pool numbers, defaulted and kind-conditional (rule 4); `Core/Combat/ChargeSkill.cs` — three `Stat`s seeded from them (rule 11); `Core/Combat/PlayerCombat.cs` — one branch on the dash's start edge (rule 3); `Core/Run/RunSession.cs` — the zone system gains what it burns with; `Game/Authoring/CharacterDefinition.cs` — three fields; `Data/Characters/Emberwright.asset` — rule 5's numbers |
| *ripple* | Tests.Core | **10 `new ZoneSystem(...)` sites across 3 files** gain two arguments — compiler-guided; `ZoneSystemTests` and `SpawnHealZoneTests` assert the heal path is byte-identical (rule 8). **67 `new MovementSkillSpec(...)` sites across 57 files are untouched** (rule 4) |

`ZoneView` and `ZoneViews` are **not** in the table and must not be edited — rule 9.

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Combat;

/// <summary>
/// Which side of the fight a zone acts on. Two members, and the closed set <c>ShotSide</c> already
/// is: a zone placed by the player either restores the player or damages what is standing in it.
/// </summary>
/// <remarks>
/// <b>Not a dispatch over content and not ADR-0009's ban arriving under a new name.</b> What
/// ADR-0009 refuses is <c>switch (effect.Type)</c> — code branching on which authored *thing* it was
/// handed. This is <c>ShotSide</c>'s shape: a two-member fact about a placement, decided once where
/// the placement is made, branched on in exactly one method (<c>Pulse</c>), and never reachable from
/// an asset. A third member is a design decision with a task behind it, not a row in a table.
/// </remarks>
public enum ZoneSide
{
    /// <summary>Heals the player standing in it. Every zone before this task — M3-11b's Consecrate.</summary>
    HealsThePlayer,

    /// <summary>Damages every living enemy standing in it. CH §3.3's fire pool.</summary>
    BurnsEnemies,
}

public sealed class ZoneSystem
{
    /// <param name="enemies">
    /// What a <see cref="ZoneSide.BurnsEnemies"/> pulse reaches, or <see langword="null"/> for a run
    /// that cannot place one — rule 2. <b>Held rather than handed to <see cref="Tick"/></b>, and
    /// rule 2 counts why.
    /// </param>
    /// <param name="player">
    /// Who a blast set off by a burn would catch — <c>EnemySystem.ApplyDamage</c>'s fourth argument,
    /// which is not optional there. Null with <paramref name="enemies"/> and never without it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// One of <paramref name="enemies"/> and <paramref name="player"/> is null and the other is not.
    /// </exception>
    public ZoneSystem(
        Health player, CombatBlackboard blackboard, IDomainEvents events,
        EnemySystem enemies = null, PlayerCombat combat = null);

    /// <param name="side">What the zone does to whoever is inside — rule 1.</param>
    /// <param name="at">
    /// Where to place it, or <see langword="null"/> for the blackboard's position — rule 3's finding.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Capacity"/> zones already stand, or <paramref name="side"/> is
    /// <see cref="ZoneSide.BurnsEnemies"/> on a system built with nothing to burn (rule 2).
    /// </exception>
    public int Spawn(
        float radius, float duration, float amountPerPulse, float pulseInterval, float now,
        object source,
        ZoneSide side = ZoneSide.HealsThePlayer,
        Vector3? at = null);

    /// <summary>What the zone at <paramref name="index"/> does. For an overlay and the tests.</summary>
    public ZoneSide SideAt(int index);
}
```

```csharp
namespace Soulvail.Core.Content;

public sealed class MovementSkillSpec
{
    // ... the nine arguments that exist, then, defaulted and last (rule 4):
    //     float poolRadius = 0f
    //     float poolDuration = 0f
    //     float poolDamagePerPulse = 0f

    /// <summary>How far a <see cref="MovementSkillKind.Blink"/>'s fire pool reaches. 0 otherwise.</summary>
    public float PoolRadius { get; }

    /// <summary>How long it burns, in simulated seconds. 0 otherwise.</summary>
    public float PoolDuration { get; }

    /// <summary>What one pulse of it takes off. 0 otherwise.</summary>
    /// <remarks>
    /// The interval is <see cref="PoolPulseInterval"/> and is not authored — rule 5. Three fields
    /// rather than four, because the one number a designer never has an opinion about is the one
    /// that decides how many events a second the pool publishes.
    /// </remarks>
    public float PoolDamagePerPulse { get; }

    /// <summary>Simulated seconds between a pool's pulses. Half a second, and a constant — rule 5.</summary>
    public const float PoolPulseInterval = 0.5f;
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class ChargeSkill
{
    // Three Stats seeded from the spec's three numbers, beside Cooldown and Damage — rule 11.

    /// <summary>How far the pool reaches, live. 0 on a movement skill that leaves none.</summary>
    public Stat PoolRadius { get; }

    /// <summary>How long it burns, live.</summary>
    public Stat PoolDuration { get; }

    /// <summary>What one pulse takes off, live.</summary>
    public Stat PoolDamagePerPulse { get; }
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>
/// A burning zone took hit points off somebody. <c>ZoneHealed</c>'s mirror, and a separate struct
/// for its reason: a subscriber that flashes the player is not the one that flashes an enemy.
/// </summary>
public readonly struct ZoneBurned
{
    public ZoneBurned(int id, int enemyId, float amount);

    public readonly int Id;
    public readonly int EnemyId;
    public readonly float Amount;
}
```

## Behaviour

1. **A side is a field on the entry, which is the widening `ZoneSystem` asked for in writing.** One
   more parallel array beside `_radii` and `_heals`, one branch in `Pulse`, and **nothing else in the
   class moves**: the schedule, the catch-up, the capacity, the retirement order, the pulse-before-
   retirement rule and the XZ containment are all unchanged, because none of them is about who is
   standing there. A second system was weighed and refused: it would duplicate the absolute-time
   schedule, the catch-up loop and `MaxPulses`, and the one thing that differs is a call at the
   bottom of one method.
2. **What it burns with is held rather than handed to `Tick`, and the count is the argument.**
   `ProjectileSystem.Tick` takes its `EnemySystem` as a parameter and this class is otherwise shaped
   after it, so the symmetry is tempting — but `ZoneSystem.Tick` has **35 call sites** against the
   constructor's **10 across 3 files**, and a `Tick` argument would have to be threaded through every
   fixture that ticks a heal zone for a feature none of them uses. **The pair is optional and last
   and refused half-set**: a system built with neither cannot place a burn, and `Spawn` says so by
   name rather than placing a zone that burns nobody — which is
   [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 5's direction (*"a mis-wired run would clear
   stages and be paid nothing"*) applied at the door that can see it rather than at the constructor
   that cannot. A row asserts `RunSession` wires both, [M6-06b](M6-06b-four-ordeals-and-two-refusals.md)
   rule 5's arrangement.
3. **A Blink drops its pool where the blink *left*, on the start edge, and the position is passed
   rather than read.** CH §3.3 says the teleport *"leaves a fire pool"* — a thing left behind — which
   is M5-03 rule 6's argument for the corpse decoy word for word: dropping it at the destination
   would put the fire under the player's feet and burn nothing they blinked away from.
   `PlayerCombat.TickCharge` gains one branch beside the Shroudstep's, on the same edge, from the
   same position it already hands the decoy. **`Spawn`'s `at` parameter exists because of what
   grepping found:** `TickCharge` runs near the *top* of `PlayerCombat.Tick` and
   `UpdateBlackboard` near the bottom, so `_blackboard.PlayerPosition` at the drop is **last frame's**
   — 5.7 cm at 3.4 m/s and 60 fps, which is nothing and is still the wrong number. The existing
   `SpawnHealZone` path passes nothing and keeps the blackboard, because a cast *is* resolved after
   `UpdateBlackboard` (the skills step) and that is the reason `ZoneSystem` reads the blackboard at
   all.
4. **The three pool numbers are defaulted and last on `MovementSkillSpec`, and validated against the
   kind.** `new MovementSkillSpec(...)` has **67 sites across 57 files**, so M4-01a rule 4's trade
   applies for the third time: every existing construction keeps meaning what it meant and no shipped
   asset is rewritten. Validation is `DecoyDuration`'s, one field over — on a `Blink` all three must
   be finite and greater than zero; on every other kind all three must be exactly zero. A Charge with
   a pool radius is a forgotten field, and a Blink without one is a teleport, which is the thing CH
   §3.3 says it is not. **`decoyDuration` is untouched and a `Blink` still refuses one**:
   `MovementSkillSpec.Decoy` already wrote *"a third kind is `Blink` (M6-07), which leaves a fire
   pool and not a decoy, so it belongs on the zero side of this line"*, and this is that line being
   kept rather than moved.
5. **The pool's numbers, and the interval is a `const` rather than a fourth field.** CH §3.3 gives
   the pool no numbers at all, so all of these are ours:

   | Field | Value | Why |
   |---|---|---|
   | `poolRadius` | **3** | the orb's blast. One distance the class reads by, rather than two |
   | `poolDuration` | **3** | against a 2.0 s cooldown, so two pools legitimately overlap for a second — `LureSystem.Capacity`'s arithmetic, and `ZoneSystem.Capacity` is 8, which is ample |
   | `poolDamagePerPulse` | **4** | six pulses over three seconds is **24 damage**, which is about one orb. A blink is worth a shot, not a second weapon |
   | `PoolPulseInterval` | **0.5 s**, `const` | Consecrate's interval, so the two zones in the game pulse on the same beat and a view has one cadence to draw |

   **The interval is not authored, and that is a decision about events rather than about feel.** It
   is the one number that decides how many `ZoneBurned`s a second reach the hub — a pool authored at
   a millisecond is `ZoneSystem.MaxPulses`' hang, caught at the door but only after somebody typed
   it. A designer retunes the *damage* and the *duration*, which is what the damage-over-three-seconds
   actually is.
6. **A burn pulse damages every living enemy inside, and the walk is `ProjectileSystem.LandOnEnemies`'
   walk.** `Registry.Alive` with the `IsAlive` test — *registered is not breathing*, and a corpse
   inside a pool takes nothing — squared XZ distance (AR §18.4), and `EnemySystem.ApplyDamage(id,
   amount, now, player)` for each, which is what makes a pool that kills a Bloater set off the blast
   that can catch the Emberwright standing in their own fire. **One `ZoneBurned` per enemy damaged**,
   after the damage, because a subscriber reading the enemy's health inside the event should see the
   burn already applied — `ZoneSystem.Pulse`'s existing ordering.
7. **A burn pulse that reached nobody publishes nothing, and a heal pulse that restored nothing still
   does.** The asymmetry is deliberate and it is the two events meaning different things:
   `ZoneHealed` carries *"what this zone did to the player, who is always there"* — zero is the
   honest answer and the class says so — while a burn has no single subject, so *"nothing was
   standing in it"* is an absence rather than a zero. A `ZoneBurned` per enemy or none.
8. **The heal path is byte-identical and two existing fixtures say so.** `ZoneSide.HealsThePlayer` is
   the default on `Spawn`, `SpawnHealZoneHandler` passes nothing, and every row in `ZoneSystemTests`
   and `SpawnHealZoneTests` runs unedited. **This is the claim that makes the PR safe to merge**, and
   it is asserted rather than implied: `Zone_TheHealPathIsUnchanged` walks a Consecrate through its
   twelve pulses and its expiry and compares against the numbers M3-11b pinned.
9. **The pool is drawn by the view that already exists, in the colour it already uses, and the limit
   is stated rather than papered over.** `ZoneViews` pools a decal off `ZoneSpawned` and `ZoneView`
   draws it in `Palette.Heal`, which is `#22D3EE` — the same value as `Palette.Player`. GD §16.4
   gives cyan to *"the player. Projectiles, dash trail, **safe things**"*, and a pool that cannot hurt
   the player is a player thing that is safe for them, so **no `Palette` member is added and
   `ZoneSpawned` gains no field.** The cost: **a healing circle and a burning circle look the same**,
   and one run can hold both — an Emberwright that borrows the Oathbound's *Judgment* branch at CH
   §5.4's half-tree moment owns Consecrate. That is a **decal texture** rather than a colour, so it
   is M7's art pass, and it is written here so nobody reads this diff and concludes the view was
   forgotten. Adding a `ZoneKind` to the event today would be a field nothing reads for a milestone,
   which is the thing [M6-06a](M6-06a-what-an-ordeal-is.md) rule 6 only accepts when the reader is
   one task away.
10. **Nothing here allocates.** One more `ZoneSide[]` sized at `Capacity` at construction, one branch
    per pulse, and a walk bounded by the enemy capacity that runs at most twice a second per pool
    rather than per frame (AR §14).
11. **The three pool numbers become `Stat`s on `ChargeSkill` the day they are authored, and nothing
    addresses them yet.** ADR-0008's rule and `MovementSkillSpec`'s own remarks — *"the live number is
    `ChargeSkill.Cooldown`, a `Stat` seeded from `Cooldown`, and every '−15 % dash cooldown' in the
    game lands on that rather than here"* — so a pool authored as three raw floats would be the one
    movement-skill number a node could never reach. They sit beside `Cooldown` and `Damage`, are read
    at the moment of the drop rather than cached, and are **floored at the read** because a `Stat`
    clamps nothing: a radius, a duration or a damage driven to zero or below means *no pool at all*
    rather than a `ZoneSystem.Spawn` that throws out of a dash. **No `PlayerStat` member is added
    here** — M3-12a's sequence, and [M6-08](M6-08-emberwright-tree-v1.md)'s Ash branch is where two of
    the three acquire an address; the third is named on `PlayerStat`'s own *"what is still
    deliberately not here"* list with **M7-04** beside it.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Zone_TheHealPathIsUnchanged` | a Consecrate-shaped zone / twelve pulses and its expiry / every number M3-11b pinned, and `SideAt` is `HealsThePlayer` — rule 8 |
| `Zone_ASideIsRequiredToBeBuiltFor` | a system with no enemies / `Spawn(..., BurnsEnemies)` / `InvalidOperationException` naming the wiring, **and no zone placed** — rule 2 |
| `Zone_TheConstructorRefusesHalfAPair` | enemies without a combat, then the reverse / — / throws either way — rule 2 |
| `Burn_TakesHitPointsOffWhatIsStandingInIt` | two Husks inside, one outside / one pulse / two `EnemyDamaged` of 4, one `ZoneBurned` each, the third untouched — rule 6 |
| `Burn_SkipsACorpse` | one Husk dead this tick and still registered / a pulse / nothing, and no `ZoneBurned` — rule 6, `LandOnEnemies`' own rule |
| `Burn_ReachingNobodyIsSilent` | an empty pool / six pulses / no event at all — rule 7 |
| `Burn_AHealPulseOnAFullPlayerStillPublishes` | a heal zone, player at full / a pulse / one `ZoneHealed(id, 0)` — rule 7's other half, unchanged |
| `Burn_SetsOffABloater` | a Bloater killed by a pulse, the player inside the pool / — / the player takes the blast — rule 6, `EnemySystem.ApplyDamage`'s fourth argument doing its job |
| `Burn_DoesNotTouchThePlayer` | the player standing in their own pool with no Bloater near / six pulses / hit points unmoved — rule 1, and the thing a playtest will ask about first |
| `Burn_CatchesUpRatherThanSkipping` | a pool and a 1.6 s tick / — / three pulses land, not one — `ZoneSystem`'s absolute schedule, asserted on the new side |
| `Burn_PulsesOnTheTickItExpires` | a 3 s pool at a 0.5 s interval / ticked to exactly 3.0 / the sixth pulse lands **and then** it retires — `Zone_PulsesOnTheTickItExpires`' claim on the new side |
| `Blink_LeavesAPoolWhereItLeft` | an Emberwright-shaped spec, the blink starting at (5, 0, 5) / — / one `ZoneSpawned` at (5, 0, 5) with radius 3, **not** at the destination — rule 3 |
| `Blink_UsesThisFramesPositionNotTheBlackboards` | a player that moved this tick / a blink / the pool is at the snapshot's position and **not** at the blackboard's — rule 3's finding, which is invisible when it works |
| `Blink_StillDodges` | the same / — / the i-frames, the `ChargeIntent` and the `ChargeStarted` are the Charge's, distance 10, duration 0.05 |
| `Blink_DropsNoDecoy` | the same / — / `LureSystem.Count` 0 — rule 4, `MovementSkillSpec.Decoy`'s line unmoved |
| `Charge_AndShroudstepLeaveNoPool` | the shipped Oathbound and Gravecaller / a dash each / `ZoneSystem.Count` 0 — rule 4 |
| `Blink_TwoPoolsOverlapLegitimately` | two blinks 2.0 s apart / — / two zones stand for a second, both pulsing — rule 5's arithmetic |
| `Spec_ThePoolNumbersAreKindConditional` | a `Charge` with a pool radius; a `Blink` with a zero duration / — / throws in both directions, naming the field — rule 4 |
| `Spec_TheShippedTwoAreUnchanged` | the nine-argument constructor as 67 sites call it / — / all three pool numbers zero and nothing else moved — rule 4 |
| `Emberwright_CarriesThePoolNumbers` | `Emberwright.asset` / converted / 3 / 3 / 4, and `PoolPulseInterval` is 0.5 — rule 5 |
| `Pool_TheThreeNumbersAreStats` | `typeof(ChargeSkill)` / reflection / three `Stat`s seeded from the spec, and **no `PlayerStat` member names any of them** — rule 11, M3-12a's sequence |
| `Pool_AStatDrivenToZeroLeavesNoPool` | a modifier taking `PoolRadius` to 0, then to −2 / a blink / no zone, no throw, and the dash is otherwise unchanged — rule 11 |
| `Pool_ReadsTheStatAtTheDrop` | a `+50 %` modifier on `PoolDuration` applied between two blinks / — / the second pool lasts 4.5 s and the first is unaffected — rule 11 |
| `Pool_IsWorthAboutOneOrb` | the authored pool over its life against the authored orb / — / 24 against 17, and the row's message names both assets — rule 5, so a retune of either reddens it |
| `Run_TheZoneSystemIsWiredToBurn` | a live `RunSession` / reflection / `ZoneSystem` holds the run's `EnemySystem` and its `PlayerCombat` — rule 2 |
| `View_IsNotEdited` | `ZoneView`, `ZoneViews` and `ZoneSpawned` / — / no `ZoneSide`, no new field, no second colour — rule 9, pinned so the limit is a decision |
| `Burn_AllocatesNothing` | 100 000 pulses over 28 bodies / `AllocationAssert.None` / zero — rule 10 |

**Guard rows are implied, not listed:** every existing `ZoneSystem` guard firing unchanged, a
non-finite `at`, and a non-finite or non-positive pool number refused at `MovementSkillSpec`'s door.

## Manual verification (Editor / device)

1. **[Editor]** Play an Emberwright and blink into three Husks. *Expected: a cyan circle where you
   left, the three take damage in six visible steps over three seconds, and you take none standing
   in it.*
2. **[Editor]** Blink twice in four seconds. *Expected: two circles overlapping for about a second,
   and an enemy in both takes both — rule 5.*
3. **[Editor]** Play an **Oathbound** through a stage with Consecrate taken. *Expected: identical to
   the build before this PR — the zone heals, pulses twelve times and expires. Rule 8, looked at.*
4. **[Editor]** Blink onto a Bloater and kill it with the pool. *Expected: the blast catches you —
   rule 6, which is the one interaction most likely to be read as a bug.*
5. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**: a healing circle and a
   burning circle are the same cyan at the same radius (rule 9). Whether a player can tell which one
   they are standing in — and whether it matters, given one is harmless to them — is a six-inch
   question the Editor's `Screen.dpi` of 120 cannot ask ([Traps §9](../../Traps.md)).

## Out of scope

- **A decal that looks like fire.** Rule 9. **M7**'s art pass; the pool ships as the circle the
  project already draws.
- **A `Palette` member for burning ground.** Rule 9, and GD §16.4's cyan row is why — the same
  refusal [M6-05b](M6-05b-the-offer-that-rolls-one.md) rule 8 makes for Pact frames.
- **An effect primitive that spawns one.** A node that places a burning zone is
  [M6-08](M6-08-emberwright-tree-v1.md)'s `SpawnBurnZone`; the only caller of `BurnsEnemies` after
  this task is the Blink.
- **A zone that slows, or one an enemy places.** `ZoneSide` has two members and a third is a task.
  M7-02's affixes are where an enemy-placed zone would land, which is the case `ZoneSystem`'s remarks
  named.
- **Kindling counting a pool pulse.** [M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md) rule 6
  refuses it and pins it before this task makes it reachable.
- **Saving a pool.** It joins *"cooldowns, granted shields, live zones"* on the by-ruling-unsaved
  list, unchanged — M5-03 rule 9, and three seconds of fire is not a format bump.
- **The Emberwright's Veilrot relationship.** [M6-07c](M6-07c-what-each-class-does-with-the-veil.md).

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
