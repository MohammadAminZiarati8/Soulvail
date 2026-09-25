# RS-03a — Holding fire on the move, and a class with no movement skill

**Size:** M · **Depends on:** RS-00 · **Branch:** `rs-03a-holding-fire-on-the-move`
**Design refs:** CC §2.4, §4.2, §4.3, §5; GD §6.1 · **Ledger rows:** none

## Goal

Core can hold a class's fire while it moves, and a skill can lift the hold. A class can also have
no movement skill at all. Both are needed for the Ranger (RS-03c) and neither changes the three
shipped classes.

## The owner's rulings of 2026-09-25

- **The Ranger shoots standing still.** While it runs it faces where it runs, its bow is down, and
  a shot being drawn is dropped. Stopped, it turns to its target and shoots. This is the Ranger's
  rule, not the game's: CC §4.2's *attacking never slows movement* still holds, because here
  movement stops the attack.
- **Shooting on the move is to be a skill.** So the hold is a `Stat` a node can move, not a flag:
  the future skill is one `ModifyStat` of +1, with no new code.
- **The Ranger's movement skill is a dodge roll, and the owner wants to be able to switch it off**
  to test the class without one. So a class may declare `MovementSkillKind.None`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/PlayerCombat.cs` | Core | **Edit.** The hold (rules 1–5), `None` (rule 7), the empty dash (rule 8) |
| `Tests/Core/Combat/HoldFireTests.cs` | Tests.Core | **New.** Rules 1–6 |
| `Tests/Core/Combat/MovementSkillNoneTests.cs` | Tests.Core | **New.** Rules 7–9 |
| *small edits* | Core | `WeaponSpec` gains `firesWhileMoving` (default `true`). `PlayerStat` appends `FireWhileMoving`, with its `Resolve` and `Has` lines. `CombatEvents` gains `HoldFireChanged`. `MovementSkillKind` appends `None`. `ChargeSkill.Request` ignores a press on `None` |
| *small edits* | Game | `CharacterDefinition` gains `_weaponFiresWhileMoving` (default `true`). `SkillButton` hides on `None` (rule 9) |

## Public API

```csharp
// WeaponSpec(..., float shotSpread = 0f, bool firesWhileMoving = true);  public bool FiresWhileMoving { get; }
public enum PlayerStat { /* …PoolDuration, */ FireWhileMoving }   // appended, never inserted
public enum MovementSkillKind { Charge, Shroudstep, Blink, None }  // appended
public readonly struct HoldFireChanged { public readonly bool IsHolding; public HoldFireChanged(bool isHolding); }

// PlayerCombat
public Stat FireWhileMoving { get; }   // base 1 when the weapon fires while moving, else 0
public bool IsHoldingFire { get; }
```

## Behaviour

1. **`FireWhileMoving` is a `Stat`**, seeded 1 when `WeaponSpec.FiresWhileMoving` is true and 0 when
   it is false. The class may fire while it moves when `Value > 0.5`. `PlayerStats` resolves the
   address for every class, and `Has` is true for every class.
2. **Still means stopped.** The player is still when the stick is centred
   (`snapshot.MoveInput == 0`), no dash is active, and `snapshot.PlayerVelocity` is at most
   0.05 m/s. `PlayerMotor` uses the same number for the point below which a direction is noise.
3. **The player holds fire when it may not fire while moving and is not still.** While holding, no
   new swing starts. A swing already in progress is dropped with `Weapon.Reset()`, and any pending
   shot with it, so no arrow leaves on the move. The next swing starts on the first still tick, not
   a cadence later.
4. **While the stick is pushed on a holding class, `FaceDirection` is null**, so the motor faces
   where the player runs. With the stick centred it points at the target again, so the turn begins
   while the body is still slowing.
5. **`HoldFireChanged` is published when `IsHoldingFire` changes**, and never for a class whose
   `FireWhileMoving` stays above 0.5. `TargetChanged` is unaffected: the reticle keeps what the
   player will shoot when it stops.
6. **Nothing that ships changes.** The Oathbound, the Gravecaller and the Emberwright fire while
   moving (the default), and their suites pass unchanged.
7. **A `None` movement skill does nothing.** A press is ignored, nothing is published, no cooldown
   runs, and `IsActive` is never true. Its distance, duration and cooldown stay validated, since the
   authoring asset always carries them, and they are never read.
8. **A dash that deals nothing touches nobody.** When the movement skill's damage `Value` and its
   knockback are both 0, `ResolveChargeHits` applies no damage and shoves nobody. So the Ranger's
   roll passes through a pack without a flash or a zero-metre shove.
9. **The HUD hides the movement-skill button on a `None` class.** Its `CanvasGroup` goes to alpha 0
   and stops blocking raycasts. The keyboard's Space still reaches `ChargeSkill.Request`, which rule 7
   ignores.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `FireWhileMoving_SeedsFromTheWeapon` | `firesWhileMoving` true and false / built / base 1 and 0 — rule 1 |
| `FireWhileMoving_ResolvesForEveryClass` | `PlayerStats` / `Resolve` and `Has` / the stat, `true` — rule 1 |
| `Hold_NoSwingStartsOnTheMove` | a projectile class that holds, a target in range, the stick pushed / ticked / no `PlayerAttacked`, no shot — rule 3 |
| `Hold_ADrawnShotIsDroppedWhenTheStickMoves` | a swing started still / the stick pushed before the damage frame / no shot, `Weapon.IsSwinging` false — rule 3 |
| `Hold_StoppedTheNextShotStartsAtOnce` | held for 2 s / stick centred, velocity 0 / `PlayerAttacked` on that tick — rule 3 |
| `Hold_SlowingToAStopIsNotStill` | stick centred, velocity 0.5 / ticked / still holding — rule 2 |
| `Hold_AModifierLiftsIt` | a holding class / `ModifyStat(FireWhileMoving, Flat, +1)` / fires on the move — rule 1 |
| `Face_RunningFacesTheWayItRuns`, `Face_StickCentredFacesTheTarget` | — rule 4 |
| `HoldFireChanged_PublishedOnChangeOnly`, `HoldFireChanged_NeverForAClassThatFiresMoving` | — rule 5 |
| `Shipped_EveryClassFiresWhileMoving` | the three class assets / — / `_weaponFiresWhileMoving` true — rule 6 |
| `None_APressDoesNothing` | a `None` class / `MovementSkill()` and 2 s of ticks / no `ChargeStarted`, no `ChargeIntent`, `IsActive` false — rule 7 |
| `None_ValidatesAsAnyOtherKind` | `MovementSkillSpec(None, …)` / built / accepted; a decoy or pool on it refused — rule 7 |
| `EmptyDash_TouchesNobody` | a `Charge` with damage 0 and knockback 0 / hits reported / no `EnemyDamaged`, no knockback intent — rule 8 |
| `EmptyDash_AnyDamageStillHits` | damage 0, knockback 1 / — / the shove lands — rule 8 |
| `SkillButton_HiddenOnANoneClass` (EditMode, Game) | a session whose class is `None` / bound / alpha 0, raycasts off — rule 9 |

## Manual verification (Editor / device)

1. **[Editor]** Runs as each shipped class. *Expected: unchanged — every class still fires on the
   move and dashes.*
2. **[Editor]** Set a copy of the Gravecaller to `firesWhileMoving` off and `None`, and play it.
   *Expected: it faces where it runs and holds fire, and shoots when it stops. The button is gone,
   and Space does nothing.* Revert the copy.

## Out of scope

- **The Ranger itself** (RS-03c), **its roll's animation** (RS-03d), and **the running-shot skill**:
  a later node, one `ModifyStat` on `FireWhileMoving`.
- **The sandbox.** `RangerSandboxLoop` keeps its own copy of the rule until RS-03d moves it onto
  `HoldFireChanged`. It is an animation bench, not a run.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
