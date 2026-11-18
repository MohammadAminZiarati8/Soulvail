# SS-16b2 — Measure lanes and stable pillar identity

**Size:** M (5 counted) · **Depends on:** SS-16b1 · **Branch:** `ss-16b2-probe-adapter`
**Design refs:** [GD §9.4](../../GameDesign.md#94-the-sunscar--stage-10-the-shardback), [Invariants §18.1](../../Invariants.md#181-ordering).

## Goal

Answer tagged probes after the transform flush; owner go needed.

## Files

Under `Assets/_Project/`; new assets include `.meta`.

| Path | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/ShardbackProbeAdapter.cs` | Game | Nonalloc first-contact query |
| `Game/Views/ShardbackPillar.cs` | Game | Explicit stable slot/owner binding |
| `Tests/Game/Adapters/ShardbackProbeAdapterTests.cs` | Tests.Game | Physics and classification |
| `Tests/Game/Adapters/LaneProbeAdapterTests.cs` | Tests.Game | Legacy isolation |
| `Tests/PlayMode/FrameOrderTests.cs` | Tests.PlayMode | Facts ordering |
| *small edits* `Game/Adapters/LaneProbeAdapter.cs` | Game | Skip nondefault keys |
| *small edits* `Game/Composition/RunScope.cs`, `Game/Composition/RunTicker.cs` | Game | Register/inject/resolve at facts seam |

Only these paths change.

## Public API

```csharp
// Game.Adapters
ShardbackProbeAdapter(LayerMask cover, ArenaPool arenas);
ShardbackStop Probe(in LaneProbeIntent probe, Rect playBounds);
void Resolve(IntentBuffer intents, IRunSession session);
// Game.Views; MonoBehaviour, block namespace, serialized private _slot.
int ShardbackPillar.Slot { get; }
int ShardbackPillar.BossId { get; }
Guid ShardbackPillar.Lifetime { get; }
void ShardbackPillar.Bind(int bossId, Guid lifetime);
```

## Behaviour

1. Slot is authored 0–2, never hierarchy/collider/name/hit order. Bind requires positive boss ID/nonempty lifetime. A marker matching probe enemy/lifetime identifies a pillar; unmarked/foreign/unbound cover is Cover. Reject empty cover mask/null arenas.
2. Query Cover only, ignoring triggers. Charge: SphereCast radius 1 at EyeHeight, reporting safe centre travel. Fan: BoxCast half-width 0.6, half-height 0.5, half-depth 0.01, centre 0.01 behind From; report leading-plane travel. Initial overlap means zero. Edge cover clips the whole lane. Boundary comes from ArenaPool.Active.PlayBounds: analytically clip the swept footprint to that world rectangle, including radius for charge/lateral width for fan. Compare with cover; nearest wins. No Boundary layer exists; no floor/wall-name heuristics or NavMesh rebake. Missing/invalid arena gives Unknown at zero.
3. Preallocated nonalloc hit/overlap buffers; nearest independent of return order. Ties choose Boundary, Cover, then lowest pillar slot. Overflow gives Unknown at zero/-1. None reports Reach/-1. Preserve enemy/key/lane on every answer, including misses. Clamp distance to [0, Reach]; refuse invalid tagged input.
4. Legacy adapter handles default keys only; new adapter handles valid tagged keys only. Resolve after existing Physics.SyncTransforms, under existing pause guard; no extra sync/autoSyncTransforms. Register using existing cover mask/ArenaPool. Add trailing optional RunTicker dependency; no scene/settings edits.

## Tests

| Test | Given / When / Then |
|---|---|
| `Identity_SlotsAndForeignCover_Classified` | Reordered children/multiple colliders/foreign owner / probe / stable slot or generic cover (1) |
| `Probe_EdgesOverlapBoundary_FirstContact` | Edge cover, overlap, translated bounds, boundary-only, clear / probe / safe footprint distances/kinds (2/3) |
| `Probe_OrderOverflow_NoFalseClear` | Reversed hit order/ties/saturated buffers / query / deterministic selection or Unknown (3) |
| `Facts_TaggedOnly_AfterSync` | Mixed probes/moved colliders/pause / frame / one correct report after sync; legacy unchanged (4) |
| `Probe_Warm_AllocationFree` | Repeated overlap/cast/resolve / AllocationAssert.None / zero (3) |

Tests > Behaviour > API > Files; earlier code wins; record resolutions. Template guards implied.

## Out of scope

Pillar lifecycle, gameplay, art, stage wiring. Physics tests only.

## As built

Implementation: ≤1,500-byte body; delete/tick child/update PROGRESS. Fresh EditMode + PlayMode frame-order counts, compiler/analyzers, format.
