# UI-08b — The camera in portrait

**Size:** S · **Depends on:** UI-08a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** CC §2 · **Ledger rows:** none

## Goal

The follow camera shows as much ground across a portrait phone as it showed up a landscape one, so the player is not boxed into a 29° slot.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/FollowCamera.cs` | Game | holds the short side's field of view and sets `Camera.fieldOfView` from the aspect |
| `Tests/Game/Views/FollowCameraTests.cs` | Tests.Game | the rows below; `FollowCamera` has no fixture today |
| *small edits* | | `Run.unity`'s camera, if the distance is retuned |

## Public API

```csharp
// FollowCamera
[SerializeField] private float _shortSideFovDeg = 60f;   // today's vertical 60° at 16:9
public static float VerticalFov(float shortSideFovDeg, float aspect);  // aspect = width / height
```

## Behaviour

1. **The short side keeps 60°.** At an aspect of 1 or wider the vertical field of view is `_shortSideFovDeg`, as today. Narrower than 1, the *horizontal* field is `_shortSideFovDeg` and the vertical is derived from it: `2·atan(tan(short/2) / aspect)`. At 9:20 that is about 104° vertical, against today's 29° horizontal.
2. **It follows the aspect**: `fieldOfView` is set when the camera's aspect changes, not every frame, and never on a frame where it has not.
3. **Pitch, yaw, smoothing and the follow are unchanged.** The distance may be retuned by the owner's look at UI-07b's capture; if it is, the new number is in `Run.unity` and in *As built*.

## Tests

| Test | Given / When / Then |
|---|---|
| `VerticalFov_Landscape_IsTheShortSide` | rule 1: 16:9 → 60 |
| `VerticalFov_Portrait_KeepsHorizontalAtShortSide` | rule 1: 9:20 → horizontal 60 ± 0.1 |
| `VerticalFov_Square_IsTheShortSide` | rule 1: 1:1 → 60 |
| `FollowCamera_SetsFovOnAspectChangeOnly` | rule 2 |

## Manual verification

1. **[Editor]** UI-07b's capture of a run at 1080 × 2400 @ 420 dpi: the Ranger near the centre, both arena walls in sight across a Jungle stage-1 arena or as near as it comes.
2. **[device]** The owner judges the framing; distance retuned if asked.

## Out of scope

The fixed whole-arena camera of the endgame (the owner's direction, a later task), shifting the player up the screen to clear the thumbs, and any HUD change.

## As built

**Built.** `FollowCamera` holds `_shortSideFovDeg` (60) and a public static `VerticalFov(short, aspect)`: the short side at an aspect of 1 or wider, `2·atan(tan(short/2)/aspect)` below it (104.1° at 9:20). It caches its `Camera` in `OnEnable` and writes `fieldOfView` from `ApplyAspect`, which `OnEnable` and `LateUpdate` call and which writes only when the camera's aspect is not the one last applied. Pitch, yaw, smoothing, distance (16) and the follow are unchanged; `Run.unity` is untouched, its camera still stores 60, which the component overwrites on enable. `FollowCameraTests` has the four rows; the fourth plants a sentinel field of view and shows two frames at the same aspect leave it.

**Look.** Not through `Capture Look`: no Device Simulator was opened (opening one edits `Packages/manifest.json`, Traps §9). Instead `Run.unity` was opened in edit mode with `Arena_Hollow` (24 m floor, walls at ±12.5) instantiated, the camera put where `FollowCamera` puts it for a player at the origin, and rendered at 1080 × 2400 through `SubmitRenderRequest` with the new field and with the old 60° vertical (`Soulvail_Looks/UI-08b_hollow_new.png` and `_old.png`; nothing saved). New: the north and south walls are both in frame and the east and west walls sit just off the edge (the view is 18.5 m across at the player's depth). Old: the floor under the player and one wall. No HUD, enemies or run state in these. **Distance not retuned.**

**Judgement calls.** The owner's look at the framing, and whether to pull the distance back to bring the side walls in, is the device step.
