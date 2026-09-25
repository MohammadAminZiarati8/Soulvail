# M7-03c — A song you can see, and a ring with its gaps drawn

**Size:** M · **Depends on:** M7-03b · **Branch:** `m7-03c-a-song-you-can-see`
**Design refs:** GD §9.1 (rules 1, 2, 7), §9.2, §11.3, §16.4, §17.1; AR §7, §18.1, §18.2, §18.4 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m7) — GD §9.1 rule 7 for her, deferred with the rest (step 4)

## Goal

Before the Choirmother sings, a red-orange wedge fills across the floor, and the ground behind each
pillar is cut out of it. So the safe place is drawn, not inferred. Every shield wears a steel plate
turned outward and a steel arc on the floor across what it covers, so a gap in the ring on screen is
the gap in the guard.

## What core already says, and who has been waiting for it

Every fact this task draws is already published, and nothing reads any of it yet:

- **`SonicConeTelegraphed`**, **`SonicConeReleased`** and **`SonicConeWithdrawn`**
  ([M7-03b](M7-03b-the-choirmother.md) rule 4) carry the origin, the direction, the range, the
  half-angle and the telegraph's length, then the release, or the song's withdrawal by a beat.
- **`EscortBound`** ([M7-03a](M7-03a-the-ring.md) rule 10) carries the shield, the boss it covers, and
  its arc, once, when the shield takes its slot.
- **`EnemyLook.GuardArcDegrees`** and the *Guard* plate on `Enemy.prefab`
  ([M7-01d](M7-01d-three-archetypes-a-player-can-read.md) rule 4) already size a steel plate to an arc
  and yaw it with the body's facing. An orbiter faces outward (M7-03a rule 5), so a plate on a shield
  points away from the boss with no new view.
- **A covered hit already shrugs in steel** (M7-01d rule 3), and **the reticle's ✕** already marks a
  focused boss that cannot be hurt from where the player stands.

**What no view can do today is draw a shadow.** A wedge on the floor passes under a pillar exactly as
it passes over open ground. Without the cut, the one thing the song teaches, *"stand behind something"*,
is taught by being hit.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/SonicConeView.cs` | Game | **New.** A fan of rays against `Cover`, filled over the telegraph; flashes on release, leaves on a withdrawal (rules 1–4) |
| `Game/Views/EscortArcView.cs` | Game | **New.** A steel band on the floor across one shield's arc, turning with it (rules 5–6) |
| `Game/Views/BossViews.cs` | Game | Two more pools and six more subscriptions, stepped in the existing `Step` (rule 7) |
| `Tests/Game/Views/ChoirViewTests.cs` | Tests.Game | **New.** Every rule below |
| *small edits* | Game, Prefabs | `Game/Authoring/EnemyDefinition.cs` — `ToLook` reads an orbit's arc when no guard is authored (rule 8); `Game/Composition/RunScope.cs` — two prefab fields, their `RequireHazardPrefab` lines, and five `.WithParameter` lines on the `BossViews` registration: the two prefabs, `_coverLayer`, and both prewarms. VContainer resolves every constructor parameter or throws and never reads a C# default (`LineOfSightSense.DefaultRefreshHz`'s remark); `Prefabs/Vfx/VFX_SonicCone.prefab` and `Prefabs/Vfx/VFX_EscortArc.prefab` — **new**, each a `MeshFilter` and `MeshRenderer` on `M_TelegraphRing.mat`, premultiplied as M4-03's three views are; `Scenes/Run.unity`'s `RunScope` — the two fields dressed |
| *ripple* | Tests.Game, Tests.PlayMode | **12 `new BossViews(...)` sites across 4 files are untouched**: every new argument is optional and last, and a null prefab builds no pool (rule 7). `Views_CarryNoSerializedColour` joins the two new views |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Game/Views/SonicConeView.cs — block namespace (Traps §5).
namespace Soulvail.Game.Views
{
    public sealed class SonicConeView : MonoBehaviour, IPoolable
    {
        /// <summary>Rays across the wedge, edge to edge. 26 — 25 gaps of 2° across a 50° wedge (rule 2).</summary>
        public const int Rays = 26;

        /// <summary>Seconds the release flash lasts before the body goes back to the pool (rule 3).</summary>
        public const float FlashSeconds = 0.3f;

        /// <summary>
        /// Casts <see cref="Rays"/> rays at <c>LineOfSightSense.EyeHeight</c> from <paramref name="origin"/>
        /// against <paramref name="cover"/> and builds the wedge out of what they reach (rule 2).
        /// </summary>
        public void Bind(int ownerId, Vector3 origin, Vector2 directionXZ, float range, float halfAngleDegrees, float seconds, LayerMask cover);

        public int OwnerId { get; }

        /// <summary>How far ray <paramref name="index"/> reached, in metres: the range, or the first cover it met.</summary>
        public float Reach(int index);

        /// <summary>0 to 1 across the telegraph — how much of the wedge is filled.</summary>
        public float Fill { get; }

        public bool IsReleased { get; }

        /// <summary>The song left her: flash the whole wedge and retire after <see cref="FlashSeconds"/>.</summary>
        public void Release();

        /// <summary>Advances the fill or the flash by <paramref name="dt"/>; false once it should go back to the pool.</summary>
        public bool Step(float dt);

        /// <summary>Its mesh renderer is wired — what <c>RunScope.RequireHazardPrefab</c> reads (rule 7).</summary>
        public bool IsDrawable { get; }
    }

    public sealed class EscortArcView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// A band centred on <paramref name="anchor"/>, <paramref name="radius"/> out, spanning
        /// <paramref name="arcDegrees"/> and yawed to <paramref name="escort"/>'s bearing each step.
        /// </summary>
        public void Bind(int escortId, int anchorId, Transform anchor, Transform escort, float radius, float arcDegrees);

        public int EscortId { get; }
        public int AnchorId { get; }

        /// <summary>The bearing drawn this step, in degrees from +X toward +Z.</summary>
        public float Bearing { get; }

        /// <summary>Follows the two bodies; false once either is gone.</summary>
        public bool Step(float dt);

        /// <summary>Its mesh renderer is wired — <see cref="SonicConeView.IsDrawable"/>'s reason.</summary>
        public bool IsDrawable { get; }
    }
}
```

```csharp
public sealed class BossViews
{
    // Constructor gains, after beatPrewarm, defaulted:
    //     SonicConeView conePrefab = null, EscortArcView arcPrefab = null, LayerMask cover = default,
    //     int conePrewarm = 1, int arcPrewarm = 8
    // A null prefab builds no pool and draws nothing of its kind (rule 7). RunScope's
    // RequireHazardPrefab refuses a null in a live run, so only a fixture ever passes one — the
    // 12 existing construction sites keep compiling and draw no song.

    public int ConeCount { get; }
    public int ArcCount { get; }
    public bool TryGetCone(int bossId, out SonicConeView view);
    public bool TryGetArc(int escortId, out EscortArcView view);
}
```

## Behaviour

1. **A song is one wedge per boss, keyed by the singer's id.** `OnSonicConeTelegraphed` rents a
   `SonicConeView` and binds it to the event's origin, direction, range, half-angle and seconds. There
   is one song at a time per singer (M7-03b rule 1), so a second telegraph on a live id retires the
   first rather than leaking it, which is `ZoneViews.OnSpawned`'s ruling.
2. **The wedge is built from rays cast the way the sense casts, so its shadow is the sense's shadow.**
   - **The rays.** At bind, `Rays` rays spread evenly from `−halfAngle` to `+halfAngle` about the
     direction. Each starts at `origin + up × LineOfSightSense.EyeHeight` and runs horizontally to
     `range` with `Physics.Raycast` against `cover` only, triggers ignored. That is the sense's height,
     its mask and its trigger rule (AR §18.4), so a pillar that breaks the song breaks the drawing the
     same way.
   - **Where the two are one line.** The sense lifts both ends to eye height, so from `Arena_Tiered`'s
     0.6 m dais its ray falls from 1.7 m to a floor-standing player's 1.1 m, while the view's stays at
     1.7 m. Seen from above they are the same line. Only the pillars are on `Cover` in either arena, and
     every one stands from the floor to 4 m, so both rays are cut at the same place. Cover whose top
     or bottom fell between 1.1 m and 1.7 m would part them, and no arena authors any.
   - **The mesh.** Each ray's reach is where it met cover, or `range`. The mesh is a fan from the
     origin through those reaches: the wedge with the ground behind every pillar cut out.
   - **Built once.** The pillars do not move and she neither walks nor is shoved (M7-03b rules 1, 9), so rebuilding the
     fan every frame would draw the same shape sixty times a second.
   - **Sampled, and the sample is stated.** Rays are 2° apart, and the fan is drawn straight between a
     blocked ray and an open one, so a shadow's drawn edge can be off core's by up to one gap: 0.52 m
     at 15 m. Core's test is the authority (M7-03b rule 6), and this is its picture.
   - **The mask is `RunScope._coverLayer`**, the one `LineOfSightSense` is built with, handed over
     rather than re-authored. A second mask would be a second answer to *"what is cover"*.
3. **It fills over the telegraph, flashes on release, and leaves quietly on a withdrawal.**
   - **The fill.** A *Fill* child sharing the fan's mesh scales radially from 0 to 1 over `seconds`,
     from the origin outward. That is the ring's and the lane's *"filling means coming"*, the one
     telegraph vocabulary this game has taught.
   - **`SonicConeReleased`** snaps the fill to full, flashes the wedge for `FlashSeconds` and retires
     it. That happens **caught or not**, which is `FissureFired`'s reasoning.
   - **`SonicConeWithdrawn`** retires it at once, with no flash. The song was never sung, and a flash
     would say it was.
4. **A song whose singer dies is withdrawn, and a song whose release never comes retires on its own.**
   - **`EnemyDied` for the owner** retires a live wedge, [M7-01d](M7-01d-three-archetypes-a-player-can-read.md)
     rule 2's reason: a telegraph left after its owner dissolved promises something that will never
     come.
   - **The net.** A wedge whose release never arrives retires at `seconds + FlashSeconds` of its own
     clock. It is M4-03 rule 7's net and derived from the event, never authored.
5. **Every shield draws its arc, and the arc turns with it.** `OnEscortBound` resolves both bodies
   through `EnemyViews.TryGet` and binds an `EscortArcView`.
   - **Geometry.** The radius is the shield's XZ distance from the anchor at that moment, which is the
     `Radius` core bound (M7-03a rule 5). The band runs 0.35 m either side of it and spans the event's
     `ArcDegrees`.
   - **Each step** puts the band on the anchor and yaws it to the shield's current bearing, read from
     the two transforms. So a ring of six closed arcs is a closed steel circle, and a dead shield is a
     gap in it that turns.
   - **The mesh** is built once at bind and only its transform moves.
   - **An `EscortBound` whose bodies do not resolve is dropped** rather than drawn at the origin, which
     is `OnBeatStarted`'s ruling.
6. **An arc leaves with its shield or its boss.** `EnemyDied` or `EnemyDespawned` for either id retires
   the arc. A shield killed, a ring cleared by a beat (M7-03a rule 9), and a ring that fell with its boss
   (rule 10 of that task) each take their arcs off the floor on the same frame the bodies go.
   `Step`'s false, when a transform is destroyed, is the net.
7. **Both live in `BossViews`, the census that already owns a boss fight's screen** (M4-03's *As
   built*, deviation 1).
   - **Two pools, prewarmed.** One wedge, because one boss sings at a time. Eight arcs, the most any
     phase summons (M7-03b rule 10). A pool asked for more instantiates rather than refusing, which is
     `beatPrewarm`'s bargain.
   - **Six subscriptions**, all taken in the constructor (AR §18.1): the three song events,
     `EscortBound`, `EnemyDied` and `EnemyDespawned`.
   - **One `Step`.** `RunTicker` already steps `BossViews` with the snapshot's `dt`, so no argument
     moves. Neither view reads `Time.deltaTime`; each advances by the `dt` it is handed, as M4-03's
     three do.
   - **A null prefab builds no pool.** Its events are ignored, so a fixture that never meant to draw
     a song constructs `BossViews` as before. A live run cannot reach that state, because `RunScope`
     refuses a null prefab, or one that is not `IsDrawable`, by name, as it does for the ring, the
     crack and the shell.
   - **Colour.** Both views take their colour from `Palette` and carry no serialized `Color`: the wedge
     is `Palette.Danger`, because it is danger, and the arc is `Palette.Guard`, because hits do not land
     there (M7-01d rule 6). Each is premultiplied as M4-03's views are.
8. **A shield wears M7-01d's plate across its arc.** `EnemyDefinition.ToLook` reads the guard block's
   arc when one is authored, and the orbit block's escort arc when not. So `WeaverShield.asset`'s look
   carries 60°, and the plate on the body's outer face is the 60° arc's chord, 0.58 of the Warden's
   120° one. **No new view**: the plate
   yaws with the intent's facing, which is outward (M7-03a rule 5).
9. **Nothing on a frame path allocates.**
   - **Rays and vertices.** The rays write into arrays sized at `Awake`, and the mesh is rebuilt from
     them with the array overloads. One `RaycastHit` field is reused with the non-allocating
     `Physics.Raycast` overload, which is `LineOfSightSense`'s pattern.
   - **Steps.** An arc's step is two transform reads and a yaw. Retirement goes through the scratch
     list `BossViews` already keeps.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

Physics in an EditMode fixture: a pillar placed by hand is flushed with `Physics.SyncTransforms()`
before a ray is cast, which is the adapter fixtures' rule (AR §18.1).

| Test | Given / When / Then |
|---|---|
| `Song_BindsAWedgeAtTheEventsOrigin` | `SonicConeTelegraphed` from (0, 0, 0) east, 22 m, 25° / — / one live wedge for the owner; every ray reaches 22 with no cover present; the edge rays are ±25° off east — rules 1, 2 |
| `Song_APillarCutsTheWedge` | a 2 m `Cover` box 8 m down the centre line / bind / the centre rays reach ~7, the edge rays 22 — rule 2 |
| `Song_OnlyCoverCuts` | an `Enemy`-layer body and a trigger on `Cover` in the wedge / bind / every ray reaches 22 — rule 2 |
| `Song_TheRaysRunAtEyeHeight` | a 1 m-tall box on `Cover` in the wedge / bind / not cut; a 1.5 m pillar is — rule 2, AR §18.4 |
| `Song_FillsOverItsTelegraph` | a 1.2 s song / 0.6 s / `Fill` 0.5 — rule 3 |
| `Song_IsDanger` | a live wedge / — / its colour is `Palette.Danger`, premultiplied — rule 7 |
| `Song_FlashesOnReleaseHitOrMiss` | `SonicConeReleased(id, false)` / — / `IsReleased`, then back in the pool after `FlashSeconds` — rule 3 |
| `Song_AWithdrawalLeavesWithoutAFlash` | `SonicConeWithdrawn(id)` / — / back in the pool on that frame, never released — rule 3 |
| `Song_ASingersDeathWithdrawsIt` | `EnemyDied(owner)` mid-fill / — / retired; another id's death leaves it — rule 4 |
| `Song_RetiresOnItsOwnNet` | no release ever / `seconds + FlashSeconds` / back in the pool — rule 4 |
| `Song_ASecondTelegraphReplacesTheFirst` | two telegraphs for one owner / — / one live wedge, the first returned — rule 1 |
| `Arc_BindsOnEscortBound` | an anchor at the origin, a shield 3 m east / `EscortBound(shield, anchor, 60)` / one arc on the anchor, 60° wide, band 2.65–3.35 m, bearing 0 — rule 5 |
| `Arc_TurnsWithItsShield` | the shield moved to bearing 20° / a step / `Bearing` 20 — rule 5 |
| `Arc_IsGuard` | a live arc / — / `Palette.Guard`, premultiplied — rule 7 |
| `Arc_AnUnresolvedBindingIsDropped` | `EscortBound` for ids `EnemyViews` does not hold / — / no arc — rule 5 |
| `Arc_LeavesWithItsShield` | `EnemyDied(shield)`, and separately `EnemyDespawned(shield)` / — / retired each time — rule 6 |
| `Arc_LeavesWithItsBoss` | `EnemyDespawned(anchor)` / — / every arc on that anchor retired — rule 6 |
| `Shield_WearsItsPlateAcrossItsArc` | `WeaverShield.asset` / `ToLook` / arc 60, and the plate the 60° chord; `Warden.asset` still 120, `Husk.asset` 0 — rule 8 |
| `Views_CarryNoSerializedColour` | the two new views / reflected / no `[SerializeField] Color` — rule 7 |
| `Views_StepOnTheDtTheyAreHanded` | a 1.2 s song and an arc / one `Step(0.37f)`, deliberately not a plausible frame time / `Fill` is 0.37 ÷ 1.2, and the arc's bearing is the shield's after 0.37 s — rule 7 |
| `Pool_ReturnsEverything` | a fight's worth of songs and three rings of arcs / ended / every instance back in its pool — rule 7 |
| `Census_ANullPrefabDrawsNothing` | a `BossViews` built with no cone or arc prefab / every song and escort event / no throw, `ConeCount` and `ArcCount` 0 — rule 7 |
| `Prefabs_AreDressed` | `VFX_SonicCone.prefab`, `VFX_EscortArc.prefab` / loaded / each view non-null and `IsDrawable` — rule 7, Traps §5 |
| `ChoirViews_AllocateNothing` | 1 000 songs bound, filled and released, 1 000 arcs bound and stepped / after the first rental / zero — rule 9 |

## Manual verification (Editor / device)

She is rostered by [M7-03d](M7-03d-the-choirmother-at-stage-ten.md), so no Play session reaches her
here. The evidence is a probe, as M4-03's was: a camera capture is what proves a decal draws at all.

1. **[Editor] probe.** Through the MCP, raise `Arena_Pillars` in an open scene and build an
   `EnemyViews` and a `BossViews` by hand over it. Publish a `SonicConeTelegraphed` from the centre
   toward a pillar. Then publish `EnemySpawned` for a boss at the centre and six bodies on a 3 m ring,
   so `EnemyViews` holds all seven (rule 5 drops a binding it cannot resolve), an `EscortBound` for
   each of the six, and an `EnemyDied` for one. Capture from the game camera's height. *Expected: a
   red-orange wedge with a pillar-shaped notch cut out of it behind the pillar; a steel ring on the
   floor with one 60° gap; nothing opaque, and no square.*
2. **[Editor] probe.** The same capture after a `SonicConeReleased`. *Expected: a flash, then nothing.*
3. **[Editor]** Play `Run.unity` to stage 1. *Expected: the run starts, with no VContainer exception
   in the Console.* No test resolves `RunScope`'s registrations, so this is what proves the five new
   `.WithParameter` lines are all there. The owner's Play steps with her in the arena are M7-03d's.
4. **[device]** **GD §9.1 rule 7 for her.** Whether the wedge, its notches and a 30° gap in a steel
   ring read on a 6-inch screen at 400 dpi, in daylight. Deferred with
   [M7 row 1](../ROADMAP.md#carry-forward-into-m7).

## Out of scope

- **Her body.** She is the shared body in her own tint at 2.6× (M7-03b rule 10). A model is
  [M7-00e](../ROADMAP.md#m7--content-pass)'s art ruling, which is now given.
- **The circle texture.** Known issue 1. The fan and the band are meshes of their own and need none.
- **Sound.** The three song events are what M7-07's cue hangs off.
- **A shadow that moves.** Rule 2: nothing it is cast from can.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
