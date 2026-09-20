# M5-04b — Rise, and an address book for something that is neither the player nor an enemy

**Size:** M · **Depends on:** M5-04a · **Branch:** `m5-04b-rise`
**Design refs:** CH §3.2; GD §12.3; AR §14, §18.1, §18.2, §18.3; ADR-0006, ADR-0008, ADR-0011 · **Ledger rows:** none

## Goal

A quarter of the enemies a Gravecaller kills stand back up on their side — and a tree node can reach
the number a Wight hits for, through the same `ModifyStat` that moves everything else in the game.

## Which forcing question this answers

**The second one, whole.** [M5's spec-group note](../ROADMAP.md#m5--second-class) says the Wights are
the first minions to ask `IStatBlock` for anything, that `CombatantStats` answers three members over
an `EnemyAgent` and refuses the rest by name, and that a friendly minion is neither the player nor an
enemy. Rules 6–8 are the answer: **a third implementation, `MinionStats`, deliberately not a
generalisation of the first two** — with the alternative priced rather than dismissed, and with the
one thing it still cannot do named in rule 9 rather than discovered at M5-06.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/RisePassive.cs` | Core | CH §3.2's *"25 % of enemies killed rise as Wights"*: one draw, one spawn |
| `Core/Effects/MinionStats.cs` | Core | `IStatBlock` over a `MinionAgent` — the third implementation |
| `Core/Ai/EnemySystem.cs` | Core | **Substantial.** A death drain beside the experience drain (rule 2) |
| `Tests/Core/Ai/RiseTests.cs` | Tests.Core | The chance, the stream, the cap, the placement, and what does not rise |
| *small edits* | Core, Tests.Core | `RunState` holds the passive; `RunSession.Tick` drains and offers (rule 4); `Tests/Core/Effects/StatBlockTests.cs` gains the third block's rows |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Ai/EnemySystem.cs — widened
/// <summary>One enemy's death, as much of it as anything downstream needs.</summary>
public readonly struct EnemyDeath
{
    public ContentId SpecId { get; }
    public Vector3 Position { get; }
    public bool WasBoss { get; }
}

/// <summary>
/// The deaths since the last drain, oldest first, and empties the buffer. DrainXp's shape, and
/// beside it for its reason (rule 2).
/// </summary>
/// <returns>How many were written into <paramref name="destination"/>.</returns>
public int DrainDeaths(Span<EnemyDeath> destination);

// Core/Ai/RisePassive.cs
/// <summary>CH §3.2's Rise. One draw per death, one spawn per success, nothing else.</summary>
public sealed class RisePassive
{
    public RisePassive(MinionSpec spec, MinionSystem minions, IRandomStream random);

    /// <summary>The chance a death makes a Wight, live. Where "+10 % rise chance" goes (rule 5).</summary>
    public Stat Chance { get; }

    /// <summary>How many Wights this run has raised. For a readout and for the tests.</summary>
    public int Raised { get; }

    /// <summary>Offers <paramref name="deaths"/> to the passive, in order.</summary>
    public void OnDeaths(ReadOnlySpan<EnemyDeath> deaths, float now);
}

// Core/Effects/MinionStats.cs
public sealed class MinionStats : IStatBlock
{
    public MinionStats(MinionAgent agent);
    public Stat Resolve(PlayerStat stat);
    public bool Has(PlayerStat stat);
}
```

## Behaviour

1. **Rise draws from `IRandom.Drops`, and no sixth stream is created.** `RandomState` carries five
   positions and its own remarks say a sixth is *"the format's version field's job"* (AR §18.3) — so
   a new stream would be a **v4** for a 25 % chance. `Drops` is the right one on meaning as well as
   on cost: it is *"a kill produced something"*, it is the stream a loot drop will use when GD §14
   gets one, and **it is drawn from by nothing today**, so every existing seeded run replays
   identically. One draw per death, whether or not it succeeds, so the sequence is a function of the
   death count rather than of the outcomes (ADR-0011).
2. **The death drain is a pull beside the experience drain, not a subscription.** `RunSession` already
   calls `DrainXp` and `DrainKills` once a tick; `DrainDeaths` joins them. **Core does not subscribe
   to its own events** — `RunSession`'s own remarks refuse that in as many words — and `PendingKills`
   is a count, which is not enough: Rise needs *where*. The buffer is preallocated at the enemy
   capacity, filled in `EnemySystem.ApplyDamage`'s kill branch beside the `EnemyDied` publish, and
   emptied by the drain. **An undrained buffer at capacity drops the oldest rather than growing**,
   which cannot happen while the drain runs every tick and is loud in *As built* if it ever does.
3. **A Wight rises where the enemy fell, on the tick it fell.** CH §3.2 is *"25 % of enemies killed
   rise as Wights"* — the corpse is the Wight, so the position is the death's. The spawn goes through
   `MinionSystem.Spawn`, which refuses silently at the cap: **a draw that succeeds against a full
   army is spent and produces nothing**, and that is the honest reading of *"cap 3"* rather than a
   banked rise that would make the cap a queue.
4. **Ordered after the enemy tick and the minion tick, and above the death check.** The drain must
   run after everything that can kill an enemy this tick — the player's swing, a bolt, a Wight's
   strike — so a kill never waits a frame to rise. It is above the death check for the reason every
   other pass is: a Wight raised on the tick the player dies is raised into a run that is ending, and
   the alternative is a kill silently losing its rise depending on when the player happened to die.
5. **`Chance` is a `Stat` and the base is the authored 0.25.** It is where CH §3.2's Legion nodes go,
   and it is **read through a clamp at the point of use** rather than clamped in the stat (ADR-0008
   says a `Stat` clamps nothing): a live value at or below zero never rises, a value at or above one
   always does, and a non-finite one **never rises** — the direction every comparison in core takes,
   because a modifier stack nobody can read must not become a guaranteed army.
6. **`MinionStats` is a third implementation and not a generalisation, and the alternative is priced.**
   A shared `IHasCombatStats` on both agents, or a base class, would remove about ten lines and cost
   three things: it would put a member on `EnemyAgent` for the benefit of a type it must not know
   about ([M5-04a](M5-04a-minion-agents-and-registry.md) rule 1); it would make one refusal message
   serve two very different content errors, where M4-01a rule 2 requires the throw to **name the
   content** — `"'enemy.husk' has no stat at address WeaponRange"` and `"'minion.wight' has no stat
   at address WeaponRange"` are different facts for different authors; and it would generalise from
   two cases to a third, which is one case too early. Three small honest switches beat one clever one.
7. **It answers the same three addresses `CombatantStats` does, and refuses the rest by name.**
   `MaxHp`, `MoveSpeed`, `ContactDamage` — the three `Stat`s a `MinionAgent` holds (M5-04a rule 2).
   `WeaponRange` on a Wight is a question with no answer, and the answer is a throw naming the
   address and `minion.wight`, **never a silently created `Stat`**, which would be a modifier landing
   on a number nothing reads (Traps §1). `Has` is written as its own switch rather than as a
   try/catch, for the reason `CombatantStats.Has` gives, and a row walks every `PlayerStat` member
   and asserts the two agree.
8. **`PlayerStat` gains nothing, and this is the task that proves M4-01a rule 1 was right.** The
   shared address space was argued at M4-01a against a parallel `EnemyStat`; a Wight is the second
   caller that is not the player and it needs **no new member** — which is the evidence that argument
   was about the design and not about the one case in front of it. `PlayerStat.cs` is not in the
   Files table.
9. **What a node still cannot do, named here rather than found at M5-06.** `ModifyStat` carries
   `StatTarget { Player, Self }` and `ModifyStatHandler.Aiming(self)` aims it for one call. **Neither
   spells *"every Wight I own"***, and a Legion node that says *"+20 % minion damage"* is exactly
   that. It is not fixed here: M4-01a rule 3 rules that an effect aimed at *someone else* is a
   different primitive with a selection rule, and inventing `StatTarget.Minions` with one authored
   node behind it would be guessing at the shape. **What this task ships is that a Wight has an
   address book and a live `ContactDamage` a modifier can sit on**; who is allowed to put one there
   is M5-06's first question, and it is carried to M5-06 as a ledger row at this task's merge.
10. **Only the Gravecaller has one, and every other run is byte-identical.** `RisePassive` is
    constructed only where `CharacterSpec.Minions` is non-null; an Oathbound run holds no passive, no
    `MinionSystem`, draws nothing from `Drops`, and its `DrainDeaths` is called but is the only new
    work — one array write per kill, which is the price of the pull and is measured.
11. **A boss does not rise, and nor does a Wight.** `EnemyDeath.WasBoss` exists for this one rule:
    CH §3.2's *"25 % of enemies killed"* against a Warden would put a 4 200 HP boss on the player's
    side as a 20 HP Wight, which is either absurd or free, depending on which numbers it kept.
    Minions are not in `EnemyRegistry` at all (M5-04a rule 1), so a Wight's death cannot reach this
    path and the rule costs nothing to keep.
12. **Nothing here allocates on the frame path.** The drain writes into a caller-owned span, the
    passive holds no collection, and `MinionStats.Resolve` is a jump table over an enum returning a
    reference the agent already holds.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Rise_AQuarterOfKillsStandBackUp` | a scripted stream answering below 0.25 exactly once in four / 400 deaths, cap raised past them / **100** Wights |
| `Rise_DrawsOncePerDeathWhateverTheOutcome` | a counting stream / 50 deaths, none succeeding / exactly 50 draws — rule 1's reproducibility claim |
| `Rise_DrawsFromTheDropsStream` | a counting `IRandom` / 20 deaths / `Drops` advanced 20, and `Spawn`, `Offers`, `Affixes` and `Misc` advanced **zero** |
| `Rise_WhereTheEnemyFell` | a death at (7, 0, −3), the draw succeeding / drained / one `MinionSpawned` at (7, 0, −3) |
| `Rise_ASuccessAtTheCapIsSpent` | three standing, cap 3, the draw succeeding / a death / no Wight, no throw, and `Raised` did **not** increase — rule 3 |
| `Rise_ABossDoesNotRise` | a boss death, the draw succeeding / drained / no Wight — rule 11 |
| `Rise_ChanceIsAStat` | a `Flat +0.25` on `Chance` / a stream answering 0.4 / it rises, where at base it would not |
| `Rise_RefusesAnUnreadableChance` | the live chance at NaN, then at −1, then at +∞ / 100 deaths / **no** Wight in any case — rule 5's direction |
| `Rise_AlwaysAtOrAboveOne` | a `Flat +1` on `Chance` / 10 deaths under the cap / every one rises |
| `Rise_AllocatesNothing` | 10 000 deaths drained and offered / `AllocationAssert.None` / zero |
| `Deaths_AreDrainedOldestFirstAndEmptied` | three kills in one tick / `DrainDeaths` / three entries in kill order, and a second drain returns 0 |
| `Deaths_CarryTheSpecAndThePosition` | a Husk killed at a known point / drained / `SpecId` is `enemy.husk` and `Position` is where it stood |
| `Deaths_ADespawnIsNotADeath` | an enemy despawned by `Clear` and one by `DespawnAtEndOfTick` / drained / nothing — only a kill is a death |
| `Deaths_AFullBufferDropsTheOldestLoudly` | capacity + 1 kills without a drain / — / the newest survive and the behaviour is asserted rather than left to be discovered — rule 2 |
| `Minion_ImplementsTheBlock` | a standing Wight / resolved through `IStatBlock` / `MaxHp`, `MoveSpeed` and `ContactDamage` return the agent's own live `Stat` instances, not copies |
| `Minion_RefusesAnAddressItDoesNotHave` | `Resolve(WeaponRange)` / — / throws, and the message names both the address and `minion.wight` — rule 7 |
| `Minion_HasAgreesWithResolve` | every `PlayerStat` member / `Has` vs `Resolve` / true exactly when `Resolve` does not throw |
| `Minion_AndCombatantAnswerTheSameSet` | `MinionStats` and `CombatantStats` / every member / the same three of twelve answered and the same nine refused, with **different messages** — rule 6, asserted so a later generalisation has to argue with this row |
| `Modify_ReachesAWightsDamage` | a `ModifyStat(ContactDamage, PercentAdd, +0.2)` aimed at a Wight through `Aiming` / applied / the Wight's strike deals 20 % more, and the player's numbers do not move |
| `PlayerStat_GainedNothing` | the enum / — / the same twelve members as after M4-01a — rule 8 |
| `Run_AnOathboundRunDrawsNothing` | the shipped Oathbound / a full stage played out / `Drops` unadvanced, no `MinionSystem`, no `RisePassive` — rule 10 |
| `Run_ARiseOnTheTickThePlayerDies` | a kill and a death on one tick / `RunSession.Tick` / the Wight is raised and the `PlayerDied` still follows — rule 4 |

**Guard rows are implied, not listed:** null `MinionSpec`, `MinionSystem` and `IRandomStream` to the
passive; a null `MinionAgent` to `MinionStats`; a non-finite `now`; and a destination span shorter
than the pending deaths.

## Manual verification (Editor / device)

_None._ The Gravecaller is not selectable until M5-07 and a Wight has no view until M5-05, so no run
this build plays raises one. **The first time anyone sees a Wight is M5-05**, and the question it
carries to [ledger row 3](../ROADMAP.md#carry-forward-into-m5) is CH §3.2's own watch item: *Wights
must be unmistakable from enemies at phone scale.*

## Out of scope

- **`StatTarget.Minions`, or any way to buff every Wight at once.** Rule 9, carried to M5-06.
- **Second Death (CH §3.2's Grave-Work Keystone).** M5-06. `MinionDied` already carries the position
  it needs (M5-04a rule 10).
- **The Host's +4 cap.** M5-06 authors it; M5-04a rule 3 already made the cap a `Stat`.
- **Enemies fighting back, or damaging a Wight in play.** M5-04a rule 9.
- **Views.** M5-05.
- **A sixth random stream, and any save-format change.** Rule 1.
- **Veilrot, and CH §3.2's *"+1 % damage per Veilrot point"*.** GD §10's meter does not exist; M6-04.

## As built

_Filled at merge, 6 000 bytes or fewer, measured._
