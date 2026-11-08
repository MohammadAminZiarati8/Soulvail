# BT-02 — Axes stop at cover

**Size:** S · **Depends on:** BT-01 · **Branch:** `dev` (direct)
**Design refs:** CH §3.6 (Weapon row)

## Goal

A rock turns an axe on its way out; the way back passes cover, so no axe is ever stranded; he no
longer aims at an enemy behind cover.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/AxeSystem.cs` | Core | `Tick` takes the snapshot's `TraceCover`; an out step that meets cover turns there |
| `Core/Combat/PlayerCombat.cs` | Core | The target filter drops the `ReturningAxe` exemption from `IsBehindCover` |
| `Tests/Core/Combat/AxeCoverTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession` passes `snapshot.TraceCover` to `Axes.Tick` · axe test fixtures |

## Public API

```csharp
public void Tick(float dt, float now, Vector3 hand, PlayerCombat player, EnemySystem enemies,
    Func<Vector3, Vector3, Vector3?> traceCover);   // null = no cover in the arena
```

## Behaviour

1. **Out pass:** each step traces from the axe to its step end; on a hit the axe moves to the hit
   point, enemies on the step before it are swept, and it turns there (`AxeTurned` at the hit point).
2. **Back pass:** never traced; it homes through cover as now.
3. **Aim:** an enemy behind cover is not a target for the axe, as for the Ranger (FX-03).
4. A null `traceCover` plays exactly as before.

## Tests

| Test | Given / When / Then |
|---|---|
| `OutPass_CoverAtFourMetres_TurnsAtFour` (1) | |
| `OutPass_EnemyBeforeCover_Hit_EnemyBehind_NotHit` (1) | |
| `BackPass_ThroughCover_ReachesHand` (2) | |
| `Target_BehindCover_NotChosen` (3) | |
| `NoTrace_FullRange` (4) | |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage with stones: an axe thrown at a stone turns at it and flies home over it.

## Out of scope

- Axes bouncing off arena walls. The animals' line of sight (BT-07a for the boar).

## As built

_Filled at merge._
