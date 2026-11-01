# ST-03a — Poison in core: the first damage over time

**Size:** M · **Depends on:** ST-01 · **Branch:** `st-03a-poison-core`
**Design refs:** GD §16.5

## Goal

An enemy can be poisoned: stacks add up to a cap, share one timer and deal damage in pulses through the normal damage door, so kills, XP and hit events behave as for any other damage.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/Poison.cs` | Core | one agent's stacks, timer and pulse clock |
| `Tests/Core/Ai/PoisonTests.cs` | Tests.Core | |
| *small edits* | | `EnemyStatuses` (+ `Poisoned`, key `status.enemy.poisoned`), `EnemyAgent` (owns a `Poison`, reset with the agent), `EnemySystem` (`Poison`, `PoisonStacks`, pulse in the timer pass), `RunSession.DebugEnemyStatus` (poisoned), `DebugOverlay` (a Poison key) |

## Public API

```csharp
public sealed class Poison
{
    public const int MaxStacks = 5;
    public const float PulseSeconds = 0.5f;
    public int Stacks { get; }
    public float Remaining { get; }
    public float DamagePerStackPerSecond { get; }
}

// EnemySystem
/// <exception cref="ArgumentOutOfRangeException">dps or seconds not positive and finite.</exception>
public bool Poison(int enemyId, float damagePerStackPerSecond, float seconds);
public int PoisonStacks(int enemyId);   // 0 for unknown or corpse
```

## Behaviour

1. The rule: `Debuff`, priority 25 (between root and slow), boss scale 1.
2. Each application adds one stack, up to `MaxStacks`; past the cap it adds nothing but may still refresh.
3. All stacks share one timer, which refreshes to the longer of what is left and the new `seconds`.
4. Damage per stack is the strongest applied during this poisoning; a weaker application still adds a stack.
5. Every `PulseSeconds` of tick time after the first stack lands, the enemy takes `stacks × dps × PulseSeconds` through `ApplyDamage(…, ctx.Player)`. When the timer ends mid-pulse, that pulse never fires.
6. A stunned or rooted enemy is still poisoned. Pulses run in the timer pass, before behaviours (MK-06a rule 6).
7. `EnemyStatusChanged` with the poisoned rule fires when the stack count or remaining time grows, carrying `Stacks`. It fires with 0 s at expiry; nothing fires when the poison ends with the enemy's death or despawn.
8. A kill by poison pays XP and publishes `EnemyDied` exactly like a hit.
9. The pass allocates nothing.
10. Debug: the Poison key applies three stacks of 4 dps for 4 s to the nearest enemy.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rule_DebuffPriority25_FullOnBoss` | the rule / read / Debuff, 25, 1 |
| `Apply_AddsStacks_UpToCap` | 6 applications / `PoisonStacks` / 5 |
| `Apply_RefreshesToLonger` | 4 s then 2 s / remaining / 4 s |
| `Apply_PastCap_StillRefreshes` | 5 stacks at 1 s left, apply 3 s / / 5 stacks, 3 s |
| `Dps_StrongestWins_WeakerStillStacks` | 4 dps then 2 dps / / 2 stacks at 4 dps |
| `Pulse_DamageIsStacksTimesDpsTimesHalfSecond` | 3 stacks of 4 dps / tick 0.5 s / one `EnemyDamaged` of 6 |
| `Pulse_EndsWithTimer` | 1 stack, 1.2 s / tick to 2 s / exactly 2 pulses |
| `Pulse_StunnedStillPoisoned` | stunned + poisoned / tick 0.5 s / damaged |
| `Pulse_Boss_FullDuration` | a boss, 4 s / / remaining 4 s |
| `Changed_StacksAndExpiry` | 2 applications then expiry / events / stacks 1, 2, then 0 s |
| `Death_ClearsSilently` | poisoned enemy killed by a hit / tick / no poisoned event, no pulse |
| `Kill_ByPoison_PaysLikeAHit` | 1 HP poisoned enemy / pulse / `EnemyDied`, XP granted |
| `Tick_Poisoned_NoAllocation` | 20 poisoned enemies / 100 ticks / `AllocationAssert.None` |

## Out of scope

- The look and icon (ST-03b); who applies poison in play (the owner picks); poison on the player.

## As built

_Commit body at merge, ≤ 1 500 bytes._
