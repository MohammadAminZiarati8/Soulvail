# M7-04d — An Upgrade that adds to a cast, and a clock for every skill

**Size:** M · **Depends on:** M7-04c · **Branch:** `m7-04d-an-upgrade-that-adds-to-a-cast`
**Design refs:** CH §4, §4.1; GD §13.1; AR §11.1, §11.2, §18.1; ADR-0006, ADR-0008, ADR-0009 · **Ledger rows:** none

## Goal

An Upgrade can change what an owned Active *does* — *"Consecrate also burns what stands in it"* —
rather than only how often. And one number moves every Active's cooldown at once.

## Why, counted

CH §4 names three kinds of Upgrade: *"lower cooldown, more damage, an added effect."* The build has the
first alone. The three V1 classes' Upgrades are each `ModifySkillCooldown(parent, PercentMult, −0.25)`; the
Ranger's six are its chains' gated stat nodes.
M7-04h to M7-04j author about thirteen Upgrades. As cooldown cuts they are filler by GD §13.1's
*"every node must change how you play"*, and two or three on one skill reach CH §4.1's 40 % floor.

**"More damage" is refused here and costs nothing.** Every cast verb but one authors flat numbers on
the effect, so *more damage to one skill* would be a per-skill multiplier read inside six handlers. The
one exception is M7-04b's `Burst`, which reads the weapon and already grows with every weapon node.

**"An added effect" is one mechanism**, because a cast is already a list. The runner walks
`ActiveSpec.OnCast` in `ApplyCast`, so an Upgrade that appends to *this run's* list for one skill is
the whole of it.

**The clock for every skill is the Arcana branch's missing verb.** CH §3.3 calls Arcana *"actives,
cooldowns"*, and the only cooldown a node can move today is one named skill's.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/ExtendCast.cs` | Core | **New.** The primitive and its handler |
| `Game/Authoring/ExtendCastDefinition.cs` | Game | **New.** A skill reference and an effect reference |
| `Core/Combat/SkillRunner.cs` | Core | **Substantial.** Extensions per skill, pending ones for a skill not yet owned, and the scale on every cooldown |
| `Tests/Core/Effects/ExtendCastTests.cs` | Tests.Core | **New.** Extensions, their source, their arrival, their removal; the scale |
| *small edits* | Core, Game | `Core/Combat/PlayerCombat.cs` — `SkillCooldown`, a `Stat` at base 1 (rule 7); `Core/Effects/PlayerStat.cs` — `SkillCooldown` appended, a `Resolve` line and `Has` true (rule 7); `Core/Run/RunSession.cs` — the runner is handed `combat.SkillCooldown`, and one `Register` line (rules 6, 7); `Tests/Game/Authoring/ContentValidationTests.cs` — `Content_EveryCastEffectEndsOnItsOwn` (rule 5) |
| *ripple* | Tests.Core | `ModifyStatTests.Stats_ResolveEveryMember` gains the member; `StatBlockTests`' ordinal list and `PlayerStatCoverageTests`' count move by one. Every `new SkillRunner(...)` site is untouched: the scale is optional and last |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Effects;

/// <summary>
/// One more effect on one named skill's cast, for this run: <em>"Consecrate also burns"</em>.
/// </summary>
public sealed class ExtendCast : IEffect, IWrapsEffect
{
    /// <exception cref="ArgumentException">
    /// <paramref name="skillId"/> is a default id; <paramref name="inner"/> is itself an
    /// <see cref="ExtendCast"/> — a cast that grew its own cast list every time it fired.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    public ExtendCast(ContentId skillId, IEffect inner);

    public ContentId SkillId { get; }
    public IEffect Inner { get; }
}

public sealed class ExtendCastHandler : IEffectHandler<ExtendCast>
{
    public ExtendCastHandler(SkillRunner runner);
    public void Apply(ExtendCast effect, object source);   // runner.Extend(effect, source)
    public void Remove(ExtendCast effect, object source);  // runner.Unextend(effect, source)
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class SkillRunner
{
    /// <summary>The most extensions one owned skill may carry.</summary>
    public const int MaxExtensionsPerSkill = 4;

    /// <summary>The most extensions waiting for skills not yet owned.</summary>
    public const int MaxPendingExtensions = 16;

    // ... after `veilrot`, optional and last:  Stat cooldownScale = null
    public SkillRunner(EffectRegistry effects, CombatBlackboard blackboard, IDomainEvents events,
                       Veilrot veilrot = null, Stat cooldownScale = null);

    /// <summary>
    /// Adds <paramref name="extension"/>'s inner effect to its skill's cast, now or when the skill arrives.
    /// <paramref name="owner"/> is the node that took it, and is what <see cref="Unextend"/> matches.
    /// </summary>
    /// <exception cref="InvalidOperationException">The skill already carries MaxExtensionsPerSkill, or MaxPendingExtensions are waiting.</exception>
    public void Extend(ExtendCast extension, object owner);

    /// <summary>Takes back the pair <c>(extension, owner)</c>, live or pending.</summary>
    public void Unextend(ExtendCast extension, object owner);

    /// <summary>How many extensions are waiting for a skill the player does not own yet.</summary>
    public int PendingExtensions { get; }

    /// <summary>How many extensions the skill at <paramref name="index"/> carries.</summary>
    public int ExtensionCountOf(int index);
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class PlayerCombat
{
    /// <summary>A multiplier on every Active's cooldown, base 1 — <c>PlayerStat.SkillCooldown</c>.</summary>
    public Stat SkillCooldown { get; }
}

namespace Soulvail.Core.Effects;

public enum PlayerStat
{
    // ... VolleyDamage, then — appended, never inserted:

    /// <summary>Every Active's cooldown multiplier — <c>PlayerCombat.SkillCooldown</c>. Base 1. Every class has one.</summary>
    SkillCooldown,
}
```

## Behaviour

1. **An extension is cast after the skill's own list, with the `ExtendCast` itself as source.**
   - `ApplyCast` walks `Active.OnCast` with the `ActiveSpec` as source, as today. Then it walks the
     entry's extensions in the order they were added, applying each `Inner` with **its own
     `ExtendCast` instance** as the source.
   - **Why not the `ActiveSpec`.** An extension that is an `Empower` on the stat the skill's own
     `Empower` names would share its source, and M7-04c rule 2's refresh would take the skill's own
     buff off.
   - **Why not the Upgrade's `SkillSpec`.** That is the source `SkillTree.Record` applied the node's
     take effects and its Pact's with. An extended `Empower(MoveSpeed, …)` on a node whose Pact
     downside is `MoveSpeed −0.10` would `RemoveAll` the downside at its first refresh.
   - **The `ExtendCast` is one immutable object per authored asset**, never confused with another
     node's, and a clean extension and its Pact's are two instances. That is M3-05 rule 7's
     *"whoever owns the effect"*, one level in: the extension owns what it casts.
2. **A bought cast carries them too.** `TryBuy` calls `ApplyCast`, which walks the extensions. A cast
   bought through a cooldown is a cast, M6-11b's finding.
3. **An extension for a skill not yet owned waits, and lands when it arrives.**
   - It is `ModifySkillCooldownHandler`'s rule 3, for its reason. On a resume `SkillTree.Restore`
     replays an Upgrade before `RunSession.Start`'s loop hands the runner its Actives (AR §18.1's
     restore row), so an extension always arrives first there.
   - **The runner holds the table itself**, because `WatchArrivals` has one watcher and it is taken.
     `Add` spends the pending entries for the new skill after `_count++` and before `_onAdded`.
   - **The capacities are backstops that throw**, like `SkillRunner.Add`'s. `MaxPendingExtensions` is
     16. `MaxExtensionsPerSkill` is 4 against a design that puts at most two Upgrades on one skill.
4. **`Unextend` takes back exactly the pair `(extension, owner)`**, live or pending, by reference.
   Nothing in M7-04 removes a node except [M7-04k](M7-04k-a-full-tree-still-tempts.md)'s corruption,
   which swaps a clean extension for its Pact's, and that is this door. **An inner buff already
   running is left to run out.** Its source is the clean `ExtendCast`, so its expiry takes off its
   own modifier and never the Pact extension's, which is a different instance.
5. **What may be inside, asserted over content rather than refused in core.**
   - A cast list runs its effects on every cast. So an effect that stays on — `ModifyStat`,
     `ModifySkillCooldown`, `KnockbackOnSwing` — stacks one more modifier per cast for ever.
     `ActiveSpec` has never refused that, and no Active has ever authored it.
   - `Content_EveryCastEffectEndsOnItsOwn` holds both doors to one list: every `OnCast` entry and
     every `ExtendCast.Inner` of every shipped skill is a `Burst`, `Empower`, `GrantShield`,
     `SpawnHealZone`, `SpawnBurnZone` or `RaiseMinions`.
   - A new cast primitive joins the list by decision.
   - Core refuses the one shape that is wrong at any number: an extension inside an extension.
6. **`ExtendCastHandler` is registered for every class**, on the line after
   `ModifySkillCooldownHandler`'s, beside the runner it holds. **What it carries is swept through
   `IWrapsEffect`** (M7-04c rule 5), so a `RaiseMinions` extension on a minionless class refuses the
   tree at `Start`, and on a borrowed branch draws it dead.
7. **`SkillCooldown` scales every Active's cooldown, under the same floor.**
   - `EffectiveCooldownOf(index)` becomes `CooldownRules.Effective(base, _cooldowns[index].Value ×
     scale)`. `scale` is `cooldownScale.Value`, or 1 for a runner built without one.
   - The floor is still 40 % of the authored base, and NaN is still survived by `CooldownRules`' own
     spelling.
   - **Multiplicative, as CH §4.1 says**: −15 % on every skill times −25 % on one is ×0.6375.
   - **The dash is not an Active and is not scaled.** `ChargeSkill.Cooldown` is `MovementSkillCooldown`'s.
   - **Why the `Stat` lives on `PlayerCombat`.** `PlayerStats` is built before `Veilrot`, which takes
     it, and the runner takes `Veilrot`. So the runner cannot be handed to `PlayerStats`, and the
     number lives where `PlayerStats` can already reach, like `HealPerKill`, `SwingKnockback` and
     `FireWhileMoving`.
8. **`ExtendCastDefinition` names a skill and an effect.**
   - `_skill` is a `SkillDefinition` reference and its id is read; `_effect` is an `EffectDefinition`
     reference, converted.
   - `ToEffect` rewraps a refusal naming the asset.
   - `OnValidate` warns on either field empty, and on a named skill that is not an Active.
9. **Nothing allocates on a tick.** Fixed arrays of `MaxActives × MaxExtensionsPerSkill` pairs and
   `MaxPendingExtensions` pairs, allocated in the constructor; the cast walk is an index loop.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Extend_AddsToTheCast` | an owned Active casting a heal zone, then `ExtendCast(it, SpawnBurnZone(3.5, 6, 3))` applied / one cast / one heal zone and one burn zone spawned, in that order — rule 1 |
| `Extend_TheSourceIsTheExtension` | the same with a spying handler / a cast / the zone's source is the `ActiveSpec`, the burn's is the `ExtendCast` — rule 1 |
| `Extend_ABuffNeverTakesTheNodesOwnModifier` | an Upgrade taken as a Pact with a `MoveSpeed −0.10` downside, extending with `Empower(MoveSpeed, PercentAdd, 0.4, 4)` / three casts and an expiry / the −0.10 is on throughout — rule 1 |
| `Extend_TwoBuffsOnOneStatAreTwo` | an Active casting `Empower(FireRate, PercentAdd, 0.4, 6)`, extended by `Empower(FireRate, PercentAdd, 0.3, 4)` / one cast / ×1.7 for 4 s, then ×1.4 to 6 s — rule 1 |
| `Extend_ABoughtCastCarriesIt` | an Emberwright with the meter full, an extended Active on cooldown, trigger met / `TryBuy` / the extension applied, `CastBought` published — rule 2 |
| `Extend_WaitsForItsSkill` | `ExtendCast` applied for an unowned skill / then `Add` / `PendingExtensions` 1, then 0 and `ExtensionCountOf` 1 — rule 3 |
| `Extend_AResumedUpgradeLandsOnItsSkill` | a snapshot taking an Active, then its extending Upgrade / `Start` / one extension on the Active, nothing pending — rule 3 |
| `Extend_RemoveTakesBackLiveAndPending` | one live and one pending from one owner / `Remove` both / zero and zero; another owner's extension stays — rule 4 |
| `Extend_ARunningBuffOutlivesItsRemoval` | an extended buff running, then the extension removed and its Pact's added / the clean buff's expiry / the Pact extension's next buff is untouched — rule 4 |
| `Extend_TheCapacityIsABackstop` | a fifth extension on one skill; a seventeenth pending / — / `InvalidOperationException` naming the skill — rule 3 |
| `Extend_RefusesAnExtensionOfAnExtension` | `new ExtendCast(id, new ExtendCast(id, burst))` / — / `ArgumentException` — rule 5 |
| `Extend_AMinionExtensionOnAMinionlessTreeRefusesTheRun` | an Oathbound tree with `ExtendCast(x, RaiseMinions(1, 2))` / `Start` / refused naming `ExtendCast` and `RaiseMinions` — rule 6 |
| `Extend_ABorrowedMinionExtensionIsDead` | a lender branch whose Upgrade carries `ExtendCast(x, RaiseMinions(1, 2))` / `BranchesOf` for an Oathbound / not borrowable, `ui.splash.refused.primitive` — rule 6 |
| `Extend_IsRegisteredForEveryClass` | a run of each class / — / `CanApply` true — rule 6 |
| `Scale_MovesEveryActive` | two Actives of 10 s and 20 s, `SkillCooldown PercentAdd −0.15` / `EffectiveCooldownOf` / 8.5 and 17 — rule 7 |
| `Scale_MultipliesWithTheSkillsOwnCut` | a 10 s Active cut `PercentMult −0.25`, scale −0.15 / — / 6.375 — rule 7 |
| `Scale_StillFloorsAtFortyPercent` | scale ×0.3 on a 10 s Active / — / 4 — rule 7 |
| `Scale_LeavesTheDashAlone` | the scale moved / — / `ChargeSkill`'s effective cooldown unchanged — rule 7 |
| `Scale_EveryClassHasIt` | a run of each class / `PlayerStats.Has(SkillCooldown)` / true, and `Resolve` is `PlayerCombat.SkillCooldown` — rule 7 |
| `Content_EveryCastEffectEndsOnItsOwn` | *(in `ContentValidationTests`)* every shipped `SkillDefinition` / converted / every `OnCast` entry and every `ExtendCast.Inner` is one of rule 5's six types — rule 5 |
| `Extend_AllocatesNothing` | 10 000 casts of an Active with two extensions / `AllocationAssert.None` / zero — rule 9 |

**Guard rows are implied, not listed:** the handler's nulls; `ExtendCastDefinition.ToEffect`'s
rewrap; a null `cooldownScale` reading as 1.

## Manual verification (Editor / device)

_None this task._ The first extension a player can take is [M7-04h](M7-04h-the-oathbounds-twenty-seven.md)'s
Hallowed Ground; its step 4 watches Consecrate burn.

## Out of scope

- **Per-skill damage.** The *Why* above: six handlers would each read a multiplier, and `Burst` already
  scales with the weapon.
- **An Upgrade that removes something from a cast.** Nothing in the design asks for one.
- **Drawing an extension.** Each inner effect draws itself — a zone through `ZoneViews`, a burst
  through `BurstFlashes`, a shield through `BulwarkView`.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
