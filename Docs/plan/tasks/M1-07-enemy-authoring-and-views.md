# M1-07 — `EnemyDefinition`, `Husk.asset`, `EnemyView`, `EnemyViews`, dummy prefab

**Size:** M · **Depends on:** M1-06 · **Branch:** `m1-07-enemy-authoring-and-views`
**Design refs:** AR §3, §10.1; ADR-0006; GD §16.4 (colour language)

## Goal

Enemies core decided to spawn appear in the scene, report their positions back through the snapshot, and vanish when core despawns them — with the enemy authored as data like the player.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/EnemyDefinition.cs` | Game | ScriptableObject + `ToSpec()` |
| `Game/Views/EnemyView.cs` | Game | A body for one enemy id |
| `Game/Views/EnemyViews.cs` | Game | Id → view map; reacts to spawn/despawn events |
| `Tests/Game/Authoring/EnemyDefinitionTests.cs` | Tests.Game | Conversion + shipped numbers |
| **Assets** | | |
| `Data/Enemies/Husk.asset` | — | GD §8.1 numbers, `Behaviour = Static` for now |
| `Prefabs/Enemies/Enemy.prefab` | — | Capsule (bone-grey), `EnemyView`, `CapsuleCollider` (trigger, layer `Enemy`) |
| *small edits* | | `BootScope` + `EnemyDefinition[]`; `BootInstaller` builds catalog enemies; `RunScope` + spawn-plan positions + enemy prefab; `SnapshotBuilder` fills `Enemies` from `EnemyViews`; `Run.unity`; `TagManager` layer `Enemy` |

## Public API

```csharp
namespace Soulvail.Game.Authoring;

[CreateAssetMenu(menuName = "Soulvail/Content/Enemy", fileName = "Enemy")]
public sealed class EnemyDefinition : ScriptableObject
{
    // [SerializeField] private … one per EnemySpec field, with sensible [Min]s
    public string Id { get; }
    public EnemySpec ToSpec();
}
```

```csharp
namespace Soulvail.Game.Views;

public sealed class EnemyView : MonoBehaviour
{
    public int     Id       { get; }
    public Vector3 Position { get; }
    public Vector3 Velocity { get; }          // set by movement application (M1-18); zero until then
    public void Bind(int id, Vector3 position);
    public void Unbind();
}

public sealed class EnemyViews : IDisposable
{
    public EnemyViews(IObjectResolver resolver, EnemyView prefab, Transform parent, DomainEventHub hub);
    public int Count { get; }
    public bool TryGet(int id, out EnemyView view);
    public void CopyInto(WorldSnapshot snapshot);      // one EnemySense per live view; path/LOS left zero/false until M1-19
    public void Dispose();
}
```

## Behaviour

1. `EnemyViews` subscribes to `EnemySpawned` → instantiates the prefab through `resolver.Instantiate` (so `[Inject]` works later), `Bind(id, position)`, stores by id. `EnemyDespawned` → `Unbind`, `Destroy`. **Instantiate/Destroy is a stopgap** accepted only until M1-19 pools them; the PROGRESS entry says so.
2. `CopyInto`: `snapshot.Clear()` is **not** called here (the builder owns that); for each view `ref var e = ref snapshot.AddEnemy(); e.Id, e.Position, e.Velocity` — stops at capacity and logs a warning once if exceeded.
3. `SnapshotBuilder.Build` gains `enemyViews.CopyInto(snapshot)` after the player fields.
4. `RunScope` serialises `Vector3[] _dummyPositions` (eight, spread around the arena, none within 6 m of origin) and `EnemyDefinition _dummySpec`; `RunTicker.Start` builds `new RunConfig(pending.CharacterId, new SpawnPlan(...))`. (`RunConfig` gained `SpawnPlan` in M1-06.)
5. `EnemyView` has a trigger `CapsuleCollider` on layer `Enemy` so M1-12's overlap query can find it; a static `EnemyView.FromCollider(Collider)` lookup (dictionary populated in `Bind`) returns the id — no `GetComponent` in the query loop.
6. Prefab colour: desaturated bone grey (GD §16.4). No red — red is reserved for danger telegraphs.
7. `Husk.asset`: id `enemy.husk`, name key `enemy.husk.name`, hp 36, speed 3.5, priority 1, not elite, contact 8, reach 1.2, windup 0.4, recover 0.6, behaviour `Static` (flipped to `Chaser` in M1-18).

## Tests

| Test | Given / When / Then |
|---|---|
| `Husk_ToSpec_MatchesGameDesign` | asset / ToSpec / values in rule 7 |
| `AllEnemyDefinitions_ValidUniqueIds` | FindAssets / ToSpec each / no throws, distinct |
| `ToSpec_InvalidPriority_ThrowsNamingAsset` | priority 9 via SerializedObject / ToSpec / throws with asset name |

`EnemyViews` is exercised by the manual steps; it's a body.

## Manual verification (Editor)

1. Play in `Run`: eight grey capsules appear at the serialised positions the moment the run starts (no frame with zero enemies after `RunStarted`).
2. Debug overlay (extend by one line in this task: `enemies {n}`) shows 8.
3. Move around — the player capsule walks *through* dummies for now (their collider is a trigger; blocking comes with the chaser body in M1-18).

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified
- [ ] `PROGRESS.md` entry appended (note the Instantiate stopgap); Current State updated; ROADMAP box ticked

## Out of scope

- Reticle — M1-09. Movement, chasing — M1-18. Pooling — M1-19.
- Health bars / damage tint — M3-13.

## As built

_Filled at merge._
