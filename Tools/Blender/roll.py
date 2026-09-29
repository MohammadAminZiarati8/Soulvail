"""The roll: four real rolls, forward, backward, left and right, authored on KayKit's Rig_Medium (RS-06d).

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/roll.py [-- --pose-sheet <out.png>]

It writes, overwriting what it wrote last time, so the file is an output of this script:

- Assets/_Project/Animation/Clips/A_Roll.fbx   Rig_Medium's armature, no mesh, and four takes

**Why authored.** KayKit has no roll. Its Dodge_* clips, which AC_Ranger's Dodge blend played until
RS-06d, lean and hop about a quarter of a metre, and no clip among the 139 turns a body over. These
are on Rig_Medium, read from KayKit's own Ranger.fbx and never rebuilt, so every body on that rig can
play them, bone for bone, as it plays KayKit's (ThirdParty/KayKit/VERSIONS.md).

**How a frame is made.** Each starts from KayKit's own stance, the first frame of Dodge_Forward. The
body folds into a tuck by an amount that rises and falls over the clip. The whole tuck turns about
the hips by an angle that runs from 0 to 360 degrees. Then the hips are raised or lowered until
the body's lowest vertex, measured on the Ranger's skinned meshes, touches the ground. So a roll
rolls on the floor, never through it, whatever the tuck's shape.

**In place.** The root bone never moves, and ChargeMotion carries the body, as AR 18's M2-art rows
and RS-03d's rule 4 hold. 12 frames at 30 fps, 0.4 s, which is each Dodge_* clip's length, so
AC_Ranger's transitions and the roll's 0.35 s ChargeEnded are unchanged.

Blender's frame: Z up, the body facing -Y, its left at +X.
"""

import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
KAYKIT = os.path.join(ROOT, "Assets", "ThirdParty", "KayKit")
BODY = os.path.join(KAYKIT, "Adventurers", "Characters", "Ranger.fbx")
BODY_TEXTURE = os.path.join(KAYKIT, "Adventurers", "Textures", "ranger_texture.png")
STANCE = os.path.join(KAYKIT, "Animations", "Rig_Medium", "Rig_Medium_MovementAdvanced.fbx")
OUT = os.path.join(ROOT, "Assets", "_Project", "Animation", "Clips", "A_Roll.fbx")

FPS = 30
LAST_FRAME = 12  # 0.4 s, a Dodge_* clip's length
GROUND_TOLERANCE = 0.001  # metres; the script's own check that each frame was grounded

ACROSS = Vector((1, 0, 0))  # the body's left: a forward roll turns about it
ALONG = Vector((0, 1, 0))  # the body's back: a side roll turns about it

# Each roll: the axis it turns about, and the sense. A positive turn about ACROSS tips the head
# toward -Y, the way the body faces; a positive turn about ALONG tips it toward +X, its left.
ROLLS = {
    "Roll_Forward": (ACROSS, 1.0),
    "Roll_Backward": (ACROSS, -1.0),
    "Roll_Left": (ALONG, 1.0),
    "Roll_Right": (ALONG, -1.0),
}

# Per frame, 0 to 12: how far into the tuck, and how far over, in degrees. The tuck is nearly whole by
# frame 2 and is left from frame 10. The turn eases in and out and never steps more than 55 degrees a
# frame, so a quaternion key never takes the short way round.
TUCK = (0.0, 0.55, 0.9, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.8, 0.5, 0.25)
TURN = (0.0, 0.0, 20.0, 60.0, 112.0, 167.0, 222.0, 272.0, 315.0, 345.0, 360.0, 360.0, 360.0)

# The tuck, bone by bone, parents first: a turn in degrees about ACROSS at full tuck. A positive turn
# bends a bone that points up forward, and a bone that points down backward.
TUCK_POSE = (
    ("spine", 25.0),
    ("chest", 30.0),
    ("head", 25.0),
    ("upperleg.l", -115.0),
    ("upperleg.r", -115.0),
    ("lowerleg.l", 130.0),
    ("lowerleg.r", 130.0),
    ("foot.l", 30.0),
    ("foot.r", 30.0),
    ("upperarm.l", -55.0),
    ("upperarm.r", -55.0),
    ("lowerarm.l", 40.0),
    ("lowerarm.r", 40.0),
)


# ---------------------------------------------------------------------------------------------
# The body: KayKit's Ranger, its armature and skinned meshes, in its stance.
# ---------------------------------------------------------------------------------------------


def load_body():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    bpy.ops.import_scene.fbx(filepath=BODY)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    assert arm.name == "Rig_Medium" and len(arm.data.bones) == 23, (arm.name, len(arm.data.bones))
    # Blender imports hips connected to root's tail, and a connected bone ignores its location, so
    # neither the turn about the tuck's middle nor the grounding could move it. Unity knows no such
    # thing, and KayKit's own clips key the hips' position. Disconnecting moves no bone.
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    arm.data.edit_bones["hips"].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    return arm, meshes


def stance(arm):
    """Each bone's basis on the first frame of KayKit's Dodge_Forward, which is where a roll starts."""
    placed = arm.matrix_world.copy()
    before_objects, before_actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=STANCE)
    for obj in set(bpy.data.objects) - before_objects:
        bpy.data.objects.remove(obj)
    imported = [a for a in bpy.data.actions if a not in before_actions]
    dodge = next(a for a in imported if a.name.endswith("Dodge_Forward"))
    arm.animation_data_create()
    arm.animation_data.action = dodge
    # Stepped off and back: a new scene already stands on frame 1, and setting the frame it is on
    # evaluates nothing, which read the rest pose as the stance.
    first = int(dodge.frame_range[0])
    bpy.context.scene.frame_set(first + 1)
    bpy.context.scene.frame_set(first)
    basis = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    hand, shoulder = arm.pose.bones["hand.l"].head, arm.pose.bones["upperarm.l"].head
    assert (arm.matrix_world @ shoulder).z - (arm.matrix_world @ hand).z > 0.2, "the stance's arms are not down"
    # The stance's root carries no translation on its first frame; a roll's never does.
    basis["root"] = Matrix.Identity(4)
    arm.animation_data.action = None
    for action in imported:
        bpy.data.actions.remove(action)
    arm.matrix_world = placed  # the take keys the armature object too
    return basis


# ---------------------------------------------------------------------------------------------
# Posing, in the world's frame. Each turn sets one pose bone's armature-space matrix, and the view
# layer is updated after it, so a child is turned from where its parent now is.
# ---------------------------------------------------------------------------------------------


class Poser:
    def __init__(self, arm, meshes, basis):
        self.arm = arm
        self.meshes = meshes
        self.basis = basis
        self.to_arm = arm.matrix_world.inverted()

    def reset(self):
        for pb in self.arm.pose.bones:
            pb.matrix_basis = self.basis[pb.name].copy()
        bpy.context.view_layer.update()

    def turn(self, name, axis, degrees, pivot=None):
        """Turns a bone, and all it carries, about a world axis through its head or through `pivot`."""
        pb = self.arm.pose.bones[name]
        m = pb.matrix.copy()
        centre = m.to_translation() if pivot is None else self.to_arm @ pivot
        q = Quaternion((self.to_arm.to_3x3() @ axis).normalized(), math.radians(degrees))
        pb.matrix = Matrix.Translation(centre) @ q.to_matrix().to_4x4() @ Matrix.Translation(-centre) @ m
        bpy.context.view_layer.update()

    def shift(self, name, offset):
        pb = self.arm.pose.bones[name]
        pb.matrix = Matrix.Translation(self.to_arm.to_3x3() @ offset) @ pb.matrix
        bpy.context.view_layer.update()

    def points(self):
        """Every vertex of the skinned body, where the pose puts it, in the world."""
        graph = bpy.context.evaluated_depsgraph_get()
        out = []
        for obj in self.meshes:
            evaluated = obj.evaluated_get(graph)
            mesh = evaluated.to_mesh()
            co = np.empty(len(mesh.vertices) * 3, np.float64)
            mesh.vertices.foreach_get("co", co)
            world = np.array(evaluated.matrix_world)
            out.append(co.reshape(-1, 3) @ world[:3, :3].T + world[:3, 3])
            evaluated.to_mesh_clear()
        return np.concatenate(out)

    def pose(self, tuck, axis, degrees):
        self.reset()
        for name, full in TUCK_POSE:
            self.turn(name, ACROSS, full * tuck)
        # The turn is about the hips, not the tuck's middle. KayKit's head is a third of the body, so
        # the middle sits well forward of the hips, and a half turn about it carried them 1.2 m off
        # the root: a body out of its capsule (RS-03d rule 4). About the hips, they stay over the
        # root, and the grounding below keeps the ball on the floor.
        self.turn("hips", axis, degrees)
        self.shift("hips", Vector((0, 0, -self.points()[:, 2].min())))
        return self.points()[:, 2].min()


def author(arm, poser):
    made = []
    arm.animation_data_create()
    for name, (axis, sense) in ROLLS.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        last = {}
        for frame in range(LAST_FRAME + 1):
            lowest = poser.pose(TUCK[frame], axis, sense * TURN[frame])
            assert abs(lowest) < GROUND_TOLERANCE, f"{name} frame {frame} is {lowest:.4f} m off the ground"
            for pb in arm.pose.bones:
                q = pb.matrix_basis.to_quaternion()
                if pb.name in last and last[pb.name].dot(q) < 0:
                    q.negate()
                last[pb.name] = q
                pb.rotation_mode = "QUATERNION"
                pb.location = pb.matrix_basis.to_translation()
                pb.rotation_quaternion = q
                pb.scale = pb.matrix_basis.to_scale()
                pb.keyframe_insert("location", frame=frame, group=pb.name)
                pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
                pb.keyframe_insert("scale", frame=frame, group=pb.name)
        action.frame_range = (0, LAST_FRAME)
        action.use_frame_range = True
        made.append(action)
        print(f"  {name}: {LAST_FRAME} frames")
    arm.animation_data.action = None
    # At rest, not in the stance: the exporter writes the pose on screen as each bone's static
    # transform, and a reader takes that as the file's rest. KayKit's own clips carry the rest
    # pose there. In the stance, Blender read every key against it and played frame 0 as a T-pose.
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    return made


def export(arm):
    for action in list(bpy.data.actions):
        if action.name not in ROLLS:
            bpy.data.actions.remove(action)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(
        filepath=OUT,
        use_selection=True,
        object_types={"ARMATURE"},
        apply_scale_options="FBX_SCALE_ALL",  # rootling.py says why
        add_leaf_bones=False,
        use_armature_deform_only=False,
        armature_nodetype="NULL",
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
    )


# ---------------------------------------------------------------------------------------------
# The pose sheet: the exported rolls, reloaded onto a fresh Ranger, every frame, over a floor.
# ---------------------------------------------------------------------------------------------

CELL = 200


def pose_sheet(path):
    arm, meshes = load_body()
    image = bpy.data.images.load(BODY_TEXTURE)
    for obj in meshes:
        for slot in obj.material_slots:
            material = slot.material
            material.use_nodes = True
            nodes = material.node_tree.nodes
            tex = nodes.new("ShaderNodeTexImage")
            tex.image = image
            material.node_tree.links.new(tex.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
            nodes.active = tex  # Workbench draws the active image node
    before_objects, before_actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=OUT)
    for obj in set(bpy.data.objects) - before_objects:
        bpy.data.objects.remove(obj)
    actions = {a.name.split("|")[-1]: a for a in bpy.data.actions if a not in before_actions}
    assert sorted(actions) == sorted(ROLLS), sorted(actions)

    bpy.ops.mesh.primitive_plane_add(size=12)
    floor = bpy.context.object
    floor.data.materials.append(bpy.data.materials.new("floor"))
    floor.data.materials[0].diffuse_color = (0x4E / 255, 0x6E / 255, 0x3F / 255, 1.0)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.view_settings.view_transform = "Standard"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = scene.render.resolution_y = CELL
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.8
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def render():
        out = os.path.join(bpy.app.tempdir, "cell.png")
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(out)
        px = np.array(img.pixels[:], np.float32).reshape(CELL, CELL, 4)[..., :3]
        bpy.data.images.remove(img)
        return np.flipud(px)

    target = Vector((0, 0, 0.9))
    rows = []
    arm.animation_data_create()
    for name, (axis, _) in ROLLS.items():
        arm.animation_data.action = actions[name]
        # Seen across the roll: from the side for a forward or backward one, from the front for a side one.
        eye = Vector((6, -0.8, 0.9)) if axis == ACROSS else Vector((0.8, -6, 0.9))
        cam.location = target + eye
        cam.rotation_euler = (-eye).to_track_quat("-Z", "Y").to_euler()
        cells = []
        for frame in range(LAST_FRAME + 1):
            scene.frame_set(frame)
            cells.append(render())
        rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {name}")
    sheet = np.concatenate(rows, axis=0)
    h, w, _ = sheet.shape
    out = bpy.data.images.new("sheet", w, h, alpha=False)
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., :3] = np.clip(sheet, 0.0, 1.0)
    out.pixels.foreach_set(np.flipud(rgba).ravel())
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("roll.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
                 "FBX's object ids (rootling.py's docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    arm, meshes = load_body()
    basis = stance(arm)
    print("rolls")
    author(arm, Poser(arm, meshes, basis))
    export(arm)
    if "--pose-sheet" in args:
        pose_sheet(os.path.abspath(args[args.index("--pose-sheet") + 1]))  # Blender would not read it from here


main()
