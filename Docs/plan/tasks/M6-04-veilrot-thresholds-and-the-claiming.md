# M6-04 — Veilrot: a meter that only goes up, four thresholds, and the gamble at the top

**Size:** M · **Depends on:** M6-01b · **Branch:** `m6-04-veilrot-and-the-claiming`
**Design refs:** GD §10, §13.3, §15, §16.4, §19; CH §3.3, §4.2, §4.4; AR §9, §18.1, §18.2, §18.3; ADR-0008 · **Ledger rows:** none directly; this task retires the *deliberate absence* `ContentValidationTests.Written` has carried since M5-06a (rule 10)

## Goal

GD §10's meter exists, its four thresholds mean something, `TriggerField.Veilrot` finally has a
writer, and a run at 100 can buy ninety seconds of godhood with the rest of its life.

## Why this is built before the Sanctum, and why that is the first time build order is not ID order

The Sanctum's fourth service is **Cleanse: −15 Veilrot for 60 Essence** (GD §13.3). There is nothing
to cleanse until this task exists, and the two ways of shipping
[M6-02b](M6-02b-four-things-essence-buys.md) first are both worse than reordering: three services
with a fourth added later is a screen re-dressed twice, and four services with one that refuses is
exactly the defect [M5-08a](M5-08a-splash-offers-what-install-refuses.md) was written to close —
*a screen may not offer what the model refuses.* So M6 builds **M6-01a → M6-01b → M6-04 → M6-02a →
M6-02b → M6-03a**, and [the ROADMAP](../ROADMAP.md#m6--systems-complete)'s `Depends on` column says
so. The IDs are the ROADMAP's and do not move; what moves is the order they are taken in.

## What GD §10.2 asks for, and what a build with six archetypes can give

| Veilrot | GD §10.2 | Ships here |
|---|---|---|
| **25** | All enemies +5 % move speed | **Yes** — rule 4, one modifier at spawn |
| **50** | A Revenant stalks you each stage, regardless of depth. Ambient audio shifts | **No, and neither half is this task's.** See below |
| **75** | Max HP −20 %. Screen edges begin to fray | **The number, yes** — rule 5. The fraying is a post-process and is [M6-03b](M6-03b-the-meter-on-the-right-edge.md)'s at most, M8-01's at best |
| **100** | The Claiming: +100 % damage, +30 % move speed, dash cooldown halved, −1 % max HP per second until you die | **Yes, all four** — rules 6, 7 |

**The 50 threshold names an enemy this game will not have in V1.** The Revenant is GD §8.1's
eighth archetype (70 HP, threat 18, teleports behind the player every 4 s), GD §8.2 introduces it at
stage 17, and **GD §19 puts it in V2** — *"Remaining 2 biomes + Choir and Revenant"*. M7-01 builds
the Lunger, the Weaver and the Warden, and stops. So the threshold ships **crossed, published and
otherwise silent**: `VeilrotThresholdCrossed(50, entered: true)` goes out, the ambient shift is
M7-07's audio pass, and the stalker becomes a [parking-lot](../ROADMAP.md#parking-lot) line promoted
by **M7-01**, the task that builds archetypes.

**Substituting a shipped archetype is refused rather than forgotten.** GD §8.1's own rule is that
*"if two enemies pressure the player identically, one is redundant and should be cut"*, and the
Revenant's pressure is *denying safe zones* — a thing no Husk, Spitter or Bloater does. A Husk that
follows you every stage is not a weak Revenant; at 4 threat against a 50-Veilrot player it is a free
kill, which would make the 50 threshold a **reward** and invert the whole meter. The honest version
of a threshold nobody can build is one that does nothing and says so.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/Veilrot.cs` | Core | The meter, the four states, the Claiming, and the drain |
| `Core/Events/VeilrotEvents.cs` | Core | `VeilrotChanged`, `VeilrotThresholdCrossed`, `ClaimingBegan` |
| `Core/Combat/PlayerCombat.cs` | Core | **Substantial.** `AnnounceDeath` — one publisher for a death that arrives without damage (rule 8) |
| `Tests/Core/Run/VeilrotTests.cs` | Tests.Core | The meter, each threshold, the latch, the drain, and the death it causes |
| *small edits* | Core | `Core/Ai/EnemySystem.cs` — one modifier after `_scaling.Apply` (rule 4); `Core/Run/RunState.cs` — `Veilrot` and `IsClaimed` reads, replacing M6-01b's zero; `Core/Run/RunSession.cs` — builds the meter, ticks it above `Combat.Tick`, restores it below the tree (rules 2, 9); `Core/Save/RunRecorder.cs` — `Take` passes the meter instead of `0f` |
| *ripple* | Tests.Core, Tests.Game | `ContentValidationTests.Written` gains `Veilrot` with its writer (rule 10); `PlayerCombatTests` gains the announcement's rows; `EnemySystemTests` gains the spawn modifier; `SaveDtoTests`' Veilrot rows stop being about a field nothing writes |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Run;

/// <summary>
/// GD §10's corruption meter: 0–100, never decays, and the only thing in the game that makes the
/// player stronger for being in trouble.
/// </summary>
public sealed class Veilrot
{
    public const float Max = 100f;

    /// <summary>How many rows GD §10.2 has. Four.</summary>
    /// <remarks>
    /// <b>Amended at M6-00b: this was drafted as <c>public static readonly float[] Thresholds</c>
    /// and that is static mutable state, which AR §7 bans.</b> <c>readonly</c> protects the handle
    /// and not the four floats, so any caller could write <c>Thresholds[3] = 5f</c> and every later
    /// comparison in the meter would be wrong for the rest of the session — <c>PaletteTests</c>'
    /// <c>Palette_IsTheOneSanctionedStatic</c> states the rule in as many words, and grepped,
    /// <c>Soulvail.Core</c> has no <c>public static readonly</c> field at all today, so this would
    /// have been the first and nothing would have caught it. Two members handing out floats by value
    /// cost the one reader — <see href="M6-03b-the-meter-on-the-right-edge.md">M6-03b</see>'s HUD
    /// meter — four calls at <c>Start</c>.
    /// </remarks>
    public static int ThresholdCount { get; }

    /// <summary>GD §10.2's four rows, in order: 25, 50, 75, 100.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside <c>[0, ThresholdCount)</c>.
    /// </exception>
    public static float Threshold(int index);

    /// <summary>What one second of the Claiming takes, as a fraction of the maximum it began at.</summary>
    public const float ClaimingDrainPerSecond = 0.01f;

    /// <param name="stats">This run's addresses. The meter puts modifiers on four of them.</param>
    /// <param name="blackboard">Where <c>TriggerField.Veilrot</c> is read from — rule 10.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public Veilrot(PlayerStats stats, PlayerCombat combat, CombatBlackboard blackboard, IDomainEvents events);

    /// <summary>The meter, in [0, <see cref="Max"/>].</summary>
    public float Value { get; }

    /// <summary>True from the moment <see cref="Value"/> first reaches <see cref="Max"/>. A latch — rule 6.</summary>
    public bool IsClaimed { get; }

    /// <summary>Seconds since the Claiming began, or zero. What the drain is a function of.</summary>
    public float ClaimedFor { get; }

    /// <summary>
    /// The <c>PercentMult</c> every enemy spawned from now on wears on its move speed: 0.05 at or
    /// above 25, otherwise 0 — rule 4.
    /// </summary>
    public float EnemySpeedBonus { get; }

    /// <summary>Pacts and Ordeals. Clamps at <see cref="Max"/>; non-positive does nothing — rule 1.</summary>
    public void Gain(float amount);

    /// <summary>
    /// GD §13.3's Cleanse. Clamps at zero, so buying one at 8 Rot is legal and wastes 7 — rule 3.
    /// Never un-Claims.
    /// </summary>
    public void Cleanse(float amount);

    /// <summary>The Claiming's drain, and nothing else. One step per whole second — rule 7.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is negative or non-finite.</exception>
    public void Tick(float dt);

    /// <summary>What a resumed run comes back at. No events, no crossings — rule 9.</summary>
    internal void Restore(float value);
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>The meter moved. Both numbers, for <c>EssenceChanged</c>'s reason.</summary>
public readonly struct VeilrotChanged
{
    public VeilrotChanged(float value, float delta);
    public readonly float Value;
    public readonly float Delta;
}

/// <summary>A threshold was entered or left. Carries direction, because Cleanse goes back down.</summary>
public readonly struct VeilrotThresholdCrossed
{
    public VeilrotThresholdCrossed(float threshold, bool entered);
    public readonly float Threshold;
    public readonly bool Entered;
}

/// <summary>The latch closed. Published once per run, after the 100 crossing — rule 6.</summary>
public readonly struct ClaimingBegan
{
    public ClaimingBegan(float maxHpAtClaiming);

    /// <summary>What the drain is 1 % of, per second. Carried so a bar can draw the countdown.</summary>
    public readonly float MaxHpAtClaiming;
}
```

```csharp
// Core/Combat/PlayerCombat.cs — widened
public sealed class PlayerCombat
{
    /// <summary>
    /// Publishes <c>PlayerDied</c> if the player is dead and it has not been said yet. Rule 8: the
    /// one publisher, now reachable by a death that arrives without a DamageResult.
    /// </summary>
    public void AnnounceDeath(float now);
}
```

## Behaviour

1. **The meter only goes up on its own, and `Gain` is the one verb that does it.** GD §10.1: *"never
   decays."* Nothing ticks it upward, nothing decays it, and the only downward verb is
   <see cref="Cleanse"/>. `Gain(0)` and `Gain(-5)` do nothing and publish nothing —
   `EssenceWallet.Earn`'s rule. **A gain that would exceed 100 clamps**, and the clamp is what makes
   the Claiming reachable by a single +20 Pact from 85 rather than something a run has to hit
   exactly.
2. **The three lower states are recomputed from `Value`, and the Claiming alone is a latch.** GD
   §10.2 is a table of effects *at* a meter reading, and Cleanse is GD §13.3's whole reason to exist,
   so cleansing from 78 to 63 **gives the 20 % max HP back**. Each state is one modifier, on and off
   with the crossing, sourced to the meter so `Stat.RemoveAll(this)` takes it back without knowing
   what else was added since (ADR-0008). The Claiming is the exception GD §10.2 writes in itself:
   *"permanently, until you die."*
3. **`Cleanse` clamps at zero and does not refuse a partial one.** Buying a 15-point cleanse at 8
   Rot takes the meter to 0 and wastes 7, which is the player's decision and not the model's to
   prevent; what [M6-02b](M6-02b-four-things-essence-buys.md) rule 7 does refuse is buying one at **0**, where
   there is nothing to cleanse at all. There is deliberately **no `Spend`** beside it: CH §3.3's
   Emberwright spends 5 Veilrot to cast off-cooldown and *must have* the 5, which is a different
   question with a different failure, and a port grows a member when its mechanic lands (AR §6).
   M6-07 adds `CanSpend`/`Spend`; this task ships the one caller that exists.
4. **The 25 threshold is applied when an enemy is *spawned*, and a body already standing keeps its
   speed.** `EnemySystem.Spawn` adds `new Modifier(PercentMult, _veilrot.EnemySpeedBonus, _veilrot)`
   to `agent.MoveSpeed` immediately after `_scaling.Apply(agent, _depth)`, where
   `EnemyAgent.Initialise`'s `MoveSpeed.RemoveAll()` has already cleared the last rental's stack.
   **This is M5-06a rule 3's trade, and the bound is shorter**: there, a node buffed the *next*
   Wight and the lag was one lifespan; here a crossing speeds up the *next* body and the lag is the
   rest of the current wave. Veilrot is gained at a level-up, which pauses the run, so the bodies
   that miss it are the ones already on screen when the player took the Pact. **Walking
   `EnemyRegistry.Alive` on the crossing was weighed and refused**: it needs the meter to hold the
   registry, or a second call site in `RunSession` and a method on `EnemySystem`, for five per cent
   of one wave's move speed.
5. **The 75 threshold is one `PercentMult` of −0.20 on `PlayerStat.MaxHp`, and it is a `PercentMult`
   rather than a `PercentAdd` on purpose.** Pooled additively it would cancel against a +20 % max-HP
   node and the threshold would silently do nothing for a run that had taken one; as its own factor
   it is a true fifth of whatever the player has built, which is what GD §10.2 means by −20 %.
   `Modifier`'s own remarks name this class of thing — *"the rare, loud multipliers — Focus at full
   ramp, a boss phase, the Claiming."*
6. **The Claiming is four modifiers and a latch, and it closes exactly once per run.** On the tick
   `Value` first reaches 100: `WeaponDamage` `PercentMult +1.0`, `MoveSpeed` `PercentMult +0.30`,
   `MovementSkillCooldown` `PercentMult −0.50`, all sourced to one object, and a fifth source
   reserved for the drain. `ClaimingBegan` carries `Health.MaxHp.Value` **as it stands after the
   other three have gone on**, which is the number the drain is a percentage of. `IsClaimed` never
   goes false again: cleansing to 40 leaves the meter at 40, the 75 and 25 states off, and the
   Claiming on — which is the one state combination that looks like a bug and is not, so it has its
   own row.
7. **The drain is one modifier rewritten once per whole second, not per frame, and it reaches zero
   in a hundred.** `Tick` accumulates `dt` and, on each whole second of `ClaimedFor`, removes the
   drain source and re-adds `new Modifier(PercentMult, −0.01 × seconds, drainSource)` on `MaxHp`,
   floored at −1. Per-frame rewriting would raise `Stat.Changed` sixty times a second on the one
   stat the HUD is subscribed to, for a step GD §10.2 states in seconds. **A hundred seconds to
   zero is the design's own arithmetic read back:** GD §10.3 says the Claiming buys *"90 seconds of
   godhood to push two more stages"*, which is what −1 % of the starting maximum per second gives
   and what −1 % of the *current* maximum would never give, since the compounding version is
   asymptotic and never kills anybody. The drain multiplies with rule 5's −20 % rather than
   replacing it, so a Claimed run at 100 Rot is on ×0.8 × (1 − 0.01 t).
8. **A death caused by a vanishing maximum is announced by the same object that announces every
   other one, and `Health` asked for this in writing.** `Health.OnMaxHpChanged` already pulls
   `Current` down with a falling maximum and its own remarks say: *"no `DamageResult` exists to
   carry `Killed` … that is a content mistake rather than a mechanic (**nothing in V1 removes max
   HP**), and the first thing that does needs the owner to check `IsDead` after the change."*
   **The Claiming is the first thing in V1 that removes max HP**, and rule 5 is the second.
   `PlayerDied` is published from exactly one line in the project — `PlayerCombat.ApplyDamage`'s
   `result.Killed` branch — so a run drained to zero would end (`RunSession.Tick` reads
   `Combat.IsDead`) with the HUD, the haptics and every other subscriber never told. So
   `PlayerCombat` gains `AnnounceDeath(now)`: publishes once if dead and not yet said, `ApplyDamage`
   routes its own branch through it, and `Reset` clears the flag. The meter calls it after each
   drain step. **One publisher, one flag, one place** — a second publisher with its own flag would
   make *"exactly once per life"* a property of two objects agreeing.
9. **The meter ticks immediately above `State.Combat.Tick`, and restores below the tree.** Above
   combat, because the blackboard field rule 10 writes has to be this tick's before
   `State.Skills.Tick` reads a trigger over it; and above the death check, because a drain step that
   kills must end the run on the tick it happened rather than on the next one. `Restore` sets the
   value, recomputes every state and the latch, and **publishes nothing** —
   `EssenceWallet.Restore`'s rule (M6-01a rule 8): a resume is not news, and a `ClaimingBegan`
   published inside `RunSession.Start` would reach a HUD that has not subscribed yet. A run restored
   at exactly 100 comes back Claimed with `ClaimedFor` at **zero**, which is the one thing a save
   cannot carry without a field: the drain restarts, and that is a `Continue` being worth up to a
   hundred seconds. It is recorded here rather than discovered, and it is smaller than the free
   cooldown reset the same `Continue` already grants.
10. **`TriggerField.Veilrot` gets its writer, and a five-milestone deliberate absence ends.**
    `Veilrot.Tick` writes `Blackboard.Veilrot`, which is `ProjectileSystem`'s arrangement for
    `IncomingProjectiles` rather than `PlayerCombat.UpdateBlackboard`'s for the other eight — and
    `UpdateBlackboard`'s last line already pairs the two: *"Veilrot and IncomingProjectiles are
    deliberately untouched."* `ContentValidationTests.Written` gains `Veilrot` with `Veilrot.Tick`
    beside it, which retires the entry whose *absence was the whole value of the row* (M5-06a rule
    8) and unblocks CH §4.2's Rot Nova for **M7-04** — the clause
    [M5-06b](M5-06b-gravecaller-tree-v1.md) rule 6 refused to author.
11. **Nothing here allocates.** Four floats, a `bool`, two `object` sources allocated once at
    construction, and a `Stat.RemoveAll`/`Add` pair at most once a second. `Tick` on an unclaimed run
    is one comparison.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Meter_StartsAtZero` | a new meter / — / `Value` 0, `IsClaimed` false, nothing published |
| `Meter_GainAdds` | `Gain(15)` / — / 15, one `VeilrotChanged` carrying **15 and +15** |
| `Meter_GainIsSilentForNothing` | `Gain(0)`, `Gain(-5)`, `Gain(NaN)` / — / unmoved, nothing published, no throw — rule 1 |
| `Meter_GainClampsAtMax` | 85 / `Gain(20)` / **100**, delta **+15**, and the Claiming fires — rule 1 |
| `Meter_NeverDecays` | 40 / 600 s of `Tick` / still 40 — rule 1 |
| `Meter_TheThresholdsAreNotAMutableStatic` | `typeof(Veilrot)` / reflection / **no `public static` field**, `ThresholdCount` 4, and `Threshold(0…3)` are 25, 50, 75, 100 — AR §7, the Public API's amendment |
| `Meter_ThresholdRefusesAnIndexOutsideIt` | `Threshold(-1)`, `Threshold(4)` / — / throws |
| `Meter_CleanseTakesAway` | 40 / `Cleanse(15)` / 25, one event carrying −15 |
| `Meter_CleanseClampsAtZero` | 8 / `Cleanse(15)` / **0**, delta −8 — rule 3 |
| `Twenty5_SpeedsWhatSpawnsNext` | a run crossing 25 / an enemy spawned / its `MoveSpeed` carries a `PercentMult` of **0.05** sourced to the meter, on top of depth scaling — rule 4 |
| `Twenty5_LeavesWhatIsStanding` | one Husk alive, then the crossing / — / **its speed is unmoved**, and the next spawn is faster — rule 4's stated cost |
| `Twenty5_CleansingBelowItSlowsTheNextSpawn` | 30, then `Cleanse(15)` / a spawn / no Veilrot modifier — rule 2 |
| `Fifty_IsPublishedAndDoesNothingElse` | 50 crossed / — / one `VeilrotThresholdCrossed(50, true)`, **no spawn, no stat moved, nothing in the log** |
| `Seventy5_TakesAFifthOfWhateverYouBuilt` | 200 max HP from a +40 % node / 75 crossed / max HP **×0.8 of 200**, and the modifier is a `PercentMult` — rule 5 |
| `Seventy5_PullsCurrentHpDownWithIt` | full at 200 / 75 crossed / `Current` is 160, not 200 — `Health.OnMaxHpChanged`, asserted here because this is its first caller |
| `Seventy5_IsGivenBackByCleansing` | 78, then `Cleanse(15)` / — / max HP back to 200, `Current` **still 160** — a lost maximum is not a heal |
| `Claiming_PutsOnAllFour` | 100 reached / — / damage ×2, move speed ×1.3, dash cooldown ×0.5, and one `ClaimingBegan` carrying the post-buff maximum — rule 6 |
| `Claiming_FiresExactlyOnce` | 100 reached, `Gain(20)` again / — / one `ClaimingBegan`, four modifiers, not eight |
| `Claiming_SurvivesCleansing` | Claimed, then `Cleanse(60)` / — / `Value` 40, `IsClaimed` **true**, the three buffs on, the 75 and 25 states **off** — rule 6's odd-looking state |
| `Claiming_DrainsAWholePercentPerSecond` | Claimed at 200 max / ticked 1 s, 10 s, 50 s / max HP 198, 180, 100 — rule 7 |
| `Claiming_StepsOncePerSecondNotPerFrame` | Claimed / 60 ticks of 1/60 s / **one** `Stat.Changed` on `MaxHp`, not sixty — rule 7 |
| `Claiming_ReachesZeroInAHundredSeconds` | Claimed at any maximum / ticked 100 s / max HP 0 and `Health.IsDead` — rule 7, GD §10.3's ninety seconds bounded |
| `Claiming_MultipliesWithTheSeventyFive` | Claimed at 100 Rot, 200 base / 50 s / `0.8 × 0.5 × 200` = 80 — rule 7 |
| `Claiming_PublishesPlayerDied` | drained to zero / — / exactly one `PlayerDied`, and `RunSession` ends the run on the same tick — rule 8 |
| `Claiming_DoesNotPublishTwiceIfDamageGotThereFirst` | killed by a Husk, then a drain step / — / **one** `PlayerDied` — rule 8 |
| `Combat_AnnounceDeathIsSilentForTheLiving` | a healthy player / `AnnounceDeath` / nothing published — rule 8 |
| `Combat_AnnounceDeathIsSilentTwice` | dead, announced / announced again / one event — rule 8 |
| `Combat_ResetClearsTheFlag` | dead, announced, `Reset` / killed again / a second `PlayerDied` — rule 8 |
| `Trigger_VeilrotIsWrittenEveryTick` | a run gaining and cleansing / ticked / `CombatBlackboard.Veilrot` tracks `Veilrot.Value` on every tick — rule 10 |
| `Trigger_VeilrotClauseNowFires` | `Veilrot AtLeast 50` on a run at 50 / — / true; at 49, false — the clause M5-06b could not author |
| `Content_VeilrotHasAWriter` | *(existing)* `Written` / — / includes `Veilrot`, and `EveryTriggerField_HasAWriter` stays green with a Veilrot clause authored — rule 10 |
| `Restore_ComesBackAtItsValueAndSilently` | `Restore(78)` / — / 75 and 25 states on, `IsClaimed` false, **nothing published** — rule 9 |
| `Restore_AtAHundredComesBackClaimed` | `Restore(100)` / — / `IsClaimed` true, four modifiers on, `ClaimedFor` **0**, nothing published — rule 9's stated cost |
| `Meter_AllocatesNothing` | 100 000 gains, cleanses and drain steps / `AllocationAssert.None` / zero — rule 11 |

**Guard rows are implied, not listed:** nulls to the constructor, a non-finite or negative `dt`, and
a non-finite `Gain`/`Cleanse` amount.

## Manual verification (Editor / device)

1. **[Editor]** With a debug command that grants Veilrot, step to 25 and watch the next wave. The
   arrows arrive measurably sooner; the bodies already in the arena do not change pace (rule 4).
2. **[Editor]** Step to 75 at full health. The HP readout drops to 80 % of its number **and the bar
   does not refill**. Cleanse back below 75 and the maximum returns while the current does not.
3. **[Editor]** Step to 100 in an empty arena and do nothing for one hundred and ten seconds. The
   maximum falls in visible one-second steps, the run ends, and the run-end screen appears with the
   right depth. This is the only step that exercises rule 8.
4. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**: whether a meter drained at
   1 %/s reads as *a clock running out* or as *a bug* at six inches, and whether
   `Palette.Veilrot`'s `#A855F7` is distinguishable from the arena's bone at phone brightness.
   Neither is answerable in an Editor whose `Screen.dpi` reads 120 ([Traps §9](../../Traps.md)).

## Out of scope

- **Drawing any of it.** No meter, no fraying screen edge, no Claimed vignette. GD §16.4's
  `Palette.Veilrot` is still read by nothing after this task —
  [M6-03b](M6-03b-the-meter-on-the-right-edge.md) puts GD §16.1's meter down the right edge and is
  the first reader of both `Threshold` and that colour — and GD §10.2's *"screen edges begin to
  fray"* is a post-process effect and belongs with M8-01's game-feel pass.
- **Anything that *gains* Veilrot.** Pacts are [M6-05b](M6-05b-the-offer-that-rolls-one.md)'s and
  Hunger is [M6-06b](M6-06b-four-ordeals-and-two-refusals.md)'s; the only caller of `Gain` after this
  task is a test and the debug overlay.
- **`Spend`, and the Emberwright's 5-Rot instant cast.** Rule 3, and M6-07's.
- **Cleansing shrines** (GD §10.1's *"or at rare Cleansing shrines"*). There is no shrine, no arena
  feature and no task that owns one; the Sanctum is the only sink in V1.
- **The Revenant, and the 50 threshold doing anything.** See above; a parking-lot line promoted by
  **M7-01**. GD §10.2's *"regardless of depth"* also contradicts GD §8.2's stage-17 introduction,
  and that is a design question for whoever builds it rather than one this task may settle.
- **Saving `ClaimedFor`.** Rule 9's stated cost, and a field on a format
  [M6-01b](M6-01b-save-format-v4.md) has already cut.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
