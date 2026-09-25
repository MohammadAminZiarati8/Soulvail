# M7-01c — The Warden, and a hit that knows where it came from

**Size:** M · **Depends on:** M7-02b · **Branch:** `m7-01c-the-warden`
**Design refs:** GD §8.1, §8.2, §9.2, §12.4; CC §3.5, §3.6; AR §9, §18.1, §18.2, §18.4, §18.5; ADR-0003 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — one body added to its instrument; [11](../ROADMAP.md#carry-forward-into-m7) — why this task waits for M7-02b; AR §18.5's first soft spot, discharged (rule 9)

## Goal

GD §8.1's sixth archetype exists: a slow body behind a shield that turns slower than a player can
circle it, so that what hurts it is standing somewhere else — and every hit in the game now says
where it came from, which is the only way *"from behind"* can be true.

## Why it waits for M7-02b

The Warden costs **14**, the dearest archetype on the roster. Until [M7-02b](M7-02b-who-buys-an-elite.md)
lands, `WaveComposer.Upgrade` answers a capped wave by swapping every body for the dearest affordable
archetype — today twenty-eight Bloaters from stage ~26 ([ledger row 11](../ROADMAP.md#carry-forward-into-m7)),
and with this asset rostered, twenty-eight **Wardens**: an arena of shields turned to face the player,
which is not a hard stage but an unwinnable one. The `Depends on` column is what keeps that build from
existing.

## What was already built for it

Four seams, each still reserved and each found by grepping:

- `EnemyAgent.IsVulnerable` — *"True for everything in M1 — the first archetype that lowers it is the
  Warden, whose front shield blocks all damage (GD §8.1, M7-01)."*
- `EnemySystem.ApplyDamage`'s remarks — *"a `DamageResult.Blocked` result is published, because
  something arrived and was turned away, which is the one thing M7-01's Warden needs the game to say
  out loud."*
- `Targeter` — CC §3.6 in full, including *"if every candidate is blocked, hold facing on the nearest
  one and keep the glyph up"*, built at M1-04 against an enemy that did not exist.
- AR §18.5 — *"`IsCurrentBlocked` suppressing the invulnerability retarget has no test — bites whenever
  a Warden-like enemy exists."*

**What was not built is a direction.** `ApplyDamage(enemyId, amount, now, player)` is told who and how
much, never from where, and core stores no enemy facing at all — `EnemyMoveIntent.FacingXZ` leaves for
the body and is not kept. Both arrive here.

## The name, and the body the boss wears

`enemy.warden` is taken: `Data/Enemies/Warden.asset` is the **Warden of Ash's body** — 3 200 HP,
`Boss`-kind, named *"The Warden of Ash"*. GD §9.2 calls the boss *"a Warden that grew"*, so the
archetype has the better claim to the plain name. **`Warden.asset` becomes the archetype, keeping its
file and its GUID**, and the boss's body moves to a new `WardenOfAsh.asset` (`enemy.warden_of_ash`)
that `WardenBoss.asset` repoints to. No asset is moved or renamed: `BootScope.prefab` is the only
thing holding `Warden.asset`'s GUID, and it now holds the archetype it names. **What the move costs,
counted:** three tree fixtures load the boss's body *by path* to read its hit points
(`OathboundTreeTests`, `GravecallerTreeTests`, `EmberwrightTreeTests`) and must follow it — left
alone they would read 90 where they meant 3 200 and still compile. Eight fixtures declare a
`const "enemy.warden"` for a boss body they build themselves; those ids are theirs and are untouched.
A profile that met the boss before this task lists `enemy.warden` in `MetArchetypeIds`, so its
owner is paid no first-meeting Shard for the archetype — one term of GD §14.1, once, on one install.

**The behaviour is named for what it does**, as `ChaserBehaviour` drives the Husk:
`EnemyBehaviourKind.Shielded` and `ShieldedBehaviour`. The boss's `WardenBehaviour` keeps its name
and its file.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/GuardSpec.cs` | Core | **New.** The shield: how wide its arc, how fast it turns |
| `Core/Ai/ShieldedBehaviour.cs` | Core | **New.** A Chaser that turns at a capped rate and strikes only what is in front of it |
| `Tests/Core/Ai/ShieldedBehaviourTests.cs` | Tests.Core | **New.** The behaviour, the guard, every source, and the targeting row AR §18.5 asked for |
| `Core/Ai/EnemySystem.cs` | Core | `ApplyDamage` takes a source and asks the guard first (rules 3–5) |
| *small edits* | Core, Game, Data, Docs | `Core/Content/EnemySpec.cs` — `Shielded` appended, a `guard` block optional and last, the kind requiring it; `Core/Ai/EnemyAgent.cs` — one arm in `Initialise`; `Core/Ai/EnemyBlackboard.cs` — `Facing`, working memory (rule 2), and its remark that the `TargetId` rename is *"the Choir, M7-01"* re-aimed to GD §19's V2, where the Choir is (the same sentence in `MinionSystem`'s remarks goes with that file's one-argument edit; `LureSystem`'s is left for the next task that opens it); `Core/Combat/PlayerCombat.cs` — the cone and the Charge pass the player's position and skip a blocked hit's knockback (rule 6); `Core/Combat/ProjectileSystem.cs`, `Core/Combat/ZoneSystem.cs`, `Core/Ai/MinionSystem.cs`, `Core/Ai/BloaterBehaviour.cs` — one argument each (rule 4); `Game/Authoring/EnemyDefinition.cs` — two fields under a *Guard* header; `Game/Authoring/BossDefinition.cs` — two comments that name the boss's body; `Data/Enemies/Warden.asset` — **re-authored** as the archetype (rule 8); `Data/Enemies/WardenOfAsh.asset` — **new**, the boss's body exactly as `Warden.asset` held it; `Data/Enemies/WardenBoss.asset` — `_enemySpecId`; `Data/Modes/Descent.asset` — the Warden at stage 11; `Prefabs/Composition/BootScope.prefab` — `_enemies` gains `WardenOfAsh`; `Data/Localisation/English.asset` — `enemy.warden.name` becomes *"Warden"*, `enemy.warden_of_ash.name` is *"The Warden of Ash"*; `Pseudo.asset` regenerated; `Docs/Architecture.md` — two §18.4 rows, and §18.5's first soft spot struck (rule 9) |
| *ripple* | Tests.Core, Tests.Game | **37 `ApplyDamage` call sites across 14 test files** gain a fifth argument — compiler-guided; the three tree fixtures' boss path; comments in `WardenBehaviourTests`, `ShockwaveAndFissureTests`, `BossSpecTests` and `BossBarViewTests` that call the boss's body `Warden.asset`; the M7-01b pins count seven. **62 `new EnemySpec(...)` sites are untouched** |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

public enum EnemyBehaviourKind
{
    Static, Chaser, Spitter, Bloater, Boss, Lunger,

    /// <summary>
    /// Walks in behind a shield that turns at a capped rate, and strikes what is in front of it — GD
    /// §8.1's Warden. <c>ShieldedBehaviour</c> (M7-01c). Appended: 6.
    /// </summary>
    Shielded,
}

/// <summary>A shield on the front of a body: how wide, and how fast the body can bring it round.</summary>
public sealed class GuardSpec
{
    /// <param name="arcDegrees">The front arc a hit is turned away from, centred on the facing. In (0, 360). 120 for the Warden.</param>
    /// <param name="turnRateDegrees">How fast the body turns, in degrees a second. Greater than zero. 60 for the Warden.</param>
    public GuardSpec(float arcDegrees, float turnRateDegrees);

    public float ArcDegrees { get; }
    public float TurnRateDegrees { get; }

    /// <summary>cos(arc ÷ 2), computed once — what the dot product in rule 3 compares against.</summary>
    public float HalfArcCosine { get; }
}

public sealed class EnemySpec
{
    // ... after M7-01b's split, defaulted and last:  GuardSpec guard = null
    // and Shielded with guard == null throws.
    public GuardSpec Guard { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemyBlackboard
{
    /// <summary>
    /// Which way the body is facing, as its own behaviour last turned it — unit, or zero before the
    /// first tick. Written only by a behaviour that turns at a rate; read by the guard (rule 3).
    /// </summary>
    public Vector2 Facing;
}

public enum ShieldedState { Idle, Advance, Windup, Strike, Recover }

public sealed class ShieldedBehaviour : IEnemyBehaviour
{
    public ShieldedBehaviour(EnemyAgent agent);
    public ShieldedState State { get; }
    public void Tick(in EnemyTickContext ctx);
    public void Reset();
}

public sealed class EnemySystem
{
    /// <param name="source">
    /// Where the damage came from, in world metres: the player for a cone or a Charge, the shot's
    /// origin, the zone's centre, the Wight's position, the enemy's own for its own fuse. Required —
    /// the compiler enumerates every door a direction has to reach (rule 4).
    /// </param>
    public DamageResult ApplyDamage(int enemyId, float amount, float now, PlayerCombat player, Vector3 source);
}
```

## Behaviour

1. **`ShieldedBehaviour` is the Chaser with its facing slowed, and it strikes only forward.** `Idle`
   → `Advance` on `AggroRange`; `Advance` walks the path direction at `MoveSpeed.Value` while the
   facing turns toward `DirectionToPlayer` at `Guard.TurnRateDegrees`; `Windup` begins only when the
   quarry is within `Spec.Reach` **and** inside the arc, publishes `EnemyTelegraph`, and cancels on
   the Chaser's 1.5 × reach band; `Strike` lands the Chaser's way; `Recover` returns to `Advance`. **A
   player standing behind it is out of its reach by construction** — it has to turn first, which is
   the whole of *"forces flanking"*. It walks *sideways* while it turns: the velocity is the path and
   the facing is the shield, and `EnemyMoveIntent` has always carried the two separately.
2. **The facing is working memory on the blackboard, turned at a capped rate, and it leaves in the
   intent.** `EnemyBlackboard.Facing` is written every tick by rotating toward the target direction by
   at most `TurnRateDegrees × dt`, and the same vector is the intent's `FacingXZ`, so the body the
   player sees and the shield core tests are one direction. A zero facing — a fresh spawn — snaps to
   `DirectionToPlayer` on the first tick rather than turning from nowhere. AR §9's write discipline
   holds: the behaviour writes it, `Reset` zeroes it, and the census only reads it.
3. **A hit is turned away when its source is inside the front arc, and the test is a dot product.**
   `ApplyDamage` asks the guard before `Health`: the spec carries a `GuardSpec`, the agent is alive,
   `Facing` is not zero, the XZ direction from the body to `source` is at least `1e-4` m long, and
   `dot(Facing, direction) >= Guard.HalfArcCosine`. Then the result is `Blocked` — nothing applied,
   nothing killed — and `EnemyDamaged(id, 0, fraction, killed: false)` goes out exactly as a
   `Health`-blocked hit's always has, so a view that flashes a blocked hit needs nothing new. **A
   source at the body's own position is never guarded**: a pool of fire under its feet reaches it,
   which is the honest answer to *"which side did that come from"*.
4. **The source is required, and every door names its own.** Six production callers, each passing
   what the damage physically came from:

   | Caller | Source |
   |---|---|
   | `PlayerCombat.ResolveConeHits` | `Blackboard.PlayerPosition` — written by this tick's `UpdateBlackboard`, and the fact phase follows the tick |
   | `PlayerCombat.ResolveChargeHits` | the same |
   | `ProjectileSystem.LandOnEnemies` | `shot.Origin` — a bolt arrives from the direction it was fired from, so an orb lobbed over the shield still meets it from the front |
   | `ZoneSystem.Burn` | the zone's centre |
   | `MinionSystem.Strike` | the Wight's position — a Gravecaller's army flanks for them |
   | `BloaterBehaviour`'s fuse | its own position — rule 3's self case, and a Bloater carries no guard anyway |

   **Required rather than defaulted**, for `player`'s reason at M2-08: a defaulted source is a door a
   future caller walks through without deciding where the damage came from, and a guard would then
   read `Vector3.Zero` as a direction. The **37 test sites across 14 files** pass a position the
   compiler asks them for.
5. **`IsVulnerable` is the targeter's question, and it is asked of the real player.**
   `ShieldedBehaviour` lowers `IsVulnerable` on every tick the real player — `EnemySystem.PlayerPosition`,
   M7-01a rule 6, never the quarry — stands inside the arc, and raises it on every tick they do not.
   So `TargetScorer` scores a Warden that faces the player out of contention and picks the next-best
   target, and when every candidate is facing them `Targeter` holds on the nearest with
   `IsCurrentBlocked` up — CC §3.6's *"go around"*, built at M1-04 and reached for the first time
   here. **Nothing in `Targeter` or `TargetScorer` changes.** A Wight or a zone may still hurt a
   Warden the player could not: `IsVulnerable` answers for the player's weapon, and rule 3 answers
   for every hit.
6. **A hit the guard turned away moves nobody.** `ResolveConeHits` already holds each result and
   gains `!result.Blocked` on its knockback line; `ResolveChargeHits` starts reading the result for
   the same test. A shield that turned a Charge away and was then shoved four metres by it would be a
   shield in name — and GD §8.1's *"forces flanking"* has no meaning if the head-on answer is to push
   the Warden out of the way.
7. **Nothing on the per-frame path allocates.** The rotation is two `MathF` calls and a clamp on a
   struct; the guard is a subtraction, a length and a dot product.
8. **`Warden.asset`'s numbers.** GD §8.1 publishes three.

   | Field | Value | Why |
   |---|---|---|
   | `_maxHp` | **90** | GD §8.1 — **seven** Censer hits, above §12.4's 3–5 band, and GD's own number; §12.4 is written about a *basic* enemy and the Warden is a wall you are meant to walk round, so the band is stated as not applying rather than tuned to. M8-05 measures it |
   | `_threatCost` / `_targetPriority` | **14** / **3** | GD §8.1 |
   | `_xpValue` | **42** | 3 × cost |
   | `_moveSpeed` | **1.6** | the slowest body in the game |
   | `_contactDamage` | **13** | inside the envelope; past the Emberwright's ceiling from stage 27 — M7-01a rule 10's gap |
   | `_reach` / `_windupTime` / `_recoverTime` | **1.8 / 0.7 / 1.0** | a shield bash: a longer reach, a longer tell, a longer punish |
   | `_guardArcDegrees` | **120** | the flanks are open; only the front 120° is shielded |
   | `_guardTurnRateDegrees` | **60** | at 3.0 m/s a player circles at **86°/s** at 2 m and **34°/s** at 5 m — close in you out-turn it, far out you cannot, and that is the lesson |
   | `_behaviour` | **Shielded** | rule 1 |
   | `_tint` / `_bodyScale` | **(0.32, 0.38, 0.50)** / **1.3** | slate, broad; the boss's own dark tint and 2.2 move to `WardenOfAsh.asset`. M7-01d owns the look |

9. **AR §18.5's first soft spot gets its test, and the row leaves.** `Targeter`'s re-choice condition
   ends `|| (!current.IsVulnerable && !IsCurrentBlocked)` — so a target already held *as blocked* is
   not re-chosen every tick. That branch has had no test since M1-04 because nothing could lower
   `IsVulnerable` outside a boss's beat. `Targeting_AHeldBlockedTargetIsNotReChosenEveryTick` pins it
   against a real Warden, and the soft-spot row is struck in the same PR.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Warden_TurnsAtItsRate` | facing east, the player due north / 0.5 s / facing has turned 30°, not 90° — rule 2 |
| `Warden_SnapsOnItsFirstTick` | a fresh spawn / one tick / `Facing` is `DirectionToPlayer` — rule 2 |
| `Warden_WalksSidewaysWhileItTurns` | the player circling / a tick / the intent's velocity is the path, its facing the shield — rule 1 |
| `Warden_StrikesOnlyForward` | the player in reach but 150° off the facing / ticks / no `Windup` until the facing brings them inside the arc — rule 1 |
| `Warden_StrikeLandsLikeAChasers` | the player in front, in reach / windup and strike / one `PlayerDamaged` of 13 — rule 1 |
| `Guard_TurnsAwayAHitFromTheFront` | a cone from dead ahead / `ApplyDamage` / `Blocked`, HP unchanged, one `EnemyDamaged(id, 0, 1, false)` — rule 3 |
| `Guard_LetsAHitFromBehindThrough` | the same hit from behind / — / applied — rule 3 |
| `Guard_TheEdgeOfTheArcIsInclusive` | a source at exactly 60° / — / blocked — rule 3 |
| `Guard_ASourceUnderItsFeetLands` | a source 1e-5 m away / — / applied — rule 3 |
| `Guard_AFreshSpawnWithNoFacingGuardsNothing` | `Facing` zero / a hit from anywhere / applied — rule 3 |
| `Sources_EveryDoorPassesItsOwn` | the six callers against a Warden facing the player / each in turn / the cone, the Charge and a bolt from the player are blocked; a burn centred behind, a Wight behind and a bolt fired from behind land — rule 4 |
| `Vulnerability_FollowsTheRealPlayer` | the player moves from front to back / ticks / `IsVulnerable` false then true — rule 5 |
| `Vulnerability_IgnoresADecoy` | a decoy in front, the player behind / — / `IsVulnerable` true — rule 5 |
| `Targeting_PicksTheNextBestPastAFacingWarden` | a Warden facing the player at 3 m, a Husk at 8 m / a targeting tick / the Husk — rule 5, CC §3.6 |
| `Targeting_HoldsTheNearestWhenEveryoneIsFacing` | two Wardens, both facing / — / the nearer, `IsCurrentBlocked` true — rule 5 |
| `Targeting_AHeldBlockedTargetIsNotReChosenEveryTick` | the held blocked Warden and a second blocked one moved nearer / the next scheduled tick only / the choice changes then and not before — rule 9, AR §18.5 |
| `Knockback_ABlockedConeShovesNobody` | `SwingKnockback` above 0, a blocked cone / — / no `EnemyKnockbackIntent` for the Warden — rule 6 |
| `Knockback_ABlockedChargeShovesNobody` | a Charge into a facing Warden / — / no knockback — rule 6 |
| `Warden_TickAllocatesNothing` | a full cycle, 10 000 ticks, and 10 000 guarded hits / `AllocationAssert.None` / zero — rule 7 |
| `Spec_AShieldedKindRequiresItsGuard` | `Shielded` with no `GuardSpec` / — / throws, naming it |
| `Kind_ShieldedIsSix` | — / — / `(int)EnemyBehaviourKind.Shielded == 6` |
| `Warden_AssetCarriesTheDesignNumbers` | `Warden.asset` / converted / rule 8's table, kind `Shielded`, id `enemy.warden` |
| `WardenOfAsh_IsTheBossesBodyUnchanged` | `WardenOfAsh.asset` / converted / every number `Warden.asset` held before this task, id `enemy.warden_of_ash`; `WardenBoss.asset` names it |
| `Descent_RostersTheWardenAtEleven` | `Descent.asset` / — / `enemy.warden` at 11; `enemy.warden_of_ash` on no roster |

**Guard rows are implied, not listed:** `GuardSpec`'s arc outside (0, 360) and a non-positive or
non-finite turn rate; a non-finite `source`.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 11. *Expected: one Warden in wave 1. The Censer's arc does nothing to
   it face-on — the reticle goes hollow — and circling it close lets hits land from the side and
   behind.*
2. **[Editor]** Stand still at 5 m. *Expected: it turns to face you and walks in; the auto-aim picks
   something else while it does — rule 5.*
3. **[Editor]** As the Gravecaller, let the Wights engage a Warden that is facing you. *Expected: the
   Wights' strikes from behind land — rule 4's army row.*
4. **[Editor]** Stage 5. *Expected: the Warden of Ash exactly as before this task — same name, same
   size, same fight.*

## Out of scope

- **Drawing the shield, the hollow reticle, or a blocked flash.** [M7-01d](M7-01d-three-archetypes-a-player-can-read.md);
  `EnemyDamaged` and `TargetChanged.IsBlocked` already carry what it needs.
- **A guard on anything else.** An Elite affix or a boss that guards authors a `GuardSpec`; no second
  mechanism is needed, and none is written here.
- **A slower or faster facing for the other archetypes.** Every other behaviour faces instantly, as it
  always has; only a body with a guard has a reason to be slow.
- **Renaming `WardenBehaviour`.** It drives the boss and keeps its name.
