"""Build the Sunscar Hierophant and seal from the pinned seed-42 TRELLIS scans.

PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1
    -P Tools/Blender/hierophant.py
"""

from datetime import datetime
from functools import partial
from hashlib import sha256
from pathlib import Path
import sys
from unittest.mock import patch

import bmesh
import bpy
from io_scene_fbx import export_fbx_bin
from mathutils import Matrix, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
ART = ROOT / "Assets/_Project/Art"
RIG = ROOT / "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx"
BODY_HASH = "e16921632308ac99fbdfda17f3e9864613cc0a75f70049b4d644dcfdf655bf18"
SEAL_HASH = "b4771c389503247135401d1bc71b07de09fcce418a1a4a2e20be365a20d91150"

sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from jackal import export, mesh_bounds, paint, smoothstep
from stalker import texture_sampler


def load_scan(name, expected_hash, fit):
    path = TOOLS / "Sources" / f"{name}_src.glb"
    if sha256(path.read_bytes()).hexdigest() != expected_hash:
        raise ValueError(f"Unexpected source scan: {path}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    mesh, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    low, high = mesh_bounds([mesh])
    mesh.data.transform(fit(low, high) @ mesh.matrix_world)
    mesh.parent = None
    mesh.matrix_world = Matrix.Identity(4)
    mesh.data.update()
    return mesh


def simplify(obj, limit, voxel):
    # Join texture seams and close the overlapping scan sheets before decimation. Colour is
    # sampled from the original surface, since remeshing discards the original UVs.
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=0.00001)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    bpy.context.view_layer.objects.active = obj
    shell = obj.modifiers.new("Close scan sheets", "SOLIDIFY")
    shell.thickness = 2.8 * voxel
    shell.offset = 0
    bpy.ops.object.modifier_apply(modifier=shell.name)
    closed = obj.modifiers.new("Voxel surface", "REMESH")
    closed.mode = "VOXEL"
    closed.voxel_size = voxel
    bpy.ops.object.modifier_apply(modifier=closed.name)
    obj.data.calc_loop_triangles()
    decimate = obj.modifiers.new("Runtime triangle budget", "DECIMATE")
    decimate.ratio = min(1, .98 * limit / len(obj.data.loop_triangles))
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    bpy.context.view_layer.update()
    obj.data.calc_loop_triangles()
    assert 0 < len(obj.data.loop_triangles) <= limit, len(obj.data.loop_triangles)


def lightness(sample, point):
    r, g, b = sample(point)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def body_fit(low, high):
    # The prototype is 1.6182 m tall in its rest pose. Keep its floor and health bar clearance.
    origin = Vector(((low.x + high.x) / 2, 0, low.z))
    return Matrix.Scale(1.6182 / (high.z - low.z), 4) @ Matrix.Translation(-origin)


def body_colour(sample, point):
    red, green, blue = sample(point)
    luma = 0.2126 * red + 0.7152 * green + 0.0722 * blue
    if luma < .23:
        return "priest_umber", 1 - smoothstep(.015, .23, luma)
    if luma > .54 and blue > red * .63:
        return "priest_bone", .85 - .72 * smoothstep(.54, .84, luma)
    return "priest_ochre", .88 - .78 * smoothstep(.20, .69, luma)


def torso_and_leg_weights(point):
    x, y, z = point
    if z < .31:
        side = "l" if x >= 0 else "r"
        shin = smoothstep(.08, .165, z)
        thigh = smoothstep(.24, .35, z) * shin
        toes = (1 - shin) * (1 - smoothstep(-.18, -.07, y))
        return {"toes." + side: toes, "foot." + side: 1 - shin - toes,
                "lowerleg." + side: shin - thigh, "upperleg." + side: thigh}

    # Blend the skirt between both legs, but keep the belt on the pelvis.
    lower = .55 * (1 - smoothstep(.31, .44, z))
    pelvis = smoothstep(.43, .64, z)
    left = smoothstep(-.14, .14, x)
    thighs = (1 - lower) * (1 - pelvis)
    hips = (1 - lower) * pelvis
    spine = smoothstep(.67, .84, z)
    chest = smoothstep(.84, 1.03, z)
    head = smoothstep(1.18, 1.29, z)
    return {"lowerleg.l": lower * left, "lowerleg.r": lower * (1 - left),
            "upperleg.l": thighs * left, "upperleg.r": thighs * (1 - left),
            "hips": hips * (1 - spine), "spine": hips * spine * (1 - chest),
            "chest": hips * spine * chest * (1 - head), "head": hips * spine * chest * head}


def fit_arm(point, rig):
    x, y, z = point
    side = "l" if x >= 0 else "r"
    x = abs(x)
    shoulder = .23
    # Straighten the scan's A-pose. The perpendicular coordinate preserves the sleeve thickness.
    reach = .55 * (x - shoulder) - .835 * (z - 1.085)
    cross = .835 * (x - shoulder) + .55 * (z - 1.085)
    edge = float(np.interp(z, (.60, .80, .91, 1.04), (.33, .25, .20, .20)))
    blend = smoothstep(edge, edge + .065, x) * smoothstep(.49, .54, z)
    blend *= smoothstep(.005, .14, reach)
    length = float(np.interp(reach, (0, .215, .365, .414, .563),
                            (.212, .4535, .7132, .787, .956)))
    fitted = Vector((length if side == "l" else -length, y + .012,
                     rig["upperarm.l"].z + cross - .05 * smoothstep(.73, .86, length)))
    elbow = smoothstep(.405, .50, length)
    wrist = smoothstep(.675, .753, length)
    hand = smoothstep(.755, .825, length)
    weights = {"upperarm." + side: 1 - elbow, "lowerarm." + side: elbow - wrist,
               "wrist." + side: wrist - hand, "hand." + side: hand}
    return fitted, weights, blend


def skin(body, arm):
    bpy.context.view_layer.update()
    assert arm.name == "Rig_Medium" and len(arm.data.bones) == 23
    rig = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    groups = {name: body.vertex_groups.new(name=name) for name in rig}
    to_rig = arm.matrix_world.inverted()
    for vertex in body.data.vertices:
        source = vertex.co.copy()
        x, y, z = source
        fitted = Vector((x * (0.84 + .16 * smoothstep(.40, .68, z)), y,
                         np.interp(z, (0, .13, .28, .61, 1.06, 1.23, 1.6182),
                                   (0, .145, .292, .62, 1.105, 1.2414, 1.6182))))
        arm_point, arm_weights, blend = fit_arm(source, rig)
        weights = {name: weight * (1 - blend)
                   for name, weight in torso_and_leg_weights(source).items()}
        weights.update({name: weight * blend for name, weight in arm_weights.items()})
        strongest = sorted(((weight, name) for name, weight in weights.items() if weight > .001),
                           reverse=True)[:4]
        total = sum(weight for weight, _ in strongest)
        assert total > 0
        for weight, name in strongest:
            groups[name].add([vertex.index], weight / total, "REPLACE")
        vertex.co = to_rig @ fitted.lerp(arm_point, blend)

    body.parent = arm
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    body.modifiers.new("Rig_Medium", "ARMATURE").object = arm
    body.data.update()


def build_body():
    body = load_scan("Hierophant", BODY_HASH, body_fit)
    sample = texture_sampler(body)
    simplify(body, 3900, .0035)
    body.name = body.data.name = "Hierophant_Body"
    paint(body, lambda point: body_colour(sample, point))
    body.data.transform(body_fit(*mesh_bounds([body])))
    bpy.ops.import_scene.fbx(filepath=str(RIG), use_anim=False)
    arm, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    skin(body, arm)
    export(ART / "Enemies/Hierophant.fbx", [arm, body])


def seal_fit(low, high):
    # A 96 cm tile, centred on its placement anchor. Leave the visible relief just above the
    # arena plane to avoid z-fighting, with the bottom of the stone sunk into the floor.
    centre = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, high.z))
    span = .96 / max(high.x - low.x, high.y - low.y)
    return (Matrix.Translation(Vector((0, 0, .012)))
            @ Matrix.Diagonal((span, span, .27, 1)) @ Matrix.Translation(-centre))


def seal_part(tile, name, keep):
    mesh = tile.data.copy()
    mesh.name = name
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[face for face in bm.faces if face.index not in keep], context="FACES")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_seal():
    tile = load_scan("HierophantSeal", SEAL_HASH, seal_fit)
    sample = texture_sampler(tile)
    simplify(tile, 2800, .0012)
    # Split on the painted carving so its triangles also form the independent cast light.
    carving = {face.index for face in tile.data.polygons
               if face.normal.z > .1 and face.center.z > 0
               and face.center.xy.length < .45 and lightness(sample, face.center) < .35}
    stone = set(range(len(tile.data.polygons))) - carving
    assert len(carving) > 40 and len(stone) > len(carving)
    slab = seal_part(tile, "SealStone", stone)
    rays = seal_part(tile, "SealCarving", carving)
    paint(slab, lambda point: ("priest_ochre", .95 - .75 * smoothstep(.23, .70, lightness(sample, point))))
    paint(rays, lambda point: ("priest_umber", .80 - .68 * smoothstep(.11, .38, lightness(sample, point))))
    export(ART / "Environment/HierophantSeal.fbx", [slab, rays])


if __name__ == "__main__":
    # Blender otherwise writes the current wall time into both binary FBX headers.
    header = partial(export_fbx_bin.fbx_header_elements, time=datetime(1970, 1, 1))
    with patch.object(export_fbx_bin, "fbx_header_elements", header):
        build_body()
        build_seal()
