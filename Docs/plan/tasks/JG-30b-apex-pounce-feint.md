# JG-30b — The Apex's pounce and feint

**Size:** M · **Depends on:** JG-30a · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2; Bullfrog leap (`ChaserBehaviour`, `EnemyLeapAimed`); Ranger roll (CC §5)

## Goal

A long pounce onto a fixed ring that the roll answers, with a landing window to punish, and a feint
that has no ring and no lit eyes, so an early roll is wasted.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ApexBehaviour.cs` | Core | Pounce and feint moves |
| `Core/Ai/ApexStats.cs` | Core | Gains the stats below |
| `Core/Events/ApexEvents.cs` | Core | **New.** `ApexCrouched`, `ApexLanded` |
| `Tests/Core/Ai/ApexPounceTests.cs` | Tests.Core | **New** |

## Public API

```csharp
public readonly struct ApexCrouched { public ApexCrouched(int id, bool isFeint, float seconds); /* props */ }
public readonly struct ApexLanded   { public ApexLanded(int id, Vector3 at, float windowSeconds); /* props */ }
// ApexBehaviour gains: public bool InWindow { get; }
// ApexStats gains: PounceMinRange 5, PounceMaxRange 11, PounceCrouchSeconds 0.8, PounceAirSeconds 0.6,
//   PounceRadius 2.2, PounceDamageScale 2, PounceWindowSeconds 1.5, PounceCooldownSeconds 7,
//   FeintChance 0.3, FeintCrouchSeconds 0.8
```

## Behaviour

1. **Pick.** Between `PounceMinRange` and `PounceMaxRange` with the pounce off cooldown, the pounce
   comes before the dash in 30a's pick order. That roll draws `FeintChance` on the boss's stream.
   The fight's first pounce is never a feint, and never two feints in a row.
2. **Real crouch.** She stops facing the player. The landing is fixed at the player's position.
   Publish `ApexCrouched(id, false, 0.8)`, `EnemyLeapAimed(id, from, target, PounceRadius, 0.8,
   0.6)` (its ring is drawn as the Bullfrog's) and `EnemyTelegraph(id, 0.8, 3)`.
3. **Flight and bite.** She flies straight to the landing in `PounceAirSeconds`, biting nothing on
   the way. On landing, a player within `PounceRadius` takes `ContactDamage × PounceDamageScale`
   through the player's damage door, so a roll's i-frames or being outside the ring takes nothing.
4. **Window.** On landing publish `ApexLanded(id, at, 1.5)`. For `PounceWindowSeconds` she stands
   still and picks nothing (`InWindow`). The pounce cooldown counts from the landing.
5. **Feint.** Publish `ApexCrouched(id, true, 0.8)` and `EnemyTelegraph(id, 0.8, 4)`, with no
   `EnemyLeapAimed`, so there is no ring. At its end she does not leap: she arms a dash at once (30a
   rule 3, ignoring the dash cooldown). The pounce cooldown restarts.
6. **Phase entry** drops a crouch before it leaps; a pounce already in the air lands.
7. **Every telegraph ≥ 0.6 s.** No allocation per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pick_InPounceBand_PouncesBeforeDash` / `Feint_NeverFirst_NeverTwiceRunning` / `Feint_DrawnFromBossStream` (1) |
| `Crouch_PublishesCrouchedAimedTelegraph_LandingFixed` (2) |
| `Land_PlayerInRing_Bitten` / `Land_PlayerOut_Unhurt` / `Land_RollIFrames_Unhurt` — session (3) |
| `Land_PublishesLanded_StandsWindow_PicksNothing` / `Cooldown_CountsFromLanding` (4) |
| `Feint_NoAimedEvent_ThenArmsDashAtOnce` (5) |
| `PhaseEntry_DropsCrouch_AirborneStillLands` (6) |
| `Telegraphs_AtLeastPointSix` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- Double pounce and phase tuning (30c); eye glow and tail flick (30e).
