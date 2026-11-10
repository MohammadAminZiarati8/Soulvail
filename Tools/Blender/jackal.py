"""Build the SS-05b body and sling in Blender 4.1.

PYTHONHASHSEED=0 blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/jackal.py
    [-- --turnaround Temp/jackal_turnaround.png | --pose-sheet Temp/jackal_poses.png]

Turnaround renders the repaired source with its baked colour and skips export.
Pose-sheet mode builds the shipping assets and previews the shared KayKit clips.
"""

import argparse
from collections import defaultdict
from hashlib import sha256
from math import cos, pi, sin
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from enemy_atlas import build_atlas, uv_for
from mossguard import reduce, segment_distance
from stalker import texture_sampler, textured

SOURCE = TOOLS / "Sources" / "Jackal_src.glb"
SOURCE_SHA256 = "76add7e3f7c149567ba04a2f82a89aabd914ebad904d8ec7fa1225ed961d047b"
ART = ROOT / "Assets/_Project/Art/Enemies"
ATLAS = ART / "Textures/T_Enemy_Albedo.png"
KAYKIT = ROOT / "Assets/ThirdParty/KayKit"
RIG = KAYKIT / "Skeletons/Characters/Skeleton_Minion.fbx"
CLIP_GROUPS = (
    ("General", ("Idle_B", "Throw", "Hit_A", "Death_A")),
    ("MovementBasic", ("Walking_B",)),
    ("MovementAdvanced", ("Walking_Backwards",)),
    ("CombatRanged", ("Ranged_1H_Aiming",)),
)
POSES = ("Idle_B", "Walking_B", "Walking_Backwards", "Ranged_1H_Aiming", "Throw", "Hit_A", "Death_A")


def repair(destination):
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    if actual != SOURCE_SHA256:
        raise ValueError(f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}")
    subprocess.run([
        bpy.app.binary_path, "-b", "--factory-startup", "-t", "8",
        "--python-exit-code", "1", "-P", str(TOOLS / "repair_scan.py"),
        "--", str(SOURCE), str(destination), "--texture-size", "1024",
    ], check=True)


def mesh_bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    return (
        Vector(tuple(min(point[axis] for point in points) for axis in range(3))),
        Vector(tuple(max(point[axis] for point in points) for axis in range(3))),
    )


def load_body(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ART / "Mossguard.fbx"), use_anim=False)
    low, high = mesh_bounds(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    height = high.z - low.z

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    body, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    low, high = mesh_bounds([body])
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    scale = height / (high.z - low.z)
    body.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-offset) @ body.matrix_world)
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    print(f"Jackal review bounds in metres: {tuple(body.dimensions)}", flush=True)
    return body, height


def build_sling(preview=False):
    """Closed cord and pouch shells with the pinch grip at the mesh origin."""
    vertices, faces = [], []

    def add_ring(points):
        ring = tuple(range(len(vertices), len(vertices) + len(points)))
        vertices.extend(points)
        return ring

    def bridge(first, second):
        for i in range(len(first)):
            j = (i + 1) % len(first)
            faces.append((first[i], first[j], second[j], second[i]))

    for side in (-1, 1):
        path = [Vector((side * x, 0, z)) for x, z in (
            (.007, 0), (.037, -.13), (.063, -.32), (.074, -.46), (.060, -.54),
        )]
        rings = []
        for i, center in enumerate(path):
            tangent = (path[min(i + 1, len(path) - 1)] - path[max(0, i - 1)]).normalized()
            right = Vector((0, 1, 0)).cross(tangent)
            rings.append(add_ring([
                center + .008 * (cos(step * pi / 3) * right + sin(step * pi / 3) * Vector((0, 1, 0)))
                for step in range(6)
            ]))
        faces.append(tuple(reversed(rings[0])))
        for first, second in zip(rings, rings[1:]):
            bridge(first, second)
        faces.append(rings[-1])

    # The leather cup opens toward +Y, with a raised rim and a thickened underside.
    rings = [
        add_ring([
            (.081 * scale * cos(i * pi / 6), depth, -.538 - .055 * scale * sin(i * pi / 6))
            for i in range(12)
        ])
        for scale, depth in ((.55, -.031), (1, -.004), (.86, .003), (.50, -.022))
    ]
    faces.append(tuple(reversed(rings[0])))
    for first, second in zip(rings, rings[1:]):
        bridge(first, second)
    faces.append(rings[-1])

    mesh = bpy.data.meshes.new("Sling")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.calc_loop_triangles()
    assert len(mesh.loop_triangles) <= 300
    sling = bpy.data.objects.new("JackalSling", mesh)
    bpy.context.scene.collection.objects.link(sling)
    if preview:
        leather = bpy.data.materials.new("Dark leather preview")
        leather.use_nodes = True
        leather.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.13, .075, .041, 1)
        leather.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = .85
        mesh.materials.append(leather)
    else:
        paint(sling, lambda _: ("jackal_leather", .26))

    stone = bpy.data.objects.new("Stone", None)
    bpy.context.scene.collection.objects.link(stone)
    stone.parent = sling
    stone.location = (0, -.007, -.538)
    print(f"Sling: {len(mesh.loop_triangles)} triangles", flush=True)
    return sling


def add_label(camera, position, size):
    text = bpy.data.curves.new("View label", "FONT")
    text.size = size
    text.align_y = "CENTER"
    obj = bpy.data.objects.new(text.name, text)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = camera
    obj.location = position
    material = bpy.data.materials.new("Label")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (.77, .82, .86, 1)
    material.node_tree.links.new(emission.outputs[0], nodes.new("ShaderNodeOutputMaterial").inputs["Surface"])
    text.materials.append(material)
    return text


def light_studio(scene, height):
    scene.render.engine = "CYCLES"
    scene.cycles.use_denoising = True
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.world = bpy.data.worlds.new("Turnaround studio")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (.075, .083, .095, 1)
    background.inputs["Strength"].default_value = .8

    center = Vector((0, 0, height / 2))
    for direction, power in (((-2, -3, 4), 240), ((2, 3, 4), 220), ((2, -1, 1), 100)):
        data = bpy.data.lights.new("Studio area", "AREA")
        data.energy = power * height ** 2
        data.size = 1.5 * height
        light = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(light)
        light.location = center + height * Vector(direction)
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()


def render_turnaround(closed, destination):
    body, height = load_body(closed)
    sling = build_sling(preview=True)
    scene = bpy.context.scene
    light_studio(scene, height)
    scene.cycles.samples = 24
    cell = 640
    scene.render.resolution_x = scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100
    center = Vector((0, 0, height / 2))
    camera_data = bpy.data.cameras.new("Orthographic")
    camera_data.type = "ORTHO"
    span = max(body.dimensions) * 1.42
    camera_data.ortho_scale = span
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = add_label(camera, (-.45 * span, .42 * span, -1), .044 * span)

    sheet = np.empty((2 * cell, 2 * cell, 4), np.float32)
    tile = closed.with_name("tile.png")
    views = (("Front", (0, -1, 0)), ("Side", (1, 0, 0)), ("Back", (0, 1, 0)), ("Top", (0, 0, 1)))
    for index, (name, direction) in enumerate(views):
        camera.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()
        if name == "Top":
            camera.rotation_euler = (0, 0, 0)
        rotation = camera.rotation_euler.to_quaternion()
        camera.location = center + Vector(direction) * span * 4 + rotation @ Vector((span * .06, 0, 0))
        label.body = name
        # Keep the loose sling beside the sculpt with its cup facing the viewer.
        sling.rotation_mode = "QUATERNION"
        sling.rotation_quaternion = rotation @ Euler((.87, -.25, pi + .22)).to_quaternion()
        sling.location = center + rotation @ Vector((span * .40, height * .12, 0))
        scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(tile), check_existing=False)
        pixels = np.empty(cell * cell * 4, np.float32)
        image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        row = (1 - index // 2) * cell
        col = (index % 2) * cell
        sheet[row:row + cell, col:col + cell] = pixels.reshape(cell, cell, 4)

    output = bpy.data.images.new("Jackal turnaround", 2 * cell, 2 * cell, alpha=False)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(destination)
    output.file_format = "PNG"
    output.save()


def paint(obj, choose_swatch):
    mesh = obj.data
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    uv = mesh.uv_layers.new(name="UVMap").data
    mesh.materials.clear()
    mesh.materials.append(bpy.data.materials.get("enemy") or bpy.data.materials.new("enemy"))
    swatches = [choose_swatch(face.center) for face in mesh.polygons]
    faces_by_edge = defaultdict(list)
    for face in mesh.polygons:
        for edge in face.edge_keys:
            faces_by_edge[edge].append(face.index)

    # Reject isolated flecks from the colour bake without smoothing across a cloth or leather seam.
    cleaned = swatches.copy()
    for face in mesh.polygons:
        neighbours = [swatches[index] for edge in face.edge_keys for index in faces_by_edge[edge]
                      if index != face.index]
        if neighbours and all(swatch == neighbours[0][0] for swatch, _ in neighbours):
            cleaned[face.index] = (neighbours[0][0], sum(shade for _, shade in neighbours) / len(neighbours))

    shades = defaultdict(list)
    for face, (swatch, shade) in zip(mesh.polygons, cleaned):
        for vertex in face.vertices:
            shades[vertex, swatch].append(shade)
    for face in mesh.polygons:
        swatch, _ = cleaned[face.index]
        for index in face.loop_indices:
            corner = shades[mesh.loops[index].vertex_index, swatch]
            uv[index].uv = uv_for(swatch, sum(corner) / len(corner))
        face.use_smooth = True


def body_palette(sample, point):
    r, g, b = sample(point)
    luma = .2126 * r + .7152 * g + .0722 * b
    kilt = .40 < point.z < .63 and (point.y < .065 or point.y > .161)
    if luma > (.59 if kilt else .63) and b > .67 * r:
        return "jackal_bone", float(np.interp(luma, (.63, .93), (.78, .12)))
    if luma < .39:
        return "jackal_leather", float(np.interp(luma, (.08, .39), (.93, .10)))
    return "jackal_ochre", float(np.interp(luma, (.39, .82), (.92, .10)))


def runtime_mesh(high):
    sample = texture_sampler(high)
    body = reduce(high, 2960, voxel=.008)
    body.name = "Jackal_Body"
    bm = bmesh.new()
    bm.from_mesh(body.data)
    assert all(edge.is_manifold for edge in bm.edges), "The runtime shell must stay closed"
    bm.free()
    bpy.context.view_layer.update()
    paint(body, lambda point: body_palette(sample, point))
    bpy.data.objects.remove(high, do_unlink=True)
    body.data.calc_loop_triangles()
    count = len(body.data.loop_triangles)
    assert 1500 <= count <= 3000, count
    print(f"Jackal body: {count} triangles", flush=True)
    return body


def smoothstep(low, high, value):
    t = min(1, max(0, (value - low) / (high - low)))
    return t * t * (3 - 2 * t)


def distance_weights(point, names, heads, tails):
    nearest = sorted(
        (segment_distance(point, heads[name], tails[name]), name) for name in names
    )[:2]
    return {name: 1 / (distance + .035) ** 4 for distance, name in nearest}


def leg_weights(source, leg, x, z):
    # Cloth spans both legs. Its centre follows both thighs, with a smooth
    # transition to the near thigh at the outer hem and to hips at the waist.
    hem = smoothstep(.335, .435, source.z)
    belt = smoothstep(.52, .76, source.z)
    right_to_left = smoothstep(-.18, .18, x)
    thigh = hem * (1 - belt)
    shin = (1 - hem) * smoothstep(.105, .20, z)
    return {
        "hips": hem * belt,
        "upperleg.l": thigh * right_to_left,
        "upperleg.r": thigh * (1 - right_to_left),
        "lowerleg." + leg: shin,
        "foot." + leg: (1 - hem) - shin,
    }


def fit_and_skin(body, arm, height):
    heads = {bone.name: arm.matrix_world @ bone.head_local for bone in arm.data.bones}
    tails = {bone.name: arm.matrix_world @ bone.tail_local for bone in arm.data.bones}
    groups = {name: body.vertex_groups.new(name=name) for name in heads}
    rig_space = arm.matrix_world.inverted()
    for vertex in body.data.vertices:
        s = vertex.co.copy()
        side = "l" if s.x >= 0 else "r"
        ax = abs(s.x)
        z = float(np.interp(s.z, (0, .125, .365, .71, 1.075, 1.16, height),
                            (0, heads["foot.l"].z, heads["lowerleg.l"].z, .59, 1.15, 1.22, height)))
        point = Vector((s.x, s.y - .115, z))
        if s.z < .78:
            # Widen the narrow calves to KayKit's stance without splitting the skirt.
            widening = (1 - smoothstep(.29, .49, s.z)) * .026
            point.x += widening * (2 * smoothstep(-.10, .10, s.x) - 1)
            weights = leg_weights(s, side, point.x, z)
            spine = smoothstep(.71, .78, s.z)
            weights["hips"] *= 1 - spine
            weights["spine"] = spine
        elif ax > .17 and .89 < s.z < 1.12:
            # The sculpt's shoulder and wrist line sit low and the arm span is
            # shorter. Align the mesh to the rest bones, tapering at the cape.
            blend = smoothstep(.17, .31, ax) * smoothstep(.89, .94, s.z)
            x = float(np.interp(ax, (.17, .25, .38, .58, .744), (.17, .25, .455, .787, .96)))
            point.x = x if side == "l" else -x
            y_center = float(np.interp(ax, (.25, .37, .55, .68, .744), (.11, .105, .063, .038, .04)))
            z_center = float(np.interp(ax, (.25, .38, .55, .68, .744), (1.00, .985, .972, .97, .97)))
            aligned = Vector((point.x, s.y - y_center, heads["upperarm.l"].z + s.z - z_center))
            # Lay the palm over KayKit's offset grip without changing the slot or the skeleton.
            aligned.z -= .038 * smoothstep(.58, .68, ax)
            point = Vector((s.x, point.y, point.z)).lerp(aligned, blend)
            names = ["upperarm." + side, "lowerarm." + side, "wrist." + side, "hand." + side]
            weights = distance_weights(point, names, heads, tails)
            total = sum(weights.values())
            weights = {name: weight * blend / total for name, weight in weights.items()}
            weights["chest"] = 1 - blend
        else:
            head = smoothstep(1.065, 1.13, s.z)
            chest = smoothstep(.81, .975, s.z)
            weights = {"head": head, "chest": (1 - head) * chest, "spine": (1 - head) * (1 - chest)}
        strongest = sorted(((weight, name) for name, weight in weights.items() if weight > .001),
                           reverse=True)[:4]
        total = sum(weight for weight, _ in strongest)
        assert total > 0
        for weight, name in strongest:
            groups[name].add([vertex.index], weight / total, "REPLACE")
        vertex.co = rig_space @ point
    body.parent = arm
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    modifier = body.modifiers.new("Rig_Medium", "ARMATURE")
    modifier.object = arm
    body.data.update()


def export(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", use_mesh_modifiers=False,
        add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype="NULL",
        bake_anim=False, path_mode="STRIP",
    )


def build_assets(closed):
    high, height = load_body(closed)
    body = runtime_mesh(high)
    bpy.ops.import_scene.fbx(filepath=str(RIG), use_anim=False)
    arm, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    assert arm.name == "Rig_Medium" and len(arm.data.bones) == 23
    fit_and_skin(body, arm, height)
    export(ART / "Jackal.fbx", [arm, body])
    sling = build_sling()
    export(ART / "JackalSling.fbx", [sling, *sling.children])
    build_atlas(str(ATLAS.parent))


def load_poses():
    actions = {}
    for group, names in CLIP_GROUPS:
        objects_before = set(bpy.data.objects)
        actions_before = set(bpy.data.actions)
        path = KAYKIT / "Animations/Rig_Medium" / f"Rig_Medium_{group}.fbx"
        bpy.ops.import_scene.fbx(filepath=str(path))
        for obj in set(bpy.data.objects) - objects_before:
            bpy.data.objects.remove(obj, do_unlink=True)
        for action in set(bpy.data.actions) - actions_before:
            name = action.name.split("|")[-1]
            if name in names:
                actions[name] = action
            else:
                bpy.data.actions.remove(action)
    assert actions.keys() == set(POSES), sorted(actions)
    return actions


def render_poses(destination, tile):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name in ("Jackal", "JackalSling"):
        bpy.ops.import_scene.fbx(filepath=str(ART / f"{name}.fbx"), use_anim=False)
    arm, body, sling = (bpy.data.objects[name] for name in ("Rig_Medium", "Jackal_Body", "JackalSling"))
    actions = load_poses()
    arm.animation_data_create()
    atlas = bpy.data.images.load(str(ATLAS))
    for obj in (body, sling):
        textured(obj, atlas)
        shader = obj.data.materials[0].node_tree.nodes["Principled BSDF"]
        shader.inputs["Roughness"].default_value = .9
    scene = bpy.context.scene
    light_studio(scene, 1.5)
    scene.cycles.samples = 18
    cell = 320
    scene.render.resolution_x = scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100
    camera_data = bpy.data.cameras.new("Pose camera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 2.34
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = add_label(camera, (-1.05, .97, -1), .09)
    sheet = np.empty((len(POSES) * cell, 4 * cell, 4), np.float32)
    target = Vector((0, 0, .72))
    slot = arm.pose.bones["handslot.r"]
    grip = Matrix.Rotation(pi / 4, 4, "Z") @ Matrix.Rotation(-pi / 2, 4, "X")
    directions = ((-1.1, -4, 1.15), (-2.4, -3.5, 1.4), (-1.1, -4, 1.15), (.8, -1.2, 4))
    for row, name in enumerate(POSES):
        label.body = name.replace("_", " ")
        action = actions[name]
        arm.animation_data.action = action
        # KayKit animates a weapon drop late in Death_A. Sample its held poses.
        fractions = (.10, .27, .39, .34) if name == "Death_A" else (.12, .45, .76, .45)
        for col, (fraction, eye) in enumerate(zip(fractions, directions)):
            first, last = action.frame_range
            scene.frame_set(round(first + (last - first) * fraction))
            sling.matrix_world = arm.matrix_world @ slot.matrix @ grip
            direction = Vector(eye)
            camera.location = target + direction
            camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(tile)
            bpy.ops.render.render(write_still=True)
            image = bpy.data.images.load(str(tile), check_existing=False)
            pixels = np.empty(cell * cell * 4, np.float32)
            image.pixels.foreach_get(pixels)
            bpy.data.images.remove(image)
            y = (len(POSES) - row - 1) * cell
            sheet[y:y + cell, col * cell:(col + 1) * cell] = pixels.reshape(cell, cell, 4)
            label.body = ""
    output = bpy.data.images.new("Jackal pose sheet", 4 * cell, len(POSES) * cell, alpha=False)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(destination)
    output.file_format = "PNG"
    output.save()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--turnaround", type=Path)
    mode.add_argument("--pose-sheet", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    requested = args.turnaround or args.pose_sheet
    destination = requested.resolve() if requested else None
    temporary_root = destination.parent if destination else ROOT / "Temp"
    temporary_root.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=temporary_root) as temporary:
        closed = Path(temporary) / "jackal_closed.glb"
        repair(closed)
        if args.turnaround:
            render_turnaround(closed, destination)
        else:
            build_assets(closed)
            if args.pose_sheet:
                render_poses(destination, closed.with_name("pose.png"))


if __name__ == "__main__":
    main()
