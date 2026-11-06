# EA-01a — Shades and Mirror Image in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Echo Archer); AR §18.1; SK-03a

## Goal

Shades — Ranger copies that stand, shoot and fade — and Mirror Image to cast them.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ShadeSystem.cs` | Core | **New.** Store of 8: spawn, fire, expire |
| `Core/Events/ShadeEvents.cs` | Core | **New.** Appeared, loosed, faded |
| `Core/Effects/MirrorImage.cs` | Core | **New.** `RicochetShot`'s shape |
| `Core/Effects/PlayerStat.cs` | Core | Append `MirrorImageShades` (base 1), `MirrorImageSeconds` (base 6) |
| `Tests/Core/Combat/ShadeSystemTests.cs` | Tests.Core | **New** |
| `Tests/Core/Effects/MirrorImageTests.cs` | Tests.Core | **New** |
| *small edits* | | `Projectile`/`ProjectileFired` (`ShotOrigin Origin`), `CombatantStats`, `RunSession` (construct, tick, `Register`, clears), stat tests |

## Public API

```csharp
public enum ShadeKind { Image, Decoy, Crossfire, Hall }
public enum ShotOrigin { Ranger, Shade }   // EA-01b, EA-04a append
public readonly struct Shade { Id, Kind, Position, ExpiresAt, DamageOfWeapon, NextShotAt }
public sealed class ShadeSystem {
  public const int Capacity = 8; public const float SideOffset = 1.2f;
  public ShadeSystem(ProjectileSystem projectiles, LureSystem lures, PlayerStats stats, CharacterSpec character, IDomainEvents events);
  public ReadOnlySpan<Shade> Standing { get; }
  public int Spawn(ShadeKind kind, Vector3 position, float now, float seconds, float damageOfWeapon);
  public void Tick(float now, EnemySystem enemies);
  public void Clear(); }
// ShadeAppeared(Id, Kind, Position, ExpiresAt) · ShadeLoosed(Id, Target) · ShadeFaded(Id, Position, bool Expired)
public sealed class MirrorImage : IEffect { public MirrorImage(float damageOfWeapon); }
```

## Behaviour

1. **Spawn** stores a shade and publishes `ShadeAppeared`. Full → the oldest leaves first with
   `Expired: false`. Ids rise from 1, never reused in a run.
2. **Expiry:** at `now ≥ ExpiresAt` a shade leaves with `ShadeFaded(Expired: true)`.
3. **Fire:** only `Image` and `Hall` shoot. When ready, `PickRicochetTarget` from the shade
   (`WeaponRange`, no preferred id); one led arrow, `Origin = Shade`, for `DamageOfWeapon ×
   WeaponDamage`; `ShadeLoosed`; next shot in `1 / FireRate`. No target or a full sky → retry next
   tick. It may shoot on the tick it appears.
4. Shade kills pay XP the usual way; shade arrows take no Momentum, give no Hit and Run.
5. `ShadeSystem.Tick` runs right after the player's queued shots fire in `RunSession.Tick`.
6. **Clear** (stage boundary, death, run end): all leave, `Expired: false`.
7. **Mirror Image:** `MirrorImageShades` (clamped 1–3) `Image` shades for `MirrorImageSeconds` at
   `damageOfWeapon`, at the player ±X by `SideOffset`, the third −Z; no arena clamp.
8. No enemy in `WeaponRange` with a clear line → `CanCast` false, cooldown kept. Auto-cast
   `EnemiesInAcquireRange ≥ 1`; cooldown 14 s.
9. **Ranks** (for EA-02): 1 — Active, 0.5 × weapon; 2 — `MirrorImageShades` +1;
   3 — `MirrorImageSeconds` +2; 4 — `MirrorImageShades` +1, cooldown −3 s (`ModifySkillCooldown`).
10. No allocation per tick, spawn or shot.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spawn_PublishesAppeared` / `Spawn_Full_OldestLeavesNotExpired` (1) |
| `Expiry_PublishesFadedExpired` (2) |
| `Image_ShootsNearest_AtFireRate_ShareOfWeapon` / `Decoy_NeverShoots` / `NoTarget_Waits` (3) |
| `ShadeKill_PaysXp` / `ShadeHit_NoHitAndRunBurst` (4) |
| `Session_ShadeArrow_AfterPlayerShots` | one tick / player's arrow id lower (5) |
| `Clear_AllLeaveNotExpired` (6) |
| `Rank1_OneShade_SixSeconds` / `Rank4_ThreeShades_Positions` (7, 9) |
| `Cast_NoneInRange_Refused_CooldownKept` (8) |
| `Tick_DoesNotAllocate` (10) |

## Out of scope

- Views and assets (EA-02); lures, bursts, Crossfire (EA-03a, -03b, -04a).

## As built

_Filled at merge._
