# BM-02a — The Beastmaster's body and character asset

**Size:** M · **Depends on:** BM-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.6; GD §16.4, §17.1; art source ruling; RS-02b

## Goal

A playable Beastmaster in class select: the Barbarian on `Rig_Medium`, an axe in each hand, throwing
returning axes while he walks. No pet and no Space move yet.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Characters/Beastmaster.asset` | — | **New.** `character.beastmaster`: 100 HP, 3.1 m/s, `ReturningAxe` 6 dmg, `FireRate` 2, range 8, `AxeSpeed` 14, carry 2, 3 hits a pass, `ShotRadius` 0.5, `MarkSeconds` 4, fires while moving; movement `None`; no unlock price |
| `Prefabs/Characters/Beastmaster.prefab` | — | **New.** Variant of `Barbarian.prefab`: `axe_1handed` on both hand slots, round shield off, `M_Beastmaster` |
| `Materials/Characters/M_Beastmaster.mat` | — | **New.** On `T_Beastmaster_Albedo` |
| `Art/Characters/Textures/T_Beastmaster_Albedo.png` | — | **New.** The Barbarian atlas with every gold or red-orange cell repainted bone or cyan |
| `Animation/Controllers/AC_Beastmaster.controller` | — | **New.** Rig_Medium `Idle`, `Running_A`, `Hit_A`, `Death_A`; an upper-body `Throw` layer |
| `Game/Views/BeastmasterAnimatorView.cs` | Game | **New.** Throw on `PlayerAttacked(ReturningAxe)`; hides a hand's axe on `AxeThrown`, shows it on `AxeCaught` |
| `Tests/Game/Art/BeastmasterBodyTests.cs` | Tests.Game | **New** |
| *small edits* | | `Boot.unity` BootScope Characters and Class Select lists; `English`/`Pseudo` (name, blurb); `CharacterDefinition` exposes the axe fields; `RunScope` builds `AxeSystem` when the weapon is `ReturningAxe` |

## Behaviour

1. Class select shows the Beastmaster; choosing him starts a run with his body and axes. He is free
   until BM-08 prices him.
2. **Colour:** no texel within ΔE 20 of `Palette.Danger`, none in the gold band (40–55°, saturation
   > 0.5), none in the enemy lime band; the cyan he carries is `Palette.Player`'s.
3. **Hands:** the left and right hand slots each hold one axe prop; the view hides the one thrown
   and shows it on the catch, left first. With 3 axes (BM-07b) the third is never drawn in a hand.
4. **Throw:** the upper-body `Throw` plays on each throw while the legs keep walking (the Skirmisher's
   layering, SK-02).
5. The body is a Rig_Medium Generic avatar driven by Rig_Medium clips only.

## Tests

| Test | Given / When / Then |
|---|---|
| `Beastmaster_InCatalogAndClassSelect` (1) |
| `Beastmaster_AssetNumbers_MatchCh36` (1) |
| `Paint_NoDanger_NoGold_NoLime` (2) |
| `Prefab_AxeOnEachHand_NoShield` (3) |
| `View_ThrowHidesAxe_CatchShowsIt` (3) |
| `Controller_HasThrowLayer_AndRigMediumClips` (4, 5) |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 1 as the Beastmaster: axes fly 8 m and come back while walking; he
   never stands to throw; frogs die in about 3 throws.

## Out of scope

- Axe and mark views (BM-02b), the Hound (BM-03), Wild Swap (BM-06), the tree (BM-07), unlock (BM-08).

## As built

_Filled at merge._
