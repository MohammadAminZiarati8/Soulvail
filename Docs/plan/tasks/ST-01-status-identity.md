# ST-01 — Enemy statuses get an identity and a side

**Size:** M · **Depends on:** — · **Branch:** `st-01-status-identity`
**Design refs:** GD §16.5, AR §18.3

## Goal

Enemy statuses are identified by `ContentId` with a buff/debuff side and a priority, so any new status can be added without touching an enum or a view's slot index.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/StatusSide.cs` | Core | `Debuff`, `Buff` |
| `Core/Combat/StatusRule.cs` | Core | id, side, priority, boss scale |
| `Core/Ai/EnemyStatuses.cs` | Core | the ids and rules of enemy statuses; replaces the `EnemyStatus` enum |
| `Core/Events/EnemyStatusChanged.cs` | Core | rewritten: carries the rule and stacks; enum deleted |
| `Tests/Core/Ai/EnemyStatusesTests.cs` | Tests.Core | |
| *small edits* | | `CrowdControl` (`Has`/`Remaining`/`Tick` by rule), `EnemySystem` (`Statuses`, `Announce`, boss scale from the rule, `Has`/`Remaining` by id), `RunSession.DebugEnemyStatus` |
| *ripple* | | `EnemyStatusView`, `EnemyHealthBar`, `BindingChainView`, `DebugOverlay` map ids instead of ordinals; every test naming `EnemyStatus` |

## Public API

```csharp
public enum StatusSide { Debuff, Buff }

public readonly struct StatusRule
{
    public StatusRule(ContentId id, StatusSide side, int priority, float bossScale);
    public ContentId Id { get; }
    public StatusSide Side { get; }
    public int Priority { get; }     // higher draws first within a side
    public float BossScale { get; }  // (0, 1]: duration multiplier on a boss
}

public sealed class EnemyStatuses
{
    public const string StunnedKey = "status.enemy.stunned"; // + RootedKey, SlowedKey, HastedKey
    public StatusRule Stunned { get; }                         // + Rooted, Slowed, Hasted
    public bool TryGet(ContentId id, out StatusRule rule);
}

public readonly struct EnemyStatusChanged
{
    public EnemyStatusChanged(int enemyId, StatusRule rule, float seconds, int stacks);
    public int EnemyId { get; }
    public StatusRule Rule { get; }
    public ContentId Status => Rule.Id;
    public float Seconds { get; }   // 0 = ended
    public int Stacks { get; }      // 1 for a status that does not stack
}

// EnemySystem: Statuses { get; }, Has(int, ContentId), Remaining(int, ContentId)
// RunSession: DebugEnemyStatus(int enemyId, ContentId status)
```

## Behaviour

1. Stun, root and slow are `Debuff`; haste is `Buff`. Ids are the four keys above.
2. Priorities: stunned 40 > rooted 30 > slowed 20; hasted 10.
3. Stun, root and slow have boss scale 0.5; haste 1. A boss's duration is `seconds × BossScale` — the old `BossDurationScale` constant goes.
4. Every `EnemyStatusChanged` carries its status's rule and `Stacks` 1.
5. `TryGet` finds the four by id; any other id returns false.
6. `Has`/`Remaining` with an id that `TryGet` rejects throw `ArgumentOutOfRangeException`. An unknown enemy id still returns false/0.
7. Nothing else changes: every MK-06a and JG-16b crowd-control test passes with ids swapped for enum values.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sides_DebuffsAndHaste` | the four rules / read / stun, root, slow Debuff; haste Buff |
| `Priorities_StunRootSlowHaste` | the four / compare / 40 > 30 > 20 > 10 |
| `BossScale_HalfForCrowdControl_FullForHaste` | a boss / stun 2 s, haste 2 s / remaining 1 s, 2 s |
| `Changed_CarriesRuleAndOneStack` | a slow lands / event / `Rule.Side` Debuff, `Stacks` 1, `Status` slowed |
| `TryGet_KnownAndUnknown` | `status.enemy.hasted`, `status.enemy.nope` / `TryGet` / true, false |
| `Has_UnknownStatusId_Throws` | a living enemy / `Has(id, status.enemy.nope)` / throws |
| `StatusRule_Default_Refused` | `default(StatusRule)` into `EnemyStatusChanged` / / throws |
| existing CC tests | ids swapped in / run / all green |

## Out of scope

- Row order and icon count (ST-02); poison (ST-03a); boss immunity; the player's `StatusBoard`.

## As built

_Commit body at merge, ≤ 1 500 bytes._
