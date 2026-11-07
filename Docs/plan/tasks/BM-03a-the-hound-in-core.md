# BM-03a — The Hound in core

**Size:** M · **Depends on:** BM-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.2; AR §18.2, §18.3; M5-04a (`MinionSystem`)

## Goal

A pet that follows, hunts marked enemies first, bites, pays XP as the player's and grows with his
level. Nothing hurts it yet (BM-04).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/PetSpec.cs` | Core | **New.** The constructor's nine numbers |
| `Core/Ai/PetAgent.cs` | Core | **New.** Id, position, `Health`, the three stats, `PetState`, quarry, next bite |
| `Core/Ai/PetSystem.cs` | Core | **New.** Spawn, ingest, choose, move, bite, clear (not a stretched `MinionSystem`) |
| `Core/Effects/PetStats.cs` | Core | **New.** `IStatBlock` over the lead pet: `MaxHp`, `MoveSpeed`, `ContactDamage` (the bite) |
| `Core/Events/PetEvents.cs` | Core | **New.** `PetSpawned`, `PetBit`, `PetRetargeted` |
| `Core/Effects/ModifyStat.cs` | Core | Append `StatTarget.Pet`; the handler aims at `PetSystem.LeadStats`, a no-op with no pet |
| `Tests/Core/Ai/PetSystemTests.cs` | Tests.Core | **New** |
| *small edits* | | `CharacterSpec.Pet`; `IIntentSink.PetMove(in EnemyMoveIntent)` + `IntentBuffer` + fakes; `WorldSnapshot.Pets`; `LevelGrowth` (pet too); `RunSession` (tick after `MinionSystem`) |

## Public API

```csharp
public enum PetState { Hunt, Heel }        // BM-04 and BM-05a append
public sealed class PetSpec { public PetSpec(ContentId id, float maxHp, float moveSpeed, float biteDamage,
  float biteInterval, float reach, float leash, float returnRange, float heelDistance); /* getters */ }
public sealed class PetSystem {
  public const int Capacity = 3; public const float RetargetInterval = 0.5f;
  public PetSystem(PetSpec spec, IDomainEvents events, IIntentSink intents);
  public ReadOnlySpan<PetAgent> Pets { get; }
  public IStatBlock LeadStats { get; }      // null with no pet
  public void SpawnBeside(Vector3 player, float now);
  public void Ingest(WorldSnapshot snapshot);
  public void Tick(float dt, float now, Vector3 player, EnemySystem enemies, PlayerCombat combat);
  public void Clear(); }
// PetSpawned(Id, Position) · PetBit(PetId, EnemyId, Amount) · PetRetargeted(PetId, EnemyId)  // 0 = none
```

## Behaviour

1. A character with a `Pet` gets one pet `HeelDistance` behind the player at run start and again
   at every `StageArrived`, at full HP. No `Pet` → the system holds nothing and every call no-ops.
2. Its position is the snapshot's, as for a Wight.
3. **Target**, rechosen every `RetargetInterval`: the marked enemy nearest the pet within `Leash` of
   the player; else the enemy nearest the player within `Leash`; else none → `Heel`, walking to
   `HeelDistance` beside the player.
4. Farther than `ReturnRange` from the player it drops its target and heels until back within `Leash`.
5. **Bite:** within `Reach` and past its next bite, `EnemySystem.ApplyDamage(target, bite, now,
   combat)`; `PetBit`; next bite `BiteInterval` later. Kills pay XP as the player's; boss adds still pay 0.
6. Not in `EnemyRegistry`: the targeter and the stage-complete check cannot see it.
7. Each level-up adds the mode's HP and damage percentages to the pet's `MaxHp` and bite, healing
   the HP gain, like the player's (LV-01a).
8. `ModifyStat` with `StatTarget.Pet` lands on `LeadStats`; with no pet it does nothing and does not throw.
9. No allocation per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spawn_StageArrived_FullHp` / `NoPetSpec_NoOps` (1) |
| `Target_MarkedBeforeNearer` / `Target_OutsideLeash_Ignored` / `NoEnemy_Heels` (3) |
| `TooFar_DropsTarget_Heels` (4) |
| `Bite_AtInterval_PaysXp` / `BossAdd_KilledByPet_PaysNothing` (5) |
| `Pet_NotInRegistry` (6) |
| `LevelUp_GrowsPetHpAndBite` (7) |
| `ModifyStat_Pet_Lands_OrNoOps` (8) |
| `Tick_DoesNotAllocate` (9) |

## Out of scope

- Model and view (BM-03b, BM-03c); being hurt (BM-04); heel regen, fall, rescue (BM-05a).

## As built

_Filled at merge._
