# RS-01c — The Ranger in Unity

**Size:** S · **Depends on:** RS-01b, and the class's name from the owner · **Branch:** `rs-01c-the-ranger-in-unity`
**Design refs:** GD §16.4, §17.1; AR §18 (the M2-art rows); the parking lot's `handslot` line · **Ledger rows:** none

## Goal

The new Ranger is a prefab in the project. It plays every `Rig_Medium` clip, holds its bow in a
handslot, and stands in `CharacterShowcase.unity` beside the KayKit Ranger it was drawn over.

## The name, asked before this task

Files are named for the class, as `Oathbound.asset`, `Gravecaller.asset` and `Emberwright.asset` are.
The class's name is the owner's. **`Ranger` cannot be the file name**: KayKit's `Ranger.prefab`
already sits in `Prefabs/Characters/`. Below, `<Name>` stands for the answer.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/CharacterModelTests.cs` | Tests.Game | **New.** Every rule below |
| *assets* | — | `Models/Characters/<Name>/<Name>.fbx`, `Bow.fbx`, `Quiver.fbx` and `Arrow.fbx` — **new**, from RS-01b's `Export/`, imported by rule 1; `Textures/Characters/T_<Name>_Albedo.png` — **new**; `Materials/Characters/M_<Name>.mat` — **new**, on the shader `M_Ranger.mat` uses; `Prefabs/Characters/<Name>.prefab` — **new**, rule 2. `Models/` and `Textures/` are new folders |
| *small edits* | Scenes | `Scenes/CharacterShowcase.unity` — the prefab beside the KayKit Ranger (rule 4) |

The `unity` CLI drives the Editor for every step: `import_asset`, `set_import_settings`,
`create_prefab`, `set_parent`, `instantiate_prefab`, `capture_scene_view`, and `run_tests` for the
suite (CLAUDE.md › Unity).

## Public API

_None._ No code outside the test file, and no core type changes.

## Behaviour

1. **The import settings are KayKit's, copied rather than chosen.** They are read from
   `ThirdParty/KayKit/Adventurers/Characters/Ranger.fbx.meta` and written onto `<Name>.fbx`:
   - **Rig:** Generic (`animationType: 2`), with an avatar created from this model.
   - **Optimize Game Objects off,** so the handslots survive (M2-art).
   - **No animation import,** because the clips are KayKit's.
   - **No material import,** because the prefab assigns `M_<Name>`.
   - **Scale:** `globalScale: 1` and `useFileScale: 1`.

   The props take the same settings with no rig. **Skin weights:** if the project's quality setting
   is below 4 bones, that is said in *As built* rather than changed, since `ProjectSettings/` is ask-first.
2. **The prefab is built like KayKit's `Ranger.prefab`, plus a bow.**
   - **The body:** the model root, and an `Animator` with the model's avatar, no controller and root
     motion off (AR §18). `M_<Name>` is on every renderer.
   - **The bow.** Parented to the handslot the bow clips hold still in front of the body, at identity.
     That is expected to be `handslot.l`, because `Ranged_Bow_Draw` moves `handslot.r` as the drawing
     hand (RS-00's measurement). The builder confirms it with a capture and names it in *As built*.
   - **The quiver.** Parented to `chest` if it is a separate prop, and absent if the body carries it.
   - **The arrow is not in the prefab.** Drawing one is RS-02's.
   - **This is not a weapon decision.** Parenting a prop in a prefab decides nothing about who owns a
     weapon. The parking lot's `handslot` line and that question stay open.
3. **It plays every clip, because nothing in the clips was touched.** The clips are KayKit's
   `Rig_Medium` set, unchanged. So AR §18's two M2-art rows hold as they do for the Knight: no
   Animation Events, and no root motion.
4. **It stands beside the KayKit Ranger in `CharacterShowcase.unity`.**
   - **One controller.** Both play `AC_Ranger_Showcase`, which cycles all 126 of its clips, so the
     two are compared in the same pose at the same moment.
   - **Out of the build.** The scene stays out of `EditorBuildSettings`, as it is today.
5. **The player does not wear it.** `Player.prefab` keeps the Knight for every class, because no
   `CharacterSpec` names a body. A body per class is RS-02's.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Model_EveryRigMediumClipBinds` | every clip in the eight `Rig_Medium_*.fbx` / `AnimationUtility.GetCurveBindings` / every binding's path resolves under the prefab's model root — rule 3 |
| `Model_IsGenericAndKeepsItsHandslots` | `<Name>.fbx`'s importer / — / Generic, `optimizeGameObjects` false, no animation or material import; `handslot.l` and `handslot.r` found in the prefab — rule 1 |
| `Model_ImportsAsTheKayKitRangerDoes` | both importers / — / every rule 1 setting equal — rule 1 |
| `Model_KeepsItsBudget` | the prefab's skinned meshes, and each prop / — / ≤ 8 900 triangles; the props within [RS-01b](RS-01b-the-mesh-on-rig-medium.md) rule 8's — RS-01b rules 4, 8 |
| `Model_StandsAsTallAsTheKayKitRanger` | both prefabs' renderer bounds / — / heights within 3 % — RS-01b rule 2 |
| `Prefab_HoldsItsBowInItsHandslot` | the prefab / — / a bow under the handslot rule 2 names, at identity — rule 2 |
| `Prefab_HasNoControllerAndNoRootMotion` | its `Animator` / — / `runtimeAnimatorController` null, `applyRootMotion` false — rule 2, AR §18 |
| `Prefab_EveryRendererWearsItsMaterial` | its renderers / — / each has `M_<Name>` alone — rule 2 |
| `Player_StillWearsTheKnight` | `Player.prefab` / — / its body is `Knight.fbx`'s — rule 5 |

## Manual verification (Editor / device)

1. **[Editor]** Open `CharacterShowcase.unity` and press Play. *Expected: both Rangers cycle the same
   clips together. The new one's elbows, knees and shoulders hold as they did on the pose sheet, and
   the bow stays in the hand through every `Ranged_Bow_*` clip.*
2. **[Editor] probe.** `capture_scene_view` from the Run camera's angle and height, with the new
   Ranger at the player's in-game scale (the Knight's `Body` is at 0.6615) beside the Knight. *Expected:
   its silhouette reads at that size, and cyan is its accent.*
3. **[device]** Whether it reads on a 6-inch screen at 400 dpi. Deferred with every device row.

## Out of scope

- **Wearing it as the player.** RS-02: a body per class needs a field on `CharacterSpec`, and the
  player view has to swap bodies.
- **Its own animator.** RS-02: bow locomotion, draw, release, and the arrow in the hand.
- **Its kit, and whether it joins class select.** RS-03, where the owner is asked both.
- **Replacing KayKit's Ranger.** It stays as the reference and the fallback.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
