# MK-06b — Stun, root and slow are drawn

**Size:** M · **Depends on:** MK-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (*The battle HUD*); JG-10 (enemy bars); colour law (no red; cyan = player)

## Goal

A stunned, rooted or slowed enemy looks it — on its body and as a small timed icon over its health
bar.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyStatusView.cs` | Game | **New.** Per enemy: body effects + animator response, on `EnemyStatusChanged` |
| `Game/Views/EnemyHealthBar.cs` | Game | Up to three status icons above the bar, each with a sweep |
| `Prefabs/VFX/StunSparks.prefab` | prefab | **New.** Three pale sparks circling over the head |
| `Prefabs/VFX/RootVines.prefab` | prefab | **New.** Cyan-lit rope/vine coils at the feet |
| `Prefabs/VFX/SlowFrost.prefab` | prefab | **New.** Pale-blue mist at the feet, faint |
| `Tests/Game/Views/EnemyStatusViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `EnemyViews` (attach/pool the view; reset in `OnDespawn`), `EnemyAnimatorView` (stun pose), `StatusIconBook` (`status.enemy.stunned/rooted/slowed`), `DebugOverlay` (dev-only stun/root/slow buttons, manual step 1) |

## Behaviour

1. **Stunned:** sparks circle over the head; the animator holds — speed 0.1 on its current state, or
   a stun/dizzy clip if the rig has one — and resumes on the end event. Flinch still plays on hits.
2. **Rooted:** coils at the feet, scaled to the body radius; the legs keep animating.
3. **Slowed:** frost at the feet; the animator speed × (1 − fraction) while slowed.
4. **Bar icons:** 14 dp each, left to right stun → root → slow, with a radial sweep of the time left;
   shown only while the bar is shown (JG-10's rules) — a bar hidden at full health appears while a
   status is on.
5. Every effect and icon ends exactly on the end event or the enemy's death/despawn; a pooled enemy
   comes back clean (AR §18.4).
6. Effects are pooled (`ArenaPool` rule, AR §18.2); none instantiated per event after warm-up.

## Tests

| Test | Given / When / Then |
|---|---|
| `Stun_ShowsSparks_HoldsAnimator` / `Stun_End_Resumes` (1) |
| `Root_ShowsCoils_ScaledToRadius` (2) |
| `Slow_ScalesAnimator` (3) |
| `Bar_IconsInOrderWithSweep` / `Bar_AppearsWhileStatused` (4) |
| `Death_ClearsEverything` / `Pooled_ComesBackClean` (5) |
| `Effects_ArePooled` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Debug: a console/DebugOverlay toggle that stuns/roots/slows the nearest enemy for 3 s
   (dev build only) — each reads at a glance on the dark floor; the bar icon counts down.

## Out of scope

- The skills that apply these.

## As built

_Filled at merge._
