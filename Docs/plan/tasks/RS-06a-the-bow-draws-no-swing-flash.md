# RS-06a — The bow draws no swing flash

**Size:** S · **Depends on:** RS-03c (the Ranger plays a run) · **Branch:** `rs-06a-the-bow-draws-no-swing-flash`
**Design refs:** CC §4.1, GD §16.4 · **Ledger rows:** none

The owner's go of 2026-09-28: *"disable the blue thing that shows when the Ranger shoots"*. The
arrow stays as it is, and so do the cyan disc under the Ranger's feet (`FocusGlow`) and the bow's
volley glow.

## Goal

The Ranger's shots flash no cyan wedge. The Oathbound's swing flashes it exactly as before.

## Why the swing says its kind

- **The blue thing is `PlayerView`'s swing cone.** It is a child of `Player.prefab` wearing
  `M_Reticle` cyan: CC §4.1's 8 m, 60° wedge, shown for 0.1 s on every `PlayerAttacked`. Every class
  wears `Player.prefab` (RS-02b), and `PlayerCombat` publishes `PlayerAttacked` at every swing start
  for every weapon kind. So the Ranger's bow, `_weaponKind: 1` in `Ranger.asset`, flashed the Censer's
  wedge on every shot.
- **Core already knows the kind, at the moment it publishes.** `TickWeapon` reads
  `_weaponSpec.Kind` a few lines later to throw a cone or offer a shot. Putting it on the event lets
  the view decide what to draw from what it is told, as `FacingXZ` already does. The view needs no
  class, weapon or catalog.
- **The body stays shared.** Taking the cone off the Ranger's body cannot work: the cone is on the
  shared `Player.prefab`, not on the per-class body.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/PlayerView.cs` | Game | the wedge drawn for a cone swing only (rule 2) |
| `Tests/Game/Views/PlayerViewTests.cs` | Tests.Game | **New.** Rules 2 and 3 |
| *small edits* | | `Core/Events/CombatEvents.cs` (`PlayerAttacked.Kind`); `Core/Combat/PlayerCombat.cs` (publishes the weapon's kind); `Game/Sandbox/RangerSandboxLoop.cs` (the sandbox's bow says `Projectile`); `Tests/Core/Combat/WeaponTests.cs`, `PlayerProjectileTests.cs` (rule 1's rows) |

## Public API

```csharp
public readonly struct PlayerAttacked
{
    public readonly Vector2 FacingXZ;
    public readonly WeaponKind Kind;                 // new
    // Optional and last: a cone when unsaid, which is also what default(PlayerAttacked) reads
    public PlayerAttacked(Vector2 facingXZ, WeaponKind kind = WeaponKind.Cone);
}
```

## Behaviour

1. **A swing says what kind of weapon made it.** `PlayerCombat` publishes `PlayerAttacked` with
   `_weaponSpec.Kind`: `Cone` for the Censer, `Projectile` for a bow or a bolt. The sandbox's loop,
   which publishes its own swings, says `Projectile`.
2. **`PlayerView` draws the wedge for a cone swing only.** A projectile swing leaves the wedge hidden
   and does not turn it. A cone swing flashes it along the swing's facing for `_swingSeconds`, as
   before.
3. **The Oathbound's wedge is unchanged.** `Player.prefab` still dresses `SwingCone` on `M_Reticle`,
   at 0.1 s, 8 m and 60°. `FocusGlow` and the bow's volley glow are not touched.

## Tests

| Test | Given / When / Then |
|---|---|
| `WeaponTests.PlayerAttacked_NamesAConeSwing` | the Censer, a Husk in reach / a second of swings / every `PlayerAttacked` says `Cone` (rule 1) |
| `PlayerProjectileTests.Combat_ASwingNamesAShot` | the Gravecaller's bolt, a Husk in range / a swing starts / `PlayerAttacked` says `Projectile` (rule 1) |
| `PlayerViewTests.Swing_AConeSwingFlashesTheWedge` | a dressed body / a cone swing along +X / the wedge shown, facing +X (rule 2) |
| `PlayerViewTests.Swing_AShotDrawsNoWedge` | a dressed body / a projectile swing, then a cone swing / hidden and unturned; then shown (rule 2) |
| `PlayerViewTests.Player_TheOathboundsWedgeIsUnchanged` | `Player.prefab` / read / `SwingCone` on `M_Reticle`, 0.1 s, 8 m, 60° (rule 3) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → the Ranger → descend, and stand still near a Rootling. *Expected:* each
   draw looses an arrow with no cyan wedge in front of the Ranger. The cyan disc under its feet and
   the bow's glow when a volley is ready are as before.
2. **[Editor]** Resume or start an Oathbound run (clear *Class Select* on `BootScope.prefab` to show
   every class, then restore it). *Expected:* the faint cyan wedge flashes on each Censer swing,
   as before.

## Out of scope

- The arrow and its flight (the owner: *"the arrow itself is fine"*).
- `FocusGlow` and the volley glow.
- An arrow's impact. That is RS-06b.

## As built

**As specified.** The kind is optional on the constructor, for RS-05a's reason: about twenty
`new PlayerAttacked(Facing)` lines in `PlayerAnimatorViewTests` and `RangerAnimatorViewTests` stay as
they are, and neither animator reads the kind. `default(PlayerAttacked)` reads `Cone` anyway, so a
required parameter would have added churn and protected nothing.

**Red-checked.** With `PlayerView`'s kind guard removed, `PlayerViewTests` went 2 / 1: exactly
`Swing_AShotDrawsNoWedge` failed.

**Verified.** EditMode 3 444 — 3 443 passed, 0 failed, 1 inconclusive (the animator clock row,
`Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns`) — +5 on RS-05d's 3 439. PlayMode
67 / 67 / 0 on an unfocused Editor, `Loop_ShootsOnlyStandingStill` included. Console: no compile
errors; the errors and warnings a pass leaves are the ones its rows provoke and expect. Format check
clean on the seven C# files. `ProjectSettings/TimeManager.asset` reverted.
