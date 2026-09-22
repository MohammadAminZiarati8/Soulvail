# M5-01 — A weapon that throws something, and the lead that makes it hit

**Size:** M · **Depends on:** M2-07a, M1-10, M1-11 · **Branch:** `m5-01-projectile-weapon`
**Design refs:** CC §3.7, §4.1–4.2, §6.2; GD §6.2, §8.1; AR §3, §18.1, §18.3, §18.4; ADR-0003, ADR-0006, ADR-0008 · **Ledger rows:** none — this task carries neither of [M5's two forcing questions](../ROADMAP.md#m5--second-class) and says so in *Which forcing question this answers* below

## Goal

`WeaponKind.Projectile` stops being a comment. A class whose basic attack is a shot fires one at
where its target **will be**, the shot flies, and what it lands on is an enemy — so CC §3.7's seam
is built and used on the same day rather than built at M2 and discovered wrong at M6.

## Which forcing question this answers

**Neither.** The tree's branch-index limitation is M5-07a-i's and is specced by M5-00b; `IStatBlock`
for a minion is [M5-04b](M5-04b-rise-and-minion-stats.md)'s. What this task carries instead is the
**one-directional `ProjectileSystem`**, found by grep at M5-00a and stated here because it is the
reason this is size M rather than size S: `ProjectileSystem.Land` tests the *player's* position
against the radius and calls `PlayerCombat.ApplyDamage`, with no branch for anything else. Every
shot in the game today is fired **at** the player. Rule 4 is that fact turned into a task.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ProjectileLead.cs` | Core | CC §3.7's two-iteration solve, as a pure static function of four numbers |
| `Core/Combat/ProjectileSystem.cs` | Core | **Substantial.** A shot gains a side; `Land` branches on it |
| `Tests/Core/Combat/ProjectileLeadTests.cs` | Tests.Core | The arithmetic, its convergence, and its refusals |
| `Tests/Core/Combat/PlayerProjectileTests.cs` | Tests.Core | A weapon that shoots: cadence, lead, flight, and an enemy that takes it |
| *small edits* | Core, Game | `WeaponKind` gains `Projectile`; `WeaponSpec` gains three **defaulted** shot numbers and one kind-conditional validation (rule 2); `PlayerCombat` gains `PendingShot` and a branch in `TickWeapon`; `RunSession.Tick` fires it; `CharacterDefinition` gains three `[SerializeField]`s |
| *ripple* | Tests.Core | `ProjectileSystemTests` — every `new Projectile(...)` gains the side argument |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Combat/ProjectileLead.cs
/// CC §3.7's aim point. Pure arithmetic: no clock, no registry, no allocation.
public static class ProjectileLead
{
    /// <summary>How many times the flight time is re-solved. CC §3.7's "iterate twice".</summary>
    public const int Iterations = 2;

    /// <summary>
    /// Where to aim so that a shot of <paramref name="speed"/> leaving <paramref name="origin"/>
    /// meets a target at <paramref name="targetPosition"/> moving at <paramref name="targetVelocity"/>.
    /// XZ only (AR §18.4). Returns <paramref name="targetPosition"/> unchanged when there is no
    /// solution worth having — rule 3.
    /// </summary>
    public static Vector3 Solve(
        Vector3 origin, Vector3 targetPosition, Vector3 targetVelocity, float speed);
}

// Core/Combat/ProjectileSystem.cs — widened
/// <summary>Who a shot may hurt. Not a faction system: two members, and there is no third.</summary>
public enum ShotSide
{
    /// <summary>Fired at the player. Every shot in the game before M5-01.</summary>
    AtPlayer,

    /// <summary>Fired by the player, at enemies.</summary>
    AtEnemies,
}

public readonly struct Projectile
{
    public Projectile(
        ContentId specId, int sourceId, Vector3 origin, Vector3 target,
        float speed, float radius, float damage,
        ShotSide side = ShotSide.AtPlayer);          // rule 5

    public ShotSide Side { get; }
}

public sealed class ProjectileSystem
{
    /// <param name="enemies">
    /// Who an <see cref="ShotSide.AtEnemies"/> shot may reach. Passed in rather than held, for the
    /// reason <c>PlayerCombat.ResolveConeHits</c> is handed the world each time it is asked about it.
    /// </param>
    public void Tick(float now, Vector3 playerPosition, PlayerCombat player, EnemySystem enemies);
}

// Core/Content/WeaponSpec.cs — widened
public sealed class WeaponSpec
{
    public WeaponSpec(
        WeaponKind kind, float damage, float swingsPerSecond, float range,
        float coneAngleDeg, float damageFrame,
        float shotSpeed = 0f, float shotRadius = 0f, float shotSpread = 0f);   // rule 2

    /// <summary>Metres per second of flight. Zero on a <see cref="WeaponKind.Cone"/>.</summary>
    public float ShotSpeed { get; }

    /// <summary>Metres from the impact point that still count. Zero on a cone.</summary>
    public float ShotRadius { get; }

    /// <summary>Reserved and required to be zero — rule 9.</summary>
    public float ShotSpread { get; }
}

// Core/Combat/PlayerCombat.cs — widened
/// <summary>The shot this tick's damage frame produced, or null. Read and cleared by the run.</summary>
public Projectile? PendingShot { get; private set; }

/// <summary>Takes the pending shot, leaving none behind.</summary>
public bool TryTakeShot(out Projectile shot);
```

## Behaviour

1. **`WeaponKind` gains `Projectile` and the weapon's *timing* does not change at all.** `Weapon`
   already decides "when a swing starts and when its damage lands" and its own remarks say the
   projectile weapon "differs in what it asks for and not in when" — so `Weapon.cs` is **not in the
   Files table** and must not be edited. A damage frame is a damage frame; what changes is what
   `PlayerCombat` does with one.
2. **The three shot numbers are defaulted parameters on `WeaponSpec`, and that is what keeps the
   ripple at nothing.** `new WeaponSpec(...)` has **forty-five call sites under `Tests/`** plus one
   in `Soulvail.Game`, every one of them `WeaponKind.Cone`, and M4-01a rule 4's precedent is the same
   trade for the same reason. Validation is **kind-conditional**: on `Projectile`, `shotSpeed` and
   `shotRadius` must each be a finite number greater than zero **and `coneAngleDeg` must be exactly
   360**; on `Cone`, all three shot numbers must be exactly zero. A cone with a shot speed is a
   forgotten field and must not be silently ignored, and a shot with a 60° arc is a number nothing
   reads — 360 is the one value that says *"the angle does not gate this weapon"*, and it is what
   `PlayerCombat.ConeAngle` already clamps to.
3. **The lead is CC §3.7 verbatim, and it fails by returning the target's position.** `t = d / v`,
   `aim = p + vel·t`, twice. It is XZ only (AR §18.4 — a muzzle height is a rendering detail and
   counting it inflates every flight time). It returns `targetPosition` unchanged when the speed is
   not finite and positive, when either position is non-finite, or when the velocity is non-finite —
   **never a NaN aim point**, because a NaN target makes a shot that never lands and nothing reports
   it, which is exactly the failure `Projectile`'s own door refuses. A stationary target solves to
   itself on the first iteration and the second changes nothing, which is the row that proves
   convergence is not a loop that has to terminate.
4. **`ProjectileSystem.Land` branches on the side, and the two branches are not symmetrical.** An
   `AtPlayer` shot is unchanged: one distance test against `playerPosition`, `player.ApplyDamage`,
   and `ProjectileImpacted` published either way. An `AtEnemies` shot walks `enemies.Registry.Alive`
   and applies the shot's damage to **every living agent inside the radius** — a bolt is a small
   blast, not a single-target hit, which is the same bargain the Censer's arc makes and what keeps
   one number honest. `ProjectileImpacted.Hit` is true when it reached at least one. The walk is
   bounded by the enemy capacity and allocates nothing.
5. **`Side` is a defaulted parameter and there is no third member.** Every existing `Projectile`
   construction keeps meaning what it meant. `ShotSide` is deliberately not a faction, a team or a
   layer mask: M5-04a's Wights do **not** fire, so nothing in M5 asks for a third value, and
   inventing one would be M7's Archon arriving early (CH §8.5).
6. **`PlayerCombat` fires nothing; it *offers* a shot.** On a damage frame, a `Cone` weapon emits the
   `ConeHitIntent` it always has, and a `Projectile` weapon instead writes `PendingShot` — origin the
   player's position, target `ProjectileLead.Solve(...)` against the current target's live
   `EnemyAgent.Position` and `EnemyAgent.Velocity`, speed and radius off the spec, damage
   `Weapon.Damage.Value`, side `AtEnemies`, `sourceId` 0. **This class deliberately owns no
   `ProjectileSystem`**, for the reason it owns no registry: it is handed the world each time it is
   asked to think about it, and a constructor argument here would ripple through every fixture that
   builds one.
7. **`RunSession.Tick` takes the shot immediately after the combat step and above the skills block.**
   A shot decided this tick is in the air this tick, and it cannot arrive on the tick it left because
   `State.Projectiles.Tick` runs after the enemy behaviours — the one-frame grace M2-07a rule 10
   gives every bolt, now given to the player's. Nothing else about AR §18.1's order moves, and
   `Tests/PlayMode/FrameOrderTests.cs` is **not** in the Files table.
8. **No target, no shot.** A `Projectile` weapon with nothing in range produces no damage frame at
   all — `Weapon.Tick` is already handed `targetInRange` and refuses to start a swing without it — so
   there is no path where the lead is solved against a target that does not exist. A target that dies
   between the swing start and the damage frame yields no shot rather than a shot at the last known
   point: a cone's "not a commitment" cuts both ways because the wedge resolves against whoever is
   standing there, and a bolt aimed at a corpse would land on nobody and look like a miss the player
   did not make.
9. **`ShotSpread` ships required-to-be-zero, and that is a decision about seeds rather than about
   aim.** `ProjectileSystem`'s own remarks say a spread "is a change to what a seed means
   (ADR-0011)"; the field exists so that the day one is wanted it is a validation change rather than
   a spec widening, and it is refused today so it cannot become a number nobody draws for.
10. **Nothing here allocates on the frame path.** `ProjectileLead.Solve` is four multiplies and a
    square root over two `Vector3`s; `Land`'s enemy walk is over a `ReadOnlySpan`; `PendingShot` is a
    `Projectile?`, a struct in a nullable, and is overwritten rather than queued.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Lead_AStationaryTargetSolvesToItself` | velocity zero, any speed / `Solve` / the target's own position, exactly, both iterations |
| `Lead_ACrossingTargetIsLedAhead` | a target at 10 m moving 3 m/s perpendicular, shot at 40 m/s / `Solve` / the aim point is ahead of the target along its velocity, and the residual — `\|aim − (p + vel·(\|aim−origin\|/speed))\|` — is under 1 cm |
| `Lead_TheSecondIterationImprovesTheFirst` | the same crossing target / one iteration vs two / the two-iteration residual is strictly smaller — CC §3.7's "iterate twice" measured rather than quoted |
| `Lead_ARetreatingTargetIsStillReachable` | a target fleeing at 3 m/s from 10 m, shot at 40 m/s / `Solve` / finite, and further than the target |
| `Lead_ATargetFasterThanTheShotDoesNotDiverge` | a target at 60 m/s, shot at 40 m/s / `Solve` / finite and within 200 m — two iterations cannot run away even where no solution exists |
| `Lead_RefusesNonFiniteInputs` | NaN speed, NaN position, NaN velocity, zero speed, infinite speed / `Solve` / returns `targetPosition` unchanged in every case, never NaN |
| `Lead_IgnoresHeight` | two targets differing only in Y / `Solve` / the same XZ aim point — AR §18.4 |
| `Lead_AllocatesNothing` | 100 000 solves / `AllocationAssert.None` / zero |
| `Spec_AProjectileWeaponRequiresItsShotNumbers` | `WeaponKind.Projectile` with `shotSpeed` 0, then `shotRadius` 0, then either non-finite / constructed / throws, and the message names the field and the kind |
| `Spec_AConeRefusesShotNumbers` | `WeaponKind.Cone` with `shotSpeed` 12 / constructed / throws — rule 2's forgotten-field guard |
| `Spec_AProjectileRequiresAFullCircle` | `WeaponKind.Projectile` with `coneAngleDeg` 60 / constructed / throws; the same spec at 360 is accepted — rule 2's unread-number guard |
| `Spec_TheShippedConeIsUnchanged` | the six-argument constructor, exactly as twenty fixtures call it / constructed / `ShotSpeed`, `ShotRadius` and `ShotSpread` are all zero and nothing else moved |
| `Spec_SpreadMustBeZero` | any kind, `shotSpread` 0.1 / constructed / throws — rule 9 |
| `Weapon_AProjectileWeaponKeepsTheConesCadence` | the same damage frame and interval on both kinds / ticked 10 s / the identical count and timing of damage frames — rule 1, asserted rather than assumed |
| `Combat_ADamageFrameOffersAShot` | a projectile weapon, one enemy in range / ticked to the damage frame / `PendingShot` is set once, `TryTakeShot` empties it, and **no `ConeHitIntent` was written** |
| `Combat_AConeWeaponOffersNoShot` | the shipped Oathbound / ticked through several swings / `PendingShot` is null on every tick and the cone intents are unchanged |
| `Combat_TheShotIsLedAtTheTargetsVelocity` | a target walking 3 m/s across / a damage frame / the shot's `Target` equals `ProjectileLead.Solve` against that agent's live position and velocity — not the agent's position |
| `Combat_NoTargetProducesNoShot` | a projectile weapon, an empty arena / ticked 5 s / no damage frame and no shot |
| `Combat_ATargetThatDiedProducesNoShot` | target killed between swing start and damage frame / ticked / no shot, and no throw — rule 8 |
| `Shot_LandsOnEveryEnemyInTheRadius` | three agents, two inside the radius / an `AtEnemies` shot arrives / both inside take the damage, the third takes none, and one `ProjectileImpacted` with `Hit` true |
| `Shot_AtEnemiesNeverTouchesThePlayer` | the player standing on the impact point / an `AtEnemies` shot arrives / the player's HP is unchanged |
| `Shot_AtPlayerIsUnchanged` | the Spitter's shot, as `ProjectileSystemTests` already builds it / arrives / identical damage, identical event, identical blackboard count — the widening asserted to be a widening |
| `Shot_AMissPublishesAnImpact` | an `AtEnemies` shot with nobody inside the radius / arrives / one `ProjectileImpacted` with `Hit` false, because the view has to stop existing either way |
| `Shot_AKillFromABoltPaysXpOnce` | one agent at 1 HP inside the radius / a shot arrives / exactly one `EnemyDied` and the XP drained once |
| `Shot_DoesNotDamageACorpse` | an agent dead this tick, still in the registry for `CorpseTime` / a shot arrives on it / nothing applied |
| `Shot_TickAllocatesNothing` | 10 000 ticks with shots of both sides in flight / `AllocationAssert.None` / zero |
| `Run_TheShotIsInTheAirTheTickItWasDecided` | a projectile weapon, a damage frame / one `RunSession.Tick` / `InFlightCount` is 1 at the end of that tick and the shot has **not** landed — rule 7's one-frame grace |

**Guard rows are implied, not listed:** a null `EnemySystem` to `Tick`, `Enum.IsDefined` on
`ShotSide` and on the widened `WeaponKind`, and a non-finite `now`.

## Manual verification (Editor / device)

_None._ Nothing in `Soulvail.Game` renders differently: the Oathbound is the only authored class and
it is a cone, so every run this build plays is byte-identical in behaviour. `ProjectileView` and
`ProjectileViews` already draw a `ProjectileFired` and are untouched — the first time anything is
seen is [M5-02](M5-02-gravecaller-and-bone-bolt.md), which authors a weapon that uses this.

## Out of scope

- **The Gravecaller, Bone Bolt, and any authored asset.** M5-02. This task ships the machinery and
  no content that walks through it — M4-01a's bargain, and what keeps the review honest.
- **A view for a player's bolt.** `ProjectileViews` is already the pool for `ProjectileFired` and
  needs nothing; whether a player's bolt should look different from a Spitter's is M5-05b's.
- **Manual aim / Precision mode (CC §3.8).** The lead exists for the auto-targeter; a thumb-aimed
  shot is not led at all, and CC §3.8 is V1-but-not-M5.
- **Spread, multi-shot, piercing, bouncing.** Rule 9 refuses the first and the rest have no design
  behind them yet.
- **Enemies leading their shots.** The Spitter fires at the player's *current* position and GD §8.1
  wants it dodgeable; leading an enemy shot would remove the dodge the archetype exists for.
- **`ShotSide.AtMinions`, or any third member.** Rule 5.
- **Any change to `Weapon.cs`.** Rule 1.

## As built

**Built as specced, four counted files, and every rule landed where it said it would.** `ProjectileLead.Solve`
is CC §3.7 twice over, XZ only, keeping the target's Y; `ProjectileSystem.Land` split into
`LandOnPlayer` (M2-07a's code, moved not changed) and `LandOnEnemies` (a walk of `Registry.Alive`);
`WeaponSpec` gained the three defaulted numbers and kind-conditional validation; `PlayerCombat.TickWeapon`
became a two-branch switch over `ThrowCone` and `OfferShot`; `RunSession.Tick` takes the shot between the
combat step and the skills block. **2 228 EditMode / 0 / 0** — thirty-one new rows — and **PlayMode 16 / 0 / 0**.

**Four deviations, three of them beyond the Files table.**

1. **`ProjectileImpacted.HitPlayer` was renamed `Hit`** — `Core/Events/ProjectileEvents.cs`, plus eight
   reads in `ProjectileSystemTests`, `SpitterBehaviourTests` and `ProjectileViewsTests`. Rule 4 and the
   *Public API* both write `ProjectileImpacted.Hit`, the shipped field was `HitPlayer`, and after this task
   that name is a lie: an `AtEnemies` bolt setting `HitPlayer` describes an arrival that never went near the
   player. **Behaviour outranks Files** by the spec's own precedence line, and this is the same defect
   `PlayerStat` has and is a known issue for — caught while it was eight call sites. No view reads it;
   `TelegraphRings` says in a comment that it deliberately does not.
2. **`CombatBlackboard.IncomingProjectiles` now counts `AtPlayer` shots only** — one method in
   `ProjectileSystem`, and **the spec does not state it**. That field is CC §6.4's Bulwark trigger, "an
   enemy projectile is inbound"; counted raw, a Gravecaller firing four bolts a second would hold the
   predicate true for the whole run and auto-cast Bulwark off the player's own fire. The field's name always
   meant one side — until a side existed there was no way for it to be wrong. Pinned by
   `Shot_OnlyInboundShotsCountForBulwark`, which is beyond the Tests table and says so.
3. **`WeaponSpec` refuses an undefined `WeaponKind`**, reversing that enum's own remark that it is
   *"deliberately unvalidated by `WeaponSpec`"*. That remark was written when there was one member and no
   kind-conditional validation; with two, an undefined kind satisfies neither set of rule 2's rules and would
   be constructed with three unchecked numbers on it. Refused at the door, remark rewritten in place. The
   implied guard row asked for exactly this.
4. **Two ripple sites the Files table does not name** — `StageFlowTests.Step` and
   `SpitterBehaviourTests.Land`, one line each, because `Tick` gained a non-defaulted `EnemySystem`. The
   table named only `ProjectileSystemTests`; the guard row requiring a null `EnemySystem` to throw is what
   makes the parameter non-defaulted, and therefore what makes those two lines unavoidable.

**`Architecture.md` §18.1 was edited too**, which the Files table does not cover: rule 7 adds a step to the
enumerated `RunSession.Tick` order, and that table is the one place the order is written down. The chain gained
*take the shot* and one row explains why it sits where it does. A spec cannot add an ordering the code depends
on and leave §18 describing the old one.

**Two guards added that no rule asked for, both on the frame path.** `PlayerCombat.ShotDamage` clamps a live
`Weapon.Damage` to zero when it is negative or non-finite, because `Projectile`'s constructor *throws* where a
`ConeHitIntent`'s door does not — `Stat` clamps nothing (ADR-0008), so a −200 % Pact would end the run from
inside a damage frame. `ConeAngle` and `ConeRange`'s job, reached from the other side. And `Projectile` refuses
an undefined `ShotSide` with two comparisons rather than `Enum.IsDefined`, which boxes.

**`ShotSide.AtPlayer` is first in the enum as well as the parameter default**, so an unwritten side reads as
the thing every shot in the game has always been. `Weapon.cs` was not opened. Rule 1's claim that the cadence
is identical is asserted rather than assumed: `Weapon_AProjectileWeaponKeepsTheConesCadence` compares the
damage-frame *moments* of both kinds over ten seconds, not merely their count.

**The Oathbound asset was converted in the Editor after the change** and comes out unmoved — `Cone`, 13, 3.0,
8 m, 60°, 0.4, with the three new fields at zero. Nothing renders differently and nothing is playable: M5-02
authors the first weapon that walks through any of this.
