# RS-06d — A roll that rolls

**Size:** M · **Depends on:** RS-03d (the Dodge blend and its view) · **Branch:** `rs-06d-a-roll-that-rolls`
**Design refs:** CC §3.5; AR §18 (the M2-art rows: Generic, no root motion) · **Ledger rows:** none

The owner's word of 2026-09-29: *"i want a rolling over animation for our characters … for the ranger
the space key will roll over"*, and the go: *"implement the rolling animation and add to all humanoid
characters. or adding to all of them is complicate, then just add to ranger only."* Every class is to
have its own Space move, with the Knight's shield and the Necrophos's ethereal state still to be
designed. So this task gives the roll to the one class whose Space move is a roll.

## Goal

The Ranger's Space move is a real roll, head over heels in the direction it goes, where it was
KayKit's lean-and-hop. The four rolls are authored on `Rig_Medium`, so any body on it can play them.

## Why this shape

- **KayKit has no roll.** Its four `Dodge_*` clips, which RS-03d plays, lean and hop about a quarter
  of a metre. No clip in the 139 turns a body over. So the rolls are authored in Blender, from a
  script, the way the Frog's clips are (M7-05j).
- **Four rolls in the blend RS-03d built.** Core holds the facing through a dash, so a Ranger facing
  its target rolls sideways or backwards as often as forwards. `AC_Ranger`'s `Dodge` blend already
  picks a clip by the roll's direction in the body's frame. Four new clips take the four old places,
  and no code changes.
- **On `Rig_Medium`, so every humanoid can have it.** The Oathbound, the Gravecaller and the
  Emberwright wear the Knight's body on the same rig, and so will the next classes. None of their
  Space moves is a roll: a charge, a blink with a decoy, a teleport. So the Ranger alone plays it
  today, and a later class whose Space move is a roll wires the same clips in its controller.
- **In place, at the dodge's length.** The root never moves, and `ChargeMotion` carries the body,
  as RS-03d's rule 4 holds. Each roll is 12 frames at 30 fps, 0.4 s, as each `Dodge_*` is, so the
  controller's transitions and the 0.35 s `ChargeEnded` need no change.
- **The roll's gameplay numbers stay the Ranger's.** Its 5 m in 0.3 s, the i-frames and the cooldown
  are `Ranger.asset`'s, and the owner's to move. The clip fits them.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/roll.py` | Blender 4.1 | **New.** Authors the four rolls and writes `A_Roll.fbx`; `--pose-sheet <png>` renders the review sheet |
| `Assets/_Project/Animation/Clips/A_Roll.fbx` | generated | `Rig_Medium`'s armature, no mesh, and four takes |
| `Assets/_Project/Tests/Game/Art/RollClipTests.cs` | Tests.Game | **New.** Rules 1–5 |
| *small edits* | | `AC_Ranger`: the `Dodge` blend's four motions. `RangerAnimatorView`: two comments name the rolls |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/roll.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **Four rolls import as Generic clips on `Rig_Medium`.** `Roll_Forward`, `Roll_Backward`,
   `Roll_Left` and `Roll_Right`: 30 fps, 0.4 s, none looping, and every curve binds on KayKit's
   `Ranger.fbx`.
2. **A roll never moves the root, and turns about the hips.** `root` stays at rest on every frame
   of every roll, and the hips stay within 0.3 m of it on XZ, so the body never leaves its capsule.
3. **A roll goes head over heels, the way it is named.** On each roll the head passes below the
   hips, it leads first in the roll's direction, and the body stands upright again by the last
   frame.
4. **A roll stays on the floor.** On every frame the body's lowest point is within 3 cm of the
   ground, so a rolling body neither sinks through the floor nor floats over it.
5. **The Ranger plays them.** `AC_Ranger`'s `Dodge` blend holds the four rolls at the four
   directions its `Dodge_*` held, and nothing else changes in the controller.
6. **One script makes the clips, and a re-run makes the same ones.** The FBX differs in its header's
   timestamp alone.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Roll_ImportsGenericWithFourClips` | the importer and its clips / read / Generic, own avatar, four named clips, none looping, 30 fps, 0.4 s (rule 1) |
| `Roll_EveryCurveBindsOnTheRanger` | each clip / every binding / resolves on `Ranger.fbx` (rule 1) |
| `Roll_LeavesTheRootAlone` | each clip, sampled at every frame on `Ranger.fbx` / `root` / at rest (rule 2) |
| `Roll_KeepsTheHipsOverTheRoot` | each clip, sampled / the hips against the root on XZ / within 0.3 m (rule 2) |
| `Roll_GoesHeadOverHeels` | each clip, sampled / head against hips / below them once, ahead in the roll's direction first, above them at the end (rule 3) |
| `Roll_KeepsTheBodyOnTheFloor` | each clip, sampled, every skinned mesh baked / the lowest vertex / within 0.03 m of 0 (rule 4) |
| `Ranger_DodgeBlendPlaysTheRolls` | `AC_Ranger` / the `Dodge` blend's children / the four rolls at (0, 1), (0, −1), (−1, 0), (1, 0) (rule 5) |
| `RangerSandboxTests.Roll_TheBodyStaysInItsCapsule` (PlayMode, existing) | a roll each way / each frame / the hips within 0.3 m of the `PlayerView` (RS-03d rule 4, unchanged) |

Rule 6 is checked by hand: a re-run and a byte compare.

## Manual verification (Editor / device)

1. **[owner]** Read the pose sheet (`roll.py -- --pose-sheet Temp/Renders/roll_pose_sheet.png`):
   each roll tucks, turns once over and stands, with no limb through the body.
2. **[Editor]** Play the Ranger and press Space moving each way, standing and while shooting.
   *Expected: a roll the way it goes, and no jump back after it.*

## Out of scope

- **The other classes' Space moves.** A charge, a blink and a teleport are not rolls. The Knight's
  shield and the Necrophos's ethereal state wait for those classes.
- **The roll's speed, distance and i-frames**, which are `Ranger.asset`'s numbers.
- **Deleting RS-03d's `A_Dodge_*_InPlace.anim`**, which nothing plays after this. Deleting assets is
  asked first.
- **The Knight's `Charge` playing the imported `Dodge_Forward`**, a parking-lot line.

## As built

**Deviations.** *(1)* **Rule 2 gained the hips, and the Tests table a seventh row.** The first rolls
turned about the middle of the tucked body. KayKit's head is a third of the body, so that middle sits
well forward of the hips, and a half turn carried them 1.2 m off the root at model scale.
`RangerSandboxTests.Roll_TheBodyStaysInItsCapsule` caught it in PlayMode at 0.78 m, and no EditMode
row did. The rolls now turn about the hips, and `Roll_KeepsTheHipsOverTheRoot` pins that without a
PlayMode pass. The ball wobbles about the hips rather than spinning about its own middle, which a
5 m dash hides. *(2)* **`A_Roll.fbx` imports with Preserve Hierarchy on**, where KayKit's clips
leave it off. See finding 4.

**Findings.** *(1)* **Blender imports `hips` connected to `root`'s tail, and a connected bone ignores
its location.** Neither the turn nor the grounding could move it, and the ground check failed on
frame 0. The script disconnects it, which moves no bone. Unity has no such rule, and KayKit's own
clips key the hips' position. *(2)* **Setting the frame a scene already stands on evaluates nothing.**
The stance was read on frame 1 of a new scene, which is frame 1 already, so the rest pose was
recorded as the stance. The script steps off the frame and back, and asserts the arms are down.
*(3)* **Blender's exporter writes the pose on screen as each bone's static transform**, and Blender's
importer takes that as the file's rest. Exported in the stance, every key read against the stance,
and frame 0 came back as a T-pose. The export is made at rest, as KayKit's files are. Unity reads a
key as the transform itself, so this mattered only to the pose sheet. It would matter to any tool
that reads the file as Blender does. *(4)* **Unity strips a single root node that carries no
transform.** KayKit's `Rig_Medium` node carries one and survives, but the exported one is identity.
Stripped, every curve bound to `root/…` rather than `Rig_Medium/root/…`, and nothing moved on the
Ranger. `Roll_EveryCurveBindsOnTheRanger` went red, and Preserve Hierarchy fixed it.

**Numbers.** Four clips of 13 keys, 0.4 s at 30 fps. The tuck folds in by frame 2 and opens from
frame 10. The turn runs 0 to 360° from frame 1 to frame 10, never more than 55° a frame. Every frame
is grounded to 1 mm in Blender, and to 3 cm on the imported clip. The FBX is 612 KB, through LFS.

**Verified.** `RollClipTests` 7 / 7. EditMode: 3,491 (3,484 plus these seven), of which 3,490
passed, 0 failed and 1 was inconclusive (the animator clock row, by construction). One earlier pass
failed `RangerTests.Ranger_WearsItsBodyAndFliesArrows` with "same as \<Ranger\> but was \<Ranger\>"
after the controller was saved, and a script reload cleared it ([Traps §5](../../Traps.md)).
PlayMode: 66 / 67, with `Roll_TheBodyStaysInItsCapsule` green. `Loop_ShootsOnlyStandingStill` failed
with "No arrow on the run" on the full pass, and alone after a clean recompile. It failed again alone
with `AC_Ranger` put back to `dev`'s, so it is Traps §8's sandbox failure on an Editor that is not in
front, not this change. A re-run of `roll.py`: the FBX differing in 4 timestamp bytes. Format check
clean on both changed C# files. Console clean. `ProjectSettings/TimeManager.asset`, rewritten by the
Editor, was reverted. `A_Dodge_*_InPlace.anim` stay in the repository, unplayed.
