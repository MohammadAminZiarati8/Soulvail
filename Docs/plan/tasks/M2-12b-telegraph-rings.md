# M2-12b — The ring that says *something is about to happen here*

**Size:** S · **Depends on:** M2-05 (`SpawnTelegraphed`), M2-08 (`EnemyExploded`), M2-09 (the pooled-view pattern) · **Branch:** `m2-12b-telegraph-rings`
**Design refs:** GD §7.1 (the 0.8 s ring), §8.1 (the Bloater), §9.1 rule 1 (everything is telegraphed), §11.3 (overdraw), §16.4 (colour language), §12.4; AR §3, §4.3, §14, §18.1, §18.2 · **Ledger rows:** none — M2-09 closed the pooled-view half of row 8 and M2-11b takes the rest

## Goal

The two events that have been describing a circle on the ground to nobody finally draw one: a spawn ring that fills as its 0.8 s runs out, and a blast ring the size of the thing that just went off.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/TelegraphRingView.cs` | Game | One ring: radius, fill, fade — **block namespace** (Traps §5) |
| `Game/Views/TelegraphRings.cs` | Game | The census: rent on telegraph and on blast, return when the ring's time is up, step them all |
| `Tests/Game/Views/TelegraphRingsTests.cs` | Tests.Game | The census, the two lifetimes, the fill arithmetic, the pool |
| *small edits* | | `RunScope` + the ring prefab and a decal root; `RunInstaller` registers `TelegraphRings`; `RunTicker` takes it and steps it (rule 4); `DebugOverlay` shows rented-versus-pooled |
| *assets* | | `Prefabs/Vfx/VFX_TelegraphRing.prefab` — listed, not counted |
| *ripple* | | `InstallerTests` gains the registration; `RunTicker`'s constructor grows one argument, so its fixtures do too |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Views
{
    /// One ring on the ground. It decides nothing and knows no events: core has already said where
    /// and for how long, and this draws that and then goes away.
    public sealed class TelegraphRingView : MonoBehaviour, IPoolable
    {
        /// Puts this ring into service at `centre`, `radius` metres across, for `duration` seconds.
        /// `fills` true draws a countdown (a disc growing to the rim); false draws a flash that
        /// fades from full — the difference between "this is coming" and "that happened" (rule 5).
        public void Bind(Vector3 centre, float radius, float duration, bool fills, Color colour);

        /// True while this ring still has time left. `TelegraphRings` returns it when it does not.
        public bool IsLive { get; }

        /// Advances by `dt` — the snapshot's clamped step, never `Time.deltaTime` (rule 4).
        public void Step(float dt);

        public void OnSpawn();
        public void OnDespawn();
    }
}
```

```csharp
namespace Soulvail.Game.Views;

/// Every ring on the ground right now. `ProjectileViews`' shape, one layer simpler again: nothing
/// here is bound to an id, because a ring outlives nothing and nothing ever asks for it back.
public sealed class TelegraphRings : IDisposable
{
    /// How long a blast ring stays up after the blast. Cosmetic, and the only lifetime here that
    /// core does not supply — see rule 6.
    public const float BlastLingerTime = 0.35f;

    public TelegraphRings(
        IObjectResolver resolver,
        TelegraphRingView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 8);

    public int Count { get; }

    /// Steps every live ring and returns the ones whose time is up. Called once a frame by
    /// `RunTicker`, with `snapshot.Dt`.
    public void Step(float dt);

    public void Dispose();
}
```

## Behaviour

**The census**

1. **It is a listener, not a spawner** — `ProjectileViews` rule 1, and both subscriptions are taken **in the constructor**, which is AR §18.1's rule: a `Start` of its own would be ordered against nothing and would drop the opening wave's rings in silence.
2. `SpawnTelegraphed(specId, position, firesAt)` rents a ring at `position` for `SpawnDirector.TelegraphTime`, filling. `EnemyExploded(centre, radius)` rents one at `centre` for `BlastLingerTime`, not filling. Nothing is indexed by an id and nothing is ever looked up: a ring's whole life is decided at the moment it is rented.
3. **A telegraph is never cancelled, so neither is its ring.** M2-05 rule 7 is explicit — *"a ring the player dodged that produced nothing, or produced something elsewhere, is worse than no ring at all"* — and the consequence here is that there is no un-rent path and no id to un-rent by. A ring runs its 0.8 s and returns itself. That is not a simplification, it is the design rule showing up as an absent method.
4. **`RunTicker` steps them and they own no `Update`.** The ring's fill is the visible half of a countdown core is running on the snapshot's clamped `Dt`; a frame hitch shortens core's step and not the wall clock, so a ring on `Time.deltaTime` would finish filling before — or after — the body it is promising actually appears (AR §18.2). `ProjectileViews` rule 3, for its reason, and it is also what puts this object on the dependency chain early enough for rule 1.

**The two rings**

5. **Filling means *coming*; fading means *happened*.** A spawn ring draws a disc growing from the centre to the rim over its 0.8 s, so the amount of ring left is the amount of time left, readable at a glance and without a number (GD §7.1, §9.1 rule 1). A blast ring is drawn at full radius on the first frame and fades — there is nothing to count down to, the damage has already been applied by the corpse that owned it (M2-08), and a blast ring that filled would be promising an explosion that is already over.
6. **`BlastLingerTime` is cosmetic and it is the only number here core does not supply.** The spawn ring's duration is `TelegraphTime` and the blast's radius is `ExplosionSpec.Radius`, both authored; 0.35 s is how long the after-image stays, and it is a tuning knob rather than a rule — named, so that nobody later reads it as a game-feel constant with a source.
7. **Both are GD §16.4's saturated red-orange** — `#FF4A1F`, *"used for nothing else, ever"* — because both are danger, and that is the same reservation M2-12a's threat arrows draw on. It is also why cyan is available to that task for the held focus: one palette, two sides, and a ring that was any other colour would spend the one thing the colour language has.
8. **The radius is the truth, not a decoration.** A blast ring is exactly `ExplosionSpec.Radius` metres and a spawn ring is exactly the clearance the director promised, so *"I was outside it"* is a statement about the same circle core tested. A ring drawn a little larger to look better would make the game a liar in the one place GD §12.4's guardrails are about fairness.
9. **It lies flat on the ground and does not billboard.** The camera is fixed at 57° (GD §5.1); nothing in this game is seen from the side, so a billboard would solve a problem that cannot occur — `ReticleView`'s reasoning about its own chevron, applied to a decal.
10. **Additive-transparent, and that is the overdraw GD §11.3 warns about.** Eleven rings from one wave overlapping is the exact shape of the mobile fill-rate cost the budget names, so: one quad per ring rather than a `LineRenderer` per ring, no soft particles, and the device step below is the one that decides whether the count needs capping.
11. Pooled and prewarmed to the director's telegraph ceiling, so the only `Instantiate` calls happen while the scene loads (AR §14, GD §11.3). `Dispose` unsubscribes and disposes the pool, destroying every body it made whether or not it was returned — `EnemyViews`' bargain, one owner for the whole set.

## Tests

| Test | Given / When / Then |
|---|---|
| `Telegraphed_RentsAFillingRing` | a `SpawnTelegraphed` at (4, 0, 4) / publish / `Count` 1, centred there, filling, duration 0.8 (rule 2) |
| `Telegraphed_ReturnsItselfWhenTheTimeIsUp` | as above / `Step` to 0.79 / `Count` 1; to 0.81 / `Count` 0, the pool has it back (rule 3) |
| `Telegraphed_FillsLinearly` | duration 0.8 / `Step(0.4)` / the fill is 0.5 ± 0.01 (rule 5) |
| `Telegraphed_HasNoCancelPath` | a ring in service / — / no public way to end it early (rule 3) |
| `Exploded_RentsAFadingRing` | an `EnemyExploded` at (0,0,0) radius 3 / publish / a ring of radius exactly 3, not filling (rules 2, 8) |
| `Exploded_LingersThenReturns` | as above / `Step` to 0.34 / live; to 0.36 / returned (rule 6) |
| `Exploded_FadesFromFull` | as above / `Step(0)` then `Step(0.175)` / alpha starts at full and is roughly half way (rule 5) |
| `Rings_CoexistAndAreIndependent` | a telegraph and a blast in the same frame / `Step(0.4)` / the first is half-filled and live, the second returned |
| `Rings_ElevenAtOnce` | a wave telegraphing 11 / — / `Count` 11, nothing instantiated beyond the prewarm |
| `Ticker_StepsWithSnapshotDt` | a ring live, `snapshot.Dt` clamped below `Time.deltaTime` / one frame / it advanced by `snapshot.Dt` (rule 4) |
| `Ring_LiesFlat` | any ring / bind / its rotation is flat on XZ, unchanged by camera movement (rule 9) |
| `Ring_IsDangerColoured` | both kinds / bind / GD §16.4's red-orange (rule 7) |
| `Ring_ComesBackClean` | a ring bound, stepped, returned / re-rent / radius, fill, alpha and position as a fresh one (AR §18.4's pooled-reset rule, M2-09 rule 10's family) |
| `Prewarm_InstantiatesUpFront` | prewarm 8 / ctor / 8 inactive, and the first 8 rentals instantiate nothing (rule 11) |
| `Step_AllocatesNothing` | 11 live, warm-up / 10 000 × `Step` / allocated-bytes delta == 0 |
| `Dispose_UnsubscribesAndDestroys` | two live / `Dispose`, then publish a `SpawnTelegraphed` / nothing rented, no throw (rule 11) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play a stage. Every body arrives inside a ring that filled first, and the ring is where the body appears — not near it. That is M2-05 rule 7's promise being kept visually for the first time.
2. **[Editor]** Stand on a ring and stay there. Something spawns on top of you, which is legal beyond 6 m from where you were when it was chosen, and the ring is the only reason it was your decision.
3. **[Editor]** Let a Bloater go off. The ring is the blast's real radius: step just outside it and take nothing, step just inside and take the hit (rule 8).
4. **[Editor]** `DebugOverlay`: rented rises and falls with the wave and the total never grows after the first few seconds. If it grows, the pool is being bypassed.
5. **[device]** Eleven overlapping rings on a real screen — whether the fill still reads as a countdown when they overlap, and what the overdraw does to the frame time (GD §11.3, rule 10). This is the step that decides whether a concurrent-ring cap is needed. Deferred with the rest of the device list.

## Out of scope

- **Screen-edge threat arrows and the held-focus marker** — [M2-12a](M2-12a-threat-arrows-and-held-focus.md). Screen space, and a different question.
- **A ring for the Chaser's windup.** `EnemyTelegraph` is already drawn by `EnemyHitFeedback`'s swell (M1-18), and a second telegraph vocabulary for the same event would be two answers to one question.
- **The Gate, the barrier, or any arena decal** — M2-11a owns everything that belongs to a room rather than to a moment.
- **Particles, dust, or an impact burst.** GD §11.3 names overlapping transparent VFX as the real mobile cost and rule 10 is already spending it; M7-05's art pass, measured on a device.
- **A diegetic spawn telegraph.** The parking lot notes `Rig_Medium_Special`'s `Skeletons_Awaken_Floor` as the eventual replacement for a ring decal; it is promoted when the owner brings enemy art in, and this ring is what it would replace.

## As built

**Built as specified in behaviour; eleven deviations, three of which change a decision. Two items are reported rather than answered and both are the owner's — see the last section.**

### Deviations that change a decision

1. **`TelegraphRings` is registered in `RunScope`, not `RunInstaller`, and it is *required* rather than optional — with a second guard nothing in the spec asked for.** The registration half is forced for the reason `ArenaPool`, `EnemyViews`, `ProjectileViews` and M2-12a's `ThreatArrows` all moved: two of its four arguments are references to *this scene*, and `RunInstaller` is deliberately the half a headless test can build. The *required* half is M2-12a's argument transplanted: GD §9.1 rule 1 — everything is telegraphed — is an **invariant**, and this is the only thing in the game that draws a spawn telegraph, so a run composed without rings is one where bodies appear from nowhere. The second guard is the one worth naming: **a prefab whose quad was never dragged into its field rents, binds, steps and returns perfectly and draws nothing at all**, so the feature would be silently absent rather than broken. `TelegraphRingView.IsDrawable` exists solely so the composition root can say that out loud, once, instead of the run looking exactly like the one before this task.

2. **A spawn ring is 1 m in radius, and the number is derived rather than chosen.** Rule 8 asks for "exactly the clearance the director promised" without naming it, and `SpawnTelegraphed` carries no radius. The only circle core actually tests for a spawn is `SpawnDirector.MinSpawnSeparation` (2 m), and **its own note says the thing it prevents is "two rings on one patch of floor"** — so half of it is precisely the disc the director reserved for this body and nobody else, and two spawn rings therefore can never overlap. Spelled `TelegraphRings.SpawnRingRadius = SpawnDirector.MinSpawnSeparation / 2f` so the two cannot drift apart, with a test row asserting exactly that. The alternative readings — `MinPlayerDistance`'s 6 m, or an authored constant — would each draw a circle core never tested.

3. **The fill is the quad's own scale, not a shader property, so "one quad per ring" is literal.** Rule 5 describes "a disc growing from the centre to the rim", which a scaled quad *is* — so no shader, no second draw call, no `S_` asset, and the additive overdraw rule 10 is about stays at one quad per ring. **The cost is real and is the one thing to look at on a device:** there is no rim outline at full radius, so a spawn ring shows *where* the body will land only as the disc arrives, rather than marking the full extent from the first frame. Manual step 5 is where that gets judged; a rim would be a second quad and double the overdraw the rule exists to bound.

### Deviations in shape

4. **`TelegraphRingView` exposes five members the spec's API block does not list:** `Radius`, `Fill`, `Alpha`, `Colour` and `IsDrawable`. The first four are what let the behaviour rows assert the arithmetic without reaching through a renderer into a material — the claim in rule 5 is about the countdown, not about URP — and the fifth is deviation 1's guard.
5. **`TelegraphRings` exposes `PooledCount` and `SpawnRingRadius`.** `PooledCount` is what the spec's own Files table asks for two lines later ("`DebugOverlay` shows rented-versus-pooled"); it could not be met without it.
6. **`InstallerTests` gained nothing**, against the ripple row. It covers `RunInstaller`, and the registration is on `RunScope`, whose `Configure` runs only when a scene loads. Exactly what happened to M2-12a for the same reason.
7. **One asset the Files table does not name: `Materials/M_TelegraphRing.mat`.** A `MeshRenderer` with no material draws magenta, and rule 10's "additive-transparent" has to be serialised somewhere. URP/Unlit, `_Surface` transparent, `SrcAlpha`/`One` additive, `ZWrite` off, double-sided, queue 3000 — the `M_Reticle` recipe with the blend swapped.
8. **The `MaterialPropertyBlock` is built on first paint, and neither obvious place works.** A field initialiser throws `"CreateImpl is not allowed to be called from a MonoBehaviour constructor (or instance field initializer)"` — and it throws **at import**, from inside `AddComponent` and prefab serialisation, which is how it was found: four exceptions while saving the prefab, from code that compiles cleanly and that no test would have run. `ReticleView` builds its own in `Awake` for that reason, but `Awake` never runs in EditMode (Traps §5) and a fixture-bound body would then paint through a null. Filed as a Traps candidate — see the PROGRESS entry.
9. **`Rings_ElevenAtOnce` asserts `Count + PooledCount == 11`** rather than counting `Instantiate` calls, which nothing in the project can observe. Same shape as `Prewarm_InstantiatesUpFront`.
10. **Twenty test rows rather than the spec's sixteen plus implied guards** — the extra is `SpawnRingRadius_IsHalfTheDirectorsSeparation`, which is deviation 2 under test.
11. **The step sits immediately after `_projectileViews.Step`, and that neighbour's comment was rewritten.** It claimed to be "the last cosmetic thing in the frame", which stopped being true. Both are now described as the frame's two purely cosmetic steps, read by nothing below.

### Reported, not answered — both the owner's

**The physics-sync question, now with a measurement.** M2-12a left `Physics.SyncTransforms()` as a recommendation the owner had not ruled on, and this task was told not to touch it. It was not touched. What this task can add is evidence, taken three ways with the Run scene open:

| Tree | PlayMode |
|---|---|
| `dev` (12a merged) | **10 passed, 1 failed** — `Ticker_RunsTheStepsInOrder`. Reproduced twice, once with the fixture in isolation. |
| this branch | **9 passed, 2 failed** — that row plus `Ticker_ReportsFactsAfterBodiesMoved`. Both are only the `ConeReport` assertion; every ordering assertion passes. |
| this branch **+ the one line**, temporarily, then reverted | **11 passed, 0 failed.** |

So: **`dev` is not green either** — the merged baseline fails one of these rows, which Current State did not yet say — and this branch moves the count by one. That is expected from the mechanism rather than from the rings: with `m_AutoSyncTransforms` at 0, whether the sweep sees a body that moved this frame depends on whether a `FixedUpdate` happened to intervene, and **the ring step lands inside exactly that window** (between `ApplyEnemyMoves` and the fact phase), so it changes the load either side of an assertion that was already decided by timing. It touches no collider and asks physics nothing. **The one line makes all eleven green, which is the AR §18.1 invariant becoming true of the code rather than only of the call order.** `RunTicker`'s step order is outside this task's remit and nothing was kept: `grep SyncTransforms` over the branch returns nothing.

**The palette now couples two namespaces both ways.** Rule 7's colour is read from `ThreatArrows.Danger` rather than copied, which is what the owner asked for and keeps `#FF4A1F` in one place. But `Presentation` already depends on `Views` (both `ThreatArrows` and `DebugOverlay` do), so `Views → Presentation` makes the two mutually dependent — legal inside one assembly, and untidy. The one-line fix is a shared `Palette` holding GD §16.4's two colours, which is a file outside this task's table. Left as is, flagged.
