# M7-03b — The Choirmother, and a song a pillar breaks

**Size:** S · **Depends on:** M7-03a · **Branch:** `m7-03b-the-choirmother`
**Design refs:** GD §9.1 (rules 1–6), §9.2, §12.3, §12.4, §16.4; CC §3.6; AR §6, §18.1, §18.2, §18.4; ADR-0006, ADR-0011 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — her fight and her song join its instrument (rules 10, 11)

## Goal

GD §9.2's second boss exists in core. She never moves and is ringed by M7-03a's Weaver shields. Five
seconds after each song she telegraphs the next down a wedge of the arena. It lands on the player
inside the wedge unless a pillar stands between them. The run chooses between two boss fights for the
first time.

## The reading of GD §9.2, ruled at M7-00c

*"Sonic cone broken by line-of-sight"* is read as **one attack**: a wedge 50° wide and 22 m long,
aimed at where the quarry stood when the telegraph began, that hurts the real player if they are inside
it **and** she can see them. What *"see"* means is the sense [M2-11b](M2-11b-line-of-sight-sense.md) already built:
`EnemySense.HasLineOfSight`, a ray at eye height against the `Cover` layer. So a pillar is the safe
answer GD §9.1 rule 2 asks for, and **the arena is the participant rule 6 asks for**. Her room's hazard
is its cover, and no second hazard is invented.

*"Teaches: target priority"* is what the ring does to the gun. While a living shield stands between
her and the player, the targeter cannot choose her and picks the shield in the way (rule 7). Killing it
opens a window that turns with the ring and releases two Weaverlings. Burning her through the window,
clearing the children, and hiding from the song are three targets and one gun.

## What was already built for it

- **`RunSession`'s boss factory says this task is coming.** It reads *"Every boss gets a
  `WardenBehaviour`, and V1 has exactly one boss … the moment GD §9.2's second boss is authored
  (M7's Choirmother …) this line becomes a dispatch"*. AR §6's objection to an enum ahead of its second
  caller no longer applies, because this is the second caller.
- **`EnemySense.HasLineOfSight`** is refreshed at 10 Hz per enemy by `LineOfSightSense` and copied
  onto `EnemyBlackboard.HasLineOfSight` at every ingest, measured to the **real** player.
- **`EnemySystem.PlayerPosition`** ([M7-01a](M7-01a-the-lunger.md) rule 6) is the real player, and
  `EnemyBlackboard.PlayerPosition` is the quarry. The Warden's fissure opens under the quarry and bites
  the real player, and the song does the same.
- **`EnemyAgent.RecoverTime`** ([M7-02c](M7-02c-the-affix-roll.md) rule 6) is what every behaviour
  that recovers reads.

**What was not built, found while counting.** A boss's inner behaviour has no way to hear its beat
open. `BossBehaviour` simply stops ticking it. A Warden slam in its windup when a beat opens is frozen
for 1.5 s and released on the beat's last frame. That stays as it is: the M4 fight is not this task's,
and the [parking lot](../ROADMAP.md#parking-lot) records it. A song frozen the same way would land a
wedge the player was told had ended, right after the beat promised to reset pressure. Rule 5 is the
seam.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ChoirmotherBehaviour.cs` | Core | **New.** Rest, inhale, sing, recover |
| `Core/Ai/IBeatListener.cs` | Core | **New.** The one member a boss's inner behaviour implements to hear its beat open (rule 5) |
| `Tests/Core/Ai/ChoirmotherBehaviourTests.cs` | Tests.Core | **New.** Every rule below but the rows that open an asset |
| *small edits* | Core, Game, Data, Docs | `Core/Content/BossSpec.cs` — `BossFightKind` and `BossSpec.Fight`, optional and last (rule 8); `Core/Run/RunSession.cs` — the factory dispatches on it (rule 8); `Core/Ai/BossBehaviour.cs` — `MinTelegraphSeconds`, `EnterPhase` tells an `IBeatListener`, and `IsVulnerable` written once a tick in place of `SetBeat`'s two writes (rules 3, 5, 7); `Core/Ai/WardenBehaviour.cs` — its `MinTelegraphSeconds` becomes `BossBehaviour`'s (rule 3); `Core/Events/BossEvents.cs` — `SonicConeTelegraphed`, `SonicConeReleased`, `SonicConeWithdrawn` (rule 4); `Core/Combat/PlayerCombat.cs` — the cone and the Charge skip the knockback of a body authored immobile (rule 9); `Game/Authoring/BossDefinition.cs` — `_fight`; `Data/Enemies/Choirmother.asset` and `Data/Enemies/ChoirmotherBoss.asset` — **new**, rule 10's numbers; `Prefabs/Composition/BootScope.prefab` — `_enemies` and `_bosses` gain them; `Data/Localisation/English.asset` — `enemy.choirmother.name`; `Pseudo.asset` regenerated; `Tests/Game/Authoring/ContentValidationTests.cs` — `ShippedBosses` 2, and `Choirmother_AssetsCarryTheNumbers`; `Tests/Game/Authoring/EnemyLookTests.cs` — `Song_NeverTakesHalfTheOathbound`, beside `Assets_ObeyTheOneShotRule` (both open an asset, which Tests.Core cannot: M6-07c's reason); `Docs/GameDesign.md` — §9.2's row as built; `Docs/Architecture.md` — one §18.2 row and one §18.4 row (rules 6, 8) |
| *ripple* | — | **Every `new BossSpec(...)` site but `BossDefinition.ToSpec`, which passes `_fight`, is untouched**: 17 of 18 at M7-00c. `fight` is optional, last, and defaults to the Warden's `Slam`. `WardenBoss.asset` serialises no `_fight`, so it reads 0, which is `Slam` (rule 8) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// Which fight a boss's body carries into the arena — the one choice <c>RunSession</c>'s boss factory
/// makes. Named for what the fight does, as <c>EnemyBehaviourKind.Shielded</c> is (M7-01c).
/// <b>Appended, never inserted</b>: <c>BossDefinition</c> serialises it by ordinal.
/// </summary>
public enum BossFightKind
{
    /// <summary>A shield-slam's ring and a crack underfoot — <c>WardenBehaviour</c> (M4-02). 0, so every asset authored before M7-03b is one.</summary>
    Slam,

    /// <summary>A song down a wedge that a pillar breaks — <c>ChoirmotherBehaviour</c> (M7-03b). 1.</summary>
    Song,
}

public sealed class BossSpec
{
    // ... after beatSeconds, defaulted and last:  BossFightKind fight = BossFightKind.Slam
    public BossFightKind Fight { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

/// <summary>
/// A boss's inner behaviour that hears its beat open — so an attack in its telegraph when the arena
/// resets is withdrawn rather than frozen and released after it (M7-03b rule 5).
/// </summary>
public interface IBeatListener
{
    /// <summary>Called by <c>BossBehaviour</c> on the beat's first tick, after the clear and the summon.</summary>
    void OnBeatStarted(in EnemyTickContext ctx);
}

public enum ChoirmotherState { Rest, Inhale, Recover }

public sealed class ChoirmotherBehaviour : IEnemyBehaviour, IBeatListener
{
    /// <summary>How far the song reaches, in metres (rule 2).</summary>
    public const float SongRange = 22f;

    /// <summary>Half the wedge's width, in degrees — a 50° wedge (rule 2).</summary>
    public const float SongHalfAngleDegrees = 25f;

    /// <summary>Simulated seconds from one song's release, withdrawal or first tick to the next inhale (rule 1).</summary>
    public const float SongCooldown = 5f;

    public ChoirmotherBehaviour(EnemyAgent agent);

    public ChoirmotherState State { get; }

    /// <summary>The body's <c>WindupTime</c>, floored at <see cref="BossBehaviour.MinTelegraphSeconds"/> (rule 3).</summary>
    public float TelegraphSeconds { get; }

    /// <summary>The simulated second the next song is legal at.</summary>
    public float NextSongAt { get; }

    public void Tick(in EnemyTickContext ctx);
    public void Reset();
    public void OnBeatStarted(in EnemyTickContext ctx);
}

public sealed class BossBehaviour
{
    /// <summary>The shortest telegraph GD §9.1 rule 1 permits any boss, in seconds. 0.6.</summary>
    public const float MinTelegraphSeconds = 0.6f;
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>
/// She has drawn breath: where from, which way, how far, how wide and for how long. Published on the
/// inhale's first tick beside the ordinary <see cref="EnemyTelegraph"/> (rule 4).
/// </summary>
public readonly struct SonicConeTelegraphed
{
    public SonicConeTelegraphed(int enemyId, Vector3 origin, Vector2 directionXZ, float range, float halfAngleDegrees, float seconds);
    // six readonly fields of the same names
}

/// <summary>The song left her, whether or not it caught the player (rule 4).</summary>
public readonly struct SonicConeReleased
{
    public SonicConeReleased(int enemyId, bool caught);
    public readonly int EnemyId;
    public readonly bool Caught;
}

/// <summary>A song that was telegraphed and will not be sung — a beat opened during it (rule 5).</summary>
public readonly struct SonicConeWithdrawn
{
    public SonicConeWithdrawn(int enemyId);
    public readonly int EnemyId;
}
```

## Behaviour

1. **Three states, and she never walks.** She has no `Idle`, because she is singing from the moment she
   stands up. Her rhythm is `Rest` → `Inhale` → `Recover` → `Rest`, and **every intent's velocity is
   zero in every state**.
   - **Facing.** In `Rest` and `Recover` it is `DirectionToPlayer`, the quarry, so the body looks where
     the next song will go. In `Inhale` it is the committed direction.
   - **The first song waits one `SongCooldown` from her first tick.** She stands up on M7-03d's mark,
     which the director never fills with the player inside 6 m of it, and a song on the first frame
     would be a telegraph read under the arrival banner.
   - `Rest` enters `Inhale` at `NextSongAt`. `Recover` lasts `_agent.RecoverTime` (M7-02c rule 6).
     `SongCooldown` counts from the release.
2. **The direction is committed on entering `Inhale`, and the wedge never turns.**
   - `DirectionToPlayer` at that tick is the song's direction, and her `Position` at that tick is its
     origin. **The fissure's rule** (M4-02): a wedge that tracked would have no safe answer at all,
     which GD §9.1 rule 2 forbids.
   - **A zero `DirectionToPlayer` stays in `Rest` for that tick** rather than committing to nowhere, which
     is M7-01a rule 3's line.
   - **The song aims at the quarry and lands on the real player.** A Shroudstep corpse draws the wedge
     after it, and the Gravecaller standing outside that wedge is not sung at. That is the Warden's
     fissure again: placed at the quarry, biting the player.
3. **The telegraph is the body's `WindupTime`, floored at one number for every boss.**
   `BossBehaviour.MinTelegraphSeconds` is 0.6, GD §9.1 rule 1's floor. Rule 1 speaks of every boss, so
   the floor moves from `WardenBehaviour` to the class every boss is, and `WardenBehaviour.MinTelegraphSeconds`
   becomes `= BossBehaviour.MinTelegraphSeconds`. The value is unchanged and so is every reader.
   `TelegraphSeconds` is `max(Spec.WindupTime, MinTelegraphSeconds)`. It is **not** divided by attack
   rate (M7-02c rule 6: the tell never shortens).
4. **The tell is two events on the inhale's first tick, and the release is one on its last.**
   - **On the first tick**, `EnemyTelegraph(id, TelegraphSeconds)` goes out, so the swell already
     reaches her. `SonicConeTelegraphed(id, origin, direction, SongRange, SongHalfAngleDegrees,
     TelegraphSeconds)` goes out beside it.
   - **On the tick `StateTimer` reaches `TelegraphSeconds`**, the song resolves (rule 6).
     `SonicConeReleased(id, caught)` is published whether or not it caught anyone, which is
     `FissureFired`'s reasoning: a wedge that only flashed on a hit would teach that the dodged ones
     never fired.
   - **Nothing reads the three events in this task.** That is a stated exception to M6-06a rule 6,
     argued as M7-01a rule 4 argues it. The reader is [M7-03c](M7-03c-a-song-you-can-see.md), the next
     task.
5. **A beat withdraws a song, and restarts the cooldown.**
   - `BossBehaviour.EnterPhase` calls `OnBeatStarted(ctx)` on an inner that implements `IBeatListener`.
     That is a type test, not a virtual call on every inner, and it allocates nothing. It runs after
     the clear and the summon, before the two phase events.
   - **Mid-inhale**, she publishes `SonicConeWithdrawn(id)`, returns to `Rest`, and sets
     `NextSongAt = now + SongCooldown`.
   - **In `Rest` or `Recover`**, only the cooldown restarts. The player told the fight is changing is
     not sung at the moment it resumes.
   - **The Warden does not implement it.** Its frozen slam is M4's and the parking lot's. The interface
     has one implementer, and the reason is stated rather than hidden: it is the smallest seam that
     does not change a fight nobody asked to change.
6. **The song lands when three things are true on the release tick.** All three are measured on XZ,
   against `EnemySystem.PlayerPosition`:
   - the player is within `SongRange`, inclusive;
   - the angle from the committed direction to the player is at most `SongHalfAngleDegrees`, inclusive,
     tested as a dot product against a cosine computed once;
   - `Blackboard.HasLineOfSight` is true.

   Then `ctx.Player.ApplyDamage(ContactDamage.Value, now)` is called: one blow, depth's `d(n)` in the
   stat, and the player's i-frames apply as to any blow. A player closer than `1e-4` m is in every wedge.

   **Sight is read at the release, and its staleness is stated.** The sense re-measures each enemy at
   `LineOfSightSense.DefaultRefreshHz` (10 Hz), so the answer is at most 0.1 s old: about 0.3 m of the
   slowest class's walk. A player who steps behind a pillar in the song's last tenth may still be
   caught. **Unknown means *can see*** (AR §18.4), so a mis-wired sense sings through every pillar,
   visibly, rather than never landing. A per-frame sight line for one boss is a `Game` change to
   `LineOfSightSense` that nothing here needs until a phone says so.
7. **The escort decides whether the gun may choose her, and `BossBehaviour` is the one writer.** Once a
   tick, after the phase check, `BossBehaviour.Tick` writes
   `_agent.IsVulnerable = !inBeat && !ctx.Enemies.IsEscortedFrom(_agent, ctx.Enemies.PlayerPosition)`.
   That is M7-01c rule 5's shape, asked of the real player.
   - So `TargetScorer` scores her out while a shield stands between them and picks the next target. A
     tap-focus on her holds with `IsCurrentBlocked`, which is CC §3.6's *"go around"*. `Targeter`
     changes in nothing.
   - **One writer, M7-02c rule 7's line.** `SetBeat` stops writing the bool and keeps
     `Health.SetExternalInvulnerable`. `ChoirmotherBehaviour` never writes it. Had the inner written it
     beside `SetBeat`, she would have been the second writer that rule calls the bug M7-00a found.
   - **The ring is any boss's, so the write is too.** The Warden of Ash summons no orbiter, so its value
     is `!inBeat` on every tick, as it is today.
8. **The run chooses a fight by `BossSpec.Fight`, in the one place that holds the hazards.**
   - **The dispatch.** `RunSession`'s factory becomes `boss.Fight switch { Slam => new
     WardenBehaviour(...), Song => new ChoirmotherBehaviour(agent), _ => throw }`. The default arm is
     loud, naming the boss and the value, for `EnemySystem.Tick`'s reason.
   - **The field.** `BossDefinition` gains `_fight`, defaulted to `Slam`.
   - **The enum is a kind, not an identity.** It names what a fight does, like `EnemyBehaviourKind`,
     and a second boss that slams would author `Slam`. AR §18.2 gains the row: *a boss's inner behaviour
     is chosen by `BossSpec.Fight` in `RunSession`'s factory, and nowhere else*.
9. **An immobile body is never shoved.** `PlayerCombat.ResolveConeHits` and `ResolveChargeHits` skip the
   knockback for an agent whose authored `Spec.MoveSpeed` is 0, beside M7-01c rule 6's blocked-hit
   test. Her ring is centred on her live position (M7-03a rule 5), so a Charge that moved her four
   metres would walk her ring into a pillar and her into a wall she never walks back from. The authored
   speed, not the stat, because depth scales the stat by `s(n)` and zero times anything is still a
   body that does not walk.
10. **Her two assets.** GD §9.2 publishes the hook and the stage. The numbers are ours, and each says
    where it came from.

    `Choirmother.asset` is her body. **Its HP comes from an estimate, not a measurement.** The Warden's
    first measured fight was 3 968 HP in 89 s at stage 5, about 45 effective DPS against a boss, so ten
    stages of tree put stage 10 near 50. Her ring's windows are guessed at half the fight, and about
    three shields a phase with their children cost ~17 s. **1 400 × h(10) = 2 156 HP** is then about 103
    s, inside GD §9.1 rule 5's 75–120. [Ledger row 2](../ROADMAP.md#carry-forward-into-m7)'s instrument
    measures it.

    | Field | Value | Why |
    |---|---|---|
    | `_id` / `_nameKey` | `enemy.choirmother` / *"Choirmother"* | GD §9.2 |
    | `_maxHp` | **1 400** | the estimate above |
    | `_moveSpeed` | **0** | *"Immobile"*; rule 9 reads it |
    | `_targetPriority` | **8** | the Warden of Ash's |
    | `_threatCost` / `_xpValue` | **40** / **300** | the Warden of Ash's. A boss is additive to the budget (GD §12.1), so the cost is read by nothing |
    | `_contactDamage` | **16** | the song. Rule 11 |
    | `_reach` | **1** | read by nothing: she has no blow but the song |
    | `_windupTime` / `_recoverTime` | **1.2** / **1.0** | the telegraph is rule 12's. The recovery only holds her in `Recover`, a pose a view can hang an exhale on: the cooldown counts from the release, so any value under `SongCooldown` sings the same fight |
    | `_aggroRange` | **40** | read by nothing: she never idles |
    | `_behaviour` | **Boss** | built by `SpawnBoss` |
    | `_tint` / `_bodyScale` | **(0.55, 0.60, 0.62)** / **2.6** | a drowned grey-blue, GD §16.4's *"everything else"*, and taller than the Warden of Ash's 2.2 (`WardenOfAsh.asset` after M7-01c). [M7-03c](M7-03c-a-song-you-can-see.md) owns the look |

    `ChoirmotherBoss.asset` is her fight: `boss.choirmother`, `_enemySpecId` `enemy.choirmother`,
    `_fight` **Song**, and `_beatSeconds` **1.5**, the Warden's. It has three phases:

    | Enters below | Summons | Why |
    |---|---|---|
    | **1.0** | 6 × `enemy.weaver_shield` | the ring closed exactly: 60° arcs, 60° apart. One dead shield is a 60° window |
    | **0.66** | 7 × `enemy.weaver_shield` | 51.4° apart. One dead shield is a 43° window |
    | **0.33** | 8 × `enemy.weaver_shield` | 45° apart. One dead shield is a 30° window, and it takes two for 75° |

    The phases escalate by their rings alone, and every beat re-forms one (M7-03a rules 8, 9). **GD
    §9.1 rule 4's adds are the shields**, and the pressure is their children.
11. **The song against the one-shot rule.** GD §12.4 caps a boss's attack at 50 % of max HP.
    - **Against the Oathbound's 140** she never reaches it: `16 × d(n)` stops at 48 when `d` caps at 3.
    - **Against the Emberwright's 70** she passes 35 when `d(n) > 2.19`, which is **stage 35**. She
      stands at 10, 20, 30 and 40, so her fourth appearance is the first over it.

    That is the gap [row 2](../ROADMAP.md#carry-forward-into-m7) already holds for the Warden of Ash
    (M7-00a): the rule has only ever been tested against the Oathbound. Lowering her alone closes
    nothing, and M8-05 owns the frailest-class test.
12. **The safe answer, in numbers.** In 1.2 s the slowest class walks 3.6 m.
    - **Close in, sidestep.** The wedge's half-width at `d` metres is `0.466 d`, so sidestepping
      clears it from anywhere within **7.7 m** of her.
    - **Further out, a pillar.** Past that the answer is a pillar, which costs the ground the player was
      fighting from and none of their health: GD §9.1 rule 2's *"positioning, not health"*.
    - **The cooldown is fixed** and counts from the release, so the next song's *when* is always
      readable. Only its direction waits for the tell.
13. **Nothing on a frame path allocates.** The machine's handlers are delegates built once. The wedge
    test is a subtraction, a length and a dot product, and the escort read is M7-03a's walk.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Choirmother_NeverWalks` | a full cycle / — / every `EnemyMoveIntent`'s velocity is zero — rule 1 |
| `Choirmother_FacesTheQuarryAtRest` | the player moving round her / `Rest` ticks / facing is `DirectionToPlayer` — rule 1 |
| `Choirmother_FirstSongWaitsACooldown` | her first tick at t / — / no `Inhale` before t + 5 — rule 1 |
| `Song_TelegraphsTwiceOnItsFirstTick` | `Inhale` entered / — / one `EnemyTelegraph(id, 1.2)` and one `SonicConeTelegraphed(id, origin, direction, 22, 25, 1.2)`, and neither on any other tick — rule 4 |
| `Song_CommitsToWhereTheQuarryWas` | the player walks 4 m sideways during the inhale / the release / the wedge is the committing tick's — rule 2 |
| `Song_NoDirectionNoSong` | `DirectionToPlayer` zero at `NextSongAt` / one tick / still `Rest` — rule 2 |
| `Song_LandsOnAPlayerInsideTheWedge` | the player 12 m down the centre line, in sight / the release / one `PlayerDamaged` of 16, `SonicConeReleased(id, true)` — rule 6 |
| `Song_MissesAPlayerWhoSteppedOut` | the player 30° off the line / — / no damage, `SonicConeReleased(id, false)` — rule 6 |
| `Song_MissesAPlayerOutOfRange` | 22.1 m down the line / — / no damage — rule 6 |
| `Song_TheEdgesAreInclusive` | exactly 25° off, then exactly 22 m / — / both caught — rule 6 |
| `Song_APillarBreaksIt` | inside the wedge, `HasLineOfSight` false on the release tick / — / no damage, `SonicConeReleased(id, false)` — rule 6 |
| `Song_SightIsReadAtTheRelease` | out of sight for the inhale, in sight on the release tick / — / caught — rule 6 |
| `Song_ALuredSongAimsAtTheDecoy` | a decoy north, the player east / the release / the wedge points north; the player is not hit; moved into the decoy's wedge, they are — rule 2 |
| `Song_ScalesWithDepth` | depth 20 / a caught song / `16 × d(20)` — rule 6 |
| `Song_ASidestepClearsItCloseIn` | the player 7 m down the centre line, walking perpendicular at 3.0 m/s from the inhale's first tick / the release / not caught — rule 12 |
| `Song_NeverTakesHalfTheOathbound` | *(in `EnemyLookTests`)* `Choirmother.asset`'s contact at `d`'s cap of 3.0 / — / 48, under 50 % of 140 — rule 11 |
| `Song_RecoversThenRests` | a release at t / — / `Recover` for 1.0 s, then `Rest`, next inhale at t + 5 — rule 1 |
| `Beat_WithdrawsASongMidInhale` | a crossing mid-inhale / the beat and after it / one `SonicConeWithdrawn(id)`, no `SonicConeReleased`, the next inhale 5 s after the crossing — rule 5 |
| `Beat_RestartsTheCooldownAtRest` | `NextSongAt` 1 s away, a crossing / — / `NextSongAt` is the crossing + 5 — rule 5 |
| `Beat_TheWardenIsNotAListener` | `WardenBehaviour` / — / not `IBeatListener`; its M4 rows unchanged — rule 5 |
| `Vulnerability_FollowsTheEscortFromThePlayer` | a closed ring, then the shield toward the player killed / ticks / `IsVulnerable` false, then true — rule 7 |
| `Vulnerability_TheBeatWinsDuringIt` | a fixture boss whose next phase summons nothing, a window toward the player, a crossing / every beat tick / `IsVulnerable` false; true on the first tick after, because the beat cleared the ring and nothing re-formed it — rule 7 |
| `Vulnerability_TheWardenOfAshIsUnchanged` | the Warden of Ash's fight through a beat / every tick / `IsVulnerable == !inBeat` — rule 7 |
| `Targeting_PicksTheShieldInTheWay` | a closed ring between her and the player / a targeting tick / the nearest shield, not her — rule 7 |
| `Targeting_HoldsHerBlockedWhenFocused` | she is focused, then covered / — / held, `IsCurrentBlocked` true — rule 7, CC §3.6 |
| `Run_TheFightFollowsTheSpec` | a run with a `Slam` boss and a `Song` boss / each spawned / a `WardenBehaviour` inside one, a `ChoirmotherBehaviour` inside the other — rule 8 |
| `Kind_SlamIsZero` | — / — / `(int)BossFightKind.Slam == 0`, `Song == 1` — rule 8 |
| `Spec_TheFightIsOptionalAndLast` | `BossSpec` as the existing sites construct it / — / `Fight` is `Slam` — rule 8 |
| `Knockback_AnImmobileBodyIsNotShoved` | `SwingKnockback` and the Charge's `MovementSkillSpec.Knockback` above 0, a cone and a Charge through a window / — / no `EnemyKnockbackIntent` for her; a Husk beside her is shoved — rule 9 |
| `Telegraph_TheFloorIsEveryBosses` | a body authored 0.3 / `TelegraphSeconds` / 0.6, and `WardenBehaviour.MinTelegraphSeconds == BossBehaviour.MinTelegraphSeconds` — rule 3 |
| `Choirmother_ResetReturnsToRest` | mid-inhale / `Reset` / `Rest`, timer 0, `NextSongAt` 0 — AR §9 |
| `Choirmother_TickAllocatesNothing` | a full cycle and a beat, 10 000 ticks / `AllocationAssert.None` / zero — rule 13 |
| `Choirmother_AssetsCarryTheNumbers` | *(in `ContentValidationTests`)* both assets / converted / rule 10's tables; the boss names the body and `Fight` is `Song` |
| `ContentValidation_ShipsTwoBosses` | `ShippedBosses` 2; `Boss_EveryAttackTelegraphsLongEnough` walks both / — / green — rule 3 |

**Guard rows are implied, not listed:** the null agent; an undefined `BossFightKind` at the factory.

## Manual verification (Editor / device)

_None this task._ No mode rosters her until [M7-03d](M7-03d-the-choirmother-at-stage-ten.md), and
nothing draws her song until [M7-03c](M7-03c-a-song-you-can-see.md). A build that rostered her here
would be the stage-5 build M4-03's *As built* describes, which took hit points off the player with
nothing on the floor to say it was coming. Her fight is proved by the suite, and the first eyes on it
are M7-03c's probe.

## Out of scope

- **Drawing anything.** [M7-03c](M7-03c-a-song-you-can-see.md).
- **Putting her in a run.** [M7-03d](M7-03d-the-choirmother-at-stage-ten.md): the roster, the arena's
  mark, and the Gravecaller's deed.
- **The Warden's frozen slam.** The [parking lot](../ROADMAP.md#parking-lot); rule 5 says why it is not
  here.
- **Audio.** GD §9.1 rule 1's *"audio cue"* and §17.2's *"boss music is unique per boss"* are M7-07's.
  This task publishes the three events a cue would hang off.
- **A per-frame sight line for a boss.** Rule 6.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
