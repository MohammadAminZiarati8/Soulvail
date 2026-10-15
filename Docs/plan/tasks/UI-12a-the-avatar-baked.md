# UI-12a — The avatar, baked

**Size:** M · **Depends on:** UI-11h-c · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** owner's ruling of 2026-10-15 (an avatar per hero, a render of the 3D model, baked), UI-05b (the preview stage) · **Ledger rows:** none

## Goal

An Editor menu item renders each class's 3D body, head and shoulders, on the character preview's stage and writes it to `Art/UI/Heroes/Portrait_<Class>.png` as a sprite; `CharacterDefinition` holds it and `CharacterLook.Portrait` carries it to the screens.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Editor/Heroes/PortraitBaker.cs` | Soulvail.Editor | the menu item `Soulvail/Heroes/Bake Portraits`, the render, the import and the assignment |
| `Editor/Heroes/PortraitFraming.cs` | Soulvail.Editor | the framing arithmetic, a value type |
| `Art/UI/Heroes/Portrait_*.png` (4) | — | what the bake writes: one per `CharacterDefinition` with a body |
| `Tests/Game/Editor/PortraitBakerTests.cs` | Tests.Game | the framing rows, the import rows, the assignment rows |
| *small edits* | | `CharacterDefinition.cs`: `[SerializeField] private Sprite _portrait`, passed by `ToLook`; `CharacterLook.cs`: `Portrait`, a new optional constructor parameter after `spaceDescriptionKey`; `Ranger.asset`, `Oathbound.asset`, `Gravecaller.asset`, `Emberwright.asset`: `_portrait` set by the bake; `CharacterLookTests.cs`: the pass-through row |

## Public API

```csharp
// PortraitBaker — Editor-only, static because [MenuItem] is (LookCapture's shape). File-scoped namespace.
public static class PortraitBaker
{
    public const string MenuPath = "Soulvail/Heroes/Bake Portraits";
    public const string Folder = "Assets/_Project/Art/UI/Heroes";
    public const int Side = 512;                 // the PNG is Side × Side
    public const float FieldOfViewDeg = 20f;
    public const float ShoulderFraction = 0.42f; // the top this much of the body's height fills the frame

    // Bakes every CharacterDefinition with a body; returns the asset paths written.
    public static IReadOnlyList<string> BakeAll();

    // Rule 8's step, public so the tests reach it (Soulvail.Editor gives no InternalsVisibleTo).
    // Returns whether it assigned.
    public static bool AssignPortrait(CharacterDefinition definition, Sprite baked, string bakedPath);

    // The pure part: where the camera stands and what it looks at, in the body's space.
    public static PortraitFraming Frame(Bounds body, float fieldOfViewDeg);

    // "Portrait_" + the last segment of the id, PascalCase: character.ranger -> Portrait_Ranger.
    public static string FileNameFor(ContentId character);
}

// PortraitFraming.cs
public readonly struct PortraitFraming
{
    public Vector3 Position { get; }
    public Vector3 LookAt { get; }
}

// CharacterDefinition
[SerializeField] private Sprite _portrait;   // a baked render, or a painted one put in its place

// CharacterLook
public CharacterLook(GameObject body, ProjectileView projectile = null, ContentId volleyNode = default,
    Sprite emblem = null, Sprite spaceIcon = null, LocKey spaceNameKey = default,
    LocKey spaceDescriptionKey = default, Sprite portrait = null);
public Sprite Portrait { get; }   // null when the class has none: the screens fall back to Emblem
```

## Behaviour

1. **The menu item bakes every `CharacterDefinition` in the project whose body is set**, found by `AssetDatabase.FindAssets("t:CharacterDefinition")`, in path order. A definition with no body is skipped and logged once, by name. Today that is four files: the Ranger in KayKit's body and three classes in the Knight's.
2. **The body stands on the real stage.** The bake instantiates `Prefabs/UI/CharacterStage.prefab` (`HideFlags.HideAndDontSave`), stands the body with `CharacterPreview.Show(definition.ToLook())`, and destroys the stage afterwards, every path through, a throw included. The stage's lights and materials are the preview's, so the avatar and the page's 3D model match.
3. **The pose is the idle's first frame**: the body's `Animator` is rebound and updated by zero (`Rebind()`, `Update(0f)`) before the render, so no avatar is a T-pose. A body with no `Animator` renders as it stands.
4. **Head and shoulders, by arithmetic** (`Frame`): from the body's combined renderer bounds, the frame's height is the top `ShoulderFraction` of the bounds' height; the camera looks at the middle of that band, from in front of the body (its forward, +Z on the stage's anchor), at the distance where a `FieldOfViewDeg` vertical field just holds the band's height, and 5 % higher than the point it looks at, so it looks very slightly down. The stage's own camera is moved for the bake and put back.
5. **The render is `Side` × `Side`, transparent outside the body**: an ARGB32 target, the camera cleared to `(0,0,0,0)` with post-processing off, through `UniversalRenderPipeline.SingleCameraRequest` (`LookCapture.Submit`'s route). The pedestal, and anything on the stage that is not the body or a light, is off for the bake.
6. **It writes `Art/UI/Heroes/Portrait_<Class>.png`**, `<Class>` from `FileNameFor`, overwriting an earlier bake. The folder is created if it is missing.
7. **The import is the UI sprite's** (`HomeArt`'s rules): one full-rect sprite, 100 PPU, clamped, bilinear, alpha as transparency, no mipmaps, ASTC 4×4 on Android, `maxTextureSize` 512. The bake sets the importer itself, so a fresh bake needs no hand-built `.meta`.
8. **The bake assigns `_portrait` only where it may.** An empty `_portrait`, or one that already points at this class's baked file, is set to the new sprite and the definition saved. **One that points anywhere else is a painted portrait: the bake still writes its PNG but leaves the field alone**, and logs which class it skipped. A painted portrait therefore replaces the render by being dragged into the field, with no code change.
9. **`CharacterLook.Portrait`** is the definition's `_portrait`, or null; `ToLook` passes it through and nothing else reads it in this task.
10. **The bake runs in Edit mode only**, and the menu item is greyed in Play mode (`LookCapture`'s validator shape, inverted).

## Tests

| Test | Given / When / Then |
|---|---|
| `Frame_HoldsTheTopBandOfTheBody` | a 2 m-tall bounds at the origin / `Frame` / the look-at is at 2 − 0.42 × 2 ÷ 2 m, and the band's top and bottom both sit inside the vertical field, within 1 % of its edges (rule 4) |
| `Frame_StandsInFront_LooksSlightlyDown` | the same / `Frame` / position.z > look-at.z and position.y > look-at.y (rule 4) |
| `Frame_ScalesWithTheBody` | 1 m and 2 m bounds / `Frame` / the distance doubles (rule 4) |
| `FileNameFor_TakesTheLastSegment` | `character.ranger`, `character.oathbound` / → `Portrait_Ranger`, `Portrait_Oathbound` (rule 6) |
| `Portraits_OnePerDefinitionWithABody` | the project / the folder holds `Portrait_<Class>.png` for exactly the definitions with a body (rules 1, 6) |
| `Portraits_ImportAsAUiSprite` | each PNG / its importer is rule 7's, and it is `Side` × `Side` |
| `Portraits_AreTransparentAtTheCorners` | each PNG / the four corner pixels have alpha 0, and the centre column has opaque pixels (rule 5) |
| `Definitions_PointAtTheirPortrait` | each definition with a body / `_portrait` is its baked sprite (rule 8) |
| `Assign_LeavesAPaintedPortraitAlone` | a throwaway definition whose `_portrait` is `Icon_Placeholder` / the assignment step / the field is unchanged (rule 8) |
| `Assign_SetsAnEmptyField` | a throwaway definition with no portrait / the assignment step / the field holds the given sprite (rule 8) |
| `CharacterLook_CarriesThePortrait` (in `CharacterLookTests`) | rule 9, set and default |

The PNG rows read the committed files; they do not bake in the suite. **A row that renders is not in the suite**: the bake's render is checked by the manual step, because an EditMode render in the open Editor reads Direct3D12's quirks ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)), which is what `CharacterPreviewTests.Resize_…` already fails on. The assignment rows call `AssignPortrait` directly.

## Manual verification

1. **[Editor]** `Soulvail/Heroes/Bake Portraits` in Edit mode: four PNGs appear under `Art/UI/Heroes/`, the Console names each one, and no error. Open `Portrait_Ranger.png`: head and shoulders, the face not cut by the top edge, the hood or hair inside the frame, the bow's tip allowed to leave it; the background checkered (transparent).
2. **[Editor]** Run it twice: the second run writes the same pictures and changes no `.asset` (`git status` shows only the PNGs if anything, and their bytes match).
3. **[Editor]** The open scene is unchanged after the bake: no `CharacterStage` left in the Hierarchy, nothing dirty.

## Out of scope

- **Showing the portrait anywhere** — UI-12b.
- **A painted portrait**, or a per-class framing tweak: one framing for every body. If one class reads wrong, the fix is a painted portrait in the field (rule 8), not a parameter.
- **Different bodies for the three Knight classes**: their three avatars are the same Knight until they have bodies of their own.
- **Baking at build time** or on import: the menu item is the one way, and its output is committed.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`). Deviations from the above with their reasons, or "as specified"._
