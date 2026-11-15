"""Build the SS-13b sculpt and spiral staff in Blender 4.1.

PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1
    -P Tools/Blender/storm_caller.py
    [-- --turnaround Temp/storm_caller_turnaround.png | --pose-sheet Temp/storm_caller_poses.png]
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
SOURCE = TOOLS / "Sources/StormCaller_src.glb"
SOURCE_SHA256 = "953ce4b27aceb8984cfcc34dd2aee4aa0cf90605030c23b6e32c9e90731c1c9a"
ART = ROOT / "Assets/_Project/Art/Enemies"
ATLAS = ART / "Textures/T_Enemy_Albedo.png"
RING_CENTER = 1.19
sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from enemy_atlas import build_atlas, uv_for
from jackal import add_label, distance_weights, export, light_studio, mesh_bounds, paint, smoothstep
from jackal_priest import POSES, RIG, STAFF_GRIP, load_poses, preview_material
from mossguard import components, reduce
from stalker import texture_sampler, textured


def repair(destination):
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    if actual != SOURCE_SHA256:
        raise ValueError(f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}")
    subprocess.run([
        bpy.app.binary_path, "-b", "--factory-startup", "-t", "8", "--python-exit-code", "1",
        "-P", str(TOOLS / "repair_scan.py"), "--", str(SOURCE), str(destination),
        "--texture-size", "1024",
    ], check=True)


def load_body(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ART / "Jackal.fbx"), use_anim=False)
    low, high = mesh_bounds(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    height = .96 * (high.z - low.z)

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
    # Keep the repaired scan's silhouette and baked albedo for the turnaround.
    print(f"Caller review body: {len(body.data.loop_triangles)} triangles at {height:.4f} m",
          flush=True)
    bpy.context.view_layer.update()
    return body, height


def build_staff(preview=False):
    """Two closed shells sharing the grip origin and the spiral's lower joint."""
    vertices, faces, finishes = [], [], []

    def add_shell(rings, finish):
        start = len(vertices)
        width = len(rings[0])
        for ring in rings:
            vertices.extend(ring)
        shell = [tuple(range(i, i + width)) for i in range(start, len(vertices), width)]
        first_face = len(faces)
        faces.append(tuple(reversed(shell[0])))
        for bottom, top in zip(shell, shell[1:]):
            for i in range(width):
                j = (i + 1) % width
                faces.append((bottom[i], bottom[j], top[j], top[i]))
        faces.append(shell[-1])
        finishes.extend([finish] * (len(faces) - first_face))

    shaft = []
    for z, center, radius, phase in (
        (-.60, -.024, .019, .00), (-.48, -.032, .029, .12),
        (-.13, .010, .023, -.10), (.28, -.024, .026, .17),
        (.71, .033, .023, -.12), (1.00, .000, .038, .00),
    ):
        shaft.append([
            (center + radius * cos(tau * side / 8 + phase),
             .82 * radius * sin(tau * side / 8 + phase), z)
            for side in range(8)
        ])
    add_shell(shaft, 0)

    spiral = []
    for step in range(25):
        t = step / 24
        angle = -pi / 2 + 3 * pi * t
        radius = .202 - .120 * t
        width = .022 - .012 * t
        thickness = .019 - .010 * t
        spiral.append([
            ((radius + across * width) * cos(angle), depth * thickness,
             RING_CENTER + (radius + across * width) * sin(angle))
            for across, depth in ((-1, -1), (1, -1), (1, 1), (-1, 1))
        ])
    add_shell(spiral, 1)

    mesh = bpy.data.meshes.new("CallerStaff")
    mesh.from_pydata(vertices, [], faces)
    if preview:
        mesh.materials.append(preview_material("Dark umber", (.070, .041, .028)))
        mesh.materials.append(preview_material("Pale bone", (.76, .65, .48)))
        for face, finish in zip(mesh.polygons, finishes):
            face.material_index = finish
    else:
        mesh.materials.append(bpy.data.materials["enemy"])
        uv = mesh.uv_layers.new(name="UVMap").data
        for face, finish in zip(mesh.polygons, finishes):
            for index in face.loop_indices:
                point = mesh.vertices[mesh.loops[index].vertex_index].co
                shade = .62 - .32 * smoothstep(-.60, 1.4, point.z)
                uv[index].uv = uv_for("caller_bone" if finish else "caller_cloak", shade)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    assert all(edge.is_manifold for edge in bm.edges)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    mesh.calc_loop_triangles()
    count = len(mesh.loop_triangles)
    assert count <= 300, count
    print(f"Caller staff: {count} triangles", flush=True)
    staff = bpy.data.objects.new("CallerStaff", mesh)
    bpy.context.scene.collection.objects.link(staff)
    glow = bpy.data.objects.new("Glow", None)
    bpy.context.scene.collection.objects.link(glow)
    glow.parent = staff
    glow.location.z = RING_CENTER
    return staff


def render_turnaround(closed, destination):
    _, height = load_body(closed)
    staff = build_staff(preview=True)
    scene = bpy.context.scene
    light_studio(scene, height)
    scene.cycles.samples = 20
    cell, span = 640, 2.47
    scene.render.resolution_x = scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100

    lens = bpy.data.cameras.new("Orthographic")
    lens.type = "ORTHO"
    lens.ortho_scale = span
    camera = bpy.data.objects.new(lens.name, lens)
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = add_label(camera, (-.44 * span, .435 * span, -1), .041 * span)
    staff.rotation_mode = "QUATERNION"
    face_camera = Matrix.Rotation(-pi / 2, 3, "X").to_quaternion()

    sheet = np.empty((2 * cell, 2 * cell, 4), np.float32)
    tile = closed.with_name("tile.png")
    views = (
        ("Front", (0, -1, 0)), ("Side", (1, 0, 0)),
        ("Back", (0, 1, 0)), ("Top", (0, 0, 1)),
    )
    for index, (name, direction) in enumerate(views):
        direction = Vector(direction)
        camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
        if name == "Top":
            camera.rotation_euler = (0, 0, 0)
        rotation = camera.rotation_euler.to_quaternion()
        target = Vector((0, 0, height / 2 if name == "Top" else .91))
        camera.location = target + direction * span * 4 + rotation @ Vector((.26, 0, 0))
        label.body = name
        # Present the detached prop face-on at the same scale as the body.
        staff.rotation_quaternion = rotation @ face_camera
        staff.location = target + rotation @ Vector((1.18, -.38, 0))
        row, col = (1 - index // 2) * cell, (index % 2) * cell
        sheet[row:row + cell, col:col + cell] = render_tile(scene, tile, cell)
    save_sheet("Storm Caller turnaround", sheet, destination)


def render_tile(scene, tile, cell):
    scene.render.filepath = str(tile)
    bpy.ops.render.render(write_still=True)
    image = bpy.data.images.load(str(tile), check_existing=False)
    pixels = np.empty(cell * cell * 4, np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(cell, cell, 4)


def save_sheet(name, sheet, destination):
    height, width, _ = sheet.shape
    output = bpy.data.images.new(name, width, height, alpha=False)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(destination)
    output.file_format = "PNG"
    output.save()


def caller_palette(sample, point):
    r, g, b = sample(point)
    lightness = .2126 * r + .7152 * g + .0722 * b
    x, y, z = point
    arms = abs(x) > .29 and .87 < z < 1.14 and y < -.04
    face = z > 1.12 and y < -.15
    feet = z < .20 and y < -.08
    fur = face or arms or feet or (.22 < z < .53 and abs(x) < .25 and y < -.16)
    if lightness > .47 and (not face or lightness > .67) and (b > .67 * r or not fur):
        return "caller_bone", .83 - .69 * smoothstep(.47, .82, lightness)
    if fur and lightness > .28:
        return "caller_fur", .89 - .77 * smoothstep(.28, .72, lightness)
    return "caller_cloak", .93 - .75 * smoothstep(.07, .48, lightness)


def lower_weights(point):
    x, y, z = point
    side = "l" if x >= 0 else "r"
    if z < .22 and y < -.09:
        shin = smoothstep(.085, .20, z)
        toe = (1 - shin) * (1 - smoothstep(-.26, -.19, y))
        return {"toes." + side: toe, "foot." + side: 1 - toe - shin, "lowerleg." + side: shin}

    # The tattered hem shares both leg motions across the centre and follows
    # its nearer leg at the edge. Blend back into the belt higher on the cloak.
    left = smoothstep(-.20, .20, x)
    hips = smoothstep(.38, .63, z)
    shins = (1 - hips) * (1 - smoothstep(.23, .37, z))
    thighs = 1 - hips - shins
    return {"hips": hips, "upperleg.l": thighs * left, "upperleg.r": thighs * (1 - left),
            "lowerleg.l": shins * left, "lowerleg.r": shins * (1 - left)}


def fit_and_skin(body, arm, height):
    assert arm.name == "Rig_Medium" and len(arm.data.bones) == 23
    heads = {bone.name: arm.matrix_world @ bone.head_local for bone in arm.data.bones}
    tails = {bone.name: arm.matrix_world @ bone.tail_local for bone in arm.data.bones}
    groups = {name: body.vertex_groups.new(name=name) for name in heads}
    rig_space = arm.matrix_world.inverted()
    for vertex in body.data.vertices:
        source = vertex.co.copy()
        x, y, z = source
        reach = abs(x)
        side = "l" if x >= 0 else "r"
        fitted_z = float(np.interp(z, (0, .14, .33, .58, .96, 1.14, height),
                                   (0, .145, .292, .58, 1.076, 1.21, height)))
        point = Vector((x, y + .205, fitted_z))
        arm_blend = (smoothstep(.20, .35, reach) * smoothstep(.84, .93, z)
                     * (1 - smoothstep(1.08, 1.19, z)) * (1 - smoothstep(-.04, .07, y)))
        if arm_blend > 0:
            arm_x = float(np.interp(reach, (.20, .34, .45, .59, .68, .72),
                                     (.20, .34, .52, .76, .883, .96)))
            arm_z = float(np.interp(reach, (.20, .39, .62, .72), (.99, .985, .974, .95)))
            palm = smoothstep(.59, .68, reach)
            aligned = Vector((arm_x if x >= 0 else -arm_x, y + .205,
                              heads["upperarm.l"].z + z - arm_z - .057 * palm))
            point = point.lerp(aligned, arm_blend)

        if z < .70:
            weights = lower_weights(source)
            waist = smoothstep(.59, .70, z)
            weights = {name: weight * (1 - waist) for name, weight in weights.items()}
            weights["spine"] = waist
        else:
            head = smoothstep(1.07, 1.23, z)
            chest = smoothstep(.72, .96, z)
            weights = {"head": head, "chest": (1 - head) * chest, "spine": (1 - head) * (1 - chest)}

        if arm_blend > 0:
            joints = [joint + "." + side for joint in ("upperarm", "lowerarm", "wrist", "hand")]
            arm_line = Vector((point.x, point.y, heads["upperarm.l"].z))
            arms = distance_weights(arm_line, joints, heads, tails)
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
    high, height = load_body(closed)
    sample = texture_sampler(high)
    body = reduce(high, 2860, voxel=.006)
    body.name = "StormCaller_Body"
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.index_update()
    for part in components(bm):
        if len(part) < 14:
            bmesh.ops.delete(bm, geom=part, context="VERTS")
    assert all(edge.is_manifold for edge in bm.edges), "The runtime shell must stay closed"
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    body.data.calc_loop_triangles()
    count = len(body.data.loop_triangles)
    assert 1500 <= count <= 3000, count
    print(f"Caller body: {count} triangles", flush=True)
    paint(body, lambda point: caller_palette(sample, point))

    bpy.ops.import_scene.fbx(filepath=str(RIG), use_anim=False)
    arm, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    fit_and_skin(body, arm, height)
    export(ART / "StormCaller.fbx", [arm, body])
    staff = build_staff()
    export(ART / "CallerStaff.fbx", [staff, *staff.children])
    build_atlas(str(ATLAS.parent))


def render_poses(destination, tile):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name in ("StormCaller", "CallerStaff"):
        bpy.ops.import_scene.fbx(filepath=str(ART / f"{name}.fbx"), use_anim=False)
    arm, body, staff = (bpy.data.objects[name] for name in ("Rig_Medium", "StormCaller_Body", "CallerStaff"))
    actions = load_poses()
    arm.animation_data_create()
    atlas = bpy.data.images.load(str(ATLAS))
    for obj in (body, staff):
        textured(obj, atlas)
        shader = obj.data.materials[0].node_tree.nodes["Principled BSDF"]
        shader.inputs["Roughness"].default_value = .95

    scene = bpy.context.scene
    light_studio(scene, 1.5)
    scene.cycles.samples = 16
    cell, span = 360, 2.78
    scene.render.resolution_x = scene.render.resolution_y = cell
    scene.render.resolution_percentage = 100
    lens = bpy.data.cameras.new("Pose camera")
    lens.type, lens.ortho_scale = "ORTHO", span
    camera = bpy.data.objects.new(lens.name, lens)
    scene.collection.objects.link(camera)
    scene.camera = camera
    label = add_label(camera, (-.45 * span, .43 * span, -1), .031 * span)
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
            # Death_A animates the slot away for a weapon drop. Show the held grip.
            socket = hand.matrix @ held_socket if name == "Death_A" else slot.matrix
            grip = arm.matrix_world @ socket
            grip.normalize()
            staff.matrix_world = grip @ STAFF_GRIP
            direction = Vector(eye)
            camera.location = Vector((-.14, 0, .92)) + direction
            camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
            label.body = name.replace("_", " ") if column == 0 else ""
            y, x = (len(POSES) - row - 1) * cell, column * cell
            sheet[y:y + cell, x:x + cell] = render_tile(scene, tile, cell)
    save_sheet("Storm Caller poses", sheet, destination)


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
    with TemporaryDirectory(prefix="storm_caller_", dir=destination.parent if destination else None) as scratch:
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
