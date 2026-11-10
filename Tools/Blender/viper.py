"""Build the SS-08b model in Blender 4.1, or render the unbound sculpt.

PYTHONHASHSEED=0 blender -b --factory-startup --python-exit-code 1 --python Tools/Blender/viper.py
Append -- --turnaround Temp/viper_turnaround.png for the unbound review.
"""

import argparse
from hashlib import sha256
from math import cos, pi, sin
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

SOURCE = TOOLS / "Sources" / "Viper_src.glb"
SOURCE_SHA256 = "4a2bdfeb1ec8e83eb2f2c093680f157c7672902ea3cf8539c59507e316808598"


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
    points = [body.matrix_world @ vertex.co for vertex in body.data.vertices]
    # Blender's -Y exports as Unity's +Z. Use the head and tail tip sections
    # of the repaired scan to fit the chord while keeping the sculpt's bow.
    nose_x = 0.1844
    tail_x = -0.2216
    forward = Vector((nose_x - tail_x, min(p.y for p in points) - max(p.y for p in points), 0))
    rotation = Matrix.Rotation(Vector((0, -1)).angle_signed(forward.xy), 4, "Z")
    aligned = [rotation @ point for point in points]
    low = Vector(tuple(min(point[axis] for point in aligned) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in aligned) for axis in range(3)))
    scale = 2.6 / (high.y - low.y)
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    # Fit the six-to-one length-to-width requirement while keeping the bow.
    width_scale = min(scale, 0.43 / (high.x - low.x))
    dimensions = Matrix.Diagonal((width_scale, scale, scale, 1))
    body.data.transform(
        dimensions @ Matrix.Translation(-offset) @ rotation @ body.matrix_world
    )
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    body.data.calc_loop_triangles()
    triangles = len(body.data.loop_triangles)
    assert 1500 <= triangles <= 3000, triangles
    print(f"Viper: {triangles} triangles, dimensions {tuple(body.dimensions)} m", flush=True)

def render_turnaround(closed, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    scene = bpy.context.scene
    fit_body(scene)
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
    tile = closed.with_name("tile.png")
    directions = ((0, -1, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    for index, direction in enumerate(directions):
        camera.location = center + Vector(direction) * extent * 4
        camera.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()
        # Enlarge the end views to show the eyes and the tail profile.
        camera_data.ortho_scale = extent * (0.36 if index in (0, 2) else 1.12)
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

    output = bpy.data.images.new("Viper turnaround", 1024, 1024)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(path)
    output.file_format = "PNG"
    output.save()


# Landmarks in repaired scan space. The tail bends laterally before curling


CLIPS = {
    "Idle": 48, "Slither": 24, "Sidewind": 24, "Coil": 27, "Strike": 12,
    "Recoil": 30, "Stretched": 60, "Hit": 12, "Death": 30,
}
SPINE = tuple(f"Spine_{index:02}" for index in range(1, 13))


def section_center(mesh, y):
    intersections = []
    for edge in mesh.edges:
        a, b = (mesh.vertices[index].co for index in edge.vertices)
        if min(a.y, b.y) < y < max(a.y, b.y):
            intersections.append(a.lerp(b, (y - a.y) / (b.y - a.y)))
    return Vector(tuple((min(p[i] for p in intersections) + max(p[i] for p in intersections)) / 2
                        for i in range(3)))


def fitted_rig(body):
    mesh = body.data
    # The neck sits just behind the sculpted head. Fit the spine on sections
    # from there to the taper, keeping its lateral and vertical rest curvature.
    neck = section_center(mesh, -.86)
    tail = max(mesh.vertices, key=lambda vertex: vertex.co.y).co.copy()
    points = [section_center(mesh, y) - neck for y in np.linspace(neck.y, tail.y, 13)[:-1]]
    points.append(tail - neck)
    snout = section_center(mesh, -1.27) - neck
    hinge = section_center(mesh, -1.04) - neck + Vector((0, 0, -.045))
    jaw_tip = snout + Vector((0, 0, -.055))
    bones = {"Root": (None, (0, 0, 0), (0, 0, .15))}
    for index, name in enumerate(SPINE):
        parent = "Root" if index == 0 else SPINE[index - 1]
        bones[name] = (parent, points[index], points[index + 1])
    bones["Head"] = (SPINE[0], points[0].lerp(snout, .25), snout)
    bones["Jaw"] = ("Head", hinge, jaw_tip)
    return bones, neck


def bind(body, bones):
    segments = [(Vector(head), Vector(tip)) for _, head, tip in list(bones.values())[1:]]
    groups = [body.vertex_groups.new(name=name) for name in list(bones)[1:]]
    weights = np.zeros((len(body.data.vertices), len(groups)))
    jaw = groups[-1].index
    hinge = bones["Jaw"][1]
    for vertex in body.data.vertices:
        point = vertex.co
        distances = np.array([distance_to_segment(point, segment) for segment in segments])
        # Fade the lower snout into the hinge. The upper head follows Head.
        jaw_mask = np.clip((hinge.z + .015 - point.z) / .060, 0, 1)
        jaw_mask *= np.clip((hinge.y - point.y + .03) / .11, 0, 1)
        if jaw_mask == 0:
            distances[jaw] = np.inf
        weights[vertex.index] = np.maximum(0, 1 - (distances - distances.min()) / .095)
        weights[vertex.index, jaw] *= jaw_mask

    neighbors = [[] for _ in body.data.vertices]
    for edge in body.data.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    for _ in range(2):
        smoothed = weights.copy()
        for index, adjacent in enumerate(neighbors):
            if adjacent:
                smoothed[index] = .6 * weights[index] + .4 * weights[adjacent].mean(axis=0)
        weights = smoothed

    used = set()
    for vertex, row in enumerate(weights):
        strongest = sorted(np.flatnonzero(row), key=lambda index: -row[index])[:4]
        total = row[strongest].sum()
        assert total > 0, vertex
        for index in strongest:
            groups[index].add([vertex], row[index] / total, "REPLACE")
        used.update(strongest)
    assert len(used) == len(groups), "Every deform bone needs vertices"


def apply_atlas(body, markings):
    mesh = body.data
    mesh.materials.clear()
    mesh.materials.append(bpy.data.materials.new("enemy"))
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    uv = mesh.uv_layers.new(name="UVMap").data
    for face in mesh.polygons:
        red, green, blue = markings(face.center)
        luma = .2126 * red + .7152 * green + .0722 * blue
        if face.normal.z < -.42 and face.center.z < .125:
            swatch, tone = "viper_belly", (.59 - luma) / .37
        elif luma < .39:
            swatch, tone = "viper_umber", (.39 - luma) / .34
        else:
            swatch, tone = "viper_ochre", (.64 - luma) / .30
        face.use_smooth = True
        for index in face.loop_indices:
            uv[index].uv = uv_for(swatch, tone)


def smooth(start, end, frame):
    t = min(1, max(0, (frame - start) / (end - start)))
    return t * t * (3 - 2 * t)


def motion(name, frame):
    """Return coil, straighten, neck retreat, body roll, jaw and flinch."""
    if name == "Coil":
        coil = smooth(0, 20, frame)
        return coil, 0, .32 * coil, 0, 0, 0
    if name == "Strike":
        extend = smooth(0, 5, frame)
        return 1 - extend, extend, .32 * (1 - extend), 0, .52 * extend, 0
    if name == "Recoil":
        retract = smooth(0, 23, frame)
        return .24 * sin(pi * retract), 1 - retract, .19 * sin(pi * retract), 0, .52 * (1 - retract), 0
    if name == "Stretched":
        retract = smooth(45, 60, frame)
        return 0, 1 - retract, 0, 0, .52 * (1 - retract), 0
    if name == "Hit":
        flinch = smooth(0, 3, frame) * (1 - smooth(3, 12, frame))
        return .12 * flinch, 0, .14 * flinch, .18 * flinch, 0, flinch
    if name == "Death":
        turn = smooth(0, 24, frame)
        return .19 * turn, 0, .09 * turn, pi * turn, .23 * turn, 0
    return 0, 0, 0, 0, 0, 0


COIL_YAW = (.4, 1.2, 1.55, 1.55, 1.2, .4, -.6, -1.3, -1.55, -1.55, -1.2, -.4)


def pose(arm, name, frame):
    coil, straight, retreat, roll, jaw, flinch = motion(name, frame)
    phase = 2 * pi * frame / CLIPS[name]
    head_shift = Vector((0, retreat, 0))
    if name == "Idle":
        head_shift.z = .018 * (1 - cos(phase))
    point = head_shift.copy()
    arc_distance = 0.0
    for index, bone_name in enumerate(SPINE):
        bone = arm.pose.bones[bone_name]
        rest = bone.bone.matrix_local
        axis = rest.to_3x3() @ Vector((0, 1, 0))
        direction = axis.lerp(Vector((0, 1, 0)), straight).normalized()
        yaw = COIL_YAW[index] * coil + .29 * flinch * (1 - index / len(SPINE))
        lift = 0
        if name == "Slither":
            # A 24-frame cycle travels 1.92 m at the nominal 2.4 m/s.
            wave = phase - 2 * pi * arc_distance / (2.4 * CLIPS[name] / 30)
            yaw += .46 * sin(wave)
        if name == "Sidewind":
            # Keep the neck and head planted while lateral and lifting waves
            # travel rearwards. The runtime supplies the actual translation.
            envelope = min(1, index / 3)
            # A 24-frame cycle travels 2.24 m at the nominal 2.8 m/s.
            wave = phase - 2 * pi * arc_distance / (2.8 * CLIPS[name] / 30)
            yaw += envelope * .72 * sin(wave)
            lift = envelope * .27 * cos(wave)
        direction = Quaternion((0, 0, 1), yaw) @ direction
        direction.z += lift
        direction.normalize()
        bone.matrix = aimed(rest, point, direction) @ Matrix.Rotation(roll, 4, "Y")
        bpy.context.view_layer.update()
        point += direction * bone.bone.length
        arc_distance += bone.bone.length

    head = arm.pose.bones["Head"]
    rest = head.bone.matrix_local
    direction = rest.to_3x3() @ Vector((0, 1, 0))
    direction = direction.lerp(Vector((0, -1, 0)), straight)
    direction.z += .11 * flinch
    head.matrix = aimed(rest, head_shift + rest.translation, direction) @ Matrix.Rotation(roll, 4, "Y")
    lower_jaw = arm.pose.bones["Jaw"]
    hinge_axis = lower_jaw.bone.matrix_local.to_3x3().inverted() @ Vector((1, 0, 0))
    lower_jaw.rotation_quaternion = Quaternion(hinge_axis, jaw)
    bpy.context.view_layer.update()


def animate(arm):
    arm.animation_data_create()
    for bone in arm.pose.bones:
        bone.rotation_mode = "QUATERNION"
    for name, length in CLIPS.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame in range(length + 1):
            pose(arm, name, frame)
            for bone in arm.pose.bones:
                if bone.name == "Root":
                    continue
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert("location", frame=frame, group=bone.name)
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
        action.frame_range = (0, length)
        action.use_frame_range = True
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def export_model(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    scene = bpy.context.scene
    fit_body(scene)
    body, = (obj for obj in scene.objects if obj.type == "MESH")
    body.name = "Viper_Body"
    markings = source_markings(body.data)
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
    apply_atlas(body, markings)
    bones, neck = fitted_rig(body)
    body.data.transform(Matrix.Translation(-neck))
    bind(body, bones)
    arm = armature(bones, body)
    arm.name = arm.data.name = "Rig_Viper"
    scene.render.fps = 30
    animate(arm)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    destination = TOOLS.parents[1] / "Assets/_Project/Art/Enemies"
    build_atlas(str(destination / "Textures"))
    bpy.ops.export_scene.fbx(
        filepath=str(destination / "Viper.fbx"), use_selection=True,
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
    temporary_root = path.parent if path else TOOLS.parents[1] / "Temp"
    temporary_root.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=temporary_root) as temporary:
        closed = Path(temporary) / "viper_closed.glb"
        repair(closed)
        if path:
            render_turnaround(closed, path)
        else:
            export_model(closed)


if __name__ == "__main__":
    main()
