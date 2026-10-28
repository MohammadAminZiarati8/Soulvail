# RS-08b — A volley finds every enemy in range

**Size:** M · **Depends on:** RS-08a (same file, `PlayerCombat.cs`) · **Branch:** `dev` (direct)
**Design refs:** RS-03b (the volley)

## Goal

When a volley is ready, the next damage frame looses one arrow at **each** enemy in range: one enemy, one
arrow; ten enemies, ten arrows. No fan.

## Why this shape

- **The trigger stays.** `Volley.Every` (5, then 4, 3 with ranks 2 and 3) still decides *when*. Only the
  shape changes, so the tree's three ranks keep their meaning.
- **"In range"** is what the targeter already uses: alive, vulnerable, within `Weapon.Range` (13 m).
  Each arrow is lead-solved at its own enemy, like the ordinary shot. There is no line-of-sight test per
  candidate today, and this task does not add one.
- **The shot queue grows to the enemy capacity.** `VolleySpec.MaxArrows` (7) was the queue's length.
  `VolleyArrows` and `FanAngleDeg` no longer shape anything: kept, unread, so saves and specs still load.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/PlayerCombat.cs` | Core | `OfferShot`: on a ready volley, `QueueShot` per enemy in range; `_shots` sized to capacity |
| `Core/Combat/Volley.cs` | Core | `Aim` fan removed; `ArrowCount` marked unused |
| `Data/Localisation/English.asset` | — | `skill.ranger.volley.description` |
| `Tests/Core/Combat/VolleyTests.cs` | Tests.Core | rules 1–5; the `Fan_*` rows replaced |
| *small edits* | | `Pseudo.asset` regenerated if the loc tests ask |

## Public API

```csharp
// No new members. Volley.Aim is removed; Volley.ArrowCount stays, read by nothing.
```

## Behaviour

1. A ready volley looses one arrow at each live, vulnerable enemy within `Weapon.Range`. The current target is included.
2. Each arrow aims at its own enemy's lead point at `ShotSpeed`, and does `Damage × Volley.DamageMultiplier`.
3. Every volley arrow is marked `isVolley`. One `PlayerAttacked` per volley.
4. With 0 other enemies in range, a volley is one arrow at the target. Corpses and enemies past 13 m get none.
5. A volley with as many enemies as the registry holds fills the queue without throwing. No allocation on the shot path.
6. The description reads: "After every 5 shots, the next one fires an arrow at every enemy in range,
   each dealing 50% more."

## Tests

| Test | Rule |
|---|---|
| `Volley_OneArrowPerEnemyInRange` (3 in range → 3 shots) | 1 |
| `Volley_EachArrowLeadsItsOwnEnemy` | 2 |
| `Volley_ArrowsCarryTheMultiplierAndMark` | 2, 3 |
| `Volley_IsOneTell` (kept) | 3 |
| `Volley_LoneTargetGetsOneArrow` | 4 |
| `Volley_SkipsCorpsesAndOutOfRange` | 4 |
| `Volley_FullRegistryFitsTheQueue_NoAllocation` | 5 |
| English loc row for the description | 6 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1 (3 Frogs): the 6th shot sends 3 arrows, one into each Frog.
2. **[Editor]** Late wave with ~10 enemies in range: the volley sends ~10 arrows.

## Out of scope

- The arrows' look (RS-08c), a per-arrow line-of-sight test, retuning `Every`.

## As built

Built 2026-10-28 on `dev`. `PlayerCombat` queues one lead-solved, ×1.5 marked arrow per live,
vulnerable enemy within the live weapon range, using the enemy-capacity buffer. `Volley.Aim` is
removed; `ArrowCount`, `VolleyArrows`, and `FanAngleDeg` remain for content/save compatibility but
do not shape shots. The English description and generated pseudo row are updated.

Verification: RS-08b `VolleyTests` 18/18 pass after the final compile; full EditMode run 4,519
pass, 1 inconclusive, 1 existing Editor D3D12 failure
(`CharacterPreviewTests.Resize_MakesTheTextureCappedAndFreesTheOld`, Traps §7). Unity compile:
0 errors, 0 warnings; `dotnet format whitespace` and `git diff --check` pass.

Manual Editor checks still owed: stage 1 three-Frog sixth shot; a late wave with about ten enemies.
