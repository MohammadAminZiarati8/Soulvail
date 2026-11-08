# BT-04 — The pack's formation: slots beside him, spacing, eased following

**Size:** M · **Depends on:** BT-03 · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.2 (Following)

## Goal

Out of a fight each animal walks its own slot beside or behind him, animals never overlap, and they
follow with ease and lag so they never feel stick-driven.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PackFormation.cs` | Core | **New.** Slot offsets, the lagged facing, arrival speed, separation push. Pure math |
| `Core/Ai/PetSystem.cs` | Core | Heel/idle movement goes through `PackFormation`; separation applies in every state |
| `Tests/Core/Ai/PackFormationTests.cs` | Tests.Core | **New** |
| `Tests/Core/Ai/PetFollowTests.cs` | Tests.Core | **New.** Through `PetSystem.Tick` |
| *small edits* | | `PetSystem.Tick` gains `playerFacing` · `RunSession` passes body facing · pet test call sites |

## Public API

```csharp
public sealed class PackFormation {
    public const float DeadZone = 0.5f, Spacing = 1.1f, TurnDegreesPerSecond = 240f;
    public static Vector2 SlotOffset(int slot);        // in the frame of the lagged facing
    public Vector2 Facing { get; }                      // lags the body facing
    public void Tick(float dt, Vector2 bodyFacing);
    public static float ArrivalSpeed(float distance, float maxSpeed); // min(max, 2.5 × distance)
    public static Vector2 Separation(ReadOnlySpan<Vector2> positions, int self); // ≤ 1.5 m/s
}
// slots: 0 right (1.6, 0) · 1 left (-1.6, 0) · 2 behind (0, -1.8) · 3 behind-left (-1.2, -1.6)
```

## Behaviour

1. **Slots:** pet index *i* (lead first) holds `SlotOffset(i)` relative to him, rotated by the
   formation's facing. A heeling or idle animal's goal is its slot, not his position.
2. **Lagged facing:** turns toward the body facing at `TurnDegreesPerSecond`, so a quick flick of
   the stick does not swing the pack around.
3. **Dead zone:** an animal within `DeadZone` of its slot stands still.
4. **Easing:** beyond it, speed is `ArrivalSpeed` (decelerates into the slot, never above `MoveSpeed`).
5. **Spacing:** any two standing animals closer than `Spacing` are pushed apart, hunting or not;
   fallen bodies and a leaping lead are skipped.
6. **Heel regen** still counts from the 2 m `AtHeelRadius` around him; every slot is inside it.

## Tests

| Test | Given / When / Then |
|---|---|
| `SlotOffset_LeadRight_PackLeftAndBehind` (1) | |
| `Idle_PetsSettleOnSlots` (1) | |
| `Facing_Flick_TurnsAtCappedRate` (2) | |
| `WithinDeadZone_NoMove` (3) | |
| `ArrivalSpeed_SlowsNearSlot_CappedByMoveSpeed` (4) | |
| `Separation_TwoOverlapping_PushedApart` / `Separation_FallenSkipped` (5) | |
| `Heel_SlotWithinRegenRadius` (6) | |
| `Tick_NoAllocation` | `AllocationAssert.None` |

## Manual verification (Editor / device)

1. **[Editor]** With two pack hounds: idle, they stand right, left and behind, never stacked.
2. **[Editor]** Jiggle the stick: the pack does not twitch; walk off: they trail and ease in.

## Out of scope

- Pathing around cover. The boar's slot is used from BT-07a.

## As built

_Filled at merge._
