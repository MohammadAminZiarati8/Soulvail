# JG-13d — Unity reports open ground

**Size:** S · **Depends on:** JG-13c (`WorldSnapshot.OpenGround`) · **Branch:** `dev` (direct)
**Design refs:** AR §4.2, ADR-0001, ADR-0003; JG-12g rules 2–5

## Goal

Unity reports where the standing arena's floor is open — on the NavMesh and clear of anything on the
`Cover` layer — so core can keep adds out of Mother Pitcher's stones and inside the room.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/OpenGroundSense.cs` | Game | the floor lattice, re-sensed only on change |
| `Tests/Game/Adapters/OpenGroundSenseTests.cs` | Tests.Game | rules 1–7 |
| *small edits* | | `Game/Views/CoverViews.cs` (`Version`); `Game/Adapters/SnapshotBuilder.cs` (senses, then sets `OpenGround`); `Game/Composition/RunScope.cs` (registers the sense with the `Cover` mask); `Tests/Game/Views/CoverViewsTests.cs` (rule 3 row) |
| *ripple* | | `SnapshotBuilder`'s constructor gains the sense: its fixtures pass one or null |

## Public API

```csharp
public sealed class OpenGroundSense
{
    public const float Step = 1f, CoverClearance = 1f, FloorProbe = 0.3f;
    public const int MaxSpots = 1024;
    public OpenGroundSense(LayerMask cover, CoverViews covers);
    public IReadOnlyList<System.Numerics.Vector3> Spots { get; }
    public int Senses { get; }                     // how many times it has re-sensed
    public void Sense(ArenaView arena);
}
public int Version { get; }                       // CoverViews
```

## Behaviour

1. **A lattice of the floor.** `Sense` lays points `Step` apart over the arena NavMesh surface's
   bounds and keeps a point where `NavMesh.SamplePosition` finds floor within `FloorProbe`, at the
   sampled position. At most `MaxSpots`.
2. **Clear of cover.** A kept point has no `Cover` collider within `CoverClearance` of the segment
   from the floor to `LineOfSightSense.EyeHeight`. `Physics.SyncTransforms` runs first
   (`autoSyncTransforms` is off).
3. **Sensed on change only.** `Sense` re-senses when the arena or `CoverViews.Version` differs from
   last time; otherwise it does nothing. `Version` rises on every place, lift and sweep.
4. **Stones count the next frame.** Mother spawns, `CoverViews` stands her stones, the next `Build`
   reports no spot inside them.
5. **Reported by reference.** `Build` sets `snapshot.OpenGround = Spots` every frame; no arena, empty.
6. **No garbage.** A re-sense allocates nothing (`AllocationAssert.None`).
7. **The real room plays.** `Arena_Clearing` with `Cover_MotherStones` on `Spawn_06`: ≥ 40 spots
   4–8 m from it, none within 1 m of a stone collider, every spot on the NavMesh.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sense_KeepsOnlyFloor` | 10 × 10 m surface with a hole / sense / spots 1 m apart, none in the hole or off the floor |
| `Sense_KeepsClearOfCover` | a `Cover` box at the centre / sense / no spot within 1 m of it; a non-`Cover` box ignored |
| `Sense_OnlyOnChange` | sense twice, then a cover placed, then a new arena / sense / `Senses` 1, 2, 3 |
| `Build_AfterStonesStand_ReportsNoneInside` | Mother's `EnemySpawned` published / next `Build` / no spot inside a stone |
| `Build_ReportsTheSpotsByReference` | sense, then no arena / `Build` / same list instance; then empty |
| `Sense_AllocatesNothing` | warmed / re-sense under `AllocationAssert.None` / passes |
| `CoverViews_VersionRisesOnPlaceLiftSweep` | place, lift, sweep / read / three increments |
| `Clearing_WithMotherStones_HasOpenGround` | rule 7's room / sense / rule 7 holds |

## Manual verification (Editor / device)

1. **[Editor]** Test start 10 (after JG-13c): buds and phase Frogs never stand in or touching a
   stone, never off the floor, never on the player.

## Out of scope

- Moving cover; a per-frame physics sweep; enemy or player clearance (core's, JG-13c).

## As built

`OpenGroundSense` preallocates a read-only list (cap 1 024), samples a 1 m lattice on the
standing surface's agent NavMesh within 0.3 m, and rejects Cover capsules of radius 1 m up
to eye height after syncing transforms. Surface bounds include walls: probes use the authored
player-start floor height, with the surface's position/rotation applied to the lattice.
Arena identity and `CoverViews.Version` invalidate the cache; destroyed/no arena clears it.
Place, owner lift and stage sweep advance Version. `SnapshotBuilder` reports the same list
every frame; RunScope supplies the Cover mask. Constructor ripple only in the existing fixtures.

Verification: EditMode total 4 689 / passed 4 688 / failed 0 (names: none) / inconclusive 1
(known animator clock); fresh XML start 2026-10-30 20:11:30Z. Compiler errors/analyzer warnings 0;
dotnet format clean (11 C# files). Eight new rule tests include actual Clearing/Spawn_06 with
Mother stones, next-Build invalidation, reference identity and allocation-free re-sensing.
Manual still owed: Test start 10, judge buds/phase Frogs clear stones, floor edges and the player.
