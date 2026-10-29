# JG-12f — The spray and the lobs are drawn

**Size:** M · **Depends on:** JG-12c (the events) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 6, 7; GD §16.4

## Goal

The spray's wedge lies on the floor for its wind-up, filling toward the edge, with the stones'
shadows cut out of it. Every barrage lob marks where it will land for its flight.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/SprayConeView.cs` | Game | **New.** A fan mesh; `Arm`, `Fire`, `Clear`, `Step` |
| `Prefabs/VFX/VFX_SprayCone.prefab` | — | **New.** The fan on `M_TelegraphRing` (danger red-orange) |
| `Tests/Game/Views/SprayConeViewTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | | `Game/Views/BossViews.cs` (a pooled cone per spray event); `Game/Views/TelegraphRings.cs` (rule 5); `Core/Events/ProjectileEvents.cs` (`ProjectileFired.Radius`, optional last); `Core/Combat/ProjectileSystem.cs` (publishes it); `Game/Authoring/EnemyLook.cs` (`MarksLanding`); `Game/Authoring/EnemyDefinition.cs` (`_marksLanding`); `Game/Composition/RunScope.cs` and `Scenes/Run.unity` (the prefab) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class SprayConeView : MonoBehaviour, IPoolable
    {
        public const int Rays = 32;
        public const float FlashSeconds = 0.15f;
        public float Fill { get; }              // 0 → 1 over the wind-up
        public float RayLength(int i);          // Range, or less where cover cuts it
        public bool IsLive { get; }
        public void Arm(Vector3 origin, Vector2 directionXZ, float halfAngleDeg, float range, float seconds);
        public void Fire();
        public void Clear();
        public void Step(float dt);
    }
}
// EnemyLook: `bool marksLanding = false` appended; `public bool MarksLanding { get; }`
```

## Behaviour

1. **The wedge is the bite.** `Arm` lays `Rays` rays from the origin, flat on the floor, spanning
   ±`halfAngleDeg` round the direction, each `range` long. The outline shows at once.
2. **Cover casts a safe shadow.** Each ray stops at its first hit on the `Cover` layer, cast at
   `LineOfSightSense.EyeHeight`, the height core's sight line uses. Asked once, at `Arm`.
3. **It counts down.** `Fill` grows 0 → 1 over `seconds`, the fill reaching the rim at the bite.
4. **It ends.** `Fire` flashes for `FlashSeconds`, then hides; `Clear` hides at once. Pooled clean.
5. **Marked lobs.** For a `ProjectileFired` whose spec's look `MarksLanding`, `TelegraphRings` binds
   a filling ring at the target, the shot's `Radius` wide, for its `FlightTime`. Other shots (the
   Pitcher's, the player's) are unmarked, as today.
6. **`BossViews` drives it:** `SprayArmed` arms one, `SprayFired` fires it, `SprayCleared` clears it,
   stepped on the snapshot's `dt` (AR §18.1).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Rule |
|---|---|
| `Arm_SpansTheWedge_FlatOnTheFloor` | 1 |
| `Arm_CoverCutsTheRaysBehindIt` (a box on `Cover` in the wedge) | 2 |
| `Step_FillsOverTheWindup` | 3 |
| `Fire_FlashesThenHides` / `Clear_HidesAtOnce` / `Despawn_ThenArm_DrawsOnlyTheNew` | 4 |
| `Rings_MarkedLob_GetsALandingRing` / `Rings_UnmarkedLob_GetsNone` | 5 |
| `BossViews_SprayEvents_DriveACone` | 6 |
| `Prefab_IsDressed` (Traps §5) | 1 |

## Manual verification (Editor / device)

1. **[Editor]** At JG-12h, at 411 × 914: the wedge reads on a 6-inch frame, the shadows behind
   stones are plainly clear, and each lob's ring sits where it lands.

## Out of scope

- A spray particle or goo stream (the flash is the bite). Marking the Pitcher's lobs.

## As built

_Filled at merge._
