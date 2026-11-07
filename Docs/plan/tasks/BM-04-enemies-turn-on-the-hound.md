# BM-04 — Enemies turn on the Hound

**Size:** M · **Depends on:** BM-03a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.1 (core rule); AR §18.2, §18.4; M5-03 (`LureSystem`), `EnemyBlackboard.QuarryIsADecoy`

## Goal

Melee enemies the hound bites may turn on it, and their strikes land on it. It can fall; heel and
rescue are BM-05a.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PetSystem.cs` | Core | `ApplyDamage`, the hunter slots, `LastBitBy`; 0 HP → `PetState.Fallen` |
| `Core/Ai/EnemyBlackboard.cs` | Core | `QuarryIsAPet`, `QuarryPetId`; the player fields mean the quarry, as for a decoy |
| `Core/Ai/EnemySystem.cs` | Core | `Perceive` applies the hunt rule (rule 1) after lures |
| `Core/Ai/ChaserBehaviour.cs` | Core | Strike and leap land on the pet when its quarry is one |
| `Core/Ai/LungerBehaviour.cs` | Core | Lunge and bite the same |
| `Core/Events/PetEvents.cs` | Core | `PetHurt(PetId, Amount, EnemyId)`, `PetFell(PetId, Position)`, `PetHunted(PetId, EnemyId, bool Started)` |
| `Tests/Core/Ai/PetHuntTests.cs` | Tests.Core | **New** |
| *small edits* | | `EnemyTickContext.Pets` (optional); `EnemySystem.Ingest` takes the pets; `RunSession` |

## Public API

```csharp
public enum PetState { Hunt, Heel, Fallen }   // Fallen appended
public sealed class PetSystem {
  public const int MaxHunters = 2; public const float HuntMemory = 2f;
  public DamageResult ApplyDamage(int petId, float amount, float now, int enemyId);
  public bool BitWithin(int petId, int enemyId, float now, float seconds);
  public int HuntersOf(int petId);
  public void DropHunters(int petId); }      // BM-06a's swap calls it
public struct EnemyBlackboard { public bool QuarryIsAPet; public int QuarryPetId; }
```

## Behaviour

1. **The hunt rule**, checked in `Perceive` each tick for each living enemy: it hunts a pet when
   (a) its behaviour is `Chaser` or `Lunger`, (b) it is not a boss, (c) that pet bit it within
   `HuntMemory` s, (d) the pet is nearer to it than the player (XZ), (e) the pet is not `Fallen`,
   and (f) the pet has fewer than `MaxHunters`, or this enemy already holds a slot.
2. A hunting enemy's quarry fields are the pet's; it walks at, faces and attacks the pet with its
   normal moves. When any condition fails it drops the slot and returns to the player next tick;
   `PetHunted` on start and stop.
3. **Strikes:** a hunting Chaser's strike, a Bullfrog's leap landing and a Lunger's bite resolve
   against the pet's position and `Reach` and call `PetSystem.ApplyDamage`, never
   `PlayerCombat.ApplyDamage`. An enemy hunting the player never damages the pet.
4. Projectiles, zones, blasts and telegraphs never touch the pet.
5. `ApplyDamage` subtracts, publishes `PetHurt`; at 0 HP the pet is `Fallen` where it stands,
   publishes `PetFell`, frees its hunters, stops moving and biting, and stays down until the next
   `StageArrived` (BM-03a rule 1).
6. Bosses never hunt; boss adds follow rule 1.
7. `DropHunters` frees every slot at once and suppresses rule 1 for `HuntMemory` s for those enemies.
8. No allocation in `Perceive` or strikes.

## Tests

| Test | Given / When / Then |
|---|---|
| `Bitten_NearerThanPlayer_Chaser_Hunts` / `Lunger_Hunts` (1) |
| `Spitter_Boss_NeverHunt` / `BossAdd_Hunts` (1, 6) |
| `ThirdHunter_StaysOnPlayer` (1f) |
| `TwoSecondsNoBite_ReturnsToPlayer` / `PlayerNearer_ReturnsToPlayer` (2) |
| `HuntingFrog_StrikeHurtsPet_NotPlayer` / `BullfrogLeap_LandsOnPet` (3) |
| `PlayerHunter_And_Bolt_NeverHurtPet` (3, 4) |
| `ZeroHp_Falls_FreesHunters` / `Fallen_UpAtStageArrived` (5) |
| `DropHunters_AllReturn_NoRehuntForTwoSeconds` (7) |
| `Perceive_DoesNotAllocate` (8) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 5: a frog the hound bites turns and fights it; a third frog keeps coming
   at the player; a Spitter never turns.

## Out of scope

- Heel run, regen, rescue (BM-05a); other behaviours hunting (later, one at a time, on playtest).

## As built

_Filled at merge._
