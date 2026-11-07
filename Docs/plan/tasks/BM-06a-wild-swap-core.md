# BM-06a — Wild Swap in core

**Size:** M · **Depends on:** BM-05a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.3; M6-07b (Blink on `ChargeSkill`); `MovementSkillKind`

## Goal

Space trades places with the hound: the Blink's clock, buffer, i-frames and button, aimed at the
pet instead of the stick.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/MovementSkillSpec.cs` | Core | Append `MovementSkillKind.Swap` after `None` (ordinals are stored) |
| `Core/Combat/ChargeSkill.cs` | Core | An optional `ChargeAim` that overrides stick direction and distance on the start edge |
| `Core/Combat/PlayerCombat.cs` | Core | The Swap payload: aim at the lead pet, refuse with no pet, tell `PetSystem` |
| `Core/Ai/PetSystem.cs` | Core | `SwapTo`: the leap, its i-frames, `DropHunters`, the fallen case |
| `Core/Events/PetEvents.cs` | Core | `Swapped(PetId, PlayerFrom, PlayerTo, Seconds)` |
| `Tests/Core/Combat/WildSwapTests.cs` | Tests.Core | **New** |
| *small edits* | | `IIntentSink.PetLeap(in PetLeapIntent)` + `IntentBuffer` + fakes; `MovementSkillSpec` validation names `Swap` (no pool, no decoy) |

## Public API

```csharp
public enum MovementSkillKind { Charge, Shroudstep, Blink, None, Swap }
public readonly struct ChargeAim { public ChargeAim(Vector2 directionXZ, float distance); bool HasAim; }
public sealed class ChargeSkill { public bool Tick(float dt, float now, Vector2 stickXZ, Vector2 facingXZ,
  bool allowStart, in ChargeAim aim); }   // the old overload passes default
public sealed class PetSystem { public bool SwapTo(Vector3 playerFrom, float now, float seconds); }
public readonly struct PetLeapIntent { int PetId; Vector3 To; float Seconds; }
```

## Behaviour

1. On `Swap`, a press with no pet (none spawned) is ignored and starts no cooldown.
2. **Start edge:** the aim is the XZ direction and distance from the player to the lead pet; the
   player's dash runs over the spec's duration (0.25 s) to the pet's position at that moment.
3. The same tick, `SwapTo(playerFrom)`: a standing pet leaps to where the player was over the same
   seconds (`PetLeap` intent), immune for the leap and 0.2 s after, and `DropHunters`. `Swapped`.
4. **Fallen pet:** the player dashes onto the body; the pet does not move; `AddRescueHeadStart(0.5)`.
5. The player is invulnerable for the dash plus the spec's after-window (the Charge's rule).
6. Cooldown is the spec's (6 s) and `MovementSkillCooldown` modifies it, as for every kind.
7. Distance 0 (pet underfoot) still swaps, a hop in place, and pays the cooldown.
8. The dash's path is not stopped by cover. The task checks whether `PlayerMotor` stops a dash at
   cover; if it does, the Swap kind skips that check. Name what was found in *As built*.
9. No allocation on the start edge.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoPet_PressIgnored_NoCooldown` (1) |
| `Swap_PlayerEndsAtPetsPosition` (2) |
| `Swap_PetLeapsToPlayersStart_Immune_HuntersDropped` (3) |
| `FallenPet_PlayerOnBody_RescueHalfDone` (4) |
| `Swap_PlayerInvulnerable_DashPlusWindow` (5) |
| `Swap_Cooldown_ModifiedByStat` (6) |
| `PetUnderfoot_StillSwaps` (7) |
| `Swap_ThroughCover_NotStopped` (8) |
| `Blink_StillDashesAlongStick` | the old overload / unchanged |
| `StartEdge_DoesNotAllocate` (9) |

## Out of scope

- Clips, streak, asset numbers (BM-06b); Quick Swap's roar (BM-07b).

## As built

_Filled at merge._
