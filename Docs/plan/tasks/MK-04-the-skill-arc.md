# MK-04 — The skill arc beside the roll

**Size:** M · **Depends on:** MK-03 · **Branch:** `dev` (direct)
**Design refs:** GD §5.2 (owner, 2026-10-31); CH §3.5 (*The battle HUD*); AR §18.1 (commands polled
from `CommandPhase`), §18.4 (left 45 % is the stick)

## Goal

Every active of the run is a circle in an arc above and left of the roll — cooldown, ready glow,
auto badge, lock — tap casts, long-press toggles auto; passives sit small in the corner. It replaces
the auto-cast row under the HP bar and the four manual slots.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/SkillArc.cs` | Game | **New.** Lays out and drives the arc + corner |
| `Game/Presentation/SkillArcButton.cs` | Game | **New.** One circle: icon, sweep, seconds, glow, "A", lock |
| `Game/Adapters/SkillArcInput.cs` | Game | **New.** Tap / long-press → commands, polled in `CommandPhase` |
| `Game/Presentation/HudLayout.cs` | Game | Roll position, arc and corner constants |
| `Prefabs/UI/Hud.prefab` | prefab | Arc + corner; old row and slots removed |
| `Tests/Game/Presentation/SkillArcTests.cs` | Tests.Game | **New** |
| *small edits* | | `SkillRunner` (`AutoCastGap`, `CastNow`), `IPlayerCommands` (`CastSkillNow(ContentId)`), `RunSession`, `RunTicker` (poll), `FrameOrderTests`. **Removed** with the owner's OK on this spec: `AutoCastRow`, `SkillBarPresenter`, `SkillSlotInput` and their tests |

## Behaviour

1. **Roll** moves in from the corner (centre ~96 dp from the right, ~120 dp from the bottom; consts in
   `HudLayout`, the owner tunes them). Size unchanged.
2. **Arc:** one 52 dp circle per active of the run's tree — class first (Volley), then the path's in
   path order — on an arc around the roll from straight up to straight left, ≥ 8 dp apart. Max 6.
3. A circle shows: icon; dark radial sweep + whole seconds left while cooling; **cyan glowing border**
   when ready; a small **"A"** badge while auto-cast is on; Volley keeps its shot pips.
4. **Locked:** an active not yet taken shows greyed with a padlock; if its unlock level is above the
   current level, also "Lv N". Path actives appear only after the path is chosen.
5. **Tap** casts now (`CastSkillNow`) even when auto is on; refused silently while cooling or locked.
   **Long-press** (≥ 0.45 s) toggles auto-cast (`SetAutoCast`) with a small haptic.
6. **Auto-casts are spaced:** `SkillRunner` refuses an *auto* cast within `AutoCastGap` = 0.5 s of any
   cast; it fires on a later tick. Taps ignore the gap.
7. **Corner passives:** one 28 dp icon per passive of the run's tree (taken = lit, untaken = greyed +
   padlock, "Lv N" when above level), below and right of the roll, not raycast targets.
8. Every circle is a raycast target outside the stick's left 45 %. A left-handed mirror, if set,
   mirrors the cluster.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arc_OneCirclePerActive_InOrder` | Ranger + Marksman fixture (2) |
| `Arc_SpacingAndBounds` | ≥ 8 dp, inside 360 × 780, outside left 45 % (2, 8) |
| `Circle_CoolingShowsSweepAndSeconds` / `Circle_ReadyGlows` / `Circle_AutoBadge` (3) |
| `Circle_LockedAboveLevel_ShowsLevel` (4) |
| `Tap_CastsEvenWhenAuto` / `Tap_Cooling_Refused` (5) |
| `LongPress_TogglesAuto` (5) |
| `AutoCast_WithinGap_Deferred` / `Tap_IgnoresGap` (6) |
| `Corner_PassivesLitAndLocked` / `Corner_NotRaycastTargets` (7) |
| `Input_PolledFromCommandPhase` | `FrameOrderTests` row (AR §18.1) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 12 with the Marksman: Volley's circle above-left of the roll; passives in the
   corner; long-press Volley drops the "A"; tap fires it.
2. **[device]** Thumb reach: every circle reachable without moving the hand.

## Out of scope

- The status row (MK-05). New skills' own effects.

## As built

_Filled at merge._
