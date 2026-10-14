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

As specified, with four differences.

1. **`Show` disables every `MonoBehaviour` on the body.** The Ranger's body (`Prefabs/Player/Bodies/Ranger.prefab`) carries `RangerAnimatorView`, whose `Start` throws with no `PlayerView` above it and no injection. Rule 1 expected none; switching them off right after `Instantiate` keeps them from `Start` and `Update`. The `Animator` is unaffected.
2. **A look with no body hides the stage.** `CharacterLook.Body` null means "the run's default body", which the preview does not have.
3. **A `_debugBody` field and a "Show debug body" context menu** (the Ranger's body, assigned in the prefab) serve the manual step. Not in the Files table's purpose line.
4. **`Destroy` in play, `DestroyImmediate` in EditMode**, and the body is detached first so the anchor never holds two. `Destroy_FreesTheTexture` invokes `OnDestroy` by hand: it does not run on an EditMode `DestroyImmediate`.

The pedestal is a flattened cylinder (M_BoneGrey) standing in for the sprite. The prefab and scene wiring were made by a throwaway batch-mode editor script, deleted afterwards. `MenuScope`'s `_preview` is optional, unlike its two presenters, until UI-05c needs it.

Not done: the stage has not been seen in the Editor, so the camera's framing (28° field, 3.4 m back, 1 m up) is a guess for a 1.8 m body.
