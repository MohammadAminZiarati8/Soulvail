# SS-16b4 — Execute pillar lifetime and break decisions

**Size:** M (4 counted) · **Depends on:** SS-16b3 · **Branch:** `ss-16b4-pillar-lifecycle`
**Design refs:** [GD §9.4](../../GameDesign.md#94-the-sunscar--stage-10-the-shardback), [Invariants §18.4](../../Invariants.md#184-combat-and-perception).

## Goal

Execute keyed breaks and restore fresh fights using synthetic geometry. Implementation needs the owner's go.

## Files

Under `Assets/_Project/`; new tests include `.meta`.

| Path | Assembly | Purpose |
|---|---|---|
| `Game/Views/CoverViews.cs` | Game | Subscribe, bind, break and restore |
| `Game/Views/ShardbackPillar.cs` | Game | Capture/restore authored collider and renderer states |
| `Tests/Game/Views/CoverViewsTests.cs` | Tests.Game | Preserve other boss cover behaviour |
| `Tests/Game/Views/ShardbackPillarTests.cs` | Tests.Game | Synthetic three-slot lifecycle and physics |

Only these four files change.

## Public API

```csharp
// Game.Views.ShardbackPillar (retain SS-16b2 API)
void Break();
void Restore();
bool IsBroken { get; }
// CoverViews retains constructor, LiveCount, Version and Dispose.
// Subscribe to ShardbackFightStarted and ShardbackPillarBroken from SS-16b3.
```

## Behaviour

1. Precache markers/colliders/renderers under inactive cover at construction. Marked cover requires slots 0/1/2 exactly once with blocking colliders; reject malformed sets before play. Unmarked boss cover stays supported. No hierarchy searches on break/tick.
2. EnemySpawned places cover. FightStarted for current owner restores states, clears IsBroken, binds slots to boss/lifetime, advances Version. Duplicate lifetime preserves breaks. Phases never restore; new lifetime restores all three even on reused ID/object. Missing marked cover is supported until SS-16d authors it; ignore lifecycle events then.
3. Matching owner/lifetime/intact slot only: Broken disables movement/shot colliders together, hides renderers, sets IsBroken, advances Version once. No animation callback/physical rubble. Duplicate/foreign/stale events do nothing. Solid until core observes arrival/publishes; probes never disable. Direct physics queries then see no obstruction; throttled perception keeps normal cadence.
4. Restore cached authored states, including disabled children. Despawn/stage arrival lifts cover/clears bindings; Dispose also unsubscribes. Late events cannot touch a new owner. Preserve destroyed-object/unload guards and other cover counts/placement. No Update/per-break allocations.
5. All-spent slots pass movement/shots; generic cover/boundaries stay solid through phases. Fresh fight restores blockers. Placement/rubble/tell alignment belong to SS-16d.

## Tests

| Test | Given / When / Then |
|---|---|
| `Slots_Invalid_Refused` | Synthetic malformed marked covers / construct / fail before activation (1) |
| `Fight_DuplicateAndNewLifetime_RestoreOnce` | Broken slot / same start, phase, new start / preserve then restore/bind (2) |
| `Break_AtImpact_RemovesBothObstructions` | Solid pillar / probe then event / solid until event; movement/shot clear together; Version once (3) |
| `Lifecycle_StaleEventsAndUnload_Safe` | Recycled owner/stage/destroyed cover / late event/dispose / no mutation/errors; legacy passes (4) |
| `AllSpent_FreshFight_RestoresThree` | All broken, wall present / beat/fresh fight / rule 5; authored disabled states preserved |
| `BreakRestore_Warm_AllocationFree` | Cached slots / AllocationAssert.None / zero (4) |

Tests > Behaviour > API > Files; earlier code wins; record resolutions. Template guards implied.

## Manual verification

None owed; synthetic Editor physics tests.

## Out of scope

Art/prefabs/sound/nav rebakes, stage/adds/HP, unrelated cover restoration.

## As built

Implementation: ≤1,500-byte body; delete/tick child, tick parent after all four; update PROGRESS. Fresh EditMode counts, compiler/analyzers/format.
