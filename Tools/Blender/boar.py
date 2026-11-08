"""Build PL-05b with PYTHONHASHSEED=0 blender -b --python Tools/Blender/boar.py."""
import os
from pathlib import Path
import sys

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.dont_write_bytecode = True
import apex
import panther as cat

OUT = HERE.parents[1] / "Assets" / "_Project" / "Art" / "Pets"
SCALE = 1.45
FLOOR = -.3305
SWATCHES = (
    (48, 31, 27), (70, 45, 35), (91, 60, 46), (33, 26, 24),
    (182, 154, 128), (219, 196, 173), (235, 220, 199), (34, 211, 238),
)


def point(x, y, z):
    return cat.v(x * SCALE, y * SCALE, (z - FLOOR) * SCALE)


def fit(body):
    marks = cat.landmarks(body.data)
    bones = cat.skeleton(marks)
    hips, spine, chest = point(0, .26, -.025), point(0, .04, .015), point(0, -.17, .035)
    neck, head = point(0, -.245, .025), point(0, -.33, -.085)
    tail = [point(0, .35, .005), point(0, .40, .025), point(0, .45, .03), point(0, .49, .03)]
    bones.update({
        "hips": ("root", hips, spine),
        "spine": ("hips", spine, chest),
        "chest": ("spine", chest, neck),
        "neck": ("chest", neck, head),
        "head": ("neck", head, point(0, -.48, -.19)),
        "jaw": ("head", point(0, -.34, -.15), point(0, -.46, -.22)),
        "tail_1": ("hips", tail[0], tail[1]),
        "tail_2": ("tail_1", tail[1], tail[2]),
        "tail_3": ("tail_2", tail[2], tail[3]),
    })
    for leg, paw in marks["paws"].items():
        front = leg.startswith("front")
        x = paw.x
        y = paw.y
        shoulder = cat.v(x * .9, y + (.10 if front else -.12), .32)
        knee = cat.v(x, y + (.055 if front else -.07), .17)
        ankle = cat.v(x, y, .055)
        bones[f"upper.{leg}"] = ("chest" if front else "hips", shoulder, knee)
        bones[f"lower.{leg}"] = (f"upper.{leg}", knee, ankle)
        bones[f"paw.{leg}"] = (f"lower.{leg}", ankle, paw + cat.v(0, -.045, .015))
    marks.update(head=head, tail_root=tail[0])
    return marks, bones


def paint(high, body):
    colours = apex.source_colours(high, body)
    uv = body.data.uv_layers.new(name="UVMap").data
    flanks = [0, 0]
    for face in body.data.polygons:
        x, y, z = face.center / SCALE
        z += FLOOR
        light = float(cat.luma(colours[face.index]))
        stripe = abs(x) > .135 and -.17 < z < .115 and abs(y - (.015 + .55 * z)) < .046
        if stripe:
            swatch = 7
            flanks[int(x > 0)] += 1
        elif y < -.325 and z < -.09 and light > .38:
            swatch = 6
        elif z > .055 and -.36 < y < .27 and light > .37:
            swatch = 5 if light > .54 else 4
        elif light < .12 or (z < -.26 and light < .22):
            swatch = 3
        else:
            swatch = 0 if light < .20 else 1 if light < .30 else 2
        for index in face.loop_indices:
            uv[index].uv = ((swatch + .5) / len(SWATCHES), .5)
        face.use_smooth = True
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new("boar"))
    assert all(flanks), flanks
    print("Cyan faces per flank:", flanks)


def atlas():
    image = bpy.data.images.new("T_Boar_Albedo", width=256, height=128, alpha=False)
    pixels = np.ones((128, 256, 4), dtype=np.float32)
    for index, colour in enumerate(SWATCHES):
        pixels[:, index * 32:(index + 1) * 32, :3] = np.array(colour) / 255
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = str(OUT / "Textures" / "T_Boar_Albedo.png")
    image.file_format = "PNG"
    image.save()
    print(f"Cyan texels: {100 / len(SWATCHES):.2f}%")


def clips():
    gathered = dict(offset=cat.v(0, 0, -.025), neck=(7, 0, 0), head=(2, 0, 0), legs={
        "front.l": (-24, 25, 7, 0), "front.r": (-16, 19, 4, 0),
        "back.l": (31, -31, 5, 0), "back.r": (23, -25, 5, 0),
    })
    extended = dict(offset=cat.v(0, 0, .045), chest=(-4, 0, 0), neck=(8, 0, 0), legs={
        "front.l": (43, -12, -8, 0), "front.r": (34, -10, -6, 0),
        "back.l": (-35, 19, 12, 0), "back.r": (-28, 14, 10, 0),
    })
    planted = {leg: (0, 0, 0, 0) for leg in cat.LEGS}
    still = dict(legs=planted)
    return {
        "Charge": (True, {0: gathered, 6: extended, 12: gathered}),
        "Idle": (True, {0: still, 24: dict(still, chest=(-2, 0, 0), head=(2, 0, 3),
                                           tail_2=(0, 0, 12)), 48: still}),
    }


def export(arm, body):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = cat.FPS
    bpy.ops.export_scene.fbx(
        filepath=str(OUT / "Boar.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype="NULL", bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        path_mode="STRIP",
    )


def main():
    assert os.environ.get("PYTHONHASHSEED") == "0", "Start Blender with PYTHONHASHSEED=0"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cat.SOURCE = str(HERE / "Sources" / "Boar_trellis.glb")
    cat.TRIANGLES = 1200
    cat.VOXEL = .013
    high = cat.load()
    for vertex in high.data.vertices:
        vertex.co = point(*vertex.co)
    high.data.update()
    body = cat.decimate(high)
    body.name = "Boar_Body"
    triangles = len(body.data.polygons)
    assert 0 < triangles < 1250, triangles
    print("Boar triangles:", triangles)
    paint(high, body)
    marks, bones = fit(body)
    allowed = cat.allowed
    try:
        def boar_allowed(name, position, landmarks, rig):
            if name.startswith(("upper.", "lower.", "paw.")) and position.y < -.30 * SCALE:
                return False  # Keep the low snout and tusks out of the foreleg weights.
            if name == "jaw":
                return False  # Tusks and muzzle stay rigid with the skull.
            return allowed(name, position, landmarks, rig)

        cat.allowed = boar_allowed
        cat.weigh(body, bones, marks)
    finally:
        cat.allowed = allowed
    assert all(abs(sum(group.weight for group in vertex.groups) - 1) < 1e-5
               for vertex in body.data.vertices)
    bpy.data.objects.remove(high, do_unlink=True)
    arm = cat.build_armature(bones)
    cat.bind(body, arm)
    base_clips = cat.clips
    try:
        cat.clips = clips
        cat.author_clips(arm, body, bones)
    finally:
        cat.clips = base_clips
    atlas()
    export(arm, body)


if __name__ == "__main__":
    main()
