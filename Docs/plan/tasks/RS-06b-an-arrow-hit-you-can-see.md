# RS-06b — An arrow hit you can see

**Size:** M · **Depends on:** RS-02c (a pool per prefab), RS-03b (the volley), M7-05h (the Rootling's hit flash) · **Branch:** `rs-06b-an-arrow-hit-you-can-see`
**Design refs:** GD §11.3, GD §16.4, GD §17.1; AR §18.1, AR §18.4 · **Ledger rows:** none

The owner's go of 2026-09-28: *"we need a cool effect when enemies get shot"*. The arrow stays as
it is. The Rootling's white hit flash (M7-05h) stays too, and this adds to it.

## Goal

When an arrow hits an enemy, a short white impact plays where it landed: a star, a spray of shards
thrown along the arrow's travel, and a thin ring. It is gone in 0.28 s. It is pooled and prewarmed,
and its hit path allocates nothing.

## Why the arrow names its impact, and the census plays it

- **`ProjectileViews` already knows which prefab flew each id** (RS-02c) and already hears
  `ProjectileImpacted`. A second listener would have to rebuild that table. So the shot prefab
  names its impact (`ProjectileView._impact`), and the census pools it beside the shot's own bodies.
  A Spitter's glob can name one later with no code.
- **Every pool is built while the scene loads.** For each class look that names a shot, the shot's
  pool and its impact's pool are prewarmed at construction. This also retires RS-02c's *"a class's
  pool is built on its first shot"*, which instantiated arrows during the first volley of a wave.
- **One scripted mesh, not a `ParticleSystem`.** Every other VFX here is a scripted view stepped on
  the snapshot's clock (AR §18.1). One dynamic mesh with one opaque, vertex-coloured, unlit material
  is one draw call per hit and adds no transparent layer (GD §11.3). A particle system would run on
  its own clock.
- **It faces the camera and stands in front of the body.** An arrow lands at its target's feet, so
  the impact is raised onto the body and pulled toward the camera, and the star and the ring face it.
  `RunScope` hands the census its camera.
- **A miss plays nothing.** A puff at a miss tells the player nothing they need, and it is one more
  transparent-looking thing on the floor.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ImpactView.cs` | Game | **New.** The impact (rules 6–8) |
| `Game/Views/ProjectileViews.cs` | Game | impact pools, live impacts, prewarm (rules 1–5) |
| `Prefabs/Vfx/VFX_ArrowImpact.prefab`, `Materials/M_Impact.mat` | — | **New.** The impact, and its opaque vertex-colour material on URP `Particles/Unlit` (rule 6) |
| `Tests/Game/Views/ImpactViewTests.cs` | Tests.Game | **New.** Rules 1, 4, 6, 7 |
| *small edits* | | `Game/Views/ProjectileView.cs` (`_impact`, `Impact`, `Travel`); `Game/Authoring/CharacterLook.cs` (`CharacterLookBook.All`); `Game/Composition/RunScope.cs` (`ClassShotPrewarm`, and passes `classPrewarm` and `viewer`); `Prefabs/Projectiles/Arrow.prefab` (names the impact); `Tests/Game/Views/ProjectileViewsTests.cs` (rules 2–5, 8) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class ImpactView : MonoBehaviour, IPoolable
    {
        public const int ShardCount = 6;
        public static readonly Quaternion Overhead;       // a camera looking straight down
        public bool IsLive { get; }
        public float Age { get; }
        public float Lifetime { get; }                     // derived: its longest part
        public float StarScale { get; }
        public float RingRadius { get; }
        public float RingWidth { get; }
        public void Play(Vector3 landing, Vector3 travel, Quaternion viewer, int seed);
        public void Step(float dt);
        public Vector3 ShardOffset(int index);
        public float ShardScale(int index);
    }

    // ProjectileView
    public ImpactView Impact { get; }
    public Vector3 Travel { get; }                         // target − origin; zero in the pool
}

// ProjectileViews: optional and last, and RunScope passes both (Traps §6)
public ProjectileViews(IObjectResolver resolver, ProjectileView prefab, Transform parent, DomainEventHub hub,
                       int prewarm = 0, CharacterLookBook looks = null, int classPrewarm = 0, Transform viewer = null);
public int ImpactCount { get; }
public ImpactView ImpactAt(int index);
public int PooledImpactCountOf(ImpactView prefab);

// CharacterLookBook
public IReadOnlyCollection<CharacterLook> All { get; }
```

## Behaviour

1. **A shot's prefab names its impact.** `Arrow.prefab` names `VFX_ArrowImpact`. `Projectile.prefab`
   names none, and that is every enemy's shot and every other class's.
2. **A hit plays it, and a miss plays nothing.** On a `ProjectileImpacted` whose `Hit` is true, the
   census rents an impact from the pool of the prefab that flew that id. It plays it at `Position`,
   thrown along that flight's travel and seeded by the shot's id. A miss, a hit by a shot that names
   no impact, and a hit for an id with no body play nothing.
3. **Nothing is built mid-wave.** At construction, each class look's shot pool, and the impact pool
   of every shot that names one, is built and prewarmed to `classPrewarm`. `RunScope` passes
   `2 × VolleySpec.MaxArrows`, 14. A pool that runs dry grows by one `Instantiate`.
4. **An impact goes out on the snapshot's clock and goes home.** `Step` advances every live impact
   with the bolts' `dt` and returns each one to its own pool when it goes out. `Dispose` returns and
   destroys them all. A returned impact forgets its play (AR §18.4).
5. **The hit path allocates nothing** once the pools are warm.
6. **What it looks like.** It is centred 0.6 m above the landing point and pulled 0.6 m toward the
   camera. A white eight-point star faces the camera, its long spike along the travel as the camera
   sees it, and pops and goes out in 0.1 s. Six shards leave within 50° either side of the travel,
   rise, fall, tumble and shrink over 0.24 s ± 15 %. A thin white ring races out to 0.75 m in 0.14 s.
   All of it is gone by `Lifetime`, 0.276 s. One mesh, one opaque material and no shadow: one draw
   call per hit and no transparent layer.
7. **Colours.** Every vertex is a shade of white, except one face of each shard, which is the
   player's cyan: a glint as it tumbles. There is no red-orange, gold or violet (GD §16.4).
8. **The viewer.** `RunScope` hands the census the run's camera. With none, as in the sandbox or a
   fixture, the impact is seen from overhead and stands straight above the landing point.

## Tests

| Test | Given / When / Then |
|---|---|
| `ImpactViewTests.Shipped_TheArrowNamesItsImpactAndTheBoltNone` | the two shot prefabs / read / the arrow names `VFX_ArrowImpact`; the bolt none (rule 1) |
| `ProjectileViewsTests.Hit_PlaysTheShotsImpactWhereItLanded` | an arrow that names an impact, no viewer / shot, then hit / one impact above the landing point, shards along +X, the arrow's body home (rules 2, 8) |
| `ProjectileViewsTests.Miss_PlaysNothing` | a miss, a hit by the bolt, a hit nobody fired / published / no impact, none rented (rule 2) |
| `ProjectileViewsTests.Prewarm_EveryClassShotAndItsImpactAreBuiltUpFront` | `classPrewarm` 8 / four volleys of three / 8 arrows and 8 impacts exist before and after each (rule 3) |
| `ProjectileViewsTests.Impact_IsSteppedAndReturnedWhenItGoesOut` | a hit / 0.1 s, then 0.5 s / age 0.1 and live; then home in its own pool (rule 4) |
| `ProjectileViewsTests.Dispose_ReturnsAndDestroysEveryImpact` | a live impact / disposed / returned, then destroyed (rule 4) |
| `ImpactViewTests.OnDespawn_ForgetsThePlay` | a playing impact / returned / not live, age 0, at rest, nothing drawn (rule 4) |
| `ProjectileViewsTests.Hit_AllocatesNothing` | warm pools / a hit and a step, 200 times / `AllocationAssert.None` (rule 5) |
| `ImpactViewTests.Play_CentresOnTheBodyInFrontOfIt` | the follow camera's pose / played at (2, 0, 3) / 0.6 m up, 0.6 m toward the camera (rule 6) |
| `ImpactViewTests.Play_ThrowsTheShardsAlongTheTravel` | three travels / 0.08 s / every shard ahead of the travel and inside 50° of it (rule 6) |
| `ImpactViewTests.Play_TheSameShotRepeatsAndTheNextDiffers` | seeds 11, 11 and 12 / 0.05 s / the same shard twice; a different one (rule 6) |
| `ImpactViewTests.Step_TheStarGoesOutFirstAndTheHitIsGoneWithinItsLifetime` | a hit / stepped at 60 fps / the star lit at once and out by 0.13 s while the rest goes on; all out within `Lifetime` ≤ 0.35 s; nothing drawn (rule 6) |
| `ImpactViewTests.Step_ABadStepDoesNothing` | a playing impact / 0, −0.1, NaN, ∞ / age unchanged (rule 6) |
| `ImpactViewTests.Shipped_TheImpactIsOneOpaqueDrawCall` | `VFX_ArrowImpact.prefab` / read / one renderer, `M_Impact` on `Particles/Unlit`, opaque, below the transparent queue, no shadow, drawn with its own filter (rule 6) |
| `ImpactViewTests.Shipped_TheImpactIsQuickAndThin` | the prefab's numbers / read / `Lifetime` ≤ 0.35 s, ring ≤ 0.1 m wide (rules 6, 7) |
| `ImpactViewTests.Mesh_IsWhiteWithACyanGlintOnEachShard` | a played impact / its colours / every vertex a shade of white or the player's cyan, none danger; one cyan face per shard; the ring thin (rule 7) |
| `ProjectileViewsTests.Hit_FacesAndLeansTowardTheViewer` | a viewer at 57° / a hit / pulled toward it, not sideways (rule 8) |
| *(extended)* `ProjectileViewsTests.Constructor_NullDependency_Throws` | `classPrewarm` −1 / built / thrown |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → the Ranger → descend, and stand still in reach of a Rootling.
   *Expected:* each arrow that lands on it pops a white star on its body, with the long spike pointing
   the way the arrow flew, together with the Rootling's own white flash. A few pale shards with cyan
   glints spray on past it and fall, and a thin white ring races out. All of it is gone in about a
   quarter of a second, before the next arrow lands.
2. **[Editor]** Take Volley and let a fan land. *Expected:* a separate burst on each arrow that hits,
   each a little different.
3. **[Editor]** Loose at a Rootling walking across the shot. *Expected:* where an arrow misses,
   nothing.
4. **[device]** The star's size at phone scale, and frame time with a volley's three hits.

## Out of scope

- A puff on a miss (decided: nothing), and an impact for any other shot.
- The arrow, `EnemyHitFeedback`, a hit sound, screen shake.

## As built

**As specified, with one change made after seeing it.** The first build drew the ring in the
player's cyan, at 0.85 m for 0.18 s. Rendered from the follow camera, a cyan circle round an enemy
for 0.15 s read as the targeting reticle, and it was the cyan the owner had just had removed. The
ring became white and shorter (0.75 m, 0.14 s, 0.07 m wide), and the cyan moved to one face of each
shard. The same render showed the shards as specks by 0.12 s: at 16 m and 60° a Rootling is about
45 px tall at 1080p. So the shards grew from 0.3 × 0.09 m at 6.5 m/s to 0.4 × 0.13 m at 7.5 m/s, and
the star from 0.9 to 1.05 m. Rules 6 and 7 state the tuned numbers.

**The sandbox draws impacts too.** `Arrow.prefab` is its default shot, and its loop publishes hits
on the dummies. It passes no viewer, so they are seen from overhead, and no `classPrewarm`, so the
pool is built on the first hit. The sandbox is not a run.

**The allocation row first failed on the fixture, not the hit.** Three hundred arrows fired up front
and returned one per iteration doubled the arrow pool's inactive stack during the measurement:
Traps §7's recorder growth. A scratch fixture measured `Play`, `Step`, `Mesh.SetVertices`, the
transform write and `OnDespawn` apart, all at zero, and was deleted before any tally. Every arrow
now comes home once before `Hit_AllocatesNothing` measures.

**`ImpactView.cs` did not join `Soulvail.Game`'s compile until the Editor restarted.** It was written
while the branch switch's compile was running. It imported as a `MonoScript`, yet `Refresh`, a forced
import and `RequestScriptCompilation(CleanBuildCache)` all regenerated the rsp without it, and
`csc` over that rsp plus the file compiled clean. A restart fixed it at once, which is Traps §5's
M1-01 entry. The Editor was later lost with the session and relaunched only after `csc` over Unity's
own rsp showed both assemblies compiling, so no Safe Mode prompt could block it.

**Red-checked.** With `evt.Hit` dropped from the guard and one `new int[1]` planted in `Play`, the two
fixtures went 44 / 42: exactly `Miss_PlaysNothing` and `Hit_AllocatesNothing`.

**Verified.** EditMode 3 456 — 3 455 passed, 0 failed, 1 inconclusive (the animator clock row) —
+17 on RS-05d's 3 439, taken before the stacking below. PlayMode: the first full pass went 65 / 67,
on `Loop_ShootsOnlyStandingStill` (Traps §8) and on `Run_WearsTheBodyItsClassNames("character.ranger")`,
which failed on the AI Assistant's *NoSubscription* log (Known issue 5). The two fixtures alone went
32 / 32, and the next full pass 67 / 67. Console: no compile errors; the errors a pass leaves are the
ones its rows provoke. Format check clean on the seven C# files. `ProjectSettings/TimeManager.asset`
reverted.

**Stacked on RS-06a after the owner's first play.** Built from `dev` beside RS-06a, this branch
was rebased onto it when the owner played it and still saw the wedge RS-06a removes. RS-06c
stacks on it, and its *As built* holds the suite over all three.

**Rendered** into `Soulvail_Renders/`, beside the repo: `rs06b_single_*` and `rs06b_volley_*`,
1920 × 1080 from the follow camera's pose (57°, 16 m, 60° field of view) in the Hollow, with a 3×
zoom sheet of each. They are edit-mode renders: the Rootling's white flash is a property block held
for 0.08 s, and the Editor draws with the PC pipeline asset, not the mobile one.
