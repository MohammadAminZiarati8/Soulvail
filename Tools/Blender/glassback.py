"""Build the SS-09c Glassback sculpt, rig and clips in Blender 4.1.

Set PYTHONHASHSEED=0 and run from the repository root:

    blender -b --factory-startup --python-exit-code 1 --python Tools/Blender/glassback.py
Append -- --turnaround Temp/glassback_turnaround.png for the unbound review.
"""

import argparse
from collections import defaultdict
from math import cos, pi, radians, tau
from hashlib import sha256
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bpy
from mathutils import Matrix, Quaternion, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from beetle import build_armature, source_markings, step
from brood import distance_to_segment
from enemy_atlas import build_atlas, uv_for
from frog import ik2
from scorpion import aimed, turned_about

SOURCE = TOOLS / "Sources" / "Glassback_src.glb"
SOURCE_SHA256 = "a0d6c5223ee8f6c8b6f7b5ac759eebce781c2bebd65b30280d0e103493efd6d4"
LENGTH = 1.5
VIEWS = (
    ("Front", (0, -1, 0)),
    ("Side", (1, 0, 0)),
    ("Back", (0, 1, 0)),
    ("Top", (0, 0, 1)),
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
    # glTF +Z imports as Blender -Y, which the FBX exports as Unity +Z.
    body.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-offset) @ body.matrix_world)
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    body.name = "Glassback"
    bpy.context.view_layer.update()

    body.data.calc_loop_triangles()
    triangles = len(body.data.loop_triangles)
    width, length, height = body.dimensions
    assert 1500 <= triangles <= 3000, triangles
    assert height <= 0.65 * length, height
    print(f"Glassback: {triangles} triangles, W/L/H={width:.3f}/{length:.3f}/{height:.3f} m",
          flush=True)
    return body


def render_turnaround(body, path, tile_path):
    scene = bpy.context.scene
    center = Vector((0, 0, body.dimensions.z / 2))
    extent = max(body.dimensions)

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    size = 480
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "AgX"
    scene.world = bpy.data.worlds.new("Review studio")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.22, 0.22, 0.22, 1)
    background.inputs["Strength"].default_value = 0.7
    for direction, power in (((-2, 2, 3), 180), ((2, -2, 2), 125), ((0, 1, 4), 100)):
        data = bpy.data.lights.new("Studio area", "AREA")
        data.energy = power * extent ** 2
        data.size = extent * 2
        light = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(light)
        light.location = center + Vector(direction) * extent
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()

    camera_data = bpy.data.cameras.new("Orthographic review")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = extent * 1.15
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    scene.render.filepath = str(tile_path)

    sheet = np.empty((2 * size, 2 * size, 4), np.float32)
    for index, (name, direction) in enumerate(VIEWS):
        camera.location = center + Vector(direction) * extent * 4
        if name == "Top":
            camera.rotation_euler = (0, 0, np.pi)
        else:
            camera.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(tile_path), check_existing=False)
        pixels = np.empty(size * size * 4, np.float32)
        image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        row = (1 - index // 2) * size
        column = (index % 2) * size
        sheet[row:row + size, column:column + size] = pixels.reshape(size, size, 4)

    output = bpy.data.images.new("Glassback turnaround", 2 * size, 2 * size)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(path)
    output.file_format = "PNG"
    output.save()



# Hips, knees and feet measured on the approved turnaround in grounded metres.
LEG_JOINTS = (
    ((.24, -.37, .31), (.42, -.46, .24), (.44, -.62, .032)),
    ((.39, -.14, .31), (.61, -.10, .27), (.72, -.14, .030)),
    ((.35, .20, .32), (.59, .24, .26), (.70, .35, .034)),
)
SHELLS = {"Shell_L": 1, "Shell_R": -1}
CLIP_FRAMES = {"Idle": 48, "Walk": 16, "Bite": 15, "Glint": 15,
               "Shine": 30, "Open": 30, "Hit": 12, "Death": 30}
WALK_SPEED = 2.6


def build_rig(body):
    arm, legs = build_armature(
        LEG_JOINTS,
        body_joints=((0, -.09, .38), (0, -.31, .38)),
        head_joints=((0, -.45, .32), (0, -.68, .28)),
        abdomen_joints=((0, .11, .36), (0, .61, .42)),
    )
    bpy.ops.object.mode_set(mode="EDIT")
    for name, side in SHELLS.items():
        bone = arm.data.edit_bones.new(name)
        bone.head = (.19 * side, -.365, .56)
        bone.tail = (.22 * side, .35, .68)
        bone.parent = arm.data.edit_bones["Body"]
    bpy.ops.object.mode_set(mode="OBJECT")
    body.parent = arm
    modifier = body.modifiers.new("Glassback skin", "ARMATURE")
    modifier.object = arm
    return arm, legs


def shell_faces(mesh, markings):
    classified = {}
    at_edge = defaultdict(set)
    for face in mesh.polygons:
        x, y, z = face.center
        red, green, blue = markings(face.center)
        if y > -.465 and z > .365 and (red > .24 or (y > -.30 and z > .49)):
            region = "Shell_L" if x >= 0 else "Shell_R"
            swatch = "glassback_ivory" if green > red * .69 and blue > red * .44 else "glassback_amber"
            classified[face.index] = region, swatch
            for edge in face.edge_keys:
                endpoints = tuple(sorted(tuple(mesh.vertices[index].co) for index in edge))
                at_edge[endpoints, region].add(face.index)

    # The repaired texture can also mark stray facets on the upper legs.
    # Keep the inner and outer surfaces of each case, but leave those flecks on the body.
    remaining = set(classified)
    while remaining:
        pending = [remaining.pop()]
        component = []
        while pending:
            index = pending.pop()
            component.append(index)
            region, _ = classified[index]
            for edge in mesh.polygons[index].edge_keys:
                endpoints = tuple(sorted(tuple(mesh.vertices[vertex].co) for vertex in edge))
                adjacent = at_edge[endpoints, region] & remaining
                remaining.difference_update(adjacent)
                pending.extend(adjacent)
        if len(component) < 12:
            for index in component:
                del classified[index]
    return classified


def prepare_runtime_mesh(body):
    # Duplicate vertices only where a shell meets the other shell or the body.
    # This lets each wing case move rigidly along the existing seam.
    mesh = body.data
    shells = shell_faces(mesh, source_markings(mesh))
    positions, regions, faces, swatches = [], [], [], []
    by_position = {}
    for face in mesh.polygons:
        region, swatch = shells.get(face.index, ("Body", "glassback_umber"))
        indices = []
        for source in face.vertices:
            point = tuple(mesh.vertices[source].co)
            key = (point, region)
            if key not in by_position:
                by_position[key] = len(positions)
                positions.append(point)
                regions.append(region)
            indices.append(by_position[key])
        faces.append(indices)
        swatches.append(swatch)

    runtime = bpy.data.meshes.new("Glassback skin")
    runtime.from_pydata(positions, [], faces)
    runtime.update()
    body.data = runtime
    bpy.data.meshes.remove(mesh)
    runtime.materials.append(bpy.data.materials.new("enemy"))
    uv = runtime.uv_layers.new(name="UVMap").data
    for face, swatch in zip(runtime.polygons, swatches):
        face.use_smooth = swatch == "glassback_umber"
        for index in face.loop_indices:
            point = runtime.vertices[runtime.loops[index].vertex_index].co
            uv[index].uv = uv_for(swatch, (.855 - point.z) / .855)
    runtime.calc_loop_triangles()
    assert 1500 <= len(runtime.loop_triangles) <= 3000
    return regions


def bind(body, arm, regions):
    # As on the Brood, fall off from the nearest eligible bone segments,
    # smooth adjacent weights, and retain the four strongest influences.
    groups = {bone.name: body.vertex_groups.new(name=bone.name)
              for bone in arm.data.bones if bone.use_deform}
    segments = {bone.name: (bone.head_local, bone.tail_local)
                for bone in arm.data.bones if bone.name in groups and bone.name not in SHELLS}
    weights = np.zeros((len(regions), len(groups)))
    for vertex, region in zip(body.data.vertices, regions):
        if region in SHELLS:
            weights[vertex.index, groups[region].index] = 1
            continue

        point = vertex.co
        side = "L" if point.x > 0 else "R"
        distances = {name: distance_to_segment(point, segment)
                     for name, segment in segments.items()
                     if not name.startswith("Leg_") or name.startswith(f"Leg_{side}_")}
        nearest = min(distances.values())
        for name, distance in distances.items():
            weights[vertex.index, groups[name].index] = max(0, 1 - (distance - nearest) / .085)

    neighbors = [[] for _ in regions]
    for edge in body.data.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    for _ in range(3):
        updated = weights.copy()
        for index, adjacent in enumerate(neighbors):
            if regions[index] == "Body" and adjacent:
                updated[index] = .5 * (weights[index] + np.mean(weights[adjacent], axis=0))
        weights = updated

    for index, row in enumerate(weights):
        strongest = sorted(np.flatnonzero(row), key=lambda i: -row[i])[:4]
        assert strongest, f"Unweighted vertex {index}"
        total = sum(row[i] for i in strongest)
        for group in strongest:
            body.vertex_groups[group].add([index], row[group] / total, "REPLACE")
    assert all(np.any(weights[:, group.index]) for group in groups.values())


def pose(arm, legs, *, shift=(0, 0, 0), pitch=0, roll=0, nod=0, walk=None, shell=0, curl=0):
    rest = {bone.name: bone.bone.matrix_local for bone in arm.pose.bones}
    rotation = Quaternion((0, 1, 0), roll) @ Quaternion((1, 0, 0), pitch)
    body = Matrix.Translation(shift) @ turned_about(rest["Body"].translation, rotation)
    matrices = {"Root": rest["Root"], "Body": body @ rest["Body"], "Abdomen": body @ rest["Abdomen"]}
    matrices["Head"] = body @ turned_about(rest["Head"].translation, Quaternion((1, 0, 0), nod)) @ rest["Head"]
    for name, side in SHELLS.items():
        # Cant the hinge so the rear of each case swings outward as it rises.
        hinge = Quaternion((1, 0, -.50 * side), shell)
        matrices[name] = body @ turned_about(rest[name].translation, hinge) @ rest[name]

    for side, index, upper, lower, hip, knee, foot in legs:
        target = foot.copy()
        pole = knee - hip
        if walk is not None:
            phase = (walk + .5 * ((index + (side < 0)) % 2)) % 1
            stride = WALK_SPEED * CLIP_FRAMES["Walk"] / 60
            travel, height = step(phase, stride, .095)
            target.x -= .09 * side
            target.y = hip.y + travel
            target.z += height
        if curl:
            target = body @ (hip + (foot - hip) * (1 - .55 * curl) + Vector((0, 0, .09 * curl)))
            pole = body.to_3x3() @ pole
        start = body @ hip
        bend, end = ik2(start, target, (knee - hip).length, (foot - knee).length, pole)
        if walk is not None and phase <= .5:
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
    braced = dict(shift=(0, 0, -.044), pitch=.07, nod=.12)
    opened = dict(shift=(0, 0, -.025), pitch=.04, shell=radians(50))
    return {
        "Idle": {0: {}, 12: dict(shift=(0, 0, .012), nod=.035),
                 28: dict(shift=(0, 0, .004), nod=-.025), 48: {}},
        "Walk": {f: dict(walk=f / 16, shift=(0, 0, -.065), nod=.035 * cos(f * tau / 8))
                 for f in range(17)},
        "Bite": {0: {}, 4: dict(shift=(0, .05, -.015), nod=-.20),
                 7: dict(shift=(0, -.11, -.04), pitch=.18, nod=.22),
                 11: dict(shift=(0, -.025, -.012), nod=.07), 15: {}},
        "Glint": {0: {}, 5: dict(shift=(0, .015, -.02), nod=-.05), 15: braced},
        "Shine": {0: braced, 5: dict(**braced, roll=.012), 10: dict(**braced, roll=-.012),
                  15: braced, 20: dict(**braced, roll=.012), 25: dict(**braced, roll=-.012), 30: braced},
        "Open": {0: braced, 6: dict(shift=(0, 0, -.036), shell=radians(12)),
                 12: opened, 24: opened, 30: {}},
        "Hit": {0: {}, 2: dict(shift=(0, .065, .024), pitch=-.19, nod=.15),
                6: dict(shift=(0, .023, -.017), pitch=.06), 12: {}},
        "Death": {0: {}, 6: dict(shift=(.02, 0, .13), roll=.8, curl=.25),
                  12: dict(shift=(.08, 0, .44), roll=2.1, curl=.64),
                  21: dict(shift=(.11, 0, .11), roll=pi, curl=.92),
                  30: dict(shift=(.11, 0, .10), roll=pi, curl=1)},
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
    regions = prepare_runtime_mesh(body)
    arm, legs = build_rig(body)
    bind(body, arm, regions)
    animate(arm, legs)
    bpy.context.scene.render.fps = 30
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    build_atlas(str(destination / "Textures"))
    bpy.ops.export_scene.fbx(
        filepath=str(destination / "Glassback.fbx"), use_selection=True,
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
    destination = TOOLS.parents[1] / "Assets/_Project/Art/Enemies"
    path = args.turnaround.resolve() if args.turnaround else None
    if path:
        path.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(prefix="glassback_") as temp:
        directory = Path(temp)
        closed = directory / "Glassback_closed.glb"
        repair(closed)
        body = load_body(closed)
        if path:
            render_turnaround(body, path, directory / "tile.png")
        else:
            export_model(body, destination)


if __name__ == "__main__":
    main()
