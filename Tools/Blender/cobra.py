"""Build the SS-12b model in Blender 4.1, or render the unbound sculpt.

PYTHONHASHSEED=0 blender -b --factory-startup --python-exit-code 1 --python Tools/Blender/cobra.py
Append -- --turnaround Temp/cobra_turnaround.png for the unbound review.
"""

import argparse
from hashlib import sha256
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from beetle import source_markings
from brood import distance_to_segment
from enemy_atlas import build_atlas, uv_for
from scorpion import aimed, armature

SOURCE = TOOLS / "Sources" / "Cobra_src.glb"
SOURCE_SHA256 = "a1c00bb52ed52e43169c51c474f30ccc135bfc355c4b7172446f0025f6bb8edf"
LENGTH = 2.4
ATLAS_PATH = TOOLS.parents[1] / "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png"


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


def fit_body(scene):
    body, = (obj for obj in scene.objects if obj.type == "MESH")
    # The sculpt faces +X; Blender's -Y exports as Unity's +Z.
    rotation = Matrix.Rotation(-np.pi / 2, 4, "Z")
    aligned = [rotation @ body.matrix_world @ vertex.co for vertex in body.data.vertices]
    low = Vector(tuple(min(point[axis] for point in aligned) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in aligned) for axis in range(3)))
    scale = LENGTH / (high.y - low.y)
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    body.data.transform(
        Matrix.Scale(scale, 4) @ Matrix.Translation(-offset) @ rotation @ body.matrix_world
    )
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    body.data.calc_loop_triangles()
    triangles = len(body.data.loop_triangles)
    assert 1500 <= triangles <= 3000, triangles
    print(f"Cobra: {triangles} triangles, dimensions {tuple(body.dimensions)} m", flush=True)
    return body


# The reared neck's centre line in the fitted sculpt (y, z): the anchor on the
# ground, six links up the column, then the head, which already faces -Y.
BEND = ((-.30, .12), (-.55, .11), (-.72, .14), (-.84, .22), (-.92, .34),
        (-.95, .48), (-.96, .62), (-.97, .74))
BEND_HEAD = (-1.20, .78)


def straighten(body):
    """Lay the reared neck flat ahead of the body (owner's turnaround OK, 2026-11-13).

    A temporary chain follows the column; posing its links level and baking
    the deform keeps the hood and head as sculpted, only re-hinged at the neck.
    """
    points = [Vector((0, y, z)) for y, z in BEND] + [Vector((0, *BEND_HEAD))]
    bones = {}
    for index in range(len(points) - 1):
        bones[f"Bend_{index}"] = (f"Bend_{index - 1}" if index else None,
                                  points[index], points[index + 1])
    segments = [(head, tip) for _, head, tip in bones.values()]
    groups = [body.vertex_groups.new(name=name) for name in bones]
    weights = np.zeros((len(body.data.vertices), len(groups)))
    for vertex in body.data.vertices:
        point = vertex.co
        if point.y > BEND[1][0] and point.z < .3:
            weights[vertex.index, 0] = 1
            continue
        distances = np.array([distance_to_segment(point, segment) for segment in segments])
        weights[vertex.index] = np.maximum(0, 1 - (distances - distances.min()) / .08)
    weights = smoothed(body, weights, 3)
    for vertex, row in enumerate(weights):
        row = row / row.sum()
        for index in np.flatnonzero(row > 1e-4):
            groups[index].add([vertex], row[index], "REPLACE")

    arm = armature(bones, body)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    level = Vector((0, -1, 0))
    point = points[1].copy()
    for index in range(1, len(points) - 1):
        bone = arm.pose.bones[f"Bend_{index}"]
        rest = bone.bone.matrix_local
        last = index == len(points) - 2
        if last:
            neck = point.copy()
        direction = rest.to_3x3() @ Vector((0, 1, 0)) if last else level
        bone.matrix = aimed(rest, point, direction)
        bpy.context.view_layer.update()
        point += direction.normalized() * bone.bone.length
    snout = point.copy()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=body.modifiers[-1].name)
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    bpy.data.objects.remove(arm)
    for group in list(body.vertex_groups):
        body.vertex_groups.remove(group)
    return neck, snout


def smoothed(body, weights, passes):
    neighbors = [[] for _ in body.data.vertices]
    for edge in body.data.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    for _ in range(passes):
        result = weights.copy()
        for index, adjacent in enumerate(neighbors):
            if adjacent:
                result[index] = .6 * weights[index] + .4 * weights[adjacent].mean(axis=0)
        weights = result
    return weights


def report_hood(body):
    points = [vertex.co for vertex in body.data.vertices]
    top = max(point.z for point in points)
    hood = [point for point in points if point.z > top * 0.5]
    trunk = [point for point in points if point.z < 0.2 and point.y > -LENGTH / 4]
    width = max(point.x for point in hood) - min(point.x for point in hood)
    trunk_width = max(point.x for point in trunk) - min(point.x for point in trunk)
    head = max(hood, key=lambda point: point.z)
    print(f"Cobra hood: {width:.3f} m across, trunk {trunk_width:.3f} m, "
          f"head top {top:.3f} m at y {head.y:.3f}", flush=True)


def render_turnaround(closed, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    scene = bpy.context.scene
    report_hood(fit_body(scene))
    render_sheet(scene, closed.with_name("tile.png"), path)


def render_sheet(scene, tile, path):
    corners = [obj.matrix_world @ Vector(corner) for obj in scene.objects
               if obj.type == "MESH" for corner in obj.bound_box]
    low = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
    center = (low + high) / 2
    extent = max(high - low)
    print(f"Review bounds: {tuple(low)} to {tuple(high)}", flush=True)

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "AgX"
    scene.world = bpy.data.worlds.new("Review studio")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.22, 0.22, 0.22, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
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
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera

    sheet = np.empty((1024, 1024, 4), np.float32)
    # Front, side, back, top.
    directions = ((0, -1, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    for index, direction in enumerate(directions):
        camera.location = center + Vector(direction) * extent * 4
        camera.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()
        # Enlarge the end views to show the hood and its eye-spots.
        camera_data.ortho_scale = extent * (0.5 if index in (0, 2) else 1.12)
        if index == 3:
            camera.rotation_euler = (0, 0, np.pi)
        scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(tile), check_existing=False)
        pixels = np.empty(512 * 512 * 4, np.float32)
        image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        row = (1 - index // 2) * 512
        column = index % 2 * 512
        sheet[row:row + 512, column:column + 512] = pixels.reshape(512, 512, 4)

    output = bpy.data.images.new("Cobra turnaround", 1024, 1024)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(path)
    output.file_format = "PNG"
    output.save()


HOOD_SPREAD = 0.68


def widen_hood(body, neck):
    """Spread the hood's flaps sideways to HOOD_SPREAD; the neck and head keep their width."""
    inner = .10
    points = [vertex.co for vertex in body.data.vertices]
    flaps = [p for p in points if p.y < neck.y + .25]
    half = max(abs(p.x) for p in flaps)
    scale = LENGTH / (max(p.y for p in points) - min(p.y for p in points))
    stretch = (HOOD_SPREAD / 2 / scale - inner) / (half - inner)
    for point in points:
        reach = smooth(neck.y + .50, neck.y + .30, point.y)
        if abs(point.x) > inner and reach > 0:
            side = 1 if point.x > 0 else -1
            point.x = side * (inner + (abs(point.x) - inner) * (1 + (stretch - 1) * reach))
    print(f"Cobra hood flaps stretched {stretch:.2f}x", flush=True)


def final_fit(body, neck, snout):
    """Scale the flat snake to LENGTH, nose to tail, its belly on the ground."""
    points = [vertex.co for vertex in body.data.vertices]
    low_y, high_y = min(p.y for p in points), max(p.y for p in points)
    scale = LENGTH / (high_y - low_y)
    trunk = [p for p in points if p.y > neck.y + .9]
    offset = Vector((0, (low_y + high_y) / 2, min(p.z for p in trunk)))
    transform = Matrix.Scale(scale, 4) @ Matrix.Translation(-offset)
    body.data.transform(transform)
    body.data.update()
    body.data.calc_loop_triangles()
    size = [max(p[i] for p in points) - min(p[i] for p in points) for i in range(3)]
    print(f"Cobra flat: {len(body.data.loop_triangles)} triangles, "
          f"size {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f} m", flush=True)
    return transform @ neck, transform @ snout


SWATCH_TONES = {"cobra_sand": (.62, .30), "cobra_umber": (.36, .30), "cobra_bone": (.86, .40)}


def apply_atlas(body, colours, neck):
    mesh = body.data
    mesh.materials.clear()
    mesh.materials.append(bpy.data.materials.new("enemy"))
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    uv = mesh.uv_layers.new(name="UVMap").data
    counts = {}
    for face in mesh.polygons:
        red, green, blue = colours[face.index]
        luma = .2126 * red + .7152 * green + .0722 * blue
        saturation = (max(red, green, blue) - min(red, green, blue)) / max(1e-6, max(red, green, blue))
        hood = face.center.y < neck.y + .35
        if luma < .30:
            swatch = "cobra_umber"
        elif luma > .48 and saturation < .38 or face.normal.z < -.5 and not hood:
            # The pale hood front and the belly share the bone ramp.
            swatch = "cobra_bone"
        else:
            swatch = "cobra_sand"
        light, span = SWATCH_TONES[swatch]
        counts[swatch] = counts.get(swatch, 0) + 1
        face.use_smooth = True
        for index in face.loop_indices:
            uv[index].uv = uv_for(swatch, (light - luma) / span)
    print(f"Cobra swatches: {counts}", flush=True)


def report_colour(body):
    """Mirror CobraModel_StandsOffTheDunes on the atlas this build writes."""
    image = bpy.data.images.load(str(ATLAS_PATH), check_existing=False)
    width, height = image.size
    pixels = np.empty(width * height * 4, np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(height, width, 4)
    bpy.data.images.remove(image)
    mesh = body.data
    mesh.calc_loop_triangles()
    uv = mesh.uv_layers.active.data
    area = luma = saturation = 0.0
    for triangle in mesh.loop_triangles:
        u, v = sum((Vector(uv[i].uv) for i in triangle.loops), Vector((0, 0))) / 3
        colour = pixels[min(height - 1, int(v * height)), min(width - 1, int(u * width)), :3]
        area += triangle.area
        luma += triangle.area * float(colour @ np.array((.2126, .7152, .0722)))
        saturation += triangle.area * float((colour.max() - colour.min()) / max(1e-6, colour.max()))
    print(f"Cobra colour: luma {luma / area:.3f} (sand .650), "
          f"saturation {saturation / area:.3f} (sand .481)", flush=True)


SPINE = tuple(f"Spine_{index:02}" for index in range(1, 13))
CLIPS = {"Idle": 48, "Slither": 24, "Rear": 21, "Spit": 9, "Hit": 12, "Death": 30}
# Rear pitches, Spine_01 to Spine_04: the hood's two links stand as one column.
REAR_PITCH = (1.30, 1.30, .60, .20)
# Folded, each flap squeezes onto the neck and tips up off the ground.
FOLD_SQUEEZE, FOLD_LIFT = .66, .35


def section_center(mesh, y):
    intersections = []
    for edge in mesh.edges:
        a, b = (mesh.vertices[index].co for index in edge.vertices)
        if min(a.y, b.y) < y < max(a.y, b.y):
            intersections.append(a.lerp(b, (y - a.y) / (b.y - a.y)))
    return Vector(tuple((min(p[i] for p in intersections) + max(p[i] for p in intersections)) / 2
                        for i in range(3)))


def fitted_rig(body, neck, snout):
    mesh = body.data
    neck = section_center(mesh, neck.y)
    tail = max(mesh.vertices, key=lambda vertex: vertex.co.y).co.copy()
    points = [section_center(mesh, y) - neck for y in np.linspace(neck.y, tail.y, 13)[:-1]]
    points[0] = Vector((0, 0, 0))
    points.append(tail - neck)
    snout = snout - neck
    hinge = points[0].lerp(snout, .45) + Vector((0, 0, -.035))
    bones = {"Root": (None, (0, 0, 0), (0, 0, .15))}
    for index, name in enumerate(SPINE):
        parent = "Root" if index == 0 else SPINE[index - 1]
        bones[name] = (parent, points[index], points[index + 1])
    bones["Head"] = (SPINE[0], points[0].lerp(snout, .25), snout)
    bones["Jaw"] = ("Head", hinge, snout + Vector((0, 0, -.04)))
    # The hood's flaps hinge on the neck line that Spine_01 and Spine_02 share.
    bones["Hood_L"] = (SPINE[0], points[0], points[2])
    bones["Hood_R"] = (SPINE[0], points[0], points[2])
    return bones, neck


def bind(body, bones):
    names = list(bones)[1:]
    spine_names = [name for name in names if not name.startswith("Hood")]
    segments = [(Vector(bones[name][1]), Vector(bones[name][2])) for name in spine_names]
    groups = {name: body.vertex_groups.new(name=name) for name in names}
    weights = np.zeros((len(body.data.vertices), len(names)))
    jaw = spine_names.index("Jaw")
    hood_l, hood_r = names.index("Hood_L"), names.index("Hood_R")
    hinge = Vector(bones["Jaw"][1])
    head = (Vector(bones["Head"][1]), Vector(bones["Head"][2]))
    hood_back = Vector(bones["Hood_L"][2]).y
    for vertex in body.data.vertices:
        point = vertex.co
        distances = np.array([distance_to_segment(point, segment) for segment in segments])
        # The lower snout fades into the hinge; the upper head follows Head.
        jaw_mask = np.clip((hinge.z + .015 - point.z) / .05, 0, 1)
        jaw_mask *= np.clip((hinge.y - point.y + .03) / .10, 0, 1)
        jaw_mask *= np.clip((.10 - distance_to_segment(point, head)) / .04, 0, 1)
        row = np.maximum(0, 1 - (distances - distances.min()) / .095)
        row[jaw] *= jaw_mask
        if row.sum() == 0:
            row[np.argsort(distances)[1]] = 1
        row = row / row.sum()
        # Each flap beyond the neck rides its hood bone, split at the midline.
        flap = smooth(.10, .17, abs(point.x)) * smooth(hood_back + .10, hood_back - .05, point.y)
        full = np.zeros(len(names))
        full[[names.index(name) for name in spine_names]] = row * (1 - flap)
        full[hood_l if point.x > 0 else hood_r] += flap
        weights[vertex.index] = full
    weights = smoothed(body, weights, 2)
    used = set()
    for vertex, row in enumerate(weights):
        strongest = sorted(np.flatnonzero(row > 1e-4), key=lambda index: -row[index])[:4]
        total = row[strongest].sum()
        assert total > 0, vertex
        for index in strongest:
            groups[names[index]].add([vertex], row[index] / total, "REPLACE")
        used.update(strongest)
    assert len(used) == len(names), "Every deform bone needs vertices"


def smooth(start, end, frame):
    t = min(1, max(0, (frame - start) / (end - start)))
    return t * t * (3 - 2 * t)


def motion(name, frame):
    """Return rear, hood fold, head jab, jaw, flinch and body roll."""
    if name == "Rear":
        return smooth(0, 15, frame), 1 - smooth(2, 15, frame), 0, 0, 0, 0
    if name == "Spit":
        jab = smooth(0, 3, frame) * (1 - smooth(4, 9, frame))
        jaw = .5 * smooth(1, 3, frame) * (1 - smooth(5, 8, frame))
        return 1, 0, jab, jaw, 0, 0
    if name == "Hit":
        return 0, 1, 0, 0, smooth(0, 3, frame) * (1 - smooth(3, 12, frame)), 0
    if name == "Death":
        # Frame 0 is the bind pose: the importer takes the first clip's first
        # frame as the model's default. The body jolts up, falls flat, the hood
        # folds and it rolls limp.
        jolt = np.sin(np.pi * smooth(0, 16, frame))
        return .4 * jolt, smooth(4, 22, frame), 0, .2 * jolt, 0, .5 * smooth(10, 28, frame)
    return 0, 1, 0, 0, 0, 0


def pose(arm, rest_joints, name, frame):
    rear, fold, jab, jaw, flinch, roll = motion(name, frame)
    phase = 2 * np.pi * frame / CLIPS[name]
    forward = Vector((0, -1, 0))
    joints = [joint.copy() for joint in rest_joints]
    if rear > 0:
        # Plant Spine_05 and lift the four links ahead of it into a column.
        for index in range(3, -1, -1):
            link = rest_joints[index] - rest_joints[index + 1]
            pitch = REAR_PITCH[index] * rear
            joints[index] = joints[index + 1] + Matrix.Rotation(-pitch, 3, "X") @ link
    for index in range(4):
        joints[index] += forward * .2 * jab * (1 - index / 4)
    if name in ("Idle", "Hit", "Slither"):
        if name == "Idle":
            joints[0].z += .018 * (1 - np.cos(phase))
        joints[0].y += .12 * flinch
        point = joints[0].copy()
        arc = 0.0
        for index in range(len(SPINE)):
            link = rest_joints[index + 1] - rest_joints[index]
            yaw = .29 * flinch * (1 - index / len(SPINE))
            if name == "Slither":
                # A 24-frame cycle travels 1.92 m at the nominal 2.4 m/s; the
                # hood's two links stay nearly still so the flaps ride with them.
                wave = phase - 2 * np.pi * arc / (2.4 * CLIPS[name] / 30)
                yaw += .46 * min(1, index / 3) * np.sin(wave)
            arc += link.length
            joints[index + 1] = point + Matrix.Rotation(yaw, 3, "Z") @ link
            point = joints[index + 1]
    for index, bone_name in enumerate(SPINE):
        bone = arm.pose.bones[bone_name]
        rest = bone.bone.matrix_local
        bone.matrix = aimed(rest, joints[index], joints[index + 1] - joints[index]) @ \
            Matrix.Rotation(roll, 4, "Y")
        bpy.context.view_layer.update()
    head = arm.pose.bones["Head"]
    rest = head.bone.matrix_local
    direction = rest.to_3x3() @ Vector((0, 1, 0))
    direction.z += .11 * flinch
    head.matrix = aimed(rest, joints[0] + rest.translation - rest_joints[0], direction) @ \
        Matrix.Rotation(roll, 4, "Y")
    bpy.context.view_layer.update()
    arm.pose.bones["Jaw"].rotation_quaternion = Quaternion((1, 0, 0), jaw)
    for side, bone_name in ((1, "Hood_L"), (-1, "Hood_R")):
        flap = arm.pose.bones[bone_name]
        flap.rotation_quaternion = Quaternion((0, 1, 0), -side * FOLD_LIFT * fold)
        flap.scale = Vector((1 - FOLD_SQUEEZE * fold, 1, 1))
    bpy.context.view_layer.update()


def animate(arm):
    arm.animation_data_create()
    for bone in arm.pose.bones:
        bone.rotation_mode = "QUATERNION"
    rest_joints = [arm.pose.bones[name].bone.head_local.copy() for name in SPINE]
    rest_joints.append(arm.pose.bones[SPINE[-1]].bone.tail_local.copy())
    for name, length in CLIPS.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame in range(length + 1):
            pose(arm, rest_joints, name, frame)
            for bone in arm.pose.bones:
                if bone.name == "Root":
                    continue
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert("location", frame=frame, group=bone.name)
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
                if bone.name.startswith("Hood"):
                    bone.keyframe_insert("scale", frame=frame, group=bone.name)
        action.frame_range = (0, length)
        action.use_frame_range = True
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def measure(arm, body, name, frame):
    """Hood width and Head height in a clip frame, as the Unity tests read them."""
    arm.animation_data.action = bpy.data.actions[name]
    bpy.context.scene.frame_set(frame)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(depsgraph)
    deformed = evaluated.to_mesh()
    hood = {group.index for group in body.vertex_groups if group.name.startswith("Hood")}
    xs = [vertex.co.x for vertex, source in zip(deformed.vertices, body.data.vertices)
          if sum(g.weight for g in source.groups if g.group in hood) >= .5]
    zs = [vertex.co.z for vertex in deformed.vertices]
    head = (arm.matrix_world @ arm.pose.bones["Head"].head).z
    evaluated.to_mesh_clear()
    arm.animation_data.action = None
    return max(xs) - min(xs), head, min(zs), max(zs)


def export_model(closed, review=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    scene = bpy.context.scene
    body = fit_body(scene)
    body.name = "Cobra_Body"
    bpy.context.view_layer.objects.active = body
    if body.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    mesh = bmesh.new()
    mesh.from_mesh(body.data)
    bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=1e-6)
    assert all(edge.is_manifold for edge in mesh.edges), "The repaired mesh must stay closed"
    mesh.to_mesh(body.data)
    mesh.free()
    body.data.update()
    markings = source_markings(body.data)
    colours = [markings(face.center) for face in body.data.polygons]
    neck, snout = straighten(body)
    widen_hood(body, neck)
    neck, snout = final_fit(body, neck, snout)
    apply_atlas(body, colours, neck)
    bones, neck = fitted_rig(body, neck, snout)
    body.data.transform(Matrix.Translation(-neck))
    bind(body, bones)
    arm = armature(bones, body)
    arm.name = arm.data.name = "Rig_Viper"
    scene.render.fps = 30
    animate(arm)
    build_atlas(str(ATLAS_PATH.parent))
    report_colour(body)
    for name, frame in (("Idle", 0), ("Rear", CLIPS["Rear"])):
        hood, head, low, high = measure(arm, body, name, frame)
        print(f"Cobra {name}: hood {hood:.3f} m, head {head:.3f} m, z {low:.3f}..{high:.3f}",
              flush=True)
    if review:
        render_clips(arm, scene, review)
        return
    # Hand the exporter the bind pose; the model's default comes from Death's frame 0.
    scene.frame_set(0)
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(ATLAS_PATH.parents[1] / "Cobra.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype="NULL",
        bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_simplify_factor=0.0, path_mode="STRIP",
    )


def render_clips(arm, scene, path):
    """Clip stills for review (uncommitted): Idle, Rear's end, Spit's jab, Death's end."""
    material = bpy.data.materials["enemy"]
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(ATLAS_PATH))
    texture.interpolation = "Closest"
    material.node_tree.links.new(texture.outputs["Color"],
                                 material.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    for name, frame in (("Idle", 0), ("Rear", CLIPS["Rear"]), ("Spit", 3), ("Death", 30)):
        arm.animation_data.action = bpy.data.actions[name]
        scene.frame_set(frame)
        render_sheet(scene, path.with_name("tile.png"), path.with_name(f"{path.stem}_{name}.png"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--turnaround", type=Path)
    parser.add_argument("--review", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    path = args.turnaround.resolve() if args.turnaround else None
    temporary_root = path.parent if path else TOOLS.parents[1] / "Temp"
    temporary_root.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=temporary_root) as temporary:
        closed = Path(temporary) / "cobra_closed.glb"
        repair(closed)
        if path:
            render_turnaround(closed, path)
        else:
            export_model(closed, args.review.resolve() if args.review else None)


if __name__ == "__main__":
    main()
