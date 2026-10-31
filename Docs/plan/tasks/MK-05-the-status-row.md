# MK-05 — The status row beside the HP bar

**Size:** M · **Depends on:** MK-02a, MK-04 · **Branch:** `dev` (direct)
**Design refs:** GD §5.2 (*Status*), CH §3.5 (*The battle HUD*)

## Goal

Buffs and debuffs on the player show as small icons right under the HP bar — where the auto-cast
row was — each with a timer sweep or a stack count. Steady Aim's stacks are the first status.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/StatusBoard.cs` | Core | **New.** The player's statuses: post, clear, tick, read |
| `Game/Presentation/StatusRow.cs` | Game | **New.** Draws the board |
| `Game/Authoring/StatusIconBook.cs` | Game | **New.** `ContentId` → sprite (block namespace) |
| `Tests/Core/Combat/StatusBoardTests.cs` | Tests.Core | **New** |
| `Tests/Game/Presentation/StatusRowTests.cs` | Tests.Game | **New** |
| *small edits* | | `SteadyAim` (posts `status.steady-aim`; `status.steady-aim.guard` at full stacks with Guard > 0), `RunState` (read), `RunSession` (construct, tick, boundary), `Hud.prefab`, `HudLayout` |

## Public API

```csharp
public sealed class StatusBoard {
  public const int Capacity = 8;
  void Post(ContentId id, bool isBuff, float seconds, int stacks); // seconds: +∞ = no timer
  void Clear(ContentId id); void Tick(float dt);
  int Count { get; } StatusEntry this[int i] { get; } }
public readonly struct StatusEntry { ContentId Id; bool IsBuff; float Remaining; float Duration; int Stacks; }
```

## Behaviour

1. `Post` adds or refreshes by id (no duplicates); `Tick` counts timers down and drops a status at 0;
   an untimed status stays until `Clear`. A new id over `Capacity` is refused and logged once —
   eight is a design ceiling.
2. Order: debuffs first, then buffs; within each, posting order.
3. **The row:** 24 dp icons under the HP bar, left-aligned with it, 4 dp apart. Buffs have a cyan
   rim, debuffs a pale-violet rim (no red — colour law). A timed status shows a radial sweep of
   `Remaining / Duration`; a stacked one shows its count bottom-right when > 1.
4. Steady Aim posts an untimed buff with its stack count while stacks > 0 and clears at 0; the guard
   status is posted while it applies.
5. Statuses clear at a stage boundary and at run end. Nothing allocates per tick or per redraw.

## Tests

| Test | Given / When / Then |
|---|---|
| `Post_RefreshesById` / `Tick_DropsExpired` / `Untimed_StaysUntilCleared` (1) |
| `Post_OverCapacity_Refused` (1) |
| `Order_DebuffsFirst` (2) |
| `Row_SweepAndStacks` / `Row_RimColours` (3) |
| `SteadyAim_PostsStacks_ClearsAtZero` / `Guard_PostedAtFullStacks` (4) |
| `Boundary_Clears` / `Tick_DoesNotAllocate` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Marksman with Steady Aim: stand still — the icon under the HP bar counts 1…5; move —
   it vanishes.

## Out of scope

- Enemy statuses (MK-06b). Statuses from other classes' effects.

## As built

_Filled at merge._
