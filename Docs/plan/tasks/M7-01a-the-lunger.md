# M7-01a — The Lunger, and a dash you can step out of

**Size:** S · **Depends on:** M7-00e — every spec group is written before the first build task · **Branch:** `m7-01a-the-lunger`
**Design refs:** GD §8.1, §8.2, §12.4, §16.4; AR §9, §18.1, §18.2, §18.4; ADR-0008 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — one number added to its instrument, stated; [11](../ROADMAP.md#carry-forward-into-m7) — not this task's, and why is rule 11

## Goal

GD §8.1's fourth archetype exists: a body that walks in, marks a line on the floor for 0.9 s, and
then crosses 15 m of it at a speed nothing else in the arena moves at — so where you stand when the
line appears is the whole fight.

## What was already built for it

Two seams have waited for this archetype since M1, and grepping found both still reserved:

> `EnemyBlackboard.LungeDirection` — *"The direction a committed dash is travelling, captured on
> entering a telegraph and consumed on the dash itself, so a Lunger (M7-01) commits to where the
> player **was**. Unused until then."*

and AR §15's *"A Lunger telegraphs 0.9 s then commits to a straight line" is a unit test.* This task
is that field's first writer and that sentence's first row. **Nothing else about an enemy changes:**
the Lunger is a fifth `IEnemyBehaviour` beside the Chaser, the Spitter and the Bloater, dispatched by
the two switches that already exist and numbered after `Boss` because `EnemyDefinition` serialises the
kind by ordinal.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/LungeSpec.cs` | Core | **New.** The dash block — trigger range, distance, speed — `ExplosionSpec`'s shape |
| `Core/Ai/LungerBehaviour.cs` | Core | **New.** Approach, commit, telegraph, dash, recover |
| `Tests/Core/Ai/LungerBehaviourTests.cs` | Tests.Core | **New.** Every rule below |
| *small edits* | Core, Game, Data | `Core/Content/EnemySpec.cs` — `EnemyBehaviourKind.Lunger` appended, a `lunge` block optional and last, and the kind requiring it (rule 2); `Core/Ai/EnemyAgent.cs` — one arm in `Initialise`'s switch; `Core/Ai/EnemySystem.cs` — `Lunger` joins the shared arm in `Tick`, and a public `PlayerPosition` read (rule 6); `Core/Events/EnemyEvents.cs` — `LungeTelegraphed` (rule 4); `Game/Authoring/EnemyDefinition.cs` — three fields under a *Lunge* header, and the stale `_behaviour` tooltip rewritten; `Data/Enemies/Lunger.asset` — **new**, rule 9's numbers; `Data/Modes/Descent.asset` — one roster row at stage 6; `Prefabs/Composition/BootScope.prefab` — `_enemies` gains it; `Data/Localisation/English.asset` — `enemy.lunger.name`; `Data/Localisation/Pseudo.asset` — regenerated from the menu |
| *ripple* | Tests.Game | `EnemyLookTests` — `Descent_RostersAllThree` becomes `Descent_RostersEveryArchetype` at four, and `Assets_ObeyTheOneShotRule` / `Assets_AreTellableApart` walk the new path; `EnemyDefinitionTests.Enemy_ShippedXpIsThreeTimesCost` walks it; `ModeDefinitionTests.Descent_MatchesDesign` counts four. **62 `new EnemySpec(...)` sites across 47 files are untouched** (rule 2) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

public enum EnemyBehaviourKind
{
    Static, Chaser, Spitter, Bloater, Boss,

    /// <summary>
    /// Walks in, commits to a line, telegraphs it, and crosses it — GD §8.1's Lunger. Implemented by
    /// <c>LungerBehaviour</c> (M7-01a). <b>Appended, never inserted</b>: <c>EnemyDefinition</c>
    /// serialises this enum by ordinal, so this member is 5 for as long as a save or an asset exists.
    /// </summary>
    Lunger,
}

/// <summary>
/// What a lunging archetype does when it commits: how close it gets before it does, how far the dash
/// carries it, and how fast. <see cref="ExplosionSpec"/>'s shape — a block a kind requires and that
/// does not require its kind (M2-06).
/// </summary>
public sealed class LungeSpec
{
    /// <param name="triggerRange">Metres at or within which it stops walking and commits. 9 for the Lunger.</param>
    /// <param name="distance">Metres the dash covers. 15 (GD §8.1).</param>
    /// <param name="speed">Metres per second along the line. 24.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any argument is not a finite number greater than zero.</exception>
    public LungeSpec(float triggerRange, float distance, float speed);

    public float TriggerRange { get; }
    public float Distance { get; }
    public float Speed { get; }

    /// <summary>Seconds the dash lasts: <see cref="Distance"/> ÷ <see cref="Speed"/>. 0.625 for the Lunger.</summary>
    public float Duration { get; }
}

public sealed class EnemySpec
{
    // ... the sixteen arguments that exist, then, defaulted and last (rule 2):
    //     LungeSpec lunge = null
    // and: behaviour == Lunger with lunge == null throws ArgumentException naming the block.

    /// <summary>What it does when it commits, or <see langword="null"/> on an archetype that does not lunge.</summary>
    public LungeSpec Lunge { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public enum LungerState { Idle, Approach, Windup, Dash, Recover }

public sealed class LungerBehaviour : IEnemyBehaviour
{
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public LungerBehaviour(EnemyAgent agent);

    public LungerState State { get; }

    public void Tick(in EnemyTickContext ctx);
    public void Reset();
}

public sealed class EnemySystem
{
    /// <summary>
    /// Where the player is, as of this frame's <see cref="Ingest"/> — the real player, never a decoy
    /// (rule 6).
    /// </summary>
    public Vector3 PlayerPosition { get; }
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>
/// A Lunger has committed to a line and is telegraphing it: where it starts, which way it goes, how
/// far, and for how long it will stand before it does. Published beside the ordinary
/// <see cref="EnemyTelegraph"/>, never instead of it (rule 4).
/// </summary>
public readonly struct LungeTelegraphed
{
    public LungeTelegraphed(int id, Vector3 origin, Vector2 directionXZ, float length, float duration);

    public readonly int Id;
    public readonly Vector3 Origin;
    public readonly Vector2 DirectionXZ;
    public readonly float Length;
    public readonly float Duration;
}
```

## Behaviour

1. **Five states, flat, and the Chaser's shape with the strike replaced by a line.** `Idle` →
   `Approach` on `AggroRange`; `Approach` walks the path direction at `MoveSpeed.Value` exactly as
   `ChaserBehaviour.TickChase` does, and commits when `DistanceToPlayer <= Lunge.TriggerRange`
   **and** `HasLineOfSight`; `Windup` stands for `Spec.WindupTime`; `Dash` crosses the line for
   `Lunge.Duration`; `Recover` stands for `Spec.RecoverTime` and returns to `Approach`, never to
   `Idle` — the Chaser's reason (*"an enemy that has hit you knows where you are"*). **Exactly one
   `EnemyMoveIntent` per tick in every state**, the seam's rule.
2. **`LungeSpec` is optional and last on `EnemySpec`, and `Lunger` requires it.** `new EnemySpec(...)`
   has **62 sites across 47 files**, so M4-01a rule 4's trade applies once more: every existing
   construction keeps meaning what it meant. The validation is the Spitter's and the Bloater's line,
   one kind over — *a kind requires its block; a block does not require its kind* — so a `Chaser`
   carrying a lunge block is legal and inert, which is the two-task split M2-06 was built on.
3. **The direction is committed on entering `Windup` and never re-aimed.** `LungeDirection` is
   written once, from `DirectionToPlayer` at that tick, and the whole windup faces it; the dash
   travels it. **A committed line is the mechanic**: GD §8.1's *"punishes bad positioning"* only
   means something if stepping off the line after it appears is the answer. **The windup never
   cancels** — the Spitter's rule (AR §18.4, *"the dodge window is the flight"*), because the dodge
   here is the lane, and a Lunger that gave up when you moved would teach the opposite of what it is
   for. A zero `DirectionToPlayer` on the committing tick (the player standing inside the body) stays
   in `Approach` for that tick rather than committing to nowhere.
4. **The tell is two events on the committing tick, and the second is new.** `EnemyTelegraph(id,
   WindupTime)` goes out exactly as every other archetype's does, so `EnemyHitFeedback`'s swell
   reaches the Lunger with no view change. `LungeTelegraphed(id, origin, direction, Distance,
   WindupTime)` goes out beside it and carries what a *lane* needs and a swell does not. **Nothing
   subscribes to it in this task, and this is a stated exception to M6-06a rule 6**, which accepts a
   member nothing reads only when its reader is one task away. The reader is
   [M7-01d](../ROADMAP.md#m7--content-pass)'s lane decal, **four tasks later** in the build order —
   after the Weaver, the Elite and the Warden it also draws. The alternative is worse: publishing it
   there would mean a view task editing a core behaviour, which is the seam the spec groups are cut
   along. Until then the swell and the body's facing are the tell, and manual step 1 says so.
5. **The dash is a velocity for a duration, and the body decides where it stops.** `LungeDirection ×
   Lunge.Speed` every tick for `Lunge.Duration` seconds of `StateTimer`, then `Recover`. A pillar in
   the lane stops the `CharacterController` and core keeps pushing until the time is up — core holds
   no walls (AR §18.2) and a dash that measured its own progress would need a position it cannot
   trust. **The facing is `LungeDirection` throughout**, so the body does not swing round mid-dash.
6. **Contact is a swept test against the real player, once per dash.** Each `Dash` tick measures the
   XZ distance from `EnemySystem.PlayerPosition` to the segment from last tick's position to this
   tick's, and the first tick within `Spec.Reach` calls `ctx.Player.ApplyDamage(ContactDamage.Value,
   now)`. **Swept**, because at 24 m/s a 30 fps frame is 0.8 m and a point test would let the dash
   pass through a player standing still. **Once**, because a dash is one hit and the next is a new
   commitment. **The real player and never the quarry** — which is what `PlayerPosition` exists for:
   `EnemyBlackboard.PlayerPosition` is the *decoy* while a Shroudstep corpse stands (AR §18.1's lure
   row), and a Lunger lured into dashing at a corpse still crosses real ground. That row's sentence —
   *"every other way an enemy hurts the player already resolves against the real position"* — stays
   true because the Lunger joins it rather than becoming a second reader of `QuarryIsADecoy`. The
   read is a property rather than a field on `EnemyTickContext` because the context has **32
   construction sites**, and `_playerPosition` already exists on the census, written by `Ingest` in
   the same frame.
7. **A dash that ends is a dash that happened, whatever it hit.** `Recover` follows every dash, hit
   or miss, pillar or open floor: the punish window is the price of committing and it is paid either
   way. **`Recover` is the Lunger's `RecoverTime`**, 1.2 s — twice the Husk's, because the thing being
   punished took a 15 m decision.
8. **Allocation-free, and the per-tick dependencies are parked and cleared** exactly as
   `ChaserBehaviour.Tick` parks them (M2-07b rule 2): the machine's handlers are delegates built once,
   the swept test is struct arithmetic, and the one extra field is the previous position.
9. **`Lunger.asset`'s numbers.** GD §8.1 publishes three; the rest are ours and say so.

   | Field | Value | Why |
   |---|---|---|
   | `_maxHp` | **44** | GD §8.1 — four Censer hits, inside §12.4's band |
   | `_threatCost` | **9** | GD §8.1 |
   | `_targetPriority` | **4** | GD §8.1 |
   | `_xpValue` | **27** | 3 × cost, the convention `EnemySpec.XpValue` names |
   | `_moveSpeed` | **2.6** | under every class (3.0 / 3.1 / 3.4) — `Speed_EveryClassOutrunsEveryEnemy` walks it |
   | `_contactDamage` | **12** | the Spitter's. Rule 10 says why it is not higher |
   | `_reach` | **0.9** | the lane's half-width: body radius 0.4 plus half a stride |
   | `_windupTime` | **0.9** | GD §8.1 |
   | `_recoverTime` | **1.2** | rule 7 |
   | `_aggroRange` | **30** | every archetype's |
   | `_lungeTriggerRange` | **9** | a 15 m dash committed at 9 m ends 6 m past the player — the dodge is sideways, never backwards |
   | `_lungeDistance` | **15** | GD §8.1 |
   | `_lungeSpeed` | **24** | 0.625 s across; 1.5× the Oathbound's Charge, which is the one thing it must be faster than |
   | `_tint` / `_bodyScale` | **(0.55, 0.45, 0.25)** / **0.95** | a dark ochre, slightly lean — distinct from all three; M7-01d may retune it and owns the look |

   **The windup against a 3.0 m/s walk**: the lane is 1.8 m wide, which the slowest class clears in
   0.6 s of the 0.9 s telegraph. **At 9 m on a phone the line is on screen**, which is GD §12.4's
   on-screen rule met by the trigger range rather than by an indicator.
10. **The contact damage sits inside the envelope the shipped roster already has, and the envelope
    has a gap that is not this task's.** `Assets_ObeyTheOneShotRule` checks 35 % of the **Oathbound's
    140**; the Emberwright has **70**, which puts §12.4's ceiling at 24.5 and `12 × d(n)` past it from
    **stage 31** — the Spitter's own figure, while the Bloater's 15 is past it from **stage 20**.
    Lowering the Lunger alone would not close that and would make the one archetype built to punish a
    mistake the one that does not. The gap is **[ledger row 2](../ROADMAP.md#carry-forward-into-m7)'s**,
    stated there by M7-00a, and M8-05's.
11. **Row 11 is not answered by adding an archetype, and this task says so rather than implying it.**
    The collapse is `WaveComposer.Upgrade` swapping every body in a capped wave for the dearest
    affordable archetype; a ninth-cost Lunger is simply one more rung on that ladder. The fix is
    [M7-02b](M7-02b-who-buys-an-elite.md)'s, and M7-00a orders the table so that it lands before the
    Warden — the rung that would make a capped wave twenty Wardens.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Lunger_WalksInAndCommitsAtTheTriggerRange` | player at 20 m, closing / ticks / `Approach` until 9 m, then `Windup` — rule 1 |
| `Lunger_DoesNotCommitWithoutSight` | player at 6 m, `HasLineOfSight` false / ticks / still `Approach` — rule 1 |
| `Lunger_CommitsToWhereThePlayerWas` | commit, then the player moves 4 m sideways during the windup / the dash / `LungeDirection` and the dash velocity are the committing tick's — rule 3 |
| `Lunger_TheWindupNeverCancels` | commit, then the player leaves to 30 m / 0.9 s / `Dash` — rule 3 |
| `Lunger_DoesNotCommitToNowhere` | `DirectionToPlayer` zero on the committing tick / one tick / still `Approach` — rule 3 |
| `Lunger_TelegraphsTwiceOnTheCommittingTick` | commit / — / one `EnemyTelegraph(id, 0.9)` and one `LungeTelegraphed(id, origin, direction, 15, 0.9)`, and neither on any other tick — rule 4 |
| `Lunger_DashesForItsDurationAtItsSpeed` | a dash / ticked 0.625 s / every intent's velocity is `direction × 24`, then `Recover` — rule 5 |
| `Lunger_FacesTheLineThroughout` | windup and dash / — / every intent's facing is `LungeDirection` — rule 5 |
| `Lunger_HitsAPlayerInTheLane` | player 7 m down the line / the dash / one `PlayerDamaged` of 12 — rule 6 |
| `Lunger_TheSweepCatchesAFastFrame` | one 0.05 s tick carrying the body 1.2 m past a player 0.3 m off the line / — / hit — rule 6, the reason it is swept |
| `Lunger_HitsOncePerDash` | a player the body passes through / the whole dash / exactly one hit — rule 6 |
| `Lunger_MissesAPlayerWhoSteppedOut` | player 2 m off the line by the dash / — / no damage, still `Recover` — rules 6 and 7 |
| `Lunger_ALuredDashCrossesRealGround` | a decoy standing, the player in the lane toward it / the dash / the **player** is hit — rule 6 |
| `Lunger_RecoversThenApproaches` | a dash / 1.2 s / `Approach`, never `Idle` — rules 1 and 7 |
| `Lunger_OneIntentPerTickInEveryState` | a full cycle / — / one `EnemyMoveIntent` per tick — rule 1 |
| `Lunger_ResetReturnsToIdle` | mid-dash / `Reset` / `Idle`, timer 0, `LungeDirection` zero — AR §9 |
| `Lunger_TickAllocatesNothing` | a full cycle, 10 000 ticks / `AllocationAssert.None` / zero — rule 8 |
| `Spec_ALungerRequiresItsBlock` | `Lunger` with no `LungeSpec` / — / `ArgumentException` naming it — rule 2 |
| `Spec_TheBlockDoesNotRequireItsKind` | `Chaser` carrying a `LungeSpec` / — / constructs — rule 2 |
| `Spec_TheSixteenArgumentConstructorIsUnchanged` | the constructor as 62 sites call it / — / `Lunge` null — rule 2 |
| `Kind_LungerIsFive` | — / — / `(int)EnemyBehaviourKind.Lunger == 5` — ordinal is identity |
| `System_TicksALunger` | a Lunger in the census / `EnemySystem.Tick` / its behaviour ran, and the default arm did not throw |
| `System_PlayerPositionIsTheRealOne` | a decoy standing / `Ingest` / `PlayerPosition` is the snapshot's player, the blackboard's is the decoy — rule 6 |
| `Lunger_AssetCarriesTheDesignNumbers` | `Lunger.asset` / converted / rule 9's table, and `Lunge.Duration` 0.625 |
| `Descent_RostersTheLungerAtSix` | `Descent.asset` / — / `enemy.lunger` at 6 — GD §8.2 |

**Guard rows are implied, not listed:** `LungeSpec`'s three non-finite and non-positive refusals, and
the null agent.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 6 and watch wave 1. *Expected: one Lunger among the Husks — GD §8.2's
   one new thing — which walks in, swells for most of a second, and dashes past you. Stepping sideways
   during the swell makes it miss.* The line itself is not drawn until M7-01d; the swell and the
   body's facing are the tell until then.
2. **[Editor]** Let one dash through you. *Expected: one hit of about 12 × d(6), and the Lunger
   standing still for a second afterwards — rule 7's window.*
3. **[Editor]** As the Gravecaller, drop a Shroudstep corpse between you and a Lunger. *Expected: it
   commits to the corpse, and still hits you if you are in the lane — rule 6.*

## Out of scope

- **Drawing the lane.** [M7-01d](../ROADMAP.md#m7--content-pass), rule 4's reader.
- **A Lunger that reads the player's velocity.** Committing to where the player *was* is the design;
  leading them would be `ProjectileLead`'s job and a different enemy.
- **Knockback on the dash**, in either direction. A Charge that meets a dashing Lunger resolves
  exactly as a Charge meets a Husk.
- **The one-shot envelope against the Emberwright.** Rule 10, ledger row 2, M8-05.
