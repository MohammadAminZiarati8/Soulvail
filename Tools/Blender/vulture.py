"""Build the SS-14b Vulture in Blender 4.1.

PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1
    -P Tools/Blender/vulture.py

Append -- --turnaround Temp/vulture_turnaround.png for the unrigged review.
"""

import argparse
from hashlib import sha256
from math import radians
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

TOOLS = Path(__file__).resolve().parent
SOURCE = TOOLS / "Sources" / "Vulture_src.glb"
SOURCE_SHA256 = "4ee6d652aeb4a6ea456ca371f3e5a92ff8c90f3226fc57dd95656c525f85ab8f"
WINGSPAN = 2.6
ART = TOOLS.parents[1] / "Assets/_Project/Art/Enemies"

sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from glassback import render_turnaround
from enemy_atlas import build_atlas
from hawk import bake as bake_flight
from hawk import skeleton as hawk_skeleton
from jackal import mesh_bounds, paint as paint_atlas
from panther import bind, build_armature, segment_distance
from stalker import texture_sampler


def repair(destination):
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    if actual != SOURCE_SHA256:
        raise ValueError(f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}")
    subprocess.run([
        bpy.app.binary_path, "-b", "--factory-startup", "-t", "8", "--python-exit-code", "1",
        "-P", str(TOOLS / "repair_scan.py"), "--", str(SOURCE), str(destination),
        "--target-triangles", "2850", "--texture-size", "1024", "--point-shell",
    ], check=True)


def load_body(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    body, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    low, high = mesh_bounds([body])
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    scale = WINGSPAN / (high.x - low.x)
    # The approved front view faces Blender -Y, which exports as Unity +Z.
    body.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-offset) @ body.matrix_world)
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    body.name = "Vulture"
    bpy.context.view_layer.update()

    body.data.calc_loop_triangles()
    triangles = len(body.data.loop_triangles)
    if not 1500 <= triangles <= 3000:
        raise ValueError(f"Vulture triangle count outside budget: {triangles}")
    width, length, height = body.dimensions
    print(f"Vulture: {triangles} triangles, W/L/H={width:.3f}/{length:.3f}/{height:.3f} m",
          flush=True)
    return body


def fit_skeleton():
    joints = {
        "root": ((0, 0, 0), (0, 0, .12)),
        "body": ((0, .055, .38), (0, -.10, .63)),
        "neck": ((0, -.10, .63), (0, -.24, .81)),
        "head": ((0, -.24, .81), (0, -.36, .84)),
        "tail": ((0, .10, .29), (0, .31, .07)),
    }
    for side, sign in (("l", 1), ("r", -1)):
        shoulder = (sign * .19, -.08, .63)
        wrist = (sign * .78, -.04, .79)
        joints[f"wing.{side}.1"] = (shoulder, wrist)
        joints[f"wing.{side}.2"] = (wrist, (sign * 1.26, .12, .63))
        joints[f"leg.{side}"] = ((sign * .095, -.09, .36), (sign * .13, -.15, .16))
    return {name: (parent, *map(Vector, joints[name])) for name, (parent, _, _) in hawk_skeleton().items()}


def skin(body, bones):
    groups = {name: body.vertex_groups.new(name=name) for name in bones if name != "root"}
    for vertex in body.data.vertices:
        x, y, z = vertex.co
        side = "l" if x >= 0 else "r"
        inner, outer, leg = f"wing.{side}.1", f"wing.{side}.2", f"leg.{side}"
        if abs(x) >= 1.04:
            candidates = [outer]
        elif abs(x) > .29:
            candidates = [inner, outer]
        elif y > .11 and z < .36:
            candidates = ["tail", "body"]
        elif z < .39:
            candidates = [leg, "tail", "body"]
        else:
            candidates = ["body", "neck", "head", inner]
        nearest = sorted((segment_distance(vertex.co, *bones[name][1:]), name)
                         for name in candidates)[:3]
        weights = [(name, 1 / (distance + .01)**3) for distance, name in nearest]
        total = sum(weight for _, weight in weights)
        for name, weight in weights:
            groups[name].add([vertex.index], weight / total, "REPLACE")


def paint(body):
    sample = texture_sampler(body)

    def swatch(point):
        x, y, z = point
        r, g, b = sample(point)
        light = .2126 * r + .7152 * g + .0722 * b
        if abs(x) < .13 and z > .76:
            if y < -.30 and light < .32:
                return "vulture_horn", 1 - light * 2
            return "vulture_skin", 1.25 - light
        if abs(x) < .25 and z > .51 and light > .58:
            return "vulture_ruff", 1.15 - light
        if abs(x) < .18 and z < .37 and y < .085:
            return "vulture_horn", 1 - light * 2
        if abs(x) > .29 and light > .58:
            return "vulture_edge", 1.4 - light
        return "vulture_umber", 1 - light * 1.45

    paint_atlas(body, swatch)


def turn_world(arm, name, axis, angle, tilt=0):
    bone = arm.pose.bones[name]
    rest = (arm.matrix_world @ bone.bone.matrix_local).to_quaternion()
    turn = Quaternion((1, 0, 0), radians(tilt)) @ Quaternion(axis, radians(angle))
    bone.rotation_quaternion = rest.inverted() @ turn @ rest


def ground_pose(arm, body, pitch, sweep, wrist, roll=0, reach=0, nod=0,
                right_wing=None, floor_gap=0):
    for bone in arm.pose.bones:
        bone.location = Vector()
        bone.rotation_quaternion = Quaternion()
    turn_world(arm, "body", (1, 0, 0), pitch)
    torso = arm.pose.bones["body"]
    rest = (arm.matrix_world @ torso.bone.matrix_local).to_quaternion()
    torso.rotation_quaternion @= rest.inverted() @ Quaternion((0, 1, 0), radians(roll)) @ rest
    turn_world(arm, "neck", (1, 0, 0), -.63 * pitch + nod)
    turn_world(arm, "head", (1, 0, 0), -.27 * pitch + nod)
    turn_world(arm, "tail", (1, 0, 0), 10)
    for side, sign in (("l", 1), ("r", -1)):
        wing_sweep, wing_wrist = right_wing if side == "r" and right_wing else (sweep, wrist)
        # Lower the swept wings as the body pitches forward, then tuck the tips at the wrist.
        tilt = -max(0, wing_sweep) / 80 * (pitch + 7)
        turn_world(arm, f"wing.{side}.1", (0, 0, 1), sign * wing_sweep, tilt)
        turn_world(arm, f"wing.{side}.2", (0, 0, 1), sign * wing_wrist)
        turn_world(arm, f"leg.{side}", (1, 0, 0), reach - .45 * pitch)
    bpy.context.view_layer.update()
    posed = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    low = min((posed.matrix_world @ vertex.co).z for vertex in posed.data.vertices)
    torso.location = rest.inverted() @ Vector((0, 0, floor_gap - low))
    bpy.context.view_layer.update()
    return posed


def bake_ground(arm, body):
    standing = dict(pitch=34, sweep=80, wrist=120)
    clips = {
        "Land": {
            0: dict(pitch=18, sweep=-24, wrist=-8, reach=-20, floor_gap=.20),
            5: dict(pitch=28, sweep=-9, wrist=20, reach=-32, floor_gap=.07),
            9: dict(pitch=40, sweep=63, wrist=85, reach=-12),
            12: standing, 15: standing,
        },
        "Idle_Ground": {0: standing, 24: dict(standing, nod=5), 48: standing},
        "Hit": {
            0: standing, 3: dict(standing, pitch=19, reach=-9, nod=-14, roll=11),
            7: dict(standing, pitch=39, nod=6), 12: standing,
        },
        "Death": {
            0: standing, 7: dict(standing, pitch=49, roll=14, nod=9, right_wing=(58, 65)),
            18: dict(standing, pitch=65, roll=44, nod=22, right_wing=(8, 19)),
            30: dict(standing, pitch=71, roll=53, nod=28, right_wing=(-12, 14)),
        },
    }
    for name, keys in clips.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        for frame, settings in keys.items():
            # Solve foot contact from the current pose before attaching the keyed action.
            arm.animation_data.action = None
            posed = ground_pose(arm, body, **settings)
            if name == "Idle_Ground" and frame == 0:
                xs = [(posed.matrix_world @ vertex.co).x for vertex in posed.data.vertices]
                span = max(xs) - min(xs)
                assert span <= WINGSPAN * .45, f"Folded wingspan: {span:.3f} m"
                print(f"Vulture folded wingspan: {span:.3f} m", flush=True)
            arm.animation_data.action = action
            for bone in arm.pose.bones:
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            arm.pose.bones["body"].keyframe_insert("location", frame=frame, group="body")
        action.frame_range = (0, max(keys))
        action.use_frame_range = True
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.location = Vector()
        bone.rotation_quaternion = Quaternion()


def build(body):
    # GLB splits vertices at baked UV seams. Weld those before assigning the atlas and skin.
    mesh = bmesh.new()
    mesh.from_mesh(body.data)
    bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=1e-6)
    mesh.to_mesh(body.data)
    mesh.free()
    body.data.update()
    paint(body)
    bones = fit_skeleton()
    skin(body, bones)
    arm = build_armature(bones)
    arm.name = arm.data.name = "Rig_Hawk"
    bind(body, arm)
    bake_flight(arm)
    bake_ground(arm, body)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = 30
    bpy.ops.export_scene.fbx(
        filepath=str(ART / "Vulture.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        armature_nodetype="NULL", bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        path_mode="STRIP",
    )
    build_atlas(str(ART / "Textures"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--turnaround", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    path = args.turnaround.resolve() if args.turnaround else None
    temporary_root = path.parent if path else TOOLS.parents[1] / "Temp"
    temporary_root.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(prefix="vulture-", dir=temporary_root) as temporary:
        directory = Path(temporary)
        closed = directory / "closed.glb"
        repair(closed)
        body = load_body(closed)
        if path:
            render_turnaround(body, path, directory / "tile.png")
        else:
            build(body)


if __name__ == "__main__":
    main()
