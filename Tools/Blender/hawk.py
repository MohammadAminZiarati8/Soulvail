"""Build the hawk from its TRELLIS sculpt with Blender 4.1 and PYTHONHASHSEED=0."""
import math
import os
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Quaternion

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import apex
import panther as cat

SCALE = 1.6
OUT = HERE.parents[1] / "Assets" / "_Project" / "Art" / "Pets"
SWATCHES = [
    (84, 60, 52), (117, 85, 70), (145, 111, 93), (178, 158, 142),
    (219, 204, 186), (55, 45, 44), (34, 211, 238),
]


def point(x, y, z):
    return cat.v(x, y, z) * SCALE


def skeleton():
    bones = {
        "root": (None, point(0, 0, 0), point(0, 0, .08)),
        "body": ("root", point(0, .05, -.03), point(0, -.13, .035)),
        "neck": ("body", point(0, -.13, .035), point(.015, -.20, .10)),
        "head": ("neck", point(.015, -.20, .10), point(.025, -.30, .115)),
        "tail": ("body", point(0, .08, -.04), point(-.055, .31, -.095)),
    }
    for side, sign in (("l", 1), ("r", -1)):
        shoulder = point(sign * .058, -.145, .025)
        wrist = point(sign * .255, -.188, .103)
        bones[f"wing.{side}.1"] = ("body", shoulder, wrist)
        bones[f"wing.{side}.2"] = (f"wing.{side}.1", wrist, point(sign * .48, -.245, .205))
        bones[f"leg.{side}"] = ("body", point(sign * .045, .005, -.085), point(sign * .051, .055, -.212))
    return bones


def skin(body, bones):
    groups = {name: body.vertex_groups.new(name=name) for name in bones if name != "root"}
    for vertex in body.data.vertices:
        x, y, z = vertex.co / SCALE
        side = "l" if x >= 0 else "r"
        if abs(x) > .11 and y < -.065:
            names = [f"wing.{side}.1", f"wing.{side}.2"]
            if abs(x) < .19:
                names.append("body")
        elif y > .135:
            names = ["body", "tail"]
        elif z < -.10 and y < .13:
            names = ["body", f"leg.{side}"]
        else:
            names = ["body", "neck", "head", "tail", f"wing.{side}.1", f"leg.{side}"]
        nearest = sorted((cat.segment_distance(vertex.co, *bones[name][1:]), name) for name in names)[:3]
        weights = [(name, 1 / (distance + .006) ** 3) for distance, name in nearest]
        total = sum(weight for _, weight in weights)
        for name, weight in weights:
            groups[name].add([vertex.index], weight / total, "REPLACE")


def paint(high, body):
    colours = apex.source_colours(high, body)
    uv = body.data.uv_layers.new(name="UVMap").data
    counts = [0] * 8
    for face in body.data.polygons:
        x, y, z = face.center / SCALE
        light = float(cat.luma(colours[face.index]))
        tip = abs(x) > .445
        band = abs(x) < .11 and -.055 < y < .11 and -.177 < z < -.133
        if tip or band:
            swatch = 6
        elif y > .145:
            swatch = 7
        elif light > .62:
            swatch = 4
        elif light > .43:
            swatch = 3
        elif light > .26:
            swatch = 2
        elif light > .15:
            swatch = 1
        else:
            swatch = 0
        counts[swatch] += 1
        for index in face.loop_indices:
            if swatch == 7:
                tail_y = body.data.vertices[body.data.loops[index].vertex_index].co.y / SCALE
                stripe = max(0, min(1, (tail_y - .13) / .20))
                uv[index].uv = (7.5 / 8, .04 + .92 * stripe)
            else:
                uv[index].uv = ((swatch + .5) / 8, .5)
        face.use_smooth = True
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new("hawk"))
    print("Hawk paint faces:", counts)


def atlas():
    image = bpy.data.images.new("T_Hawk_Albedo", width=256, height=128, alpha=False)
    pixels = np.ones((128, 256, 4), dtype=np.float32)
    for index, colour in enumerate(SWATCHES + [SWATCHES[0]]):
        pixels[:, index * 32:(index + 1) * 32, :3] = np.array(colour) / 255
    for row in range(128):
        stripe = int(max(0, min(1, (row / 128 - .04) / .92)) * 8)
        pixels[row, 224:, :3] = np.array(SWATCHES[5 if stripe % 2 == 0 else 3]) / 255
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = str(OUT / "Textures" / "T_Hawk_Albedo.png")
    image.file_format = "PNG"
    image.save()


def bake(arm):
    # Body pitch, inner wing lift, outer wing lift, and leg reach in degrees.
    neutral = (0, 0, 0, 0)
    clips = {
        "Glide": {0: neutral, 24: (-3, 5, -3, 0), 48: neutral},
        "Flap": {0: (0, 30, 14, 0), 6: (4, -29, -17, 0), 12: (0, 30, 14, 0)},
        "Dive": {0: neutral, 8: (53, -40, -24, -12), 20: (60, -45, -18, -12)},
        "Strike": {0: (42, -27, -16, -12), 6: (-17, 12, 19, 52), 15: neutral},
        "Climb": {0: neutral, 7: (-26, -38, -20, -6), 14: (-34, 33, 16, -6), 22: neutral},
    }
    arm.animation_data_create()
    for name, keys in clips.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        for frame, (pitch, wing, tip, leg) in keys.items():
            for bone in arm.pose.bones:
                bone.rotation_mode = "QUATERNION"
                if bone.name.startswith("wing."):
                    angle = wing if bone.name.endswith(".1") else tip
                    axis = (1, 0, 0)
                else:
                    angle = {"body": pitch, "head": -pitch * .25, "tail": -pitch * .20,
                             "leg.l": leg, "leg.r": leg}.get(bone.name, 0)
                    axis = (1, 0, 0)
                bone.rotation_quaternion = Quaternion(axis, math.radians(angle))
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
        action.frame_range = (0, max(keys))
        action.use_frame_range = True
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.rotation_quaternion = Quaternion()


def main():
    assert os.environ.get("PYTHONHASHSEED") == "0", "Start Blender with PYTHONHASHSEED=0"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cat.SOURCE = str(HERE / "Sources" / "Hawk_trellis.glb")
    cat.VOXEL = .009
    cat.TRIANGLES = 1120
    high = cat.load()
    for vertex in high.data.vertices:
        vertex.co *= SCALE
    high.data.update()
    body = cat.decimate(high)
    body.name = "Hawk_Body"
    assert len(body.data.polygons) < 1250
    print("Hawk triangles:", len(body.data.polygons))
    print("Hawk wingspan:", max(v.co.x for v in body.data.vertices) - min(v.co.x for v in body.data.vertices))
    paint(high, body)
    bpy.data.objects.remove(high, do_unlink=True)
    bones = skeleton()
    skin(body, bones)
    arm = cat.build_armature(bones)
    arm.name = arm.data.name = "Rig_Hawk"
    cat.bind(body, arm)
    bake(arm)
    atlas()
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = 30
    bpy.ops.export_scene.fbx(
        filepath=str(OUT / "Hawk.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        armature_nodetype="NULL", bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        path_mode="STRIP",
    )


if __name__ == "__main__":
    main()
