# M7-05g — A body per archetype

**Size:** M · **Depends on:** — (M2-06's look book, M1-19's pool, RS-02c's pool per prefab) · **Branch:** `m7-05g-a-body-per-archetype`
**Design refs:** GD §11.3, GD §17.1; AR §18.4 (the pooled-reset invariant) · **Ledger rows:** none

The fourth task cut from the owner's go of 2026-09-27: *"today every enemy is one shared grey body
(`Prefabs/Enemies/Enemy.prefab`) told apart by `EnemyLook`'s tint and scale … a real Rootling needs
a body of its own per archetype: work out the smallest change to `EnemyViews`/`EnemyView` pooling
that allows it without breaking the other archetypes, and say what it costs."*

## Goal

An archetype's look may name a body prefab of its own. `EnemyViews` keeps one pool per body, rents
each spawn from its archetype's pool and returns it there. An archetype that names no body wears the
shared one, as every archetype does today.

## Why this shape — the smallest change

- **`ProjectileViews` already solved it** (RS-02c): a default pool, a dictionary of pools keyed by
  prefab, and a rental that remembers which pool a body came from. `EnemyViews` takes the same
  shape. Its constructor, its twelve call sites, `EnemyView` and `ViewPool` do not change.
- **The body rides the look.** `EnemyLook` is already the Game-side half of an `EnemyDefinition`,
  built at boot and read once per spawn. A third field is a body prefab, null for the shared one.
  Core learns nothing (AR §3).
- **A body is a whole `EnemyView` prefab.** It carries the trigger capsule, the controller, the hit
  feedback and the health bar at its root, as `Enemy.prefab` does. So targeting, the cone sweep, the
  collider index and every pooled reset in `EnemyView.OnDespawn` apply to it unchanged.
- **Every pool is prewarmed at construction**, unlike `ProjectileViews`, which builds a class's pool
  on its first shot. An enemy body has a controller and a skinned mesh, and a wave of ten built on
  the frame it lands is the hitch M1-19 removed.

## What it costs

- **Memory and load time, not frames.** Each archetype with a body adds one pool prewarmed to the
  shared pool's count (`DeviceEnemyCap + 1`, 29 on every tier), in every run, whether or not the
  mode rosters it. With the Rootling alone that is 29 more inactive bodies built while the Run scene
  loads. With six bodied archetypes it would be 174, and then the pools should be prewarmed from the
  run's roster instead. The mode is known when `RunScope` composes, so that is a one-argument
  change. It is named here and not built.
- **Draw calls: none added.** A body is one renderer, as the capsule is.
- **The shared material is GD §11.3's plan kept.** A body wears `M_Enemy` or `M_BoneGrey`, and
  `EnemyHitFeedback` still drives it through a property block.
- **The bodied pool is keyed by prefab.** Two archetypes that name the same body share one pool.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyViews.cs` | Game | one pool per body; a rental remembers its pool (rules 2–5) |
| `Game/Authoring/EnemyLook.cs` | Game | `EnemyLook.Body`; `EnemyLookBook.Bodies` (rules 1, 2) |
| `Game/Authoring/EnemyDefinition.cs` | Game | `_body`, carried by `ToLook()` (rule 1) |
| `Tests/Game/Views/EnemyViewsTests.cs` | Tests.Game | **New.** Rules 2–5 |
| *small edits* | | `Tests/Game/Authoring/EnemyLookTests.cs`: rule 1's rows, and rule 6's |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Authoring;
public readonly struct EnemyLook
{
    public EnemyLook(Color tint, float bodyScale, EnemyView body = null);
    public EnemyView Body { get; }                       // null: the shared body
}
public sealed class EnemyLookBook
{
    public IReadOnlyList<EnemyView> Bodies { get; }      // each distinct authored body, once
}
// EnemyDefinition: [SerializeField] private EnemyView _body;  ToLook() => new EnemyLook(_tint, _bodyScale, _body)

namespace Soulvail.Game.Views;
public sealed class EnemyViews
{
    public int PooledCountOf(EnemyView body);            // null: the shared body's pool
}
```

## Behaviour

1. **A look may name a body.** `EnemyDefinition._body` is carried by `ToLook()` as authored. An
   empty `_body` is the shared body and is not an error. `EnemyLook.Default` names none.
2. **`EnemyViews` builds one pool per body at construction.** The shared body gets one, and so does
   each distinct body in `EnemyLookBook.Bodies`. Each is prewarmed to the constructor's `prewarm`.
   A body equal to the shared prefab is the shared pool.
3. **A spawn wears its archetype's body.** `OnSpawned` rents from the pool of `looks.For(specId).Body`,
   or from the shared pool when that is null. The tint, the scale and the Elite flag are then applied
   to the rented body exactly as today.
4. **A despawn returns the body to the pool it came from**, whatever archetype is spawned next. A
   Rootling's body never stands in for a Husk.
5. **The census is the same across bodies.** `TryGet`, `TryGetId`, `CopyInto` and `Count` answer for
   every body. `Dispose` destroys every pool's bodies.
6. **Nothing that plays changes.** No shipped archetype names a body in this task, so a run builds
   the shared pool alone, as before. The three shipped archetypes name none.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Definition_ToLook_CarriesItsBody`, `Definition_ToLook_NoBodyIsTheShared` | a definition with and without `_body` / `ToLook()` / as authored (rule 1) |
| `Look_DefaultNamesNoBody` | `EnemyLook.Default` / read / `Body` null (rule 1) |
| `Book_ListsEachBodyOnce` | three looks, two naming one body and one none / `Bodies` / that body, once (rule 2) |
| `Views_PrewarmEveryBodysPool` | a book with one body, prewarm 3 / constructed / 3 pooled of the shared body, 3 of the other (rule 2) |
| `Views_ABodyEqualToTheSharedOneIsTheSharedPool` | a look naming the shared prefab / constructed / one pool (rule 2) |
| `Spawn_WearsItsArchetypesBody` | a bodied archetype and a plain one / both spawn / each view is an instance of its own prefab (rule 3) |
| `Spawn_StillAppliesTheLookToABody` | a bodied look with a tint and scale 1.2 / spawn / the rented body wears both (rule 3) |
| `Despawn_ReturnsTheBodyToItsOwnPool` | a bodied spawn, despawned, then a plain spawn / — / the plain spawn is not the bodied instance; a second bodied spawn reuses it (rule 4) |
| `Census_FindsABodiedEnemy` | a bodied spawn / `TryGet`, `TryGetId(collider)`, `CopyInto` / all answer it (rule 5) |
| `Dispose_DestroysEveryPool` | both pools warm / `Dispose` / no body left under the parent (rule 5) |
| `Shipped_TheThreeArchetypesWearTheSharedBody` | the Husk, Spitter and Bloater assets / `ToLook()` / `Body` null (rule 6) |

## Manual verification (Editor / device)

1. **[Editor]** Play a run. *Expected:* nothing changes. The Husks, Spitters and Bloaters are the
   tinted capsules they were.

## Out of scope

- The Rootling's prefab and its animator (M7-05h), and the asset that names it (M7-05i).
- Prewarming from the run's roster, which is named under *What it costs* and not needed for one
  body.
- A shared-material check across bodies, which M7-05f's `EnemyMaterial_CanGlow` begins.

## As built

**Deviations.** *(1)* **The four shipped enemy assets gain `_body: {fileID: 0}`.**
`EnemyDefinitionTests.Husk_EveryYamlKeyBindsToAField` reserialises `Husk.asset` and compares the
text, so a new field is a new key. It was the one red row of the first full pass. The key is
written into the Husk, the Spitter, the Bloater and the Warden, so all four read the same, not only
the one the row watches. *(2)* `EnemyViewsTests` registers the event hub in its container, because
`EnemyHitFeedback` on a body takes it through `[Inject]`. *(3)* The `Book_ListsEachBodyOnce` row
lives in `EnemyViewsTests`, beside the pools it feeds.

**Cost, as built.** Each bodied archetype adds one pool of 29 (`DeviceEnemyCap` 28 + 1) on every
tier, built while the Run scene loads. The Rootling is the only one: 29 more inactive bodies, each
with an Animator and a skinned mesh. No draw is added, because a body is one renderer. Prewarming
from the run's roster is still the named fix for when a second place brings more bodies.

**Red-checked.** With `PoolFor` always returning the shared pool, the fixture ran 6 / 2. The two
failures were exactly `Spawn_WearsItsArchetypesBody` and `Despawn_ReturnsTheBodyToItsOwnPool`.

**Verified.** `EnemyViewsTests` 8 / 8, `EnemyLookTests` with its four new rows, and every fixture
that builds an `EnemyViews` unchanged: 247 rows across eleven fixtures. The constructor did not
move. Suite counts are M7-05d's *As built*.
