# M7-02d — The two affixes that fire on death

**Size:** M · **Depends on:** M7-02c · **Branch:** `m7-02d-the-two-that-fire-on-death`
**Design refs:** GD §8.3, §12.4, §16.4; AR §18.1, §18.2, §18.3, §18.4; ADR-0003, ADR-0011 · **Ledger rows:** none moved. The two gaps M7-00a handed this group are rules 4 and 6: no zone can hurt the player and a ninth one throws, and `EnemySystem` holds no projectile reference

## Goal

A Volatile Elite leaves a 4-second pool of danger where it fell, and a Splintered one throws six shards
onto a ring through where the player stands. Both come from the death drain, not the kill, and neither
survives a stage boundary.

## Where a death's consequence already lives

Two consequences of a death already exist, and each was built so that it is decided on the tick:

- **Rise** (M5-04b) is offered each tick's dead through `EnemySystem.DrainDeaths` into a buffer
  `RunSession` owns. `RisePassive.OnDeaths(deaths, now)` is the precedent in full.
- **The Weaver's split** (M7-01b rule 3) is offered the same span on the next line, for three reasons.
  `ApplyDamage` is reached between ticks, it is reached from inside three span walks, and core does not
  act on its own events.

Both reasons apply here, and a third applies too. The two things these affixes produce, a zone and six
shots, belong to systems `EnemySystem` does not hold. `ZoneSystem` holds `EnemySystem`, so the reverse
reference would be a cycle. So the death affixes are a third reader of the drained span: a small class
holding the two systems it places into, which is what `RisePassive` is for the army.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/DeathAffixes.cs` | Core | **New.** `OnDeaths(deaths, now, playerPosition)` — a pool for a Volatile death, a burst for a Splintered one (rules 1–3, 7–10) |
| `Core/Combat/ZoneSystem.cs` | Core | **Substantial.** `ZoneSide.HurtsThePlayer`, a hazard table of its own, `PlaceHazard`, `ClearHazards` (rules 4–6) |
| `Tests/Core/Ai/DeathAffixTests.cs` | Tests.Core | **New.** Rules 1–3, 7–11 |
| `Tests/Core/Combat/HazardZoneTests.cs` | Tests.Core | **New.** Rules 4–6 |
| *small edits* | Core, Game, Data, Docs | `Core/Content/AffixSpec.cs` — `PoolSpec`, `BurstSpec`, two parameters optional and last, `AffixSet.Pool` / `Burst`; `Core/Ai/EnemySystem.cs` — `EnemyDeath` gains `Affixes` and `Strike`, filled in `Bank`; `Core/Run/RunSession.cs` — builds `DeathAffixes`, one call on the drain line after Rise and the split, and hands the zones to `StageFlow`; `Core/Stage/StageFlow.cs` — `ZoneSystem zones = null`, optional and last, cleared of hazards in `Advance` (rule 6); `Core/Combat/PlayerCombat.cs` — `DropPool`'s full-table guard counts the player's zones (rule 5); `Core/Events/SkillEvents.cs` — `ZoneSpawned.IsHazard`; `Game/Views/ZoneView.cs`, `ZoneViews.cs` — a hazard is drawn in danger (rule 12); `Game/Authoring/AffixDefinition.cs` — the two blocks; `Game/Composition/BootInstaller.cs` — `ProjectileCapacity` 32 → 64 (rule 10); `Data/Affixes/Volatile.asset`, `Splintered.asset` — **new**, rule 11; `Data/Modes/Descent.asset` — the pool is five; `Data/Localisation/English.asset` — two names; `Pseudo.asset` regenerated; `Docs/GameDesign.md` — §8.3's Splintered row; `Docs/Architecture.md` — the §18.1 order row's drain step, the explosion row's Volatile clause (rule 2), and an §18.4 row (a hazard is refused, never thrown) |
| *ripple* | Tests.Core, Tests.Game | **2 `new ZoneSpawned(...)` sites** gain the flag; **19 `new StageFlow(...)` sites are untouched**; `EmberwrightTests.View_IsNotEdited` is **rewritten**, because it pins the premise this task retires (rule 12); `SkillViewTests.Views_UseNoDangerColour` is scoped to the player's zones; `ZoneSystem.Capacity`'s 15 references keep their meaning (rule 5) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>A pool of danger left where the body died: how wide, how long, and how hard it burns.</summary>
public sealed class PoolSpec
{
    /// <summary>Seconds between pulses. <c>SiphonSpec.PulseInterval</c>'s value, and for its reason.</summary>
    public const float PulseInterval = 0.5f;

    /// <param name="radius">Metres, XZ, inclusive. 2.5 for Volatile.</param>
    /// <param name="duration">Seconds it stands. 4 (GD §8.3).</param>
    /// <param name="strikeFractionPerPulse">Of the dead body's <c>ContactDamage</c>, per pulse. 0.25.</param>
    public PoolSpec(float radius, float duration, float strikeFractionPerPulse);
    public float Radius { get; }
    public float Duration { get; }
    public float StrikeFractionPerPulse { get; }
}

/// <summary>Shards thrown on death onto a ring through the player (rule 7).</summary>
public sealed class BurstSpec
{
    /// <param name="count">How many. At least 2. 6 (GD §8.3).</param>
    /// <param name="maxRange">The farthest the ring lands, in metres. At least <see cref="MinRange"/>. 12.</param>
    /// <param name="flightSeconds">How long every shard flies, whatever the range. 0.8.</param>
    /// <param name="radius">Each shard's blast, in metres. 1.2.</param>
    public BurstSpec(int count, float maxRange, float flightSeconds, float radius);
    public int Count { get; }
    public float MaxRange { get; }
    public float FlightSeconds { get; }
    public float Radius { get; }

    /// <summary>
    /// The nearest the ring lands: <c>radius ÷ sin(π ÷ count)</c>, computed once — the range at which
    /// two neighbouring blasts just touch, so no point on the floor is inside two (rule 8). 2.4 for Splintered.
    /// </summary>
    public float MinRange { get; }
}

public sealed class AffixSpec
{
    // ... after M7-02c's siphon, defaulted and last:  PoolSpec deathPool = null, BurstSpec deathBurst = null
    public PoolSpec DeathPool { get; }
    public BurstSpec DeathBurst { get; }
}

public readonly struct AffixSet
{
    public PoolSpec Pool { get; }
    public BurstSpec Burst { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public readonly struct EnemyDeath
{
    // Two fields, each with a reader (rules 1 and 2):
    /// <summary>What the body carried when it died.</summary>
    public AffixSet Affixes { get; }
    /// <summary>Its <c>ContactDamage.Value</c> as it died — d(n) and every modifier in it.</summary>
    public float Strike { get; }
}

public sealed class DeathAffixes
{
    /// <exception cref="ArgumentNullException">Either system is null.</exception>
    public DeathAffixes(ZoneSystem zones, ProjectileSystem projectiles);

    /// <summary>
    /// Places a pool for every death that carried one and throws a burst for every death that carried
    /// one. Called once a tick by <c>RunSession</c>, on the drain line after Rise and the split.
    /// </summary>
    public void OnDeaths(ReadOnlySpan<EnemyDeath> deaths, float now, Vector3 playerPosition);
}
```

```csharp
namespace Soulvail.Core.Combat;

public enum ZoneSide
{
    HealsThePlayer, BurnsEnemies,

    /// <summary>Hurts the player standing in it, through <c>PlayerCombat.Drain</c>. Placed by a death (M7-02d). Appended: 2.</summary>
    HurtsThePlayer,
}

public sealed class ZoneSystem
{
    /// <summary>The player's zones: a ninth still throws, exactly as before (rule 5).</summary>
    public const int Capacity = 8;

    /// <summary>Places a death leaves. A ninth is refused silently (rule 5).</summary>
    public const int HazardCapacity = 8;

    /// <summary>The id <see cref="PlaceHazard"/> returns when it refused. Ids are issued from 1.</summary>
    public const int NoZone = 0;

    /// <summary>How many standing zones are hazards.</summary>
    public int HazardCount { get; }

    /// <summary>How many standing zones are the player's — what <see cref="Capacity"/> bounds.</summary>
    public int PlayerZoneCount { get; }

    /// <returns>Its id, or <see cref="NoZone"/> when <see cref="HazardCapacity"/> hazards already stand.</returns>
    /// <exception cref="InvalidOperationException">The system was built without the <c>PlayerCombat</c> a hazard hurts.</exception>
    public int PlaceHazard(Vector3 at, float radius, float duration, float damagePerPulse, float pulseInterval, float now, object source);

    /// <summary>Retires every hazard and publishes a <c>ZoneExpired</c> for each; the player's zones stand (rule 6).</summary>
    public void ClearHazards();
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct ZoneSpawned
{
    /// <param name="isHazard">Placed by a death, and it hurts. Required: two sites, and a view must never guess.</param>
    public ZoneSpawned(int id, Vector3 position, float radius, float duration, bool isHazard);
    public readonly bool IsHazard;
}
```

## Behaviour

1. **A death's affixes ride the death record.** `EnemySystem.Bank` writes the body's `AffixSet` and
   its `ContactDamage.Value` into the `EnemyDeath` it already writes. A corpse is still registered
   when the drain runs, but it is retired `CorpseTime` later and a record must not depend on that.
   **The strike is read at death**, so d(n), the Veilrot bonus and anything else in the stack reaches
   the pool and the shards. That is `EnemySystem.Explode`'s rule, *"the damage is the agent's stat"*,
   one step later.
2. **The drain is offered to the death affixes after Rise and the split, and nothing in them draws.**
   `RunSession.Tick` calls `DeathAffixes.OnDeaths(deaths, State.Time, State.PlayerPosition)` on the
   line after M7-01b's `Split`, above the death check. A death with neither block costs one struct
   read. **No stream is drawn**, because every placement below is forced: the pool stands where the
   body fell, and the ring aims at the player. So a seed's later spawns, offers and drops do not depend
   on how many Volatile Elites died. A body killed between ticks places its pool one frame late, the
   lag M7-01b rule 3 already accepts.

   **Volatile is not an explosion, which two places predicted it would be.** `EnemySystem.ApplyDamage`'s
   remark and AR §18.1's explosion row both say an `ExplosionSpec` is what lets *"M7-02's Volatile
   affix"* go off without a second mechanism. GD §8.3 says *pool*, and a blast is an instant where a
   pool is a place with four seconds of life. That life is `ZoneSystem`'s, reached from the drain. The
   remark and the row's clause are corrected in this PR; the explosion rule itself stands.
3. **A Volatile death leaves a pool where it fell.** It calls `PlaceHazard` at the death position with
   the pool's radius and duration and `Strike × StrikeFractionPerPulse` per pulse, once every
   `PoolSpec.PulseInterval`. The source is the `AffixSpec`, which is identity only (ZoneSystem rule 9).
   **The first pulse is one interval away**, which is `ZoneSystem.Spawn`'s own rule: the pool
   appearing is its tell, and half a second is the step out.
4. **A hazard hurts the player through attrition, and nothing else.** `Pulse` gains its third branch.
   For `HurtsThePlayer` it runs the same XZ test the heal runs against the blackboard's position and
   calls `PlayerCombat.Drain(amount, now, sourceId: 0)`, which is M7-02c rule 10's door.
   - **Not `ApplyDamage`.** A pulse every half-second through a blow would keep the Oathbound in
     i-frames for as long as he stood in the fire, and that is the defect M7-02c's door exists to
     refuse.
   - **A hazard hurts no enemy.** `ZoneSide` is a fact about who is inside, and this one names the
     player.
   - A system built without the `PlayerCombat` it would hurt refuses the placement, as `Spawn`
     refuses a burn on the same system (M6-07b rule 2).
5. **The hazards have a table of their own, and a full one refuses silently.** The arrays are sized
   `Capacity + HazardCapacity`, and the count is kept per side.
   - **`Capacity` keeps its meaning and its throw**, now counted over the player's zones alone. A
     player's placements are bounded by cooldown arithmetic (Consecrate at 12 s for 6 s), and a ninth
     is still a bug.
   - **A death's placements are not bounded**, because any number of Volatile Elites can die in four
     seconds. So `PlaceHazard` answers `NoZone` at `HazardCapacity`, which is
     `ProjectileSystem.Fire`'s rule 7: a lost pool is better than an exception that ends the run.
   - **A hazard never displaces the player**: a full hazard table leaves every one of the player's
     eight slots free.
   - `PlayerCombat.DropPool`'s guard reads `PlayerZoneCount` instead of `Count`. Left alone it would
     refuse a Blink's pool beside eight hazards and let one through beside nine.
6. **A hazard does not cross a stage boundary, and its view is told.** `StageFlow.Advance` calls
   `ClearHazards()` beside `ProjectileSystem.Clear`, for that line's sentence: a pool on the last
   arena's floor burning the player in the next one is a bolt landing there.
   - **It publishes `ZoneExpired` per hazard**, unlike the silent sweeps. A view left standing in the
     next arena is the defect the `ProjectileViews` parking-lot line describes, and one call is the
     whole cost of not having it.
   - **The player's zones stand**, by M2-10's rule.
   - The Sanctum between the two needs nothing: `RunTicker.SanctumPhase` stops the run, so a pool
     cannot pulse while the player shops.
7. **A Splintered death throws its shards onto a ring through the player.** `Count` shards are evenly
   spaced on XZ.
   - **Aim:** the first follows the corpse-to-player bearing. A player standing on the corpse, closer
     than 1e-4 m, gets `+X` instead: a fixed axis, not a draw.
   - **Range:** every shard lands at the player's XZ distance, clamped to `[MinRange, MaxRange]`.
   - **Flight:** each is one `ProjectileSystem.Fire` of a `ShotSide.AtPlayer` shot, with source id 0
     (fired by a death, not a body) and the death's `SpecId`. Its speed is range ÷ `FlightSeconds`,
     so **every shard lands 0.8 s after the death wherever it lands**. The dodge window is the same
     at 2.4 m and at 12.
   - **Damage:** each shard deals the full `Strike`.

   GD §8.3's *"6-way projectile burst"* is read as a ring through the player rather than a fixed ring
   around the corpse. A fixed ring threatens only a player who happens to stand at its radius, and
   this one threatens every player, from the one spot they are standing on. GD §8.3's row is amended
   in this PR to say so.
8. **No point on the floor is inside two shards, by construction.** Neighbouring landing points at
   range *r* are `2r·sin(π/n)` apart, and two blasts of radius *R* touch when that equals `2R`. So
   `MinRange = R ÷ sin(π/n)` (2.4 m for six shards of 1.2) is the nearest the ring may land. At any
   allowed range **at most one shard can hit**, and one shard is the body's own strike. That keeps
   GD §12.4's one-shot rule where the archetype's strike already puts it, under
   [ledger row 2](../ROADMAP.md#carry-forward-into-m7)'s instrument, rather than tripling it for a
   player standing near the corpse. `BurstSpec` refuses a `MaxRange` below its `MinRange`.
9. **The shards are enemy shots in every other respect.** `IncomingProjectiles` counts them, so the
   Oathbound's Bulwark answers a burst as it answers a Spitter (CC §6.4). They hurt no enemy.
   `ProjectileViews` draws each as an arc to its point with nothing new, the same arc the Spitter's
   bolt has had since M2-09.
10. **A full sky refuses a shard silently, and the sky is doubled so it rarely has to.** `Fire` answers
    `NoProjectile` at capacity and the burst carries on (M2-07a rule 7). `BootInstaller.ProjectileCapacity`
    goes from 32 to 64 because the Emberwright's orb shares that sky. At 32, two Splintered deaths in a
    Spitter-heavy wave could refuse the player's own shot, which reads as a gun that stopped firing.
    64 is headroom rather than a proof: 28 bodies at one bolt each, four bursts, and the orbs. The cost
    is two struct arrays sized once at boot.
11. **The two assets.** GD §8.3 publishes Volatile's 4 s and Splintered's six. The rest are ours.

    | Asset | `_id` | Block | Why |
    |---|---|---|---|
    | `Volatile.asset` | `affix.volatile` | pool **2.5 m**, **4 s**, **0.25** of a strike a pulse | a quarter-strike every half-second: two strikes in all for a player who stands in it throughout |
    | `Splintered.asset` | `affix.splintered` | **6** shards, max **12 m**, **0.8 s**, blast **1.2 m** | 0.8 s is over GD §9.1's 0.6 s floor for a boss's tell; 12 m is the far edge of what the camera frames |

    Descent's pool is all five, in GD §8.3's order.
12. **A hazard is drawn in danger, and the player's zones exactly as before.** `ZoneSpawned` carries
    `IsHazard`. `ZoneView` draws a hazard in `Palette.Danger` and anything else in `Palette.Heal`, as it
    always has. **This is a view edit in a core task, and it is the one this task cannot defer.** A
    pool of danger drawn in the player's cyan is GD §16.4's worst failure, a hazard that reads as
    safe. It would also be on screen from this task's first manual step.
    - `EmberwrightTests.View_IsNotEdited` pinned *"the view cannot tell a zone's side"* as M6-07b's
      premise. It is rewritten to pin the rule that replaces it: the view can tell a hazard and
      nothing else. A heal and a burn still draw alike, as M6-07b ruled.
    - `SkillViewTests.Views_UseNoDangerColour` is scoped to the player's zones.
13. **Nothing on a frame path allocates.** `OnDeaths` walks a span, `PlaceHazard` writes into
    preallocated arrays, and a burst is `Count` struct constructions and `Fire` calls.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Death_CarriesItsAffixesAndItsStrike` | a Volatile Husk Elite at stage 20 killed / the drain / `Affixes.Pool` set, `Strike` 8 × d(20) — rule 1 |
| `DeathAffixes_APlainDeathPlacesNothing` | a plain Husk killed / `OnDeaths` / no zone, no shot — rule 2 |
| `DeathAffixes_DrawNothing` | two runs from one seed, one killing a Volatile and a Splintered Elite / — / every stream's position identical — rule 2 |
| `Pool_StandsWhereTheEliteFell` | killed at (4, 0, 7) / — / one hazard there, 2.5 m, 4 s, `ZoneSpawned.IsHazard` true — rule 3 |
| `Pool_EveryDoorLeavesOne` | killed by a cone, a bolt, a burn, a Wight and a blast / — / one pool each — rule 3 |
| `Pool_FirstPulseIsOneIntervalAway` | the player standing in it / 0.49 s, then 0.5 s / nothing, then one drain — rule 3 |
| `Pool_BurnsAQuarterStrikeAPulse` | stage 1, the player inside for 4 s / — / eight `PlayerDrained` of 2 — rules 3, 4 |
| `Pool_StartsNoIFrames` | the Oathbound in a pool, a Husk winding up / the strike lands between pulses / `PlayerDamaged` applied — rule 4 |
| `Pool_HurtsNoEnemy` | a Husk standing in it / 4 s / untouched — rule 4 |
| `Hazard_ANinthIsRefusedSilently` | eight hazards standing / a ninth `PlaceHazard` / `NoZone`, no event, no throw — rule 5 |
| `Hazard_NeverCrowdsOutThePlayer` | eight hazards standing / a Consecrate cast, then a Blink / both placed — rule 5 |
| `Hazard_TheNinthPlayerZoneStillThrows` | eight of the player's zones standing / a ninth `Spawn` / throws, as before — rule 5 |
| `Hazard_RefusedOnASystemThatCannotHurt` | a system built without enemies / `PlaceHazard` / throws — rule 4 |
| `Hazard_ClearedAtTheBoundaryAndSaidSo` | two hazards and a Consecrate standing / `Advance` / two `ZoneExpired`, the Consecrate stands — rule 6 |
| `Burst_SixShardsOnARingThroughThePlayer` | a Splintered death at the origin, the player at (6, 0, 0) / — / six `ProjectileFired`, targets 6 m out, 60° apart, the first at (6, 0, 0) — rule 7 |
| `Burst_EveryShardFliesItsFlightTime` | the player at 3 m, then 11 m / — / every flight 0.8 s — rule 7 |
| `Burst_NeverNearerThanItsMinimum` | the player at 1 m / — / the ring at 2.4 m — rules 7, 8 |
| `Burst_NeverFartherThanItsMaximum` | the player at 30 m / — / the ring at 12 m — rule 7 |
| `Burst_APlayerOnTheCorpseAimsAlongX` | the player 1e-5 m away / — / the first shard at (2.4, 0, 0) — rule 7 |
| `Burst_NoPointIsInsideTwoShards` | ranges from `MinRange` to `MaxRange` in 0.1 m steps / — / neighbouring targets ≥ 2 × 1.2 apart — rule 8 |
| `Burst_AShardHitsForTheBodysStrike` | a player who stands still / 0.8 s / one `PlayerDamaged` of the strike, never two — rules 7, 8 |
| `Burst_ShardsAreInbound` | a burst in the air / the next tick / `IncomingProjectiles` 6 — rule 9 |
| `Burst_HurtsNoEnemy` | a Husk on a landing point / — / untouched — rule 9 |
| `Burst_AFullSkyRefusesSilently` | 62 shots in the air / a burst / two fired, no throw — rule 10 |
| `DeathAffixes_AllocateNothing` | 10 000 drains carrying a pool and a burst each, zones and sky cleared between / `AllocationAssert.None` / zero — rule 13 |
| `Spec_BurstRefusesFewerThanTwoShards` | `count` 1 / — / throws — rule 8 |
| `Spec_BurstRefusesAMaximumInsideItsMinimum` | `maxRange` 2 with a 1.2 blast and six shards / — / throws, naming both — rule 8 |
| `ZoneView_DrawsAHazardInDanger` | `ZoneSpawned(isHazard: true)` / — / the quad's colour is `Palette.Danger` — rule 12 |
| `ZoneView_DrawsThePlayersZonesAsBefore` | a heal and a burn / — / both `Palette.Heal` — rule 12 |
| `View_TellsAHazardAndNothingElse` | *(rewritten from `View_IsNotEdited`)* `ZoneSpawned`'s fields and the views' / reflection / `IsHazard` is the one addition, and no field is a `ZoneSide` — rule 12 |
| `Affixes_TheDeathAssetsCarryTheDesignNumbers` | the two assets / converted / rule 11's table |
| `Descent_AuthorsAllFiveAffixes` | `Descent.asset` / converted / five ids in GD §8.3's order — rule 11 |

**Guard rows are implied, not listed:** `PoolSpec`'s and `BurstSpec`'s non-finite and non-positive
numbers, `PlaceHazard`'s non-finite position and clock, a null system at `DeathAffixes`'s door.

## Manual verification (Editor / device)

1. **[Editor]** Play past stage 12 and kill a Volatile Elite in melee. *Expected: a red-orange pool
   where it fell; half a second later, standing in it costs a point or two of HP a pulse and nothing
   else — Husks' strikes still land.*
2. **[Editor]** Kill a Splintered Elite from about eight metres. *Expected: six bolts arc out and land
   on a ring through where you stood, 0.8 s later; stepping sideways dodges the one aimed at you.*
3. **[Editor]** Kill a Volatile Elite last in a stage and take the door inside four seconds.
   *Expected: no pool in the next arena.*

## Out of scope

- **Two affixes on one body.** [M7-02f](M7-02f-a-second-affix-bought.md); a Volatile-and-Splintered
  Elite does both, by rule 2's walk, with nothing added.
- **A landing marker for enemy shots.** The shards land as the Spitter's bolt lands; a marker for every
  enemy shot is a readability change to all of them, and none is asked for.
- **The pool pulsing visibly.** `ZoneViews` pulses on `ZoneHealed`, and a hazard publishes
  `PlayerDrained`; the player's bar is the feedback.
- **Chained blasts.** A pool hurts no enemy (rule 4), which is `EnemySystem.Explode`'s ruling kept.
