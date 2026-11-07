# BM-03b — The Hound's model on `Rig_Panther`

**Size:** M · **Depends on:** the owner's TRELLIS sculpt (prompt below) · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.2; GD §16.4, §17.1; art source ruling; JG-30d (`Tools/Blender/apex.py`)

## Goal

The player's hound, built the way the Apex was: a TRELLIS sculpt rigged on `Rig_Panther`, painted
so it can never be read as an enemy panther.

## Owner step: the sculpt

Make the picture in Gemini, run it through TRELLIS, and save it as
`Tools/Blender/Sources/Hound_trellis.glb`. Prompt:

```
A big loyal jungle hunting hound, full body, side three-quarter view, standing tall on four long
legs, head held high, tall pricked ears, tail curled up over the back, plain white background.
Stylised low-poly game character in the KayKit style: smooth simple shapes, no fur strands.
Sandy bone-coloured coat with darker brown brindle stripes. A thick leather collar and a leather
harness with rounded bone plates on the shoulders and back. Mouth closed. No saddle, no rider,
no weapons, no red, no gold.
```

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/hound.py` (repo root) | — | **New.** `apex.py`'s pipeline: remesh, distance skinning onto `Rig_Panther`, IK-baked clips |
| `Art/Pets/Hound.fbx` | — | **New.** One skinned mesh on `Rig_Panther`, its clips |
| `Art/Pets/Textures/T_Hound_Albedo.png` | — | **New.** Its own atlas: bone coat, brindle, cyan collar and plates |
| `Materials/Pets/M_Hound.mat` | — | **New** |
| `Prefabs/Pets/Hound.prefab` | — | **New.** Root with a `Model` child, scale 1 |
| `Tests/Game/Art/HoundModelTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Body:** one `SkinnedMeshRenderer` under `Model`; a `Head` bone child for later attachments
   (Apex Bond's fang).
2. **Rig:** `Rig_Panther`'s bone names, refit to the hound's longer legs and raised head.
3. **Colour:** collar and plates are `Palette.Player` cyan (at least 3 % of texels in the cyan
   band); none within ΔE 20 of `Palette.Danger`, none in the gold or enemy lime band; the coat's
   mean luma is above the Panther's, so it stands lighter than the charcoal cats.
4. **Silhouette:** at rest its height-to-length ratio is at least 1.3× the Panther's (tall, not low).
5. **Clips**, named exactly: `Idle`, `Trot`, `Run`, `Bite` (0.3 s), `Pounce` (airborne), `Land`,
   `Hit`, `Fall` (ends lying, holdable), `GetUp`, `Howl`. Stationary root; no events.
6. **Budget:** within the crowd budget `MossguardModelTests` uses; every vertex weighted.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hound_OneModelChild_OneSkinnedBody` (1) |
| `Hound_RigHasPantherBones_AndHead` (2) |
| `Paint_HasPlayerCyan_NoDangerGoldLime_LighterThanPanther` (3) |
| `Silhouette_TallerThanPanther` (4) |
| `Hound_HasTheTenClips_NoEvents_NoRootMotion` (5) |
| `Hound_StaysInBudget_EveryVertexWeighted` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Beside a Panther and a Shadow panther on Jungle ground: at a glance the hound is
   the player's (upright, pale, cyan collar), the cats are not.

## Out of scope

- Animator, view, bar (BM-03c).

## As built

_Filled at merge._
