# EA-04a — Crossfire in core

**Size:** M · **Depends on:** EA-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Crossfire); SK-03a (`PickRicochetTarget`, the cast guard)

## Goal

An active that conjures two shades at the Ranger's sides and has every standing shade loose one
heavy arrow at the Ranger's target.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Crossfire.cs` | Core | **New.** Effect + handler + cast guard (`RicochetShot`'s shape) |
| `Core/Combat/ShadeSystem.cs` | Core | `LooseAll`: one arrow from every standing shade at a target |
| `Core/Effects/PlayerStat.cs` | Core | Append `CrossfireShades` (base 2), `CrossfireDamage` (base 0) |
| `Tests/Core/Effects/CrossfireTests.cs` | Tests.Core | **New** |
| *small edits* | | `ShotOrigin` (append `Crossfire`), `CombatantStats`, `RunSession` (one `Register`), stat coverage tests |

## Public API

```csharp
public sealed class Crossfire : IEffect {
  public const float ShadeSeconds = 1f;
  public Crossfire(float damageOfWeapon); }
public int LooseAll(EnemyAgent target, float damage, float now);   // on ShadeSystem; returns arrows loosed
```

## Behaviour

1. **Target:** the player's current target in `WeaponRange` with a clear line, else the nearest
   such enemy (`PickRicochetTarget` from the player). None → `CanCast` false, cooldown kept.
2. **Conjure:** `CrossfireShades` (clamped 2–3) `Crossfire` shades for `ShadeSeconds` at the
   player ±X by `SideOffset`, the third −Z (EA-01a's places).
3. **Loose:** then `LooseAll` — every standing shade of every kind (the new ones, Image, Decoy,
   Hall) looses one arrow from its position, led at the target, for
   `(damageOfWeapon + CrossfireDamage) × WeaponDamage`, `Origin = Crossfire`, and publishes
   `ShadeLoosed`. A shade without a clear line to the target looses nothing.
4. Crossfire arrows are ordinary arrows: the first enemy in the way takes one; nothing pierces.
   They do not touch a shade's own fire timer. A full sky skips the rest.
5. A Crossfire shade never shoots on its own; it fades after `ShadeSeconds` with `Expired: true`
   (so Fading Burst applies).
6. Auto-cast: `EnemiesInAcquireRange ≥ 1`. Cooldown 12 s.
7. **Ranks** (for EA-04b): 1 — Active, 1.5 × weapon; 2 — `CrossfireShades` +1;
   3 — `CrossfireDamage` +0.5 and cooldown −3 s (`ModifySkillCooldown`).
8. No allocation per cast.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cast_PicksCurrentTarget_ElseNearest` / `Cast_NoneInRange_Refused_CooldownKept` (1) |
| `Cast_ConjuresTwoAtSides_OneSecond` (2) |
| `Cast_EveryStandingShadeLooses` | 1 Image + 1 Decoy standing / 4 arrows (3) |
| `Arrow_Damage_OnePointFiveWeapon` / `BlockedShade_LoosesNothing` (3) |
| `Arrow_FirstInTheWayTakesIt` / `ImageFireTimer_Unchanged` (4) |
| `CrossfireShade_NeverShoots_FadesExpired` (5) |
| `Rank3_ThreeShades_TwoTimes_NineSeconds` (7) |
| `Cast_DoesNotAllocate` (8) |

Rule 6 is the asset's trigger, pinned in EA-04b.

## Out of scope

- The heavy arrow's look and the assets (EA-04b).

## As built

_Filled at merge._
