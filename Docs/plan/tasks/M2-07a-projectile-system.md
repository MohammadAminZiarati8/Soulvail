# M2-07a — `ProjectileSystem`: shots that are already in the air, and who decides they landed

**Size:** S · **Depends on:** M2-06 · **Branch:** `m2-07a-projectile-system`
**Design refs:** GD §8.1 (the Spitter), §7.2 (cover), §12.4 (the one-shot rule); CC §6.4 (Bulwark); AR §3, §4.1, §6, §18.1, §18.2, §18.4; ADR-0003, ADR-0011 · **Ledger rows:** 7 — settled here

## Goal

A run can carry shots that are in the air: fired at a point, arriving after a flight the player can walk out of, and resolved by core rather than by a collider — with the fact-versus-direct-call question that has been open since M1-18 answered once, in writing, before two archetypes answer it two ways.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ProjectileSystem.cs` | Core | The shots in flight and their arrivals; `Projectile` grouped with it |
| `Core/Events/ProjectileEvents.cs` | Core | `ProjectileFired`, `ProjectileImpacted` (grouped, `EnemyEvents.cs`' precedent) |
| `Tests/Core/Combat/ProjectileSystemTests.cs` | Tests.Core | Every rule below |
| *small edits* | | `RunSession.Start` composes one and `Tick` gains a step; `RunState` + `Projectiles` (`internal`) and a public `InFlightProjectiles` read; `RunSession.End` clears it; `DebugOverlay` shows the count; **`IRunSession`'s remarks** — the stale `ReportContact` and `ReportProjectileHit` promises are removed and replaced with rule 1 (**row 7**); **AR §5**'s ports row, which still lists both; **AR §18.1**'s ordering table gains the projectile step; **AR §18.2** gains rule 1 as an invariant |
| *ripple* | | `RunSessionTests`' ordering rows gain the projectile step; every fixture that constructs a `RunState` — it is `internal` and built only by `RunSession`, so this is one call site |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Events;

/// A shot has left. Carries everything a view needs to draw the whole flight without asking again:
/// where it started, where it will land, and how long it takes (M2-09).
public readonly struct ProjectileFired
{
    public readonly int Id;
    public readonly ContentId SpecId;      // the archetype that fired it
    public readonly int SourceId;          // the enemy that fired it, or 0 — see rule 5
    public readonly Vector3 Origin;
    public readonly Vector3 Target;
    public readonly float FlightTime;      // seconds from now until it arrives
}

/// A shot has arrived. Published whether or not it hit anything, because the view has to stop
/// existing either way — the mirror of `EnemyDespawned` rather than of `EnemyDied`.
public readonly struct ProjectileImpacted
{
    public readonly int Id;
    public readonly Vector3 Position;      // where it landed: the target point, always
    public readonly bool HitPlayer;
}
```

```csharp
namespace Soulvail.Core.Combat;

/// One shot in flight. A point and a moment, not a body — see rule 2.
public readonly struct Projectile
{
    public Projectile(ContentId specId, int sourceId, Vector3 origin, Vector3 target,
                      float speed, float radius, float damage);

    public ContentId SpecId { get; }
    public int SourceId { get; }      // a record of who fired, never resolved against the registry
    public Vector3 Origin { get; }
    public Vector3 Target { get; }
    public float Speed { get; }
    public float Radius { get; }
    public float Damage { get; }      // the shooter's ContactDamage.Value as of the shot (M2-06 rule 5)
}

public sealed class ProjectileSystem
{
    /// The id `Fire` returns when it refused the shot. Zero, because ids are issued from 1.
    public const int NoProjectile = 0;

    public ProjectileSystem(IDomainEvents events, int capacity);

    public int Capacity { get; }
    public int InFlightCount { get; }

    /// Puts a shot in the air and publishes `ProjectileFired`. Returns its id, or `NoProjectile`
    /// when it was refused (rule 7).
    public int Fire(in Projectile shot, float now);

    /// Lands every shot whose arrival has passed, oldest first.
    public void Tick(float now, PlayerCombat player);

    /// Forgets every shot in flight, silently. For the end of a run only.
    public void Clear();
}

// RunState (added)
internal ProjectileSystem Projectiles { get; }
public int InFlightProjectiles { get; }    // a scalar read, never the handle (AR §18.2)
```

## Behaviour

**The ruling — ledger row 7**

1. **Core decides every outcome an enemy causes, and calls `PlayerCombat.ApplyDamage` directly. Unity owes core a *fact* only when the answer depends on colliders core does not hold; when the geometric question is a standing one rather than an instant, it owes a *sense* on the snapshot instead.** That is the whole rule, and it settles row 7 for the three archetypes that were about to answer it separately:
   - **Contact (Husk, M1-18; Bloater, M2-08)** — a direct call from core-perceived XZ distance. M1-18's choice, now ratified rather than left as a precedent nobody decided.
   - **A projectile impact (Spitter, M2-07b)** — a direct call, made here, at the arrival time core itself computed.
   - **`IRunSession` therefore gains no member in M2.** Its remarks promised a `ReportContact` "with the chasers of M1-18" and a `ReportProjectileHit` "with the Spitter in M2-07"; both are wrong, both have been wrong since M1-18 merged, and both are deleted in this change along with AR §5's matching row. `ReportConeHits` and `ReportChargeHits` remain what facts are *for*: a wedge and a swept line, both of which are questions about which colliders a shape touched.
   **The rejected alternative is the fact route**, and it fails on three counts rather than on taste. It moves the moment of damage into the frame's physics phase, one step after the tick that decided it, so a shot lands a frame after core believes it did. It makes enemy damage **non-reproducible from a seed** — the one thing M2-13 and M2-14 are being built to preserve — because it would depend on where Unity's colliders happened to be. And it would mean an enemy needs a *body with a trigger* before it can hurt anyone, which inverts AR §3: core would be waiting for the senses to tell it what it had already decided.
   **The price is named rather than hidden:** core holds no walls, so a shot passes through a cover pillar, which GD §7.2 says it must not. Out of scope below, and a ledger row with an owner.

**A shot**

2. **A projectile is a point and a moment, not a body.** `Fire` computes the impact point and the arrival time once, from the shooter's position, the target position, the speed and the XZ distance between them (AR §18.4), and nothing about the shot changes afterwards. There is no per-tick position in core, and no per-tick intent: the view is handed origin, target and flight time in one event and draws the arc itself (M2-09), the same bargain `EnemyKnockbackIntent` already makes for a shove.
3. `FlightTime` is `xzDistance / speed`, and it is what makes the shot dodgeable — the Spitter's 14 m at 12 m/s is **1.17 s**, against a player who moves 5.4 m/s and a blast radius of 1.6 m. Any movement at all clears it, which is GD §8.1's *"punishes standing still"* stated as arithmetic. A shot fired from inside its own radius arrives on the next tick rather than instantly: `FlightTime` may be zero, and a zero is a shot that has already arrived, never a division.
4. **The target is a point on the ground, decided at the moment of firing, and it never tracks.** A homing shot would punish nothing — the player's only counter would be an i-frame — and the whole archetype exists to make standing still cost something.
5. **A shot outlives its shooter.** Killing the Spitter after it has released does not un-fire the bolt the player is already dodging, so `SourceId` is a record of who fired rather than a handle: it is never resolved against the registry, and an id that has despawned is not an error. Anything wanting the shooter's position asks the event's `Origin`.

**Arrival**

6. `Tick` lands every shot whose arrival time is at or before `now`, in the order they were fired. Landing one means: test the player's XZ distance against `Radius`; if it is inside, `player.ApplyDamage(damage, now)`; then publish `ProjectileImpacted(id, target, hit)` and drop the shot. **i-frames are not consulted here** — `PlayerCombat.ApplyDamage` owns that question and publishes a blocked `PlayerDamaged` either way, exactly as `ChaserBehaviour.EnterStrike` leaves it, so a shot that arrives during a dodge is still a shot that arrived.
7. **A refused shot is silent and costs nothing.** At `Capacity` in flight, `Fire` publishes nothing, returns `NoProjectile`, and the caller carries on: one lost bolt is better than an exception that ends the run, and at M2-04's concurrency cap of 28 with one shot in the air per Spitter the array cannot fill without something already being wrong. `Capacity` comes from `BootInstaller` beside the enemy cap, and is guarded positive.
8. Ids are issued from 1 and never reused within a run — `EnemyRegistry`'s rule, for its reason: an id held across an impact should be *stale*, not misleading. The store is a preallocated array walked backwards on removal, so a landed shot cannot shift one that has not been examined yet.

**Wiring**

9. **`ProjectileSystem` writes `CombatBlackboard.IncomingProjectiles` and `PlayerCombat` never touches it.** The field has existed since M1-08 saying *"zero until M2-07 gives something the means to fire one"*, and this is that task; it is CC §6.4's Bulwark trigger and M3's auto-cast reads it. One owner for one field, and the ordering below is what makes the value this frame's rather than last frame's.
10. **`RunSession.Tick` gains one step, after the enemy behaviours and before the death check**: `… → combat → enemy behaviours → projectiles → (dead? end) → director → motor → intent`. After the behaviours, because a shot fired this tick starts flying now and cannot arrive on the tick it left. Before the death check, because a bolt that kills must end the run on the tick it landed, exactly as a Husk's strike does. This is an addition to AR §18.1's ordering table and composes with M2-05 rule 14's director step, which stays where it is.
11. `RunSession.Start` composes one per run and `End` clears it. `RunState.Projectiles` is `internal` with a public `InFlightProjectiles` scalar, because a public handle on something with a `Tick` lets a view advance the simulation with nothing in the compiler to object (AR §18.2).
12. `Clear` forgets every shot and publishes nothing — `EnemySystem.Clear`'s reasoning for the same moment: the scope is going away and with it every subscriber an event could reach.
13. **Nothing here draws from `IRandom`.** A shot's spread, if there is ever one, is a change to what a seed means (ADR-0011); today the shot goes exactly where it was aimed, and `Fire` and `Tick` allocate nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fire_PublishesWithFlightTime` | 14 m apart, speed 12 / `Fire` / one `ProjectileFired`, `FlightTime` 1.1667 ± 0.001, id 1 |
| `Fire_FlightTimeIsXzOnly` | target 14 m away on XZ and 3 m below / `Fire` / the same flight time (rule 2, AR §18.4) |
| `Fire_ZeroDistance_ArrivesNextTick` | origin == target / `Fire`, `Tick(now)` / lands, no divide, no throw (rule 3) |
| `Fire_IssuesIdsFromOneAndNeverReuses` | fire, land, fire / — / ids 1 then 2 |
| `Fire_AtCapacity_IsRefusedSilently` | capacity 2, two in flight / `Fire` / returns `NoProjectile`, no event, `InFlightCount` 2 (rule 7) |
| `Fire_AfterCapacityFrees_Works` | as above, one lands / `Fire` / a new id, an event |
| `Tick_LandsNothingEarly` | flight 1 s / `Tick` at +0.99 / no `ProjectileImpacted`, no damage |
| `Tick_LandsOnTheDot` | flight 1 s / `Tick` at exactly +1 s / lands (rule 6) |
| `Tick_HitsPlayerInsideRadius` | radius 1.6, player 1.5 m from target / arrival / player loses the damage, `ProjectileImpacted.HitPlayer` true — **damage arrives by a direct call inside the tick, with no fact reported and no `IRunSession` member involved** (rules 1, 6) |
| `Tick_MissesPlayerOutsideRadius` | player 1.7 m from target / arrival / no damage, `HitPlayer` false, **the event still published** (rule 6) |
| `Tick_MissIsDecidedOnXz` | player 1 m away on XZ, 4 m above / arrival / a hit (AR §18.4) |
| `Tick_MovingOutOfTheWayWorks` | player at the target point when fired, 6 m away by arrival / — / no damage — GD §8.1's whole archetype (rule 4) |
| `Tick_DoesNotConsultIFrames` | player dodging at arrival, inside the radius / — / `PlayerDamaged` published blocked, by `PlayerCombat` (rule 6) |
| `Tick_LandsSeveralInFireOrder` | three shots arriving in one step / `Tick` / three impacts, ids 1, 2, 3 in that order |
| `Tick_LandsOldestFirstAcrossRemovals` | five shots, the middle three due / `Tick` / exactly those three, and the two survivors are still in flight (rule 8) |
| `Tick_ShotOutlivesItsShooter` | fired by enemy 4, enemy 4 despawned / arrival / it lands and damages (rule 5) |
| `Tick_WritesIncomingProjectiles` | two in flight / `Tick` / `Combat.Blackboard.IncomingProjectiles == 2`; both land / `Tick` / 0 (rule 9) |
| `Tick_AllocatesNothing` | 16 in flight, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 |
| `Tick_DrawsNoRandom` | a `FixedRandom` that throws on any draw / fire and land 10 shots / no draw (rule 13) |
| `Clear_ForgetsEverythingSilently` | three in flight / `Clear` / `InFlightCount` 0, no events; `Tick` / nothing lands (rule 12) |
| `Session_TicksProjectilesAfterBehavioursBeforeDeathCheck` | a shot due this tick that kills the player / `Tick` / `PlayerDied` then `RunEnded`, and the motor wrote no intent (rule 10) |
| `Session_ExposesCountAndNotTheSystem` | a run with two in flight / — / `State.InFlightProjectiles == 2`; `Projectiles` is not on the public surface (rule 11) |
| `Session_EndClearsThem` | a run with shots in flight / `End`, `Start` again / `InFlightProjectiles` 0 |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None. Nothing fires until M2-07b and nothing is drawn until M2-09; `DebugOverlay`'s count is the only visible surface and it reads zero for the length of this task, which is what manual step 1 of M2-07b checks against.

## Out of scope

- **Firing one.** M2-07b's Spitter is the first and only caller.
- **Drawing one.** M2-09 — `ProjectileFired` carries the whole flight so that the view needs nothing else.
- **Cover.** GD §7.2 says a pillar blocks an enemy projectile and rule 1's price is that it does not. The cheapest honest fix is a **line-of-sight sense**: `EnemySense.HasLineOfSight` has existed unfilled since M0-05, and a `LineOfSightSense` adapter on `NavPathSense`'s pattern — a population-scaled per-frame budget, ~5 raycasts a frame at 28 enemies — would let the Spitter simply not fire through a pillar. **Ledger row 13, owner M2-11**, which is where pillars stop being scene dressing and become an arena contract. Rejected alternatives: the projectile view raycasting and reporting a block, which is the fact route rule 1 just declined; and core holding a wall list, which is a second world model.
- **Damage from off-screen.** A Spitter shooting from 14 m can be behind the camera, and GD §12.4's on-screen rule forbids damage from outside the frustum without an edge indicator. **Ledger row 14, owner M2-12**, decided together with row 12's out-of-range focus tap, because both are the same question about things the player cannot see.
- **A projectile the player fires.** M5-01's Gravecaller, with CC §3.7's leading. This type is deliberately not generalised for it — a player projectile aims at a *lead point* against a moving target, which is a different decision made in a different place.
- **Per-archetype projectile content.** `Projectile` carries the numbers the shot was fired with; a `ProjectileDefinition` asset arrives when a second archetype needs different ones.

## As built

**Six deviations. One changes a signature, one changes a decision about how the store is walked, and three are the spec's own Tests table asking for rows nothing can currently write.**

**1. `Tick` takes the player's position as well as the player** — `Tick(float now, Vector3 playerPosition, PlayerCombat player)`, not the spec's `Tick(float now, PlayerCombat player)`. **The spec's signature cannot answer rule 6.** Landing a shot means testing the player's XZ distance against the radius, and `PlayerCombat` does not hold a position: it is *handed* `snapshot.PlayerPosition` on every call that needs one (`Tick`, `BuildCandidates`, `UpdateFaceDirection`) precisely because it owns no view of where anything is. The two ways out were adding a `PlayerPosition` field to `PlayerCombat` — a second copy of a number `RunState` already holds, and a new way for the two to disagree — or passing it in, which is the pattern `ResolveConeHits` and `FocusAt` already use. Passed in. `RunSession.Tick` calls `State.Projectiles.Tick(State.Time, State.PlayerPosition, State.Combat)`.

**2. `Land` is given this tick's `now`, not the shot's own arrival time.** Rule 6 says `player.ApplyDamage(damage, now)` and that is what it does, but it is worth writing down why the tempting alternative is wrong: the arrival can be up to a frame earlier than `now`, `Health` believes the last clock it was told, and `PlayerCombat.Tick` has already run at `now` this frame — so handing it the earlier one would start the i-frames in the past *and* walk `Health`'s clock backwards.

**3. Rule 8's "walked backwards on removal" is spelled as a two-phase tick over two arrays.** A backwards walk cannot also keep rule 6's oldest-first order — it lands the newest first — so the two halves of the spec contradict each other. What is built keeps the *stated reason* instead, which is the durable half: `Tick` does all of its bookkeeping before any callout (due shots move to a second preallocated array, survivors compact to the front in fire order) and only then resolves them, so a landing cannot disturb a shot that has not been examined yet, and a `Fire` from inside an impact handler appends to a store that is already consistent. The cost is one more array of `Capacity`. Arrival is **not** fire order — a shot fired later can be aimed closer and land first — which is why the front of the array cannot simply be assumed to be next, and `Tick_LandsOldestFirstAcrossRemovals` is written to fail if it ever is.

**4. `RunSession`'s constructor gained `projectileCapacity`, and the ripple is 8 test files rather than the "one call site" the Files table predicted.** The table said *"every fixture that constructs a `RunState`… it is `internal` and built only by `RunSession`, so this is one call site"* — true of `RunState` and beside the point, because rule 7 puts the capacity in `BootInstaller` and the only route from there to core is through `RunSession`. 28 call sites across 7 fixtures took a third `int`, wired by name in `RunInstaller` like the other two. `BootInstaller.ProjectileCapacity = 32`: one shot in the air per Spitter at a cap of 28, plus headroom, and **deliberately not an expression over `DeviceEnemyCap`** — a shot outlives its shooter, so the bolts of a wave that has just been wiped are still flying and the two counts are not the same question.

**5. Three `Session_*` rows are weaker than the Tests table asked for, and it is the spec's own design that makes them so.** Nothing fires a shot until M2-07b, and `RunState.Projectiles` is `internal` with no `InternalsVisibleTo` anywhere (AR §18.2) — so **no test can put a shot into a live run**, which is exactly what all three rows assume. What is asserted instead: `Session_ExposesCountAndNotTheSystem` reads `State.InFlightProjectiles` and proves by reflection that no public member of `RunState` is a `ProjectileSystem`; `Session_EndClearsThem` proves a second run does not inherit the first one's sky; and `Session_TicksProjectilesAfterBehavioursBeforeDeathCheck` proves the half that is reachable — a killing arrival leaves `player.IsDead` true and `PlayerDied` published **before `Tick` returns**, which is what makes a death check placed after the step end the run on the same tick — then ticks a live session to show the step is wired. **The end-to-end row is handed to M2-07b**, which is the first task with something that can fire one; it is named in the fixture's remarks and in the PROGRESS entry rather than left as a silent gap.

**6. `Tick_DrawsNoRandom` is a type assertion, not a throwing fake.** `ProjectileSystem` takes no generator at all, so there is nowhere to inject one — and `FixedRandom` does not throw on a draw in any case, it returns a default. The row fires and lands ten shots, then asserts that neither a constructor parameter nor a field of the type is an `IRandom` or an `IRandomStream`. That fails the day somebody adds a spread, which is the thing ADR-0011 wants weighed rather than slipped in.

**Everything else is as specified.** Ledger row 7 is settled in `IRunSession`'s remarks, AR §5's ports row, AR §18.1's ordering table and AR §18.2 — `IRunSession` gains no member, and the stale `ReportContact` / `ReportProjectileHit` promises are deleted rather than deferred.

**Verified:** 682 EditMode green, 0 failed, 0 skipped, 8.5 s (through `TestRunnerApi`) — M2-06's 656 plus **exactly** 26. Zero compile errors, zero analyzer warnings, no `ProjectSettings/` drift, and the expected eight Console warnings and nothing else. PlayMode was not run and did not need to be: nothing fires a projectile, so the only new behaviour reachable from a scene is a `DebugOverlay` field that reads `0`. **M2-07b must run it.**
