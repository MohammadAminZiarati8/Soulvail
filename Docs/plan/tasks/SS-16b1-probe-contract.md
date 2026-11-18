# SS-16b1 — Correlated obstruction facts

**Size:** M (3 counted) · **Depends on:** SS-16a · **Branch:** `ss-16b1-probe-contract`
**Design refs:** [GD §9.4](../../GameDesign.md#94-the-sunscar--stage-10-the-shardback), [Invariants §18.2–4](../../Invariants.md#182-the-boundary).

## Goal

Transport Shardback-specific obstruction facts without changing legacy lane semantics. Implementation needs the owner's go.

## Files

Paths under `Assets/_Project/`; new assets include their `.meta`.

| Path | Assembly | Purpose |
|---|---|---|
| `Core/Run/ShardbackProbeFacts.cs` | Core | Key, kind and immutable report structs |
| `Core/Ai/IShardbackProbeListener.cs` | Core | Fact receiver |
| `Tests/Core/Run/ShardbackProbeRoutingTests.cs` | Tests.Core | Contract/routing guards |
| *small edits* `Core/Ports/IIntentSink.cs` | Core | Extend LaneProbeIntent only; no sink member |
| *small edits* `Core/Ports/IRunSession.cs`, `Core/Run/RunSession.cs` | Core | New report door/forward |
| *ripple* `Tests/Game/Adapters/BindProbeAdapterTests.cs`, `Tests/Game/Adapters/LaneProbeAdapterTests.cs`, `Tests/Game/Composition/ResumeFlowTests.cs`, `Tests/PlayMode/FrameOrderTests.cs` | Tests.Game/PlayMode | Four report stubs; Game buffer test |

Only these files change. Ten code paths, three counted; additive edits follow ROADMAP sizing.

## Public API

```csharp
// Core.Run; readonly structs expose readonly fields named as constructor args (PascalCase).
ShardbackProbeKey(Guid lifetime, ulong cast);
bool ShardbackProbeKey.IsValid { get; }
bool ShardbackStop.IsValid { get; }
enum ShardbackStopKind { Unknown, None, Cover, Boundary, Pillar }
ShardbackStop(int bossId, ShardbackProbeKey key, int lane,
    ShardbackStopKind kind, float distance, int pillarSlot);
// Core.Ports: add readonly Key/Lane; retain old constructor/fields.
LaneProbeIntent(int enemyId, Vector3 from, Vector3 direction, float reach,
    float radius, ShardbackProbeKey key, int lane);
void IRunSession.ReportShardbackStop(in ShardbackStop report);
// Core.Ai
void IShardbackProbeListener.OnShardbackStop(in ShardbackStop report);
```

## Behaviour

1. Key: nonempty lifetime and positive cast. Old intents have default Key; new intents require valid Key, lane 0–3, finite origin/direction, nonzero XZ direction and finite positive reach/radius. Existing constructor behaviour stays intact. No new IIntentSink member or EnemySystem.RootGate ripple.
2. Lane 0 is charge; 1/2/3 are centre/+25/-25 fan. Reports require positive boss ID, valid key/lane/kind, finite nonnegative distance. Pillar requires slot 0–2; every other kind requires -1. Unknown is a conservative blocked answer, never a pillar. Default structs fail receiver validation too.
3. Report door requires Running, then forwards only to a registered, living BossBehaviour whose Inner implements the listener, outside its beat. Missing/dead/wrong-type targets are ignored. No damage, breakage or acceptance decision in the forwarder. SS-16b3 owns correlation rejection.
4. Transport uses existing LaneProbe buffering/clearing and RootGate forwarding unchanged. Legacy ReportLaneStop and its adapters/listeners retain their API and behaviour.

## Tests

| Test | Given / When / Then |
|---|---|
| `Contract_Invalid_Refused` | Rule 1/2 invalid inputs/defaults / construct or validate / reject; valid boundary at zero accepted |
| `Report_Route_OnlyLiveListener` | Rule 3 target variants + stopped session / report / exact forward, ignore, or existing Running exception |
| `LaneProbe_Extended_Preserved` (Tests.Game) | Old/new intents / buffer, RootGate, clear / fields preserved, list emptied, legacy tests unchanged |

Tests win over Behaviour, then API, then Files; earlier code wins over this spec. Name resolutions in As built. Guard rows are implied per `_TEMPLATE.md`.

## Out of scope

Physics, behaviour changes, art, presentation, stage wiring. No manual verification.

## As built

At implementation merge: commit body ≤1,500 bytes; delete this spec, tick its child row, update PROGRESS; report fresh full EditMode counts, compiler/analyzers and format.
