# BM-01b — The Mark in core

**Size:** S · **Depends on:** BM-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6 (Signature); GD §16.5; ST-01

## Goal

An axe hit marks the enemy for a few seconds, as a status the pet (BM-03a) and Pounce (BM-07c) read.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/EnemyStatuses.cs` | Core | `MarkedKey = "status.enemy.marked"`, `Marked` rule: Debuff, priority 5, boss scale 1 |
| `Core/Ai/EnemySystem.cs` | Core | `Mark(int, float)`, `IsMarked(int)` |
| `Core/Content/WeaponSpec.cs` | Core | `MarkSeconds` (≥ 0; 0 = no mark) |
| `Core/Combat/AxeSystem.cs` | Core | Every `AxeHit` marks for `MarkSeconds` |
| `Tests/Core/Ai/MarkTests.cs` | Tests.Core | **New** |
| *small edits* | | `EnemyStatuses.TryGet` gains the sixth id; status-row tests that count rules |

## Public API

```csharp
public sealed class EnemyStatuses { public const string MarkedKey = "status.enemy.marked"; public StatusRule Marked { get; } }
public sealed class EnemySystem {
  public bool Mark(int enemyId, float seconds);   // false: dead, unknown, or seconds ≤ 0
  public bool IsMarked(int enemyId); }
public sealed class WeaponSpec { public float MarkSeconds { get; } }
```

## Behaviour

1. `Mark` posts `Marked` for `seconds`; a second mark refreshes to the longer remaining, never stacks.
2. It expires like every timed status and publishes what `Slow` publishes.
3. **Bosses are marked at full length** (boss scale 1): a mark is not crowd control (JG-26a stays).
4. `Marked` is the lowest-priority debuff, so a stun, root, slow or poison icon wins the one slot on
   a basic enemy's bar.
5. `AxeSystem` marks on every hit, out and back, when `MarkSeconds > 0`.
6. `MarkSeconds` must be finite and ≥ 0 on every weapon kind.
7. No allocation in `Mark` or `IsMarked`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Mark_Applies_IsMarked` / `Mark_Again_RefreshesNotStacks` (1) |
| `Mark_Expires` (2) |
| `Mark_Boss_FullLength` (3) |
| `Marked_LosesTheSlotToStun` (4) |
| `AxeHit_Marks_BothPasses` / `MarkSecondsZero_NoMark` (5) |
| `WeaponSpec_MarkSeconds_NonFiniteRefused` (6) |
| `Mark_DoesNotAllocate` (7) |

## Out of scope

- The claw glyph and icon (BM-02b); who reads the mark (BM-03a, BM-07c).

## As built

_Filled at merge._
