# JG-07a — The Lunger dashes down a lane

**Size:** M · **Depends on:** JG-04e (`LaneSystem`) · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (the Lunger), GD §8.2, GD §9.1 rule 1; AR §9, AR §18.4

The owner's go of 2026-10-26: stage 6 and on want a new enemy, and the first is GD §8.1's **Lunger**,
which *"punishes bad positioning: telegraphs 0.9 s, dashes 15 m in a line"*. In the Jungle it is a
panther (JG-07b–d). This task is its brain alone: a new `EnemyBehaviourKind.Lunger` and the behaviour
that runs it, with nothing on screen until JG-07c gives it a body.

## Goal

An archetype authored `Lunger` stalks the player, stops, warns with a red-orange strip down its line
for its wind-up, and dashes the strip's length. The strip bites whoever is standing in it as the dash
leaves.

## Why this shape

- **The strip is the hit, and it is Old Croaker's lane.** `LaneSystem` already draws a rectangle,
  arms it for a given time and bites once as it ends. The owner played that read at JG-04e and kept
  it (*"one sidestep avoids it"*). A Lunger opens one lane at the start of its wind-up, and the lane
  bites on the tick the dash leaves. The body then crosses the strip it warned of. Contact damage
  mid-dash would be a second rule for the same danger, so there isn't any.
- **It commits to where the player was.** The direction is caught on entering the wind-up, in
  `EnemyBlackboard.LungeDirection`, which was reserved for exactly this and has stood unused since
  M7-01. A Lunger never turns once it has warned.
- **The lane reaches the behaviour through the tick context.** `EnemyTickContext` exists so that a
  widening is one line (its own remarks). `Lanes` is added as an **optional trailing** parameter, so
  the 35 fixtures that build a context compile unchanged. Only `RunSession` passes it, and a Lunger
  ticked without one throws: the silence M2-06 rule 11 refuses.
- **A lane outlives its Lunger.** One killed mid-wind-up still has its strip on the floor, and that
  strip bites, as a fissure does. The warning was honest when it was drawn, and pulling a hazard out
  from under a player who already stepped clear would be the one surprise in the read.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/LungeSpec.cs` | Core | **New.** The dash's numbers |
| `Core/Ai/LungerBehaviour.cs` | Core | **New.** Stalk → Windup → Dash → Recover |
| `Tests/Core/Ai/LungerBehaviourTests.cs` | Tests.Core | **New.** Rules 1–9 |
| *small edits* | | `Core/Content/EnemySpec.cs` (`EnemyBehaviourKind.Lunger` appended; `Lunge` property; `lunge` optional parameter, required for a Lunger); `Core/Ai/IEnemyBehaviour.cs` (`EnemyTickContext.Lanes`); `Core/Ai/EnemyAgent.cs` (dispatch); `Core/Ai/EnemySystem.cs` (the `Tick` switch); `Core/Run/RunSession.cs` (passes `_lanes`); `Core/Combat/Lane.cs` (`Open` gains `bool tongue = false`); `Core/Events/BossEvents.cs` (`LaneArmed.Tongue`); `Core/Ai/CroakerBehaviour.cs` (opens its lash with `tongue: true`) |
| *ripple* | | `Tests/Core/Combat/LaneSystemTests.cs`, `Tests/Core/Ai/CroakerBehaviourTests.cs` (rule 12's rows) |

## Public API

```csharp
public enum EnemyBehaviourKind { Static, Chaser, Spitter, Bloater, Boss, /* … as today */ Lunger } // appended

public sealed class LungeSpec
{
    public LungeSpec(float range, float length, float speed, float halfWidth, float cooldown);
    public float Range { get; }      // starts a wind-up within this many metres; ≤ Length
    public float Length { get; }     // how far the dash and its lane run, metres
    public float Speed { get; }      // metres per second while dashing
    public float HalfWidth { get; }  // the lane's half-width, metres
    public float Cooldown { get; }   // seconds from a dash's end to the next wind-up
    public float DashSeconds { get; } // Length / Speed
}

// EnemySpec: `LungeSpec lunge = null` appended to the constructor; `public LungeSpec Lunge { get; }`.

// EnemyTickContext: `LaneSystem lanes = null` appended; `public LaneSystem Lanes { get; }`.

public sealed class LungerBehaviour : IEnemyBehaviour
{
    public LungerBehaviour(EnemyAgent agent);
    public void Tick(in EnemyTickContext ctx);
    public void Reset();
}
```

Numbers authored at JG-07d, for reading the rules: wind-up 0.9 s (`WindupTime`), recover 1.0 s
(`RecoverTime`), damage 14 (`ContactDamage`), range 9 m, length 12 m, speed 24 m/s (0.5 s), half-width
0.6 m, cooldown 2.0 s. **12 m, not GD's 15**, because the Hollow is 25 m across: a 15 m dash crosses
60% of it. The number lives on the asset, so it is a retune, not a code change.

## Behaviour

1. **Stalk.** Out of `Range`, or still on cooldown, it walks the path to the player at `MoveSpeed`,
   facing its way: `ChaserBehaviour`'s chase, path direction first and straight line second.
2. **Wind-up.** In `Range` and off cooldown, it stands, catches `LungeDirection` (toward the player,
   XZ) and publishes `EnemyTelegraph(id, WindupTime)`. On the same tick it opens one lane from its own
   position along `LungeDirection`, `Length` long, `HalfWidth` wide, armed for `WindupTime`, biting
   for `ContactDamage`.
3. **It never turns once committed.** Through the wind-up and the dash, every intent faces
   `LungeDirection`, wherever the player goes.
4. **Dash.** When the wind-up ends, it moves along `LungeDirection` at `Speed` for `DashSeconds`.
   The lane's bite on that tick is `LaneSystem`'s, not the behaviour's: the behaviour applies no
   damage itself.
5. **Recover.** After the dash it stands for `RecoverTime`, facing `LungeDirection`, and
   `NextAttackAt` becomes the dash's end plus `Cooldown`. Then it stalks.
6. **One intent a tick in every state** (the seam's rule): a standing state sends zero velocity.
7. **A wind-up under GD §9.1 rule 1's 0.6 s is refused** where the spec is built: `EnemySpec` throws
   for a Lunger whose `WindupTime` is below `WardenBehaviour.MinTelegraphSeconds`. A Lunger without a
   `LungeSpec` is refused there too, as a Bloater without an `ExplosionSpec` is.
8. **A full lane table still dashes.** `LaneSystem.Open` returning 0 (four lanes already open) costs
   the warning, not the move: the wind-up and the dash run as normal, and nothing bites.
9. **No lanes, no Lunger.** `Tick` with `ctx.Lanes` null throws `InvalidOperationException`, naming
   `RunSession`.
10. **`Reset` is a fresh Lunger**: stalking, no cooldown. A lane already open stays open (see *Why*).
11. **`Tick` allocates nothing.**
12. **A lane says whether a tongue strikes down it.** `BossViews` shoots Old Croaker's tongue on
    *every* `LaneFired` today, so a panther's dash would come with a frog's tongue. `LaneArmed.Tongue`
    carries `Open`'s new `tongue` flag. The Croaker's lash sets it and a Lunger's lane does not;
    JG-07c makes the view read it.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Lunger_OutOfRange_StalksAlongThePath` | player 15 m, a path direction set / tick / velocity along the path at `MoveSpeed` (rule 1) |
| `Lunger_OnCooldown_StalksEvenInRange` | player 5 m, `NextAttackAt` in the future / tick / still stalking, no telegraph (rule 1) |
| `Lunger_InRange_TelegraphsAndOpensOneLane` | player 8 m / tick / one `EnemyTelegraph` (0.9 s), one `LaneArmed` from its position toward the player, 12 m, half-width 0.6, armed 0.9 s; velocity zero (rule 2) |
| `Lunger_WindingUp_KeepsItsDirection` | wind-up begun, player moves 90° round / tick through the wind-up / facing unchanged (rule 3) |
| `Lunger_AfterTheWindup_DashesItsLength` | wind-up over / tick through `DashSeconds` / velocity `LungeDirection × 24`, summed distance 12 m ± one tick's (rule 4) |
| `Lunger_StandingInTheLane_IsHitOnceAsTheDashLeaves` | player inside the strip / lanes and behaviour ticked through the wind-up / exactly one hit of 14, on the tick the dash begins (rules 2, 4) |
| `Lunger_SteppedOutOfTheLane_IsNotHit` | player leaves the strip mid-wind-up / ticked through / no damage (rules 2, 4) |
| `Lunger_AfterTheDash_RecoversThenStalks` | dash over / tick / stands 1.0 s, then stalks; `NextAttackAt` = dash end + 2.0 (rule 5) |
| `Lunger_EveryState_SendsOneIntent` | each of the four states / one tick / exactly one `EnemyMoveIntent` (rule 6) |
| `Spec_Lunger_RefusesAShortWindupOrNoLunge` | a Lunger at 0.5 s, and one with no `LungeSpec` / constructed / throws (rule 7) |
| `Lunger_FullLaneTable_StillDashes` | four lanes open / wind-up begins / no `LaneArmed`, the dash still runs (rule 8) |
| `Lunger_WithoutLanes_Throws` | a context with no lanes / tick / `InvalidOperationException` (rule 9) |
| `Lunger_Reset_IsAFreshLunger` | mid-recover / `Reset` / stalks, `NextAttackAt` 0 (rule 10) |
| `Lunger_Tick_AllocatesNothing` | every state / ticked / `AllocationAssert.None` (rule 11) |
| `Lunger_Lane_CarriesNoTongue` | a Lunger's wind-up / `LaneArmed` read / `Tongue` false (rule 12) |
| `Lane_Open_PublishesItsTongueFlag` *(LaneSystemTests)* | `Open` with and without `tongue` / `LaneArmed` / `Tongue` true, false (rule 12) |
| `Croaker_Lash_LaneCarriesATongue` *(CroakerBehaviourTests)* | a lash / `LaneArmed` / `Tongue` true (rule 12) |

## Manual verification (Editor / device)

None: nothing authors a Lunger until JG-07d.

## Out of scope

- The panther's model, body and content (JG-07b–d). A Lunger in Descent: it isn't rostered there.
- Hitting other enemies with the dash, or dashing through walls: the body stops where collision
  stops it, as a knockback does.
- Elites and affixes on a Lunger: already generic.

## As built

_Filled at merge._
