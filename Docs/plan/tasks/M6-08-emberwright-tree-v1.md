# M6-08 — The Emberwright tree v1, two Actives, and the five addresses its nodes need

**Size:** M (5 counted files, at the [sizing rule](../ROADMAP.md#how-to-read-this)'s cap — the split line is named below) · **Depends on:** M6-07c · **Branch:** `m6-08-emberwright-tree-v1`
**Design refs:** CH §3.3, §4, §4.1, §4.2, §5, §5.2, §5.4; GD §6.2, §12.4, §13.1; AR §10.1, §11.5, §18.2; ADR-0006, ADR-0008, ADR-0009, ADR-0012 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m6) — rule 11 is the third class's answer to the row's cause; [7](../ROADMAP.md#carry-forward-into-m6) — twenty-seven strings

## Goal

The third class has twelve nodes a player can be offered, **two** of them Actives, and the five
numbers M6-07a and M6-07b authored become addresses a node can name.

## The tree

**Three branches, two tiers of two, twelve nodes, no Keystone** —
[M5-06b](M5-06b-gravecaller-tree-v1.md)'s shape exactly, which is
[M3-12c](M3-12c-oathbound-tree-v1.md)'s. Tier 2 needs one node taken in its branch.

### Ember — the orb, and the heat that builds

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.emberwright.ember-touch` | Ember Touch | Passive | `ModifyStat(WeaponDamage, PercentAdd, +0.15)` | stat |
| 1 | `skill.emberwright.stoked-coals` | Stoked Coals | Passive | `ModifyStat(KindlingPerStack, Flat, +0.01)` | **the signature** |
| 2 | `skill.emberwright.quickened-flame` | Quickened Flame | Passive | `ModifyStat(FireRate, PercentAdd, +0.12)` | stat |
| 2 | `skill.emberwright.long-burn` | Long Burn | Passive | `ModifyStat(KindlingMaxStacks, Flat, +10)` | **the signature** |

### Arcana — the actives, and the clock

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.emberwright.emberfall` | Emberfall | **Active** | `SpawnBurnZone(4, 4, 6)` · 14 s · `EnemiesWithin8m AtLeast 3` | skill |
| 1 | `skill.emberwright.arcane-haste` | Arcane Haste | Passive | `ModifyStat(MovementSkillCooldown, PercentAdd, −0.20)` | **rule-adjacent** |
| 2 | `skill.emberwright.deep-well` | Deep Well | **Upgrade** of Emberfall | `ModifySkillCooldown(emberfall, PercentMult, −0.25)` | **rule** |
| 2 | `skill.emberwright.ashen-lore` | Ashen Lore | Passive | `ModifyStat(XpGain, PercentAdd, +0.15)` | **rule** |

### Ash — the ground you leave behind

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.emberwright.cinder-nova` | Cinder Nova | **Active** | `SpawnBurnZone(6, 1.5, 10)` · 18 s · `EnemiesWithin6m AtLeast 3` | skill |
| 1 | `skill.emberwright.scorching-ground` | Scorching Ground | Passive | `ModifyStat(PoolDamage, PercentAdd, +0.50)` | **the Blink** |
| 2 | `skill.emberwright.lingering-ash` | Lingering Ash | Passive | `ModifyStat(PoolDuration, PercentAdd, +0.50)` | **the Blink** |
| 2 | `skill.emberwright.emberheart` | Emberheart | Passive | `ModifyStat(MaxHp, Flat, +15)` | stat |

**The mix, counted honestly: six stat lines, three rule changes, one Upgrade, *two* Actives, and four
of the twelve aimed at things only this class has.** Against CH §4's shares — ~45 % Passive, ~25 %
Active, ~25 % Upgrade — that is **16.7 % Active** against the Gravecaller's 8.3 % and the Oathbound's
16.7 %. It is still under the target and the reason is rule 3's, not an authoring miss.

## Why two Actives ship where M5-06b shipped one, and why it is still not three

The ROADMAP's row says *"Emberwright tree v1 + **actives**"*, plural, and
[M5-06b](M5-06b-gravecaller-tree-v1.md) rule 3 is the standing account of why only one shipped there:
*"Exhume ships because M5-06a built the one verb it needs … Tether and Rot Nova do not, because each
wants an effect primitive nobody has built."*

**Here one primitive buys two Actives**, because `SpawnBurnZone` is authored data rather than a
mechanism: Emberfall is a wide slow burn and Cinder Nova is a tight fast one, and the difference is
three numbers on two assets. That is exactly what ADR-0006 exists for, and it is the first time in
this project that a second Active has cost nothing.

**A third is still refused.** CH §3.3's three Keystone names — **Wildfire**, **Overflow** and
**Scorched Vail** — are reserved and unauthored, M5-06b rule 2 and M3-12c rule 5's ruling: taking a
Keystone's name for a lesser node makes the Keystone feel like a repeat when M7-04 authors it. And
each of the three needs a mechanism rather than a number: Wildfire rewrites what
[M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md) rule 7's reset *does* (half the stacks rather
than all), Overflow needs a cast counter with a free-and-instant branch on
[M6-07c](M6-07c-what-each-class-does-with-the-veil.md)'s paid-cast path, and Scorched Vail needs
burning to **spread**, which is a zone that places zones. **All three are M7-04's**, with the other
six Keystones and the remaining forty-five nodes.

## Five addresses, and M3-12a is the precedent for all five at once

`PlayerStat`'s own remarks record the last time this happened: *"**Five of those arrived at
M3-12a**, which is M3-05 rule 5's promise kept: the numbers it deferred became addressable in the
task whose nodes wanted them."* This is that sentence a second time, and the numbers were deferred
two tasks ago on purpose ([M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md) rule 4,
[M6-07b](M6-07b-blink-and-the-ground-that-burns.md) rule 11):

| Member | Lives on | Named by |
|---|---|---|
| `KindlingPerStack` | `Kindling.PerStack` | Stoked Coals |
| `KindlingMaxStacks` | `Kindling.MaxStacks` | Long Burn |
| `PoolDamage` | `ChargeSkill.PoolDamagePerPulse` | Scorching Ground |
| `PoolDuration` | `ChargeSkill.PoolDuration` | Lingering Ash |
| — `PoolRadius` | `ChargeSkill.PoolRadius` | **nobody**, and it is left off |

**The sixth is deliberately not added**, which is `PlayerStat`'s own *"what is still deliberately not
here"* list being kept rather than quietly emptied: no node in this tree widens a pool, an address
with no node is `PlayerStat.ContactDamage`'s mistake made on purpose, and **M7-04** is named beside
it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/SpawnBurnZone.cs` | Core | The primitive both Actives cast, and its handler |
| `Core/Effects/PlayerStat.cs` | Core | **Substantial.** Four addresses, and `Has` learning to answer for *this run* rather than for the enum (rule 8) |
| `Game/Authoring/SpawnBurnZoneDefinition.cs` | Game | The Inspector half — `SpawnHealZoneDefinition`'s shape |
| `Tests/Core/Effects/SpawnBurnZoneTests.cs` | Tests.Core | The primitive, the handler, the registration, and the refusals |
| `Tests/Game/Authoring/EmberwrightTreeTests.cs` | Tests.Game | The twenty-five assets pin their own numbers — `GravecallerTreeTests`' shape |
| `Data/Skills/Emberwright/*.asset` ×12 · `Data/Effects/Emberwright/*.asset` ×12 · `Data/Trees/Emberwright.asset` | — | The nodes, their effects, the tree. Assets — listed, not counted |
| *small edits* | Core, Game | `Core/Progression/SplashFlow.cs` — `TryRefusal`'s third sweep (rule 8); `Core/Run/RunSession.cs` — one `Register` line and the four addresses; `Game/Authoring/EffectDefinition.cs` — the new kind; `Prefabs/Composition/BootScope.prefab` — `_skills` gains twelve and `_trees` one; `Data/Localisation/English.asset` — **twenty-seven** rows (rule 10) |
| *ripple* | Tests.Core, Tests.Game | `SkillTreeValidationTests` — M6-07a's named skip and `TheTreelessClass_IsStillTreeless` **deleted**, M5-06b's *As built* precedent, and its `Registry()` helper gains the seventh primitive (rule 5); `SkillAuthoringTests`' two array counts move 24 → 36 and 2 → 3; `ContentValidationTests`' floors move with them; `PlayerStatsTests`' `Stats_ResolveEveryMember` gains four rows and a second named exception |

**If this grows past five, the split line is [M5-06a](M5-06a-what-a-legion-node-may-reach.md)/b's:
the primitive and the addresses against the twelve nodes.** Written here so it is a cut made before
the branch rather than after.

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Effects;

/// <summary>
/// Places a patch of ground that burns whatever is standing in it — CH §3.3's Ash branch, and
/// <c>SpawnHealZone</c>'s mirror. The only new effect primitive this milestone ships.
/// </summary>
/// <remarks>
/// <b>A second primitive rather than a side flag on <see cref="SpawnHealZone"/>.</b> That type is
/// named for what it does and is authored on four shipped assets; giving it a
/// <c>ZoneSide</c> would make every existing <c>SpawnHealZoneDefinition</c> carry a field whose
/// default decides whether a Consecrate heals or burns, which is one typo away from a heal zone
/// that kills the player's own army. Two types, two definitions, two handlers, one
/// <c>ZoneSystem</c>.
/// </remarks>
public sealed class SpawnBurnZone : IEffect
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any argument is not a finite number greater than zero, or
    /// <paramref name="duration"/> over <c>MovementSkillSpec.PoolPulseInterval</c> exceeds
    /// <c>ZoneSystem.MaxPulses</c>.
    /// </exception>
    public SpawnBurnZone(float radius, float duration, float damagePerPulse);

    public float Radius { get; }
    public float Duration { get; }
    public float DamagePerPulse { get; }
}

/// <summary>
/// <c>SpawnHealZoneHandler</c>'s mirror, and it is registered <b>unconditionally</b> — rule 5.
/// </summary>
public sealed class SpawnBurnZoneHandler : IEffectHandler<SpawnBurnZone>
{
    public SpawnBurnZoneHandler(ZoneSystem zones, SimulatedClock clock);

    public void Apply(SpawnBurnZone effect, object source);

    /// <summary>Does nothing. A zone owns its own life — <c>ZoneSystem</c>'s rule 9.</summary>
    public void Remove(SpawnBurnZone effect, object source);
}
```

```csharp
namespace Soulvail.Core.Effects;

public enum PlayerStat
{
    // ... the twelve members that exist, then — appended, never inserted, because
    // ModifyStatDefinition serialises this by ordinal:

    /// <summary>What one Kindling stack is worth — <c>Kindling.PerStack</c>. Base 0.02.</summary>
    KindlingPerStack,

    /// <summary>How many stacks count — <c>Kindling.MaxStacks</c>. Base 30.</summary>
    KindlingMaxStacks,

    /// <summary>What one pulse of a Blink's pool takes — <c>ChargeSkill.PoolDamagePerPulse</c>.</summary>
    PoolDamage,

    /// <summary>How long a Blink's pool burns — <c>ChargeSkill.PoolDuration</c>.</summary>
    PoolDuration,
}

public sealed class PlayerStats : IStatBlock
{
    /// <summary>
    /// The live <see cref="Stat"/> — and <b>four of the sixteen members now depend on which class
    /// this run is</b>, which is what rule 8 changes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The address has no live stat <em>in this run</em> — an enemy's number, or a Kindling this
    /// class does not have.
    /// </exception>
    public Stat Resolve(PlayerStat stat);

    /// <summary>
    /// Whether this run answers <paramref name="stat"/>. <b>No longer a switch over the enum</b> —
    /// rule 8.
    /// </summary>
    public bool Has(PlayerStat stat);
}
```

```csharp
namespace Soulvail.Core.Progression;

public sealed class SplashFlow
{
    /// <summary>Why CH §5.4 cannot install this branch into this run, or <c>default</c> — rule 8.</summary>
    /// <remarks>
    /// <b>A third sweep beside the unregistered primitive and the <c>Minions</c> target</b>
    /// ([M5-08a](M5-08a-splash-offers-what-install-refuses.md)): a <c>ModifyStat</c> naming an
    /// address <c>PlayerStats.Has</c> answers <see langword="false"/> for.
    /// </remarks>
    public LocKey TryRefusal(SkillBranchSpec branch);

    /// <summary>The key for a branch aimed at a number this class does not have.</summary>
    public const string RefusedAddressKeyId = "ui.splash.refused.address";
}
```

## Behaviour

1. **Every number is in an asset and nothing is hard-coded** — ADR-0006, M3-12c rule 1 and M5-06b
   rule 1 verbatim. Both Actives' radii, durations, damages, cooldowns and triggers are
   `SkillDefinition` and `SpawnBurnZoneDefinition` fields; the twelve effects are `EffectDefinition`
   assets a node points at. **The owner retunes here, in the Inspector, without a task.**
2. **No Keystone, and the three names stay free.** The ruling above, and the three mechanisms each
   would need.
3. **`SpawnBurnZone` is a second primitive rather than a flag on the first**, for the reason in its
   remarks — and the two Actives differ only in data, which is the whole argument for the extra
   Active being free:

   | | Emberfall | Cinder Nova |
   |---|---|---|
   | radius / duration / damage | 4 m · 4 s · 6 | 6 m · 1.5 s · 10 |
   | pulses over its life | 8 → **48** damage | 3 → **30** damage |
   | cooldown | 14 s | 18 s |
   | trigger | `EnemiesWithin8m AtLeast 3` | `EnemiesWithin6m AtLeast 3` |
   | reads as | ground denial at range | *get off me* |

   Both sit inside the Blink pool's own arithmetic ([M6-07b](M6-07b-blink-and-the-ground-that-burns.md)
   rule 5's 24 over three seconds), so neither is a second weapon, and both are placed at the
   player's feet by `ZoneSystem.Spawn`'s existing rule — which for Cinder Nova is the mechanic and
   for Emberfall is the cost.
4. **Both Actives are tier 1 and the Upgrade is tier 2 in the same branch.** Deep Well (Arcana, 2)
   upgrades Emberfall (Arcana, 1) — `TreeRules`' cross-check satisfied rather than argued (M3-03 rule
   1) and CH §4's *"improves a skill you already own"* read literally. An Active reachable in the
   first two picks is what M3-04's `ActiveBoost` exists to weight (M5-06b rule 4).
5. **`SpawnBurnZoneHandler` is registered unconditionally, and that is the opposite call from
   `RaiseMinionsHandler`'s — for a stated reason.** That one is registered only for a class with an
   army, because a `RaiseMinions` needs a `MinionSpec` the run may not have (M5-06b's *As built* 6).
   A burning zone needs nothing class-specific: `ZoneSystem` is built for every run and
   [M6-07b](M6-07b-blink-and-the-ground-that-burns.md) wires it to burn for every run. **So
   CH §5.4's half-tree moment can lend *Ash* and *Arcana* to any class**, where the Gravecaller's
   *Legion* and *Rot* are closed to a minionless one — which is the first time the splash screen
   offers a borrower more than it refuses. `SkillTreeValidationTests.Registry()` is a hand-kept list
   and needs the seventh primitive; M5-06b's *As built* 6 is the precedent that predicts it.
6. **Ember's two signature nodes are the reason the branch exists, and the arithmetic is stated.**
   Stoked Coals takes a stack from 2 % to 3 % — a full ramp from **+60 % to +90 %** — and Long Burn
   takes the cap from 30 to 40, which with Stoked Coals is **+120 %**. That is the largest single
   multiplier any v1 tree can produce, and it is bought with the hardest condition in the game: forty
   consecutive weapon hits on 70 hit points without being touched.
   `Ember_BothTogetherAreTheBiggestMultiplierInTheBuild` pins it so the number is a decision.
7. **Ash's two nodes move the Blink rather than the weapon, which is what makes it a third branch.**
   Scorching Ground takes a pool pulse from 4 to 6 (24 → 36 over its life) and Lingering Ash takes
   three seconds to 4.5 (24 → 36 at base damage, **54** with both). Neither is addressable before
   this task and both are read at the drop, so a node taken mid-stage moves the *next* blink and not
   the pool already burning — [M6-07b](M6-07b-blink-and-the-ground-that-burns.md) rule 11's stated
   read, and M5-06a rule 3's trade for the third time.
8. **`PlayerStats.Has` stops being a switch over the enum, and that closes a hole
   [M5-08a](M5-08a-splash-offers-what-install-refuses.md) would otherwise re-open.** Four of the new
   members resolve to objects a run **may not have**: an Oathbound has no `Kindling` and a Charge has
   no pool. `PlayerStats.Has` is a static switch today — *"the eleven the switch above answers,
   written out rather than derived from `Enum.IsDefined`"* — and it has to become a question about
   *this run*, because two callers ask it before acting:
   - **`EffectRegistry`'s `ModifyStat` handler**, which asks before it applies (M4-01a's stated reason
     for `Has` existing at all).
   - **`SplashFlow.TryRefusal`**, and this is the load-bearing half. Grepped, that predicate sweeps a
     borrowed branch for an unregistered primitive and for a `ModifyStat` aimed at `Minions` — and
     **nothing else**. So an Oathbound offered the Emberwright's *Ember* branch at CH §5.4's half-tree
     moment would be offered it live, take Stoked Coals, and throw out of `SkillTree.Take` into a
     `Button.onClick`. **That is M5-08a's defect exactly, arriving from a direction its fix did not
     cover**, and it is found here by counting the new addresses rather than by playing it. The third
     sweep is one clause over a member that already exists, plus one English row.
9. **Nothing about the other two trees changes, and a row says so.** `Run_TheOtherTwoAreUnchanged`
   starts an Oathbound and a Gravecaller and walks a full tree each; the twenty-four existing nodes,
   their effects and both tree assets are untouched on disk.
10. **Twenty-seven `LocKey`s ship and all twenty-seven resolve** — twelve names, twelve descriptions
    and three branch headings (`tree.emberwright.ember`, `.arcana`, `.ash`). M5-06b rule 7's
    obligation, unchanged: `ContentValidationTests.EveryLocKey_ResolvesInEnglish` sweeps every
    authored key, so an English row per key ships in this PR or the suite is red. Descriptions are
    written to GD §13.1's two-second budget — one clause, the number in it, no clause about when.
    **Plus one for rule 8's refusal**, which is a `ui.` key rather than an authored one and is
    counted with [ledger row 7](../ROADMAP.md#carry-forward-into-m6)'s UI rows.
11. **This tree carries four weapon-scaling nodes where the Gravecaller carries one, and that is
    [ledger row 2](../ROADMAP.md#carry-forward-into-m6) being answered by authoring rather than by
    tuning.** The row's diagnosis is precise: *"The Gravecaller's whole tree carries one
    weapon-damage node (Sharpened Bone, +15 %) and one fire-rate node, so player damage grew +15 %
    across nineteen stages while a Husk's HP grew +108 %."* Ember Touch, Quickened Flame, Stoked
    Coals and Long Burn are **four**, and with a full tree they are ×1.15 × ×1.12 × (Kindling at
    +120 % against +60 %) — so an Emberwright's damage against depth grows by something like **×2.9**
    where the Gravecaller's grows by ×1.15. **The row's fix is still M8-05's and this does not claim
    it**: what this task does is stop *adding* to the problem, and **M6-11** takes the third curve
    with the instrument. Whether ×2.9 is too much is the other half of the same balance pass.
12. **Nothing here allocates on a tick.** `SpawnBurnZone` is one object per authored asset at boot,
    its handler is one `ZoneSystem.Spawn` per cast, and `Has` becomes four null checks on a method
    already called once per effect application.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_HasTwelveNodesInThreeBranches` | `Data/Trees/Emberwright.asset` / `ToSpec` / `NodeCount` 12, three branches, `TierCount` 2 each, two ids per tier |
| `Tree_NamesTheEmberwright` | the asset / `ToSpec` / `CharacterId` is `character.emberwright`, and `TryGetTreeFor` resolves it |
| `Tree_HasNoKeystone` | the twelve / — / no node's `Kind` is `Keystone` — rule 2 |
| `Tree_AvoidsTheKeystoneNames` | the twelve ids and names / — / none is `wildfire`, `overflow` or `scorched-vail` — rule 2 |
| `Tree_HasTwoActivesAndOneUpgrade` | the kinds / — / `TreeRules.ActiveCount` **2**, one `Upgrade`, nine `Passive` — the mix, asserted |
| `Tree_BothActivesAreTierOne` | `TryLocate` / — / Emberfall (Arcana, 1) and Cinder Nova (Ash, 1) — rule 4 |
| `Tree_UpgradeParentIsInBranchBelow` | Deep Well / the `TreeRules` constructor / no throw; its parent is Emberfall, same branch, tier 1 — rule 4 |
| `Tree_IdsAreKebabCaseAndConstructible` | the twelve ids / — / each constructs, a camelCase spelling is refused — M3-12c ruling 1 |
| `Nodes_FileNamesMapToTheirIds` | the twenty-five assets / — / hyphen-to-PascalCase against the last id segment |
| `Nodes_AreLinkedToAMonoScript` | the twenty-five / loaded / every `m_Script` resolves — [Traps §5](../../Traps.md) |
| `Emberfall_CarriesItsAuthoredNumbers` | the asset / `ToSpec` / cooldown 14, one clause `EnemiesWithin8m AtLeast 3` **by name**, one `SpawnBurnZone(4, 4, 6)` — rule 1 |
| `CinderNova_CarriesItsAuthoredNumbers` | the asset / `ToSpec` / cooldown 18, `EnemiesWithin6m AtLeast 3`, `SpawnBurnZone(6, 1.5, 10)` — rule 3 |
| `Actives_DifferOnlyInData` | both / — / the same `IEffect` type and the same handler; the whole difference is five numbers — rule 3, the argument for two |
| `DeepWell_LowersEmberfallsCooldown` | the asset / `ToSpec` / `ModifySkillCooldown(skill.emberwright.emberfall, PercentMult, −0.25)`, and the id it names is a node of this tree |
| `StatNodes_CarryTheirNumbers` | the eight passives / `ToSpec` / the tables' values exactly |
| `Tree_EveryNodeHasTwoKeys` | the twelve / — / `NameKey` and `DescriptionKey` non-default and all twenty-four distinct |
| `Tree_BranchesAreKeyed` | the three / — / `tree.emberwright.ember`, `.arcana`, `.ash` |
| `Tree_EveryKeyResolvesInEnglish` | the twenty-seven / against `English.asset` / every one present — rule 10 |
| `Burn_SpawnsAZoneThatBurns` | an Emberfall cast / — / one `ZoneSpawned(radius 4, duration 4)`, `SideAt` is `BurnsEnemies`, and eight pulses of 6 land on what is inside |
| `Burn_RefusesAnImpossibleZone` | zero, negative and non-finite on each of the three; a duration/interval past `MaxPulses` / — / throws in each case, naming the field |
| `Burn_RemoveDoesNothing` | a cast zone / `Remove` / it still stands and expires on its own clock — `ZoneSystem`'s rule 9 |
| `Burn_RecastingPlacesASecond` | two Emberfalls / — / two zones, not one refreshed — `ZoneSystem`'s rule 9 |
| `Burn_IsRegisteredForEveryClass` | an Oathbound run and a Gravecaller run / — / `EffectRegistry` answers for `SpawnBurnZone` in both — rule 5 |
| `Burn_AshAndArcanaAreBorrowableByEveryClass` | an Oathbound at CH §5.4's half-tree moment / `BranchesOf(character.emberwright)` / *Ash* and *Arcana* are **live**, where the Gravecaller's *Legion* and *Rot* are refused — rule 5 |
| `Splash_RefusesEmberToAClassWithNoKindling` | the same / — / *Ember* is drawn **dead** with `ui.splash.refused.address`, and `Choose` on it still throws — rule 8, M5-08a's shape |
| `Splash_TheRefusalNamesTheAddress` | the refused branch / — / the English row resolves and the message names the stat — rule 8 |
| `Splash_AnEmberwrightCanTakeItsOwnBranches` | an Emberwright borrowing nothing / — / every one of its three is installable |
| `Stats_ResolveEveryMember` | *(existing, extended)* every `PlayerStat` against a full Emberwright run / — / sixteen members, and **one** named exception (`ContactDamage`) |
| `Stats_KindlingAddressesAreTheRunsKindling` | an Emberwright / `Resolve(KindlingPerStack)` / the very `Stat` on `Kindling`, and a modifier on it moves the ramp |
| `Stats_HasIsFalseForAClassWithoutTheObject` | an Oathbound / `Has` of the four new members / **false** for all four, and `Resolve` throws — rule 8 |
| `Stats_HasIsTrueForTheClassThatHasThem` | an Emberwright / the same four / true, and `Resolve` answers |
| `Stats_NoPoolRadiusAddressExists` | `typeof(PlayerStat)` / reflection / no `PoolRadius` member, and the *"deliberately not here"* list names it with **M7-04** — the ruling above |
| `Stats_TheEnumIsAppendedNotInserted` | the shipped `ModifyStatDefinition` assets / — / every `_stat` ordinal still names the member it named before this PR — `PlayerStat`'s own rule |
| `Ember_BothTogetherAreTheBiggestMultiplierInTheBuild` | Stoked Coals and Long Burn taken / a full ramp / **+120 %**, above every other v1 tree's best — rule 6 |
| `Ash_BothTogetherMakeAPoolWorthFiftyFour` | Scorching Ground and Lingering Ash taken / one blink / 9 pulses of 6 — rule 7 |
| `Ash_ANodeMovesTheNextBlinkAndNotTheBurningOne` | a pool standing, then Lingering Ash taken / — / the standing pool expires on its old clock, the next lasts 4.5 s — rule 7 |
| `Run_AnEmberwrightStartsWithItsOwnTree` | `Start` with `character.emberwright` / — / `Available` is the six tier-1 ids of **this** tree and none of the other two's |
| `Run_TheOtherTwoAreUnchanged` | full Oathbound and Gravecaller runs / — / the twenty-four existing nodes unmoved, and neither tree asset is in `git status` — rule 9 |
| `Boot_RegistersTheThirdTreeAndTwelveSkills` | `BootScope.prefab`'s two arrays / `Install`, resolve the catalog / **36** skills, **3** trees, and all three `TryGetTreeFor` calls answer |
| `Tree_TheTreelessSkipIsGone` | `SkillTreeValidationTests` / — / no `character.emberwright` skip and no `TheTreelessClass_IsStillTreeless` — M6-07a rule 9's expiry, M5-06b's precedent |
| `Ttk_TheThirdCurveIsMeasurable` | the shipped orb with a full Ember branch against a stage-15 Husk / — / the hit count, **logged and not asserted** — rule 11, because the assertion is M8-05's and the number is M6-11's |
| `Burn_AllocatesNothing` | 100 000 casts and pulses / `AllocationAssert.None` / zero — rule 12 |

**Guard rows are implied, not listed:** `SpawnBurnZone`'s null and non-finite doors,
`SpawnBurnZoneDefinition.OnValidate`, `EffectDefinition`'s rewrap naming the asset, and every
existing `PlayerStats`, `SplashFlow` and `TreeRules` guard firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Open `Data/Trees/Emberwright.asset` and the twelve node assets. *Expected: every
   field of the three tables, and `Data/Skills/Emberwright/` and `Data/Effects/Emberwright/` hold
   twelve each.*
2. **[Editor]** Play an Emberwright and take Emberfall. *Expected: it auto-casts when three enemies
   are within 8 m, drops a burning circle at your feet, and eight pulses take them down.*
3. **[Editor]** Take Stoked Coals and Long Burn, then hold a clean stretch. *Expected: the debug
   overlay's damage figure climbs past ×1.60 to ×2.20 — rule 6.*
4. **[Editor]** Take Scorching Ground and blink into a crowd. *Expected: visibly more damage per
   pulse than an unmodified pool, and an already-burning pool that does not change — rule 7.*
5. **[Editor]** Play an **Oathbound** to the half-tree moment and open the splash. *Expected: *Ash*
   and *Arcana* live, ***Ember* dead with a reason***, and the Gravecaller's card unchanged. This is
   rule 8, which is the only step that exercises it.
6. **[Editor]** Play an Oathbound and a Gravecaller through a full tree. *Expected: identical to the
   build before this PR — rule 9.*
7. **[device]** **[ledger row 2](../ROADMAP.md#carry-forward-into-m6)**'s read: twelve **new**
   English descriptions against GD §13.1's two-second budget, now on thirty-six across three classes.
   Deferred with the rest — the Editor's `Screen.dpi` reads 120 ([Traps §9](../../Traps.md)).

## Out of scope

- **Wildfire, Overflow, Scorched Vail, and the remaining fifteen nodes.** The ruling above. **M7-04**.
- **A `PlayerStat.PoolRadius`.** No node names one. **M7-04**.
- **An effect that *spreads* a burning zone.** Scorched Vail's mechanism — a zone that places zones.
  M7-04, and it is the only one of the three Keystones that needs a new primitive rather than a new
  branch of an existing one.
- **Tuning any of the twelve.** Rule 1 puts every number in an asset so the owner can. What they
  *should* be after a played run is **M8-05**'s, with **M6-11**'s instrument.
- **Retuning the Gravecaller's one weapon node.** Rule 11 diagnoses; [ledger row
  2](../ROADMAP.md#carry-forward-into-m6) is explicit that the fix is M8-05's and that M6 adds a class
  to the problem rather than solving it.
- **Making the class cost anything.** [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md)/[b](M6-09b-a-class-you-cannot-pick-yet.md).
- **A HUD readout for Kindling.** [M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md)'s *Out of
  scope*, unchanged — and rule 6 makes it a better question than it was, because a ramp worth +120 %
  is one the player has a reason to protect.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._

**Built to the Public API, except where a deviation says otherwise.** `SpawnBurnZone` and its handler,
which beat at `MovementSkillSpec.PoolPulseInterval`. Four appended `PlayerStat` members. `Has` now
answers for the run. `SplashFlow`'s third sweep and `RefusedAddressKeyId`. One `Register` line and
the address table handed to the splash in `RunSession`. `SpawnBurnZoneDefinition`. Twenty-five
assets, twelve boot skills and one boot tree, and twenty-eight English rows. **EditMode 2 979 →
3 026 (+47), twice; PlayMode 26 / 0 / 0, twice.**

### Deviations

1. **Rule 5 overclaims, and the build follows rule 8 instead.** Ash's Scorching Ground and
   Lingering Ash name `PoolDamage`/`PoolDuration`. Rule 8 says a Charge has no pool, and a
   Shroudstep has none either. So the third sweep refuses Ash to both other classes, and **only
   Arcana is lendable to everyone**. A live Ash would throw out of `Take` into a button, which is
   M5-08a's defect. `Burn_AshAndArcanaAreBorrowableByEveryClass` became
   `Burn_ArcanaIsBorrowableByEveryClass`, and `Splash_RefusesAshToAClassWithNoPool` was added.
2. **No public `TryRefusal(SkillBranchSpec)`.** The existing private sweep got the clause
   (`AimsAtAMissingAddress`), both in the flag path and in the throw path. `SplashFlow` takes the
   table as an **optional** last `IStatBlock`, so no fixture rippled. `RunSession` always passes it.
3. **"Is a Blink" is read off `Charge.PoolDuration.Base` once, at construction.** Nothing exposes
   the movement kind. `MovementSkillSpec.Pool` makes a pool base above zero exactly when the skill
   is a Blink. `ChargeSkill` is untouched. Only the two Kindling checks are null checks.
4. **Rule 8's first caller does not exist.** `ModifyStatHandler` never asks `Has`: it resolves,
   and `Resolve` throws. It is unedited. The splash is the only caller that matters.
5. **`EffectDefinition.cs` is unedited.** It has no list of kinds; a new primitive is a new
   subclass and nothing more, as that file's remarks say.
6. **Ripple past the table:** `GravecallerTreeTests` pinned 24 skills read off disk, and eight of
   its rows went red. Its catalog now holds all three classes. `BurningGroundTests` and
   `KindlingTests` each had a row asserting "no address yet (M6-08)". Both were rewritten to name
   the addresses that exist, and the pool row still refuses a `PoolRadius`. `StatBlockTests`' two
   player rows skip a named `NotAnOathbounds` set. Its minion count went from 9 to 13, and its
   ordinal row was appended. `PlayerStatCoverageTests` went from 12 to 16.
7. **Row placement:** the `Has`/`Resolve` rows are in `ModifyStatTests`, beside
   `Stats_ResolveEveryMember`. `Burn_IsRegisteredForEveryClass`, `Stats_NoPoolRadiusAddressExists`
   and `Stats_TheEnumIsAppendedNotInserted` need shipped runs, source or assets, so they are in
   `EmberwrightTreeTests`.
8. **`Splash_AnEmberwrightCanTakeItsOwnBranches` applies every node's effects to an Emberwright
   run.** A class cannot borrow from itself (`RequireCandidate`).
9. **`Run_TheOtherTwoAreUnchanged` walks both full trees.** It asserts neither tree asset
   references an Emberwright guid. The `git status` half was checked at handover, and neither file
   is modified.
10. **Seven rows beyond the table:** Cinder Nova's own burn, placement on the run's clock, the
    handler's guards, a system that cannot burn, registry dispatch, a Blink without Kindling, and
    Ash's refusal (deviation 1).

### Findings

- **Rule 11's third curve, logged:** a stage-15 Husk has 66.2 HP. The orb deals 17 at base. With a
  full Ember branch it takes **4 hits cold and 2 at a full 40-stack ramp**, which is under
  GD §12.4's floor when hot. Ledger row 2 carries it for M6-11 and M8-05.
- **Emberfall triggers at 8 m and burns 4 m.** Half the enemies it counts can stand outside it.
  Rule 3 names this as the cost, and the owner can retune it in the asset.
- **`SkillDefinition` serialises `_cooldown: 8` on every Passive and Upgrade.** Nothing reads it.
  The new assets match the shipped ones.
- **`Burn_AllocatesNothing` casts in over half of its 100 000 iterations.** A 10⁷ HP body is used
  because a float near 10⁸ has a step of 8 and cannot record a 6-point pulse.
- **Known issue 1 did not fire.** Both PlayMode passes were 26 / 0.
