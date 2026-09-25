# RS-01a — A template Gemini can draw over, and the prompts

**Size:** S · **Depends on:** RS-00 · **Branch:** `rs-01a-a-template-gemini-can-draw-over`
**Design refs:** GD §16.4, §17.1, §19; the owner's art ruling of 2026-09-25 (GD §21.4) · **Ledger rows:** none

## Goal

The KayKit Ranger's rest pose is rendered front, side and back at one scale. The prompts that turn
those renders into a Gemini character sheet, and the sheet into a mesh, are written down for the
owner to run.

## Why a template, found while planning

- **The rig will not move to meet the mesh.** KayKit's clips key the *positions* of `hips`,
  `upperarm.l/r`, `upperleg.l/r` and `handslot.r`, not only their rotations (measured on
  `Rig_Medium_CombatRanged` at RS-00). A body whose shoulders or hips sit anywhere but KayKit's has
  them snapped back by every clip. So the new body has to be built to KayKit's proportions.
- **Gemini takes an image as well as words.** Drawn over the Ranger's own silhouette, the sheet starts
  in the right proportions instead of being corrected into them afterwards.
- **The Ranger is the rest pose to copy.** It is a T-pose: 2.27 units tall, 1.94 units from fingertip
  to fingertip, feet on 0, 8 900 triangles over eight parts on `Rig_Medium`'s 23 bones.

## Files

| Path (from the repo root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/render_template.py` | Blender 4.1 | **New.** Renders any KayKit character FBX in its rest pose: three orthographic views, and the same three with the joints marked (rules 1–3) |
| `Tools/Blender/README.md` | Docs | **New.** Where Blender is, how a script runs headless, and what each script takes and writes |
| `ArtSource/Characters/Ranger/README.md` | Docs | **New.** The two prompts, the image-to-3D requirements and the handback list (rules 4–6) |
| *outputs* | LFS | `ArtSource/Characters/Ranger/Template/ranger_{front,side,back}.png` and `ranger_{front,side,back}_joints.png`, written by the script and committed |

Nothing under `Assets/` changes, so Unity never sees this task. `.png` is already an LFS pattern.

## Public API

```text
blender --background --factory-startup --python Tools/Blender/render_template.py -- <character.fbx> <out-dir> [--size 1024]

Blender: C:/Program Files/Blender Foundation/Blender 4.1/blender.exe — the version every script in
Tools/Blender/ is written against. Exits 0 with six PNGs written, or non-zero naming what it refused.
```

## Behaviour

1. **The template is the character in its rest pose, as imported.** No clip is applied and nothing is
   posed, because the rest pose is what `Rig_Medium` is bound in. The script reads the FBX's own
   armature, and refuses a file with no armature or with bones other than `Rig_Medium`'s 23, naming
   what it found.
2. **Three orthographic views at one scale.**
   - **The views.** Front, the character's left side, and back. *Front* is the side the face is on;
     the builder reads it off the first render and fixes it in the script.
   - **One scale.** The orthographic scale is the same in all three, so a pixel is the same size in
     each and the three views can be laid side by side.
   - **Framing.** 1024 × 1024, feet 5 % above the bottom edge, and the character centred on the
     frame's vertical axis.
   - **Look.** Workbench flat shading in the texture's colours, no shadows, no outline, a plain white
     background. It shows the silhouette and the flat colour, the two things Gemini should keep.
3. **A joints variant for us, never for Gemini.** The same three views carry a dot at the head of every
   deforming bone: all 23 bar `root`, `handslot.l` and `handslot.r`, which is 20. RS-01b checks the new
   body against it. A marked image given to Gemini would teach it to draw dots.
4. **Two prompts, each sent with the template.** The README keeps both verbatim, so a re-roll is a
   paste.
   - **The character sheet.** A new ranger drawn over the reference, keeping its exact pose,
     proportions, head size and height. Front, side and back side by side, orthographic, at one scale,
     on a plain background.
     - **Style.** KayKit's: chunky, a large head, short limbs, a low-poly look, flat colours, no
       texture noise, no outlines, no painted shading.
     - **Palette (GD §16.4).** The player's cyan `#22D3EE` is the soul-fire accent: trim, fletching, a
       glow at the eyes or clasp. Around it, forest greens, leather browns and bone-white. **Never
       saturated red-orange `#FF4A1F`, violet `#A855F7` or warm gold `#FBBF24`**: each already means
       danger, Veilrot or reward.
     - **Hands open and empty.** Weapons are separate props, per the art ruling.
     - **Nothing far from the body.** A short cape or none, a hood down or close, no long loose straps.
       Cloth that hangs away from the body tears when it is skinned to 23 bones. The quiver sits close
       on the back.
   - **The props sheet.** A longbow with its string, a quiver and an arrow. Each is alone on a plain
     background, seen front and side, in the same style and palette, and sized against the reference
     as KayKit's own `bow_withString.fbx` is.
   - **When to stop re-rolling.** When the three views agree with each other, and the front view laid
     over `ranger_front.png` at 50 % opacity puts the shoulders, elbows, hips and knees where the
     template's are.
5. **Image-to-3D is set by requirements, not by a tool.** The owner picks the tool. What it has to
   deliver:
   - **Input:** the front view, or all three views if it accepts several.
   - **Pose:** the T-pose kept as drawn.
   - **Output:** GLB or FBX with one base-colour texture of at most 2048².
   - **No rig and no auto-rig.** A tool's own skeleton is the Mixamo problem the ruling refuses.
   - **Any triangle count.** RS-01b rebuilds the mesh either way.
   - **Each prop on its own.**

   Unity AI's generators were asked at RS-00 and offer no model on this account, so this step happens
   outside the Editor.
6. **The handback is a list of files in one folder.** The owner places them in
   `ArtSource/Characters/Ranger/Generated/`: `sheet.png` (the sheet chosen), `ranger.glb` (or
   `.fbx`), `bow.glb`, `quiver.glb` and `arrow.glb`. RS-01b starts from that folder and nothing else.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

The script checks its own output and exits non-zero on any failure. Each row is a run the builder
makes and records.

| Run | Given / When / Then |
|---|---|
| `Template_TheRanger` | `Ranger.fbx` / rendered / six 1024² PNGs; the character's pixel height is equal in the three plain views within 2 px; 20 dots on each joints view — rules 1–3 |
| `Template_AnyKayKitCharacter` | `Knight.fbx` / rendered / the same six — the Oathbound pilot will reuse the script — rule 1 |
| `Template_RefusesAFileWithNoRig` | a KayKit prop FBX / run / non-zero, naming the file and *"no armature"* — rule 1 |
| `Template_RefusesAMissingFile` | a path that does not exist / run / non-zero, naming it — rule 1 |

## Manual verification (Editor / device)

1. **[owner]** Open the six renders. *Expected: a flat-coloured Ranger in a T-pose, the feet on one line
   in all three plain views, and the dots on the joints in the joints views.*
2. **[owner]** Send the character prompt to Gemini with `ranger_front.png`, `ranger_side.png` and
   `ranger_back.png` attached, and re-roll until rule 4's stopping check passes. Then send the props
   prompt.
3. **[owner]** Run image-to-3D on the chosen sheet and on each prop, and place the files as rule 6
   lists. RS-01b starts when the folder is complete.

## Out of scope

- **The mesh.** [RS-01b](RS-01b-the-mesh-on-rig-medium.md) rebuilds, skins and exports it.
- **Anything in Unity.** [RS-01c](RS-01c-the-ranger-in-unity.md).
- **The class's name.** Asked before RS-01c, which names files by it.
- **Animation, skills, the showcase scene.** RS-02 to RS-04, titles in the
  [ROADMAP](../ROADMAP.md#rs--the-ranger-a-side-track-the-model-first).

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
