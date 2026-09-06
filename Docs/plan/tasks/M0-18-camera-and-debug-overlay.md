# M0-18 — `FollowCamera`, `DebugOverlay`

**Size:** S · **Depends on:** M0-16 · **Branch:** `m0-18-camera-and-debug-overlay`
**Design refs:** GD §5.1 (camera), CC §8 (checklist needs the overlay)

## Goal

The run is viewed from the decided top-down angle with smooth follow, and an on-screen overlay shows what core is seeing and deciding — so feel tuning on the phone is done against numbers, not vibes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/FollowCamera.cs` | Game | Pitch/yaw/distance follow with smoothing |
| `Game/Presentation/DebugOverlay.cs` | Game | TMP readout of snapshot + intent |
| `Game/Composition/RunScope.cs` | Game | *modified:* serialized `DebugOverlay`, `RegisterComponent` |
| **Assets** | | |
| `Prefabs/UI/DebugOverlay.prefab` | — | Small TMP text, top-left under the HUD strip |
| `Scenes/Run.unity` | — | *modified:* camera gets `FollowCamera` targeting the player; overlay instance |

## Public API

```csharp
namespace Soulvail.Game.Views;

public sealed class FollowCamera : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private float _pitchDeg = 57f;
    [SerializeField] private float _yawDeg = 0f;
    [SerializeField] private float _distance = 16f;
    [SerializeField] private float _smoothTime = 0.12f;
}
```

```csharp
namespace Soulvail.Game.Presentation;

public sealed class DebugOverlay : MonoBehaviour
{
    [SerializeField] private TMP_Text _text;
    [SerializeField] private bool _forceVisible;          // otherwise visible only in development builds / Editor
    [Inject] public void Construct(WorldSnapshot snapshot, IntentBuffer intents);
}
```

## Behaviour

**FollowCamera**
1. `LateUpdate`: `desired = target.position + Quaternion.Euler(pitch, yaw, 0) × Vector3.back × distance`; `position = SmoothDamp(position, desired, ref vel, smoothTime)`; `rotation = Quaternion.Euler(pitch, yaw, 0)` (fixed — it never rolls or re-aims, so the ground plane reads stably).
2. With yaw 0 the camera looks along +Z, matching the stick mapping in `SnapshotBuilder`. If yaw is ever changed, `SnapshotBuilder` must rotate `MoveInput` by −yaw — noted there.
3. No collision handling; the arena has no ceilings (GD §7.2).

**DebugOverlay**
4. Visible when `Debug.isDebugBuild || Application.isEditor || _forceVisible`; otherwise the GameObject disables itself in `Awake`.
5. Refreshes text 10× per second (not every frame): `in {x:F2} {y:F2}  |v| {m:F2}  face {x:F2} {z:F2}  fps {n}`. Uses a preallocated `StringBuilder`; the TMP `SetText` call is the only allocation-adjacent operation.
6. Reads `WorldSnapshot.MoveInput`, `IntentBuffer.PlayerMove.Velocity/Facing` (if `HasPlayerMove`), and a smoothed fps.

## Tests

None — presentation. Verified by eye and by the numbers it shows.

## Manual verification (Editor)

1. Play in `Run`: camera sits behind/above at ~57°, the whole 36 m arena is roughly framed, follow is smooth with no jitter when walking into a wall.
2. Overlay shows `in 0.00 0.00`, `|v| 0.00` at rest; full stick → `|v| 5.40`; `face` flips sign when reversing.
3. Set `_forceVisible = false`, make a non-development build later (M0-19) → overlay absent.

## Acceptance

- [ ] Manual steps verified
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Screen shake, hit-stop, aim-lead offset — M8-01.
- Overlay toggles or extra readouts — add per task as needed.

## As built

_Filled at merge._
