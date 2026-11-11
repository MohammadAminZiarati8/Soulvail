# SS-10c — The body: prefab, animator, the sand ripple and the eruption ring

**Size:** M · **Depends on:** SS-10a, SS-10b · **Branch:** `ss-10c-antlion-body`
**Design refs:** GD §8.10, §16.4, §3 › The Sunscar

## Goal

`Antlion.prefab` plays SS-10b's clips to core's phases. Underground, the body is hidden and a sand ripple with a fading trail moves where it is. When it arms, a red-orange lob ring and a sand swirl show the eruption.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Antlion.prefab` | — | `Glassback.prefab`'s component set on `Antlion.fbx`, plus `AntlionView` |
| `Animation/Controllers/AC_Antlion.controller` | — | Locomotion, DigIn, Erupt → Surfaced, Hit, Death |
| `Game/Views/AntlionView.cs` | Game | reads `AntlionPhaseChanged`/`AntlionRingArmed` for its id; body, ripple, ring |
| `Prefabs/Vfx/FX_Antlion_Ripple.prefab` | — | mound + dust plume + trail; `FX_Gravemaw_Trail`'s parts, sand-toned |
| `Prefabs/Vfx/FX_Antlion_Swirl.prefab` | — | a sand swirl inside the ring for the arm |
| `Tests/Game/Views/AntlionViewTests.cs` | Tests.Game | view, prefab, controller rows |
| *small edits* | | `EnemyAnimatorView`: opt-in int `AntlionPhase` (`ShinePhase`'s guard) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class AntlionView : MonoBehaviour     // block namespace (Traps §5)
    {
        [SerializeField] private Renderer[] _body;
        [SerializeField] private ParticleSystem _ripple;   // FX_Antlion_Ripple instance
        [SerializeField] private ParticleSystem _swirl;    // FX_Antlion_Swirl instance
        [Inject] public void Construct(IEventSource events);
        public void Bind(int enemyId);
    }
}
```

## Behaviour

1. `Dig`: the body is shown and plays `DigIn`. The ripple starts in its last 0.2 s.
2. `Tunnel`/`Arm`: the body renderers and its HP bar and status row are hidden, and the ripple plays at the agent's position. The trail fades over 1 s. The hit flash is off.
3. `AntlionRingArmed`: the lob ring as built (`Palette.Danger`) at the centre, radius as sent, filling over its seconds, and the swirl inside it. Both stop at the eruption.
4. `Surfaced`: the body is shown and plays `Erupt` then the `Surfaced` loop, with a sand-burst puff. The HP bar comes back. The ripple stops.
5. The ripple, trail, swirl and ring draw above the Sunscar storm (SS-08c's queue rule). Sand-toned, darker than the Dunes so the mound reads. Never cyan, and nothing red-orange but the ring.
6. Pooled reuse resets to `Dig`. Death hides the ripple and ring at once. No allocation per frame, and a `MaterialPropertyBlock` per renderer.
7. `AC_Antlion`: Locomotion `Idle`/`Walk` by speed. `AntlionPhase` 0 plays `DigIn`, 1–2 an empty hidden state, 3 `Erupt` → `Surfaced`. `Hit`, `Death` as `AC_Glassback`'s. No write defaults.
8. Every other enemy prefab, controller and view is unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `View_Tunnel_HidesBodyShowsRipple` | `Tunnel` / step / body renderers off, ripple playing, bar hidden |
| `View_Ripple_FollowsAgent` | tunnelling 2 m / step / ripple at the agent |
| `View_RingArmed_DrawsRingAndSwirl` | event r 1.6, 0.8 s / step / ring at centre, r 1.6; swirl on |
| `View_Surfaced_ShowsBody` | `Surfaced` / step / body on, ripple and ring off, bar shown |
| `View_OtherId_Ignored` | another id / publish / unchanged |
| `View_Death_ClearsEffects` | killed surfaced / step / ripple and ring off |
| `View_PooledReuse_ResetsToDig` | reuse / bind / body shown, ripple off |
| `Animator_AntlionPhase_Set` | prefab / each phase / int matches |
| `Controller_EveryStateIsTheModels` | controller / read / every motion an `Antlion.fbx` clip; no write defaults |
| `Prefab_WiresTheView` | prefab / load / `AntlionView` refs set; `_telegraphGlow` 0 |
| `Effects_DrawOverTheStorm` | ripple, swirl, ring vs storm / queue / higher |
| `Effects_NoCyan` | ripple, swirl colours / hue / outside the cyan band |

## Manual verification (Editor / device)

Not in play until SS-10d, whose steps cover the body.

## Out of scope

- Data, roster, localisation (SS-10d). Any core change. Sound.

## As built

_Commit body._
