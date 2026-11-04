# JG-26d — Every boss's adds die with it

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 4; boss summons pay no XP (`EnemySystem.SpawnSummon`)

## Goal

When any boss dies, everything it summoned dies on the same tick, so no stage is left with a mop-up
of adds after the boss falls. This covers the Warden's Husks, the Croaker's and Mother's Frogs, the
Warlord's guards, Thornheart's pods and rootlings, Gravemaw's crack Frogs, and the Apex's panthers.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/EnemySystem.cs` | Core | The boss's death kills every living summon (rules 2–4) |
| `Tests/Core/Ai/BossAddsDieTests.cs` | Tests.Core | **New** |
| *small edits* | | `EnemyAgent` gains `IsSummon`; `Initialise` clears it; `SpawnSummon` sets it |

## Public API

```csharp
// EnemyAgent
public bool IsSummon { get; internal set; }   // true only for a body SpawnSummon made, this life
```

## Behaviour

1. **Marked at birth.** `SpawnSummon` sets `IsSummon`; `EnemyAgent.Initialise` clears it, so a
   pooled body reused by a wave is not a summon.
2. **The boss's death takes them.** In `ResolveDamage`'s killed branch, when the dead body is
   authored `EnemyBehaviourKind.Boss`, every living agent with `IsSummon` dies in the same call,
   after the boss's own `EnemyDied`, in registry order. Kills go through `Execute`, which already
   refuses bosses.
3. **An ordinary death otherwise.** Each one publishes `EnemyDied`, counts in `DrainKills`, banks an
   `EnemyDeath` and pays no XP (`PaysXp` is already false). Views play their usual death.
4. **No harm from it.** A summon that dies this way does not explode (`Spec.Explosion` is skipped),
   so the boss's death never hurts the player.
5. **Only summons.** Wave enemies and Elites live. A summon killed on its own kills nothing else.
6. **The stage clears on the kill.** On a boss stage, killing the boss with adds standing leaves
   zero living enemies after that tick, so `StageCleared` arrives on the same frame it would with no
   adds.

## Tests

| Test | Given / When / Then |
|---|---|
| `SpawnSummon_MarksIsSummon` / `Spawn_IsNotSummon` / `PooledSummon_ReusedByWave_IsNotSummon` (1) |
| `BossDeath_KillsEveryLivingSummon_AfterTheBoss` (2) |
| `BossDeath_SummonsPublishDied_CountAsKills_PayNoXp` (3) |
| `BossDeath_ExplodingSummon_DoesNotExplode_PlayerUnhurt` (4) |
| `BossDeath_WaveEnemiesAndElitesLive` / `SummonDeath_KillsNoOtherSummon` (5) |
| `BossStage_KillBossWithAddsStanding_StageClearsSameTick` — session (6) |
| `EveryBossInTheFactory_AddsDieWithIt` — session, one `SpawnSummon` per registered boss id (2) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 15: let the Warlord call his guards, kill him. The guards fall with him and
   the door opens.
2. **[Editor]** Stage 20: kill Thornheart with pods standing. Every pod and rootling dies at once.

## Out of scope

- Boss hazards that are not enemies: Thornheart's walls, lanes, rings, projectiles in flight.
- Mother's pod Frogs surviving phase beats (Known issue 6).
