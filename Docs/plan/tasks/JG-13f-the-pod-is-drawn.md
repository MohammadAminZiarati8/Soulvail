# JG-13f — The pod is drawn

**Size:** M · **Depends on:** JG-13e (the events) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 7; GD §16.4; JG-12f rules 5–6

## Goal

The pod reads: a green seed pod arcs out with a landing ring like the barrage's, sits on the ground
swelling for a second, and is gone when its Frog appears.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Projectiles/PitcherSpit_Pod.prefab` | — | variant of `PitcherSpit.prefab`: a ~0.5 m green bulb |
| `Prefabs/Vfx/VFX_SeedPod.prefab` | — | the planted pod, with a `SeedPodView` |
| `Materials/M_SeedPod.mat` | — | URP Lit in Mother's body green |
| `Game/Views/SeedPodView.cs` | Game | swells and wobbles over the hatch time |
| `Game/Views/BossViews.cs` | Game | rents, steps and returns pods |
| `Tests/Game/Views/SeedPodViewTests.cs` | Tests.Game | rules 1–5 |
| *small edits* | | `Data/Enemies/Frog.asset` (`_projectile` the pod, `_marksLanding` on); `Game/Composition/RunScope.cs` + `Scenes/Run.unity` (`_seedPodPrefab`) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class SeedPodView : MonoBehaviour
    {
        public const float StartScale = 0.7f;
        public void Plant(Vector3 at, float seconds);
        public void Step(float dt);
        public float Swell { get; }          // 0 → 1 over seconds
        public void Hide();
    }
}
```

## Behaviour

1. **It flies as a pod.** A shot of the Frog's spec id draws `PitcherSpit_Pod.prefab`, and since the
   Frog's look `MarksLanding`, a ring fills at its target for its flight, the shot's radius wide. The
   Pitcher's spit and the player's shots are unchanged. The Frog itself fires nothing (standoff 0).
2. **It sits and swells.** `PodPlanted` rents a `VFX_SeedPod` at `At`; over `Seconds` it scales
   `StartScale` → 1 with a wobble.
3. **It is gone when the Frog comes.** `PodHatched` returns it; its boss's `EnemyDied` or
   `EnemyDespawned` returns that boss's pods; `StageArrived` returns all. Returned pods are reset.
4. **Stepped like the spray.** `BossViews` steps pods on the snapshot's `dt` (AR §18.1); disposal
   unsubscribes and destroys the pool.
5. **Colour law.** `M_SeedPod` is Mother's body green (her model's albedo swatch), no reserved colour
   (GD §16.4); the ring is the danger read.

## Tests

| Test | Given / When / Then |
|---|---|
| `Frog_FliesAPodAndMarksItsLanding` | Frog look, Pitcher look / read / Frog: `PitcherSpit_Pod`, `MarksLanding`, standoff 0; Pitcher unchanged |
| `Pod_SwellsOverTheHatch` | `PodPlanted` 1 s / step 0, 0.5, 1 s / active at `At`, `Swell` 0, 0.5, 1; scale 0.7 → 1 |
| `Pod_GoesOnHatchDeathAndArrival` | three pods, two bosses / `PodHatched`, one boss's `EnemyDied`, `StageArrived` / returned as each says, reset |
| `BossViews_DisposeUnsubscribesPods` | disposed / publish `PodPlanted` / nothing rented, pool destroyed |
| `SeedPod_WearsMothersGreen` | `M_SeedPod` / compare / within the swatch's tolerance; no reserved colour |

## Manual verification (Editor / device)

1. **[Editor]** Test start 10: a pod arcs with a filling ring, lands clear of stones and the player,
   swells ~1 s, and a Frog stands where it was; standing in the ring at landing hurts.
2. **[device]** Pod and ring read on a 6-inch screen.

## Out of scope

- A hatch burst VFX or sound; the Frog's spawn animation; pods for any other enemy.

## As built

2026-10-30. Built the Files table, including Unity-generated metas. Frog look now selects the
PitcherSpit variant and landing ring without enabling a Frog ranged attack. The flight has a 0.5 m
green bulb and no spit trail/impact; Pitcher/player rendering is unchanged. SeedPodView grows
0.7 → 1 with a damped wobble on snapshot dt. BossViews prewarms three pods, returns by hatch id,
boss death/despawn or stage arrival, resets rentals and unsubscribes/destroys on disposal. Run binds
the planted prefab. M_SeedPod samples the centre of Mother's dominant croak_moss body swatch
(atlas cell 7,3), rather than her chartreuse rim. No scope deviations.

Verified: EditMode total 4 704, passed 4 703, failed 0 (names: none), inconclusive 1 (known).
All five spec tests pass. Compiler errors 0, new analyzer warnings 0; final Console errors/warnings 0;
dotnet format and git diff --check clean. Rendered flight/landing-ring and planted swelling states
checked via MCP. Manual step 1's live stage-10 timing/damage check and step 2's device readability
remain owed; no device available.
