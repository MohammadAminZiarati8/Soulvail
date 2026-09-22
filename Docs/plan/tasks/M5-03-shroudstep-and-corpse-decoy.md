# M5-03 — Shroudstep, and the first thing in this game that is not the player

**Size:** S · **Depends on:** M5-02 · **Branch:** `m5-03-shroudstep-decoy`
**Design refs:** CH §3.2; CC §5; AR §3, §9, §18.1, §18.3 · **Ledger rows:** none — rows 1 and 3 are ruled at M5-00a and placed on **M5-05b**, the views task

## Goal

The Gravecaller's `Shroudstep` stops being an enum member: a blink leaves a corpse behind it, and for
three seconds every enemy in the arena walks at the corpse instead of at the player.

## Which forcing question this answers

**Neither** — but it is the task that makes the *second* one arrive early, and that is worth saying
before M5-04a finds it. `EnemyBlackboard`'s own remarks explain why it carries no `TargetId`: *"every
enemy in V1 attacks the player and nothing else, so the field would have one legal value. It returns
with the first enemy that chooses among targets — the Choir, M7-01."* **A decoy is the first thing an
enemy walks at that is not the player, one milestone early.** Rule 2 is the cheapest honest answer to
that and rule 8 says plainly what it is not.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/LureSystem.cs` | Core | The decoys that exist right now, their expiry, and the one question perception asks them |
| `Tests/Core/Combat/LureSystemTests.cs` | Tests.Core | Lifetime, capacity, the nearest-lure query, and the refusals |
| `Tests/Core/Ai/LurePerceptionTests.cs` | Tests.Core | An enemy that walks at a corpse, and goes back to the player when it rots |
| *small edits* | Core | `MovementSkillSpec` gains `DecoyDuration` (defaulted 0, rule 5); `PlayerCombat.TickCharge` spawns one on a `Shroudstep` start; `EnemySystem.Perceive` and `Ingest` consult the lure (rule 2); `CombatEvents` gains `DecoySpawned` and `DecoyExpired`; `RunState` holds the system and `RunSession.Tick` ticks it; `CharacterDefinition` gains one field |
| *ripple* | Tests.Core | `EnemySystemTests` — `Perceive`'s signature gains the lure argument |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Combat/LureSystem.cs
/// <summary>
/// The decoys standing in the arena. A place and a moment, like a Projectile and for the same
/// reason: nothing about one changes after it is dropped, so there is no per-tick state here beyond
/// "has it expired".
/// </summary>
public sealed class LureSystem
{
    /// <summary>The most decoys that may stand at once. Two, and rule 4 is why.</summary>
    public const int Capacity = 2;

    public LureSystem(IDomainEvents events);

    public int Count { get; }

    /// <summary>Drops one at <paramref name="position"/>, expiring at <c>now + duration</c>.</summary>
    /// <returns>Its id, or <see cref="NoLure"/> when the drop was refused (rule 4).</returns>
    public int Drop(Vector3 position, float now, float duration);

    /// <summary>Retires every decoy whose moment has passed, oldest first.</summary>
    public void Tick(float now);

    /// <summary>
    /// The decoy an enemy at <paramref name="from"/> should walk at, or false where it should walk
    /// at the player. Nearest wins; ties go to the oldest (rule 3).
    /// </summary>
    public bool TryGetLure(Vector3 from, out Vector3 position);

    /// <summary>Forgets every decoy, silently. For the end of a stage or a run.</summary>
    public void Clear();

    public const int NoLure = 0;
}

// Core/Ai/EnemySystem.cs — widened
/// <param name="lures">
/// Where the arena's decoys are, or null when there are none. Passed in rather than held, for the
/// reason PlayerCombat is handed the world each time it is asked about it.
/// </param>
public void Ingest(WorldSnapshot snapshot, LureSystem lures = null);

// Core/Content/MovementSkillSpec.cs — widened
/// <summary>Seconds a Shroudstep's decoy stands. Zero on a movement skill that leaves none.</summary>
public float DecoyDuration { get; }
```

## Behaviour

1. **`Shroudstep` is a `ChargeSkill` with a different payload, and no second skill class is written.**
   `MovementSkillKind`'s own remarks say so: the kinds *"differ from a Charge in what happens along
   the path — a corpse decoy, a teleport — rather than in the cooldown, buffer and i-frame window
   every one of them has."* `PlayerCombat.TickCharge` already handles the whole clock; what rule 6
   adds is one branch on the start edge. `ChargeSkill.cs` is **not in the Files table** and must not
   be edited. An `IMovementSkill` interface is refused for the reason `PlayerCombat`'s own remarks
   give — it would be an abstraction with one implementation and a second payload, not a second
   implementation.
2. **A lured enemy is redirected in `Perceive`, at the one site that writes those fields, and no
   behaviour is touched.** `EnemySystem.Perceive` computes `PlayerPosition`, `DistanceToPlayer` and
   `DirectionToPlayer` from a single `playerPosition` local; while `TryGetLure` answers for an agent,
   that local is the decoy's position instead. **And `PathDirectionToPlayer` is zeroed for that
   agent**, because the path in `EnemySense` was computed by the body against the *player* and would
   otherwise steer the enemy past the corpse it is supposed to be walking at — the behaviours already
   fall back to the straight line when it is zero (`ChaserBehaviour`'s own ternary), which is the
   fallback M1-19 built and this is the first thing that uses it on purpose. **So a lured enemy walks
   in a straight line and can be stopped by a pillar.** For three seconds over six metres that is
   acceptable and it is written down rather than discovered.
3. **Nearest wins, ties go to the oldest.** With `Capacity` 2 a tie is nearly unreachable, and the
   rule exists so the answer does not depend on array order: a decoy dropped later must not steal an
   enemy that is equidistant, because that would make two identical frames disagree.
4. **A refused drop is silent and the newest wins nothing.** At `Capacity` the drop returns `NoLure`,
   publishes nothing, and the Shroudstep still happens — `ProjectileSystem.Fire`'s rule, for its
   reason: one lost decoy is better than an exception that ends a run. **Capacity is 2 rather than 1**
   because the cooldown is 2.5 s and the decoy stands for 3, so two can legitimately overlap for half
   a second; it is not 8 because nothing in the design lets a player hold more than two.
5. **`DecoyDuration` is a defaulted parameter on `MovementSkillSpec` and the Oathbound's is zero.**
   M4-01a rule 4's trade again: every existing `new MovementSkillSpec(...)` keeps meaning what it
   meant and no shipped asset is rewritten. **Validation is kind-conditional**, as M5-01 rule 2 made
   `WeaponSpec`'s: on `Shroudstep` the duration must be a finite number greater than zero; on
   `Charge` it must be exactly zero. A Charge with a decoy duration is a forgotten field.
6. **The decoy is dropped where the blink *started*, not where it ended.** CH §3.2 says the Shroudstep
   *"leaves a corpse-decoy"* — a thing left behind. Dropping it at the destination would put the
   decoy on top of the player and taunt every enemy straight at them, which inverts the mechanic. It
   is dropped on the tick the dash starts, from the player's position as of that tick, beside the
   `ChargeIntent` and the `ChargeStarted` that already leave there.
7. **Nothing about a decoy can be hit, hurt, or killed.** It has no `Health`, occupies no registry,
   is not in the snapshot, and takes no damage — an enemy that reaches one stands next to it and
   strikes at a `DistanceToPlayer` that is the decoy's, which means **it swings at the corpse and
   hurts nobody**. That is the mechanic working: three seconds of the arena's attention, bought with
   a dodge. A decoy with hit points would be a minion, and minions are M5-04a/b's.
8. **This is not a targeting system and must not become one.** There is no `TargetId`, no threat
   table, no per-enemy taunt state, and no way for a decoy to pull *some* enemies and not others. The
   blackboard's four player fields keep their names and keep meaning *"where this enemy's quarry is"*
   for exactly as long as a decoy stands. **Renaming them — `DistanceToQuarry` and its three
   siblings — is the honest end state and is deliberately not done here**: it is forty reader sites
   across four behaviours and their fixtures, it is a rename rather than a feature, and the task that
   should pay for it is **M7-01's Choir**, which needs a real `TargetId` anyway. Recorded as a
   parking-lot line by this task's merge.
9. **Cleared with the stage, saved never.** `LureSystem.Clear` runs where `ZoneSystem.Clear` and
   `ProjectileSystem.Clear` run, and publishes nothing for their reason. A decoy is not in
   `RunSnapshot` and does not bump the save format: it joins *"cooldowns, granted shields, live
   zones"* on the by-ruling-unsaved list, and three seconds of taunt is not worth a **v4**.
10. **Ticked above the enemy behaviours and below the player.** A decoy dropped this tick is standing
    by the time this tick's enemies perceive — the blink and the redirect are one frame, not two —
    and the expiry runs before perception so an enemy is never redirected at a corpse that has
    already rotted. `EnemySystem.Ingest` is where perception happens and it already runs at the top of
    `RunSession.Tick`; the expiry goes immediately above it.
11. **Nothing here allocates.** One preallocated array of two entries, a struct per decoy, a linear
    scan of at most two on a query that runs once per living enemy per tick.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Lure_DropsAndStands` | a drop at t=0 for 3 s / ticked to 2.9 / `Count` 1, and `TryGetLure` answers it |
| `Lure_RotsOnTime` | the same / ticked to 3.0 / `Count` 0, one `DecoyExpired`, and `TryGetLure` answers false |
| `Lure_NearestWins` | two decoys, an enemy nearer the second / `TryGetLure` / the second |
| `Lure_ATieGoesToTheOldest` | two decoys equidistant / `TryGetLure` / the first dropped — rule 3 |
| `Lure_RefusesAThirdSilently` | two standing / a third drop / `NoLure`, no event, `Count` still 2, and no throw — rule 4 |
| `Lure_RefusesNonFiniteInput` | a NaN position, a NaN or non-positive duration, a NaN `now` / dropped / throws, so a decoy that never expires cannot exist |
| `Lure_ClearIsSilent` | two standing / `Clear` / `Count` 0 and **no** `DecoyExpired` — rule 9 |
| `Lure_AllocatesNothing` | 10 000 drop/query/tick cycles / `AllocationAssert.None` / zero |
| `Perception_ALuredEnemyIsToldTheDecoy` | a Husk, a decoy nearer than the player / `Ingest` / `PlayerPosition` is the decoy's, `DistanceToPlayer` and `DirectionToPlayer` agree with it, and `PathDirectionToPlayer` is **zero** — rule 2 |
| `Perception_AnUnluredEnemyIsUnchanged` | no decoy standing / `Ingest` / all four fields byte-identical to the build before this task |
| `Perception_TheDecoyRotsAndTheEnemyTurnsBack` | a Husk lured / the decoy expires / the next `Ingest` points it at the player again, with the path direction restored from the sense |
| `Perception_ADecoyBehindTheEnemyStillTakesIt` | a decoy further than the player / `Ingest` / the enemy is still lured — a taunt is not a proximity check, and the row exists because "nearest *decoy*" and "nearer than the player" are easy to conflate |
| `Chaser_WalksAtTheDecoy` | a `ChaserBehaviour` over a lured agent / ticked / the `EnemyMoveIntent`'s velocity points at the decoy, at the agent's own speed |
| `Chaser_StrikesTheDecoyAndHurtsNobody` | a lured Husk standing on a decoy / ticked past its strike / the player's HP is unchanged and no `PlayerDamaged` is published — rule 7 |
| `Skill_AShroudstepDropsADecoyWhereItLeft` | a Gravecaller-shaped spec / the blink starts at (5, 0, 5) / one `DecoySpawned` at (5, 0, 5), not at the destination — rule 6 |
| `Skill_AChargeDropsNothing` | the shipped Oathbound / a dash / `Count` 0 and no event — rule 5 |
| `Skill_TheBlinkStillDodges` | a Gravecaller-shaped spec / a blink / the i-frames, the `ChargeIntent` and the `ChargeStarted` are exactly the Charge's, with distance 6 and duration 0.05 |
| `Spec_DecoyDurationIsKindConditional` | `Charge` with a duration / `Shroudstep` with zero / constructed / throws in both directions, naming the field |
| `Spec_TheShippedOathboundIsUnchanged` | the eight-argument constructor as every fixture calls it / constructed / `DecoyDuration` zero and nothing else moved |
| `Run_TheExpiryIsAbovePerception` | a decoy expiring on the same tick an enemy would be lured / one `RunSession.Tick` / the enemy is **not** lured — rule 10 |

**Guard rows are implied, not listed:** a null `IDomainEvents` to the constructor, a null `LureSystem`
to `Ingest` meaning "no decoys", and a non-finite `now` to `Tick`.

## Manual verification (Editor / device)

_None this task, deliberately._ The Gravecaller is not selectable (M5-02 rule 9), so no run this
build plays can drop a decoy, and **a decoy has no view until M5-05b** — a corpse nobody can see is
the thing to be honest about rather than to half-render here. The first eye on it is M5-05b's, and the
device question it raises — *does a taunted swarm read as taunted, at phone scale, in three
seconds?* — goes on [ledger row 3](../ROADMAP.md#carry-forward-into-m5) at this task's merge.

## Out of scope

- **A view for the decoy.** M5-05b, beside M5-05a's Wights. Named here so it is not a surprise there.
- **`TargetId`, threat tables, per-enemy taunt.** Rule 8. M7-01's Choir pays for the rename.
- **A decoy that can be attacked, damaged or destroyed.** Rule 7.
- **Pathfinding to a decoy.** Rule 2 zeroes the path direction and accepts the straight line; a
  NavMesh path to a moving destination is `NavPathSense`'s and it only ever targets the player.
- **Saving a decoy.** Rule 9.
- **Enemies retargeting onto Wights.** M5-04a rule 9 refuses it for the whole milestone.
- **The Emberwright's Blink.** M6-07, and it leaves a fire pool, which is a `ZoneSystem` question.

## As built

**Branch:** `m5-03-shroudstep`, not the spec's `m5-03-shroudstep-decoy` — the owner named it.

**Six deviations. Two change something.**

**1. Rule 7 was false against the code, and the Tests table settled it.** *"An enemy that reaches one
… swings at the corpse and hurts nobody"* is true of every way an enemy hurts the player **except
one**: a Spitter's bolt is aimed at `PlayerPosition` and landed by `ProjectileSystem` against the
real `RunState.PlayerPosition`, and a Bloater's blast is resolved by `EnemySystem.Explode` against
the same — both miss by construction, which is the nicest thing found here. But
`ChaserBehaviour.EnterStrike` calls `PlayerCombat.ApplyDamage` when `DistanceToPlayer` is inside its
reach, and while that distance is a *decoy's*, a Husk standing on a corpse hits a player six metres
away. So `Chaser_StrikesTheDecoyAndHurtsNobody` fails on rule 2's letter (*"no behaviour is
touched"*). **`EnemyBlackboard` gained `QuarryIsADecoy` and `EnterStrike` gained one clause** — two
files outside the table. It is written as perception, not as a threat table: one writer, in
`Perceive`, recomputed every tick from the question `TryGetLure` answers the same way about every
agent, so rule 8's *"no `TargetId`, no threat table, no way to pull some enemies and not others"*
holds. The alternative — testing the real player distance in `EnterStrike` — reddens most of
`ChaserBehaviourTests`, whose fixtures set `DistanceToPlayer` by hand and never a position.
[Parking lot](../ROADMAP.md#parking-lot) records it, and names the reader the M7-01 rename deletes.

**2. Rule 9's *"cleared with the stage"* needed `StageFlow`, which the table does not list.**
`ZoneSystem.Clear` and `ProjectileSystem.Clear` do **not** run in the same places — a zone survives
a boundary on purpose and a shot does not — so "runs where both run" is two answers. The heading
decides it: a decoy stands 3 s against a boundary's 2 s of gate and arrival, so unlike a shockwave
it can genuinely cross one, and a taunt aimed at the torn-down arena is `ProjectileSystem.Clear`'s
own defect. `StageFlow` gained a **defaulted** `LureSystem` argument (null = nothing to clear), so
its nine existing constructions are untouched. **No row covers it**: this build's fixtures all run
empty-roster modes, where `RunSession` builds no `StageFlow` at all.

**3–6, which change nothing.** *(3)* **`PlayerCombat.Tick` gained a defaulted `lures` argument**
rather than the constructor or a field — the spec's own shape for `Ingest`, and it leaves all 37
existing call sites unedited. *(4)* **The *ripple* row was not needed:** `Ingest`'s new argument is
defaulted, so `EnemySystemTests` never changed; the lure-aware rows live in the new fixture instead.
*(5)* **Rows landed in two files, not three.** `Skill_`, `Spec_` and `Lure_` are all in
`LureSystemTests` (Combat), `Perception_`, `Chaser_` and `Run_` in `LurePerceptionTests` (Ai) — the
table names two test files and every row exists. *(6)* **Four files outside the table were edited
and each was required:** `Gravecaller.asset` (`_movementSkillDecoyDuration: 3`, without which rule
5's kind-conditional validation throws on the shipped asset), `GravecallerTests` (one assertion, and
a comment that had gone stale), `TimeToKillTests` (a `Shroudstep` fixture the same validation
refused — the only ripple in the suite), and `Architecture.md` §18.1 (M5-01's lesson: a task that
adds an ordering the code depends on and leaves §18 describing the old one has filed nothing).

**Rule 10's first half is not what the frame does, and is not claimed.** *"A decoy dropped this tick
is standing by the time this tick's enemies perceive"* is unreachable — the ingest runs above the
combat step that drops it, and moving either breaks a rule §18.1 already carries. A decoy dropped on
tick *n* is perceived on *n+1*, the one-frame grace every fact in the loop has. The half that **is**
load-bearing shipped exactly: `Lures.Tick` sits immediately above `Enemies.Ingest`, which is what
`Run_TheExpiryIsAbovePerception` pins.

**What was not touched, as promised:** `ChargeSkill.cs`, any `IMovementSkill`, `RunSnapshot`'s
version, `TargetId`, and any path by which a decoy could be hit.
