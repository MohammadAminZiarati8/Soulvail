# JG-21b — Shadow panthers hunt in pairs

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 (owner, 2026-11-02); AR §18.2, §18.4

## Goal

A Lunger with a `PackSpec` pairs with one of its kind: the leader stalks straight in, the flanker
circles to the player's side, and both lunge from two angles.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/PackSpec.cs` | Core | **New.** The numbers below, validated |
| `Tests/Core/Ai/ShadowPantherPackTests.cs` | Tests.Core | **New.** Rules 1–8 |
| *small edits* | | `EnemySpec` (`Pack`, optional, honoured by Lunger only); `LungerBehaviour` (pairing, `Flank` state); `EnemyBlackboard` (`PartnerId`) |

## Public API

```csharp
public sealed class PackSpec(float partnerRadius, float flankAngleDegrees, float flankTimeout);
public enum LungerState { Stalk, Windup, Dash, Recover, Flank } // appended
public PackSpec EnemySpec.Pack { get; } // null = hunts alone
public int EnemyBlackboard.PartnerId;    // -1 = none
public bool LungerBehaviour.IsFlanker { get; }
```

Authored values (JG-21f): partner 16 m, flank 90°, timeout 3 s.

## Behaviour

1. **Pairing**, every 0.5 s while unpaired: the nearest living, unpaired agent **of the same spec
   id** within `partnerRadius`. Both record each other; the lower id leads, the higher flanks.
2. **Leader**: the Lunger exactly as before.
3. **Flanker**: goal = the point at `LungeSpec.Range − 1` m from the player, rotated
   `flankAngleDegrees` from the leader's bearing to the player, on the side it is already nearer.
   It walks there (`Flank`), facing its path.
4. **Together**: when the leader enters `Windup`, a flanker within `LungeSpec.Range` enters
   `Windup` the same tick, each on its own lane.
5. **Alone after all**: a flanker at its goal (≤ 1 m) or past `flankTimeout` with the leader not
   winding up winds up on its own when in range.
6. **Partner dies**: the survivor unpairs at once, hunts as a plain Lunger and may pair again.
7. **No `PackSpec`** = the Lunger as before (Panther tests unchanged); an odd one out hunts alone.
8. **No allocation** in `Tick` at 16 agents.

Lanes, damage and cooldowns are the Lunger's; nothing here changes `LaneSystem`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pair_NearestSameSpecLowerIdLeads` | three at 5, 9, 20 m / first two pair, lower id leads (1) |
| `Pair_IgnoresOtherSpecs` | Panther 3 m / unpaired (1) |
| `Leader_IsAPlainLunger` | paired / same path and lane as a lone Lunger (2) |
| `Flanker_GoesToTheSide` | settle / 80–100° from leader's bearing, at range − 1 ± 1 m (3) |
| `Pair_WindsUpTogether` | leader winds up, flanker in range / both in `Windup` that tick (4) |
| `Flanker_TimesOut` | leader held back / flanker winds up alone by 3 s (5) |
| `PartnerDies_SurvivorHuntsAlone` | kill leader / flanker `Stalk`, `PartnerId` −1 (6) |
| `NoPackSpec_IsTheOldLunger` | Panther spec / never pairs (7) |
| `Tick_DoesNotAllocate` | 16 agents (8) |

## Out of scope

- Body and tint (JG-21f; the lane view already draws both lanes). A third member or a pack leader.

## As built

_Filled at merge._
