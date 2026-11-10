"""Build the SS-07b sculpt, rig and clips, or review the unbound body.

Run with PYTHONHASHSEED=0 and Blender 4.1:
blender -b --factory-startup --python-exit-code 1 --python Tools/Blender/brood.py
    -- --turnaround Temp/brood_turnaround.png

Omit --turnaround to write BroodMother.fbx and the shared enemy atlas.
"""

import argparse
from hashlib import sha256
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
from mathutils import Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from beetle import source_markings
from enemy_atlas import build_atlas, uv_for
from scorpion import animate, armature, rig_parts

SOURCE = TOOLS / "Sources" / "BroodMother_src.glb"
SOURCE_SHA256 = "818da7f4e7c7c40b2f7cc8fcf6f691fdeb5d33523a672b81c42201924a9d85e0"


def repair(destination):
    actual = sha256(SOURCE.read_bytes()).hexdigest()
    if actual != SOURCE_SHA256:
        raise ValueError(f"{SOURCE.name}: SHA-256 {actual} != {SOURCE_SHA256}")
    subprocess.run([
        bpy.app.binary_path, "--background", "--factory-startup", "--threads", "8",
        "--python-exit-code", "1", "--python", str(TOOLS / "repair_scan.py"),
        "--", str(SOURCE), str(destination), "--target-triangles", "24000",
        "--texture-size", "1024",
    ], check=True)


def render_turnaround(closed, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    scene = bpy.context.scene
    corners = [obj.matrix_world @ Vector(corner) for obj in scene.objects
               if obj.type == "MESH" for corner in obj.bound_box]
    low = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
    center = (low + high) / 2
    extent = max(high - low)
    print(f"Repaired bounds: {tuple(low)} to {tuple(high)}", flush=True)

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
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
    camera_data.ortho_scale = extent * 1.12
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera

    sheet = np.empty((1024, 1024, 4), np.float32)
    tile = closed.with_name("tile.png")
    directions = ((0, -1, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    for index, direction in enumerate(directions):
        camera.location = center + Vector(direction) * extent * 4
        camera.rotation_euler = (-Vector(direction)).to_track_quat("-Z", "Y").to_euler()
        if index == 3:
            camera.rotation_euler = (0, 0, 0)
        scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(tile), check_existing=False)
        pixels = np.empty(512 * 512 * 4, np.float32)
        image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        row = (1 - index // 2) * 512
        column = index % 2 * 512
        sheet[row:row + 512, column:column + 512] = pixels.reshape(512, 512, 4)

    output = bpy.data.images.new("Brood turnaround", 1024, 1024)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(path)
    output.file_format = "PNG"
    output.save()


# Landmarks in repaired scan space. The tail bends laterally before curling
# forward, and the two front feet sit slightly higher than the rear pairs.
SCALE = 2.65
GROUND = -0.28
LEGS = (
    ((.126, -.071, -.147), (.224, -.104, -.134), (.285, -.113, -.217)),
    ((.147, .038, -.148), (.279, .032, -.139), (.347, .034, -.271)),
    ((.146, .159, -.143), (.280, .190, -.133), (.341, .199, -.273)),
)
TAIL_POINTS = ((-.073, .300, -.146), (-.111, .442, -.040), (-.100, .463, .056),
               (-.045, .422, .145), (.021, .361, .209), (.067, .257, .229))
STINGER = ((.085, .217, .226), (.089, .054, .153))
TAIL_CLIPS = {
    "windup": (136, 115, 78, 47, 15, -28),
    "cocked": (138, 125, 85, 47, 13, -23),
    "extended": (91, 77, 0, 0, -4, -10),
    "down": (92, 79, 0, 0, -2, -8),
    "settle": False,
}


def place(point):
    x, y, z = point
    return Vector((x, y, z - GROUND)) * SCALE


def fitted_rig():
    def joints(side):
        return [tuple(place((side * x, y, z)) for x, y, z in chain) for chain in LEGS]

    tail = tuple(map(place, TAIL_POINTS))
    stinger = tuple(map(place, STINGER))
    bones, _, legs = rig_parts((), joints, tail, stinger)
    bones["Body"] = ("Root", place((0, .025, -.144)), place((0, -.175, -.144)))
    for side, suffix in ((1, "L"), (-1, "R")):
        bones[f"Pincer_{suffix}"] = ("Body", place((side * .095, -.163, -.134)),
                                     place((side * .245, -.385, -.143)))
    bones["Brood"] = ("Body", place((0, .092, .017)), place((0, .092, .074)))
    return bones, legs, tail, stinger


def load_runtime_mesh(closed):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(closed))
    body, = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    body.name = "BroodMother_Body"
    markings = source_markings(body.data)
    bpy.context.view_layer.objects.active = body
    if body.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    body.data.materials.clear()
    while body.data.uv_layers:
        body.data.uv_layers.remove(body.data.uv_layers[0])
    mesh = bmesh.new()
    mesh.from_mesh(body.data)
    bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=1e-6)
    mesh.to_mesh(body.data)
    mesh.free()
    body.data.calc_loop_triangles()
    remesh = body.modifiers.new("Close fine scan perforations", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = .009
    remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    body.data.calc_loop_triangles()
    modifier = body.modifiers.new("Crowd budget", "DECIMATE")
    modifier.ratio = 2960 / len(body.data.loop_triangles)
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    body.data.calc_loop_triangles()
    assert 1500 <= len(body.data.loop_triangles) <= 3000, len(body.data.loop_triangles)
    return body, markings


def distance_to_segment(point, segment):
    head, tip = segment
    axis = tip - head
    t = min(1, max(0, (point - head).dot(axis) / axis.length_squared))
    return (point - head - t * axis).length


def sac(point):
    x, y, z = point
    return (x / .171) ** 2 + ((y - .078) / .216) ** 2 <= 1 and z > -.014


def bind(body, bones):
    groups = {name: body.vertex_groups.new(name=name) for name in bones if name != "Root"}
    segments = {name: (Vector(head), Vector(tip)) for name, (_, head, tip) in bones.items()
                if name not in ("Root", "Brood")}
    weights = np.zeros((len(body.data.vertices), len(groups)))
    brood = groups["Brood"].index
    for vertex in body.data.vertices:
        source = vertex.co.copy()
        point = place(source)
        x, y, z = source
        side = "L" if x > 0 else "R"
        distances = {name: distance_to_segment(point, segment) / SCALE
                     for name, segment in segments.items()}
        tail = {name: distance for name, distance in distances.items()
                if name.startswith("Tail") or name == "Stinger"}
        tail_distance = min(tail.values())
        if sac(source) and z < (.092 if y > .12 else .108):
            weights[vertex.index, brood] = 1
        else:
            if tail_distance < .115 and (y > .303 or z > (.092 if y > .12 else .108)):
                candidates = tail
                candidates["Body"] = .042 + 2 * max(0, z + .15) + max(0, y - .31)
            else:
                candidates = {name: distance for name, distance in distances.items()
                              if name.endswith(side) or name.startswith(f"Leg_{side}")}
                candidates["Body"] = .059 + max(0, abs(x) - .132) + max(0, -.185 - y)
                if abs(x) < .115 and y > -.23 or z > -.034:
                    candidates = {"Body": 0}
            nearest = min(candidates.values())
            for name, distance in candidates.items():
                weights[vertex.index, groups[name].index] = max(0, 1 - (distance - nearest) / .042)
        vertex.co = point

    neighbors = [[] for _ in body.data.vertices]
    for edge in body.data.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    for _ in range(3):
        updated = weights.copy()
        for index, adjacent in enumerate(neighbors):
            if weights[index, brood]:
                continue
            nearby = [weights[i] for i in adjacent if not weights[i, brood]]
            if nearby:
                updated[index] = .5 * weights[index] + .5 * np.mean(nearby, axis=0)
        weights = updated

    for index, row in enumerate(weights):
        strongest = sorted(np.flatnonzero(row), key=lambda i: -row[i])[:4]
        total = sum(row[i] for i in strongest)
        for group in strongest:
            body.vertex_groups[group].add([index], row[group] / total, "REPLACE")
    assert all(vertex.groups for vertex in body.data.vertices)
    assert all(any(link.group == group.index for vertex in body.data.vertices
                   for link in vertex.groups) for group in groups.values())
    body.data.update()


def fitted_model(closed):
    body, markings = load_runtime_mesh(closed)
    bones, legs, tail, stinger = fitted_rig()
    bind(body, bones)
    arm = armature(bones, body)
    animate(arm, legs, unkeyed=("Brood",), clip_options=TAIL_CLIPS,
            tail_points=tail, stinger=stinger)
    bpy.context.scene.render.fps = 30
    apply_atlas(body, markings)
    return body, arm


def apply_atlas(body, markings):
    mesh = body.data
    mesh.materials.append(bpy.data.materials.new("enemy"))
    uv = mesh.uv_layers.new(name="UVMap").data
    brood = body.vertex_groups["Brood"].index
    ground_offset = Vector((0, 0, -GROUND))
    colours = [markings(vertex.co / SCALE - ground_offset) for vertex in mesh.vertices]
    sac_faces = [sum(any(w.group == brood for w in mesh.vertices[i].groups)
                     for i in face.vertices) >= 2 for face in mesh.polygons]
    sac_vertices = {i for face in mesh.polygons if sac_faces[face.index] for i in face.vertices}
    shell_vertices = {i for face in mesh.polygons if not sac_faces[face.index] for i in face.vertices}
    rim = sac_vertices & shell_vertices
    for face in mesh.polygons:
        sac_face = sac_faces[face.index]
        swatch = "brood_sac" if sac_face else "brood_shell"
        face.use_smooth = True
        for index in face.loop_indices:
            vertex = mesh.loops[index].vertex_index
            red, green, blue = colours[vertex]
            lightness = .2126 * red + .7152 * green + .0722 * blue
            if vertex in rim:
                tone = 1.0 if sac_face else 0.0
            else:
                tone = min(.55, (.58 - lightness) / .31) if sac_face else (.56 - lightness) / .38
            uv[index].uv = uv_for(swatch, tone)


def export_model(closed):
    body, arm = fitted_model(closed)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    destination = TOOLS.parents[1] / "Assets/_Project/Art/Enemies"
    build_atlas(str(destination / "Textures"))
    # Blender's FBX baker samples every bone, including bones with no action keys.
    # Remove only the static Brood track while keeping the full-rate animation bake.
    bpy.ops.export_scene.fbx.get_rna_type()
    from io_scene_fbx import export_fbx_bin

    bake = export_fbx_bin.fbx_animations_do
    brood_key = export_fbx_bin.ObjectWrapper(arm.data.bones["Brood"], arm).key

    def bake_without_brood(*args, **kwargs):
        take = bake(*args, **kwargs)
        if take is not None:
            take[1].pop(brood_key, None)
        return take

    export_fbx_bin.fbx_animations_do = bake_without_brood
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(destination / "BroodMother.fbx"), use_selection=True,
            object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
            mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
            use_armature_deform_only=False, armature_nodetype="NULL",
            bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True, bake_anim_simplify_factor=0.0, path_mode="STRIP",
        )
    finally:
        export_fbx_bin.fbx_animations_do = bake


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--turnaround", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    path = args.turnaround.resolve() if args.turnaround else None
    temporary_root = path.parent if path else TOOLS.parents[1] / "Temp"
    temporary_root.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=temporary_root) as folder:
        closed = Path(folder) / "brood_closed.glb"
        repair(closed)
        if path:
            render_turnaround(closed, path)
        else:
            export_model(closed)


if __name__ == "__main__":
    main()
