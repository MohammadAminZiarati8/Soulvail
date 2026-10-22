# JG-02 — The Pitcher's spit

**Size:** M · **Depends on:** JG-01 · **Branch:** `dev` (direct)
**Design refs:** GD §16.3 (every projectile has a tracer), GD §16.4 (enemy projectiles are danger red-orange)

## Goal

An enemy can throw a shot of its own, and the Pitcher throws a slower glob of red-orange goo with a
trail and a splat.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/EnemyLook.cs` | Game | `EnemyLook.Projectile`, `EnemyLookBook.Projectiles` |
| `Game/Views/ProjectileViews.cs` | Game | optional `enemyLooks`; an archetype's shot is prewarmed and flown |
| `Game/Views/ProjectileView.cs` | Game | optional `_trail`, cleared on `Bind` and `OnDespawn` |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (`_projectile`, `ToLook`); `Game/Composition/RunScope.cs` (comment); `Tests/Game/Views/ProjectileViewsTests.cs`, `Tests/Game/Authoring/EnemyLookTests.cs`, `Tests/Game/Authoring/JungleTests.cs` |
| *assets* | | `Prefabs/Projectiles/PitcherSpit.prefab`, `Prefabs/Vfx/VFX_PitcherSplat.prefab`, `Materials/M_PitcherSpit.mat`, `M_PitcherSpitTrail.mat`, `M_PitcherSplat.mat`; `Data/Enemies/Pitcher.asset` (speed 12 → 7, `_projectile`) |

## Public API

```csharp
EnemyLook(Color tint, float bodyScale, EnemyView body = null, ProjectileView projectile = null);
ProjectileView EnemyLook.Projectile { get; }
IReadOnlyList<ProjectileView> EnemyLookBook.Projectiles { get; }
ProjectileViews(..., Transform viewer = null, EnemyLookBook enemyLooks = null);
```

## Behaviour

1. An `EnemyDefinition` may name a shot prefab. `ToLook` carries it; empty is the default bolt. The
   book lists each distinct shot once.
2. `ProjectileViews` flies an archetype's own shot, prewarmed at construction to `classPrewarm`;
   an archetype with none flies the default.
3. A `ProjectileView` with a trail clears it when placed at a flight's start and when returned.
4. The Pitcher throws `PitcherSpit` at 7 m/s: a glossy `#FF4A1F` blob with two drips, a tapering
   trail, a 2.6 m lob, and `VFX_PitcherSplat` on a hit. Descent's Spitter is unchanged.

## Tests

| Test | Rule |
|---|---|
| `EnemyLookTests.Definition_ToLook_CarriesItsProjectile`, `Book_ListsEachProjectileOnce` | 1 |
| `ProjectileViewsTests.Fired_AnArchetypeWithAProjectileLookFliesIt`, `Fired_AnArchetypeWithNoLookFliesTheDefault` | 2 |
| `ProjectileViewsTests.Bind_ClearsTheTrail` | 3 |
| `JungleTests.JunglesSpitter_PlaysTheSpittersRole` (speed 7), `JunglesSpitter_ThrowsItsOwnSpit` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 2: each Pitcher lobs a red-orange goo glob with a short trail, slow
   enough to step out of; a hit splats red-orange.
2. **[Editor]** No trail streak appears from where a previous spit landed.

## Out of scope

- Descent's Spitter, whose default bolt is green (`M_Bolt`) against GD §16.4. Left as found.

## As built

As specified.

- **Red-orange, not the green proposed.** GD §16.4 reserves `#FF4A1F` for enemy projectiles, and the
  owner chose it on 2026-10-22 when asked.
- **The trail is opaque**, `M_Impact`'s unlit vertex-colour shader with the trail's gradient, so the
  spit adds no transparent layer (GD §11.3).
- **The materials are copies**: `M_PitcherSpit` of `M_Bolt` (URP Lit, smoothness 0.92, emission at
  0.55× the colour), `M_PitcherSplat` and `M_PitcherSpitTrail` of `M_Impact`. The splat is
  `VFX_ArrowImpact` re-coloured, with a larger star and ring and slower, longer shards.
- **Every enemy asset gains `_projectile: {fileID: 0}`.** `EnemyDefinitionTests.Husk_EveryYamlKeyBindsToAField`
  reserialises the Husk, and the new field is a new key; the rest were given it so a save changes nothing.
- **Verified:** 4 378 EditMode, 4 376 passed / 1 failed / 1 inconclusive, both the known pair.
