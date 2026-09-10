# M2-11a — The arena as a contract: a pool of them, a barrier, and a door that only opens once

**Size:** M · **Depends on:** M2-10 · **Branch:** `m2-11a-arena-contract-and-pool`
**Design refs:** GD §7.2 (arena design rules), §7.1, §11.3, §12.4 (cover guarantee), §16.4; AR §3, §4.3, §14, §18.2 · **Ledger rows:** 13 — the half that makes cover a thing core can be told about; **M2-11b** fills the sense and the row leaves there

## Goal

A stage happens somewhere specific: one of several hand-built arenas, chosen by the seed, raised when the screen is already black, with pillars that are on a layer rather than being scenery, and a door that stays shut until the stage is won.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Arena/ArenaView.cs` | Game | The prefab contract: anchors, spawn points, cover, barrier, door — **block namespace** (Traps §5) |
| `Game/Arena/ArenaPool.cs` | Game | Which arena is standing, raising the next one, and placing the player in it |
| `Tests/Game/Arena/ArenaPoolTests.cs` | Tests.Game | Selection, the swap, the prepare window, reuse |
| *small edits* | | `ModeSpec` + `Arenas` and `ArenaFor` (rule 3), `ModeDefinition` + the roster, `Descent.asset` + two arena ids; `SnapshotBuilder` fills `GatePosition`/`HasGate` from `ArenaPool.Active` instead of `RunScope`'s marker; **`SpawnPlan` loses `SpawnPoints`** and `SpawnDirector.Begin` takes the stage's points instead (rule 6); `StageFlow` reads them off `ArenaPool` through the snapshot and hands them over on entering `Waves`; `RunScope` + the prefab list and an arena root, **minus** `_dummyPositions`' spawn-point duty; `RunInstaller` registers `ArenaPool`; `DebugOverlay` shows the arena id; **`ProjectSettings/TagManager.asset`** gains a `Cover` layer — **owner's approval, rule 9** |
| *assets* | | `Prefabs/Arenas/Arena_Pillars.prefab` + `NavMeshArena_Pillars.asset`, `Prefabs/Arenas/Arena_Tiered.prefab` + `NavMeshArena_Tiered.asset` — `GreyBox.prefab` becomes the first of them (rule 10). Listed, not counted |
| *ripple* | | `ModeSpecTests` gains the `ArenaFor` rows; every fixture building a `SpawnPlan` loses its positions argument |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Arena
{
    /// One hand-built arena (GD §7.2). It decides nothing and ticks nothing: it is a set of named
    /// places, a barrier, a door, and the NavMesh those places are true about.
    public sealed class ArenaView : MonoBehaviour   // block namespace — Traps §5
    {
        /// This arena's content id, e.g. `arena.pillars`. Matched against the mode's roster.
        public ContentId Id { get; }

        /// Where the player stands when this arena is raised, in world metres.
        public Vector3 PlayerStart { get; }

        /// Where the door out is. What core tests the player's distance against (M2-10 rule 8).
        public Vector3 GatePosition { get; }

        /// Where the director may put a body. At least three, none within `MinPlayerDistance` of
        /// `PlayerStart` (rule 2).
        public IReadOnlyList<Vector3> SpawnPoints { get; }

        /// Barrier up, door shut. Called on `StageArrived`.
        public void Seal();

        /// Barrier down, door open. Called on `StageCleared`.
        public void Open();
    }
}
```

```csharp
namespace Soulvail.Game.Arena;

/// The arenas, on the Unity side: one standing at a time, the rest either not yet built or
/// parked inactive. `EnemyViews`' shape at the scale of a whole room.
public sealed class ArenaPool : IDisposable
{
    public ArenaPool(
        IObjectResolver resolver,
        IReadOnlyList<ArenaView> prefabs,
        Transform parent,
        DomainEventHub hub,
        PlayerView player);

    /// The arena currently standing, or null before the first `StageArrived`.
    public ArenaView Active { get; }

    /// How many arena bodies exist, standing or parked. Never more than the roster (rule 8).
    public int Count { get; }

    public void Dispose();
}
```

```csharp
namespace Soulvail.Core.Content;

// ModeSpec (added)
/// The arenas this mode draws from — GD §7.2's pool of 8–12 per biome. May be empty, which
/// makes `ArenaFor` return `default` and leaves the run in whatever the scene was dressed with.
public IReadOnlyList<ContentId> Arenas { get; }

/// Which arena stage `stage` uses. A pure function of the run's seed and the depth — never a
/// draw, never a stream (rule 3).
public ContentId ArenaFor(int stage, int seed);
```

## Behaviour

**The contract**

1. **An arena prefab is a set of named places, and `ArenaView` is the list of them.** `PlayerStart`, `GatePosition`, `SpawnPoints`, the barrier renderer, the door, the cover colliders and a `NavMeshSurface` carrying **pre-baked** data. Nothing here has an `Update`, nothing subscribes, and nothing decides: `ArenaPool` reads it and core is told the two numbers it needs through the snapshot.
2. **It validates itself loudly, in the Editor, against GD §7.2.** At least 3 spawn points and none within `SpawnDirector.MinPlayerDistance` of `PlayerStart` — a wave that cannot legally use half its own arena is a spec violation authored by hand, and the loud place for it is `OnValidate`, not the first stage of a run. Also checked: 3–6 cover pillars, a `NavMeshSurface` with data assigned, and an `Id` that parses as a `ContentId`. A failing arena is an error in the Console with the prefab named.
3. **`ModeSpec.ArenaFor(stage, seed)` is arithmetic, not a draw.** A hash of the pair, modulo the roster, and — when the roster has more than one entry — stepped forward by one if it lands on the arena the previous stage used, so the same room never appears twice running. Two consequences are the reason for it: a run **resumed** at stage 7 lands in the arena stage 7 always had without a byte of saved state, and arena identity does not depend on how many spawn positions the previous six stages happened to reject. Rejected: `spawn.NextInt(0, Arenas.Count)`, which is the obvious shape and makes the arena a function of stream position — i.e. of ledger row 1 being fixed first.
4. `Descent.asset` gains `arena.pillars` and `arena.tiered`, and M2-02 rule 7's content validation walks the arena roster with the enemy one — an unauthored arena id throws at the run's first frame with nothing announced, rather than forty seconds in at a door.

**Raising one**

5. **`StageArrived` raises the arena and places the player; `StageCleared` prepares the next one.** Two events, two jobs, and the split is the owner's rendering rule: nothing of the next stage exists while the current one is being fought, it is instantiated **inactive** during the `Clear` beat (M2-10 rule 5), and it is activated only on the `StageArrived` that arrives behind an already-opaque screen (M2-10 rule 9). At no point is a stage the player has not reached rendered, lit or ticked.
6. **Every arena stands at the same world origin, and only one is active.** That is what makes "the next stage is not visible" true by construction rather than by camera placement, and it is why the swap needs the fade at all. `ArenaPool` deactivates the outgoing arena, activates the incoming one, and moves the player to its `PlayerStart` — `CharacterController.enabled` off, transform written, on again, because a controller resolves collisions against the arena it thinks it is in. Core is not consulted and needs no intent: it holds no player position, and reads the new one off the next snapshot (AR §4.3's one-frame lag, under a black screen).
   **Rejected: laying the arenas out side by side with the door between them.** It is the literal reading of walking through a door, and GD §7.2 forbids the corridor it needs, the fixed 57° camera would clip the wall between them, and two arenas resident and rendered is the cost the swap exists to avoid.
   **Spawn points come with the room, so they leave `SpawnPlan`.** M2-05 put them on the plan because a run had one arena; a run now has one *per stage*, and a per-run field would describe the arena the player is no longer standing in. `SpawnDirector.Begin` takes the stage's points from `StageFlow`, which reads them off the active arena — the same handover, one scope narrower. M2-05 anticipated exactly this in its own *Out of scope*: *"Arena spawn-point authoring — M2-11."*
7. `NavMeshSurface` carries data baked per prefab, added when the arena activates and removed when it deactivates — the package is already in (`com.unity.ai.navigation` 2.0.14) and `GreyBox.prefab` already ships `NavMeshGreyBox.asset`, so this is the pattern M1-19 established rather than a new one. **No runtime bake:** a `BuildNavMesh` at a stage boundary is a multi-frame hitch on a phone, behind a fade that is 0.3 s long.
8. **An arena body is built once and reused.** The pool keeps what it has raised, parked inactive, and raises it again when the seed comes back to it — GD §7.2's pool of 8–12 means repeats are the normal case over a long run. `Count` never exceeds the roster, and the only `Instantiate` calls happen during a `Clear` beat, never during `Waves` (AR §14, GD §11.3).

**Cover**

9. **Cover pillars go on a `Cover` layer, and that layer is the whole of this task's contribution to ledger row 13.** M2-07a rule 1's price was that core holds no walls and a Spitter therefore shoots through a pillar; the fix is a line-of-sight *sense*, and a sense needs something to be true about. A layer rather than a tag or a component because the consumer is a `Physics.Raycast` mask, and a mask is the one form of that question that costs nothing per call. **Adding a layer edits `ProjectSettings/TagManager.asset`, which CLAUDE.md's *Ask before* covers — the owner approves it at review**, and `git diff ProjectSettings/` is the check Current State already asks for on every commit.
10. **`GreyBox.prefab` becomes `Arena_Pillars.prefab`** rather than being kept beside a new one: it is already 36 × 36 with off-centre pillars and a baked NavMesh, which is GD §7.2's rule read literally, and two arenas that differ only in name would make the pool untestable. `Arena_Tiered.prefab` is the second, and it exists so that the swap, the no-repeat rule and the per-arena NavMesh are all exercised by something rather than asserted.
11. **Two arenas, not eight.** GD §7.2 wants 8–12 per biome and that is M7-05's art pass; what this task owes is the *contract* and the machinery, and a third hand-built room proves nothing the second one did not. Named so that the gap is a decision.
12. `Dispose` unsubscribes and destroys every body it raised, standing or parked — `EnemyViews`' bargain, for its reason: one owner for the whole set.

## Tests

| Test | Given / When / Then |
|---|---|
| `ArenaFor_IsStableForAStage` | seed 7 / `ArenaFor(4, 7)` ×3 / the same id every time (rule 3) |
| `ArenaFor_DiffersBySeed` | `ArenaFor(4, 7)` vs `ArenaFor(4, 8)`, a roster of 8 / — / the ids differ (a distribution row, not an identity one) |
| `ArenaFor_NeverRepeatsConsecutively` | a roster of 2, stages 1…20 / — / no two adjacent stages share an id (rule 3) |
| `ArenaFor_SingleEntryRosterRepeats` | a roster of 1 / stages 1, 2 / the same id, no throw — the no-repeat rule cannot apply |
| `ArenaFor_EmptyRoster_IsDefault` | no arenas / `ArenaFor(1, 7)` / `default(ContentId)`, no throw (rule 3) |
| `ArenaFor_AllocatesNothing` | warm-up / 10 000 calls / allocated-bytes delta == 0 |
| `Arrived_RaisesTheArena` | a pool of two prefabs / `StageArrived(1, arena.pillars)` / `Active.Id` is `arena.pillars`, its object is active, `Count` 1 |
| `Arrived_PlacesThePlayer` | player at (9, 0, 9), arena's start (0, 0, −14) / `StageArrived` / the player is at the start |
| `Arrived_DeactivatesTheOutgoingArena` | pillars standing / `StageArrived(2, arena.tiered)` / pillars inactive, tiered active, exactly one active (rule 6) |
| `Arrived_ReusesAParkedArena` | pillars → tiered → pillars / — / `Count` 2, the same pillars instance, nothing instantiated on the third (rule 8) |
| `Cleared_PreparesTheNextInactive` | pillars standing / `StageCleared(1, gate, arena.tiered)` / a tiered body exists, **inactive**, and pillars is still the active one (rule 5) |
| `Cleared_PrepareIsNotRaise` | as above / — / the tiered body's renderers were never enabled and its `NavMeshSurface` added no data |
| `Arrived_WithoutPrepare_StillRaises` | no `StageCleared` first / `StageArrived(2, arena.tiered)` / it is raised, instantiated on the spot (rule 5) |
| `Arrived_UnknownId_Throws` | an id no prefab carries / `StageArrived` / throws naming the id and the roster (rule 4's failure, at the Unity end) |
| `Arrived_SealsIt` | / `StageArrived` / `Seal` was called: barrier on, door shut (rule 5) |
| `Cleared_OpensIt` | a sealed arena / `StageCleared` / `Open` was called: barrier off, door open |
| `Snapshot_CarriesTheActiveGate` | tiered standing, gate at (0, 0, 18) / build a snapshot / `HasGate` true, `GatePosition` (0, 0, 18) |
| `Snapshot_NoArena_HasNoGate` | nothing raised / build / `HasGate` false, no throw (M2-10 rule 15) |
| `Director_TakesTheArenasSpawnPoints` | tiered raised, 6 points / a stage begins / the director telegraphs only at those six (rule 6) |
| `Dispose_DestroysEveryBody` | two raised, one parked / `Dispose`, then publish `StageArrived` / nothing raised, no throw (rule 12) |
| **Validation (`ArenaView`, EditMode against the prefabs)** | |
| `Arena_HasAtLeastThreeSpawnPoints` | each shipped prefab / load / ≥ 3 (rule 2) |
| `Arena_SpawnPointsClearThePlayerStart` | each / load / every point ≥ 6 m from `PlayerStart` (GD §12.4) |
| `Arena_HasThreeToSixCoverPillars` | each / load / within GD §7.2's band, all on the `Cover` layer (rules 2, 9) |
| `Arena_HasBakedNavMesh` | each / load / a `NavMeshSurface` with data assigned, not an empty one (rule 7) |
| `Arena_IdMatchesTheAssetName` | each / load / `Arena_Pillars.prefab` ↔ `arena.pillars`, per the asset-naming rule |
| `Descent_RostersBothArenas` | `Data/Modes/Descent.asset` / load / `Arenas` is exactly the two ids — the shipped roster, which is rule 11's "two, not eight" as an assertion rather than a promise (rules 4, 11) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play, clear stage 1, walk into the door. The screen darkens and you are standing in a *different* room. Do it again: with two arenas, stage 3 is the first one back, and you never see a room you have not walked into.
2. **[Editor]** Pause in the Hierarchy during `Waves`: exactly one arena object is active, and no second arena exists at all. During the beat after the last kill, a second one appears **inactive**. That is rule 5, and it is the whole of the owner's "do not render the next stage until the player gets there".
3. **[Editor]** In the new arena, walk a chaser round a pillar — it routes rather than shoving through. The NavMesh came with the prefab (rule 7); if this fails, the surface's data is unassigned and rule 2's validation should already have said so.
4. **[Editor]** Check `git diff ProjectSettings/TagManager.asset`: one added layer, `Cover`, and nothing else. Current State asks for this diff on every commit; this is the one task that means to change it.
5. **[Editor]** Start two runs from the same seed. Both visit the same arenas in the same order (rule 3). Start one with a different seed: a different order.
6. **[device]** Whether a 0.3 s fade is long enough to cover the swap without reading as a stutter, and whether the room change lands as *progress* rather than as a load. Deferred with the rest of the device list.

## Out of scope

- **The line-of-sight sense** — M2-11b. This task puts the pillars on a layer; the next one raycasts against it, and **ledger row 13 leaves the table there**, not here.
- **`RunTicker`'s frame-order coverage** — ledger row 8's other half, also M2-11b, where the sense adds the step worth pinning.
- **8–12 arenas per biome, and their art** — M7-05/06 (rule 11). Two is the contract's proof, not the content.
- **Procedural arenas.** GD §7.2: *"a V3 conversation at the earliest."*
- **The Sanctum as a place** — M6-03. It is a screen, not an arena, and it does not enter this pool.
- **Hazards, tiers that block movement, or destructible cover.** GD §7.2's *no dead ends* and the cover guarantee are validated in rule 2; anything that can change them mid-stage is a system nothing has asked for.
- **Per-arena spawn *rules*** — which archetype may use which point. The director picks by clearance (M2-05 rule 9), and an arena that wants a flyer-only ledge is a conversation M7 can have.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._
