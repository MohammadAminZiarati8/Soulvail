# LS-01 — The game runs landscape, and the camera stays on the arena

**Size:** M · **Depends on:** — · **Branch:** `ls-01-landscape-and-bounded-camera`
**Design refs:** GD §5.1, GD §5.2, GD §7.2

## Goal

Landscape either way up; the follow camera never shows more than 2 m past an arena's walls, and stops on any axis the arena fits.

## Context — portrait assumptions (2026-11-09)

- `ProjectSettings.asset`: `defaultScreenOrientation: 0` (Portrait), no autorotate.
- `FollowCamera`: 60° on the short side holds; at d 16, p 57 it sees 18.5 × 41 m portrait, 40 × 25.6 m landscape (19.5:9). Unbounded.
- Arenas: squares run south → north; landscape hides the gate (LS-02, LS-03).
- `ArenaView` has no size field.
- Unchanged: `ThreatArrows`, `HawkView.Offscreen`, `SnapshotBuilder` (yaw 0), boss cover (±8 m), Thornheart ring (14 m).
- `Lane` summary: "portrait" reworded.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `ProjectSettings/ProjectSettings.asset` (repo root) | — | rule 1; owner approved, `ALLOW_PROJECT_SETTINGS=1` |
| `Game/Views/FollowCamera.cs` | Game | rules 2–5 |
| `Game/Arena/ArenaView.cs` | Game | rules 6–7 |
| `Tests/Game/Views/FollowCameraTests.cs` | Tests.Game | |
| `Tests/Game/Arena/ArenaViewTests.cs` | Tests.Game | |
| `Tests/Game/Editor/OrientationSettingsTests.cs` | Tests.Game | new |
| *small edits* | | `RunScope` registers the camera, `Run.unity` wires it; five arena prefabs get `_halfSize`; `Lane.cs` summary |

Allows `.meta` files and test count/coverage updates.

## Public API

```csharp
public Rect PlayBounds { get; }   // ArenaView, XZ
public static Rect Footprint(float pitchDeg, float distance, float verticalFovDeg, float aspect);
public static Vector2 ClampFocus(Vector2 focus, Rect footprint, Rect bounds, float margin);
[Inject] public void Construct(ArenaPool arenas);
```

## Behaviour

1. Auto-rotation; both landscapes allowed, both portraits not.
2. `Footprint`, relative to the focus: x = ±d·tan(v/2)·aspect; y from `h/tan(p+v/2) − d·cos p` to `h/tan(p−v/2) − d·cos p`, h = d·sin p; far = +∞ when p ≤ v/2.
3. `ClampFocus`, per axis, bounds grown by `margin`: a footprint at least that long is centred on the bounds; a shorter one is kept inside.
4. `LateUpdate` clamps the target before smoothing with `_boundsMarginM` (2) and `ArenaPool.Active.PlayBounds`; no pool or arena → no clamp.
5. The clamp allocates nothing.
6. `PlayBounds` = transform XZ ± `_halfSize`.
7. `DescribeFaults` reports `_halfSize` ≤ 0 on either axis, and a start, gate or spawn outside `PlayBounds`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Orientation_IsLandscapeAutoRotate` | PlayerSettings / — / rule 1 |
| `Footprint_Pitch57_Matches` | 57°, 16, 60°, 2.1667 / — / y −8.0..17.6, x ±20.0 |
| `Footprint_ShallowPitch_FarIsInfinite` | p 20, v 60 / — / +∞ |
| `ClampFocus_ArenaFits_Centres` | footprint 40, bounds 30 / any x / centred |
| `ClampFocus_ArenaLarger_StopsAtGrownEdge` | 32.8 in 44, margin 2 / far east / max x 24 |
| `ClampFocus_Inside_Unchanged` | mid-arena / — / as given |
| `LateUpdate_NoPool_Unclamped` | no pool / step / reaches target |
| `LateUpdate_Clamp_AllocatesNothing` | active arena / `AllocationAssert.None` / passes |
| `PlayBounds_IsHalfSizeAroundTransform` | (5,0,3), (10,6) / — / x −5..15, z −3..9 |
| `DescribeFaults_ZeroHalfSize` | (0,6) / — / reported |
| `DescribeFaults_GateOutside` | gate past bounds / — / reported |
| `Arenas_ShippedHaveNoFaults` *(existing)* | shipped / — / none |

## Manual verification (Editor / device)

1. **[Editor]** 2400×1080, stage 1: gate on screen, no sideways pan.
2. **[Editor]** Stage 5: at each wall the view stops 2 m past it.
3. **[device]** Turn the phone over: the game flips; upright, it stays landscape.

## Out of scope

- All UI: [ROADMAP › For the UI rework](../ROADMAP.md#landscape).
- Arena shapes (LS-02, LS-03); camera distance, pitch, a fixed mode.

## As built

_Commit body, ≤ 1 500 bytes._
