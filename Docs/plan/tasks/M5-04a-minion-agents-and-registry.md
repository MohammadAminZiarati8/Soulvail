# M5-04a — Wights: a body on the player's side

**Size:** M · **Depends on:** M5-02 · **Branch:** `m5-04a-minion-agents`
**Design refs:** CH §3.2, §8 q1; GD §11.1; AR §3, §9, §14, §18.1, §18.4; ADR-0003, ADR-0006, ADR-0008 · **Ledger rows:** [6](../ROADMAP.md#carry-forward-into-m5)

## Goal

Something that is not the player and not an enemy can stand in the arena, walk at a Husk and hit it.
Nothing produces one yet — [M5-04b](M5-04b-rise-and-minion-stats.md) is Rise — so this task is the
answer to *"what is a Wight"* and to nothing else.

## Why this is a task and not part of M5-04b

The ROADMAP's single title covers a body, a registry, a passive that produces one and a stat block
for it. Counted against the shipped code that is **six files before the tests**, and the two halves
are reviewed against different documents: a friendly body is AR §3 and §9 (who reports its position,
what it perceives), and Rise is CH §3.2 and ADR-0011 (a chance, drawn from which stream, spending
which seed). M4-01a/M4-01b is the precedent, one milestone old.

## Which forcing question this answers

**Half of the second one, and it says which half.** [M5's spec-group note](../ROADMAP.md#m5--second-class)
says the Wights are the first minions to ask `IStatBlock` for anything. **That question is
M5-04b's** — it is a question about `Core/Effects`, and it cannot be answered before the thing being
addressed exists. What this task settles is the prior question the ledger row folds into it: **what a
minion *is*, given that `CombatantStats` is written over an `EnemyAgent` and a Wight must never be
one.** Rule 1 is that answer, and rule 2 is the shape M5-04b's third `IStatBlock` gets to mirror.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/MinionAgent.cs` | Core | One Wight: its health, its three stats, its clock and its quarry |
| `Core/Ai/MinionSystem.cs` | Core | The friendly registry, the perception, and the one behaviour a Wight has |
| `Core/Events/MinionEvents.cs` | Core | `MinionSpawned`, `MinionDespawned`, `MinionDied`, `MinionStruck` |
| `Tests/Core/Ai/MinionSystemTests.cs` | Tests.Core | Capacity, lifespan, target choice, the strike, and what a Wight is not |
| *small edits* | Core | `WorldSnapshot` gains a `Minions` array, `AddMinion` and a `Clear` line; `IIntentSink` gains `MinionMove(in EnemyMoveIntent)` (rule 4); `RunState` holds the system; `RunSession.Tick` ingests and ticks it (rule 8) |
| *ledger* | Tests.Core | `Tests/Core/Ai/BossPhasesTests.cs` gains **one row**: `Boss_TickAllocatesNothing` ([row 6](../ROADMAP.md#carry-forward-into-m5)) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Ai/MinionAgent.cs
/// <summary>
/// One Wight. Shaped deliberately like an EnemyAgent — Health, three Stats, a Position and a
/// Velocity the body reports — and deliberately not one (rule 1).
/// </summary>
public sealed class MinionAgent
{
    public int Id { get; }
    public MinionSpec Spec { get; }

    public Health Health { get; }
    public Stat MaxHp => Health.MaxHp;
    public Stat MoveSpeed { get; }
    public Stat ContactDamage { get; }

    /// <summary>Where the body last reported it to be. Written by ingestion only.</summary>
    public Vector3 Position { get; internal set; }
    public Vector3 Velocity { get; internal set; }

    /// <summary>Simulated run time at which it dissolves. CH §3.2's twenty seconds.</summary>
    public float ExpiresAt { get; internal set; }

    /// <summary>The enemy id it is walking at, or 0 for none. Re-chosen on rule 6's cadence.</summary>
    public int QuarryId { get; internal set; }

    /// <summary>Simulated run time at which it may strike again.</summary>
    public float NextAttackAt { get; internal set; }

    public bool IsAlive { get; }
}

// Core/Ai/MinionSystem.cs
public sealed class MinionSystem
{
    /// <summary>
    /// The most Wights that may stand at once, whatever a node says. Eight: CH §3.2's base of
    /// three plus The Host's four, with one spare — rule 3.
    /// </summary>
    public const int MaxConcurrent = 8;

    /// <summary>How often a Wight re-chooses what it is walking at, in seconds. Rule 6.</summary>
    public const float RetargetInterval = 0.5f;

    public MinionSystem(MinionSpec spec, IDomainEvents events, IIntentSink intents);

    /// <summary>How many may stand right now, live. Where "+4 minions" goes (rule 3).</summary>
    public Stat Cap { get; }

    public int Count { get; }

    public ReadOnlySpan<MinionAgent> Alive { get; }

    public bool TryGet(int id, out MinionAgent agent);

    /// <summary>Stands one up at <paramref name="position"/>, or returns null at the cap.</summary>
    public MinionAgent Spawn(Vector3 position, float now);

    /// <summary>Copies this frame's reported positions onto the agents. AR §3.</summary>
    public void Ingest(WorldSnapshot snapshot);

    /// <summary>Expire, choose, walk, strike — rule 7's order.</summary>
    public void Tick(float dt, float now, EnemySystem enemies);

    /// <summary>Damage from an enemy. The one door a Wight can be hurt through.</summary>
    public DamageResult ApplyDamage(int minionId, float amount, float now);

    public void Clear();
}
```

## Behaviour

1. **A Wight is its own type and is never an `EnemyAgent`, and the reason is what would happen if it
   were.** Reusing `EnemyAgent` would be free — `CombatantStats`, `EnemyRegistry`, `DepthScaling` and
   `EnemyView` all work unchanged — and it would put a friendly body inside every question the game
   asks about enemies: `SpawnDirector.IsStageComplete` would wait for the player's own Wights to die
   before opening the door, `PlayerCombat.BuildCandidates` would offer them to the targeter so the
   player would shoot them, `EnemySystem.ApplyDamage`'s kill path would pay experience for one, and
   `WaveComposer` would need a roster entry the director must never draw. Each is a separate place to
   remember an exception. **The cost of a separate type is one class of about a hundred lines and one
   more `IStatBlock` in M5-04b; the cost of reuse is an exception in six systems, discovered one at a
   time.**
2. **It carries exactly the three `Stat`s an `EnemyAgent` carries, and that is on purpose.** `MaxHp`
   through `Health`, `MoveSpeed`, `ContactDamage` — the same three `CombatantStats` answers and
   refuses everything else around. It is what lets M5-04b's `MinionStats` be a mirror rather than an
   invention, and it is why CH §3.2's Legion branch can buff a Wight's damage through the
   `ModifyStat` the player already uses instead of a second mechanism.
3. **The cap is a `Stat` and the array is a constant.** `Cap` is seeded from `MinionSpec.Cap` (3) and
   is where CH §3.2's The Host — *"minion cap +4"* — lands as a `Flat` modifier. The backing array is
   `MaxConcurrent` = 8, allocated once: a `Stat` clamps nothing (ADR-0008), so a stack could drive the
   live cap to 40, and a system that resized would allocate on the spawn path. **A spawn above
   `MaxConcurrent` is refused exactly as one above the cap is** — silently, returning null, because
   `ProjectileSystem.Fire`'s argument holds here too and one lost Wight is better than an exception.
4. **It is a body, so Unity reports where it is** (AR §3, ADR-0003). `WorldSnapshot` gains a
   `Minions` array of the same `EnemySense` struct and `MinionSystem.Ingest` copies `Position` and
   `Velocity` off it, exactly as `EnemySystem.Ingest` does. Movement leaves as an
   `EnemyMoveIntent` through a **new door**, `IIntentSink.MinionMove` — the same struct because it is
   the same three facts, a different door because the two ids come from different registries and a
   shared one would let a Wight's id steer a Husk. **`HasLineOfSight` and `PathDirectionToPlayer` on
   a minion's sense are unread**; a Wight walks in a straight line at something under 12 m away, and
   `NavPathSense` only ever paths to the player.
5. **A Wight has no blackboard.** `EnemyBlackboard` carries `DistanceToPlayer`, `AlliesNearby`,
   `HasLineOfSight` and `LungeDirection` — eleven fields of which a Wight would use none, and three
   whose *names* would be lies on a friendly body. Its whole working memory is `QuarryId`,
   `NextAttackAt` and `ExpiresAt`, and those are on the agent where a reader can see them.
6. **It walks at the nearest living enemy, re-chosen twice a second.** Nearest by XZ (AR §18.4), over
   `enemies.Registry.Alive`, skipping corpses. **Re-chosen on a cadence rather than every tick**, for
   `Targeter`'s reason at 10 Hz: a minion that re-picks every frame oscillates between two
   equidistant Husks and walks nowhere. The quarry is also dropped immediately when it dies, so the
   cadence delays a *change* of mind and never a *dead* one.
7. **The tick order inside the system is: expire, choose, walk, strike.** Expiring first means a
   Wight in its last frame does not pick a target it will never reach. Striking last means it strikes
   from the position it was reported at this frame rather than the one it is walking to — the same
   discipline `ChaserBehaviour` keeps, and the reason a strike is a distance test against `Reach`
   rather than a swept volume.
8. **In `RunSession.Tick` it ingests with the enemies and ticks immediately after them.** *With*,
   because perception is one phase and splitting it would let a Wight act on last frame's positions.
   *After the enemy behaviours*, because that is where a Husk decides to strike, so a Wight that
   kills its quarry this tick removes an enemy that has already acted rather than one that never got
   to — the trade `RunSession` already documents between the player's swing and an enemy's. **Above
   the death check**, so a kill a Wight scores on the tick the player dies is counted before the run
   ends, exactly as a bolt's arrival is.
9. **Enemies do not fight back, and that is a ruling rather than an omission.** No behaviour
   retargets onto a Wight, nothing damages one, and `MinionSystem.ApplyDamage` ships **called by
   nothing**. CH §3.2 is *"You don't fight. The dead do"* — the class works with Wights as a second
   source of damage, and the only thing that dies in M5 is a Wight's own clock. **Making enemies
   choose between the player and a Wight is the `TargetId` question [M5-03](M5-03-shroudstep-and-corpse-decoy.md)
   rule 8 refuses and M7-01's Choir pays for**, and doing it here would make a twenty-second Wight
   into the arena's whole attention economy with no playtest behind it. `ApplyDamage` exists so that
   when something does hurt one there is a door rather than a new mechanism.
10. **A Wight's death and a Wight's expiry are two different events.** `MinionDied` carries the
    position (M5-06's Second Death keystone wants it); `MinionDespawned` is the clock running out,
    and **it is what happens in every run this milestone can play**. They are separate because a
    keystone that says *"enemies killed by minions explode"* must not fire on a Wight that simply
    timed out.
11. **It pays no experience, counts toward no wave, and is in no save.** A Wight kills an enemy
    through `EnemySystem.ApplyDamage`, which is the one door that grants XP, so the *enemy's* death
    pays normally and the Wight itself is worth nothing. It is not in `RunSnapshot`: it joins
    *"cooldowns, granted shields, live zones"* on the by-ruling-unsaved list, and twenty seconds is
    not worth a **v4**.
12. **Nothing here allocates on the frame path.** One array of eight agents built at construction and
    recycled in place, a linear scan of at most eight against the enemy span, struct intents and
    struct events.
13. **[Ledger row 6] `BossBehaviour.Tick` gets the allocation row it is the only M4 tick path
    without.** One row in `BossPhasesTests`, beside the four that already exist
    (`Phase_TickAllocatesNothing`, `WardenBehaviourTests.Tick_AllocatesNothing`,
    `ShockwaveAndFissureTests.Tick_AllocatesNothing`, `ShardPayoutTests.Payout_AllocatesNothing`).
    **It must exercise the phase-change path, not only the quiet one** — the add-summoning branch is
    what nobody measured, and a row that only ticks a boss at full health measures the `inner`
    behaviour that already has its own row. Nothing in `BossBehaviour.cs` changes; if the row is red,
    that is a finding and *As built* says so.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Minion_StandsUpWithItsAuthoredNumbers` | the Gravecaller's `MinionSpec` / `Spawn` / 20 HP, 3.0 speed, 8 damage, expiring at `now + 20`, and one `MinionSpawned` |
| `Minion_RefusesAboveTheCap` | cap 3, three standing / a fourth `Spawn` / null, no event, `Count` 3 — rule 3 |
| `Minion_TheCapIsAStat` | cap 3 with a `Flat +4` on it / four more spawns / seven stand, and an eighth is refused by `MaxConcurrent` |
| `Minion_RefusesAboveMaxConcurrentWhateverTheCapSays` | a `Flat +100` on the cap / nine spawns / eight stand, none throws — rule 3's ceiling |
| `Minion_ExpiresOnTime` | one at `now` for 20 s / ticked to 20.0 / gone, one `MinionDespawned`, and **no** `MinionDied` — rule 10 |
| `Minion_IsRecycledNotReallocated` | the cap reached, one expired, one spawned / `AllocationAssert.None` over 1 000 cycles / zero |
| `Minion_IngestsItsReportedPosition` | a snapshot naming one at (4, 0, 4) / `Ingest` / `Position` and `Velocity` are the snapshot's, not core's guess — rule 4 |
| `Minion_WalksAtTheNearestLivingEnemy` | three Husks, one nearest / ticked / a `MinionMove` intent whose velocity points at the nearest, at the agent's `MoveSpeed.Value` |
| `Minion_IgnoresACorpse` | the nearest Husk dead, a live one further / ticked / it walks at the live one |
| `Minion_KeepsItsQuarryWithinTheCadence` | two Husks swapping which is nearest every frame / ticked 0.4 s / the quarry did not change — rule 6 |
| `Minion_DropsADeadQuarryImmediately` | its quarry killed mid-cadence / the next tick / a new quarry, without waiting out `RetargetInterval` |
| `Minion_StrikesAtItsReach` | a Husk at 1.4 m, reach 1.5 / ticked / the Husk takes `ContactDamage.Value`, one `MinionStruck`, and the next strike is refused until `AttackInterval` has passed |
| `Minion_DoesNotStrikePastItsReach` | a Husk at 1.6 m / ticked 5 s / no damage at all |
| `Minion_KillsAndTheKillPaysExperience` | a Husk at 1 HP in reach / ticked / one `EnemyDied`, and `EnemySystem.DrainXp` returns the Husk's `XpValue` — rule 11, the row that proves the kill goes through the normal door |
| `Minion_IsNotAnEnemy` | one standing / — / `EnemyRegistry.Alive` does not contain it, `SpawnDirector.IsStageComplete` is unaffected, and `PlayerCombat`'s candidate buffer never names it — rule 1, asserted against three real systems rather than by type |
| `Minion_HasNoBlackboard` | the whole of a Wight's working state / — / `QuarryId`, `NextAttackAt` and `ExpiresAt` are on the agent, and a `MinionAgent` exposes no `EnemyBlackboard` at all — rule 5, so a later task cannot give it one without arguing with this row |
| `Minion_TakesDamageThroughOneDoor` | `ApplyDamage` past its HP / — / dead, one `MinionDied` **carrying the position**, no `MinionDespawned` — rule 10 |
| `Minion_ClearIsSilent` | three standing / `Clear` / `Count` 0 and no event — `EnemySystem.Clear`'s rule |
| `Minion_TickAllocatesNothing` | eight minions against twenty-eight enemies, 10 000 ticks / `AllocationAssert.None` / zero |
| `Run_MinionsTickAfterTheEnemiesAndAboveTheDeathCheck` | a Wight that kills on the tick the player dies / one `RunSession.Tick` / the `EnemyDied` precedes the `PlayerDied`, and the XP is drained — rule 8 |
| `Boss_TickAllocatesNothing` | a Warden driven **across a phase threshold**, so the clear-and-summon branch runs / `AllocationAssert.None` / zero — [ledger row 6](../ROADMAP.md#carry-forward-into-m5), rule 13 |

**Guard rows are implied, not listed:** null `MinionSpec`, `IDomainEvents` and `IIntentSink` to the
constructor; a non-finite `now` or position to `Spawn`; a non-finite `dt`; and `TryGet` on an id that
has expired.

## Manual verification (Editor / device)

_None._ Nothing produces a Wight until [M5-04b](M5-04b-rise-and-minion-stats.md) and nothing draws
one until M5-05, so every run this build plays is unchanged. `WorldSnapshot.Minions` is filled by
`SnapshotBuilder`, which is M5-05's edit — **until then the array is empty in play and filled only by
tests**, which is stated here so an empty array is not read as a fault.

## Out of scope

- **Rise, and anything that produces a Wight in play.** M5-04b.
- **`IStatBlock` for a minion.** M5-04b, and rule 2 is the shape it mirrors.
- **Views, prefabs, pooling, and the concurrency policy of CH §8 q1** — whether a Wight counts
  against GD §11.1's enemy cap is a *rendering and fairness* question with a device answer. M5-05.
- **Enemies fighting back.** Rule 9.
- **Second Death, The Host, and every Legion node.** M5-06. Rule 3 and rule 10 exist so those are
  authoring rather than surgery.
- **Saving a Wight.** Rule 11.
- **Touching `BossBehaviour.cs`.** Rule 13 adds a measurement, not a change.

## As built

_Filled at merge, 6 000 bytes or fewer, measured._
