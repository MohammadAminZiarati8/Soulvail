# SS-12c — The body: prefab, animator, the cone and the violet Poison icon

**Size:** M · **Depends on:** SS-12a, SS-12b · **Branch:** `ss-12c-cobra-body`
**Design refs:** GD §8.12, §16.4, §16.5, §3 › The Sunscar

## Goal

`Cobra.prefab` plays SS-12b's clips to core's phases. The rear draws the Mother's red-orange spray fan on the locked cone, the spit throws a short red-orange spray, and the hero's status row shows Poison as a pale-violet drop with its stacks.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Cobra.prefab` | — | `Viper.prefab`'s component set on `Cobra.fbx`, plus `CobraView` and a `VFX_SprayCone` instance |
| `Animation/Controllers/AC_Cobra.controller` | — | Locomotion, Rear, Spit, Hit, Death |
| `Game/Views/CobraView.cs` | Game | reads `CobraConeArmed` and `CobraSpat` for its id; drives the fan and the spray |
| `Prefabs/Vfx/FX_Cobra_Spit.prefab` | — | a short spray down the cone, red-orange |
| `Tests/Game/Views/CobraBodyTests.cs` | Tests.Game | view, prefab, controller, icon rows |
| *small edits* | | `EnemyAnimatorView`: opt-in int `CobraPhase` (`ShinePhase`'s guard); `Data/StatusIconBook.asset`: `status.poisoned` → `T_Status_Poisoned`; `StatusRow`: `PoisonTint` on that icon; `PlayerAnimatorView`, `RangerAnimatorView`, `BeastmasterAnimatorView`, `HapticsListener`: ignore `PlayerDamaged.IsPulse` |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class CobraView : MonoBehaviour     // block namespace (Traps §5)
    {
        [SerializeField] private SprayConeView _cone;
        [SerializeField] private ParticleSystem _spit;     // FX_Cobra_Spit instance
        [Inject] public void Construct(IEventSource events);
        public void Bind(int enemyId);
    }
}
namespace Soulvail.Game.Presentation
{
    // StatusRow: public static readonly Color PoisonTint;  // pale violet, the debuff rim's family
}
```

## Behaviour

1. `CobraConeArmed`: `_cone.Arm(apex, direction, half angle, length, seconds)` as the Mother's spray is armed: the fan clipped by cover, filling over the rear, `Palette.Danger`.
2. `CobraSpat`: the fan stops and the spit spray plays down the locked direction, hit or miss.
3. `AC_Cobra`: Locomotion `Idle`/`Slither` by speed. `CobraPhase` 2 plays `Rear` and holds its last frame; the spat event (phase 3) plays `Spit` then returns to Locomotion. `Hit`, `Death` as `AC_Viper`'s. No write defaults.
4. The fan and spray draw above the Sunscar storm (SS-08c's queue rule). Nothing else on the cobra is red-orange; nothing is cyan, gold or green.
5. Death or pooled reuse stops the fan and spray at once and resets the animator to Locomotion. No allocation per frame.
6. **The icon.** `status.poisoned` shows in the hero's `StatusRow` as the enemy poison drop (`T_Status_Poisoned`), tinted `PoisonTint`: pale violet, outside the cyan, gold, green and lime bands and not Veilrot's `#A855F7`. The stack count shows above 1, the sweep runs with the timer, as built.
7. **No flinch from a pulse.** A `PlayerDamaged` with `IsPulse` plays no flinch on any hero animator and fires no haptic. The HP bar still drops.
8. Every other enemy prefab, controller and view is unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `View_ConeArmed_ArmsTheFan` | event, 30°, 5 m, 0.7 s / step / fan armed at the apex, same numbers |
| `View_Spat_StopsFanPlaysSpray` | armed, then spat / step / fan off, spray playing |
| `View_OtherId_Ignored` | another id / publish / unchanged |
| `View_Death_ClearsEffects` | killed mid-rear / step / fan and spray off |
| `View_PooledReuse_Resets` | reuse / bind / nothing showing, Locomotion |
| `Animator_CobraPhase_Set` | prefab / each phase / int matches |
| `Controller_EveryStateIsTheModels` | controller / read / every motion a `Cobra.fbx` clip; no write defaults |
| `Prefab_WiresTheView` | prefab / load / `CobraView` refs set; `_telegraphGlow` 0 |
| `Effects_DrawOverTheStorm` | fan, spray vs storm / queue / higher |
| `StatusRow_PoisonIconIsVioletDrop` | hero with 2 stacks / step the row / drop sprite, `PoisonTint`, "2" shown |
| `PoisonTint_OutsideReservedBands` | tint / hue / not cyan, gold, green, lime or `#A855F7` |
| `Pulse_NoFlinchNoHaptic` | `PlayerDamaged` pulse, then a hit / publish / no flinch or haptic, then one of each |
| `Viper*Body*`, `StatusRow*`, `EnemyStatusRow*` | unchanged / run / green |

## Manual verification (Editor / device)

Not in play until SS-12d, whose steps cover the body and the icon.

## Out of scope

- Data, roster, localisation (SS-12d). Any core change. Poison bubbles on the hero's body. Sound.

## As built

_Commit body._
