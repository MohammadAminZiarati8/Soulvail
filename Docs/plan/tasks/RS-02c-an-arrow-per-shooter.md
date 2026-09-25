# RS-02c — An arrow per shooter

**Size:** S · **Depends on:** RS-02b · **Branch:** `rs-02c-an-arrow-per-shooter`
**Design refs:** AR §18.4; M2-09 rule 3 · **Ledger rows:** none

## Goal

A class can name what its shots look like. The Ranger's are arrows, and every other shot in the
game — the Spitter's bolt, the Gravecaller's, the Emberwright's orb — looks as it does today.

## Why

`ProjectileFired.SpecId` is *"here so a view can pick a mesh per shooter"* (its own remarks), and
nothing has ever picked. `ProjectileViews` holds one pool of `Projectile.prefab`, so every shot in
the game is the same bolt. `Arrow.prefab` exists since RS-02a and is flown only by the sandbox.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ProjectileViews.cs` | Game | **Substantial edit.** One pool per prefab, chosen by the shot's `SpecId` (rules 1–4) |
| `Tests/Game/Views/ProjectileViewsTests.cs` | Tests.Game | **Edit.** Rows for rules 1–5 |
| *small edits* | Game | `CharacterLook` gains `Projectile`; `CharacterDefinition` gains `_projectile` (a `ProjectileView` prefab). `RunScope` passes the look book to `ProjectileViews`. `RangerSandboxScope` keeps flying `Arrow.prefab` directly |

## Public API

```csharp
// CharacterLook: public CharacterLook(GameObject body, ProjectileView projectile);
//                public ProjectileView Projectile { get; }   // null: the run's default shot
public ProjectileViews(IObjectResolver resolver, ProjectileView prefab, Transform parent,
    DomainEventHub hub, int prewarm = 0, CharacterLookBook looks = null);
```

## Behaviour

1. **A shot whose `SpecId` is a class with a projectile look flies that prefab.** Every other shot —
   an enemy's, or a class that names none — flies the default prefab, `Projectile.prefab`.
2. **One pool per prefab.** The default pool is prewarmed as today (`BootInstaller.ProjectileCapacity`).
   A class's pool is created on its first shot. Nothing allocates per shot after that.
3. **A body goes back to the pool it came from** on `ProjectileImpacted` and on `Dispose`.
4. **No look book is today's behaviour exactly.** A null `looks` flies every shot as the default,
   which is what the sandbox and every existing fixture construct.
5. **The shipped classes name nothing.** The Oathbound, the Gravecaller and the Emberwright leave
   `_projectile` empty, so nothing that flies today changes. RS-03c names `Arrow.prefab` on the
   Ranger.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fired_AClassWithAProjectileLookFliesIt` | a book mapping `character.x` to a test prefab / a shot with that `SpecId` / the body is that prefab's — rule 1 |
| `Fired_AnEnemyShotFliesTheDefault`, `Fired_AClassWithNoLookFliesTheDefault` | — / — / the default prefab — rule 1 |
| `Fired_AClassPoolIsBuiltOnceAndReused` | two shots, one after the other lands / — / one instance, reused — rule 2 |
| `Impacted_ReturnsABodyToItsOwnPool` | one class shot and one default shot land / — / each pool's inactive count rises by one — rule 3 |
| `Dispose_ReturnsEveryBodyToItsPool` | shots of both kinds in the air / `Dispose` / every body released — rule 3 |
| `NoBook_FliesEverythingAsTheDefault` | `looks` null / a class shot / the default prefab — rule 4 |
| `Shipped_NoClassNamesAProjectile` | the three class assets / — / `_projectile` empty — rule 5 |

## Manual verification (Editor / device)

1. **[Editor]** A run as the Gravecaller and one as the Emberwright. *Expected: their shots, and
   the Spitter's, look exactly as before.*

## Out of scope

- **A look per enemy shot.** Enemies keep the default. `EnemyLookBook` is where one would go.
- **The volley's arrows looking different.** RS-03d lights the bow instead.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
