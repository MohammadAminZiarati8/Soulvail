# JG-16d — The Moss Shaman's body

**Size:** M · **Depends on:** JG-16b, JG-16c · **Branch:** `dev` (direct)
**Design refs:** GD §16.2, GD §16.4; AR §18.2

## Goal

`Shaman.prefab`: the model with its staff, an animator driven by core's events, a pale-lime aura ring
at each pulse, healed bars that rise, and a haste mark on hastened enemies.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ShamanView.cs` | Game | **New.** Cast trigger, seed glow, pulse ring |
| `Prefabs/Enemies/Shaman.prefab` | — | **New.** `Shaman.fbx`, staff on `handslot.r`, `ShamanView`, ring child |
| `Art/Enemies/Shaman.controller` | — | **New.** Idle/Walk/Run blend, Cast, Hit, Death |
| `Art/Materials/M_ShamanRing.mat` | — | **New.** Unlit additive pale lime, from the decal shader |
| `Tests/Game/Views/ShamanViewTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | | `EnemyHealthBar` (listens to `EnemyHealed`); `EnemyStatusView` (`Hasted` mark); `EnemyAnimatorView` (run above 1.2× base speed) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class ShamanView : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _seed;
        [SerializeField] private Transform _ring;
        public void Bind(int enemyId, IEventHub hub);
    }
}
```

## Behaviour

1. **`ShamanCast` for this id plays `Cast`** once and ramps the seed's emission to full over the
   cast's seconds; other ids are ignored.
2. **`ShamanPulsed` grows the ring** from 0 to the event's radius over 0.4 s, fading out, flat on
   the floor above the stone (JG-13's decal lift); the seed drops back.
3. **Not a danger telegraph**: no `EnemyTelegraph`, nothing red-orange, no warning glyph.
4. **`EnemyHealed` raises the bar** to its fraction at once, and the damage ghost follows it up.
5. **`Hasted` shows a lime chevron** over the bar while it lasts and clears when it ends.
6. **Running**: move speed above 1.2× its base plays `Run`, so fleeing and haste read as running.
7. **Pooled cleanly**: despawn mid-cast or mid-ring leaves no ring, glow or subscription.

## Tests

| Test | Given / When / Then |
|---|---|
| `ShamanView_CastPlaysForItsOwnId` | two ids / cast for one / one trigger (1) |
| `ShamanView_PulseGrowsTheRingToTheRadius` | radius 8 / 0.4 s / scale 8, alpha 0 (2) |
| `ShamanView_PulseIsNoTelegraph` | pulse / no `EnemyTelegraph`, no reserved colour (3) |
| `HealthBar_RisesOnEnemyHealed` | 0.5 → healed 0.6 / fill and ghost 0.6 (4) |
| `StatusView_ShowsAndClearsHaste` | Hasted 3.5 s, then 0 / mark on, off (5) |
| `AnimatorView_RunsAboveOnePointTwo` | speed 1.4× / `Run` (6) |
| `ShamanView_DespawnMidCastLeavesNothing` | despawn / ring off, glow 0, unsubscribed (7) |

## Manual verification (Editor / device)

1. **[Editor]** Sandbox: a Shaman behind a Mossguard raises its staff, the seed brightens, a lime
   ring rolls out, the Mossguard's bar ticks up and it shows a chevron and runs.
2. **[Editor]** Walk in: it breaks the cast and runs. The owner judges the read from the camera.

## Out of scope

- The asset and stage 17 (JG-16e). Audio. Tethers or heal beams: the ring and the bars carry it.

## As built

_Filled at merge._
