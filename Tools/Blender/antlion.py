"""Build the SS-10b antlion model from the approved sculpt.

Set PYTHONHASHSEED=0 and run from the repository root with Blender 4.1:

    blender -b --factory-startup --threads 8 --python-exit-code 1 --python Tools/Blender/antlion.py

Append -- --turnaround Temp/antlion_turnaround.png for the unbound review.
"""

import argparse
from hashlib import sha256
from math import cos, pi, radians, tau
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bpy
from mathutils import Matrix, Quaternion, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from glassback import render_turnaround
from beetle import build_armature, smoothstep, source_markings, step
from brood import distance_to_segment
from enemy_atlas import build_atlas, uv_for
from frog import ik2
from scorpion import aimed, turned_about

SOURCE = TOOLS / "Sources" / "Antlion_src.glb"
SOURCE_SHA256 = "66aaedb4fe6a802e20310647231d75fd568ca32eaca28610ae1d0b8cd4ee8131"
LENGTH = 1.4
FPS = 30
WALK_SPEED = 2.4
STANCE = .31
CLIP_FRAMES = {"Idle": 48, "Walk": 16, "DigIn": 18, "Erupt": 12,
               "Surfaced": 30, "Hit": 12, "Death": 30}
JAWS = {"Mandible_L": 1, "Mandible_R": -1}
JAW_CLOSED = -12
# Front and middle pairs are visible. The rear pair is tucked against the lower flank.
LEG_JOINTS = (
    ((.155, -.134, .183), (.30, -.202, .224), (.369, -.183, .065)),
    ((.228, .105, .191), (.388, .135, .214), (.476, .145, .058)),
    ((.214, .251, .178), (.32, .330, .164), (.343, .418, .085)),
)


def repair(destination):
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    if actual != SOURCE_SHA256:
        raise ValueError(f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}")
    subprocess.run([
        bpy.app.binary_path, "--background", "--factory-startup", "--threads", "8",
        "--python-exit-code", "1", "--python", str(TOOLS / "repair_scan.py"),
        "--", str(SOURCE), str(destination), "--target-triangles", "2800",
        "--texture-size", "1024",
    ], check=True)


def load_body(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    body, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    points = [body.matrix_world @ vertex.co for vertex in body.data.vertices]
    low = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    scale = LENGTH / (high.y - low.y)
    # The hooked mandibles are at source glTF +Z, which imports as Blender -Y.
    # This is the front direction used by the beetle FBX export.
    body.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-offset) @ body.matrix_world)
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    body.name = "Antlion"
    bpy.context.view_layer.update()

    body.data.calc_loop_triangles()
    triangles = len(body.data.loop_triangles)
    width, length, height = body.dimensions
    if not 1500 <= triangles <= 3000 or height > 0.55 * width:
        raise ValueError(f"Antlion outside mesh budget: {triangles} tris, H/W={height / width:.3f}")
    print(f"Antlion: {triangles} triangles, W/L/H={width:.3f}/{length:.3f}/{height:.3f} m",
          flush=True)
    return body


def jaw_influence(point):
    x, y, _ = point
    return smoothstep(.215, .282, -y) * smoothstep(.062, .115, abs(x))


def prepare_runtime_mesh(body):
    mesh = body.data
    sample = source_markings(mesh)
    positions, faces, colours = [], [], []
    indices = {}
    for face in mesh.polygons:
        face_indices = []
        for source_index in face.vertices:
            point = tuple(mesh.vertices[source_index].co)
            if point not in indices:
                indices[point] = len(positions)
                positions.append(point)
            face_indices.append(indices[point])
        faces.append(face_indices)

        red, green, blue = sample(face.center)
        luma = .2126 * red + .7152 * green + .0722 * blue
        if jaw_influence(face.center) > .5:
            swatch = "antlion_bone"
            shade = 1 - smoothstep(.33, .85, luma)
        else:
            swatch = "antlion_ochre" if luma < .50 else "antlion_tan"
            shade = 1 - smoothstep(.18, .76, luma)
        colours.append((swatch, shade))

    runtime = bpy.data.meshes.new("Antlion skin")
    runtime.from_pydata(positions, [], faces)
    runtime.update()
    body.data = runtime
    bpy.data.meshes.remove(mesh)
    runtime.materials.append(bpy.data.materials.new("enemy"))
    uv = runtime.uv_layers.new(name="UVMap").data
    for face, (swatch, shade) in zip(runtime.polygons, colours):
        face.use_smooth = True
        for index in face.loop_indices:
            uv[index].uv = uv_for(swatch, shade)


def build_rig(body):
    arm, legs = build_armature(
        LEG_JOINTS,
        body_joints=((0, -.015, .24), (0, -.155, .24)),
        head_joints=((0, -.133, .208), (0, -.282, .197)),
        abdomen_joints=((0, .045, .245), (0, .563, .223)),
    )
    bpy.ops.object.mode_set(mode="EDIT")
    for name, side in JAWS.items():
        jaw = arm.data.edit_bones.new(name)
        jaw.head = (.145 * side, -.232, .177)
        jaw.tail = (.122 * side, -.675, .022)
        jaw.parent = arm.data.edit_bones["Head"]
    bpy.ops.object.mode_set(mode="OBJECT")
    body.parent = arm
    body.modifiers.new("Antlion skin", "ARMATURE").object = arm
    return arm, legs


def bind(body, arm):
    groups = {bone.name: body.vertex_groups.new(name=bone.name)
              for bone in arm.data.bones if bone.use_deform}
    segments = {bone.name: (bone.head_local, bone.tail_local)
                for bone in arm.data.bones if bone.name in groups and bone.name not in JAWS}
    vertices = body.data.vertices
    weights = np.zeros((len(vertices), len(groups)))
    jaws = [jaw_influence(vertex.co) for vertex in vertices]
    for vertex in vertices:
        point = vertex.co
        side = "L" if point.x >= 0 else "R"
        distances = {name: distance_to_segment(point, segment)
                     for name, segment in segments.items()
                     if not name.startswith("Leg_") or (
                         name.startswith(f"Leg_{side}_")
                         and (not name.startswith(f"Leg_{side}_3_") or point.z < .21))}
        nearest = min(distances.values())
        for name, distance in distances.items():
            weights[vertex.index, groups[name].index] = max(0, 1 - (distance - nearest) / .06)

    neighbors = [[] for _ in vertices]
    for edge in body.data.edges:
        first, second = edge.vertices
        if not jaws[first] and not jaws[second]:
            neighbors[first].append(second)
            neighbors[second].append(first)
    for _ in range(3):
        updated = weights.copy()
        for index, adjacent in enumerate(neighbors):
            if adjacent:
                updated[index] = .5 * (weights[index] + np.mean(weights[adjacent], axis=0))
        weights = updated

    for vertex, row, jaw in zip(vertices, weights, jaws):
        if jaw:
            side = "L" if vertex.co.x >= 0 else "R"
            row.fill(0)
            row[groups[f"Mandible_{side}"].index] = jaw
            row[groups["Head"].index] = 1 - jaw
        strongest = sorted(np.flatnonzero(row), key=lambda index: -row[index])[:4]
        assert strongest, f"Unweighted vertex {vertex.index}"
        total = sum(row[index] for index in strongest)
        for index in strongest:
            body.vertex_groups[index].add([vertex.index], row[index] / total, "REPLACE")
    used = {link.group for vertex in vertices for link in vertex.groups}
    assert used == {group.index for group in groups.values()}


def pose(arm, legs, *, shift=(0, 0, 0), pitch=0, roll=0, nod=0,
         jaw=0, walk=None, scrabble=None, curl=0):
    rest = {bone.name: bone.bone.matrix_local for bone in arm.pose.bones}
    rotation = Quaternion((0, 1, 0), roll) @ Quaternion((1, 0, 0), pitch)
    body = Matrix.Translation(shift) @ turned_about(rest["Body"].translation, rotation)
    head = body @ turned_about(rest["Head"].translation, Quaternion((1, 0, 0), nod))
    matrices = {"Root": rest["Root"], "Body": body @ rest["Body"],
                "Head": head @ rest["Head"], "Abdomen": body @ rest["Abdomen"]}
    for name, side in JAWS.items():
        hinge = Quaternion((0, 0, 1), radians(side * jaw))
        matrices[name] = head @ turned_about(rest[name].translation, hinge) @ rest[name]

    for side, index, upper, lower, hip, knee, foot in legs:
        if walk is None and scrabble is None and not curl:
            matrices[upper] = body @ rest[upper]
            matrices[lower] = body @ rest[lower]
            continue

        pole = body.to_3x3() @ (knee - hip)
        if walk is not None:
            phase = (walk + .5 * ((index + (side < 0)) % 2)) % 1
            stride = WALK_SPEED * CLIP_FRAMES["Walk"] * STANCE / FPS
            # Short legs need a brief stance to travel at the in-game ground speed.
            step_phase = (.5 * phase / STANCE if phase <= STANCE
                          else .5 + .5 * (phase - STANCE) / (1 - STANCE))
            travel, height = step(step_phase, stride, .055)
            if index == 3:
                target = body @ (foot + Vector((0, travel * .18, height * .30)))
            else:
                target = Vector((foot.x - .055 * side, hip.y + travel, foot.z + height))
        elif scrabble is not None:
            phase = (scrabble + .5 * ((index + (side < 0)) % 2)) % 1
            travel, height = step(phase, .16, .06)
            target = body @ (foot + Vector((-.025 * side, travel, height)))
        else:
            target = body @ (hip + (foot - hip) * (1 - .58 * curl) + Vector((0, 0, .06 * curl)))
        start = body @ hip
        bend, end = ik2(start, target, (knee - hip).length, (foot - knee).length, pole)
        if walk is not None and phase <= STANCE:
            assert (end - target).length < 1e-5, f"{lower} cannot reach its planted foot at {phase:.3f}"
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


def clip_keys():
    dig = {}
    for frame in range(19):
        sink = smoothstep(2, 18, frame)
        dig[frame] = dict(shift=(0, 0, -.6 * sink),
                          pitch=.32 * smoothstep(0, 6, frame) - .17 * smoothstep(8, 18, frame),
                          nod=.12 * sink, scrabble=frame / 5 if 0 < frame < 18 else None)
    return {
        "Idle": {0: {}, 12: dict(shift=(0, 0, .01), nod=-.03),
                 30: dict(shift=(0, 0, .004), nod=.025), 48: {}},
        "Walk": {frame: dict(walk=frame / 16, shift=(0, 0, -.01),
                             nod=-.04 + .02 * cos(frame * tau / 8)) for frame in range(17)},
        "DigIn": dig,
        "Erupt": {0: dict(shift=(0, 0, -.6), pitch=.15, nod=.12),
                  4: dict(shift=(0, 0, -.30), pitch=-.20, jaw=20),
                  7: dict(shift=(0, 0, .055), pitch=-.20, jaw=20),
                  8: dict(shift=(0, 0, .045), pitch=.08, jaw=JAW_CLOSED),
                  10: dict(shift=(0, 0, .015), jaw=JAW_CLOSED),
                  12: dict(jaw=JAW_CLOSED)},
        "Surfaced": {0: dict(jaw=23), 8: dict(shift=(0, 0, .012), nod=-.035, jaw=25),
                     22: dict(shift=(0, 0, .004), nod=.025, jaw=21), 30: dict(jaw=23)},
        "Hit": {0: {}, 2: dict(shift=(0, .045, .02), pitch=-.14, nod=.17, jaw=10),
                6: dict(shift=(0, .019, -.018), pitch=.05), 12: {}},
        "Death": {0: {}, 6: dict(shift=(.02, 0, .09), roll=.8, curl=.28),
                  12: dict(shift=(.07, 0, .28), roll=2.1, curl=.64),
                  21: dict(shift=(.10, 0, .08), roll=pi, curl=.95),
                  30: dict(shift=(.10, 0, .008), roll=pi, curl=1)},
    }


def animate(arm, legs):
    arm.animation_data_create()
    for name, keys in clip_keys().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame, parameters in keys.items():
            pose(arm, legs, **parameters)
            for bone in arm.pose.bones:
                if bone.name == "Root":
                    continue
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert("location", frame=frame, group=bone.name)
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
        assert max(keys) == CLIP_FRAMES[name]
        action.frame_range = (0, CLIP_FRAMES[name])
        action.use_frame_range = True
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def export_model(body, destination):
    prepare_runtime_mesh(body)
    arm, legs = build_rig(body)
    bind(body, arm)
    animate(arm, legs)
    bpy.context.scene.render.fps = FPS
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    build_atlas(str(destination / "Textures"))
    bpy.ops.export_scene.fbx(
        filepath=str(destination / "Antlion.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype="NULL",
        bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_simplify_factor=0.0, path_mode="STRIP",
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--turnaround", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    path = args.turnaround.resolve() if args.turnaround else None
    if path:
        path.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(prefix="antlion_") as temp:
        directory = Path(temp)
        closed = directory / "Antlion_closed.glb"
        repair(closed)
        body = load_body(closed)
        if path:
            render_turnaround(body, path, directory / "tile.png")
        else:
            export_model(body, TOOLS.parents[1] / "Assets/_Project/Art/Enemies")


if __name__ == "__main__":
    main()
