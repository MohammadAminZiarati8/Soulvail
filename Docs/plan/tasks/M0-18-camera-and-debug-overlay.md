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

**Camera framing, measured rather than eyeballed.** At the specced `pitch 57° / distance 16 / yaw 0`, against the Run camera's FOV 60 and a 16:9 screen, the camera sits **13.42 m above and 8.71 m behind** the player and frames **25.6 m of depth** (8.0 m behind the player to 17.6 m ahead) **× 32.8 m of width** at the player's depth. The arena is 36 × 36, so manual step 1's *"the whole 36 m arena is roughly framed"* is **not** what these numbers produce — the full depth would need `distance ≈ 22.5`, which widens to ~46 m and pushes the player's own silhouette small. The specced values ship as the serialized defaults because they frame the *player* well, which is what M0 is judging; the arena-framing question is a tuning decision and M0-20 owns it, with both fields live-tunable in the Inspector while playing.

**Frame order.** `FollowCamera.LateUpdate` and `DebugOverlay.LateUpdate`, both for the same reason: `RunTicker` is an `ITickable` and therefore runs in the Update phase, so by LateUpdate the snapshot is built, core has ticked, and the intent has been written and applied. A camera reading the player's position in Update would race the body it follows; an overlay reading in Update would show the previous frame's numbers.

**Overlay values.** `in` is `WorldSnapshot.MoveInput` (X, Y in stick space); `|v|` is `PlayerMoveIntent.Velocity.Length()`; `face` is the intent's X and Z; `fps` is `1 / Time.unscaledDeltaTime` exponentially smoothed over ~0.5 s, sampled every frame and drawn at 10 Hz. All three intent-derived numbers read **0.00 when `HasPlayerMove` is false**, never the stale intent the buffer still holds.

| Setting | Value |
|---|---|
| `FollowCamera` | pitch 57, yaw 0, distance 16, smoothTime 0.12, target → `Player` |
| Overlay canvas | Screen Space Overlay, sorting order 1, Scale With Screen Size 1920 × 1080, match 0.5 — mirrors `Hud.prefab` |
| Overlay text | `LiberationSans SDF`, 24 reference px, TopLeft, white at 85 % alpha, no wrap, anchored (16, −16) from top-left |
| Raycasting | **No `GraphicRaycaster` on the canvas and `raycastTarget: false` on the text** — the overlay sits above the stick's region and must never take a thumb |

**Measured in play, not eyeballed** (frame-counted driver injecting a `Gamepad` left stick into the Run scene, sampling the camera against the player and reading the overlay's own text back):

| Phase | Camera offset from player | Camera euler | Overlay |
|---|---|---|---|
| At rest | `(0.000, 13.418, −8.714)` | `(57.00, 0, 0)` | `in 0.00 0.00  \|v\| 0.00  face 0.00 1.00` |
| Full stick +Y | `(0.000, 13.419, −9.317)` | `(57.00, 0, 0)` | `in 0.00 1.00  \|v\| 5.40  face 0.00 1.00` |
| Released | `(0.000, 13.419, −8.803)` | `(57.00, 0, 0)` | `in 0.00 0.00  \|v\| 0.00  face 0.00 1.00` |
| Full stick −Y | `(0.000, 13.419, −8.112)` | `(57.00, 0, 0)` | `in 0.00 −1.00  \|v\| 5.40  face 0.00 −1.00` |

The at-rest offset matches the predicted `(0, 13.42, −8.71)` to three decimals and the pitch is exact. Manual step 2 passes in full: `|v|` reads **5.40** at full stick, **0.00** at rest, and `face` flips sign on reversal. The follow lag under sustained motion is **0.60 m** at 5.4 m/s, against the `v × smoothTime = 0.65 m` a 0.12 s `SmoothDamp` predicts, and the camera's X and Y held to within 1 mm across every phase — there is no jitter to see. Note that `face` reads `0.00 1.00` at rest rather than zeros: core writes an intent every tick and `PlayerMotor` holds the last facing when idle, so `HasPlayerMove` is true for the whole run and the zeroing branch is only reached outside one.

**Not verified here:** manual step 1's "no jitter when walking into a wall" (needs a driven collision case), step 1's arena framing (see above — it is a tuning decision for M0-20), and step 3, which needs M0-19's non-development build to exist.

**Deviations:** four, all listed in the PROGRESS entry — an initial camera snap in `OnEnable`, a `SafeArea` child on the overlay prefab, `RunScope` treating the overlay as optional where `PlayerView` is mandatory, and a `Start`-time injection guard.
