# JG-20g — Thornheart's body: tells, the lit heart, ripening pods, the strangling ring

**Size:** M · **Depends on:** JG-20d, JG-20f · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 7 (read on a 6-inch screen), §16.4; AR §18.2 (animation never gates a damage frame); colour law

## Goal

What core decides about Thornheart, drawn: a clip per move, a heart that glows only while it takes
double damage, a ripening ring on every pod, and the thorn ring closing in.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ThornheartView.cs` | Game | **New.** `MonoBehaviour`; subscribes to Thornheart's events |
| `Animation/Controllers/AC_Thornheart.controller` | — | **New.** The seven clips of 20f |
| `Prefabs/Vfx/FX_StrangleRing.prefab` | — | **New.** A bramble band drawn round a radius |
| `Prefabs/Vfx/FX_PodRipen.prefab` | — | **New.** A ground ring that fills over the ripen seconds |
| `Tests/Game/Views/ThornheartViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Thornheart.prefab` gains `ThornheartView`, the controller and the VFX refs |

## Public API

```csharp
namespace Soulvail.Game.Views { public sealed class ThornheartView : MonoBehaviour {
  [SerializeField] private Renderer _heart; [SerializeField] private GameObject _ringPrefab, _ripenPrefab;
  public bool IsHeartLit { get; }  public float RingRadiusShown { get; } } }
```

## Behaviour

1. **Moves:** `EnemyTelegraph` kinds 1 / 2 / 3 play `Roots` / `Slam` / `Plant`; the walls' strips
   are 20e's. The animation never gates damage.
2. **Heart:** `HeartExposed(Open)` plays `Exposed` and lights `_heart`'s emission (warm, never cyan)
   for its seconds; closing puts it out.
3. **Pods:** `PodPlanted` places a ripen ring at the pod, filling in `Palette.Danger` over
   `RipenSeconds`; removed when that pod despawns, killed or ripened.
4. **Ring:** `StrangleRing` draws the band at `FromRadius`, shrinking to `ToRadius` over `Seconds`;
   the band's inner edge is exactly the core radius. Removed on the boss's despawn or `Reset`.
5. `Hit` and `Death` from the existing hit/death events. Pooled; all reset on despawn.

## Tests

| Test | Given / When / Then |
|---|---|
| `View_TelegraphKinds_PlayClips` (1) |
| `View_HeartExposed_LightsForSeconds_NotCyan` (2) |
| `View_PodPlanted_RingFills_RemovedOnDespawn` (3) |
| `View_StrangleRing_ShrinksToRadius` / `View_Despawn_DropsRing` (4) |
| `View_Despawn_ResetsAll` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Played in JG-20h: the heart glows only after the slam; pod rings read at phone
   size; the thorn band's edge is where the hurting starts.

## Out of scope

- Audio (none exists yet); the stage (20h).

## As built

_Filled at merge._
