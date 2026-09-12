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

**Built as specified in substance: cover blocks a Spitter's wind-up, the sense is a sense, and the frame order has a fixture.** `LineOfSightSense` is `NavPathSense`'s shape down to the slot table and the emergent round-robin, throttled by the `PathRefreshBudget` M2-05 already built, and `SnapshotBuilder` fills `EnemySense.HasLineOfSight` from it. **Ledger rows 13 and 8 both leave the table here.** Twelve deviations, three of which change a decision.

**Verified:** 870 EditMode green (844 + 26), 11 PlayMode green (7 + 4), 0 failed, 0 skipped, zero compile errors, zero analyzer warnings, `git diff ProjectSettings/` empty, `Run.unity` diff three lines.

### The three that change a decision

1. **The stage boundary does not call `Clear` — `SnapshotBuilder` notices the arena changed and calls it** (rule 8). The builder already holds both the `ArenaPool` and the sense, so a one-field comparison of `_arenas.Active` against last frame's replaces a `StageArrived` subscription, a new dependency and a new disposal. It is also the *more* precise trigger: rule 8's own argument is that this is about geometry rather than identity, and an arena swap is exactly when the geometry changes — a mode with one room never clears, which is correct, and a run that swapped rooms without a `StageArrived` would still clear.

2. **`EnemyViews` losing the hard `false` forced `SnapshotBuilder` to write the field on *every* path, including the one with no sense composed** — and to write `true` there. AR §18.2 says anything writing a reused slot must assign every field, and with the census out of the picture the builder's second pass is the only assignment a slot gets; a null sense that left the field alone would hand core an answer about an enemy that despawned two frames ago. `WritePathDirections` therefore became `WriteSenses` and lost its early return. **The value is `true`, which is rule 3 applied one level up**: a fixture or a mis-wired scope gets the arena M2-07b shipped, not one where no Spitter ever fires. `SnapshotBuilderTests.Build_NoPool_SaysSo` gained a row for it.

3. **An empty cover mask throws, in the sense's constructor and again in `RunScope`.** Not in the spec, and it is `ConeOverlapQuery`'s precedent read across: an empty mask is not a degraded run, it is the game M2-11a shipped, where a pillar is scenery and a Spitter shoots through it — and the failure is completely silent. Two messages because they can say different things; the scope's names the field to fix.

### The other nine

4. **`Run.unity` was edited**, which the Files table does not list: `RunScope` gained a serialized `_coverLayer` and the scene had to carry a value for it, or the guard above would stop the run composing. Set through `SerializedObject` and saved from the Editor rather than hand-edited in YAML, because the Editor holds its own copy of an open scene and the next save would have overwritten a file-only edit (Traps §5). **The diff is three lines** — `m_Bits: 512`, layer 9 — with none of the re-serialisation collateral M1-12 warns about, and `git diff ProjectSettings/` is empty.
5. **`LineOfSightSense.DefaultRefreshHz`** exists, where the spec's signature wrote `= 10f`. Same value; VContainer resolves every constructor parameter or throws and never falls back to a C# default, so `RunScope` has to pass the number and should be passing the one the default says. Deliberately *not* a reference to `NavPathSense.DefaultRefreshHz`: the two caches answer different questions and are free to diverge, and a shared constant would move both in silence.
6. **`BlockedCount` joined the public API.** The spec asked `DebugOverlay` to show a blocked count and gave the class no member that could answer. It is the number that says the feature is *alive* rather than merely throttled — a raycast count proves the budget works, and only this proves the mask, the eye height and the geometry agree. The overlay shows them together as `los n/m`, in `bolts rented/pooled`'s shape; spelled `los` rather than `blocked` because the target line already owns that word for an out-of-range focus.
7. **The spec's "every existing `SpitterBehaviourTests` row sets `HasLineOfSight = true` explicitly" is done once, in `Place`**, the helper every row in the file goes through. `Place`'s own doc already said "all four fields together, never one of them" for exactly this reason — a fixture that half-describes a world produces rows that are about the fixture. It is now five. `ChaserBehaviourTests` needed no ripple at all: it never sets the field, which is what `Chaser_UnaffectedByCover` now asserts on purpose.
8. **`FrameOrderTests` has no recording *hub*.** The spec asked for "a recording sink and a recording hub"; `RunTicker` touches neither — the sink it writes through is the concrete `IntentBuffer`, whose write members are explicit interface implementations, and it never touches `DomainEventHub`. What records is one object implementing `IRunSession` *and* `IPlayerCommands`, writing its intents through `IIntentSink` at the two moments core writes them — including the shove, which goes out from inside `ReportConeHits` because that is where `PlayerCombat` writes one and is the entire reason `ApplyKnockbacks` is the last line of the frame.
9. **The order is pinned by five consequence assertions rather than one recorded sequence**, because only two of the seven steps call anything a fake can see. Each adjacency became something a real frame either produces or does not: core is told this frame's positions (not last frame's); the flag it raised last frame is down when it is ticked; the live body has already moved when it is asked for a fact, which the real cone sweep confirms through physics against a wedge placed where this frame's move puts the body; and the shove written inside the cone answer still reaches the body. `tick` before `facts` is the one directly recorded pair.
10. **The commands step is not asserted, and is named in the fixture rather than skipped quietly.** `CommandPhase` reaches core only when the Input System reports a press, and `Soulvail.Tests.PlayMode` does not reference it — adding the reference would put un-isolated device state into the assembly that also holds the boot smoke tests, since `InputTestFixture` is a different package again. It is also the one step of the seven whose misplacement is a latency bug rather than a wrong outcome: a tap acted on one frame late. **Row 8 is closed on the six that carry rule 11's failures**, and this gap is recorded in AR §18.1's row.
11. **The PlayMode fixture builds its arena a kilometre from the origin.** `BootSmokeTests` sorts before it in the same assembly and leaves the Run scene loaded with a live run in it, so the origin has eight dummies standing on the Enemy layer — and this is the first PlayMode fixture that sweeps physics, so it is the first one that would have been answered about somebody else's arena.
12. **Three test names moved** and one row merged: `Snapshot_CarriesIt` → `Snapshot_CarriesLineOfSight`, `Ticker_RunsTheSixStepsInOrder` → `Ticker_RunsTheStepsInOrder` (there are six steps asserted, not the seven the name would claim), and `Ticker_AppliesKnockbacksAfterTheFacts` is the last third of the order row rather than a row of its own, because the shove cannot be observed on the same frames as the walk: `EnemyView.Apply` returns early while a body is sliding. `DebugOverlay`'s `StringBuilder` capacity went 160 → 224 with the new field.

### Not deviations, but worth knowing

- **`EnemyBlackboard.HasLineOfSight` and `EnemySystem.Ingest` needed no change at all.** Both have carried the field since M0-05 and M1-06; the only thing missing was somebody to answer it. The ripple was two lines of production code outside the new file.
- **`Cadence_HoldsTheAnswerForATenth` failed first time round** and the fault was the test's: the refresh falls due a tenth of a second after the *answer was taken*, not after the run began, and the row's clock started one frame in. Named here because it is the shape of mistake every cadence row invites.
- **Two Console warnings appear on a forced reimport** — `ArenaView 'Arena_Pillars'/'Arena_Tiered': it authors 0 spawn point(s)`. Pre-existing and not caused by this task: `OnValidate` runs before the prefab's fields are deserialised (Traps §5), and `ArenaViewTests`' seventeen rows over both prefabs pass, which is the proof the assets are correctly authored.
