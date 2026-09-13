# M2-08 — `BloaterBehaviour`: a fuse you have to walk away from, and a blast that is a property of the corpse

**Size:** S · **Depends on:** M2-07b · **Branch:** `m2-08-bloater-ai`
**Design refs:** GD §8.1 (the Bloater), §9.1 rule 1, §12.3, §12.4 (the one-shot rule); AR §3, §9, §18.1, §18.4 · **Ledger rows:** 7 (applied; settled in M2-07a)

## Goal

The third archetype: something that waddles at you, commits to going off, and hurts you whether you kill it or ignore it — with the explosion owned by the *content* rather than by the behaviour, so "explodes on death" means it, however it died.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/BloaterBehaviour.cs` | Core | `BloaterState` + the behaviour |
| `Tests/Core/Ai/BloaterBehaviourTests.cs` | Tests.Core | Every rule below, the blast included |
| *small edits* | | `EnemySystem` + `Explode` and the death branch, and `ApplyDamage` gains a `PlayerCombat` (rule 3); `EnemyEvents.cs` + `EnemyExploded`; `EnemyTickContext` + `Enemies` (M2-07b rule 2's promised one-line widening); `EnemySystem.Tick`'s dispatch + `case Bloater`; `Data/Enemies/Bloater.asset` + `_behaviour: Bloater` (M2-06 rule 11); `PlayerCombat`'s two `ApplyDamage` call sites pass `this` |
| *ripple* | | every `enemies.ApplyDamage(...)` in Tests.Core — `ConeHitsToDamageTests`, `ChargeIntegrationTests`, `EnemySystemTests` — a required parameter, so the compiler enumerates them |

Only these files change. Anything else is a deviation: say so in *As built*.

**If `EnemySystem` turns out to need restructuring rather than one method and one branch — if the widened `ApplyDamage` cannot carry a `PlayerCombat` without unpicking how `PlayerCombat` calls it — split into `M2-08a` (the blast on `EnemySystem`) and `M2-08b` (the behaviour) before continuing, never after.**

## Public API

```csharp
namespace Soulvail.Core.Events;

/// Something went off. Published by `EnemySystem` immediately after the `EnemyDied` that caused it,
/// once per life, and only for an archetype whose spec carries an `ExplosionSpec`.
public readonly struct EnemyExploded
{
    public readonly int Id;
    public readonly ContentId SpecId;
    public readonly Vector3 Position;      // where it died — the blast's centre
    public readonly float Radius;
    public readonly bool HitPlayer;
}
```

```csharp
namespace Soulvail.Core.Ai;

public enum BloaterState
{
    Idle,      // spawned and unaware
    Waddle,    // walking at the player
    Fuse,      // planted, swelling, committed. It does not end any way but the blast (rule 7).
}

public sealed class BloaterBehaviour : IEnemyBehaviour
{
    public BloaterBehaviour(EnemyAgent agent);

    public BloaterState State { get; }
}

// EnemyTickContext (added)
public EnemySystem Enemies { get; }

// EnemySystem (changed / added)
public DamageResult ApplyDamage(int enemyId, float amount, float now, PlayerCombat player);

/// Resolves `agent`'s explosion: everything inside `ExplosionSpec.Radius` of where it died takes
/// `agent.ContactDamage.Value`, and `EnemyExploded` is published. Today "everything" is the
/// player and nothing else — rule 4.
private void Explode(EnemyAgent agent, float now, PlayerCombat player);
```

## Behaviour

**The blast belongs to the corpse, not to the behaviour**

1. **`EnemySystem.ApplyDamage` explodes anything whose spec carries an `ExplosionSpec`**, immediately after it publishes `EnemyDied`, exactly once per life — `Killed` is true only on the call that took HP to zero, which is the same property that makes the death event fire once. The behaviour is not consulted and has no death hook.
2. **Because the trigger is the spec rather than the kind**, "explodes on death" is true however it died: shot at range, cut down mid-fuse, killed by a Charge, or killed by its own fuse (rule 8). It is also true for anything else that ever gets an `ExplosionSpec` — M7-02's Volatile affix is a spec block, not a new mechanism — and it needs no `switch` on a kind, which is what this project bans in the first place.
3. **`ApplyDamage` gains a `PlayerCombat`, and that is the honest signature.** As of this task, hurting an enemy can hurt the player, so the one door damage reaches an enemy through has to know who the player is. Both production call sites are inside `PlayerCombat` and pass `this`; the Bloater's own self-kill passes `ctx.Player`.
4. **The blast damages the player and nothing else.** **Ruled by the owner at M2-00c**, against damaging other enemies. The rejected alternative is genuinely the better *moment* — a Bloater killed in a crowd chaining through it is the best thing the archetype could produce — but it makes a blast re-enter `ApplyDamage` while `ApplyDamage` is still running, which can kill another Bloater, which explodes, and every one of those touches a registry that `EnemySystem.Tick` may be walking. That wants a work queue and a recursion guard, and it is a bigger change than the archetype. Player-only keeps the whole blast a single leaf call: one XZ distance test, one `PlayerCombat.ApplyDamage`, no registry mutation, no re-entrancy. **M7-02 is where a chain can be afforded**, funded by the same `UnspentThreat` that buys Elites.
5. The blast is **XZ from where it died** (AR §18.4) against `_playerPosition` as of this frame's `Ingest`, and the damage is `agent.ContactDamage.Value` — the stat, so `d(n)` is already in it (M2-03 rule 7, M2-06 rule 5). `EnemyExploded` is published whether or not it caught anybody, because a view has to draw the flash either way — `ProjectileImpacted`'s reasoning, and `EnemyDespawned`'s.
6. **A blast that kills the player during a fact phase ends the run one tick later, and that is new.** `ReportConeHits` and `ReportChargeHits` run *after* `RunSession.Tick`, so a Bloater killed by a swing publishes `PlayerDied` immediately — which the HUD's death overlay hangs off — while `RunEnded` waits for the next tick's death check. Nothing before this task could damage the player from a fact phase. The alternative, checking for death inside the report, would put the run's lifecycle in two places; the cost is one frame of a corpse standing up, which nothing draws. Named here rather than found later.

**The Bloater**

7. `Idle` until the player is within `Spec.AggroRange`; then `Waddle`, walking at `agent.MoveSpeed.Value` and preferring `PathDirectionToPlayer` over the straight line, exactly as the Chaser does. At `DistanceToPlayer <= Spec.Reach` — 2 m, the contact trigger, not the blast radius — it plants and enters `Fuse`.
8. **`Fuse` is committed: it does not move, does not cancel, and ends only by going off.** Entering it publishes `EnemyTelegraph(id, Spec.WindupTime)`, which `EnemyHitFeedback`'s swell already draws, and after `WindupTime` seconds the Bloater **kills itself** — `ctx.Enemies.ApplyDamage(agent.Id, agent.Health.Current, ctx.Now, ctx.Player)` — which is what makes rule 1 the only explosion path in the game. "Explodes on death or contact" is implemented as *contact kills it, and death explodes it*, so there is one place the blast can come from and no flag to keep in step.
9. **The fuse is escapable and the arithmetic says by how much.** It starts at 2 m, the blast reaches 3 m, and 0.8 s at the Oathbound's 5.4 m/s covers **4.3 m** — so a player who reacts to the swell walks out with a metre to spare, and one who does not eats 15 (32 % of max HP at the depth cap, M2-06 rule 8). That is the archetype: it punishes *not noticing*, and it punishes clustering because the metre of room you dodge into is often inside the next one's circle.
10. **Exactly one `EnemyMoveIntent` per tick in every state**, zero-velocity ones included — the body folds gravity into the same `Move` (`ChaserBehaviour`'s rule, kept).
11. **A dying behaviour does not retire its agent, and that is why the tick loop needs no restructuring.** `EnemySystem.Tick`'s remarks warn that *"the day a behaviour gains the power to retire an agent (a Bloater exploding, M2-08) it has to walk backwards the way `SweepCorpses` does"* — the day has come and the warning does not apply, because `ApplyDamage` leaves the corpse **registered**: it is swept `CorpseTime` seconds later, at the top of a subsequent tick, so the span the behaviour pass is walking is never mutated underneath it. The warning is updated to say so rather than deleted, because the next behaviour that calls `Despawn` directly will still owe the backwards walk.
12. `Reset()` returns it to `Idle` with a blank `StateTimer` — what a recycled agent gets, and what stops a body that was mid-fuse coming back already committed.
13. Allocates nothing; one `StateMachine<BloaterState>` built with the behaviour, delegates built once.

## Tests

| Test | Given / When / Then |
|---|---|
| `Idle_UntilInsideAggroRange` | aggro 30, player at 31 m / `Tick` / `Idle`, one zero-velocity intent; at 29 m / `Tick` / `Waddle` |
| `Waddle_WalksAtScaledSpeed` | a stage-40 Bloater / `Tick` / the intent's speed is `MoveSpeed.Value` (rule 7) |
| `Waddle_PrefersThePathDirection` | a path direction 90° off the straight line / `Tick` / the intent follows the path |
| `Waddle_PlantsAtReach` | reach 2, player at 1.9 m / `Tick` / `Fuse` |
| `Fuse_PublishesTelegraphOnce` | entering `Fuse`, windup 0.8 / `Tick` ×5 inside the window / one `EnemyTelegraph(id, 0.8)` |
| `Fuse_DoesNotMove` | in `Fuse` / `Tick` / a zero-velocity intent with a live facing |
| `Fuse_NeverCancels` | player walks to 40 m during the fuse / `Tick` until it elapses / it still detonates (rule 8) |
| `Fuse_KillsItselfAtTheEnd` | windup 0.8 / `Tick` to +0.8 / `EnemyDamaged(killed: true)` then `EnemyDied` then `EnemyExploded`, in that order (rules 1, 8) |
| `Fuse_DetonatesOnceOnly` | detonation, then more `Tick`s / — / exactly one `EnemyExploded` |
| `Explode_HitsThePlayerInsideTheRadius` | radius 3, player 2.5 m from where it died / — / the player loses `ContactDamage.Value`, `HitPlayer` true — reached through the `PlayerCombat` `ApplyDamage` now takes (rules 3, 5) |
| `Explode_MissesOutsideTheRadius` | player 3.2 m away / — / no damage, **`EnemyExploded` still published**, `HitPlayer` false (rule 5) |
| `Fuse_IsEscapable` | trigger 2 m, fuse 0.8 s, radius 3, a player walking away at 5.4 m/s from the moment it plants / detonation / no damage — the arithmetic of rule 9, pinned so a tuning change to any of the four numbers fails here rather than on a phone |
| `Explode_IsDecidedOnXz` | player 2 m away on XZ and 5 m above / — / a hit (AR §18.4) |
| `Explode_UsesTheScaledDamage` | a stage-20 Bloater / detonate / the player loses `15 × d(20)` (rule 5) |
| `Explode_ObeysTheOneShotRule` | a Bloater at the depth cap / detonate / the damage is ≤ 49 — 35 % of 140 (GD §12.4, M2-06 rule 8) |
| `Explode_WhenKilledAtRange` | a Bloater killed by a cone hit 10 m from the player / — / `EnemyExploded` published, no damage (rule 2) |
| `Explode_WhenKilledMidFuse` | a Bloater killed during its fuse, player 1 m away / — / one `EnemyExploded`, the player is hurt (rule 2) |
| `Explode_DamagesNoOtherEnemy` | two Bloaters 1 m apart, one killed / — / exactly one `EnemyExploded`, the second still at full HP (rule 4) |
| `Explode_OnlyForArchetypesWithTheBlock` | a Husk killed with the player 1 m away / — / no `EnemyExploded`, no damage (rule 1) |
| `Explode_PublishedAfterEnemyDied` | a killing hit / — / event order is `EnemyDamaged`, `EnemyDied`, `EnemyExploded` |
| `Explode_DoesNotDespawnTheCorpse` | detonation / immediately after / the id still resolves through the registry; after `CorpseTime` / it does not (rule 11) |
| `System_TickSpanSurvivesADetonation` | three Bloaters, all fusing, all detonating in one `Tick` / — / three detonations, no exception, every agent still walked (rule 11) |
| `Session_BlastDuringAFactPhaseEndsTheRunNextTick` | a fusing Bloater killed by `ReportConeHits`, player inside the radius and low / — / `PlayerDied` inside the report, `RunEnded` on the following `Tick` (rule 6) |
| `Tick_EmitsExactlyOneIntentPerState` | each of the three states / one `Tick` each / exactly one `EnemyMoveIntent` (rule 10) |
| `Tick_AllocatesNothing` | a Bloater through waddle and fuse, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 |
| `Reset_ReturnsToIdle` | mid-`Fuse` / `Reset` / `Idle`, `StateTimer` 0 (rule 12) |
| `Agent_ReplacesAChaserBehaviourOnRecycle` | a Husk recycled as a Bloater / `Initialise` / `Behaviour is BloaterBehaviour`, in `Idle` |
| `System_DispatchesBloaters` | a Husk, a Spitter and a Bloater registered / `Tick` / all three ticked, one intent each |
| `Bloater_AssetIsWiredUp` | `Data/Enemies/Bloater.asset` / load / `Behaviour == Bloater`, explosion radius 3 |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Start a run at stage 4. A fat rust-coloured thing waddles at you, plants, swells, and goes off; walking away when it plants costs you nothing, standing there costs you about a third of your health bar. Rule 9's arithmetic, on screen.
2. **[Editor]** Kill one at range with the Censer: it dies and the blast does nothing to you. Kill one in melee: it dies and takes a third of your bar with it. That is the whole trade the archetype offers, and it is the check that rule 2 is a property of the corpse rather than of the fuse.
3. **[Editor]** Let two arrive together and dodge the first. The second's circle is usually where you dodged to — GD §8.1's *"punishes clustering"*, and the reason the radius is 3 and not 2.
4. **[device]** Whether 0.8 s of swell reads as *get out* on a phone screen, at a moment when several other things are also on it. Deferred with the rest of the device list.

## Out of scope

- **Drawing the blast.** `EnemyExploded` carries the centre and the radius and nothing subscribes yet; a ring or a decal is **M2-12**'s, with the spawn telegraph and the threat arrows, and it is the same pooling family M2-09 builds. Until then the feedback is the swell, the player's hit flash and the haptic.
- **Chain explosions and enemy friendly fire** — rule 4, revisited at M7-02 with the Volatile affix.
- **A Bloater that can be *made* to explode where you want it** (shoving it with a Charge into a crowd). A consequence of chains, so it waits for them.
- **A death VFX distinct from the shared dissolve** — M7-05's art pass.
- **`ExplosionSpec.Falloff`.** Full damage inside the radius and none outside is what GD §8.1 describes; a falloff curve is a number with no design behind it.

## As built

**As specified in every rule.** All thirteen behaviour rules landed as written, including the two the
spec singled out — the blast triggers off `EnemySpec.Explosion` and never off the kind, and it reaches
the player and nothing else. `EnemySystem` needed one method and one branch, so the M2-08a/M2-08b
split was not reached: both production `ApplyDamage` callers are instance methods on `PlayerCombat`
and pass `this` without anything being unpicked.

**Seven deviations, one of which changes a number the spec asserts.**

1. **The ripple row named the wrong files.** It predicted `ConeHitsToDamageTests`,
   `ChargeIntegrationTests` and `EnemySystemTests`. The compiler's actual list is
   `ChaserBehaviourTests` (2 sites), `ConeHitsToDamageTests` (2), `SpawnDirectorTests` (1) and
   `RespawnPolicyTests` (1). `ChargeIntegrationTests`' `ApplyDamage` calls are `PlayerCombat`'s, which
   this task did not touch; `EnemySystemTests` has no direct call. Three of the four fixtures had no
   `PlayerCombat` in scope and now build a `Bystander()` with silent ports, commented in each case
   with why the player is inert there (every one of them kills Husks, which carry no explosion block).
2. **A second ripple the spec did not predict: every `new EnemyTickContext(...)` in the tests.** The
   struct gained a sixth member, so sixteen construction sites across `ChaserBehaviourTests`,
   `EnemySystemTests` and `SpitterBehaviourTests` were widened. Mechanical, and the compiler
   enumerated them.
3. **Rule 9's escape arithmetic is written against a speed the game no longer has.** The rule says
   0.8 s at *the Oathbound's 5.4 m/s* covers 4.3 m and the player "walks out with a metre to spare".
   The owner's post-M2-03 retune left the Oathbound at **3 m/s**, where 0.8 s covers 2.4 m — and
   starting from the 2 m trigger that clears the 3 m radius by **1.4 m**, which is what "a metre to
   spare" actually describes. At the spec's 5.4 the margin is 3.3 m. **`Fuse_IsEscapable` pins both
   speeds** rather than picking one, so the archetype has to survive the retune and the doc, and the
   thin margin is the one that is asserted. **This is the Known-issues doc contradiction surfacing in
   a third place** — GD §6.1's 5.4–6.2 band, Characters.md §3, and now this rule. Flagged, not fixed:
   moving the band is the owner's call and M5-02 is the task that cannot avoid it.
4. **The fixture senses through `EnemySystem.Ingest` instead of hand-writing the blackboard**, which
   is where it departs from `SpitterBehaviourTests`. A blast is resolved against the *system's* last
   ingested player position (rule 5), not against the blackboard, so a hand-written perception would
   have left every explosion measuring its distance to the origin while the row believed it had moved
   the player — a fixture that passes while proving nothing. Argued in the file's class remarks.
5. **Row 29 (`Bloater_AssetIsWiredUp`) folded into the existing Tests.Game row** rather than becoming
   a new one, exactly as M2-07b did with the Spitter's:
   `EnemyLookTests.Assets_AuthoredStaticUntilTheirBehaviourExists` now asserts `Bloater`, and the
   radius of 3 was already pinned next door in `Bloater_MatchesDesign`. **That row now has no `Static`
   subject left** — all three shipped archetypes have a mind — so it was kept with a third assertion
   and a comment saying why: the rule is not spent, the next archetype authored ahead of its PR
   re-arms it, and a deleted row has to be remembered instead of failing. **This makes
   `Tests/Game/Authoring/EnemyLookTests.cs` a sixth touched file, past the Files table**, called out
   rather than hidden.
6. **Three implied guard rows, not two.** The spec's implied set gives a null row per public
   constructor and a non-finite row per float door. `EnemyTickContext`'s float doors were already
   covered by M2-07b, so this task added `Ctor_NullAgent_Throws`, `Context_NullEnemies_Throws` for the
   new fifth reference, and **`ApplyDamage_NullPlayer_Throws`** — the widened signature is a new public
   door and `ApplyDamage` now guards it, which the spec's Public API block did not say it would.
7. **`EnemyAgent.Initialise`'s comment was corrected, not just extended.** It read "A Static or a
   Bloater is left with no behaviour at all"; a Bloater now builds one, so the sentence names `Static`
   alone. This is the same paragraph M2-07b had to rewrite for the same reason — it is the third time
   that comment has been the thing that went stale, which is worth noticing.

**Two numbers the spec asserted, checked rather than assumed.** `Explode_ObeysTheOneShotRule` runs a
Bloater at depth 200 where d(n) has capped at 3.0×: 15 × 3 = **45**, inside GD §12.4's ceiling of 49.
`Explode_UsesTheScaledDamage` reads the agent's own `ContactDamage.Value` rather than typing
15 × d(20), so the row cannot drift away from the curve it is about.

**Rule 11 held exactly as the spec predicted** and `EnemySystem.Tick`'s warning was updated rather than
deleted: `System_TickSpanSurvivesADetonation` puts three Bloaters through one pass, all three
detonating inside it, and the span survives because `ApplyDamage` leaves each corpse registered. The
paragraph now says the warned-about day came, why it did not apply, and that **the backwards walk is
still owed by the next behaviour that calls `Despawn` directly**.

**Verified:** 745 EditMode green, 0 failed, 0 skipped, 9.9 s — M2-07b's 714 plus exactly 31, which is
28 of the spec's 29 rows plus 3 implied guards, with its 29th folded into Tests.Game per deviation 5.
PlayMode 3 green, 0 failed, 3.9 s. Zero errors, zero analyzer warnings, no `ProjectSettings/` drift,
and the seven Console warnings are the pre-existing ones the authoring fixtures provoke on purpose.
