"""Build the SS-11b sculpt and its separate sun-disc staff in Blender 4.1.

PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1
    -P Tools/Blender/jackal_priest.py
    [-- --turnaround Temp/jackal_priest_turnaround.png | --pose-sheet Temp/jackal_priest_poses.png]
"""

import argparse
from hashlib import sha256
from math import cos, pi, sin, tau
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
from mathutils import Matrix, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
SOURCE = TOOLS / "Sources/JackalPriest_src.glb"
SOURCE_SHA256 = "d92e258071226f66b57401960ca19fd50f5d3dfb8b07a3e788ad2bf12feca577"
ART = ROOT / "Assets/_Project/Art/Enemies"
ATLAS = ART / "Textures/T_Enemy_Albedo.png"
KAYKIT = ROOT / "Assets/ThirdParty/KayKit"
RIG = KAYKIT / "Skeletons/Characters/Skeleton_Minion.fbx"
CLIPS = KAYKIT / "Animations/Rig_Medium"
POSES = (
    ("General", "Idle_B"),
    ("MovementBasic", "Walking_B"),
    ("MovementBasic", "Running_A"),
    ("CombatRanged", "Ranged_Magic_Spellcasting"),
    ("General", "Hit_A"),
    ("General", "Death_A"),
)
# Cant the shaft forward and out at the grip to clear the sleeve and face.
STAFF_GRIP = (
    Matrix.Rotation(pi / 10, 4, "Y")
    @ Matrix.Rotation(-pi / 4, 4, "Z")
    @ Matrix.Rotation(-pi / 2, 4, "Y")
)
STAFF_DISC_HEIGHT = 1.17
sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from enemy_atlas import build_atlas, uv_for
from jackal import add_label, distance_weights, export, light_studio, mesh_bounds, paint, smoothstep
from stalker import texture_sampler, textured


def repair(destination):
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    if actual != SOURCE_SHA256:
        raise ValueError(f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}")
    subprocess.run([
        bpy.app.binary_path, "-b", "--factory-startup", "-t", "8", "--python-exit-code", "1",
        "-P", str(TOOLS / "repair_scan.py"), "--", str(SOURCE), str(destination),
        "--target-triangles", "2900", "--texture-size", "1024",
    ], check=True)


def load_body(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ART / "Jackal.fbx"), use_anim=False)
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
    body.data.calc_loop_triangles()
    count = len(body.data.loop_triangles)
    assert 1500 <= count <= 3000, count
    print(f"Priest body: {count} triangles at {height:.4f} m", flush=True)
    bpy.context.view_layer.update()
    return body, height


def preview_material(name, color):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    shader = material.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = .85
    return material


def build_staff(preview=False):
    """Closed shells for the shaft, beveled sun disc and its raised rim."""
    vertices, faces, finishes = [], [], []

    def add_ring(points):
        start = len(vertices)
        vertices.extend(points)
        return tuple(range(start, len(vertices)))

    def add_face(indices, finish):
        faces.append(indices)
        finishes.append(finish)

    def bridge(first, second, finish):
        for index in range(len(first)):
            next_index = (index + 1) % len(first)
            add_face((first[index], first[next_index], second[next_index], second[index]), finish)

    def cap_shell(rings, finish):
        add_face(tuple(reversed(rings[0])), finish)
        for first, second in zip(rings, rings[1:]):
            bridge(first, second, finish)
        add_face(rings[-1], finish)

    shaft_angles = [step * tau / 8 for step in range(8)]
    shaft = [
        add_ring([(radius * cos(a), radius * sin(a), z) for a in shaft_angles])
        for z, radius in ((-.56, .019), (-.48, .027), (.84, .027), (1.04, .044))
    ]
    cap_shell(shaft, 1)

    disc_angles = [step * tau / 12 for step in range(12)]
    disc = [
        add_ring([(radius * cos(a), y, STAFF_DISC_HEIGHT + radius * sin(a)) for a in disc_angles])
        for y, radius in ((-.031, .132), (-.019, .151), (.019, .151), (.031, .132))
    ]
    cap_shell(disc, 0)

    rim_angles = [step * tau / 16 for step in range(16)]
    rim = [
        add_ring([
            ((.160 + .015 * cos(profile)) * cos(a), .022 * sin(profile),
             STAFF_DISC_HEIGHT + (.160 + .015 * cos(profile)) * sin(a))
            for a in rim_angles
        ])
        for profile in (0, pi / 2, pi, 3 * pi / 2)
    ]
    for first, second in zip(rim, rim[1:] + rim[:1]):
        bridge(first, second, 1)

    mesh = bpy.data.meshes.new("PriestStaff")
    mesh.from_pydata(vertices, [], faces)
    if preview:
        mesh.materials.append(preview_material("Bleached bone", (.88, .81, .66)))
        mesh.materials.append(preview_material("Dusty ochre", (.43, .27, .15)))
        for face, finish in zip(mesh.polygons, finishes):
            face.material_index = finish
    else:
        mesh.materials.append(bpy.data.materials.get("enemy"))
        uv = mesh.uv_layers.new(name="UVMap").data
        for face, finish in zip(mesh.polygons, finishes):
            swatch = "priest_ochre" if finish else "priest_bone"
            for index in face.loop_indices:
                point = mesh.vertices[mesh.loops[index].vertex_index].co
                uv[index].uv = uv_for(swatch, .28 + .22 * smoothstep(-.56, 1.34, point.z))
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    assert all(edge.is_manifold for edge in bm.edges)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    mesh.calc_loop_triangles()
    assert len(mesh.loop_triangles) <= 300
    print(f"Priest staff: {len(mesh.loop_triangles)} triangles", flush=True)
    staff = bpy.data.objects.new("PriestStaff", mesh)
    bpy.context.scene.collection.objects.link(staff)
    if not preview:
        glow = bpy.data.objects.new("Glow", None)
        bpy.context.scene.collection.objects.link(glow)
        glow.parent = staff
        glow.location.z = STAFF_DISC_HEIGHT
    return staff


def render_turnaround(closed, destination):
    body, height = load_body(closed)
    staff = build_staff(preview=True)
    scene = bpy.context.scene
    light_studio(scene, height)
    scene.cycles.samples = 20
    cell = 640
    scene.render.resolution_x = scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100

    camera_data = bpy.data.cameras.new("Orthographic")
    camera_data.type = "ORTHO"
    span = 2.36
    camera_data.ortho_scale = span
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = add_label(camera, (-.44 * span, .435 * span, -1), .041 * span)

    sheet = np.empty((2 * cell, 2 * cell, 4), np.float32)
    tile = closed.with_name("tile.png")
    center = Vector((0, 0, .94))
    views = (
        ("Front", (0, -1, 0)),
        ("Side", (1, 0, 0)),
        ("Back", (0, 1, 0)),
        ("Top", (0, 0, 1)),
    )
    for index, (name, direction) in enumerate(views):
        camera.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()
        if name == "Top":
            camera.rotation_euler = (0, 0, 0)
        rotation = camera.rotation_euler.to_quaternion()
        target = Vector((0, 0, height / 2)) if name == "Top" else center
        camera.location = target + Vector(direction) * span * 4 + rotation @ Vector((.25, 0, 0))
        label.body = name
        # Present the unattached prop at the same scale and angle in every panel.
        staff.rotation_mode = "QUATERNION"
        staff.rotation_quaternion = rotation @ Matrix.Rotation(-pi / 2, 3, "X").to_quaternion()
        staff.location = target + rotation @ Vector((.99, -.38, 0))
        scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(tile), check_existing=False)
        pixels = np.empty(cell * cell * 4, np.float32)
        image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        row = (1 - index // 2) * cell
        col = (index % 2) * cell
        sheet[row:row + cell, col:col + cell] = pixels.reshape(cell, cell, 4)

    output = bpy.data.images.new("Jackal priest turnaround", 2 * cell, 2 * cell, alpha=False)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(destination)
    output.file_format = "PNG"
    output.save()


def priest_palette(sample, point):
    r, g, b = sample(point)
    lightness = .2126 * r + .7152 * g + .0722 * b
    x = abs(point.x)
    z = point.z
    front = point.y < -.07
    sash = .55 < z < .70 or (.34 < z <= .55 and front and x < .105)
    if sash and lightness < .53:
        return "priest_umber", .75 - .60 * smoothstep(.08, .53, lightness)
    if z < .15 or (x > .465 and z > .91):
        swatch = "priest_umber" if lightness < .13 else "priest_ochre"
    elif z > 1.05 and (lightness < .46 or b < .56 * r):
        swatch = "priest_umber" if lightness < .09 else "priest_ochre"
    else:
        swatch = "priest_bone"
    return swatch, .82 - .68 * smoothstep(.08, .76, lightness)


def garment_weights(point):
    x, y, z = point
    if z < .145:
        side = "l" if x >= 0 else "r"
        ankle = smoothstep(.065, .145, z)
        toe = (1 - ankle) * (1 - smoothstep(-.145, -.045, y))
        return {"toes." + side: toe, "foot." + side: 1 - toe - ankle, "lowerleg." + side: ankle}

    # The garment is a continuous shell. Blend the two legs across the cloth
    # and favour the shin at the lower hem, then fade into the pelvis at the sash.
    left = smoothstep(-.29, .29, x)
    hips = smoothstep(.36, .67, z)
    shins = (1 - hips) * .62 * (1 - smoothstep(.18, .37, z))
    thighs = 1 - hips - shins
    return {"hips": hips, "upperleg.l": thighs * left, "upperleg.r": thighs * (1 - left),
            "lowerleg.l": shins * left, "lowerleg.r": shins * (1 - left)}


def fit_and_skin(body, arm, height):
    bones = arm.data.bones
    assert arm.name == "Rig_Medium" and len(bones) == 23
    heads = {bone.name: arm.matrix_world @ bone.head_local for bone in bones}
    tails = {bone.name: arm.matrix_world @ bone.tail_local for bone in bones}
    groups = {name: body.vertex_groups.new(name=name) for name in heads}
    rig_space = arm.matrix_world.inverted()
    for vertex in body.data.vertices:
        source = vertex.co.copy()
        side = "l" if source.x >= 0 else "r"
        x, z = abs(source.x), source.z
        fitted_z = float(np.interp(z, (0, .145, .29, .62, .985, 1.16, height),
                                   (0, .145, .30, .64, 1.105, 1.23, height)))
        point = Vector((source.x, source.y - .016, fitted_z))
        arm_blend = smoothstep(.16, .30, x) * smoothstep(.68, .81, z)
        if arm_blend > 0:
            # Follow the T-pose span, with the palm lowered to the rest slot.
            reach = float(np.interp(x, (.16, .22, .31, .445, .49, .635),
                                    (.16, .22, .425, .706, .795, .963)))
            palm = smoothstep(.425, .54, x)
            aligned = Vector((reach if side == "l" else -reach, source.y - .026,
                              heads["upperarm.l"].z + z - .988 - .038 * palm))
            point = point.lerp(aligned, arm_blend)

        if z < .70:
            weights = garment_weights(source)
            waist = smoothstep(.60, .70, z)
            weights = {name: weight * (1 - waist) for name, weight in weights.items()}
            weights["spine"] = waist
        else:
            head = smoothstep(1.07, 1.23, z)
            chest = smoothstep(.73, .93, z)
            weights = {"head": head, "chest": (1 - head) * chest, "spine": (1 - head) * (1 - chest)}
        if arm_blend > 0:
            names = [joint + "." + side for joint in ("upperarm", "lowerarm", "wrist", "hand")]
            # Weight the hanging sleeves by their distance along the arm.
            arm_line = Vector((point.x, point.y, heads["upperarm.l"].z))
            arms = distance_weights(arm_line, names, heads, tails)
            total = sum(arms.values())
            weights = {name: weight * (1 - arm_blend) for name, weight in weights.items()}
            weights.update({name: weight * arm_blend / total for name, weight in arms.items()})
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
    body.modifiers.new("Rig_Medium", "ARMATURE").object = arm
    body.data.update()


def build_assets(closed):
    body, height = load_body(closed)
    body.name = "JackalPriest_Body"
    sample = texture_sampler(body)
    paint(body, lambda point: priest_palette(sample, point))
    bpy.ops.import_scene.fbx(filepath=str(RIG), use_anim=False)
    arm, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    fit_and_skin(body, arm, height)
    export(ART / "JackalPriest.fbx", [arm, body])
    staff = build_staff()
    export(ART / "PriestStaff.fbx", [staff, *staff.children])
    build_atlas(str(ATLAS.parent))


def load_poses():
    actions = {}
    for group in dict.fromkeys(group for group, _ in POSES):
        objects_before = set(bpy.data.objects)
        actions_before = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=str(CLIPS / f"Rig_Medium_{group}.fbx"))
        for obj in set(bpy.data.objects) - objects_before:
            bpy.data.objects.remove(obj, do_unlink=True)
        for action in set(bpy.data.actions) - actions_before:
            name = action.name.split("|")[-1]
            if (group, name) in POSES:
                actions[name] = action
            else:
                bpy.data.actions.remove(action)
    assert actions.keys() == {name for _, name in POSES}, sorted(actions)
    return actions


def render_poses(destination, tile):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name in ("JackalPriest", "PriestStaff"):
        bpy.ops.import_scene.fbx(filepath=str(ART / f"{name}.fbx"), use_anim=False)
    arm, body, staff = (bpy.data.objects[name] for name in ("Rig_Medium", "JackalPriest_Body", "PriestStaff"))
    actions = load_poses()
    arm.animation_data_create()
    atlas = bpy.data.images.load(str(ATLAS))
    for obj in (body, staff):
        textured(obj, atlas)

    scene = bpy.context.scene
    light_studio(scene, 1.5)
    scene.cycles.samples = 16
    cell = 360
    scene.render.resolution_x = scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100
    camera_data = bpy.data.cameras.new("Pose camera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 2.64
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = add_label(camera, (-1.23, 1.19, -1), .085)
    views = ((.16, (-1.7, -4, 1.2)), (.48, (-2.7, -3.2, 1.4)), (.79, (.8, -3, 2.4)))
    sheet = np.empty((len(POSES) * cell, len(views) * cell, 4), np.float32)
    slot, hand = (arm.pose.bones[name] for name in ("handslot.r", "hand.r"))
    held_socket = hand.bone.matrix_local.inverted() @ slot.bone.matrix_local
    for row, (_, name) in enumerate(POSES):
        action = actions[name]
        arm.animation_data.action = action
        first, last = action.frame_range
        for column, (fraction, eye) in enumerate(views):
            scene.frame_set(round(first + (last - first) * fraction))
            # Death_A animates the slot away from the hand for a weapon drop.
            # Hold the priest's staff in the same rest grip relative to the hand.
            socket = hand.matrix @ held_socket if name == "Death_A" else slot.matrix
            grip = arm.matrix_world @ socket
            grip.normalize()
            staff.matrix_world = grip @ STAFF_GRIP
            direction = Vector(eye)
            camera.location = Vector((-.14, 0, .91)) + direction
            camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
            label.body = name.replace("_", " ") if column == 0 else ""
            scene.render.filepath = str(tile)
            bpy.ops.render.render(write_still=True)
            image = bpy.data.images.load(str(tile), check_existing=False)
            pixels = np.empty(cell * cell * 4, np.float32)
            image.pixels.foreach_get(pixels)
            bpy.data.images.remove(image)
            y, x = (len(POSES) - row - 1) * cell, column * cell
            sheet[y:y + cell, x:x + cell] = pixels.reshape(cell, cell, 4)

    output = bpy.data.images.new("Jackal priest poses", len(views) * cell, len(POSES) * cell, alpha=False)
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
    if destination:
        destination.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(prefix="jackal_priest_", dir=destination.parent if destination else None) as scratch:
        closed = Path(scratch) / "closed.glb"
        repair(closed)
        if args.turnaround:
            render_turnaround(closed, destination)
            return
        build_assets(closed)
        if args.pose_sheet:
            render_poses(destination, closed.with_name("pose.png"))


if __name__ == "__main__":
    main()
