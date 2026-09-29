# M7-04f — What the Gravecaller's Keystones need

**Size:** M · **Depends on:** M7-04e · **Branch:** `m7-04f-the-gravecallers-keystones`
**Design refs:** CH §3.2, §4.2, §5.4; GD §10.2, §10.3, §12.4, §13.1; AR §18.1, §18.3; ADR-0006, ADR-0008, ADR-0009 · **Ledger rows:** none

## Goal

The Wights' cap and lifespan become numbers a node can move. A kill by a Wight can set off a blast.
And the Veil can bloom, turning GD §10.2's penalties into gifts and slowing the Claiming. The Keystone
nodes are authored in [M7-04i](M7-04i-the-gravecallers-twenty-seven.md).

## The three, read against the code

| Keystone | CH §3.2 | What it is in this build |
|---|---|---|
| **The Host** *(Legion)* | *"Minion cap +4, but all Wights have 50 % HP"* | `ModifyStat(MinionCap, Flat, +4, Minions)` and **`ModifyStat(ContactDamage, PercentMult, −0.35, Minions)`** — the drawback re-read (below) |
| **Second Death** *(Grave-Work)* | *"Enemies killed by minions explode for 25 damage in 3 m"* | `ModifyStat(MinionBlastDamage, Flat, 25, Minions)` and `ModifyStat(MinionBlastRadius, Flat, 3, Minions)` |
| **Rot Bloom** *(Rot)* | *"Veilrot thresholds grant buffs instead of penalties, and the Claiming's HP drain is halved"* | `BloomVeilrot(0.5)` |

**What counting found.**

- **Nothing hurts a Wight.** `MinionSystem.ApplyDamage` has no caller, and no enemy behaviour ever
  targets a Wight. So **"all Wights have 50 % HP" costs nothing**: it would ship a Keystone whose
  drawback is text.
  - **The ruling:** The Host's price is paid in the one number a Wight uses, its blow. **Seven Wights
    at 65 % are 4.55 Wights' worth of damage against three**, still +52 %, and now a trade.
  - This is the design doc read against the build rather than a new design. CH is this project's own
    draft, and M7-04i edits §3.2's row.
  - **What would change it:** a task that lets enemies hurt Wights, which puts the HP back.
  - The same finding makes **Knitted Bone** (`MaxHp +50 %`, Minions) a node that does nothing.
    [M7-04a](M7-04a-a-pact-on-every-node.md) names it and M7-04i re-aims it at this task's lifespan.
- **The cap is already a `Stat`**, `MinionSystem.Cap`, and its remarks say The Host lands on it *"as
  a Flat modifier"*. But no `PlayerStat` reaches it, and `MinionRecipe`'s remarks refuse `Cap` and
  `Lifespan` as *"The Host's and Legion's Keystone-tier numbers"*. This is that tier.
  - `MaxConcurrent` is 8, which its remarks size as 3 + The Host's 4 + 1 spare.
  - M7-04i's Fourth Grave is that spare, so a full Legion stands **eight**.
- **Nobody records who killed an enemy, and one place could.** `MinionSystem.Strike` calls
  `enemies.ApplyDamage` and discards the `DamageResult`, whose `Killed` is exactly *"killed by a
  minion"*. `EnemyDied` and `EnemyDeath` carry no killer, and they need none.
- **Three comments misread Second Death**: `MinionSystem.ApplyDamage`'s, and two in
  `MinionEvents.cs` — the file header and `MinionDied`'s remarks. Each says it wants `MinionDied`,
  which is a *Wight's* death, not an enemy killed by one. All three are corrected, and
  `MinionSpawned.Lifespan`'s docs (*"the spec's Lifespan"*) with them.
- **The 25 row cannot go negative today.** `EnemySystem.Spawn` adds the Veilrot speed modifier only
  when `EnemySpeedBonus > 0f`, so a bloomed −0.05 would be skipped in silence.
- **Rot Bloom has nowhere to land.** `Veilrot`'s thresholds, the 25 row's `EnemySpeedBonus`, the
  75 row's −20 % and the drain's two constants are all fixed, and no effect reaches the meter.
  `VeilrotSpec` is per class and immutable. `VeilrotMeterView` draws four marks and no words, so
  nothing on screen states a penalty the bloom would falsify.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/MinionSystem.cs` | Core | **Substantial.** Cap and lifespan read off the recipe, and the blast on a Wight's kill |
| `Core/Run/Veilrot.cs` | Core | **Substantial.** `Bloom` and `Unbloom`: the 25 and 75 rows inverted, the drain scaled |
| `Core/Effects/BloomVeilrot.cs` | Core | **New.** The primitive and its handler |
| `Game/Authoring/BloomVeilrotDefinition.cs` | Game | **New.** One number |
| `Tests/Core/Ai/GravecallerKeystoneTests.cs` | Tests.Core | **New.** Every rule below |
| *small edits* | Core, Docs | `Core/Effects/MinionRecipe.cs` — four `Stat`s, and `MinionRecipeStats` answers them (rule 1); `Core/Effects/PlayerStat.cs` — four members appended, and `PlayerStats.Resolve`'s default message no longer calls `ContactDamage` the one member meant to land there (rule 1); `Core/Effects/MinionStats.cs` — its *"PlayerStat gains nothing for this"* remark corrected; `Core/Ai/EnemySystem.cs` — `Spawn` applies a Veilrot speed bonus that is non-zero, not only one above zero (rule 5); `Core/Events/MinionEvents.cs` — the Second Death remarks and `MinionSpawned.Lifespan`'s docs corrected; `Core/Run/RunSession.cs` — one `Register` line; `Docs/Architecture.md` — the §18.1 minion row names the blast (rule 4) |
| *ripple* | Tests.Core | `LegionEffectsTests.Recipe_AndMinionStatsAnswerTheSameSet` — the recipe now answers seven and a live Wight still three, so the row asserts the three they share and the four only the recipe has; `ModifyStatTests.Stats_ResolveEveryMember` names the four as refused by `PlayerStats`, beside `ContactDamage`; `StatBlockTests.NotAnOathbounds` gains the four, which `Player_ImplementsTheBlock` and `Player_BehaviourIsUnchanged` read; `StatBlockTests.PlayerStat_GainedNothing` is rewritten to say which four were gained and why; the member-count rows move by four; `MinionSystemTests` and `RiseTests`, which put modifiers on `MinionSystem.Cap`, are untouched because `Cap` now returns the recipe's; `VeilrotTests`' drain rows hold, pinned by rule 5's snap |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Effects;

public sealed class MinionRecipe
{
    public Stat Cap { get; }          // seeded from MinionSpec.Cap
    public Stat Lifespan { get; }     // seeded from MinionSpec.Lifespan
    public Stat BlastDamage { get; }  // base 0
    public Stat BlastRadius { get; }  // base 0
}

public enum PlayerStat
{
    // ... ShieldRefill (M7-04e), then — appended, all four answered by the recipe alone:
    MinionCap,
    MinionLifespan,
    MinionBlastDamage,
    MinionBlastRadius,
}

/// <summary>GD §10.2 inverted for this run, and the Claiming's drain scaled (M7-04f rule 5).</summary>
public sealed class BloomVeilrot : IEffect
{
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="drainScale"/> not finite, or not in (0, 1].</exception>
    public BloomVeilrot(float drainScale);
    public float DrainScale { get; }
}

public sealed class BloomVeilrotHandler : IEffectHandler<BloomVeilrot>
{
    public BloomVeilrotHandler(Veilrot veilrot);
    public void Apply(BloomVeilrot effect, object source);   // veilrot.Bloom(effect.DrainScale, source)
    public void Remove(BloomVeilrot effect, object source);  // veilrot.Unbloom(source)
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class MinionSystem
{
    /// <summary>How many may stand — the recipe's <c>Cap</c>, clamped by <c>LiveCap</c> as before.</summary>
    public Stat Cap { get; }   // now => the recipe's Cap
}

namespace Soulvail.Core.Run;

public sealed class Veilrot
{
    /// <summary>Whether GD §10.2 is inverted for this run.</summary>
    public bool IsBloomed { get; }

    /// <summary>Inverts the 25 and 75 rows and scales the drain by <paramref name="drainScale"/> from the next step.</summary>
    /// <exception cref="InvalidOperationException">Already bloomed by another source.</exception>
    public void Bloom(float drainScale, object source);

    public void Unbloom(object source);
}
```

## Behaviour

1. **Four numbers join the recipe, read where the system used the spec's.**
   - `MinionRecipe` gains `Cap`, `Lifespan`, `BlastDamage` and `BlastRadius`. The first two are seeded
     from `MinionSpec`; the blast pair starts at 0.
   - `MinionRecipeStats.Resolve` and `Has` answer the four new `PlayerStat` members beside its three.
     `PlayerStats.Has` answers false for all four, so a player-aimed node naming one refuses the run
     (RS-03b rule 7's sweep).
   - `MinionSystem` keeps the recipe its constructor is already handed, which it does not store
     today. `MinionSystem.Cap` returns the recipe's `Cap`, and `LiveCap` reads it unchanged: clamped
     to `MaxConcurrent`, and below 1 or NaN standing nobody.
   - `Spawn` stamps `now + LiveLifespan()` and publishes it in `MinionSpawned`. `LiveLifespan()` is the
     recipe's value when it is finite and above zero. Otherwise `Spawn` refuses as it does at the cap:
     no body, no event, a null. A lifespan nobody can read raises nobody, rather than a Wight that
     never leaves.
2. **The cap is read live; the lifespan at the rise.** A cap raised mid-stage lets the next rise stand.
   A lifespan raised mid-stage reaches the next Wight and not the ones standing, which is M5-06a rule
   4's recipe bargain, stated for a fourth and fifth number.
3. **A Wight's kill is known where it happens.**
   - `Strike` keeps the `DamageResult` it discarded. After it has published `MinionStruck`, a result
     with `Killed` true calls `player.Burst(enemies, quarryPosition, radius, damage, 0, now)`.
     `quarryPosition` is read before the blow, and the burst is sent only when both the recipe's
     `BlastRadius` and `BlastDamage` are finite and above zero.
   - **After `ApplyDamage` has returned**, which is [M7-04b](M7-04b-a-burst-around-you.md) rule 6's
     second legal caller.
   - **The blast is not a Wight's kill**, so what it kills does not blast again. One generation, the
     Weaver's rule (M7-00a), and no recursion.
   - **The damage is flat, as CH wrote it.** It is a Wight's number, not the player's weapon, so it
     does not scale with the player's weapon nodes.
4. **The blast sits inside the minion step, above the death drain** — AR §18.1's minion row gains
   *"… and a Wight's kill may burst, after its blow has resolved."* A kill the blast makes is drained,
   risen and paid on the same tick.
5. **Rot Bloom inverts two rows and halves the drain.**

   | Row | Unbloomed (M6-04) | Bloomed |
   |---|---|---|
   | 25 | new enemies +5 % move speed (`EnemySpeedBonus` 0.05) | new enemies **−5 %** (`EnemySpeedBonus` −0.05) |
   | 50 | crossed, published, silent | the same — there is no Revenant to invert |
   | 75 | `MaxHp PercentMult −0.20` | `MaxHp PercentMult` **+0.20** |
   | 100 | the Claiming, draining 1 % a second, 100 s to nothing | the Claiming's three gifts unchanged, draining **0.5 %** a second, 200 s |

   - **Bloom re-settles what is already standing.** A meter at or above 75 swaps its −0.20 for +0.20
     (`RemoveAll(this)`, then the new one). Enemies already walking keep the speed they spawned with,
     which is the 25 row's own spawn-time rule.
   - **The 25 row reaches the enemies.** `EnemySystem.Spawn` applies `EnemySpeedBonus` when it is
     non-zero, where it asked `> 0f`, so the bloomed −0.05 goes on as a `PercentMult` like the +0.05.
   - **The drain is accumulated rather than recomputed**, so a bloom taken while Claimed halves the
     rate from the next step and returns nothing already drained.
     - Each whole second of `ClaimedFor` adds `ClaimingDrainPerSecond × drainScale` to `_drained`. A
       tick longer than a second adds one step per second.
     - **The sum snaps to exactly 1 once it is within `1e-4` of it**, and stops there. In float32, 100
       × 0.01 is 0.9999993 and 200 × 0.005 is 0.9999992, so a clamp that only catches an overshoot
       leaves a player alive at a millionth. `1e-4` is fifty times smaller than the smallest step, so
       it can only ever catch the last one.
     - **`SecondsToZero`'s cap goes.** Steps stop when `_drained` is 1, not at 100 s, or a bloomed
       Claiming would stop at half.
     - The modifier is rewritten as `−_drained`. Unbloomed, this lands on −1 at 100 s as M6-04's
       step-count clamp did, so `VeilrotTests`' drain rows hold.
   - **`Unbloom` puts every row back the same way**, and nothing in M7 calls it but a test. A Keystone
     is never corrupted (M7-04a rule 2).
6. **One bloom a run.** A second `Bloom` from another source throws: two sources inverting one table
   is a content error, and a second call from the same source is a no-op.
7. **A resume blooms before the meter comes back.**
   - `SkillTree.Restore` replays Rot Bloom's take, which is `Bloom`, above `Veilrot.Restore` (AR
     §18.1's restore row).
   - The restore then settles the 75 row from the class's start with the bloom already in: +0.20,
     never −0.20 then a swap.
   - A Claimed resume restarts the drain at zero, as M6-11b already does.
8. **Registered for every class**, because every run has a meter; only the Gravecaller's tree carries
   the node, and a Keystone is never lent (CH §5.4).
9. **Nothing allocates on a tick.** Four `Stat`s built once, a field for the drain, and the blast is
   M7-04b's walk.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cap_IsTheRecipes` | a Gravecaller / `ModifyStat(MinionCap, Flat, 4, Minions)` applied / `MinionSystem.Cap.Value` 7, and seven rises stand — rule 1 |
| `Cap_StillClampsAtEight` | the cap at 9 / nine rises / eight stand — rule 1 |
| `Cap_IsNotThePlayers` | a Gravecaller's own node naming `MinionCap` at the player / `Start` / refused, naming the node — rule 1 |
| `Lifespan_ReachesTheNextWight` | one Wight standing, then `MinionLifespan PercentAdd +0.5` / a second rise / the first expires 20 s after its rise, the second 30 s after its own, and the second `MinionSpawned` says 30 — rules 1, 2 |
| `Lifespan_UnreadableRaisesNobody` | the lifespan forced to NaN, then 0 / a rise / null, no `MinionSpawned` — rule 1 |
| `Blast_AWightsKillBursts` | `BlastDamage` 25, `BlastRadius` 3, a Wight one blow from killing a Husk, with two more Husks at 20 HP within 3 m / the minion step / three `EnemyDied`, one `BurstReleased` at the first Husk's position — rule 3 |
| `Blast_OnlyAKillBursts` | a blow that does not kill / — / no burst — rule 3 |
| `Blast_WithoutBothNumbersNothing` | radius 3, damage 0; then damage 25, radius 0 / a kill each / no burst — rule 3 |
| `Blast_DoesNotChain` | a blast kills a Husk standing beside a third / — / one burst only — rule 3 |
| `Blast_APlayersKillDoesNotBurst` | both numbers set, a cone kill / — / no burst — rule 3 |
| `Blast_KillsPayOnTheTick` | a blast kill / one `RunSession.Tick` / its XP is drained on that tick — rule 4 |
| `Bloom_InvertsTheTwentyFiveRow` | bloomed at 30 / a spawn through `EnemySystem.Spawn` / the new enemy's speed ×0.95 — rule 5 |
| `Drain_UnbloomedStillEndsAtOneHundred` | Claimed, unbloomed / 100 s of ticks / `MaxHp` exactly 0 and `PlayerDied` — rule 5 |
| `Drain_ALongTickTakesEveryStep` | Claimed / one 3.5 s tick / three steps, −0.03 — rule 5 |
| `Bloom_InvertsTheSeventyFiveRow` | bloomed, the meter moved to 80 / — / `MaxHp` ×1.2 — rule 5 |
| `Bloom_ReSettlesAMeterAlreadyAbove` | the meter at 80 with −0.20 on / `Bloom` / exactly one modifier from the meter, +0.20 — rule 5 |
| `Bloom_HalvesTheDrain` | bloomed and Claimed / 100 s of ticks / `MaxHp` ×0.5 of the Claiming maximum; 200 s — ×0, and `PlayerDied` published — rule 5 |
| `Bloom_MidClaimingReturnsNothing` | Claimed for 40 s (−0.40), then bloomed / 10 more seconds / −0.45, never −0.20 — rule 5 |
| `Bloom_TheLastStepIsExactlyNothing` | bloomed, Claimed / 200 steps / the modifier is −1 exactly — rule 5 |
| `Bloom_UnbloomPutsItBack` | bloomed at 80 / `Unbloom` / −0.20, `EnemySpeedBonus` 0.05 — rule 5 |
| `Bloom_ASecondSourceIsRefused` | bloomed by one source / `Bloom` by another / `InvalidOperationException`; by the same, nothing — rule 6 |
| `Bloom_AResumeBloomsBeforeTheMeter` | a snapshot with the Keystone taken and the meter at 80 / `Start` / `MaxHp` carries +0.20 and never carried −0.20 — rule 7 |
| `Bloom_IsRegisteredForEveryClass` | a run of each class / — / `CanApply` true — rule 8 |
| `Keystones_AllocateNothing` | 10 000 minion kills with the blast set, and 10 000 drain steps bloomed / `AllocationAssert.None` / zero — rule 9 |

**Guard rows are implied, not listed:** `BloomVeilrot`'s refusals; the handler's nulls;
`BloomVeilrotDefinition.ToEffect`'s rewrap.

## Manual verification (Editor / device)

_None this task._ [M7-04i](M7-04i-the-gravecallers-twenty-seven.md) authors the three nodes, and its
steps 5 to 7 play them.

## Out of scope

- **Letting enemies hurt a Wight.** The *Found* above; it is what would put The Host's HP back.
- **A 50 row for the bloom.** No Revenant exists (GD §19, the parking lot).
- **A killer on `EnemyDied`.** Rule 3 knows where it needs to.
- **Rise chance as an address.** `RisePassive.Chance` is a `Stat` no node names; nothing in M7-04i asks
  for it, and an address with no node is `ContactDamage`'s mistake.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
