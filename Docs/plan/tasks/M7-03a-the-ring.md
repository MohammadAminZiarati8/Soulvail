# M7-03a — The ring: bodies that orbit a boss and turn its hits away

**Size:** M · **Depends on:** M7-02e · **Branch:** `m7-03a-the-ring`
**Design refs:** GD §8.1, §9.1 (rules 3, 4), §9.2, §11.1; AR §9, §18.1, §18.3, §18.4; ADR-0006, ADR-0011 · **Ledger rows:** none moved

## Goal

A boss can call in bodies that stand on a ring around it, turn with the ring, and turn away any hit
aimed at the boss from behind them. When the boss dies, the ring goes with it. Every phase clears
what the last phase called in, including the children those bodies split into.

## The reading of GD §9.2, ruled at M7-00c

*"Ringed by rotating Weaver shields"* is read as **bodies, not a shader**. Each shield is an enemy the
player can target and kill. A shield covers the boss across an arc centred on where it stands, and a
dead shield leaves a window that turns with the ring. The design documents are this project's own
drafts, so the reading is ruled here rather than asked. Three reasons choose it:

- **Target priority needs a target.** GD §9.2's lesson for this fight is *"target priority"*. A shield
  that cannot be shot teaches waiting, not choosing.
- **What the player sees is what core tests.** The cover test reads each shield's reported position, so
  a gap in the ring on screen is a gap in the guard.
- **The mechanism belongs to no one boss.** An orbit and an arc are data on an archetype, so any boss
  can author a ring. This task ships the mechanism and the one shield asset;
  [M7-03b](M7-03b-the-choirmother.md) authors the boss that summons it.

**The code says *escort*, not *shield*.** `Health.Shield` is the Oathbound's Aegis and
`ShieldedBehaviour` is M7-01c's Warden, so a third meaning for the word would be a bug waiting to be
read. The content keeps GD's name, *Weaver Shield*.

## What was already built for it

- **`BossBehaviour.Summon` already places adds on a ring.** It puts them `AddRingRadius` (3 m) from
  the boss, evenly spaced and without a draw. A shield summoned there is already in its slot.
- **`EnemySystem.ApplyDamage` knows where a hit came from** after [M7-01c](M7-01c-the-warden.md) rule 4,
  and asks the ward ([M7-02c](M7-02c-the-affix-roll.md) rule 8) and the guard before `Health`. The
  escort is a third question at the same door.
- **A Weaver splits on death** ([M7-01b](M7-01b-the-weaver.md)). A shield that carries the same block
  releases two Weaverlings when it dies, with nothing new written.

**What was not built, found while counting:**

- **A beat clears only the ids it summoned.** `BossBehaviour._addIds` holds exactly what `Summon`
  spawned, so the Weaverlings a dead shield leaves would outlive the phase that made them. GD §9.1
  rule 3 says the beat *"clears adds and resets pressure"*.
- **The opening phase never summons.** `Summon` runs from `EnterPhase`, which runs only on a crossing,
  so phase 0's summons are dropped. The Warden authors none, so no build has noticed. A ring that forms
  only at 66 % is a fight with no ring for a third of its length.
- **`RunSession.RequireAuthored` walks a boss's body and not its summons.** An unauthored summon
  surfaces as a `KeyNotFoundException` at the first crossing, mid-fight. That is the silence the
  method exists to move to `Start`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/OrbitSpec.cs` | Core | **New.** How fast a body turns around its summoner, and the arc it covers |
| `Core/Ai/OrbiterBehaviour.cs` | Core | **New.** Bind to the summoner, hold a slot on the ring, turn, and fall with the summoner |
| `Tests/Core/Ai/OrbiterTests.cs` | Tests.Core | **New.** Every rule below but the rows that open an asset |
| `Core/Ai/EnemySystem.cs` | Core | `IsEscortedFrom`, the escort at the damage door, `SummonerId` inherited by a split, `Orbiter` in `Tick`'s shared arm (rules 3–6) |
| `Core/Ai/BossBehaviour.cs` | Core | Stamps `SummonerId` on every summon, summons the opening phase on the first tick, and clears by summoner rather than by table (rules 7–9) |
| *small edits* | Core, Game, Data, Docs | `Core/Content/EnemySpec.cs` — `Orbiter` appended, an `orbit` block optional and last, the kind requiring it (rule 2); `Core/Ai/EnemyAgent.cs` — `SummonerId`, reset in `Initialise`, and one arm in its switch; `Core/Ai/EnemySystem.cs`'s `EnemyDeath` — a `summonerId`, optional and last (rule 6); `Core/Content/BossSpec.cs` — the remarks on `MaxSummonedBodies` and `BossPhaseSpec.SummonedBodyCount`, which name a table this task deletes; `Core/Events/BossEvents.cs` — `EscortBound` (rule 10); `Core/Run/RunSession.cs` — `RequireAuthored` walks every phase's summons and one step down each summon's split, and refuses an `Orbiter` on the enemy roster (rule 11); `Game/Authoring/EnemyDefinition.cs` — two fields under an *Orbit* header; `Data/Enemies/WeaverShield.asset` — **new**, rule 12's numbers; `Prefabs/Composition/BootScope.prefab` — `_enemies` gains it; `Data/Localisation/English.asset` — `enemy.weaver_shield.name`; `Pseudo.asset` regenerated; `Docs/Architecture.md` — the §18.1 boss row amended, and one §18.4 row (rules 3, 9); `Tests/Game/Authoring/EnemyDefinitionTests.cs` — the two `WeaverShield_*` rows, because Tests.Core cannot open an asset (M6-07c's reason); `Tests/Core/Ai/WardenBehaviourTests.cs` — `Opening_AWardenCallsInNothingAsBefore`, against the mirror of `WardenBoss.asset` it already keeps |
| *ripple* | Tests.Core, Tests.Game | `BossPhasesTests`' summon and beat rows are unchanged, and `AddCount` keeps its meaning (rule 9). `EnemyDefinitionTests.Enemy_ShippedXpIsThreeTimesCost` walks the shield as a family (rule 12). `EnemyLookTests.Assets_AreTellableApart` walks eight archetypes. **Every `new EnemySpec(...)` site but `EnemyDefinition.ToSpec`, all 18 `new BossSpec(...)` and the `new EnemyDeath(...)` in `RiseTests` are untouched**, because every new argument is optional and last. At M7-00c that is 61 of 62 enemy sites; M7-01a to M7-02f add more, and each is untouched for the same reason |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

public enum EnemyBehaviourKind
{
    Static, Chaser, Spitter, Bloater, Boss, Lunger, Shielded,

    /// <summary>
    /// Holds a slot on a ring around the boss that summoned it, turns with the ring, and covers the
    /// boss across an arc — GD §9.2's Weaver shields. <c>OrbiterBehaviour</c> (M7-03a). Appended: 7.
    /// </summary>
    Orbiter,
}

/// <summary>
/// How a summoned body keeps station on its summoner: how fast the ring turns, and the arc of the
/// summoner it covers. <see cref="ExplosionSpec"/>'s shape — a block a kind requires.
/// </summary>
public sealed class OrbitSpec
{
    /// <param name="degreesPerSecond">How fast the ring turns, counter-clockwise from above. Finite, above zero. 20 for the Weaver Shield.</param>
    /// <param name="escortArcDegrees">The arc of the summoner this body covers, centred on its own bearing. In (0, 360]. 60.</param>
    public OrbitSpec(float degreesPerSecond, float escortArcDegrees);

    public float DegreesPerSecond { get; }
    public float EscortArcDegrees { get; }

    /// <summary>cos(arc ÷ 2), computed once — what rule 3's dot product compares against.</summary>
    public float HalfArcCosine { get; }
}

public sealed class EnemySpec
{
    // ... after M7-01c's guard, defaulted and last:  OrbitSpec orbit = null
    // and Orbiter with orbit == null throws, naming the block.
    public OrbitSpec Orbit { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemyAgent
{
    /// <summary>
    /// The boss whose phase called this body in, or 0 for a body nobody summoned. Inherited by a split
    /// child. Written by <c>BossBehaviour.Summon</c> and <c>EnemySystem.Split</c>; 0 after <c>Initialise</c>.
    /// </summary>
    public int SummonerId { get; internal set; }
}

public readonly struct EnemyDeath
{
    // ... after every argument M7-02d adds for Affixes and Strike, defaulted and last:  int summonerId = 0
    /// <summary>The summoner of the body that died — what its split children inherit (rule 6).</summary>
    public int SummonerId { get; }
}

public enum OrbiterState { Unbound, Orbiting }

public sealed class OrbiterBehaviour : IEnemyBehaviour
{
    public OrbiterBehaviour(EnemyAgent agent);
    public OrbiterState State { get; }

    /// <summary>Metres from the summoner, fixed at binding.</summary>
    public float Radius { get; }

    public void Tick(in EnemyTickContext ctx);
    public void Reset();
}

public sealed class EnemySystem
{
    /// <summary>
    /// Whether a living orbiter <paramref name="agent"/> summoned stands between it and
    /// <paramref name="from"/>: the XZ bearing from the agent to the orbiter within the orbiter's arc
    /// of the bearing to <paramref name="from"/> (rule 3). False for anything but a boss.
    /// </summary>
    public bool IsEscortedFrom(EnemyAgent agent, Vector3 from);
}

public sealed class BossBehaviour
{
    /// <summary>How many living bodies this boss's summons stand for — its adds and their children (rule 9).</summary>
    public int AddCount { get; }
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>
/// An orbiter has taken its slot: which body, whose, and the arc it covers. Published once, on the
/// orbiter's first tick (rule 10).
/// </summary>
public readonly struct EscortBound
{
    public EscortBound(int id, int anchorId, float arcDegrees);
    public readonly int Id;
    public readonly int AnchorId;
    public readonly float ArcDegrees;
}
```

## Behaviour

1. **An orbiter is its own behaviour, because nothing else holds a slot.** A Chaser walks at the
   player and a Static emits nothing, so neither can keep station on a body. `OrbiterBehaviour` is the
   eighth `IEnemyBehaviour`. It is dispatched by `EnemyAgent.Initialise`'s switch and by
   `EnemySystem.Tick`'s shared arm, and it is numbered after `Shielded` because `EnemyDefinition`
   serialises the kind by ordinal. **Exactly one `EnemyMoveIntent` per tick in every state**, which is
   the seam's rule.
2. **`OrbitSpec` is optional and last on `EnemySpec`, and `Orbiter` requires it.** The block goes
   after M7-01c's `guard`, so every existing construction keeps meaning what it meant. The
   validation is the Lunger's line one kind over: *a kind requires its block; a block does not require
   its kind*.
3. **A hit on a boss is turned away when a living orbiter it summoned stands between the boss and the
   hit's source.** The test is `IsEscortedFrom(boss, source)`. It walks `Registry.Alive` for living
   agents whose `SummonerId` is the boss and whose spec carries an orbit. For each, the XZ bearing
   `b` from the boss to the orbiter and `s` from the boss to the source are compared, and
   `dot(b̂, ŝ) >= Orbit.HalfArcCosine` covers.
   - **Degenerate cases.** A bearing or a source direction shorter than `1e-4` m covers nothing. A
     source at the boss's own position lands, which is M7-01c rule 3's self case.
   - **The escort is a sector from the boss's centre, never a wall.** A source inside the ring, in a
     shield's arc, is still covered. Standing inside the ring is not an answer, and killing a shield
     is.
   - **Every door that can reach a boss is covered.** The source is whatever M7-01c rule 4 passes:
     the cone, the Charge, a bolt's origin, a burn's centre and a Wight. The sixth door, a Bloater's
     fuse, only ever damages the Bloater. A Gravecaller's army flanks a ring no better than the player
     does.
4. **The door asks the ward, then the guard, then the escort.** All three return `Blocked`, apply
   nothing, and publish `EnemyDamaged(id, 0, fraction, killed: false)`, M7-01c rule 3's shape. So a
   covered hit shrugs in M7-01d's steel with no view change, and M7-01c rule 6's *"a blocked hit
   shoves nobody"* holds. **The walk runs only for a boss.** Only a boss summons, so
   `agent.Spec.Behaviour == Boss` is the gate, and the cost is paid by the one body in a stage that
   can be escorted: at most 64 comparisons per hit on it, and none on anything else.
5. **An orbiter binds on its first tick, keeps its slot, and turns.** On its first tick it resolves
   `SummonerId` through the registry. Binding records three things from the reported positions, each
   the one it was spawned on:
   - `Radius`, its XZ distance from the anchor;
   - its slot angle, its bearing from the anchor;
   - `now`, the moment it bound.

   From then on its target is `anchor + Radius × (cos θ, 0, sin θ)`, with
   `θ = slot + DegreesPerSecond × (now − boundAt)` in radians, measured from +X toward +Z.
   - **The velocity is `(target − position) ÷ dt`, clamped to `MoveSpeed.Value`.** So a body pushed
     off its slot, by the player or a pillar, walks back at its own speed. A `dt` that is not above
     zero emits a zero velocity.
   - **Every orbiter a phase summons binds on the same tick, so the ring keeps its spacing** from one
     shared clock rather than from each body's own drift.
   - **The anchor is read live, every tick.** A boss that is moved takes its ring with it.
   - **The facing is outward**, the unit bearing from the anchor. The body and M7-03c's plate point
     away from what they cover.
   - **An orbiter standing on its anchor binds at `AddRingRadius` and slot 0.** `Summon` cannot put it
     there; a bearing of length zero would otherwise be a direction made of noise.
6. **A child inherits its parent's summoner.** `EnemyDeath` carries the dead body's `SummonerId`,
   banked in `ApplyDamage`'s kill branch. `EnemySystem.Split` stamps it on each child it stands up.
   *"A child of an add is an add"* is what rule 9's clear reads, and it is identity, not an argument
   about which bodies a boss stage happens to hold. **`EnemyAgent.Initialise` resets it to 0**, so a
   pooled agent the director rents again answers to nobody.
7. **Every summon is stamped.** `BossBehaviour.Summon` writes `SummonerId = _agent.Id` on each body
   `Spawn` returns. That happens after `EnemySpawned` is published, so the spawn event carries no
   summoner, which is M4-01b rule 7's reasoning. A view that needs the pairing gets it from
   `EscortBound`.
8. **The opening phase's summons are called in on the boss's first tick, with no beat.**
   `BossBehaviour.Tick` summons phase 0 once, on the first tick after the phase check, and a `Reset`
   re-arms it. No `BossBeatStarted` goes out, because the fight is starting rather than changing. **The
   Warden authors no opening summons, so its fight is unchanged**, which `Opening_AWardenCallsInNothingAsBefore`
   pins.
9. **A beat clears every living body the boss's summons stand for.** `ClearAdds` walks
   `Registry.Alive` and queues `DespawnAtEndOfTick` for each living agent whose `SummonerId` is this
   boss, which covers the adds and their children.
   - **The id table goes.** `_addIds` and its sizing from `BossSpec.MaxSummonedBodies` are deleted,
     and `AddCount` becomes that walk's count, so `Summon_ClampsToTheConcurrencyCap`'s *"two fit"*
     still reads 2.
   - **A body nobody summoned survives the beat**, as today. A corpse is not alive and is left to
     `SweepCorpses`.
   - **The forward walk stays forward and the despawn is deferred**, which is AR §18.1's row, unchanged.
10. **An orbiter announces its slot once, and falls with its anchor.**
    - **On binding** it publishes `EscortBound(id, anchorId, EscortArcDegrees)`. Nothing reads it in
      this task. That is a stated exception to M6-06a rule 6, argued as M7-01a rule 4 argues it: the
      reader is [M7-03c](M7-03c-a-song-you-can-see.md)'s arc, and publishing it from a view task would
      mean a view task editing a core behaviour.
    - **An orbiter whose anchor is missing or dead** queues `DespawnAtEndOfTick` on itself and stands.
      That covers the boss's death, and a body with `SummonerId` 0 that no boss called in. It pays no
      experience and does not split: the ring falls with the boss rather than being killed off it.
    - **Its children are not orbiters and do not fall.** A boss stage waits for them, which is
      [M7-01b](M7-01b-the-weaver.md) rule 6.
11. **What a boss can call in is refused at `Start`, not at the crossing.**
    - `RequireAuthored` walks every `AddWave` of every phase of every rostered boss through
      `RequireArchetype`, naming the boss.
    - It takes one step down each summon's split, which is M7-01b rule 4's step applied to summons.
    - It refuses an `Orbiter` on the mode's *enemy* roster, naming it. A composed orbiter has no
      summoner and would despawn on its first tick: budget spent on nothing.
12. **`WeaverShield.asset`.** It is a Weaver that holds station. GD §8.1 publishes the family's numbers,
    and the rest is ours.

    | Field | Value | Why |
    |---|---|---|
    | `_id` / `_nameKey` | `enemy.weaver_shield` / *"Weaver Shield"* | the asset-naming rule |
    | `_maxHp` | **50** | the Weaver's, GD §8.1 |
    | `_threatCost` / `_xpValue` | **12** / **18** | the Weaver's, so the family is 18 + 2 × 9 = 36 = 3 × 12 and M7-01b rule 7 walks it as one |
    | `_targetPriority` | **3** | above a Weaverling's 2, so the gun prefers the shield in the way over the child chasing you. That is a choice the player overrides by tapping |
    | `_moveSpeed` | **2.5** | the servo's cap. It is above the ring's 1.05 m/s at 3 m and 20°/s, so a shield knocked off its slot catches up, and it is under every class's 3.0, so `Speed_EveryClassOutrunsEveryEnemy` stays true |
    | `_contactDamage` / `_reach` / `_windupTime` / `_recoverTime` | **5** / **1.0** / **0** / **0** | read by nothing: an orbiter never strikes. The Weaverling's contact, inside every envelope |
    | `_aggroRange` | **40** | read by nothing |
    | `_behaviour` | **Orbiter** | rule 1 |
    | `_orbitDegreesPerSecond` / `_orbitEscortArcDegrees` | **20** / **60** | a full turn in 18 s. At 12 m a player circles at 14°/s, so a window sweeps past a ranged player rather than following them, and at 8 m (21°/s) a melee player can walk with it. Six shields at 60° close the ring exactly |
    | `_splitInto` / `_splitCount` | **`enemy.weaverling`** / **2** | GD's *"Weaver"*: opening a window costs two children |
    | `_tint` / `_bodyScale` | the Weaver's **(0.74, 0.70, 0.58)** / **1.0** | the same family at its own size; M7-03c gives it its plate |

13. **Nothing on a frame path allocates.** Binding is a lookup and three floats. The orbit is a sine, a
    cosine and a clamp. The escort walk is a span and dot products, and the clear is a span and a queue.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Orbiter_BindsToItsSummoner` | a fixture boss whose phase 0 summons six / its first tick, then theirs / each `SummonerId` is the boss, `Radius` 3, one `EscortBound(id, bossId, 60)` each — rules 5, 7, 10 |
| `Orbiter_KeepsItsSlotAndTurns` | bound at bearing 0 / 1 s at 20°/s / the intent points at the ring's point at 20° — rule 5 |
| `Orbiter_TheRingKeepsItsSpacing` | six bound together / 10 s / their targets are 60° apart — rule 5 |
| `Orbiter_WalksBackAtItsOwnSpeed` | pushed 1 m off its slot / a tick / velocity toward the slot, magnitude `MoveSpeed.Value` — rule 5 |
| `Orbiter_FollowsAMovedAnchor` | the boss moved 2 m / a tick / the target is on the ring around the new position — rule 5 |
| `Orbiter_FacesOutward` | any tick / — / facing is the unit bearing from the anchor — rule 5 |
| `Orbiter_FallsWithItsAnchor` | the boss killed / the next behaviour pass / every orbiter despawned at the end of that tick; no `EnemyDied`, no XP, no split — rule 10 |
| `Orbiter_WithNoSummonerLeaves` | an orbiter spawned directly / its first tick / despawned, nothing thrown — rule 10 |
| `Orbiter_OneIntentPerTickInEveryState` | `Unbound`, `Orbiting`, and the tick it finds its anchor dead / — / one `EnemyMoveIntent` a tick — rule 1 |
| `Escort_TurnsAwayAHitFromBehindAShield` | a shield at bearing 0, a source 20 m out at bearing 0 / `ApplyDamage` / `Blocked`, HP unchanged, one `EnemyDamaged(id, 0, 1, false)` — rules 3, 4 |
| `Escort_AWindowLetsItThrough` | that shield dead, its neighbours alive / the same hit / applied — rule 3 |
| `Escort_TheArcEndsAtItsHalfWidth` | sources 29.9° and 30.1° off a shield's bearing / — / blocked, then applied — rule 3. Not *exactly* 30°: a cosine of a bearing rebuilt from positions can miss `HalfArcCosine` by an ulp either way |
| `Escort_IsASectorNotAWall` | a source 1.5 m out in a shield's arc, inside the ring / — / blocked — rule 3 |
| `Escort_ASourceAtTheCentreLands` | a source 1e-5 m from the boss / — / applied — rule 3 |
| `Escort_EveryDoorIsCovered` | a closed ring / a cone, a Charge, a bolt, a burn and a Wight, each from outside / each `Blocked` — rule 3 |
| `Escort_ACorpseCoversNothing` | the covering shield dead and still registered / — / applied — rule 3 |
| `Escort_OnlyItsOwnSummonerIsCovered` | two bosses, a ring on one / a hit on the other from behind the ring / applied — rule 3 |
| `Escort_ABodyInsideTheRingIsNotCovered` | a composed Husk standing 1.5 m from the boss, inside a closed ring / a hit on the Husk from behind a shield / applied — rule 4's gate: only a boss is asked |
| `Summoner_AChildInheritsIt` | a summoned Weaver-kind add killed / the split / both children's `SummonerId` is the boss — rule 6 |
| `Summoner_ARecycledAgentForgets` | a summoned body despawned and rented by the director / — / `SummonerId` 0 — rule 6 |
| `Opening_ThePhaseSummonsOnTheFirstTick` | a boss whose phase 0 authors six / its first tick / six spawned, no `BossBeatStarted` — rule 8 |
| `Opening_AWardenCallsInNothingAsBefore` | *(in `WardenBehaviourTests`)* its mirror of `WardenBoss.asset` / the first ten ticks / no spawn, no event beyond M4's — rule 8 |
| `Opening_AResetReArmsIt` | a boss reset and ticked / — / its opening summons come in again — rule 8 |
| `Beat_ClearsTheChildrenOfItsAdds` | a summoned add killed and split into two / the next crossing / both children gone on the beat's first tick, and the parent's corpse left to `SweepCorpses` — rule 9 |
| `Beat_LeavesABodyItDidNotCallIn` | a composed Husk beside the adds / a crossing / the Husk stands — rule 9 |
| `AddCount_CountsWhatItsSummonsStandFor` | two adds and one's two children / — / 3 — rule 9 |
| `Start_RefusesASummonNobodyAuthored` | a rostered boss summoning an absent id / `Start` / `KeyNotFoundException` naming the boss and the id, nothing announced — rule 11 |
| `Start_RefusesASummonsMissingChild` | a summon splitting into an absent id / `Start` / refused, naming both — rule 11 |
| `Start_RefusesAnOrbiterOnTheRoster` | `Orbiter` on the enemy roster / `Start` / refused, naming it — rule 11 |
| `Spec_AnOrbiterRequiresItsBlock` | `Orbiter` with no `OrbitSpec` / — / throws, naming it — rule 2 |
| `Spec_TheBlockDoesNotRequireItsKind` | `Chaser` carrying an orbit / — / constructs — rule 2 |
| `Spec_TheConstructorAsItsSitesCallIt` | the constructor as the existing sites call it / — / `Orbit` null — rule 2 |
| `Kind_OrbiterIsSeven` | — / — / `(int)EnemyBehaviourKind.Orbiter == 7` — ordinal is identity |
| `System_TicksAnOrbiter` | an orbiter in the census / `EnemySystem.Tick` / its behaviour ran, and the default arm did not throw — rule 1 |
| `Orbiter_TickAllocatesNothing` | six orbiting, 10 000 ticks / `AllocationAssert.None` / zero — rule 13 |
| `Escort_TheDoorAllocatesNothing` | 10 000 covered and 10 000 uncovered hits / `AllocationAssert.None` / zero — rule 13 |
| `WeaverShield_AssetCarriesTheNumbers` | *(in `EnemyDefinitionTests`)* `WeaverShield.asset` / converted / rule 12's table |
| `WeaverShield_TheFamilyIsWorthThreeTimesItsCost` | *(in `EnemyDefinitionTests`)* the shield and two Weaverlings / — / 18 + 2 × 9 = 3 × 12 — rule 12 |

**Guard rows are implied, not listed:** `OrbitSpec`'s non-finite or non-positive speed and its arc
outside (0, 360]; `IsEscortedFrom`'s null agent and non-finite point.

## Manual verification (Editor / device)

_None this task._ Nothing rosters or summons the shield until [M7-03b](M7-03b-the-choirmother.md)
authors the boss that calls it in, and the first time anyone sees a ring is M7-03c's probe. The
Warden's fight is unchanged, and `Opening_AWardenCallsInNothingAsBefore` is the evidence.

## Out of scope

- **The boss that summons a ring.** [M7-03b](M7-03b-the-choirmother.md).
- **Drawing the arc, or the shield's plate.** [M7-03c](M7-03c-a-song-you-can-see.md), reading
  `EscortBound` and M7-01d's `EnemyLook.GuardArcDegrees`.
- **A ring that turns clockwise, or one that speeds up by phase.** A phase that wants either summons
  a different orbiter asset. Nothing is built ahead of it.
- **Knockback on an orbiter.** A shoved shield walks back (rule 5). A boss that must not be shoved is
  M7-03b's.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
