# SS-14c — The body: prefab, animator, the ground shadow and the dive

**Size:** M · **Depends on:** SS-14a, SS-14b · **Branch:** `ss-14c-vulture-body`
**Design refs:** GD §8.14, §16.4, §3 › The Sunscar, JG-25h (`GravemawView.PassesCover`)

## Goal

`Vulture.prefab` plays SS-14b's clips to core's phases. Aloft only a dark wing-shaped shadow moves on the sand. The lock draws the red-orange lane strip as built, the body drops down it, lands and stands, then climbs away.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Vulture.prefab` | — | `Antlion.prefab`'s component set on `Vulture.fbx`, plus `VultureView` and a shadow instance |
| `Animation/Controllers/AC_Vulture.controller` | — | Glide/Flap aloft, Dive → Strike → Land → Idle_Ground, Climb, Hit, Death |
| `Game/Views/VultureView.cs` | Game | reads `VulturePhaseChanged`/`VultureDiveLocked` for its id; body height, shadow, cover pass |
| `Prefabs/Vfx/FX_Vulture_Shadow.prefab` | — | a dark wing-shaped ground decal, wings slowly beating |
| `Tests/Game/Views/VultureBodyTests.cs` | Tests.Game | view, prefab, controller, lane rows |
| *small edits* | | `EnemyAnimatorView`: opt-in int `VulturePhase` (`ShinePhase`'s guard); `BossViews` only if its `LaneArmed` drawing is gated to bosses (then ungated for any lane, boss rows unchanged) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class VultureView : MonoBehaviour     // block namespace (Traps §5)
    {
        public const float AloftHeight = 6f;
        [SerializeField] private Renderer[] _body;
        [SerializeField] private Transform _model;         // raised and lowered, never the root
        [SerializeField] private Renderer _shadow;         // FX_Vulture_Shadow instance
        [Inject] public void Construct(IEventSource events);
        public void Bind(int enemyId);
        public bool PassesCover { get; }
    }
}
```

## Behaviour

1. **Aloft** (`Circle`, `Lock`): the body renderers, HP bar and status row are hidden; the shadow shows at the agent's XZ, scaled to the 2.6 m span, facing its motion. The hit flash is off.
2. **Climb**: the body shows, plays `Climb` and rises from the ground to `AloftHeight` over the phase, fading out in its last 0.2 s; the shadow fades in and the HP bar hides at once.
3. **Lock**: the lane strip is the one `LaneArmed` already draws (`Palette.Danger`), from the shadow through the hero; nothing else is added. The shadow holds still.
4. **Dive**: the body shows at `AloftHeight` over the lane's start and comes down to the landing over the phase, playing `Dive` then `Strike`. The shadow shrinks into the body.
5. **Grounded**: `Land` then the `Idle_Ground` loop at the landing; the HP bar and status row show; the shadow is off.
6. **Through cover.** In every phase but `Grounded` the body's `CharacterController` excludes the cover layer (`GravemawView`'s `PassesCover` rule), so circling and diving never catch on rocks.
7. The shadow and strip draw above the Sunscar storm (SS-08c's queue rule). The shadow is dark umber at about 0.5 alpha, never cyan, never red-orange.
8. Death on the ground plays `Death`; pooled reuse resets to `Climb` with the shadow off. No allocation per frame; a `MaterialPropertyBlock` per renderer.
9. `AC_Vulture`: `VulturePhase` 0 `Climb`, 1–2 `Glide` with `Flap` on a turn, 3 `Dive` → `Strike`, 4 `Land` → `Idle_Ground`. `Hit` only from the ground states. No write defaults.
10. Every other enemy prefab, controller and view is unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `View_Aloft_HidesBodyShowsShadow` | `Circle` / step / body off, shadow on at the agent's XZ, bar hidden |
| `View_Shadow_FollowsAgent` | circling 2 m / step / shadow at the agent |
| `View_Climb_RisesAndFades` | `Climb` 0.6 s / step to the end / model at `AloftHeight`, body faded, shadow on |
| `View_Dive_DescendsToLanding` | `Dive` 0.4 s / step half, end / model at about half height, then 0 at the landing |
| `View_Grounded_ShowsBodyAndBar` | `Grounded` / step / body on, shadow off, bar shown |
| `View_PassesCoverAloftOnly` | each phase / read / true aloft and diving, false grounded |
| `View_OtherId_Ignored` | another id / publish / unchanged |
| `View_PooledReuse_Resets` | reuse / bind / `Climb`, shadow off |
| `Lane_DrawsForTheVulture` | a non-boss stage, `LaneArmed` from a vulture / step / the strip drawn, `Palette.Danger` |
| `Animator_VulturePhase_Set` | prefab / each phase / int matches |
| `Controller_EveryStateIsTheModels` | controller / read / every motion a `Vulture.fbx` clip; no write defaults |
| `Prefab_WiresTheView` | prefab / load / `VultureView` refs set; `_telegraphGlow` 0 |
| `Effects_DrawOverTheStorm` | shadow vs storm / queue / higher |
| `Shadow_NoCyanOrDanger` | shadow colour / hue / outside both bands |
| `Antlion*View*`, `BossViews*`, `HawkView*` | unchanged / run / green |

## Manual verification (Editor / device)

Not in play until SS-14d, whose steps cover the body.

## Out of scope

- Data, roster, localisation (SS-14d). Any core change. The hawk pet. Sound.

## As built

_Commit body._
