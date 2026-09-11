# M2-07b — `IEnemyBehaviour`, and the Spitter that keeps its distance

**Size:** S · **Depends on:** M2-07a · **Branch:** `m2-07b-spitter-ai`
**Design refs:** GD §8.1 (the Spitter), §9.1 rule 1 (everything is telegraphed), §12.4; CC §3.4, §4.2; AR §3, §9, §14, §18.1, §18.4 · **Ledger rows:** 7 (applied; settled in M2-07a)

## Goal

The arena has a second kind of mind in it: an enemy that stops at fourteen metres, stares, and throws — and the seam that let a second behaviour exist at all, which `EnemyAgent` has been deferring to this task since M1-05.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/IEnemyBehaviour.cs` | Core | The seam and `EnemyTickContext`, grouped |
| `Core/Ai/SpitterBehaviour.cs` | Core | `SpitterState` + the behaviour |
| `Tests/Core/Ai/SpitterBehaviourTests.cs` | Tests.Core | Every rule below |
| *small edits* | | `EnemyAgent.Behaviour` retyped to `IEnemyBehaviour` and built by kind (rule 4); `ChaserBehaviour` implements it and its `Tick` takes the context; `EnemySystem.Tick` takes the context and dispatches `Spitter`; `RunSession.Tick` builds the context; `Data/Enemies/Spitter.asset` + `_behaviour: Spitter` (M2-06 rule 11) |
| *ripple* | | `ChaserBehaviourTests` and `EnemySystemTests` build a context instead of passing four arguments — mechanical, and the compiler enumerates them |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Ai;

/// Everything a behaviour is allowed to reach this tick. One instance is built per tick by
/// `RunSession` and passed by `in` to every living agent — see rule 2.
public readonly struct EnemyTickContext
{
    public EnemyTickContext(float dt, float now, PlayerCombat player, IIntentSink intents,
                            IDomainEvents events, ProjectileSystem projectiles);

    public float Dt { get; }
    public float Now { get; }                    // simulated run seconds, never a wall clock
    public PlayerCombat Player { get; }
    public IIntentSink Intents { get; }
    public IDomainEvents Events { get; }
    public ProjectileSystem Projectiles { get; }
}

/// What drives one enemy. Implemented once per `EnemyBehaviourKind` that does anything.
public interface IEnemyBehaviour
{
    /// Decide, then say so — through an intent, an event, or damage. Exactly one
    /// `EnemyMoveIntent` per call, in every state (rule 9).
    void Tick(in EnemyTickContext ctx);

    /// Back to a freshly spawned enemy. What a recycled agent gets instead of a new behaviour.
    void Reset();
}

public enum SpitterState
{
    Idle,       // spawned and unaware
    Approach,   // walking towards the standoff band, or backing out of it
    Aim,        // planted, facing, telegraphing — never cancelled (rule 7)
    Release,    // the shot leaves. One tick.
    Recover,    // rooted, reloading, punishable
}

public sealed class SpitterBehaviour : IEnemyBehaviour
{
    /// How far inside the standoff range the player must get before it backs away, as a fraction
    /// of `ProjectileSpec.StandoffRange`. See rule 6 for why it is 0.7 and not 1.
    public const float RetreatFraction = 0.7f;

    public SpitterBehaviour(EnemyAgent agent);

    public SpitterState State { get; }
}

// EnemyAgent (changed)
public IEnemyBehaviour Behaviour { get; private set; }
```

## Behaviour

**The seam**

1. **`IEnemyBehaviour` exists now because there are three implementers landing in three consecutive tasks**, which is the condition `EnemyAgent`'s own remarks set: *"an `IEnemyBehaviour` with a single implementer would be an abstraction invented for a second one nobody has written yet — M2-07's Spitter and M2-08's Bloater are where the shape of the seam becomes knowable."* It is knowable, and this is its shape: two methods, no state, no properties. `State` stays off it because each behaviour's states are its own enum and nothing outside a behaviour interprets them.
2. **One context struct rather than six parameters.** `ChaserBehaviour.Tick` already takes five, this task would make it six and M2-08 makes it seven; a `readonly struct` passed by `in` means widening it is one line in one file instead of an edit to every implementer and every test that calls one. It allocates nothing, it is built **once per tick** in `RunSession` rather than once per agent, and it holds no state — a behaviour that stored it would be storing this tick's clock, which is why `ChaserBehaviour` keeps unpacking it into fields and clearing them in a `finally` exactly as it does today.
3. `EnemySystem.Tick` takes the context instead of `(dt, now, player, intents)` and reads `Dt`, `Now` and `Player` off it for the corpse sweep and the respawn. Its `switch` gains `case EnemyBehaviourKind.Spitter` and remains the one site that knows the full set of kinds — the unhandled-kind throw stays exactly where M1-05 put it.
4. **A recycled agent whose archetype changed gets a new behaviour, and that costs an allocation on the spawn path.** `Initialise` keeps the behaviour it holds when the kind still matches and builds a fresh one when it does not, so a wave of one archetype recycles for free and a mixed arena churns one small object per changed rental. That is a *spawn*-path cost, not a frame-path one, and AR §14's ban is about the latter — but it is a cost, so a test pins the free case rather than leaving it to be assumed. Rejected: a field per kind on the agent, which is a list that gets one entry too short the first time somebody adds an archetype and forgets.

**The Spitter**

5. `Idle` until the player is within `Spec.AggroRange` (M2-06 rule 4), then `Approach`. Same as the Chaser, and for the same reason: at 30 m it is a spawner's concern rather than a stealth mechanic.
6. **`Approach` is a band, not a point.** Beyond `StandoffRange` it walks in; inside `StandoffRange × RetreatFraction` it walks *out*; between the two it plants and aims. The gap is what stops it oscillating at exactly fourteen metres, the same argument `WindupCancelReachMultiplier` makes for the Chaser's cancel — and 0.7 is chosen against the Censer rather than picked: **14 × 0.7 is 9.8 m, just outside the Oathbound's 8 m weapon range and just inside its 12 m acquire range**, so a Spitter the player has targeted still has to be *chased*, and one at full standoff cannot be auto-acquired at all. That is the pressure the archetype is for.
7. **`Aim` never cancels**, and that is the deliberate opposite of the Chaser's windup. A Spitter that abandoned its aim whenever the player moved would never fire, because moving is what the player does — the dodge window is the *flight*, not the telegraph (M2-07a rule 3). It publishes `EnemyTelegraph(id, Spec.WindupTime)` on entering, which is GD §9.1 rule 1 and which the existing `EnemyHitFeedback` swell already draws, and it stands and faces for `WindupTime` whatever the player does, including walking into melee range or out of aggro.
8. **`Release` fires at where the player is standing at that instant** and lasts one tick, `ChaserState.Strike`'s shape and for its reason: the moment of the shot is nameable by anything watching. The shot's damage is **`agent.ContactDamage.Value`**, the stat — so M2-03's `d(n)` is already in it and M7-02's affixes will be (M2-06 rule 5) — and its speed and radius come from `Spec.Projectile`. A `Fire` that returns `NoProjectile` still moves to `Recover`: the Spitter believes it fired, which is the only reading that does not need it to know the projectile system's capacity (M2-07a rule 7).
9. `Recover` roots it for `Spec.RecoverTime` and returns to `Approach` — never to `Idle`, `ChaserBehaviour.TickRecover`'s rule: an enemy that has shot at you knows where you are.
10. **Exactly one `EnemyMoveIntent` per tick in every state**, standing ones included. `EnemyView` folds gravity into the same `CharacterController.Move` that carries the walk, so a tick with no intent is a tick this body is not pinned to the floor by — `ChaserBehaviour`'s rule, kept verbatim because it is the body's rule rather than the Chaser's.
11. **Walking in follows the NavMesh path; walking out does not.** `PathDirectionToPlayer` when it is non-zero, the straight line otherwise — but a retreat uses `−DirectionToPlayer` and nothing else, because negating a path direction points *away from the next waypoint*, not away from the player, and would walk a Spitter into the pillar it was just routed around. The cost is that a retreating Spitter can back into geometry; the `CharacterController` slides it along, and GD §7.2 guarantees no dead ends. Named rather than discovered.
12. Speed is `agent.MoveSpeed.Value` (M2-03 rule 7), never `Spec.MoveSpeed`, so a depth-scaled Spitter kites at the scaled speed.
13. Allocates nothing: one `StateMachine<SpitterState>` built with the behaviour, delegates built once, struct arithmetic over fields.
14. `Reset()` returns it to `Idle` with a blank `StateTimer`, and is what a recycled agent gets.

## Tests

| Test | Given / When / Then |
|---|---|
| `Idle_UntilInsideAggroRange` | aggro 30, player at 31 m / `Tick` / `Idle`, one intent with zero velocity; player at 29 m / `Tick` / `Approach` |
| `Approach_WalksInFromBeyondStandoff` | 20 m, standoff 14 / `Tick` / an intent towards the player at the scaled move speed |
| `Approach_PlantsInsideTheBand` | 12 m (between 9.8 and 14) / `Tick` / `Aim` |
| `Approach_BacksOutBelowRetreatBand` | 6 m / `Tick` / an intent *away* from the player, still `Approach` (rule 6) |
| `Approach_DoesNotOscillateAtTheEdge` | exactly 14 m, then 13.9, then 14.1 / three `Tick`s / `Aim` throughout, never a retreat |
| `Approach_PrefersThePathDirection` | a path direction 90° off the straight line / `Tick` / the intent follows the path (rule 11) |
| `Retreat_IgnoresThePathDirection` | inside the retreat band with a path direction set / `Tick` / the intent is exactly `−DirectionToPlayer` (rule 11) |
| `Aim_PublishesTelegraphOnce` | entering `Aim`, windup 0.7 / `Tick` ×5 within the window / one `EnemyTelegraph(id, 0.7)` |
| `Aim_DoesNotMove` | in `Aim` / `Tick` / an intent with zero velocity and a live facing |
| `Aim_NeverCancels` | in `Aim`, player walks to 40 m / `Tick` until the windup elapses / it still reaches `Release` (rule 7) |
| `Aim_NeverCancelsWhenThePlayerClosesToMelee` | player walks to 1 m during the aim / — / still `Release` |
| `Release_FiresAtWhereThePlayerIsNow` | player moved 5 m during the aim / windup elapses / one `ProjectileFired` whose `Target` is this tick's player position (rule 8) |
| `Release_UsesTheDamageStat` | stage-20 Spitter, `ContactDamage` scaled / release, land the shot / the player loses the **scaled** damage (rule 8) |
| `Release_UsesSpecSpeedAndRadius` | standoff 14, speed 12 / release / `FlightTime` 1.1667 ± 0.001 |
| `Release_LastsOneTick` | in `Release` / `Tick` / `Recover` |
| `Release_RefusedShot_StillRecovers` | a full projectile system / release / no event, `Recover` (rule 8) |
| `Recover_RootedThenApproaches` | recover 0.9 / `Tick` at +0.5 / `Recover`, zero velocity; at +0.9 / `Approach` (rule 9) |
| `Recover_NeverReturnsToIdle` | player at 40 m through the whole recovery / — / `Approach` |
| `Tick_EmitsExactlyOneIntentPerState` | each of the five states / one `Tick` each / exactly one `EnemyMoveIntent` per tick (rule 10) |
| `Tick_WalksAtScaledSpeed` | stage-40 Spitter / `Tick` in `Approach` / the intent's speed is `MoveSpeed.Value` (rule 12) |
| `Tick_AllocatesNothing` | a Spitter through a full fire cycle, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 |
| `Reset_ReturnsToIdle` | mid-`Aim` / `Reset` / `Idle`, `StateTimer` 0 |
| `Agent_BuildsASpitterForASpitterSpec` | a Spitter spec / `Initialise` / `Behaviour is SpitterBehaviour`, reached through `IEnemyBehaviour` rather than through a typed field (rule 1) |
| `Context_IsBuiltOncePerTickAndAllocatesNothing` | 28 agents, warm-up / 10 000 × `EnemySystem.Tick` / allocated-bytes delta == 0, and every behaviour saw the same `Now` (rule 2) |
| `Agent_KeepsItsBehaviourOnSameKindRecycle` | a Spitter despawned and recycled as a Spitter / `Initialise` / the same instance, reset (rule 4) |
| `Agent_ReplacesItOnKindChange` | a Chaser recycled as a Spitter / `Initialise` / `Behaviour is SpitterBehaviour`, in `Idle` |
| `System_DispatchesSpitters` | one Husk and one Spitter registered / `EnemySystem.Tick` / both behaviours ticked, one intent each (rule 3) |
| `System_StillThrowsOnAnUnhandledKind` | an agent whose kind no case covers / `Tick` / throws, naming the kind and the archetype (M1-05's rule) |
| `Spitter_AssetIsWiredUp` | `Data/Enemies/Spitter.asset` / load / `Behaviour == Spitter`, projectile block present (M2-06 rule 11) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Start a run at stage 2. A Spitter walks in, stops well short of you, swells, and throws — and `DebugOverlay`'s in-flight count goes to 1 and back to 0. **Nothing is drawn between the two**: the bolt is invisible until M2-09, so what is being checked here is the *rhythm* and the damage arriving about a second after the swell.
2. **[Editor]** Stand still through a shot and take the damage; walk two paces through the next one and take none. That is the archetype working (GD §8.1) and it is the one behaviour that cannot be proved anywhere but in play, because it depends on the flight time feeling long enough to react to.
3. **[Editor]** Walk at a Spitter. It backs off and keeps backing off, and it stays just out of the Censer's reach until you commit — rule 6's arithmetic on screen.
4. **[device]** Whether a 0.7 s swell at fourteen metres reads as a warning on a phone screen, and whether a bolt arriving from off-screen is unfair rather than difficult (ledger row 14). Deferred with the rest of the device list.

## Out of scope

- **Drawing the bolt** — M2-09. Manual step 1 is deliberately written to work without it.
- **Cover, and off-screen damage** — ledger rows 13 and 14, owners M2-11 and M2-12. Both are named in M2-07a's Out of scope with the reasoning.
- **`EnemyRegistry` preferring a free agent whose behaviour already matches the requested kind**, which would make rule 4's churn rare in a mixed arena. An optimisation with no measurement behind it; parking lot, promoted by a profile rather than by a hunch.
- **Leading the shot** (CC §3.7). That is the *player's* projectile problem and a different decision in a different place — M5-01. An enemy that led its target would remove the dodge the archetype exists to demand.
- **A second attack, or a melee panic move** when the player is inside the retreat band. GD §8.1 gives the Spitter one trick; a cornered-Spitter move is content, not plumbing.

## As built

**Nine deviations. One changes a decision about what a recycled agent keeps; two are rows that cannot live where the Tests table put them; the rest are the spec declining to say how.**

**1. `Spitter_AssetIsWiredUp` is in `Soulvail.Tests.Game`, and `EnemyLookTests.cs` is a ripple file the table did not name.** Loading `Spitter.asset` needs the `AssetDatabase`, and `Soulvail.Tests.Core` has no engine reference — so the row could never have been written in the fixture the table lists. What flipped instead is M2-06's own row, `Assets_AuthoredStaticUntilTheirBehaviourExists`: its Spitter half now asserts `Spitter`, its Bloater half still asserts `Static`, and the assertion moved with the asset rather than being deleted. `Spitter_MatchesDesign` gained the block's other two numbers — the 12 m/s and the 1.6 m — because M2-07b is the task that made them reachable: `EnterRelease` reads both off the archetype on every shot, and until now only the standoff was pinned.

**2. `SilentEvents` moved to `Tests/Core/Fakes/SilentEvents.cs`, which is a fourth new file.** M1-18 nested it privately inside `ChaserBehaviourTests` under a note saying it would move "if a second fixture needs it"; this is that fixture, and it needs it twice over — a Spitter publishes a telegraph *and* a `ProjectileFired` every cycle, and `RecordingEvents` boxes both. Following the note was cheaper than duplicating it, and both allocation rows would otherwise have measured the fake. It ships without a fixture of its own, unlike its neighbours in `Fakes/`: there is nothing to assert about a method whose body is empty. Four new code files is still S by the ROADMAP's count.

**3. The end-to-end ordering row is in `SpitterBehaviourTests`, not back in `ProjectileSystemTests`.** `Session_ShotThatKills_EndsTheRunWithNoIntent` starts a real run with one Spitter in the plan and the player planted at 12 m, and ticks it until the run ends: on the last tick, `PlayerDamaged` → `PlayerDied` → `ProjectileImpacted` → `RunEnded`, and `PlayerMoves` is empty because `Tick` returns before `TickBody`. It lives beside the thing that can fire rather than beside the thing that carries the shot, which is what M2-07a's deviation 5 was waiting for.

**4. `EnemyAgent.Initialise` asks rule 4's question with `as`, and a Chaser recycled as a Static now loses its behaviour.** The build is a `switch` expression — `Behaviour as ChaserBehaviour ?? new ChaserBehaviour(this)`, and the same for a Spitter, with every other kind mapping to `null`. That keeps the object when the kind matches and rebuilds when it does not, with no second field to fall out of step. **It also overturns a sentence M1-05 wrote:** its remarks promised that "a Chaser recycled as a Static keeps the object and comes back to it if it is recycled as a Chaser again". It no longer does, and the paragraph was rewritten rather than left to lie. The cost is one small object per *changed* rental on the spawn path, which is the churn rule 4 accepts and whose free half `Agent_KeepsItsBehaviourOnSameKindRecycle` pins.

**5. The dispatch gives `Chaser` and `Spitter` one line, not two.** Rule 3 says the switch "gains `case EnemyBehaviourKind.Spitter`"; what it gained is a second label above the same `agent.Behaviour.Tick(ctx)`. That is the seam earning its keep — everything that differs between a Husk and a Spitter is on the other side of the interface — and the unhandled-kind throw stays exactly where M1-05 put it.

**6. `EnemyTickContext` guards its two floats as well as its four references.** The Public API sketch shows a bare constructor; the implied guard rows ask for a non-finite row on every `float` door, and this is one. Guarded here because it is built once a tick for the whole arena rather than once per enemy — which is also the argument for leaving both `Tick` implementations unguarded, and it is now written down in **AR §18.2**.

**7. A retreating Spitter keeps facing the player.** Rules 6 and 11 fix the *velocity* — `−DirectionToPlayer`, never the path — and say nothing about the facing. It stares: that is the archetype, and it is the facing the next `Aim` starts from. `Approach_BacksOutBelowRetreatBand` asserts both halves, so the choice is pinned rather than incidental.

**8. `EnterRelease` reads the blackboard, not the agent.** Origin is `Blackboard.SelfPosition` and target is `Blackboard.PlayerPosition` — the same numbers `agent.Position` and the snapshot hold, reached through perception. AR §9's rule that a behaviour reads facts and never asks where they came from, and what lets every row here state its geometry through one helper.

**9. `Release_UsesSpecSpeedAndRadius` needs two Spitters.** One arrival is resolved against one player position, so proving that 1.7 m is a miss *and* that 1.5 m is a hit takes two shots — a second Spitter aims and releases after the first has landed. The flight time is asserted on the first: 14 m at 12 m/s is 1.1667 s.

**One note on manual step 1, which cannot be followed as written.** *"Start a run at stage 2"* has no route today: `RunTicker.Start` takes the depth from `ModeSpec.StartingStage` — deliberately, since GD §4.5 forbids hard-coding "starts at stage 1" — `Descent.asset` states 1, and **nothing advances a stage within a run until M2-10**. So a Spitter cannot appear in a playtest of the shipped assets, because `Descent`'s roster introduces it at stage 2. The way to run the step is to set `_startingStage` to 2 in the Inspector, play, and **put it back without committing it**; the alternative is to wait for M2-10 and check it then. Named here rather than left for the owner to discover at the keyboard.

**Everything else is as specified.** Ledger row 7 is applied rather than closed — it was struck at M2-07a, and M2-08 is the last archetype that has to obey it. Two durable rules were filed: **AR §18.2** (the context) and **AR §18.4** (an aim never cancels; a walk out never follows the path).

**Verified:** 714 EditMode green, 0 failed, 0 skipped, 8.8 s (through `TestRunnerApi`) — M2-07a's 682 plus **exactly** 32: 28 of the Tests table's 29 rows, its 29th in `Soulvail.Tests.Game`, and 3 implied guard rows. **PlayMode 3 green, 3.6 s** — the first run of that suite since M2-05, and the same three `BootSmokeTests` rows it held then; this task adds none. Zero compile errors, zero analyzer warnings, no `ProjectSettings/` drift, and the expected eight Console warnings and nothing else.
