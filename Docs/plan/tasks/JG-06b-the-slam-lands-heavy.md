# JG-06b — The slam lands heavy

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 1, GD §11.3

## Goal

When Old Croaker lands a hop-slam, the screen shakes and the ground bursts: earth chunks thrown up and
falling back, and a low dust puff rolling out. The landing reads like something heavy hitting the ground.

## Why this shape

- **Keyed on `ShockwaveEmitted`.** A slam's landing is the tick its ring is sent, and the event carries
  where. It is also the Warden's shield slam, which should feel just as heavy. One trigger covers both
  bosses, and no new core event is needed.
- **The shake lives on the camera, as an offset.** `FollowCamera` adds `CameraShake.Offset` after its
  smoothing, so the follow is not disturbed and the shake ends exactly at rest. The shake is cosmetic, so
  it runs on `Time.deltaTime`. Pause sets `timeScale` to 0, and that freezes it.
- **Shake that decays, not random jitter.** A damped wobble with a fixed seed per kick (two sines at fixed
  frequencies on X and Z, the amplitude falling to 0) reads as a thud, not noise. `UnityEngine.Random` is
  not needed.
- **The burst is `ImpactView`'s pattern.** It is one mesh, one opaque vertex-coloured material and one
  draw call, pooled. It adds no transparent layer, which matches GD §11.3's fill-rate rule. The dust puff
  is opaque flat-shaded billows that shrink away, not alpha.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/CameraShake.cs` | Game | **New.** `Kick(strength)`, `Offset`; a damped wobble, stepped in `LateUpdate` before `FollowCamera` applies it |
| `Game/Views/SlamDustView.cs` | Game | **New.** One pooled mesh: 8 earth chunks thrown up and falling back, and 6 dust billows rolling out and shrinking; done in 0.6 s |
| `Prefabs/VFX/VFX_SlamDust.prefab` | — | **New.** `SlamDustView` on `M_Impact`, in Jungle earth and moss colours |
| `Tests/Game/Views/SlamImpactTests.cs` | Tests.Game | Rules 1–4 |
| *small edits* | | `Game/Views/FollowCamera.cs` (adds the shake offset); `Game/Views/BossViews.cs` (on `ShockwaveEmitted`, kicks the shake and plays a burst at the origin); `Game/Composition/RunScope.cs` (the prefab and the camera's `CameraShake`, passed to `BossViews`); the Run scene's camera (gains `CameraShake`) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class CameraShake : MonoBehaviour
    {
        public const float SlamStrength = 0.35f;  // metres, the first swing
        public const float Seconds = 0.4f;        // to rest
        public Vector3 Offset { get; }
        public bool IsShaking { get; }
        public void Kick(float strength);         // a stronger kick wins; a weaker one does not cut a stronger short
        public void Step(float dt);
    }

    public sealed class SlamDustView : MonoBehaviour, IPoolable
    {
        public const float Lifetime = 0.6f;
        public void Play(Vector3 position);
        public bool Step(float dt);               // false once done
    }
}
```

## Behaviour

1. **A kick shakes, then settles.** After `Kick(SlamStrength)`, `Offset` is non-zero, never longer than
   the strength, and exactly zero by `Seconds`. Before any kick it is zero.
2. **Kicks do not stack past the strongest.** A second kick during a shake restarts the decay at the
   larger of the two strengths. It never adds the two together.
3. **The burst plays and ends.** `Play` puts the burst at the position. The chunks rise and come back to
   the ground, and `Step` returns false by `Lifetime`. A pooled burst played again starts from nothing.
4. **Every `ShockwaveEmitted` lands heavy.** `BossViews` kicks the shake with `SlamStrength` and plays one
   burst at the ring's origin.

## Tests

| Test | Rule |
|---|---|
| `Kick_Shakes_ThenRestsAtZero` / `NoKick_NoOffset` | 1 |
| `Kick_DuringShake_TakesTheStronger` | 2 |
| `Dust_Plays_ThenEnds` / `Dust_Replayed_StartsClean` | 3 |
| `BossViews_ShockwaveEmitted_ShakesAndBursts` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 5: every Old Croaker landing thuds. The view jolts and settles in under half
   a second, and earth chunks and dust burst from under it. Descent's Warden slam does the same.
2. **[Editor]** Pause mid-shake: the view holds still and finishes the shake on Resume.
3. **[device]** A haptic pulse on the landing (`HapticsListener`), if the shake alone feels thin.

## Out of scope

- A settings toggle for screen shake. That comes with the options screen.
- Haptics, which are device-only and already owned by `HapticsListener`.
- Changing the ring itself (JG-06c).

## As built

_2026-10-26._ **`CameraShake`** is down-first at 12 Hz on Y, with a half-strength sway at 9 Hz on X. Both run under an
envelope of `strength × (1 − t/0.4)²`, so `|Offset|` ≤ strength and it is exactly zero at rest. `Kick` ignores
non-positive or non-finite strengths. `OnDisable` stops a shake. **`FollowCamera` now smooths a private
`_followPosition`, never `transform.position`.** Otherwise the shake would be smoothed into the follow
and drift it. It steps its optional `_shake` with `Time.deltaTime` in its own `LateUpdate` and adds `Offset`. So
the order is fixed and the shake has no `Update` of its own.

**`SlamDustView`** is `ImpactView`'s one-mesh pattern on `M_Impact`: chunks and billows are boxes of 24
vertices with one shade per face. Chunks are earth `7A5636`, every third moss `5E7A3A`. They leave a 1.8 m
circle at 55° ± 8° and 8 m/s ± 10% under 30 m/s² of gravity, so the heaviest is down by 0.52 s. They tumble,
land and stay without bouncing, and shrink into the ground over the last fifth. Billows are squat dust
`C9B48A` boxes rolling from 1.8 m to 4.5 m, swelling over the first third and then shrinking to nothing. Seeded by the
ring id, with `ImpactView`'s hash.

- **`BossViews`** takes `slamDustPrefab` and `shake` as optional last parameters, after `tonguePrefab`.
  `OnShockwaveEmitted` calls `LandHeavy`: `Kick(SlamStrength)` and one pooled burst at the ring's origin.
  The pool is prewarmed to `ShockwaveSystem.Capacity`. New readers: `DustCount`, `Dust(i)`.
- `Run.unity`: `CameraShake` added on Main Camera, `FollowCamera._shake` set, and `RunScope._slamDustPrefab` and
  `_cameraShake` set. `VFX_SlamDust.prefab` was built through the MCP.

**Deviations (said here):** Count and size went up after the first render. 8 chunks, 6 billows and a 1.2 m start
read as a few loose cubes under a 2.2× boss, so it is now **12 chunks of 0.5 m and 10 billows of 1.3 m from 1.8 m out
to 4.5 m**. `SlamImpactTests` adds two rows to the table: `Kick_NonPositiveOrNonFinite_IsIgnored` (the implied guard)
and `Prefab_IsDressed` (Traps §5). `Play(position)` also has a `Play(position, seed)` overload. Manual step 3
(haptics) stays deferred, as the spec says.

**Verified.** Two renders of the burst beside `Croaker.prefab` at 2.2× from the game's 57° pitch, at
0.12 s and 0.33 s: a ring of dust and earth bursting out from under the frog, with chunks in the air and then spread.
Full suite below. No compiler error or warning, and `dotnet format whitespace` is clean on the six .cs files.
