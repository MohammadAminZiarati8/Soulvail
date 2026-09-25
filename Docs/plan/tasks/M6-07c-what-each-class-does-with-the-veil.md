# M6-07c — What each class does with the Veil, and the cast you buy with it

**Size:** M (5 counted files, at the [sizing rule](../ROADMAP.md#how-to-read-this)'s cap) · **Depends on:** M6-07b, M6-02b · **Branch:** `m6-07c-class-veilrot-relationships`
**Design refs:** CH §3, §3.1, §3.2, §3.3, §4.2; GD §10, §10.1, §10.2, §13.2, §13.3; AR §10.1, §18.1; ADR-0006, ADR-0008 · **Ledger rows:** none directly

## Goal

CH §3's third identity axis stops being a table nobody reads: the Oathbound resists the Veil, the
Gravecaller feeds on it, and the Emberwright spends it to cast through a cooldown.

## Why this is an M6-07 task and not a fourth thing bolted onto one

**Two merged specs already put it here by name.** [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md)
rule 3: *"CH §3.3's Emberwright spends 5 Veilrot to cast off-cooldown and must have the 5, which is a
different question with a different failure … **M6-07 adds `CanSpend`/`Spend`**."*
[M6-02b](M6-02b-four-things-essence-buys.md)'s *Out of scope*: *"The Emberwright's 5-Veilrot instant
cast. **M6-07's**, and it needs `Veilrot.Spend`."* So one third of this task was assigned before
M6-00c opened.

**The other two thirds had no owner at all, and the counting is what found that.** CH §3 gives every
class a Veilrot row — *Resists · Thrives · Spends* — and calls it *"the strongest differentiator
available."* Grepped across M6's eleven merged specs: **`−40 % from Pact nodes`, `Cleanses at half
price`, `Starts at 15`, `+50 % faster` and `+1 % damage per Veilrot point` appear in none of them.**
M6-04 ships the meter, M6-05a/b ship the Pacts, M6-02b ships the Cleanse — and all three are the same
for every class. A milestone whose goal is *"a corruption to gamble with"* that ships one corruption
for three classes has shipped half of GD §10.

So the row takes all three relationships rather than the Emberwright's alone: they are five numbers
on one block read by two objects, and splitting them across the class that has one and the classes
that already shipped would mean editing `Veilrot` twice for the same reason.

## The one clause that cannot ship, refused for M6-05a's own reason

CH §3.1's Oathbound row has three clauses. Two are numbers. The third is *"**Pact effects are 25 %
weaker for him**"*, and it is **the multiplication [M6-05a](M6-05a-what-a-pact-is.md) refused**, in
the same six files and with the same three getting it backwards:

- `IEffect` is a marker interface with zero members, so scaling one needs a `Corrupt`-shaped member
  on six primitives — that spec's whole ruling.
- **And here it is strictly worse than it was there**, because M6-05a ruled that a Pact is *authored*
  and GD §13.2's own examples carry **downsides**: *"+45 % damage, **−25 max HP**"*. Making a Pact
  25 % weaker makes the −25 a −18.75, which is the Oathbound's penalty for resisting corruption being
  a **discount on the corruption**. There is no sign convention that reads both halves right.

**Refused in writing**, M5-06b rule 3's shape: named, costed, and handed on. It is one more line on
the [parking lot](../ROADMAP.md#parking-lot) beside GD §13.2's table-versus-examples contradiction,
which is the same argument seen from the other end, and it is promoted by the owner's ruling on that
line or by **M7-04**, which authors eighty-one nodes against whichever answer it gets.

## In this build a Pact is the only thing that raises the meter, and that collapses two dials into one

CH §3.1 scopes the Oathbound's reduction to *"from Pact nodes"*; CH §3.2 says the Gravecaller
*"gains +50 % faster"* with no scope. Two different dials — except that grepped,
[M6-06b](M6-06b-four-ordeals-and-two-refusals.md) rule 6 already counted the callers: *"Hunger has
exactly one non-test caller after this task, [M6-05b](M6-05b-the-offer-that-rolls-one.md)'s Pact
take, **which is the only thing in the build that raises the meter**."* GD §10.2's ambient gain does
not exist, the Revenant does not exist, and cleansing shrines are M6-04's *Out of scope*.

**So one multiplier is both rows, today**, and the day a second source of Veilrot ships the
Oathbound's dial has to become source-scoped. That is written down rather than discovered:
`Relationship_OnlyAPactRaisesTheMeter` sweeps `Soulvail.Core` for callers of `Veilrot.Gain` and fails
the day there is a second, which is `PaletteTests.Reads`' technique aimed at a caller set rather than
a field. Its message names **M7-01** (the Revenant, GD §10.2's 50 threshold) as the likeliest second.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/VeilrotSpec.cs` | Core | CH §3's third column as five authored dials |
| `Core/Run/Veilrot.cs` | Core | **Substantial.** The start, the multiplier, the damage that tracks the meter, and the spend |
| `Core/Combat/SkillRunner.cs` | Core | **Substantial.** A cast bought through a cooldown, once per cooldown |
| `Tests/Core/Run/ClassVeilrotTests.cs` | Tests.Core | The five dials, the three shipped classes, and the clause that is refused |
| `Tests/Core/Combat/PaidCastTests.cs` | Tests.Core | The paid cast, its governor, and the runs that never pay |
| *small edits* | Core, Game | `Core/Content/CharacterSpec.cs` — `veilrot`, optional and last beside `kindling` (rule 1); `Core/Progression/SanctumShop.cs` — the Cleanse price (rule 4); `Core/Run/RunSession.cs` — the block reaches the meter, the shop and the runner; `Game/Authoring/CharacterDefinition.cs` — a Veilrot foldout; `Data/Characters/*.asset` ×3 — rule 2's table |
| *ripple* | Tests.Core | **13 `new SkillRunner(...)` sites across 5 files** are untouched (rule 6); `VeilrotTests` and `SanctumShopTests` gain a neutral-block row each, asserting M6-04's and M6-02b's numbers are what a class with no relationship still gets |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// CH §3's Veilrot column, as authored data: what GD §10's meter does differently for this class.
/// Null on a class the Veil treats ordinarily.
/// </summary>
/// <remarks>
/// <b>Five neutral dials and no kind enum</b>, which is
/// <see href="M6-06a-what-an-ordeal-is.md">M6-06a</see> rule 2's shape and its argument: three
/// classes turn three different dials, and the version that reads them with a
/// <c>switch (character.Id)</c> is ADR-0009's ban with the word <em>effect</em> removed. A class that
/// leaves a dial neutral costs nothing, which is what <see cref="ScalingSpec"/> and
/// <see cref="OverflowSpec"/> already look like.
/// </remarks>
public sealed class VeilrotSpec
{
    /// <param name="startingVeilrot">
    /// What a fresh run of this class opens at, in [0, <c>Veilrot.Max</c>). The Gravecaller's 15
    /// (CH §3.2). <b>Below <c>Max</c> rather than at or below it</b>: a class that started Claimed
    /// would begin every run on a hundred-second clock, which is a different game.
    /// </param>
    /// <param name="gainMultiplier">
    /// What every <c>Gain</c> is multiplied by. The Gravecaller's 1.5, the Oathbound's 0.6. Finite
    /// and above zero — zero would be a class Pacts are free for, which is the temptation removed
    /// rather than resisted.
    /// </param>
    /// <param name="cleansePriceMultiplier">
    /// What GD §13.3's Cleanse costs, times this. The Oathbound's 0.5 (CH §3.1). Finite and above
    /// zero.
    /// </param>
    /// <param name="damagePerPoint">
    /// Fractional weapon damage per point on the meter. The Gravecaller's 0.01 (CH §3.2). Finite and
    /// not negative.
    /// </param>
    /// <param name="instantCastCost">
    /// What one cast through a cooldown costs, or <b>0 for a class that cannot buy one</b>. The
    /// Emberwright's 5 (CH §3.3). Finite and not negative.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any of the five is outside its range.</exception>
    /// <exception cref="ArgumentException">
    /// All five are neutral, which is a relationship that is no relationship — M6-06a rule 3's
    /// refusal, and the reason a class without one authors <see langword="null"/> instead.
    /// </exception>
    public VeilrotSpec(
        float startingVeilrot = 0f,
        float gainMultiplier = 1f,
        float cleansePriceMultiplier = 1f,
        float damagePerPoint = 0f,
        float instantCastCost = 0f);

    public float StartingVeilrot { get; }
    public float GainMultiplier { get; }
    public float CleansePriceMultiplier { get; }
    public float DamagePerPoint { get; }
    public float InstantCastCost { get; }
}

public sealed class CharacterSpec
{
    // ... M6-07a's `kindling`, then, optional and last (rule 1):
    //     VeilrotSpec veilrot = null

    /// <summary>How the Veil treats this class, or <see langword="null"/> for ordinarily.</summary>
    public VeilrotSpec Veilrot { get; }
}
```

```csharp
namespace Soulvail.Core.Run;

public sealed class Veilrot
{
    // ... M6-04's four arguments, then, optional and last (rule 6):
    //     VeilrotSpec relationship = null

    /// <summary>
    /// Whether <paramref name="amount"/> could be spent right now. The predicate
    /// <see cref="Spend"/> is the invariant behind — M6-01a rule 7's pairing.
    /// </summary>
    public bool CanSpend(float amount);

    /// <summary>
    /// Takes <paramref name="amount"/> off the meter as a price rather than as a cleanse — rule 5.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanSpend"/> is false.</exception>
    public void Spend(float amount);

    /// <summary>What one cast through a cooldown costs this class, or 0 — <c>SkillRunner</c>'s read.</summary>
    public float InstantCastCost { get; }
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class SkillRunner
{
    // ... the three arguments that exist, then, optional and last — 13 sites over (rule 6):
    //     Veilrot veilrot = null

    /// <summary>
    /// How many casts this run has bought through a cooldown. Zero for every run of two of the
    /// three shipped classes, and the one number a playtest reads to know the mechanic fired.
    /// </summary>
    public int PaidCasts { get; }

    /// <summary>
    /// Whether the active at <paramref name="index"/> has already been bought during the cooldown it
    /// is currently serving — rule 7's governor.
    /// </summary>
    public bool WasPaidFor(int index);
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>
/// An ability was cast through its cooldown and the Veil was charged for it. Beside
/// <c>SkillCast</c> rather than a flag on it: every subscriber of that event would otherwise have to
/// learn a field that is false in every run of two classes out of three.
/// </summary>
public readonly struct CastBought
{
    public CastBought(ContentId skillId, float cost, float veilrotAfter);

    public readonly ContentId SkillId;
    public readonly float Cost;
    public readonly float VeilrotAfter;
}
```

## Behaviour

1. **The block is optional and last on `CharacterSpec`, beside M6-07a's.** `new CharacterSpec(...)`
   has **64 sites across 45 files**, and this is the second block appended in two tasks for the same
   counted reason. Null means *the Veil treats this class the way GD §10 describes and no other way*,
   which is `ShieldSpec`'s and `MinionSpec`'s rule; a block with all five dials neutral is refused at
   the constructor, because it is a row in CH §3's table that says nothing (M6-06a rule 3).
2. **The three shipped relationships, authored.**

   | Class | CH §3 | `startingVeilrot` | `gainMultiplier` | `cleansePriceMultiplier` | `damagePerPoint` | `instantCastCost` |
   |---|---|---|---|---|---|---|
   | **Oathbound** | *Resists* | 0 | **0.6** | **0.5** | 0 | 0 |
   | **Gravecaller** | *Thrives* | **15** | **1.5** | 1 | **0.01** | 0 |
   | **Emberwright** | *Spends* | 0 | 1 | 1 | 0 | **5** |

   Every one of these is CH §3's own number. **The Oathbound's third clause is not in the table** —
   see the refusal above — and it is the only thing CH §3 asks for that this task does not build.
3. **A fresh run starts at the class's number and a resumed one does not, and the meter is silent
   either way.** `startingVeilrot` is applied in the constructor, before anything subscribes, so
   nothing is published — M6-04 rule 9's rule (*a resume is not news*) extended to the other end of
   the same run, because `RunStarted` is what seeds
   [M6-03b](M6-03b-the-meter-on-the-right-edge.md)'s meter from `RunState.Veilrot` and a
   `VeilrotChanged` inside `RunSession.Start` would reach a HUD that has not subscribed. **A restore
   overwrites it rather than adding to it**: a Gravecaller resumed at 40 comes back at 40, not at 55,
   and `Restore_DoesNotReapplyTheStart` is the row, because that is the class of bug M2-14b's whole
   fixture exists for.
4. **The gain multiplier applies before M6-06b's Hunger and before M6-04's clamp; the cleanse price
   is rounded and floored at 1.** `Gain(a)` becomes `a × GainMultiplier × Ordeals.VeilrotMultiplier`,
   clamped at `Max` — so a 15-Rot Pact is 9 for an Oathbound, 22.5 for a Gravecaller, and 33.75 for a
   Gravecaller under Hunger, and a run at 85 still arrives at exactly 100 and Claims.
   **`Cleanse` is untouched by `GainMultiplier`**, M6-06b rule 6's ruling one dial over — a cleanse is
   not a gain, and multiplying it would make *resisting* corruption also mean *resisting the cure*.
   What the Oathbound gets instead is `SanctumShop.PriceOf(Cleanse)` × 0.5 = **30**, rounded with
   `MathF.Round` and floored at **1**: a free service is the *worthless* half of M6-02b rule 7's
   refusal arriving from the price side, and a shop row that reads 0 is a button nobody believes.
5. **`Spend` is not `Cleanse` and the difference is the refusal.** `Cleanse` clamps at zero and
   wastes the remainder, because buying a 15-point cleanse at 8 Rot is the player's decision (M6-04
   rule 3). `Spend` **throws** when the meter is short, because it is a price: a cast that happened
   and was not paid for is the invariant `CanSpend` guards, and M6-04 rule 3 predicted exactly this —
   *"a different question with a different failure."* Neither verb ever un-Claims, and **`Spend` does
   not cross thresholds downward silently**: it recomputes the three lower states the way `Cleanse`
   does, so a run that spends from 26 to 21 loses the 25 threshold's enemy speed bonus. That is the
   Emberwright paying twice for the same cast and it is correct — the meter is the meter.
6. **Three consumers take the block optional and last, and the wiring is asserted rather than
   trusted.** `Veilrot` (M6-04's own fixture and `RunSession`), `SanctumShop` and `SkillRunner`
   (**13 sites across 5 files**) each default to no relationship, which is
   [M6-06b](M6-06b-four-ordeals-and-two-refusals.md) rule 5's ruling and its stated condition: the
   null is *legitimate* — every fixture in the project and any class that authors none — so what
   keeps it honest is three rows asserting `RunSession` passes the run's block to all three.
7. **A paid cast fires through the cooldown without rescheduling it, once per cooldown, and the
   governor is the load-bearing half.** `SkillRunner.Tick` already skips an active whose
   `_readyAt` is in the future. The new branch: *not ready*, **and** the authored condition is met,
   **and** `InstantCastCost > 0`, **and** `CanSpend(cost)`, **and** this cooldown has not been bought
   already → `Spend(cost)`, fire the effects, publish `SkillCast` and `CastBought`, and **leave
   `_readyAt` where it is.**
   - **Leaving the clock alone is CH §3.3 read literally** — *"any ability may be cast instantly
     off-cooldown"* says the ability is on cooldown and fires anyway, not that the cooldown restarts.
   - **Without the once-per-cooldown latch it is a runaway**, and the arithmetic is why it is a rule
     rather than a nicety: if a paid cast rescheduled `_readyAt`, a trigger that stays true pays 5
     Rot **every tick** — sixty casts and three hundred Rot a second — and if it did not reschedule
     and had no latch, the same. With the latch, an active on a 20 s cooldown casts twice per 20 s,
     once free and once for 5, so the whole mechanic is bounded at `5 × actives / cooldown` Rot per
     second. The Emberwright's v1 tree carries one or two Actives
     ([M6-08](M6-08-emberwright-tree-v1.md)), which is **0.25 Rot a second at most**.
   - The latch clears when the cooldown actually ends, in the same comparison that makes the skill
     ready — one `bool[]` beside `_readyAt`, no second clock.
8. **The machine pays and the player does not, and that is a ruling with a measured reason.**
   `SkillRunner.Cast` — M3-07a's manual door — **still refuses an active that is not ready**, and
   nothing here changes it. CH §3.3 does not say who spends, CH §4.2 says *"every active skill
   auto-casts when off cooldown and its trigger condition is met. The player does nothing"*, and
   [M5-08](M5-08-acceptance-and-tag.md) measured **80 of 84 casts automatic across two full played
   runs**. So the auto path is where the mechanic actually lives, and the manual half needs something
   this task deliberately does not build: a thumb button that draws a Rot price on a greyed skill,
   which is a screen, a localisation row and a refusal to draw — [M6-03a](M6-03a-the-sanctum-screen.md)'s
   whole subject on a 24 dp cell that [ledger row 3](../ROADMAP.md#carry-forward-into-m6) already says
   holds neither an icon nor a word. **M6-11**'s call, on a played Emberwright.
9. **The Gravecaller's damage rides the meter as one `PercentAdd` rewritten when the meter moves.**
   `DamagePerPoint × Value` on `Weapon.Damage`, sourced to the meter, ADR-0008's pooling so a run at
   60 Rot is ×1.60 rather than 1.01⁶⁰ (×1.82) — M6-07a rule 3's arrangement for Kindling and the same
   reason. **Rewritten on a change and never on a tick**: the meter moves on a Pact, a Cleanse, a
   Spend and the Ordeal multiplier, which is a handful of times a stage, against the sixty a second a
   `Tick`-driven rewrite would put on the one stat the HUD is subscribed to (M6-04 rule 7). At 100
   Rot it is **+100 %**, and it multiplies with the Claiming's own +100 % `PercentMult` — a Claimed
   Gravecaller at 100 does ×2.0 × 2.0 = **four times** its base weapon damage on a hundred-second
   clock, which is CH §3.2's *"rushes to 100 Veilrot on purpose"* arriving as arithmetic. That number
   is pinned rather than left to be discovered by a playtest.
10. **Nothing here allocates.** Five floats on an object built once at boot, one `bool[]` sized with
    `SkillRunner.MaxActives` at construction, one `Modifier` rewritten at most a few times a stage,
    and one comparison added to a loop that already runs.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_CarriesItsFive` | every dial set / — / all five read back |
| `Spec_DefaultsAreNeutral` | `new VeilrotSpec()` / — / throws — a relationship that is no relationship, rule 1 |
| `Spec_RefusesAnImpossibleDial` | a start at 100 and above; a zero, negative or non-finite multiplier; a negative `damagePerPoint` or `instantCastCost` / — / throws in each case, naming the field |
| `Spec_AcceptsAStartJustBelowMax` | 99.9 / — / no throw; 100 throws — the Public API's stated reason |
| `Oathbound_ResistsTheVeil` | `Oathbound.asset` / converted / 0 / **0.6** / **0.5** / 0 / 0 — rule 2 |
| `Gravecaller_ThrivesOnIt` | `Gravecaller.asset` / converted / **15** / **1.5** / 1 / **0.01** / 0 — rule 2 |
| `Emberwright_SpendsIt` | `Emberwright.asset` / converted / 0 / 1 / 1 / 0 / **5** — rule 2 |
| `Relationship_TheOathboundsThirdClauseIsNotBuilt` | `typeof(VeilrotSpec)` / reflection / **no dial scales a Pact's effects**, and the message names M6-05a's ruling and the parking-lot line — the refusal, pinned so nobody adds one without reading why |
| `Relationship_OnlyAPactRaisesTheMeter` | a sweep of `Soulvail.Core` for callers of `Veilrot.Gain` / — / exactly one non-test caller — the collapse of two dials into one, and the row that fails the day there is a second |
| `Start_TheGravecallerOpensAtFifteen` | a fresh Gravecaller run / — / `RunState.Veilrot` 15, **nothing published**, and the 25 state off — rule 3 |
| `Start_TheOtherTwoOpenAtZero` | fresh runs of both / — / 0 |
| `Start_ANullBlockOpensAtZero` | a run with no relationship / — / 0, and every M6-04 number unchanged — rule 6 |
| `Restore_DoesNotReapplyTheStart` | a Gravecaller saved at 40, killed, resumed / — / **40**, not 55 — rule 3 |
| `Gain_IsMultipliedByTheClass` | a 15-Rot Pact / taken by each class / **9**, **22.5**, **15** — rule 4 |
| `Gain_StacksWithHunger` | a Gravecaller under Hunger / a 15-Rot Pact / **33.75** — rule 4, M6-06b rule 6's multiplier surviving a second one |
| `Gain_TheClampStillWins` | an Oathbound at 95, a 20-Rot Pact / — / **100** and one `ClaimingBegan` — rule 4 |
| `Cleanse_IsNotMultipliedByTheGainDial` | a Gravecaller at 40 / `Cleanse(15)` / **25**, not 17.5 — rule 4 |
| `Price_TheOathboundCleansesAtHalf` | the shipped `Descent.asset` / `PriceOf(Cleanse)` per class / **30**, 60, 60 — rule 4 |
| `Price_ACleanseIsNeverFree` | a multiplier of 0.001 on a 60 price / — / **1**, not 0 — rule 4's floor |
| `Price_TheOtherThreeServicesAreUnmoved` | all three classes / `PriceOf` of Reroll, Banish and Heal / M6-02b's numbers exactly, including the doubling — the blast radius of this task, bounded |
| `Spend_TakesItOff` | 40 Rot / `Spend(5)` / 35, one `VeilrotChanged` of −5 |
| `Spend_RefusesWhatIsNotThere` | 3 Rot / `CanSpend(5)` false, `Spend(5)` / `InvalidOperationException`, meter unmoved — rule 5 |
| `Spend_IsNotACleanse` | 3 Rot / `Cleanse(5)` then `Spend(5)` / the first clamps to 0, the second throws — rule 5, the two verbs contrasted in one row |
| `Spend_LosesAThresholdOnTheWayDown` | 26 Rot / `Spend(5)` / 21, and the next spawn carries **no** Veilrot speed modifier — rule 5's stated cost |
| `Spend_NeverUnClaims` | Claimed, cleansed to 10 / `Spend(5)` / `IsClaimed` still true — M6-04 rule 6 |
| `Paid_FiresThroughACooldown` | an Emberwright, an active on cooldown with its trigger met, 40 Rot / one tick / one `SkillCast`, one `CastBought(id, 5, 35)`, `PaidCasts` 1 — rule 7 |
| `Paid_DoesNotRescheduleTheCooldown` | the same / — / `CooldownFraction` is exactly what it was before the cast — rule 7 |
| `Paid_OncePerCooldown` | the same, trigger held true / 600 ticks / **one** paid cast and 5 Rot spent, not 600 and 3 000 — rule 7's governor, which is the whole reason it is a rule |
| `Paid_AgainAfterTheCooldownTurnsOver` | a 2 s cooldown, trigger held / 10 s / the free casts and **four** paid ones, alternating — rule 7 |
| `Paid_StopsWhenTheMeterIsShort` | 3 Rot, trigger met / ticks / no cast, no throw, nothing published — rule 7, `CanSpend` before `Spend` |
| `Paid_NeverHappensForTheOtherTwoClasses` | an Oathbound and a Gravecaller run, every trigger held / 600 ticks / `PaidCasts` 0 and the meter unmoved — rule 7 |
| `Paid_NeverHappensWithoutAMeter` | `new SkillRunner(..., veilrot: null)` / the same / 0 paid casts, no throw — rule 6 |
| `Paid_ManualIsStillRefused` | an Emberwright, an active on cooldown, 40 Rot / `Cast(index, now, auto: false)` / **false**, no cast, no spend — rule 8 |
| `Paid_ATriggerThatIsFalseBuysNothing` | 40 Rot, the condition unmet / ticks / nothing — rule 7: the price buys the cooldown, never the condition |
| `Paid_AManualSkillIsNeverBought` | an active set to Manual, its condition true, on cooldown / ticks / nothing — `SkillRunner.Tick`'s existing Manual rule, which the new branch sits below rather than beside |
| `Damage_RidesTheMeter` | a Gravecaller at 0, 30, 60 Rot / — / ×1.00, ×1.30, ×1.60 on `Weapon.Damage` — rule 9 |
| `Damage_PoolsAdditively` | 60 Rot / — / ×1.60, not 1.01⁶⁰ — rule 9, ADR-0008's order |
| `Damage_DoesNotRewritePerTick` | a Gravecaller at 40 / 600 ticks with the meter still / **no** `Stat.Changed` on `WeaponDamage` — rule 9 |
| `Damage_AClaimedGravecallerIsFourTimesBase` | Claimed at 100 Rot / — / ×4.0, and the row's message names both multipliers — rule 9, pinned rather than discovered |
| `Damage_IsZeroForTheOtherTwo` | an Oathbound and an Emberwright at 80 Rot / — / no meter-sourced modifier on `WeaponDamage` |
| `Run_TheBlockReachesAllThree` | a live `RunSession` / reflection over the meter, the shop and the runner / each holds the run's `VeilrotSpec` — rule 6 |
| `Veilrot_AllocatesNothing` | 100 000 gains, spends and cleanses under a full block / `AllocationAssert.None` / zero — rule 10 |
| `Paid_AllocatesNothing` | 100 000 ticks with a paid cast available / `AllocationAssert.None` / zero — rule 10 |

**Guard rows are implied, not listed:** nulls to `VeilrotSpec`'s readers, a non-finite amount to
`Spend`, and every existing `Veilrot`, `SanctumShop` and `SkillRunner` guard firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Start a Gravecaller. *Expected: the meter on the right edge is already at 15 before
   anything has happened, and the debug overlay's damage figure is ×1.15.*
2. **[Editor]** Take a 15-Rot Pact as each class. *Expected: the meter moves 9, 22.5 and 15 — rule 4,
   which is the one number a player could plausibly notice.*
3. **[Editor]** Open the Sanctum as an Oathbound with 40 Essence. *Expected: Cleanse reads **30** and
   is live, where the other two classes read 60 and it is dead.*
4. **[Editor]** Play an Emberwright with an Active taken and a debug grant of 40 Rot. *Expected: the
   skill fires twice as often as its cooldown allows while the Rot lasts, the meter steps down in
   fives, and it stops firing early the moment the meter is under 5. Nothing about the cooldown ring
   changes — rule 7.*
5. **[Editor]** Play a Gravecaller to 100 Rot and let the Claiming start. *Expected: the damage
   figure is four times where it started, and a hundred seconds later the run is over — rule 9,
   looked at rather than reasoned about.*
6. **[device]** Nothing new. Every readout this task moves is
   [M6-03b](M6-03b-the-meter-on-the-right-edge.md)'s meter and
   [M6-03a](M6-03a-the-sanctum-screen.md)'s price row, both of which are already on
   [ledger row 1](../ROADMAP.md#carry-forward-into-m6).

## Out of scope

- **The Oathbound's *"Pact effects are 25 % weaker"*.** Refused above, with the owner.
- **A thumb button that buys a cast.** Rule 8, with the measurement behind it and the task that would
  take it.
- **A readout for `PaidCasts` or for the Gravecaller's damage bonus.** Both are debug-overlay numbers.
  A third HUD element is **M6-11**'s call, beside M6-07a's Kindling ramp.
- **A second source of Veilrot gain.** The collapse above depends on there being one, and
  `Relationship_OnlyAPactRaisesTheMeter` is what notices when that stops being true.
- **Cleansing shrines, and GD §10.2's ambient gain.** M6-04's *Out of scope*, unchanged.
- **Balancing any of the five dials.** They are CH §3's numbers. **M8-05**, with **M6-11**'s
  instrument — and rule 9's ×4.0 is the first thing that pass should look at.

## As built

**Built to the Public API.** `VeilrotSpec` with five dials and the all-neutral refusal;
`CharacterSpec.Veilrot`, optional and last; `Veilrot` takes the block, opens at its start silently,
multiplies every gain before Hunger, keeps one `PercentAdd` on weapon damage for a class with a
per-point dial, and gains `CanSpend`/`Spend`/`InstantCastCost`; `SanctumShop` prices the Cleanse ×
the dial, rounded and floored at 1; `SkillRunner` takes the meter and buys a cast through a cooldown
once per cooldown, publishing `SkillCast` then `CastBought`. `RunSession` passes the block to the
meter and the shop, and the meter to the runner. The three assets carry rule 2's table.
**EditMode 2 927 → 2 979 (+52), twice; PlayMode 26 / 0 / 0, twice.**

### Deviations

1. **`CastBought` lives in `Core/Events/SkillEvents.cs`**, beside `SkillCast`. Not in the table;
   M6-07b deviation 1's precedent.
2. **Five rows sit in `Tests/Game/Authoring/CharacterDefinitionTests.cs`**: the three class rows,
   `Price_TheOathboundCleansesAtHalf` and `Veilrot_AllFiveNeutralConvertsToNull`.
   `Soulvail.Tests.Core` cannot open an asset (M6-07b deviation 5). The price row reaches a live
   shop through `ClassVeilrotTests.CleansePrice`, a public static helper.
3. **The latch clears in `Fire`, not in the comparison that makes a skill ready.** `Fire` is where a
   new cooldown starts, so either door (trigger or thumb) hands the next wait a fresh latch. Clearing
   on the comparison would miss a skill whose ready tick was spent casting an earlier entry. `Add`
   and `Reset` clear it too. Still one `bool[]` and no second clock.
4. **`Paid_AgainAfterTheCooldownTurnsOver` asserts five paid casts, not four.** Over 10 s a 2 s
   cooldown casts free at 0, 2, 4, 6 and 8, and each free cast is bought once a frame later. The row
   asserts the order `FPFPFPFPFP` and 15 Rot left of 40.
5. **A bought cast's `SkillCast.Cooldown` is the wait left** (`readyAt − now`), not the effective
   cooldown. The clock did not move, and the field's own doc says *"seconds until it may fire
   again"*.
6. **`Run_TheBlockReachesAllThree` checks that the runner holds the meter, not the spec.** The Public
   API gives `SkillRunner` a `Veilrot`, and the spec reaches it through `InstantCastCost`.
7. **`VeilrotSpec` reads `Veilrot.Max` for its upper bound.** That is the first `Content → Run`
   reference, and it keeps one source for 100 rather than a second literal.
8. **Five rows beyond the table:** `Spend_RefusesANonPrice`, `Damage_FollowsAGainAndACleanse`,
   `Paid_StopsTheMomentTheMeterRunsOut`, `Paid_ReadsTheCostOffTheMeter` and the authoring row in
   deviation 2. Each one is a guard or a premise the table implies.
9. **The ripple is 15 `new SkillRunner(...)` sites in 7 files by today's count, not 13 in 5.** None
   was edited: the argument is optional.
10. **The branch was cut from M6-07b's head**, because `origin/dev` still stops at M6-05b. This PR
    carries 06a, 06b, 07a and 07b until those merge.

### Findings

- **A constructor start runs through `ApplyStates` too.** A class authored to open above 25 would
  open with the 25 row on and nothing published. No shipped class does that; the Gravecaller's 15
  sits under every row.
- **The Gravecaller now opens at ×1.15 weapon damage.** None of its TTK rows moved, because they
  build `PlayerCombat` rather than a run. The stage-9 TTK break in *What does not work yet* is
  measured on a played run, and M6-11 should re-read it with this in.
- **Rule 9's ×4.0 is pinned** (`Damage_AClaimedGravecallerIsFourTimesBase`). M8-05 should look at
  it first.
- **The parking-lot line for the Oathbound's third clause already existed** (M6-00c). Nothing was
  added.
- **Known issue 1 did not fire.** Both PlayMode passes were 26 / 0.
