# JG-20a — Thorn walls in core: arm, bite, stand, wither

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 6; GD §7.2 (a circle-strafe route); AR §18.2 (core holds no walls), §18.4; `LaneSystem`'s shape

## Goal

A reusable line hazard that warns, bites once as it rises, then stands as a wall for seconds and
withers — Thornheart's roots and slam (20b) and, later, the Thornweaver's.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ThornSystem.cs` | Core | **New.** Fixed-capacity walls, absolute deadlines |
| `Core/Events/ThornEvents.cs` | Core | **New.** Three events |
| `Tests/Core/Combat/ThornSystemTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession`: a `_thorns` field built beside `_lanes`, ticked beside it, cleared with it at the stage boundary and on `Reset` |

## Public API

```csharp
public sealed class ThornSystem {
  public const int Capacity = 12;  public const float MinArmSeconds = 0.6f;
  public ThornSystem(IDomainEvents events);
  public int ActiveCount { get; }
  public Vector3 OriginAt(int i); Vector2 DirectionAt(int i); float LengthAt(int i);
  public float HalfWidthAt(int i); bool IsStandingAt(int i);
  public int Open(Vector3 origin, Vector2 direction, float length, float halfWidth,
                  float armSeconds, float standSeconds, float damage, float now); // id, 0 = refused
  public bool Blocks(Vector3 point, float clearance);   // XZ, arming or standing
  public void WitherAll();
  public void Tick(float now, Vector3 playerPosition, PlayerCombat player);
  public void Clear();
}
// ThornEvents.cs: ThornArmed(Id, Origin, Direction, Length, HalfWidth, ArmSeconds)
//   ThornRose(Id, Hit) · ThornWithered(Id)
```

## Behaviour

1. **Open** validates (finite, positive length/width/stand, `armSeconds ≥ MinArmSeconds`,
   `damage ≥ 0`, direction normalised) and publishes `ThornArmed`. Ids from 1, never reused.
2. **Rise** at `now + armSeconds`: a player inside the rectangle (XZ, `LaneSystem.Contains`' maths)
   takes `damage` once through `PlayerCombat.ApplyDamage`, so roll i-frames answer it. Publishes
   `ThornRose(id, hit)`.
3. **Stand** for `standSeconds`, biting no more (the wall itself is Unity's collider, 20e), then
   withers: `ThornWithered`, slot compacts.
4. **Full:** the oldest *standing* wall withers to make room; with none standing, `Open` refuses
   (returns 0). An arming wall is a promise and is never dropped by `Open`.
5. **Blocks** is true for a point within `halfWidth + clearance` of any live wall's segment.
6. **WitherAll** (a boss beat) and **Clear** (stage boundary, reset) drop every wall, arming ones
   included, publishing `ThornWithered` for each in slot order.
7. A tick that swallows rise and wither lands both, rise first. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Open_Valid_PublishesArmed` / `Open_ArmUnderPointSix_Throws` (1) |
| `Rise_PlayerInside_DamagedOnce` / `Rise_PlayerOutside_NotHit` / `Rise_RollIFrames_NoDamage` (2) |
| `Stand_NoSecondBite_ThenWithers` (3) |
| `Full_OldestStandingWithers` / `Full_AllArming_Refuses` (4) |
| `Blocks_NearSegment_True_FarFalse` (5) |
| `WitherAll_DropsArmingAndStanding` / `Clear_PublishesWitheredInOrder` (6) |
| `Tick_SwallowsBoth_RiseThenWither` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- Who opens walls (20b); colliders and the look (20e).

## As built

_Filled at merge._
