"""Build the SS-06b sculpt, rig and clips in Blender 4.1 with PYTHONHASHSEED=0.

blender --background --factory-startup --python Tools/Blender/beetle.py
    -- --turnaround Temp/beetle_turnaround.png

Omit --turnaround to write Beetle.fbx and the shared atlas.
"""

import argparse
from hashlib import sha256
import math
import os
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from enemy_atlas import SWATCHES, build_atlas, uv_for
from frog import ik2
from scorpion import aimed, preview_material, render_sheet, turned_about

SOURCE = TOOLS / "Sources" / "Beetle_src.glb"
SOURCE_SHA256 = "cac2c5273c6de3940eadcfc38e2d2982f87ab066ca602f1d167d270e3a3f865a"
SCALE = 3.0
VENT = Vector((0.151, 0.448, 0.243))
VENT_OUT = Vector((0.0, 0.65, 0.76)).normalized()


def verify_source():
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    assert actual == SOURCE_SHA256, f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}"


def repair(destination):
    subprocess.run([
        bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
        "--python", str(TOOLS / "repair_scan.py"), "--", str(SOURCE), str(destination),
        "--target-triangles", "24000", "--texture-size", "256",
    ], check=True)


def weld(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def load_body(closed, preview=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    parts = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        world = part.matrix_world.copy()
        part.parent = None
        part.matrix_world.identity()
        part.data.transform(world)
        part.select_set(True)
    bpy.context.view_layer.objects.active = max(parts, key=lambda obj: len(obj.data.polygons))
    if len(parts) > 1:
        bpy.ops.object.join()
    body = bpy.context.object
    body.name = "Beetle"
    mesh = body.data
    markings = source_markings(mesh)
    mesh.materials.clear()
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    if mesh.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    weld(mesh)
    mesh.calc_loop_triangles()
    decimate = body.modifiers.new("Crowd budget", "DECIMATE")
    decimate.ratio = min(1, 2490 / len(mesh.loop_triangles))
    decimate.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    smooth_dorsal_seam(body.data)
    weld(body.data)
    repaint(body, markings, preview)
    return body


def smooth_dorsal_seam(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    seam = [vertex for vertex in bm.verts
            if abs(vertex.co.x - 0.018) < 0.085 and -0.10 < vertex.co.y < 0.07
            and vertex.co.z > 0.14]
    for _ in range(3):
        bmesh.ops.smooth_vert(bm, verts=seam, factor=0.42,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(mesh)
    bm.free()


def source_markings(mesh):
    """Sample the repaired bake as a placement mask for the new atlas colours."""
    mesh.calc_loop_triangles()
    points = [vertex.co.copy() for vertex in mesh.vertices]
    triangles = [tuple(triangle.vertices) for triangle in mesh.loop_triangles]
    uv = mesh.uv_layers.active.data
    uv_triangles = [tuple(Vector((*uv[i].uv, 0)) for i in triangle.loops)
                    for triangle in mesh.loop_triangles]
    tree = BVHTree.FromPolygons(points, triangles, all_triangles=True)
    nodes = mesh.materials[0].node_tree.nodes
    image = next(node.image for node in nodes
                 if node.type == "TEX_IMAGE" and node.image.colorspace_settings.name == "sRGB")
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(height, width, 4)

    def sample(point):
        nearest, _, index, _ = tree.find_nearest(point)
        a, b, c = (points[i] for i in triangles[index])
        u, v, _ = barycentric_transform(nearest, a, b, c, *uv_triangles[index])
        return pixels[int(v * height) % height, int(u * width) % width, :3]

    return sample


def swatch_at(point, color):
    x, y, z = point
    vent_offset = point - VENT
    if vent_offset.length < 0.083 and vent_offset.dot(VENT_OUT) > -0.043:
        return "beetle_vent"
    red, green, blue = color
    if red < 0.24 and abs(x) < 0.19 and y < -0.30:
        return "beetle_vent"
    if red < 0.53 or z < -0.19:
        return "beetle_umber"
    if blue > red * 0.31 and green > red * 0.65:
        return "beetle_bone"
    return "beetle_ochre"


def repaint(body, markings, preview):
    mesh = body.data
    swatches = [name for name in SWATCHES if name.startswith("beetle_")]
    uv = mesh.uv_layers.new(name="UVMap").data
    if preview:
        for swatch in swatches:
            mesh.materials.append(preview_material(swatch, SWATCHES[swatch][2]))
    else:
        mesh.materials.append(bpy.data.materials.new("enemy"))
    for face in mesh.polygons:
        swatch = swatch_at(face.center, markings(face.center))
        face.material_index = swatches.index(swatch) if preview else 0
        face.use_smooth = True
        for loop_index in face.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv[loop_index].uv = uv_for(swatch, (0.28 - point.z) / 0.58)


def place_body(body):
    mesh = body.data
    ground = min(vertex.co.z for vertex in mesh.vertices)
    for vertex in mesh.vertices:
        vertex.co.z -= ground
        vertex.co *= SCALE
    mesh.update()
    bpy.context.view_layer.update()
    return ground


def turnaround(closed, path):
    body = load_body(closed)
    place_body(body)
    mesh = body.data
    mesh.calc_loop_triangles()
    width, length, height = body.dimensions
    count = len(mesh.loop_triangles)
    print(f"Beetle: {count} triangles, W/L/H={width:.3f}/{length:.3f}/{height:.3f} m", flush=True)
    assert 400 <= count <= 2500
    assert width >= 1.5 * height
    path.parent.mkdir(parents=True, exist_ok=True)
    render_sheet(path, body.dimensions)


# Rest joints measured on the approved sculpt, after grounding and scaling to metres.
LEG_JOINTS = (
    ((0.58, -0.78, 0.77), (0.99, -0.96, 0.64), (1.21, -1.29, 0.10)),
    ((0.83, -0.10, 0.68), (1.24, -0.10, 0.62), (1.44, 0.04, 0.10)),
    ((0.79, 0.54, 0.65), (1.12, 0.72, 0.62), (1.32, 0.97, 0.10)),
)
CLIPS = {"Idle": 48, "Walk": 16, "Turn": 24, "Aim": 21, "Fire": 12, "Hit": 12, "Death": 30}
AIM_LIFT = math.radians(24)


def build_armature(leg_joints=LEG_JOINTS, *, body_joints=((0, -0.22, 0.85), (0, -0.60, 0.85)),
                   head_joints=((0, -0.77, 0.88), (0, -1.16, 0.91)),
                   abdomen_joints=((0, -0.20, 0.90), (0, 0.93, 1.24))):
    data = bpy.data.armatures.new("Rig_Beetle")
    arm = bpy.data.objects.new(data.name, data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    def add(name, parent, head, tail):
        bone = data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = data.edit_bones[parent]

    add("Root", None, (0, 0, 0), (0, 0, 0.2))
    data.edit_bones["Root"].use_deform = False
    add("Body", "Root", *body_joints)
    add("Head", "Body", *head_joints)
    add("Abdomen", "Body", *abdomen_joints)
    legs = []
    for side, suffix in ((1, "L"), (-1, "R")):
        for index, joints in enumerate(leg_joints, 1):
            hip, knee, foot = (Vector((side * x, y, z)) for x, y, z in joints)
            upper, lower = (f"Leg_{suffix}_{index}_{part}" for part in ("Upper", "Lower"))
            add(upper, "Body", hip, knee)
            add(lower, upper, knee, foot)
            legs.append((side, index, upper, lower, hip, knee, foot))

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm, legs


def rig(body, ground):
    arm, legs = build_armature()
    body.parent = arm
    modifier = body.modifiers.new("Beetle skin", "ARMATURE")
    modifier.object = arm
    bind_skin(body, legs)

    vent = bpy.data.objects.new("Vent", None)
    bpy.context.scene.collection.objects.link(vent)
    vent.empty_display_type = "ARROWS"
    vent.empty_display_size = 0.3
    center = (VENT - Vector((0, 0, ground))) * SCALE
    vent.parent = arm
    vent.parent_type = "BONE"
    vent.parent_bone = "Abdomen"
    bpy.context.view_layer.update()
    vent.matrix_world = Matrix.Translation(center) @ VENT_OUT.to_track_quat("Z", "Y").to_matrix().to_4x4()
    return arm, legs, vent


def smoothstep(low, high, value):
    t = min(1.0, max(0.0, (value - low) / (high - low)))
    return t * t * (3 - 2 * t)


def distance_to_segment(point, start, end):
    direction = end - start
    t = min(1.0, max(0.0, (point - start).dot(direction) / direction.length_squared))
    return (point - start - t * direction).length


def bind_skin(body, legs):
    names = ["Body", "Head", "Abdomen"]
    names.extend(name for _, _, upper, lower, *_ in legs for name in (upper, lower))
    groups = {name: body.vertex_groups.new(name=name) for name in names}
    for vertex in body.data.vertices:
        point = vertex.co
        x, y, z = point
        candidates = []
        for side, _, upper, lower, hip, knee, foot in legs:
            if side * x > 0:
                upper_distance = distance_to_segment(point, hip, knee)
                lower_distance = distance_to_segment(point, knee, foot)
                candidates.append((min(upper_distance, lower_distance), upper_distance,
                                   lower_distance, upper, lower, hip))
        weights = {}
        leg_weight = 0.0
        if candidates:
            distance, upper_distance, lower_distance, upper, lower, hip = min(candidates)
            leg_weight = smoothstep(abs(hip.x) - 0.07, abs(hip.x) + 0.22, abs(x))
            leg_weight *= 1 - smoothstep(0.73, 1.06, z)
            leg_weight *= 1 - smoothstep(0.29, 0.55, distance)
            lower_mix = smoothstep(-0.16, 0.16, upper_distance - lower_distance)
            weights[upper] = leg_weight * (1 - lower_mix)
            weights[lower] = leg_weight * lower_mix

        torso = 1 - leg_weight
        head = 1 - smoothstep(-0.96, -0.68, y)
        abdomen = smoothstep(-0.28, 0.20, y)
        weights.update(Body=torso * (1 - head - abdomen), Head=torso * head, Abdomen=torso * abdomen)
        weights = {name: weight for name, weight in weights.items() if weight > 0.001}
        assert 1 <= len(weights) <= 4
        total = sum(weights.values())
        for name, weight in weights.items():
            groups[name].add([vertex.index], weight / total, "REPLACE")


def step(phase, stride, lift):
    # The foot moves back at gameplay speed during stance. Swing eases back to front.
    if phase <= 0.5:
        return stride * (2 * phase - 0.5), 0.0
    swing = 2 * phase - 1
    return stride * (0.5 - smoothstep(0, 1, swing)), lift * math.sin(math.pi * swing)


def pose(arm, legs, *, shift=(0, 0, 0), pitch=0, roll=0, lift=0, nod=0,
         walk=None, turn=None, curl=0):
    rest = {bone.name: bone.bone.matrix_local for bone in arm.pose.bones}
    rotation = Quaternion((0, 1, 0), roll) @ Quaternion((1, 0, 0), pitch)
    body = Matrix.Translation(shift) @ turned_about(rest["Body"].translation, rotation)
    matrices = {"Root": rest["Root"], "Body": body @ rest["Body"]}
    for name, angle in (("Head", nod), ("Abdomen", lift)):
        matrices[name] = body @ turned_about(rest[name].translation, Quaternion((1, 0, 0), angle)) @ rest[name]

    for side, index, upper, lower, hip, knee, foot in legs:
        target = foot.copy()
        pole = knee - hip
        if walk is not None or turn is not None:
            phase = ((walk if walk is not None else turn) + 0.5 * ((index + (side < 0)) % 2)) % 1
            stride = 2.8 * (CLIPS["Walk"] / 30) * 0.5 if walk is not None else 0.23 * side
            travel, height = step(phase, stride, 0.17 if walk is not None else 0.10)
            center = (0.23, 0.0, -0.22)[index - 1] if walk is not None else 0.0
            target.y += center + travel
            target.z += height
        if curl:
            target = body @ (hip + (foot - hip) * (1 - 0.52 * curl) + Vector((0, 0, 0.18 * curl)))
            pole = body.to_3x3() @ pole
        start = body @ hip
        bend, end = ik2(start, target, (knee - hip).length, (foot - knee).length, pole)
        if walk is not None and phase <= 0.5:
            assert (end - target).length < 1e-5, f"{lower} cannot reach its planted foot"
        matrices[upper] = aimed(rest[upper], start, bend - start)
        matrices[lower] = aimed(rest[lower], bend, end - bend)

    for bone in arm.pose.bones:
        name = bone.name
        basis = rest[name].inverted()
        if bone.parent:
            parent = bone.parent.name
            basis = basis @ rest[parent] @ matrices[parent].inverted()
        bone.rotation_mode = "QUATERNION"
        bone.matrix_basis = basis @ matrices[name]


def clips():
    return {
        "Idle": {0: {}, 12: dict(shift=(0, 0, 0.021), nod=-0.045, lift=0.025),
                 28: dict(shift=(0, 0, 0.008), nod=0.035), 48: {}},
        "Walk": {frame: dict(walk=frame / 16, shift=(0, 0, -0.045),
                             nod=0.04 * math.cos(frame * math.tau / 8)) for frame in range(17)},
        "Turn": {frame: dict(turn=frame / 24) for frame in range(25)},
        "Aim": {0: {}, 5: dict(lift=0.10, shift=(0, 0, -0.032)),
                15: dict(lift=AIM_LIFT, shift=(0, 0, -0.032)),
                21: dict(lift=AIM_LIFT, shift=(0, 0, -0.032))},
        "Fire": {0: dict(lift=AIM_LIFT, shift=(0, 0, -0.032)),
                 1: dict(lift=AIM_LIFT, shift=(0, 0.057, -0.012)),
                 3: dict(lift=0.29, shift=(0, -0.026, 0.039)),
                 7: dict(lift=0.07, shift=(0, 0, -0.018)), 12: {}},
        "Hit": {0: {}, 2: dict(shift=(0, 0.09, 0.035), pitch=-0.16, nod=0.15, lift=0.08),
                6: dict(shift=(0, 0.03, -0.025), pitch=0.065, nod=-0.05), 12: {}},
        "Death": {0: {}, 5: dict(shift=(0.04, 0, 0.17), roll=0.66, curl=0.16),
                  12: dict(shift=(0.15, 0, 0.58), roll=1.9, curl=0.56),
                  20: dict(shift=(0.24, 0, 0.10), roll=math.pi, curl=0.86),
                  30: dict(shift=(0.24, 0, 0.08), roll=math.pi, curl=1)},
    }


def animate(arm, legs):
    arm.animation_data_create()
    for name, keys in clips().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame, parameters in keys.items():
            pose(arm, legs, **parameters)
            for bone in arm.pose.bones:
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert("location", frame=frame, group=bone.name)
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
        assert max(keys) == CLIPS[name]
        action.frame_range = (0, CLIPS[name])
        action.use_frame_range = True
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def export_model(closed, destination):
    body = load_body(closed, preview=False)
    ground = place_body(body)
    body.data.calc_loop_triangles()
    assert 400 <= len(body.data.loop_triangles) <= 2500 and len(body.data.materials) == 1
    assert body.dimensions.x >= 1.5 * body.dimensions.z
    arm, legs, vent = rig(body, ground)
    animate(arm, legs)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (arm, body, vent):
        obj.select_set(True)
    bpy.context.scene.render.fps = 30
    build_atlas(str(destination / "Textures"))
    bpy.ops.export_scene.fbx(
        filepath=str(destination / "Beetle.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype="NULL",
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_simplify_factor=0.0, path_mode="STRIP",
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--turnaround", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    assert os.environ.get("PYTHONHASHSEED") == "0", "Set PYTHONHASHSEED=0 before Blender starts"
    verify_source()
    with TemporaryDirectory(prefix="beetle-repair-") as folder:
        closed = Path(folder) / "Beetle_closed.glb"
        repair(closed)
        if args.turnaround:
            turnaround(closed, args.turnaround.resolve())
        else:
            export_model(closed, TOOLS.parents[1] / "Assets/_Project/Art/Enemies")


if __name__ == "__main__":
    main()
