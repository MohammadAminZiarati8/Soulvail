# SS-09d — The body: prefab, animator, the white shine and the glass bolt

**Size:** M · **Depends on:** SS-09a, SS-09b, SS-09c · **Branch:** `ss-09d-glassback-body`
**Design refs:** GD §8.9, §16.4

## Goal

`Glassback.prefab` plays SS-09c's clips to core's beats. Its shell brightens white over the glint, blazes through the shine and goes dark when it opens. Each reflected bolt flies as a pale glass shard into the lob ring as built.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Glassback.prefab` | — | `Beetle.prefab`'s component set on `Glassback.fbx`, plus the shine view |
| `Animation/Controllers/AC_Glassback.controller` | — | Locomotion, Bite, Glint → Shine → Open, Hit, Death |
| `Game/Views/EnemyShineView.cs` | Game | reads `EnemyShineChanged` for its id, drives the shell overlay |
| `Materials/Enemies/M_GlassbackShine.mat` | — | additive white rim, `_Intensity` property |
| `Prefabs/Projectiles/GlassBolt.prefab` | — | pale shard, the bolt's mesh |
| `Tests/Game/Views/GlassbackBodyTests.cs` | Tests.Game | view, prefab, controller, bolt rows |
| *small edits* | | `EnemyAnimatorView`: opt-in int `ShinePhase` from `EnemyShineChanged`; the view that draws `ProjectileFired` by spec id gets the glassback → `GlassBolt` entry, as the beetle's glob |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class EnemyShineView : MonoBehaviour   // block namespace (Traps §5)
    {
        [SerializeField] private Renderer _overlay;       // a second SkinnedMeshRenderer on the same bones, M_GlassbackShine
        [Inject] public void Construct(IEventSource events); // as the other enemy views subscribe
        public void Bind(int enemyId);
    }
}
```

## Behaviour

1. `EnemyShineView`: `None` → overlay disabled. `Glint` → enabled, `_Intensity` ramps 0 → 0.6 over the glint's seconds. `Shine` → 1.0 with a slight 6 Hz shimmer (±0.1). `Open` → disabled. It runs only for its own id, and pooled reuse resets it to `None`.
2. A `MaterialPropertyBlock` per renderer, so the shared material never changes. No allocation per frame.
3. `EnemyAnimatorView`'s `ShinePhase` int (0–3) is set only where the controller has it (`AimDropped`'s guard). `Forget` sets it to 0.
4. `AC_Glassback`: Locomotion `Idle`/`Walk` by speed. The Attack trigger plays `Bite`. `ShinePhase` 1 plays `Glint`, 2 `Shine` (loop), 3 `Open`, and 0 returns to Locomotion. `Hit`, `Death` as `AC_Beetle`'s. No write defaults.
5. The prefab: `EnemyHitFeedback` with `_telegraphGlow` 0. The overlay child shares the body's bones and mesh. The model is unscaled (SS-09c sized it).
6. `GlassBolt`: an elongated pale ivory shard about 0.5 m long, unlit-bright so it reads on sand. Never cyan, and nothing red-orange on the shard. The landing ring is the lob ring as built (`Palette.Danger`). Both draw above the Sunscar storm, raising the queue as SS-08c did if needed.
7. `Beetle.prefab`, `AC_Beetle` and every other enemy view are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Shine_None_OverlayOff` | view, `None` / step / overlay disabled |
| `Shine_Glint_Ramps` | `Glint` 0.5 / half / intensity ≈ 0.3 |
| `Shine_Shine_Blazes` | `Shine` / sample / 0.9–1.1 |
| `Shine_Open_Off` | `Open` / step / disabled |
| `Shine_OtherId_Ignored` | another id / publish / unchanged |
| `Shine_SharedMaterialUntouched` | full cycle / read asset / unchanged |
| `Animator_ShinePhase_Set` | Glassback prefab / each phase / int matches |
| `Animator_OtherController_Untouched` | Beetle prefab / `EnemyShineChanged` / no error |
| `Controller_ChainsGlintShineOpen` | controller / read / rule 4 |
| `Controller_EveryStateIsTheModels` | controller / read / every motion a `Glassback.fbx` clip; no write defaults |
| `Prefab_WiresTheViews` | prefab / load / rule 5 |
| `Bolt_MapsToGlassBolt` | glassback spec id / resolve / `GlassBolt` |
| `Bolt_DrawsOverTheStorm` | bolt and ring materials vs storm / queue / higher |

## Manual verification (Editor / device)

Not in play until SS-09e, whose steps cover the body.

## Out of scope

- Data, roster, localisation (SS-09e). Any core change. Sound.

## As built

_Commit body._
