# M2-09 — Projectile views, and the first test that proves a pooled body forgets its last life

**Size:** M · **Depends on:** M2-07a (the events), M2-07b (something that fires) · **Branch:** `m2-09-projectile-views`
**Design refs:** GD §8.1, §11.3 (pool everything, watch overdraw); AR §3, §4.3, §14, §18.1, §18.2, §18.4 · **Ledger rows:** 8 — the pooled-view half; M2-11 keeps the rest

## Goal

The bolt a Spitter throws is visible, arcs, and is rented rather than instantiated — and the reset chain that every pooled body depends on is finally asserted by something that runs, instead of by a careful read.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ProjectileView.cs` | Game | One bolt's body. Dumb, cosmetic, poolable — **block namespace** (Traps §5) |
| `Game/Views/ProjectileViews.cs` | Game | The census: rent on fired, return on impacted, step them all |
| `Tests/Game/Views/ProjectileViewsTests.cs` | Tests.Game | The census and the arc's arithmetic |
| `Tests/PlayMode/PoolingLifecycleTests.cs` | Tests.PlayMode | **Row 8**: a full life through the pool, for both families |
| *small edits* | | `Prefabs/Projectiles/Projectile.prefab` (new asset, listed not counted); `RunScope` + the prefab field and a pool root; `RunInstaller` registers `ProjectileViews`; `RunTicker` takes it and steps it (rule 3); `DebugOverlay` shows rented-versus-pooled |
| *ripple* | | `InstallerTests` gains the new registration; `RunTicker`'s constructor grows one argument, so its test fixtures do too |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Views
{
    /// One bolt in flight. It decides nothing: core already knows where and when this lands
    /// (M2-07a rule 2), and everything here is the picture of a decision that has been made.
    public sealed class ProjectileView : MonoBehaviour, IPoolable
    {
        /// The id of a view standing in for nothing. Zero, because core issues ids from 1.
        public const int Unbound = 0;

        public int Id { get; }
        public bool IsBound { get; }

        /// Puts this body into service flying `origin` → `target` over `flightTime` seconds.
        public void Bind(int id, Vector3 origin, Vector3 target, float flightTime);

        /// Advances the flight by `dt` seconds — the snapshot's clamped step, never
        /// `Time.deltaTime` (rule 3).
        public void Step(float dt);

        public void OnSpawn();
        public void OnDespawn();
    }
}
```

```csharp
namespace Soulvail.Game.Views;

/// The bolts in the air, on the Unity side: one body per shot core says exists, rented and
/// returned by the two projectile events. `EnemyViews`' shape, one layer simpler — nothing here
/// reports anything back into the snapshot, because core never lost track of where a shot was.
public sealed class ProjectileViews : IDisposable
{
    public ProjectileViews(
        IObjectResolver resolver,
        ProjectileView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 0);

    public int Count { get; }
    public bool TryGet(int id, out ProjectileView view);

    /// Steps every bolt in service. Called once a frame by `RunTicker`, with `snapshot.Dt`.
    public void Step(float dt);

    public void Dispose();
}
```

## Behaviour

**The census**

1. **It is a listener, not a spawner**, and the mirror of `EnemyViews` right down to the ordering: `ProjectileFired` arrives after core has recorded the shot, so a body can be rented knowing the id resolves; `ProjectileImpacted` arrives after the shot has been resolved and dropped, so a body returned here can never be one core still expects to exist. Both subscriptions are taken **in the constructor**, which is AR §18.1's rule — a `Start` of its own would be ordered against nothing.
2. `ProjectileFired` rents a body, binds it to the event's `Origin`, `Target` and `FlightTime`, and indexes it by id. `ProjectileImpacted` un-indexes it and releases it. An impact for an id with no body is ignored rather than refused, for the reason `EnemyViews.OnDespawned` ignores one: a view may legitimately never have been made.
3. **`RunTicker` steps them, and they own no `Update`.** Core times the flight with the snapshot's clamped `Dt` and the view has to use the same step, or a frame hitch — which shortens core's step and not the wall clock — lands the *picture* of the bolt before the damage it represents (AR §18.2). This also settles the question of what puts this object on the dependency chain early enough for rule 1: it is on the frame path, like everything else `RunTicker` holds, rather than being held only so that it exists.
4. `Dispose` unsubscribes and disposes the pool, which destroys every body it made whether or not it was returned — `EnemyViews`' bargain, for its reason: one owner for the whole set.
5. The pool is prewarmed to core's projectile capacity, so the only `Instantiate` calls of a run happen while the scene is loading (AR §14, GD §11.3).

**The flight**

6. **XZ is linear and matches core exactly; the arc is a hump on Y and nothing else.** The body's XZ position is `lerp(origin, target, elapsed / flightTime)`, which is the same straight line core measured the flight time along, and Y adds `4h·t(1−t)` on top of the endpoints' own interpolation. Core's hit test is XZ (M2-07a rule 6), so the hump cannot change an outcome — it is exactly the sort of thing that must live on this side of the boundary, and exactly the reading GD §8.1 asks for with *"slow arcing projectile"*.
7. A zero `flightTime` is a shot already arrived (M2-07a rule 3): the view clamps `t` to 1 rather than dividing, and is usually released on the same frame it was rented. Not an error, and no branch in core.
8. The body **faces along its own travel**, recomputed per step, so an arcing bolt tips over rather than pointing at the sky the whole way. A zero-length step leaves the rotation alone — `EnemyView.Face`'s rule, for its reason.
9. **It never overruns.** Once `t` reaches 1 the body sits on the target point until the impact event returns it, which is at most one frame. A view that kept flying past its target on a late event would draw a bolt that missed something core says it hit.

**Row 8 — the pooled-body reset, finally under test**

10. **One PlayMode test per pooled family, each a full life: rent → bind → hurt → kill → despawn → re-rent, then assert the body is exactly as clean as a fresh one.** For `EnemyView`: scale, `_BaseColor` and its alpha, the material it is drawn with, the trigger collider's `enabled`, `Id == Unbound`, and no knockback in flight. For `ProjectileView`: position, elapsed, rotation, `Id == Unbound`.
11. **It has to be PlayMode and that is the point.** `EnemyHitFeedback` captures `_liveColour` and `_liveScale` in `Awake`, which never runs on a prefab instantiated in EditMode, and the dissolve advances in `Update`, which never ticks there — so an EditMode test of `OnDespawn` asserts that nothing was undone because nothing had happened. That is precisely the shape of test that passes against a broken feature, and it is why the ledger calls `EnemyView.OnDespawn` *"the most dangerous method here"* while having no coverage of it at all. The test builds its own `LifetimeScope` with a `DomainEventHub` and a pool; it does not boot the game.
12. **This closes half of ledger row 8, and the half it does not close is named:** `RunTicker`'s frame order still has no automated coverage, and it stays with **M2-11**, which is the other task the row names. Anything added to `Enemy.prefab` or `Projectile.prefab` that remembers something joins the assertions in rule 10 — the AR §18.4 invariant now has a test to grow rather than only a paragraph.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fired_RentsAndBindsAtTheOrigin` | a `ProjectileFired` at (3, 0, 3) / publish / `Count` 1, the body is at the origin, bound to the id |
| `Fired_TwiceRentsTwoBodies` | two events, two ids / — / `Count` 2, both resolvable |
| `Impacted_ReleasesTheBody` | one in service / `ProjectileImpacted` / `Count` 0, the pool has it back, `Id == Unbound` |
| `Impacted_UnknownId_IsIgnored` | nothing in service / `ProjectileImpacted(99)` / no throw, no warning (rule 2) |
| `Impacted_ReturnsTheBodyForReuse` | fire, impact, fire / — / the same instance, rebound to the new id, and nothing was instantiated |
| `Step_MovesAlongXzLinearly` | origin (0,0,0), target (12,0,0), flight 1 s / `Step(0.5)` / XZ is exactly (6, _, 0) (rule 6) |
| `Step_ArcsOnYOnly` | as above / `Step(0.5)` / Y is above both endpoints, XZ unchanged from the row above |
| `Step_YReturnsToTheTargetHeight` | as above / `Step` to `t == 1` / the body is exactly on the target point |
| `Step_ZeroFlightTime_DoesNotDivide` | flight 0 / `Step(0.016)` / the body is on the target, no NaN, no throw (rule 7) |
| `Step_DoesNotOverrun` | flight 1 s / `Step` to `t == 1.5` / still exactly on the target (rule 9) |
| `Step_FacesAlongTravel` | a flight along +X / `Step` / forward is +X-ish; a zero-length step / the rotation is unchanged (rule 8) |
| `Step_IgnoresUnboundBodies` | a pooled body / `Step` / nothing moves, no throw |
| `Dispose_UnsubscribesAndDestroysEverything` | two in service / `Dispose`, then publish a `ProjectileFired` / nothing rented, no throw (rule 4) |
| `Prewarm_InstantiatesUpFront` | prewarm 8 / ctor / 8 inactive instances, and the first 8 rentals instantiate nothing (rule 5) |
| `Ticker_StepsWithSnapshotDt` | a run with one bolt in flight, `snapshot.Dt` clamped below `Time.deltaTime` / one frame / the body advanced by `snapshot.Dt` (rule 3) |
| **PlayMode** | |
| `EnemyView_ComesBackCleanAfterAFullLife` | a pooled `EnemyView`: bind, `EnemyDamaged`, `EnemyTelegraph`, `EnemyDied`, several frames of dissolve, release / re-rent / scale, colour, alpha, material, collider `enabled` and `Id` all as a fresh body's (**row 8**, rule 10) |
| `EnemyView_ComesBackCleanAfterAKnockback` | a shove in flight when it is released / re-rent / no slide inherited, velocity zero, position wherever it was bound |
| `ProjectileView_ComesBackCleanAfterAFullFlight` | bind, step to arrival, release / re-rent / position, rotation and elapsed as a fresh body's |
| `Pool_ReusesRatherThanInstantiating` | prewarm 4, twelve full lives / — / four instances ever created (AR §14) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Start a run at stage 2 and let a Spitter throw: a bolt leaves it, arcs, lands where you were standing, and the damage arrives as it lands rather than before or after. That last part is rule 3 — the whole reason the view does not own its `Update`.
2. **[Editor]** Watch `DebugOverlay` through a fight: rented rises and falls, pooled is its complement, and the total never grows after the first few shots. If it grows, the pool is being bypassed.
3. **[Editor]** Kill a Spitter mid-flight. The bolt keeps going and still lands (M2-07a rule 5) — the one behaviour that looks like a bug until you know it is the rule.
4. **[device]** Whether a bolt is legible against a busy arena at a phone's size, and whether its arc reads as *incoming* rather than as decoration. Deferred with the rest of the device list.

## Out of scope

- **The blast ring, the spawn telegraph and the threat arrows** — M2-12. `EnemyExploded` and `SpawnTelegraphed` both already carry a centre and a radius; nothing subscribes to either yet.
- **A trail, particles or impact VFX.** GD §11.3 warns that overlapping transparent VFX are the real cost on mobile, and a projectile trail is the first thing that would prove it. M7-05's art pass, measured on a device.
- **A projectile prefab per archetype.** One body, tinted by whatever fires it, is the same argument M2-06 rule 9 made for enemies.
- **`RunTicker`'s frame-order coverage** — rule 12, stays with M2-11 on ledger row 8.
- **A view for the player's own projectiles** — M5-01, and it may well reuse this whole file. Nothing here is written to prevent it, and nothing here is generalised for it either.

## As built

**Built as specified in behaviour; eight deviations, three of them in the Files table and one that changes what a test row asserts.**

1. **`RunInstaller` is untouched — `ProjectileViews` is registered in `RunScope`.** The Files table names the installer, and the installer cannot hold it: two of the four constructor arguments are references to *this scene* (the prefab and the pool root), and `RunInstaller` is deliberately the half a headless test can build. It is registered exactly as `EnemyViews` is, three lines up, with the same `WithParameter`-by-name reasoning. The spec's own Public API block — a constructor taking a `ProjectileView` and a `Transform` — is what rules this out, so it is the Files row that was wrong rather than the design.
2. **The `InstallerTests` ripple does not exist, and follows from 1.** That fixture builds containers from `BootInstaller` and `RunInstaller` alone; there is no `RunScope` in it and never has been, so there is no registration there to assert. The wire is covered instead by the PlayMode row that already existed: `Descend_StartsARun_AndRefusesASecondTap` composes the real `RunScope` and reaches a running arena, which cannot happen if `ProjectileViews` fails to resolve or the prefab field is unassigned. It went from passing to *meaning something new* without a line changing.
3. **The `RunTicker` ripple does not exist either.** Nothing in the project constructs a `RunTicker` — it has no fixtures, which is ledger row 8's other half — so the constructor grew its sixteenth argument with no test to update.
4. **`Ticker_StepsWithSnapshotDt` is implemented at the view rather than at the ticker**, and this is the one row whose assertion changed. Rule 12 and *Out of scope* both keep `RunTicker`'s coverage with **M2-11b**, and building a fixture for it here would have meant a fifth test file and fifteen fake dependencies for one line. What the row claims is split into the two halves that are checkable without one: `Step` advances by the argument it was handed (0.2 s on a 10 m / 1 s flight lands on 2 m, which no frame time would produce), and `ProjectileView` **declares no `Update`, `LateUpdate` or `FixedUpdate`** — asserted by reflection, because that is the property that makes the ticker the only thing that can advance a bolt. The remaining line, that the ticker passes `snapshot.Dt`, is one call site read in review and watched in manual step 1.
5. **A second new asset: `Materials/M_Bolt.mat`.** The prefab needs a material and the project ships three: bone grey (the enemies'), the player's cyan, and the reticle's. A grey bolt against a grey arena is the exact failure this task exists to fix, and tinting the shared enemy material from the view would be a second writer of `_BaseColor`. One URP/Lit asset, acid green and emissive, listed not counted like the prefab.
6. **`Run.unity` gained a `Projectiles` root object and two references on `RunScope`.** Implied by the Files table's "the prefab field and a pool root", but it is a scene edit and so is named here. The diff is 34 added lines and nothing else — no unrelated TMP overrides, no re-serialisation (Traps §5), and `git diff ProjectSettings/` is empty.
7. **`ProjectileViews.PooledCount` is a member the Public API block does not list.** `DebugOverlay` shows rented *against* pooled, and the pool is private to the census, so the second number needs a door. It is the one number that answers the manual step's "if the total grows, the pool is being bypassed".
8. **One test row beyond the table, and one member fewer than `EnemyView` has.** `Step_AdvancesEveryBoltByTheGivenDt` covers `ProjectileViews.Step` walking the whole census, which the table's rows only reach one body at a time; and `ProjectileView.Unbind` is **private**, unlike `EnemyView`'s public one, because its only caller is `OnDespawn` six lines below it and the Public API block does not list it either.

**Two decisions inside the rules, worth naming.** `OnDespawn` restores the pose captured in `Awake` rather than zeroing the transform — so "as clean as a fresh one" is measured against what the pool actually hands out, and the PlayMode row asserts it against a second, never-used body rather than against a pose written into the test. And the arc height is a serialized field with a **field initialiser** (1.2 m), which is what makes it readable in EditMode where `Awake` never runs — the same trick `EnemyHitFeedback._prefabScale` uses, for the same reason (Traps §5).

**Row 8's pooled-view half is closed.** `PoolingLifecycleTests` is four PlayMode rows: an enemy body through hit → wind-up → death → four frames of dissolve → return, asserting scale, material, tint, alpha, collider, id and velocity against a clean body; a shove caught mid-slide; a bolt that flew its whole flight; and twelve lives through a pool of four, which had to become **three rounds of four concurrent bodies** rather than twelve consecutive ones — a pool rented one at a time returns the same instance every time and would pass that row with a prewarm of one. The fixture builds its bodies in code and writes `EnemyHitFeedback`'s two serialized references by reflection: a PlayMode assembly is compiled for every platform, so it cannot reach `AssetDatabase` or `SerializedObject`, and `Resources.Load` is banned. Templates are created **inactive**, which is what stops the template running the `Start` that throws when nothing injected it — VContainer deactivates a prefab before instantiating and injects the copy while it is off, so an inactive template still yields an injected instance.

**Verified:** EditMode **764 green, 0 failed, 9.3 s** — M2-08's 745 plus exactly this task's 19. PlayMode **7 green, 0 failed, 3.4 s** — M2-05's 3 plus this task's 4. Zero compile errors, zero analyzer warnings, no `ProjectSettings/` drift.
