# RS-07b — Volley's own arrow and burst

**Size:** M · **Depends on:** RS-07a · **Branch:** `dev` (direct)
**Design refs:** GD §16.3 (every projectile has a tracer), GD §16.4 (player shots are cyan), GD §11.3 (no transparent layer), AR §3 (a model is not core's business)

## Goal

A Volley's arrows are told apart from ordinary shots: each is marked in core and flies a larger
arrow with a glowing cyan head, a wider, brighter trail and its own bigger burst on a hit.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ProjectileViews.cs` | Game | a volley shot flies its class's volley arrow; the arrow is prewarmed |
| `Core/Combat/ProjectileSystem.cs` | Core | `Projectile.IsVolley`; `Fire` carries it onto the event |
| *small edits* | | `Core/Events/ProjectileEvents.cs` (`ProjectileFired.IsVolley`); `Core/Combat/PlayerCombat.cs` (`QueueShot` marks a fan); `Game/Authoring/CharacterLook.cs` (`VolleyProjectile`); `Game/Authoring/CharacterDefinition.cs` (`_volleyProjectile`, `ToLook`); `Game/Views/ProjectileView.cs` (`_playerGlow`) |
| *tests* | | `Tests/Core/Combat/VolleyTests.cs`, `Tests/Core/Combat/ProjectileSystemTests.cs`, `Tests/Game/Views/ProjectileViewsTests.cs`, `Tests/Game/Authoring/CharacterLookTests.cs`, `Tests/Game/Authoring/RangerTests.cs` |
| *assets* | | `Prefabs/Projectiles/VolleyArrow.prefab`, `Prefabs/Vfx/VFX_VolleyImpact.prefab`, `Materials/M_VolleyHead.mat`, `Materials/M_VolleyTrail.mat`; `Data/Characters/Ranger.asset` (`_volleyProjectile`); the other three character assets gain the empty key; `Prefabs/Projectiles/Arrow.prefab` (its trail moves to a `Tail` child) |

## Public API

```csharp
// Core
Projectile(ContentId specId, int sourceId, Vector3 origin, Vector3 target, float speed,
           float radius, float damage, ShotSide side = ShotSide.AtPlayer, bool isVolley = false);
bool Projectile.IsVolley { get; }
ProjectileFired(int id, ContentId specId, int sourceId, Vector3 origin, Vector3 target,
                float flightTime, bool isVolley = false);
readonly bool ProjectileFired.IsVolley;

// Game
CharacterLook(..., LocKey attackDescriptionKey = default, ProjectileView volleyProjectile = null);
ProjectileView CharacterLook.VolleyProjectile { get; }
// ProjectileView (serialized): [SerializeField] private Renderer _playerGlow;
```

## Behaviour

1. Every arrow of a Volley's fan is a `Projectile` with `IsVolley` true; an ordinary shot, and every
   enemy shot, has it false. Damage, aim, count and order are unchanged.
2. `ProjectileSystem.Fire` publishes the shot's `IsVolley` on its `ProjectileFired`.
3. A `CharacterDefinition` may name a volley shot prefab; `ToLook` carries it as
   `CharacterLook.VolleyProjectile`. Empty is not an error.
4. `ProjectileViews` flies a volley `ProjectileFired` with its class's `VolleyProjectile`, and falls
   back to the class's ordinary shot when the class names none. A non-volley shot never flies it.
   Every named volley shot is prewarmed at construction to `classPrewarm`, like a class's shot.
5. A `ProjectileView`'s `_playerGlow` renderer, if any, is lit `Palette.Player` (base and emission,
   through a property block) on its first `Bind`, as RS-07a's trail is — never an authored cyan.
6. The Ranger's volley shot is `VolleyArrow.prefab`: the KayKit arrow at 2.2× RS-02c's 0.6615, an
   emissive cyan arrowhead (`_playerGlow`), a trail 0.26 m wide and 0.22 s long whose tail stays at
   0.75 shade, and `VFX_VolleyImpact` on a hit — `VFX_ArrowImpact`'s white-and-cyan burst with a
   larger star, ring and shards.

## Tests

| Test | Rule |
|---|---|
| `VolleyTests.Fan_EveryArrowIsMarkedAVolley` | 1 |
| `ProjectileSystemTests.Fire_CarriesTheVolleyMark` | 2 |
| `CharacterLookTests.CharacterLook_CarriesTheVolleyProjectile` | 3 |
| `ProjectileViewsTests.Fired_AVolleyFliesTheClassVolleyArrow`, `Fired_AVolleyWithNoVolleyArrowFliesTheShot`, `Construct_PrewarmsTheVolleyArrow` | 4 |
| `ProjectileViewsTests.Bind_LightsThePlayerGlowInPaletteCyan` | 5 |
| `RangerTests.Ranger_FliesItsVolleyArrow` | 6 |

## Manual verification (Editor / device)

1. **[Editor]** A Ranger run with Volley taken: every Nth shot is a fan of visibly bigger arrows with
   a glowing cyan head and a wider cyan trail; ordinary shots are RS-07a's arrow.
2. **[Editor]** A volley arrow that hits plays a bigger white-and-cyan burst than an ordinary hit.

## Out of scope

- A flash at the bow when the fan is released (the prompt's optional item): it needs a once-per-fan
  signal the view does not have, and the arrows' own look already marks the volley.
- Volley's damage, aim, count or timing.

## As built

- **Both trails ride on a `Tail` child at the fletching**, `Arrow.prefab`'s included (an RS-07a
  asset, changed here). The preview showed a trail on the root starting at the arrow's pivot, its
  ribbon covering the rear half of the shaft and the fletching; from the tail the arrow reads ahead
  of its streak. `RangerTests` reads the trail with `GetComponentInChildren`.
- **The arrowhead glow is a stretched diamond**: a cube turned 45° (0.17 m) under a `Head` scaled
  2× along the flight, at z 0.72, over the KayKit head. A sphere read as a bulb in the first preview.
  `M_VolleyHead` is URP Lit, white, `_EMISSION` on; `ProjectileView._playerGlow` (rule 5) lays
  `Palette.Player` and 1.5× it on through a property block, for RS-07a's reason (`PaletteTests`).
  The keyword had to be set again after `CreateAsset`, which dropped it.
- **"Brighter" is the tail, not the head.** The head is `Palette.Player` in both; the volley trail's
  tail stays at 0.75 shade where the arrow's falls to 0.4, and the trail is 0.26 m wide against 0.12 m.
- **`VFX_VolleyImpact`** is `VFX_ArrowImpact` resized, since `ImpactView` builds its own white
  star and cyan glints in code: star 1.05 → 1.7, ring 0.75 → 1.3 (width 0.07 → 0.11), shards
  0.4 → 0.6 long, 0.13 → 0.18 wide, at 9 m/s (was 7.5), each phase a few hundredths longer.
- **No bow flash** (out of scope, as written).
- **Preview:** `Temp/rs07_arrows.png` and `Temp/rs07_preview.png`, rendered through
  `SubmitRenderRequest` in a preview scene, were checked before handover.
- **Verified:** 4 389 EditMode, 4 387 passed / 1 failed / 1 inconclusive, both the known pair.
