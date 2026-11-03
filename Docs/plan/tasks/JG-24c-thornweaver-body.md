# JG-24c — The Thornweaver's body: the cast and the root it sends

**Size:** M · **Depends on:** JG-24a, JG-24b · **Branch:** `dev` (direct)
**Design refs:** GD §8.2; AR §18.2; memory "readable at first look"

## Goal

A player sees *which* enemy is planting the wall: the weaver casts, its crown glows, and a root
line runs along the ground from it to the warned strip.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ThornweaverView.cs` | Game | **New.** Cast trigger, crown glow, root line |
| `Prefabs/Enemies/Thornweaver.prefab` | — | **New.** `Thornweaver.fbx`, `ThornweaverView`, root `LineRenderer` child |
| `Animation/Controllers/AC_Thornweaver.controller` | — | **New.** Idle/Walk blend, Cast, Hit, Death |
| `Materials/Enemies/M_ThornRoot.mat` | — | **New.** Unlit, moss green, from the thorn wall's palette |
| `Tests/Game/Views/ThornweaverViewTests.cs` | Tests.Game | **New.** Rules 1–5 |
| *small edits* | | none: `ThornArmed.OwnerId` comes from JG-24a; `ThornWallViews` unchanged |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class ThornweaverView : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _crown;
        [SerializeField] private LineRenderer _root;
        public void Bind(int enemyId, IEventHub hub);
    }
}
```

## Behaviour

1. **`EnemyTelegraph` for this id plays `Cast`** once and ramps the crown's emission to full over
   the telegraph's duration; other ids are ignored.
2. **`ThornArmed` whose `OwnerId` is this id** draws the root: a line on the floor (JG-13's decal
   lift) from the weaver's feet to the wall's midpoint, growing over 0.25 s, gone when the wall
   stands. The crown drops back.
3. **Not a second warning**: the warned strip stays JG-20e's; the root is moss green, never
   red-orange.
4. **Death mid-cast** stops the clip and the glow; a root already drawn fades with its wall.
5. **Reset**: despawn clears the line, the glow and the animator.

## Tests

| Test | Given / When / Then |
|---|---|
| `Telegraph_PlaysCastAndGlows` | own id; other id / `Cast`, glow 1 at end; nothing (1) |
| `ThornArmed_OwnDrawsRoot` | own wall; Thornheart's wall / line to midpoint; none (2) |
| `Root_IsNotADangerColour` | material / moss green, no red-orange (3) |
| `Death_MidCastStopsGlow` | die at half cast / glow 0 (4) |
| `Despawn_ResetsEverything` | despawn with a root / line off, glow 0 (5) |

## Manual verification (Editor / device)

1. **[Editor]** A weaver at 10 m raises its arms, a root crawls to a strip beyond the Ranger, the
   wall rises. With two weavers, the player can tell whose wall is whose.

## Out of scope

- Asset and stage (JG-24d).

## As built

_Filled at merge._
