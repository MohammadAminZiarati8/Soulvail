# M6-07a — The Emberwright, the Cinder Orb, and heat that builds while nothing touches you

**Size:** M · **Depends on:** M6-06b · **Branch:** `m6-07a-emberwright-and-kindling`
**Design refs:** CH §3, §3.3, §4; GD §6.1, §6.2, §12.4, §12.5, §19; CC §2.5, §4.1, §7; AR §10.1, §18.1; ADR-0006, ADR-0008 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m6) — the third curve arrives here and rule 2 is what decides where it starts; [7](../ROADMAP.md#carry-forward-into-m6) — two strings

## Goal

A third `CharacterDefinition` exists and is playable, its weapon is one that already works, and CH
§3.3's Kindling turns *not being touched* into damage.

## Three of the four things in the ROADMAP's title need no new code, and one needs a task

The row reads *"Emberwright spec, Cinder Orb, Blink, Kindling"*. Grepped rather than assumed:

| Title | What it costs | Where it lands |
|---|---|---|
| **Cinder Orb** — 30 dmg, 1.5/s, 25 m/s, 3 m AoE | **Nothing.** `WeaponKind.Projectile` with `shotSpeed` and `shotRadius`, and `ProjectileSystem.LandOnEnemies` already damages *every living agent inside the radius* | this task, as authored numbers |
| **Emberwright spec** | one asset, and `ClassCard.Clear`'s own remarks say the prefab already carries three cards | this task |
| **Kindling** | a passive nothing in the project resembles | this task, rule 4 |
| **Blink** | `MovementSkillKind.Blink`, a fire pool, and a `ZoneSystem` that heals the player and nothing else | **[M6-07b](M6-07b-blink-and-the-ground-that-burns.md)** |

**So the split the ROADMAP predicted is the one that happens, and the counting says where.** Unsplit,
M6-07 is `Kindling.cs`, `KindlingSpec.cs`, `KindlingTests.cs`, `EmberwrightTests.cs`,
`ZoneSystem.cs`, a burning-ground fixture, and `VeilrotSpec.cs` with its two fixtures — **nine**
against the ROADMAP's five. The seam here is [M5-02](M5-02-gravecaller-and-bone-bolt.md)/[M5-03](M5-03-shroudstep-and-corpse-decoy.md)'s
exactly: **the class authored whole, with a movement kind that does nothing yet**, against the task
that makes the kind mean something. A third piece — what each class's *Veilrot relationship* is — is
[M6-07c](M6-07c-what-each-class-does-with-the-veil.md), and that spec argues its own placement.

## The number CH §3.3 publishes cannot ship, and this is M5-02 rule 2 a second time

**30 damage kills a stage-1 Husk in two hits.** `Husk.asset` carries `_maxHp: 36` and
`ceil(36 / 30) = 2`, against GD §6.2's *"a basic enemy dies in **3–5 hits from any class**"* — which
GD §12.4 lists under *"Invariants. Violating them is a bug, not a tuning choice."* It is outside the
band at stage 1, with no tree, no Overflow and no Kindling. Husk HP grows **2.08×** by stage 16
(measured at [M5-08](M5-08-acceptance-and-tag.md)), so 30 stays at two hits until roughly **stage
13** — a third of a played run spent under the band.

**[Ledger row 2](../ROADMAP.md#carry-forward-into-m6) did the DPS arithmetic and not this one.** It
says *"a 30-damage Cinder Orb at 1.5/s is 45 DPS against the Censer's 39 and the Bone Bolt's 36"* and
concludes the Emberwright is *"the class most likely to break §12.4 from the other end"*. That is
right and it is worse than the row states: the break is not a drift at depth, it is **at stage 1**,
and an invariant written in *hits* cannot be reached by moving a number written in *damage per
second*.

**The asset carries 17 and the document keeps 30.** `ceil(36 / 17) = 3`, the bottom of the band, and
the class stays the big-hit class its fantasy is made of:

| | Censer | Bone Bolt | Cinder Orb |
|---|---|---|---|
| Damage × rate | 13 × 3.0 | 9 × 4.0 | **17 × 1.5** |
| DPS, one body | 39 | 36 | **25.5** |
| Hits on a stage-1 Husk | 3 | 4 | **3** |
| **Seconds** on a stage-1 Husk | 1.00 | 0.75 | **2.00** |
| Reaches | 60° arc, 8 m | 0.8 m blast, 12 m | **3 m blast, 12 m** |

**The slowest single-target killer in the game and the widest blast in it** — CH §3.3's *"enormous,
slow, unforgiving"* as two numbers rather than as a sentence. Against four bodies inside 3 m it is
102 effective DPS, which is where the class's power is and why 25.5 against one is not weakness.

**Two alternatives were weighed and refused.** Lowering the *fire rate* instead moves DPS without
moving hits-to-kill, which M5-02 rule 2 already refused in as many words — *"it cannot touch an
invariant stated in hits."* Keeping 30 and calling the 3 m blast the compensation fails on the same
sentence: GD §6.2 is about *a basic enemy*, singular, and the blast is what makes 17 generous rather
than what makes 30 legal. **This is one field on one asset and the owner overrides it by typing a
different number** — M5-02 rule 2's closing line, which is the point of it being authored.

**CH §3.3's table keeps 30 and gains the paragraph under it**, which is M5-02's treatment of the Bone
Bolt's 7 — the document publishes the design and the ruling is written beneath it, so the next reader
finds both rather than a silent disagreement.

## Kindling spans the whole width of the band, and that is the mechanic

CH §3.3: *"consecutive hits without taking damage stack +2 % damage to +60 %."* **+60 % is 1.6, and
GD §6.2's band is 5 / 3 ≈ 1.67 wide.** So *any* weapon that sits at 3 hits cold sits at 2 hits at
full ramp: `ceil(36 / 27.2) = 2`, and no authored damage avoids it — a base of 5 hits would need
8 damage and 12 DPS, a third of the Censer's.

**So the Emberwright at full Kindling is the one thing in the build that goes under GD §6.2's floor,
and it is written down rather than discovered.** Thirty consecutive weapon hits without taking a
point of damage is the price, and a class on 70 hit points pays it rarely.
`Kindling_AtFullRampGoesUnderTheBand` is the row, so the day somebody retunes either number it is a
decision rather than a drift. **GD §6.2 gains a half-line**, the way GD §12.4's TTK row gained one at
M5-08: the band is the *weapon's*, and a signature that spends a run's worth of not being hit is
allowed to beat it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/KindlingSpec.cs` | Core | CH §3.3's signature as authored data. Null on a class without one — `MinionSpec`'s rule |
| `Core/Combat/Kindling.cs` | Core | The stack, the two edges that move it, and the one modifier it owns |
| `Tests/Core/Combat/KindlingTests.cs` | Tests.Core | The ramp, the cap, the reset, and the hits that do not count |
| `Tests/Game/Authoring/EmberwrightTests.cs` | Tests.Game | Every authored number against the document it comes from or the ruling that moved it — `GravecallerTests`' shape |
| `Data/Characters/Emberwright.asset` | — | The class. One asset, `character.emberwright` |
| *small edits* | Core, Game | `Core/Content/CharacterSpec.cs` — `kindling`, optional and last (rule 1); `Core/Content/MovementSkillSpec.cs` — `MovementSkillKind.Blink` (rule 8); `Core/Combat/PlayerCombat.cs` — holds the passive and calls its two edges (rules 5, 6); `Core/Combat/ProjectileSystem.cs` — one call in `Land`'s `AtEnemies` branch (rule 5); `Core/Run/RunSession.cs` — builds it for a class that authors one; `Game/Authoring/CharacterDefinition.cs` — the Kindling block; `Prefabs/Composition/BootScope.prefab` — `_characters` gains one; `Data/Localisation/English.asset` — two rows |
| *ripple* | Tests.Core, Tests.Game | **64 `new CharacterSpec(...)` sites across 45 files** are untouched, because the block is optional and last (rule 1); `SkillTreeValidationTests` gains M5-02's named skip back (rule 9); `ContentValidationTests` and `CharacterDefinitionTests` sweep a third character |
| *docs* | — | `Characters.md` §3.3's damage paragraph (rule 2); `GameDesign.md` §6.2's half-line (rule 4) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// CH §3.3's Kindling, as authored data: how much one hit is worth, and how many count. Null on a
/// class without a stacking signature, which is every class but the Emberwright.
/// </summary>
/// <remarks>
/// <b>Optional on <see cref="CharacterSpec"/> for <see cref="ShieldSpec"/>'s reason, and it is the
/// same argument rather than a second one</b> (M5-02 rule 6): a zeroed block would have to be read
/// against the class id to be understood, and a <see langword="null"/> says <em>this class has no
/// signature of this kind</em> in one word.
/// </remarks>
public sealed class KindlingSpec
{
    /// <param name="perStack">
    /// Fractional weapon damage one stack is worth — 0.02 (CH §3.3). Finite and above zero.
    /// </param>
    /// <param name="maxStacks">
    /// How many stacks the ramp holds — 30, which is CH §3.3's +60 % divided by
    /// <paramref name="perStack"/>. Above zero. <b>The cap is authored as a count rather than as a
    /// ceiling fraction</b>, because a count is what the HUD would draw and what a node adds to; the
    /// fraction is the product, and <see cref="MaxBonus"/> is where it is computed once.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="perStack"/> is not a finite number greater than zero, or
    /// <paramref name="maxStacks"/> is below 1.
    /// </exception>
    public KindlingSpec(float perStack, int maxStacks);

    public float PerStack { get; }
    public int MaxStacks { get; }

    /// <summary>What a full ramp is worth as a fraction — 0.60 for the shipped block.</summary>
    public float MaxBonus { get; }
}

public sealed class CharacterSpec
{
    // ... the eleven arguments that exist, then, optional and last (rule 1):
    //     KindlingSpec kindling = null

    /// <summary>
    /// The class's stacking signature, or <see langword="null"/> for a class with none. Only the
    /// Emberwright has one in V1.
    /// </summary>
    public KindlingSpec Kindling { get; }
}
```

```csharp
namespace Soulvail.Core.Content;

public enum MovementSkillKind
{
    Charge,
    Shroudstep,

    /// <summary>
    /// The Emberwright's Blink (CH §3.3): an instant 10 m teleport leaving a fire pool. The kind is
    /// content identity and lands here; what it <em>does</em> is
    /// <see href="M6-07b-blink-and-the-ground-that-burns.md">M6-07b</see>'s — rule 8, and M5-02 rule
    /// 7's arrangement for <see cref="Shroudstep"/> one class earlier.
    /// </summary>
    Blink,
}
```

```csharp
namespace Soulvail.Core.Combat;

/// <summary>
/// CH §3.3's Kindling: consecutive weapon hits with nothing touching you, as a modifier on weapon
/// damage. The only thing in the game that pays for a perfect stretch rather than for a build.
/// </summary>
public sealed class Kindling
{
    /// <param name="spec">The class's authored block. Null is refused — build one or do not.</param>
    /// <param name="damage">
    /// <c>Weapon.Damage</c>, the <c>Stat</c> the ramp rides. Handed the stat rather than the weapon,
    /// for <c>RisePassive</c>'s reason: the one number this touches is the one it is given.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public Kindling(KindlingSpec spec, Stat damage, IDomainEvents events);

    /// <summary>How many stacks are standing, in <c>[0, MaxStacks.Value]</c>.</summary>
    public int Stacks { get; }

    /// <summary>
    /// What one stack is worth, live. Where <see href="M6-08-emberwright-tree-v1.md">M6-08</see>'s
    /// Ember branch puts <em>"+1 % a stack"</em> — rule 3.
    /// </summary>
    public Stat PerStack { get; }

    /// <summary>How many count, live. Where Ember puts <em>"+10 stacks"</em>.</summary>
    /// <remarks>
    /// <b>Two <see cref="Stat"/>s rather than two floats, and it is <c>RisePassive.Chance</c>'s
    /// arrangement rather than a new one</b>: an authored number a node is going to move is a
    /// <c>Stat</c> seeded from the spec, always (ADR-0008). Both are read through a clamp at the
    /// point of use rather than clamped in the stat, because a <see cref="Stat"/> clamps nothing —
    /// a negative per-stack is a signature that punishes a good stretch and a cap below zero is one
    /// that never starts, so both are floored where they are read.
    /// <b>Neither is addressable until M6-08</b>, which is M3-12a's own sequence: the stat exists
    /// here because the number is authored, and the <c>PlayerStat</c> member arrives with the node
    /// that names it.
    /// </remarks>
    public Stat MaxStacks { get; }

    /// <summary>What the stacks are worth right now, as a fraction — rule 3.</summary>
    public float Bonus { get; }

    /// <summary>
    /// One more of the class's weapon hits landed. Adds a stack and rewrites the modifier — rule 5.
    /// </summary>
    public void OnWeaponHitLanded();

    /// <summary>
    /// Damage reached the player. Drops every stack — rule 6. Silent for a blocked hit.
    /// </summary>
    public void OnPlayerDamaged();

    /// <summary>Back to cold, silently. For the end of a run — <c>PlayerCombat.Reset</c>'s.</summary>
    public void Reset();
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>The ramp moved. Both numbers, so a readout never has to divide.</summary>
public readonly struct KindlingChanged
{
    public KindlingChanged(int stacks, int maxStacks, float bonus);

    public readonly int Stacks;
    public readonly int MaxStacks;
    public readonly float Bonus;
}
```

## Behaviour

1. **The block is optional and last on `CharacterSpec`, and the count is why.** `new
   CharacterSpec(...)` has **64 sites across 45 files** — every fixture in the project builds one —
   so `kindling` goes after `minions`, exactly where `minions` went after `hitIFrames` for the same
   counted reason (M5-02 rule 6). `Kindling` is null on the Oathbound and on the Gravecaller and
   **neither shipped asset is rewritten**, which is M4-01a's measured finding: Unity does not
   re-serialise an asset because the script that reads it grew a field. `_kindlingMaxStacks` is the
   null switch on `CharacterDefinition`, on `_minionCap`'s precedent and for its stated reason — a
   count is the one field the spec cannot express at zero.
2. **The class is authored whole and every number that is not CH §3.3's is named as ours.**

   | Field | Value | Where it comes from |
   |---|---|---|
   | `maxHp` | **70** | CH §3.3 |
   | `speed` | **3.4** | M5-02 rule 1's ruled band, the ceiling of it — the wizard is the fastest |
   | `shield` | **none** (`_shieldMax: 0`) | CH §3.1 calls the Aegis *"the only regeneration in the game"* |
   | `minions` | **null** | CH §3.3 has none |
   | `hitIFrames` | 0.5 | both shipped classes', M5-02 rule 4's decision |
   | targeting, focus, handling | the Oathbound's | M5-02 rule 4: CC §3 is one targeting system for every class, and CC §2.5 publishes one set of handling numbers |
   | `weaponKind` | `Projectile` | CH §3.3 |
   | `weaponDamage` | **17** | **ours** — the ruling above. CH §3.3 says 30 |
   | `weaponSwingsPerSecond` | 1.5 | CH §3.3 |
   | `weaponRange` | **12** | ours, and it is the Bone Bolt's for M5-02 rule 3's reason: it matches `_acquireRange`, so the weapon fires at everything it can acquire |
   | `weaponConeAngleDeg` | 360 | `WeaponSpec` requires exactly this on a `Projectile` |
   | `weaponDamageFrame` | **0.15** | ours, and M5-02 rule 3's sentence verbatim — *a bolt's release is the moment it leaves the hand.* 0.15 of a 0.667 s interval is **100 ms** |
   | `weaponShotSpeed` | 25 | CH §3.3's *"slow"*. The Bone Bolt is 40 and a Spitter's is 12, so the orb is the second-slowest thing in the air; at 12 m that is a **0.48 s** flight |
   | `weaponShotRadius` | **3** | CH §3.3's *"3 m AoE detonation"*, and it is the biggest blast in the game against the Bone Bolt's 0.8 and a Spitter's 1.6 |
   | `kindlingPerStack` / `MaxStacks` | 0.02 / **30** | CH §3.3, with 30 derived from its own +60 % |

   **The 3 m radius costs the lead something and that is stated rather than discovered.** M5-01 built
   `ProjectileLead` so a 25 m/s orb would be aimable (CC §3.7, and CH §3.3's own *"pleasing
   synergy"*), and at a 3 m blast a lead that is a metre wrong still connects. The lead is not
   pointless — it is what puts the blast *centred* on a walking target rather than clipping it, which
   is the difference between one body and four. `Orb_TheLeadStillDecidesHowManyItCatches` is the row.
3. **The bonus is `Stacks × PerStack`, one `PercentAdd` modifier under one source, rewritten when the
   count moves.** ADR-0008's order: pooled additively under the passive's own source, so a full ramp
   is ×1.60 rather than 1.02³⁰ (×1.81), and `Stat.RemoveAll(this)` takes the whole ramp back without
   knowing what else was added since. Rewritten on a change and **never per frame** — M6-04 rule 7's
   discipline, and here the trigger is an event rather than a clock, so a run that lands no hits
   raises nothing.
4. **The cap is `MaxStacks.Value` and the ramp saturates silently.** A thirty-first hit adds nothing,
   publishes nothing and is not an error — it is the ordinary state of a good stretch. Both numbers
   are `Stat`s seeded from the spec, `RisePassive.Chance`'s arrangement and ADR-0008's rule that an
   authored number a node will move is a `Stat` from the day it is authored; **neither is addressable
   until [M6-08](M6-08-emberwright-tree-v1.md)**, which is M3-12a's own sequence. The cap is read as
   `max(0, floor(MaxStacks.Value))` and the per-stack as `max(0, PerStack.Value)`, because a `Stat`
   clamps nothing: a node that drove either negative would otherwise make the signature punish the
   stretch it exists to reward.
5. **A stack is one *weapon* hit that reached something living, and the two doors are
   `PlayerCombat`'s.** The cone path already has one — `ResolveConeHits` knows the distinct ids it
   damaged — and the projectile path is `ProjectileSystem.Land`'s `AtEnemies` branch, which already
   computes `hit` for `ProjectileImpacted` and means *"reached a living agent"* in that class's own
   words. **One swing is one stack however many bodies it caught**, because CH §3.3 counts *hits*
   and a blast that caught four would otherwise fill the ramp in eight shots. **A miss neither adds
   nor resets:** CH §3.3's only reset condition is being hit, so a shot that sails through a gap
   costs the ramp nothing.
6. **Nothing but a dash and a zone is excluded, and the exclusion is the rule that keeps the
   signature about aim.** `ResolveChargeHits` does not feed it and neither does a
   [M6-07b](M6-07b-blink-and-the-ground-that-burns.md) fire pool: a pool pulsing six times in three
   seconds would fill thirty stacks off five blinks, which makes Kindling a property of the movement
   button rather than of *"consecutive hits"*. The Blink deals zero damage anyway (rule 8), so the
   dash half is inert for the class that has the signature and is written for the class M7 adds.
7. **It resets on damage that landed and not on one the i-frames ate.**
   `PlayerCombat.ApplyDamage`'s own spelling — *"nothing arrived"* rather than a comparison against
   `None` — is what the reset reads: a `DamageResult` with `Blocked` true and nothing applied leaves
   the ramp standing, because CC §5's i-frames exist to make a dodge worth something and a signature
   that broke on the frame after a successful dodge would punish the player for dodging. **Damage
   that went entirely to a shield does reset**, which is unreachable in V1 (the one class with a
   shield has no Kindling) and is written for M7's fourth.
8. **`MovementSkillKind.Blink` lands here and does nothing here** — M5-02 rule 7 verbatim, one class
   on, and the enum's own remarks have said *"`Blink` arrives with M6-07"* since M5-02.
   `PlayerCombat` builds a `ChargeSkill` from the spec whatever the kind says, so **an Emberwright in
   this build teleports 10 m in 0.05 s, damages nothing, shoves nothing and leaves no fire**. That is
   an honest intermediate state rather than a broken one and it is named so nobody reports it as a
   bug. `MovementSkillSpec.Decoy` already puts a `Blink` on the zero side of its line in writing, so
   `decoyDuration` stays 0 and that validation needs no edit. Authored: 10 m over 0.05 s (CH §3.3's
   *instant*, and the Shroudstep's duration for its reason), **2.0 s cooldown** (CH §3.3), 0 damage,
   0 knockback, 0.05 s i-frame trail, 0.15 s input buffer.
9. **The class is selectable the moment it is in the catalog, and it has no tree for two tasks.**
   This is the opposite of M5-02 rule 9's situation and it is the *screen* that changed:
   `ClassSelectPresenter.Draw` binds one card per authored class, `ClassCard.Clear`'s own remarks say
   *"the prefab carries CH §3's whole roster of three and the catalog holds two … which is what makes
   the Emberwright's arrival at M6-07 a card being filled rather than a prefab being edited"*, and
   `_cards` is authored at three. So the third card appears with no prefab edit — and
   `SkillTreeValidationTests.EveryShippedCharacter_HasATree` goes red, because
   [M6-08](M6-08-emberwright-tree-v1.md) is where the tree lands. **M5-02's named skip comes back
   exactly as it was**: `character.emberwright` skipped *by name* rather than by a rule, with
   `TheTreelessClass_IsStillTreeless` beside it saying in its own message that it goes red the day
   M6-08 merges and the fix is to delete both. A treeless run is already a tested state — `RunSession`
   builds no `LevelUpFlow` for one (`SkillTreeValidationTests.NoTree_CanStarve_*`) — so an Emberwright
   picked today plays a run with a weapon, a signature and no progression at all.
10. **Two `LocKey`s ship and both resolve.** `character.emberwright.name` and
    `.description`, English rows in this PR — not deferred, because
    `ContentValidationTests.EveryLocKey_ResolvesInEnglish` sweeps every `CharacterDefinition`'s keys
    and a missing row is a red suite rather than a note for M6-10 (M5-02's *As built* 2, which is
    where that stopped being optional). The description is CH §3.3's *"Enormous, slow,
    unforgiving"* — the card has room for a sentence and three numbers, which is what
    `CharacterSpec.DescriptionKey` exists for.
11. **Nothing here allocates.** An `int`, a `float`, one `Modifier` struct written into a `Stat`'s
    pool at most once per landed hit, and two calls on paths that already run.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_CarriesItsTwo` | `new KindlingSpec(0.02f, 30)` / — / both read back, `MaxBonus` **0.60** |
| `Spec_RefusesAnImpossibleBlock` | per-stack 0, negative, NaN, ∞; max stacks 0 and −1 / — / throws in each case, naming the field |
| `Kindling_StartsCold` | a fresh passive / — / `Stacks` 0, `Bonus` 0, no modifier on the stat, nothing published |
| `Kindling_OneHitIsOneStack` | `OnWeaponHitLanded()` / — / 1 stack, damage ×1.02, one `KindlingChanged(1, 30, 0.02)` |
| `Kindling_PoolsAdditively` | 30 hits / — / damage **×1.60**, not 1.02³⁰ — rule 3, ADR-0008's order |
| `Kindling_SaturatesSilently` | 30 hits, then 5 more / — / still 30 and ×1.60, and **no** event for the five — rule 4 |
| `Kindling_BothNumbersAreStats` | `typeof(Kindling)` / reflection / `PerStack` and `MaxStacks` are `Stat`s seeded from the spec, and **no `PlayerStat` member names either** — rule 4, M3-12a's sequence |
| `Kindling_ANegativeStatIsFlooredWhereItIsRead` | a modifier driving `PerStack` to −0.5 and `MaxStacks` to −4 / 10 hits / `Bonus` 0 and `Stacks` 0, no throw — rule 4 |
| `Kindling_ADamagedPlayerLosesEverything` | 20 stacks / damage that applied / `Stacks` 0, the modifier gone, one `KindlingChanged(0, 30, 0)` — rule 7 |
| `Kindling_ABlockedHitLeavesItStanding` | 20 stacks, i-frames live / a hit refused / **20** stacks, nothing published — rule 7 |
| `Kindling_RemoveAllTakesTheWholeRamp` | 12 stacks and a `+15 %` node / `Stat.RemoveAll(kindling)` / the node's modifier survives, the ramp is gone — rule 3 |
| `Kindling_ResetIsSilent` | 20 stacks / `Reset` / 0 and **nothing published** — the end of a run is not news (`EssenceWallet.Restore`'s rule) |
| `Kindling_AConeSwingIsOneStackHoweverManyItCaught` | a cone reaching four enemies / resolved / **1** stack — rule 5 |
| `Kindling_AnOrbIsOneStackHoweverManyItCaught` | an orb landing on four / — / 1 stack — rule 5 |
| `Kindling_AMissCostsNothing` | 10 stacks / an orb that reaches nobody / still 10, nothing published — rule 5 |
| `Kindling_ADashHitIsNotAStack` | 10 stacks / `ResolveChargeHits` over two enemies / still 10 — rule 6 |
| `Kindling_AZonePulseIsNotAStack` | 10 stacks, a zone pulsing / ticked / still 10 — rule 6, pinned before [M6-07b](M6-07b-blink-and-the-ground-that-burns.md) can make it reachable |
| `Kindling_AtFullRampGoesUnderTheBand` | the shipped orb at 30 stacks against a stage-1 Husk / — / **2** hits, and the cold weapon is **3** — the ruling above, asserted so a retune of either number is a decision |
| `Kindling_IsNullOnBothShippedClasses` | `Oathbound.asset`, `Gravecaller.asset` / converted / `Kindling` null, and neither asset appears in `git status` — rule 1 |
| `Emberwright_ConvertsAndIsInTheCatalog` | the shipped asset / `ToSpec` / `character.emberwright`, and the boot catalog resolves it beside the other two |
| `Emberwright_IsSeventyHitPointsAndNoShield` | the asset / converted / `MaxHp` 70, `Shield` null, `Minions` null — rule 2 |
| `Emberwright_SpeedIsTheTopOfTheRuledBand` | the asset / converted / 3.4, strictly above both shipped classes — rule 2, M5-02 rule 1's band |
| `Speed_EveryClassOutrunsEveryEnemy` | *(existing, widened)* all **three** characters against every shipped enemy / — / every ratio above 1 — GD §6.1's rule |
| `Orb_IsTheRuledWeapon` | the asset / converted / `Projectile`, **17** damage, 1.5/s, 12 m, 360°, 0.15 frame, 25 m/s, 3 m — rule 2's table, one assertion a row |
| `Orb_KillsAStageOneHuskInThreeHits` | the shipped orb and Husk / no tree, no Overflow, no Kindling / **3**, inside GD §6.2 — the ruling's whole argument |
| `Orb_AtTheDocumentedThirtyWouldBreakTheBand` | the same at 30 damage / — / **2** hits, outside the band, **and still 2 at stage 13's scaled Husk** — the control that proves the ruling moved something actually wrong |
| `Orb_IsTheSlowestKillerAndTheWidestBlast` | all three shipped weapons / — / 2.00 s against 1.00 and 0.75, and radius 3 against 0.8 and a Spitter's 1.6 — rule 2, asserted as comparisons rather than as literals |
| `Orb_CatchesEverythingInsideThreeMetres` | four Husks within 3 m of the impact / one orb / four `EnemyDamaged` of 17 each — `LandOnEnemies` unchanged, asserted because it is what makes 17 generous |
| `Orb_TheLeadStillDecidesHowManyItCatches` | a line of walking Husks, led and unled / — / the led shot catches strictly more — rule 2, CC §3.7 |
| `Emberwright_TargetingAndFocusMatchTheOathbound` | all three assets / converted / every `TargetingSpec` and `FocusSpec` field equal — rule 2's decision, pinned |
| `Emberwright_MovementIsABlinkThatIsNotYetOne` | the asset / converted / `Kind` is `Blink`, distance 10, duration 0.05, cooldown **2.0**, damage 0, knockback 0, `DecoyDuration` **0** — rule 8 |
| `Blink_LeavesNothingYet` | an Emberwright-shaped spec / a dash / no decoy, no zone, no damage, and the i-frames and `ChargeStarted` are the Charge's — rule 8's honest intermediate state |
| `Spec_ABlinkStillRefusesADecoyDuration` | `Blink` with a decoy duration / — / throws — `MovementSkillSpec.Decoy`'s existing line, unchanged by a third member |
| `Tree_TheTreelessClassIsStillTreeless` | the catalog / — / `character.emberwright` resolves no tree, the named skip is present, **and the message names M6-08** — rule 9 |
| `Class_TheThirdCardIsBoundWithNoPrefabEdit` | the Menu scene / `ClassSelectPresenter.Open` / three cards drawn, `ClassSelect.prefab` **unchanged on disk** — rule 9, `ClassCard.Clear`'s own prediction |
| `Assets_AreLinkedToAMonoScript` | `Emberwright.asset` / loaded / `m_Script` resolves — [Traps §5](../../Traps.md) |
| `Keys_ResolveInEnglish` | the two new keys / against `English.asset` / both present — rule 10 |
| `Kindling_AllocatesNothing` | 100 000 hits and resets / `AllocationAssert.None` / zero — rule 11 |

**Guard rows are implied, not listed:** nulls to both constructors,
`CharacterDefinition.OnValidate` on the two new fields, and every existing `CharacterSpec` guard
firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Open `Emberwright.asset`. *Expected: every field of rule 2's table, the shield block
   zero, the minion block zero, and the Kindling block filled.*
2. **[Editor]** Open the Menu and press `Descend`. *Expected: **three** cards. The third reads
   Emberwright, 70 HP, 3.4 m/s and 25.5 DPS, and the prefab has no diff.*
3. **[Editor]** Play it. *Expected: a slow orb that kills the first Husk in three, and a blast that
   catches a cluster. The debug overlay's damage figure climbs by 2 % a hit to ×1.60 and drops to
   ×1.00 the moment anything connects. There is no level-up screen at all — rule 9, looked at.*
4. **[Editor]** Blink into a crowd. *Expected: a 10 m teleport, nothing damaged, no fire — rule 8,
   which is the thing most likely to be filed as a bug before
   [M6-07b](M6-07b-blink-and-the-ground-that-burns.md) merges.*
5. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**: whether a ramp with no
   readout is felt at all. The debug overlay is the only place Kindling is visible and there is no
   HUD element for it — see *Out of scope* — so *"did the player notice they were on ×1.6"* is a
   question a phone and a person answer together.

## Out of scope

- **Blink meaning anything.** [M6-07b](M6-07b-blink-and-the-ground-that-burns.md). Rule 8 ships the
  member, not the behaviour — M5-02 rule 7's arrangement exactly.
- **What the Emberwright does with Veilrot.** [M6-07c](M6-07c-what-each-class-does-with-the-veil.md),
  which takes all three classes' relationships rather than only this one's.
- **The Emberwright's tree, its branches and its Keystones.** [M6-08](M6-08-emberwright-tree-v1.md);
  Wildfire, Overflow and Scorched Vail are M7-04's with the other six, M5-06b rule 2's ruling.
- **A HUD readout for the ramp.** GD §16.1 authors no meter for it and
  [M6-03b](M6-03b-the-meter-on-the-right-edge.md) has already put two down the right edge. A third
  is a screen decision with a played run behind it — **M6-11**'s call, or M8-01's.
- **A `PlayerStat` address for either Kindling number.** The two `Stat`s ship here because the
  numbers are authored (rule 4); the *addresses* wait for [M6-08](M6-08-emberwright-tree-v1.md)'s
  Ember branch, because an address with no node is `PlayerStat.ContactDamage`'s mistake made on
  purpose — and because `PlayerStats.Resolve` would then answer for a class that has no Kindling,
  which is the hole M6-08 rule 8 closes.
- **Retuning the orb.** Rule 2 authors one number and says why. What it *should* be after a played
  run is **M8-05**'s, with the instrument at **M6-11**.
- **Locking the class.** [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md)/[b](M6-09b-a-class-you-cannot-pick-yet.md).
  It is free to pick for four tasks, which is what makes rule 9's treeless state visible at all.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
