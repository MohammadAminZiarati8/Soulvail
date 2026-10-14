# UI-05b — The character preview

**Size:** S · **Depends on:** UI-05a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** AR §3, ADR for hexagonal split · **Ledger rows:** none

## Goal

The class's real 3D body stands on a stone pedestal, lit and idling, rendered to a texture a `RawImage` can show; a drag turns it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/CharacterPreview.cs` | Game | owns the stage: shows a body, drives the camera, makes and frees the texture |
| `Prefabs/UI/CharacterStage.prefab` | — | a camera, a key and a rim light, the pedestal sprite's 3D stand-in and a body anchor |
| `Tests/Game/Presentation/CharacterPreviewTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `Menu.unity` (one stage instance, parked far from the origin); `MenuScope` (registers the component) |

## Public API

```csharp
// CharacterPreview (MonoBehaviour):
//   RenderTexture Texture { get; }            // null until Resize
//   bool IsShowing { get; }
//   GameObject Body { get; }                  // the live instance, or null
//   void Resize(int width, int height);       // pixels; capped at 1024 on the long side
//   void Show(CharacterLook look);            // replaces any body already there
//   void Hide();
//   void Rotate(float degrees);               // yaw, about the pedestal's axis
```

## Behaviour

1. **`Show` instantiates `look.Body` on the anchor** and replaces the one already there; at most one body exists. Its colliders, rigidbodies and gameplay components are none of the preview's business: the body is instantiated from the same prefab the run clones, so a Body carrying any `MonoBehaviour` of the run's is a deviation to name in *As built*.
2. **The camera renders only while a body is showing.** `Hide` destroys the body and disables the camera; a hidden stage costs nothing.
3. **The texture is made by `Resize`, capped, and freed in `OnDestroy`.** A second `Resize` to the same size makes nothing; to a new size frees the old one first. Format RGBA, 24-bit depth, 2× MSAA, a transparent clear colour so the page's stone shows around the character.
4. **The body idles.** Its `Animator` plays whatever its default state is, and `Time.timeScale` 0 does not stop it (`AnimatorUpdateMode.UnscaledTime`), so it still moves if the menu is ever opened over a stopped game.
5. **`Rotate` turns the body, not the camera**, so the lights stay put and the model catches them. Yaw wraps to 0–360.
6. **The stage is parked at y = −500** and the camera's culling mask is the default layer, so nothing else in the Menu scene is seen and no new layer or `TagManager` edit is needed. Its lights are added to the scene's own main light, and the Menu scene has no other lit object.
7. **Nothing static, nothing in core.** The preview reads a `CharacterLook` from the book and holds no game state.

## Tests

| Test | Given / When / Then |
|---|---|
| `Show_InstantiatesBodyOnTheAnchor` | rule 1 |
| `Show_Another_ReplacesTheBody` | rule 1: one body, the first destroyed |
| `Hide_DestroysBodyAndDisablesCamera` | rule 2 |
| `Resize_MakesTheTextureCappedAndFreesTheOld` | rule 3 |
| `Destroy_FreesTheTexture` | rule 3 |
| `Rotate_YawsTheBodyAndWraps` | rule 5 |
| `Stage_IsParkedFarFromTheOrigin` | rule 6 |
| `Body_Idles_AnimatorIsUnscaled` | rule 4 |

## Manual verification

1. **[Editor]** Menu scene, Play: nothing visible until UI-05c wires the page. In the Scene view the stage is at y = −500 with the Ranger on it when `Show` is called from the Inspector's context menu.

## Out of scope

- The page that shows the texture: UI-05c. Skill animations on the preview: later, when a skill has one.
- Drag input: the page owns the pointer and calls `Rotate`.

## As built

_Filled at merge._
