# M2-11b — `LineOfSightSense`: a pillar you can hide behind, and the frame order finally under test

**Size:** S · **Depends on:** M2-11a (something to hide behind), M2-07b (something to hide from) · **Branch:** `m2-11b-line-of-sight-sense`
**Design refs:** GD §7.2 (*cover blocks enemy projectiles but not pathing*), §8.1, §11.3; AR §3, §4.1, §4.3, §14, §18.1, §18.2, §18.4 · **Ledger rows:** **13** — answered and closed here; **8** — the frame-order half M2-09 left

## Goal

Standing behind a pillar stops a Spitter shooting you, by the only route that does not make core hold a copy of the arena — and `RunTicker`'s frame order, which nothing has ever asserted, is pinned by the test the new step makes worth writing.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/LineOfSightSense.cs` | Game | Per-enemy occlusion, refreshed round-robin inside a per-frame budget |
| `Tests/Game/Adapters/LineOfSightSenseTests.cs` | Tests.Game | The cadence, the budget, the census, the unknown answer |
| `Tests/PlayMode/FrameOrderTests.cs` | Tests.PlayMode | **Row 8**: `RunTicker`'s six steps, in order, against a real frame |
| *small edits* | | `SnapshotBuilder` fills `EnemySense.HasLineOfSight` from it; `EnemyViews` **loses** the hard `false` it writes today ([`EnemyViews.cs:181`](../../../Assets/_Project/Game/Views/EnemyViews.cs)); `SpitterBehaviour` holds fire without it (rule 6); `RunScope` + the `Cover` mask; `DebugOverlay` shows raycasts-per-frame and blocked-count; **AR §18.4** gains rules 4 and 5 as invariants |
| *ripple* | | `SnapshotBuilderTests` gains the sense; `SpitterBehaviourTests` gains the blocked rows and every existing row now sets `HasLineOfSight = true` explicitly |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Adapters;

/// Whether each enemy can see the player, as a standing question rather than an instant one.
/// `NavPathSense`'s shape and its budget: a cached answer per enemy, refreshed round-robin at a
/// fixed cadence inside a per-frame allowance that scales with the population.
public sealed class LineOfSightSense
{
    /// How high off the floor the ray runs, at both ends. Chest height on a 2 m capsule: low
    /// enough that a 1.5 m pillar blocks it, high enough that a floor tier does not.
    public const float EyeHeight = 1.1f;

    public LineOfSightSense(int capacity, LayerMask cover, PathRefreshBudget budget, float refreshHz = 10f);

    /// Raycasts owed this frame, after the budget clamped them. `DebugOverlay` reads it.
    public int RaycastsLastFrame { get; }

    /// Whether `enemyId` can see `to` from `from`, as of its last refresh. True when it has never
    /// been measured — rule 3.
    public bool HasLineOfSight(int enemyId, Vector3 from, Vector3 to, float time, int activeCount, float dt);

    /// Forgets every cached answer. Called when a stage boundary tears the arena down.
    public void Clear();
}
```

## Behaviour

**The sense**

1. **It is a sense, not a fact, and that is ledger row 13's answer.** M2-07a rule 1 set the rule: core decides every outcome, Unity owes a *fact* only when the answer depends on colliders core cannot see, and **a *sense* when the geometric question is a standing one rather than an instant**. "Is there a pillar between this Spitter and the player" is standing — it is true for as long as neither moves — so it belongs on the snapshot beside `PathDirectionToPlayer`, which is the same shape of question about the same geometry. `EnemySense.HasLineOfSight` has existed unfilled since M0-05 and CC §3.1's *"Line-of-sight check: skip for V1, revisit when cover arrives"* is the note that has been waiting for this task.
   **The two rejected alternatives are M2-07a's, not re-argued:** a projectile view raycasting and reporting a block is the fact route rule 1 declined, and core holding a wall list is a second world model.
2. **The budget is `PathRefreshBudget`, the one M2-05 already built** — `ceil(activeCount · refreshHz · dt)`, floored at 1, capped at its maximum. Not a second budget class with the same arithmetic: at 28 enemies and 10 Hz this is **5 raycasts a frame at 60 fps and 10 at 30**, which is the number ledger row 13 was priced at. One refresh per enemy per 0.1 s, round-robin, the same cadence `NavPathSense` runs and for the same reason — a 100 ms stale answer about a pillar is imperceptible, and 28 raycasts every frame is not free on a phone.
3. **An enemy that has never been measured can see.** The permissive default, and it is `NavPathSense`'s: no path yet means the straight line, not paralysis. The failure mode is what decides it — a mis-wired sense that answers *false* means no Spitter ever fires and the archetype silently does nothing, while one that answers *true* degrades to exactly today's behaviour, which is visible, diagnosable and already shipped. A body spawned this frame therefore holds its fire for no frames at all, and `EnemyBlackboard.Reset`'s `false` survives only until the first `Ingest` overwrites it, which is the same tick.
4. **The ray is 3D, and it is the one perception in this project that is.** AR §18.4 says all perception is XZ and *"any new sense that measures a separation owes the same treatment"* — this one measures **occlusion**, not separation, and a pillar is a solid with a height. Every *distance* a Spitter uses stays XZ; only the question "is there something in between" is asked in three dimensions, at `EyeHeight` on both ends so that neither a floor tier nor a camera-facing lip decides a fight. This goes into §18.4 as a named exception rather than being left for the next reader to rediscover.
5. **The mask is `Cover` and nothing else.** Not the ground, not the arena walls, and above all **not enemies** — a Spitter that could not fire because another Husk was standing in front of it would look broken, and GD §7.2 makes cover an arena property, not a crowd property. M2-11a's layer is what makes a single mask sufficient.

**What uses it**

6. **A Spitter that cannot see the player does not enter `Aim`, and one already aiming still fires.** The first half is GD §7.2 — cover blocks the shot, so the honest place to block it is before the telegraph, not by deleting a bolt in flight. The second half is M2-07b rule 7, kept verbatim: *"`Aim` never cancels"*, because an aim that could be broken by the player stepping behind something would mean the archetype never fires at a moving target. So the pillar is a shield you have to be behind **before** the wind-up starts, which is what makes it a positioning decision instead of a reflex.
7. **It changes no other archetype.** The Chaser walks around cover already and contact damage does not care what is in between; the Bloater's blast is a radius, and M2-08 rules it damages the player only. Cover blocks projectiles, and GD §7.2 says it blocks nothing else — *"but not pathing"* is in the same sentence.
8. `Clear` forgets every cached answer, and the stage boundary calls it: an arena has been swapped underneath the ids, so an answer about the pillars of the room you just left is worse than no answer. Ids are never reused within a run (`EnemyRegistry`'s rule), so this is about geometry, not identity.
9. Allocates nothing: a preallocated bool-and-timestamp array sized to capacity, one reused `RaycastHit`, `Physics.Raycast`'s non-allocating overload.

**Row 8 — the frame order, finally asserted**

10. **One PlayMode test walks a real frame and pins `RunTicker`'s order:** commands → snapshot → clear intents → core tick → bodies → facts → knockbacks (AR §18.1). It asserts the order by observation rather than by reading the method — a recording sink and a recording hub, ordered by the sequence they were touched in — so a reordering fails it whatever the code looks like.
11. **The rows that matter are the ones whose failure does not look like its cause**, which is why this is worth a file: an intent buffer cleared after core wrote to it reads *empty every frame* and looks like the motor being broken; a fact reported before the tick resolves against last frame's arena and looks like the cone missing; a snapshot built after the tick makes the gun aim at where enemies were and looks like the auto-aim being stupid. M1-16 already learned one of these the hard way and it became an invariant with no test under it.
12. **This closes ledger row 8.** M2-09 covered the pooled-view half — *"a pooled body forgets its last life"* — and named this half as M2-11's; the two together are the whole row, and it leaves the table when this task's *As built* says so. `LineOfSightSense` is what makes now the moment: it adds a step to `SnapshotBuilder`, and a new step in an order nothing asserts is exactly how an order stops being true.

## Tests

| Test | Given / When / Then |
|---|---|
| `Unknown_CanSee` | a fresh sense / `HasLineOfSight(4, …)` before any refresh / true (rule 3) |
| `Blocked_WhenCoverIsBetween` | a collider on `Cover` between the two points / refresh / false |
| `Clear_WhenNothingIsBetween` | open ground / refresh / true |
| `IgnoresNonCoverColliders` | an enemy capsule and the floor between them, neither on `Cover` / refresh / true (rule 5) |
| `RayRunsAtEyeHeight` | a 0.4 m kerb on `Cover` between them / refresh / true — it does not block; a 1.5 m pillar / false (rule 4) |
| `DistanceIsNotXzClamped` | target 4 m above, cover between / refresh / blocked — occlusion is 3D even though distance is not (rule 4) |
| `Cadence_HoldsTheAnswerForATenth` | blocked, then the cover is moved away / query at +0.05 s / still false; at +0.11 s / true (rule 2) |
| `Budget_LimitsRaycastsPerFrame` | 28 enemies, 10 Hz, dt 1/60 / one frame / `RaycastsLastFrame` 5 (rule 2) |
| `Budget_AtThirtyFps` | as above, dt 1/30 / — / 10 |
| `Budget_RoundRobinCoversEveryone` | 28 enemies / 6 frames at 60 fps / every id has been refreshed at least once |
| `Budget_ClampAtLeastOne` | 1 enemy, dt 1/240 / — / 1 |
| `Clear_ForgetsEveryAnswer` | several blocked / `Clear` / all read true again (rule 8) |
| `Query_AllocatesNothing` | 28 enemies, warm-up / 10 000 frames of querying / allocated-bytes delta == 0 |
| `Snapshot_CarriesIt` | a blocked enemy / build a snapshot / that slot's `EnemySense.HasLineOfSight` is false, and `EnemyViews` no longer overwrites it |
| **The Spitter** | |
| `Spitter_HoldsFireWithoutSight` | in `Approach` inside the band, no sight / `Tick` / stays `Approach`, no `EnemyTelegraph` (rule 6) |
| `Spitter_AimsWhenSightReturns` | as above, sight returns / `Tick` / `Aim`, one telegraph |
| `Spitter_AimingIgnoresLostSight` | in `Aim`, sight lost mid-windup / `Tick` to the end / it still reaches `Release` and fires (rule 6, M2-07b rule 7) |
| `Chaser_UnaffectedByCover` | a Husk with no sight, in reach / `Tick` / it strikes (rule 7) |
| **PlayMode — row 8** | |
| `Ticker_RunsTheSixStepsInOrder` | a run, one frame / — / commands, snapshot, intent-clear, core tick, bodies, facts, knockbacks, in that sequence (rules 10, 11) |
| `Ticker_ClearsIntentsBeforeCoreWrites` | a frame with a move intent / — / the intent read by `PlayerView` is this frame's, never empty (M1-16's invariant) |
| `Ticker_ReportsFactsAfterBodiesMoved` | a swing landing on a body that moved this frame / — / the cone resolves against the moved position |
| `Ticker_SnapshotPrecedesTheTick` | an enemy that moved this frame / — / targeting used the new position, not the previous frame's |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Start a run at a depth with Spitters. Stand in the open: one plants at 14 m and throws. Step behind a pillar **before** it starts its wind-up: it plants, does not telegraph, does not fire, and waits. That is GD §7.2 working, and rule 6's two halves in one observation.
2. **[Editor]** Step behind the pillar *during* the wind-up: it fires anyway, and the bolt hits the pillar's side of nothing and lands where you were. Not a bug — rule 6, and M2-07b rule 7's *"`Aim` never cancels"* is the reason.
3. **[Editor]** `DebugOverlay` reads about 5 raycasts a frame with a full arena and roughly 10 when the editor is running at 30. If it reads 28, the budget is not being applied.
4. **[Editor]** Put a Husk between a Spitter and yourself. The Spitter still fires (rule 5) — bodies are not cover.
5. **[device]** Whether a pillar reads as *cover* at a phone's size — whether a player can tell, without being told, that standing there is why they stopped being shot. Deferred with the rest of the device list.

## Out of scope

- **Line of sight in the player's targeting.** CC §3.1 lists it as an optional filter and answers it *"Skip for V1"*; the auto-aim shooting a Spitter through a pillar is a different question from a Spitter shooting *you* through one, and the Censer's 8 m cone rarely has a pillar in it. It becomes M8's if a playtest complains.
- **Cover blocking the Bloater's blast.** GD §8.1 makes the blast a radius and M2-08 makes the corpse own it; a shadow-cast blast is a second geometry system for one archetype.
- **Cover for enemies against the player.** GD §7.2 gives cover one job.
- **Destructible or moving cover.** The cached answer's 0.1 s staleness is only safe because pillars do not move; anything that changes that owes rule 2 a re-read.
- **The arena, the pool and the `Cover` layer** — M2-11a, which is where the pillars stopped being scenery.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._
