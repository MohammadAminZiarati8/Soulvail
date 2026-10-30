"""JG-12d: the owner's sculpt, cut, repainted, skinned and animated in Blender 4.1.

PYTHONHASHSEED=0 blender --background --factory-startup --python
Tools/Blender/mother_pitcher.py [-- --pose-sheet <out.png>]

Only MotherPitcher_src.glb is geometry input. The existing atlas is read, never written.
The optional sheet reloads the exported FBX and compares Pitcher and Croaker silhouettes.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
from enemy_atlas import uv_for, save_png  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
SOURCE = os.path.join(HERE, "Sources", "MotherPitcher_src.glb")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Enemies")
TEX = os.path.join(OUT, "Textures")
FPS = 30
TRIANGLES = 2400
VOXEL = 0.009


def v(x, y, z):
    return Vector((x, y, z))


def load():
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    for other in [o for o in bpy.context.scene.objects if o is not obj]:
        bpy.data.objects.remove(other)
    return obj


def decimate(obj):
    """A copy cut to TRIANGLES; the original stays as the colour source."""
    lo = obj.copy()
    lo.data = obj.data.copy()
    lo.name = "MotherPitcher_Body"
    bpy.context.scene.collection.objects.link(lo)
    # The source UV seams are discarded: the decimated copy is repainted.
    while lo.data.uv_layers:
        lo.data.uv_layers.remove(lo.data.uv_layers[0])
    bpy.context.view_layer.objects.active = lo
    # Voxel remesh unifies the sculpt's intersecting sheets before collapse,
    # retaining the cup opening, roots and leaf tips at this resolution.
    lo.data.remesh_voxel_size = VOXEL
    lo.data.remesh_voxel_adaptivity = 0.0
    bpy.ops.object.voxel_remesh()
    for _ in range(4):  # a collapse can stop short of its ratio on the shells' open rims; go again
        tris = sum(len(p.vertices) - 2 for p in lo.data.polygons)
        if tris <= TRIANGLES * 1.02:
            break
        mod = lo.modifiers.new("decimate", "DECIMATE")
        mod.ratio = TRIANGLES / tris
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    loose = [vt for vt in bm.verts if not vt.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(lo.data)
    bm.free()
    return lo


def source_colours(hi, lo):
    """For each face of `lo`, the sculpt's texture colour at the nearest point of `hi`'s surface."""
    image = next(i for i in bpy.data.images if i.size[0] > 0)
    w, h = image.size
    px = np.array(image.pixels[:], np.float32).reshape(h, w, 4)[..., :3]
    bvh = BVHTree.FromObject(hi, bpy.context.evaluated_depsgraph_get())
    uv = hi.data.uv_layers.active.data
    polys = hi.data.polygons
    out = []
    for poly in lo.data.polygons:
        _, _, index, _ = bvh.find_nearest(poly.center)
        src = polys[index]
        # The texture under the nearest triangle's centre: the source faces are much smaller than
        # the decimated ones, so a point is as good as an average and much cheaper.
        u = sum(uv[i].uv.x for i in src.loop_indices) / src.loop_total
        t = sum(uv[i].uv.y for i in src.loop_indices) / src.loop_total
        out.append(px[min(h - 1, max(0, int(t * h))), min(w - 1, max(0, int(u * w)))])
    return np.array(out)


def luma(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def saturation(c):
    hi, lo = c.max(axis=-1), c.min(axis=-1)
    return np.where(hi > 0, (hi - lo) / np.maximum(hi, 1e-9), 0.0)


def shape(obj):
    # Source: Z up, mouth toward -Y. Centre horizontally and stand on the floor.
    co = np.array([tuple(vt.co) for vt in obj.data.vertices])
    centre = v(float((co[:, 0].min() + co[:, 0].max()) / 2),
               float((co[:, 1].min() + co[:, 1].max()) / 2), float(co[:, 2].min()))
    scale = 2.0 / float(co[:, 2].max() - co[:, 2].min())
    for vt in obj.data.vertices:
        vt.co = (vt.co - centre) * scale
    obj.data.update()


def skeleton():
    # Landmarks measured on the source after shape(): lid hinge behind the bowl,
    # four radial leaf fans below it, central stem and the grounded root mat.
    bones = {"root": (None, v(0, 0, 0), v(0, 0, .12)),
             "base": ("root", v(0, 0, .12), v(0, 0, .35)),
             "stem_1": ("base", v(0, 0, .35), v(0, 0, .60)),
             "stem_2": ("stem_1", v(0, 0, .60), v(0, 0, .85)),
             "stem_3": ("stem_2", v(0, 0, .85), v(0, 0, 1.12)),
             "cup": ("stem_3", v(0, 0, 1.12), v(0, 0, 1.65)),
             "lid": ("cup", v(0, .43, 1.66), v(0, -.36, 1.91))}
    for i, (x, y) in enumerate(((1, 0), (0, -1), (-1, 0), (0, 1)), 1):
        bones[f"leaf_{i}"] = ("stem_1", v(x * .12, y * .12, .58), v(x * 1.0, y * .85, .30))
    return bones


def is_lid(co):
    # The lid slopes up toward the rear; its front is separated from the rim by air.
    return co.z > 1.70 + .10 * co.y


def weigh(body, bones):
    groups = {n: body.vertex_groups.new(name=n) for n in bones if n != "root"}
    for vt in body.data.vertices:
        co = vt.co
        radius = math.hypot(co.x, co.y)
        if is_lid(co):
            weights = {"lid": 1.0}
        elif co.z > 1.10:
            weights = {"cup": 1.0}
        elif co.z < .22:
            weights = {"base": 1.0}
        elif radius > .34 and co.z < .90:
            direction = max(range(4), key=lambda i: (co.x, -co.y, -co.x, co.y)[i])
            t = min(1.0, max(0.0, (radius - .25) / .36))
            weights = {f"leaf_{direction + 1}": t, "stem_1": 1 - t}
        else:
            # Linear blend along the central chain; no vertex rides root.
            names = ("base", "stem_1", "stem_2", "stem_3", "cup")
            t = min(3.999, max(0.0, (co.z - .22) / .22))
            i = int(t)
            weights = {names[i]: 1 - (t - i), names[i + 1]: t - i}
        for name, weight in weights.items():
            if weight > 0:
                groups[name].add([vt.index], weight, "REPLACE")


def paint(body, colours):
    uv = body.data.uv_layers.new(name="UVMap").data
    kinds = []
    for poly, colour in zip(body.data.polygons, colours):
        co = poly.center
        lum = float(luma(colour))
        # Muted brown roots and bowl, black throat, pale teeth/rim, a dark lid and green
        # leaf tips. Spatial landmarks preserve large readable patches after the cut.
        if is_lid(co):
            swatch = "pitcher_eye"
        elif co.z > 1.28 and lum > .58:
            swatch = "pitcher_shine"
        elif co.z > 1.10 and (co.y < .15 or lum < .15):
            swatch = "frog_pupil"
        elif co.z < .9 and math.hypot(co.x, co.y) > .95:
            swatch = "pitcher_lid"
        else:
            swatch = "pitcher_root"
        kinds.append(swatch)
        for li in poly.loop_indices:
            z = body.data.vertices[body.data.loops[li].vertex_index].co.z
            uv[li].uv = uv_for(swatch, min(1.0, max(0.0, 1 - z / 2)))
        poly.use_smooth = True
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new("enemy"))
    print("swatches", {k: kinds.count(k) for k in sorted(set(kinds))})


def check(body):
    mesh = body.data
    assert 400 <= len(mesh.polygons) <= 2500
    assert body.dimensions.x >= .7 * body.dimensions.z
    for vt in mesh.vertices:
        assert 1 <= len(vt.groups) <= 4
        assert abs(sum(g.weight for g in vt.groups) - 1) < 1e-4
    atlas = bpy.data.images.load(os.path.join(TEX, "T_Enemy_Albedo.png"))
    w, h = atlas.size
    px = np.array(atlas.pixels[:]).reshape(h, w, 4)[..., :3]
    # Image.pixels, as loaded here, carries the stored atlas values.
    area = lum = sat = 0.0
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        u, t = sum((uv[i].uv for i in poly.loop_indices), Vector((0, 0))) / poly.loop_total
        c = px[min(h - 1, int(t * h)), min(w - 1, int(u * w))]
        area += poly.area
        lum += poly.area * float(luma(c))
        sat += poly.area * float(saturation(c))
    print(f"MotherPitcher: {len(mesh.polygons)} triangles, {len(mesh.vertices)} vertices, "
          f"bounds {tuple(body.dimensions)}, luma {lum / area:.3f}, saturation {sat / area:.3f}")
    assert .15 <= lum / area <= .45 and sat / area <= .4, "repaint outside JG-12d rule 6"


# Degrees about model axes: positive pitch bends the plant toward -Y.
CLIPS = {
    "Idle": {0: {}, 15: {"stem_2": (0, 2, 0), "lid": (-3, 0, 0)},
             30: {"stem_2": (2, 0, 0)}, 45: {"stem_2": (0, -2, 0), "lid": (-3, 0, 0)}, 60: {}},
    "Barrage": {0: {}, 7: {"stem_2": (-8, 0, 0), "cup": (-16, 0, 0), "lid": (-45, 0, 0)},
                12: {"stem_2": (8, 0, 0), "cup": (22, 0, 0), "lid": (-65, 0, 0)},
                17: {"cup": (9, 0, 0), "lid": (-25, 0, 0)}, 24: {}},
    "Spray": {0: {}, 6: {"cup": (-10, 0, 0), "lid": (-40, 0, 0)},
              10: {"stem_2": (8, 0, 0), "cup": (24, 0, 0), "lid": (-75, 0, 0)},
              30: {"stem_2": (8, 0, 0), "cup": (24, 0, 0), "lid": (-75, 0, 0)}},
    "Hit": {0: {}, 3: {"stem_3": (-12, 3, 0), "lid": (12, 0, 0)},
            7: {"stem_3": (3, -1, 0)}, 12: {}},
    "Death": {0: {}, 8: {"stem_2": (12, 0, 0), "lid": (15, 0, 0)},
              20: {"stem_1": (18, 0, 0), "stem_2": (25, 0, 0), "stem_3": (25, 0, 0),
                   "cup": (20, 0, 0), "lid": (25, 0, 0)},
              32: {"stem_1": (20, 0, 0), "stem_2": (28, 0, 0), "stem_3": (25, 0, 0),
                   "cup": (15, 0, 0), "lid": (30, 0, 0)},
              40: {"stem_1": (20, 0, 0), "stem_2": (28, 0, 0), "stem_3": (25, 0, 0),
                   "cup": (15, 0, 0), "lid": (30, 0, 0)}},
}


def author_clips(arm):
    arm.animation_data_create()
    A = arm.matrix_world.to_3x3()
    for name, keys in CLIPS.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        for frame, pose in sorted(keys.items()):
            for pb in arm.pose.bones:
                pb.matrix_basis = Matrix.Identity(4)
                angles = pose.get(pb.name, (0, 0, 0))
                # Leaves stir in idle, flare with attacks, and droop in death.
                if pb.name.startswith("leaf_"):
                    phase = int(pb.name[-1]) * math.pi / 2
                    a = 3 * math.sin(frame / 60 * math.tau + phase) if name == "Idle" else 0
                    if name == "Death":
                        a = -min(32, frame)
                    angles = (a, 0, 0)
                q = (Quaternion(v(0, 0, 1), math.radians(angles[2]))
                     @ Quaternion(v(0, 1, 0), math.radians(angles[1]))
                     @ Quaternion(v(1, 0, 0), math.radians(angles[0])))
                R = A @ pb.bone.matrix_local.to_3x3()
                pb.rotation_quaternion = R.inverted().to_quaternion() @ q @ R.to_quaternion()
                if pb.name != "root":
                    pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            # Export keys the unchanging root too, for a complete Generic take.
        action.use_frame_range = True
        action.frame_range = (0, max(keys))
        # LINEAR keeps Spray exactly held from 10 and avoids death overshoot.
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)


def build_armature(bones):
    data = bpy.data.armatures.new("Rig_MotherPitcher")
    arm = bpy.data.objects.new("Rig_MotherPitcher", data)
    bpy.context.scene.collection.objects.link(arm)
    # frog.py's shape: Y-up data under an object turned 90 degrees about X, so it faces +Z in Unity.
    arm.matrix_world = Matrix.Rotation(math.radians(90), 4, "X")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    to_arm = arm.matrix_world.inverted()
    edit = {}
    for name, (parent, head, tail) in bones.items():
        bone = data.edit_bones.new(name)
        bone.head = to_arm @ head
        bone.tail = to_arm @ tail
        direction = (tail - head).normalized()
        up = v(0, 0, 1) if abs(direction.z) < 0.7 else v(0, -1, 0)
        bone.align_roll(to_arm.to_3x3() @ up)
        if parent:
            bone.parent = edit[parent]
        edit[name] = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    data.bones["root"].use_deform = False
    return arm


def bind(body, arm):
    """Under the armature, identity transform, data in its space, as KayKit's own meshes sit."""
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    body.parent = arm
    body.data.transform(arm.matrix_world.inverted())
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_local = Matrix.Identity(4)
    bpy.context.view_layer.update()


def export(arm, body):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, "MotherPitcher.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",  # rootling.py says why
        mesh_smooth_type="FACE",
        use_mesh_modifiers=False,
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


CELL = 288
SHEET = {"Idle": (0, 15, 30, 45, 60), "Barrage": (0, 7, 12, 17, 24),
         "Spray": (0, 6, 10, 20, 30), "Hit": (0, 3, 7, 12), "Death": (0, 8, 20, 32, 40)}


def pose_sheet(path):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, "MotherPitcher.fbx"))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    image = bpy.data.images.load(os.path.join(TEX, "T_Enemy_Albedo.png"))

    def textured(material, img):
        material.use_nodes = True
        nodes = material.node_tree.nodes
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = "Closest"
        material.node_tree.links.new(tex.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
        nodes.active = tex  # Workbench draws the active image node

    textured(body.data.materials[0], image)
    floor_img = bpy.data.images.new("floor", 4, 4)
    floor_img.pixels.foreach_set(np.tile(np.array([0x4E / 255, 0x6E / 255, 0x3F / 255, 1.0], np.float32), 16))
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    floor = bpy.context.object
    floor.data.materials.append(bpy.data.materials.new("floor"))
    textured(floor.data.materials[0], floor_img)

    actions = {a.name.split("|")[-1]: a for a in bpy.data.actions}
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.view_settings.view_transform = "Standard"  # AgX, the default, greys the atlas's colours
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "TEXTURE"
    shading.show_object_outline = True
    shading.show_shadows = True
    shading.show_specular_highlight = False  # M_Enemy's smoothness is 0.05; Workbench's sheen greys the throat
    scene.render.resolution_x = scene.render.resolution_y = CELL
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def aim(eye, target, scale):
        cam_data.ortho_scale = scale
        cam.location = eye
        cam.rotation_euler = (target - eye).to_track_quat("-Z", "Y").to_euler()

    def render():
        out = os.path.join(bpy.app.tempdir, "cell.png")
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(out)
        px = np.array(img.pixels[:], np.float32).reshape(CELL, CELL, 4)[..., :3]
        bpy.data.images.remove(img)
        return np.flipud(px)

    pitch_r = math.radians(57)
    views = {
        "three_quarter": (v(-3.2, -3.4, 2.4), v(0, 0, .95), 2.7),
        "side": (v(-5, 0, 2.0), v(0, 0, .75), 2.7),
        "game": (v(0, -math.cos(pitch_r) * 6, math.sin(pitch_r) * 6 + 0.4), v(0, 0, .95), 2.7),
    }
    width = max(len(f) for f in SHEET.values())
    rows = []
    arm.animation_data_create()
    for clip, frames in SHEET.items():
        arm.animation_data.action = actions[clip]
        for view in (("three_quarter", "side") if clip in ("Barrage", "Spray", "Death") else ("three_quarter",)):
            aim(*views[view])
            cells = []
            for f in frames:
                scene.frame_set(int(f))
                cells.append(render())
            while len(cells) < width:
                cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
            rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {clip}")
    # Equal-scale silhouettes, left to right: Pitcher, Mother Pitcher, Croaker.
    arm.animation_data.action = actions["Idle"]
    scene.frame_set(0)
    def shift(obj, x):
        holder = bpy.data.objects.new("at_" + obj.name, None)
        scene.collection.objects.link(holder)
        holder.location.x = x
        obj.parent = holder
    shift(arm, 0)
    for name, x in (("Pitcher", -5.0), ("Croaker", 5.0)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"))
        for obj in set(bpy.data.objects) - before:
            if obj.parent is None:
                shift(obj, x)
    floor.hide_render = True
    shading.light = "FLAT"
    shading.color_type = "SINGLE"
    shading.single_color = (0, 0, 0)
    shading.show_object_outline = False
    shading.show_shadows = False
    cells = []
    for x in (-5, 0, 5):
        aim(v(x - 1.8, -6, 3.0), v(x, 0, 1.4), 4.4)
        cells.append(render())
    while len(cells) < width:
        cells.append(np.ones((CELL, CELL, 3), np.float32) * .15)
    rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("mother_pitcher.py: set PYTHONHASHSEED=0 for stable FBX ids")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    hi = load()
    shape(hi)
    body = decimate(hi)
    body.data.update()
    colours = source_colours(hi, body)
    bpy.data.objects.remove(hi)
    paint(body, colours)
    bones = skeleton()
    weigh(body, bones)
    # 1.6 m at 1x: JG-12e's BodyScale 2.5 makes a 4 m giant.
    body.data.transform(Matrix.Scale(.8, 4))
    body.data.update()
    bpy.context.view_layer.update()
    bones = {n: (parent, head * .8, tail * .8) for n, (parent, head, tail) in bones.items()}
    check(body)
    arm = build_armature(bones)
    bind(body, arm)
    author_clips(arm)
    export(arm, body)
    if "--pose-sheet" in args:
        pose_sheet(os.path.abspath(args[args.index("--pose-sheet") + 1]))


if __name__ == "__main__":
    main()
