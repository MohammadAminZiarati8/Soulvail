# SS-13c — The body: prefab, animator, the sand spiral and the tornado

**Size:** M · **Depends on:** SS-13a, SS-13b · **Branch:** `ss-13c-caller-body`
**Design refs:** GD §8.13, §16.4, §3 › The Sunscar

## Goal

`StormCaller.prefab` plays the shared Rig_Medium takes to core's phases, with sand spiralling up from the staff while it channels. Each tornado is drawn as a tight dark ochre funnel standing on a red-orange 1.5 m ring that the funnel never hides, and collapses in 0.4 s.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/StormCaller.prefab` | — | `JackalPriest.prefab`'s component set on `StormCaller.fbx`, `CallerStaff` in `handslot.r`, plus `StormCallerView` |
| `Animation/Controllers/AC_StormCaller.controller` | — | copied from `AC_JackalPriest`: Locomotion, Channeling → looped Spellcasting, Hit, Death |
| `Game/Views/StormCallerView.cs` | Game | the channel spiral; the broken flicker |
| `Game/Views/TornadoViews.cs` | Game | two pooled funnels from `RunState.TornadoAt`; rise, follow, end, collapse |
| `Prefabs/Vfx/FX_StormCaller_Spiral.prefab` | — | sand spiralling up round the caller from the staff's `Glow` |
| `Prefabs/Vfx/FX_Tornado.prefab` | — | funnel particles + a `VFX_TelegraphRing` instance at the base |
| `Materials/Vfx/M_Tornado.mat` | — | the funnel's dark ochre, particle unlit |
| `Tests/Game/Views/StormCallerBodyTests.cs` | Tests.Game | view, prefab, controller, funnel rows |
| *small edits* | | `EnemyAnimatorView`: opt-in bool `Channeling` (`Mending`'s guard); `Game/Composition/RunScope.cs` + `Scenes/Run.unity`: `TornadoViews` registered as a component |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class StormCallerView : MonoBehaviour   // block namespace (Traps §5)
    {
        [SerializeField] private ParticleSystem _spiral;  // FX_StormCaller_Spiral instance
        [Inject] public void Construct(IEventSource events);
        public void Bind(int enemyId);
    }
    public sealed class TornadoViews : MonoBehaviour
    {
        [SerializeField] private GameObject _funnelPrefab;  // FX_Tornado
        [Inject] public void Construct(IRunSession session, IEventSource events);
        public int ShownCount { get; }
    }
}
```

## Behaviour

1. `Channel`: `Channeling` true and the spiral plays from the staff's `Glow`, rising round the body over the 2 s. `StormChannelBroken`: the spiral stops with a 0.15 s scatter, `Channeling` false. Hold or Flee: off.
2. `TornadoRose`: a pooled funnel (two, built under a deactivated root, AR §18.2) appears at the position, rising from the ground over 0.3 s. Each frame it follows `RunState.TornadoAt`. The ring is at the base, radius as sent, `Palette.Danger`.
3. **The ring is never hidden.** The ring draws after the funnel (higher queue), and the funnel's base is under 0.6 m across and under 0.6 alpha, so the whole 1.5 m ring shows from above.
4. **Unlike the storm.** The funnel is dark ochre, darker than the sandstorm and the Dunes, tight and turning fast (≥ 1 turn per second). It and the ring draw above the Sunscar storm (SS-08c's queue rule). Never cyan, gold or lime.
5. `TornadoEnded`: collapsed, the funnel shrinks into the ground over 0.4 s and the ring goes at once. Timed out, it thins out over 0.3 s. Then the funnel returns to the pool.
6. Stage clear or unload hides every funnel. No allocation per frame; a `MaterialPropertyBlock` per renderer.
7. `AC_StormCaller`: Idle/Walk/Run blend as `AC_JackalPriest`; `Channeling` → looped `Ranged_Magic_Spellcasting`; `Hit_A`, `Death_A`; all Rig_Medium; no write defaults.
8. Every other enemy prefab, controller and view is unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `View_Channel_PlaysSpiral` | `Channel` / step / spiral playing at `Glow`, `Channeling` true |
| `View_Broken_StopsSpiral` | broken / step / spiral stopped, `Channeling` false |
| `View_OtherId_Ignored` | another id / publish / unchanged |
| `Tornado_RoseShowsFunnelAndRing` | rose, r 1.5 / step / one funnel there; ring r 1.5, `Palette.Danger` |
| `Tornado_FollowsTheReading` | reading moves 1 m / step / funnel moved 1 m |
| `Tornado_RingDrawsOverFunnel` | funnel vs ring / queue, base width / ring higher; base < 0.6 m |
| `Tornado_CollapseTakes04` | collapsed / step 0.2, 0.45 s / shrinking; pooled, ring gone at once |
| `Tornado_TwoAtMost` | two rose / step / `ShownCount` 2, pool unchanged |
| `Tornado_UnlikeTheStorm` | funnel colour vs storm, Dunes / luminance / darker; hue not cyan, gold or lime |
| `Effects_DrawOverTheStorm` | spiral, funnel, ring vs storm / queue / higher |
| `Controller_EveryStateIsRigMedium` | controller / read / every motion a Rig_Medium take; no write defaults |
| `Prefab_WiresTheView` | prefab / load / `StormCallerView` refs set; staff in `handslot.r`; `_telegraphGlow` 0 |
| `PriestBody*`, `Shaman*View*` | unchanged / run / green |

## Manual verification (Editor / device)

Not in play until SS-13d, whose steps cover the body and the tornado.

## Out of scope

- Data, roster, localisation (SS-13d). Any core change. The background sandstorm. Sound.

## As built

_Commit body._
