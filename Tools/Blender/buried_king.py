"""Fit the supplied Buried King scan to KayKit Rig_Medium.

PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1
    -P Tools/Blender/buried_king.py
"""

from hashlib import sha256
from math import radians
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
ART = ROOT / "Assets/_Project/Art/Enemies"
RIG = ROOT / "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx"
SOURCE_HASH = "eb61de68969d1eef29dd6a9a0852067947064ca1cd784d5fc3aadae7e1c4ee93"
TRIANGLE_LIMIT = 6400

sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from jackal import export, smoothstep
from hierophant import simplify


def load_body():
    path = TOOLS / "Sources/BuriedKing_src.glb"
    if sha256(path.read_bytes()).hexdigest() != SOURCE_HASH:
        raise ValueError(f"Unexpected source scan: {path}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    source, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    body = source.copy()
    body.data = source.data.copy()
    bpy.context.scene.collection.objects.link(body)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    for layer in list(body.data.uv_layers):
        body.data.uv_layers.remove(layer)
    simplify(body, TRIANGLE_LIMIT, .0035)
    bake_albedo(source, body)
    bpy.data.objects.remove(source, do_unlink=True)
    body.name = body.data.name = "BuriedKing_Body"
    return body


def bake_albedo(source, body):
    source_material = source.active_material
    nodes = source_material.node_tree.nodes
    shader = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    output = next(node for node in nodes if node.type == "OUTPUT_MATERIAL")
    albedo = shader.inputs["Base Color"].links[0].from_socket
    emission = nodes.new("ShaderNodeEmission")
    source_material.node_tree.links.new(albedo, emission.inputs["Color"])
    source_material.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])

    material = bpy.data.materials.new("Buried King")
    material.use_nodes = True
    body.data.materials.clear()
    body.data.materials.append(material)
    image = bpy.data.images.new("T_BuriedKing_Albedo", 1024, 1024, alpha=False)
    node = material.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    material.node_tree.nodes.active = node
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=radians(70), island_margin=0)
    bpy.ops.uv.pack_islands(shape_method="AABB", rotate_method="CARDINAL", margin=.008)
    bpy.ops.object.mode_set(mode="OBJECT")

    source.select_set(True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    bpy.ops.object.bake(type="EMIT", use_selected_to_active=True, cage_extrusion=.018,
                        max_ray_distance=.044, margin=5, margin_type="EXTEND")
    image.filepath_raw = str(ART / "T_BuriedKing_Albedo.png")
    image.file_format = "PNG"
    image.save()
    material.use_nodes = False


def body_fit(point, side):
    x, y, z = point
    height = np.interp(z, (-.50, -.425, -.34, -.165, .045, .28, .35, .4986),
                       (0, .13, .292, .535, .78, 1.12, 1.25, 1.72))
    # Keep the broad pauldrons and crown while lining the boots up with the KayKit stance.
    fitted_x = x * (1.48 + .10 * smoothstep(-.32, -.13, z))
    fitted = Vector((fitted_x, y * 1.70, height))
    shin = smoothstep(-.435, -.395, z)
    thigh = smoothstep(-.37, -.30, z) * shin
    toe = (1 - shin) * (1 - smoothstep(-.105, -.045, y))
    torso = smoothstep(-.235, -.105, z)
    spine = smoothstep(-.045, .115, z)
    chest = smoothstep(.105, .245, z)
    head = smoothstep(.326, .372, z)
    weights = {
        "toes." + side: (1 - torso) * toe,
        "foot." + side: (1 - torso) * (1 - shin - toe),
        "lowerleg." + side: (1 - torso) * (shin - thigh),
        "upperleg." + side: (1 - torso) * thigh,
        "hips": torso * (1 - spine),
        "spine": torso * spine * (1 - chest),
        "chest": torso * spine * chest * (1 - head),
        "head": torso * spine * chest * head,
    }
    return fitted, weights


def arm_fit(point, side):
    x, y, z = point
    x = abs(x)
    # Unbend the A-pose in the front plane. The transverse coordinate preserves the
    # armor thickness, and the knots put the elbows, wrists and palms on Rig_Medium.
    reach = .33 * (x - .196) - .944 * (z - .252)
    cross = .944 * (x - .196) + .33 * (z - .252)
    edge = np.interp(z, (-.155, .06, .12, .23, .32), (.245, .218, .198, .174, .19))
    blend = smoothstep(edge - .012, edge + .026, x) * smoothstep(-.06, .135, reach)
    length = np.interp(reach, (-.13, 0, .145, .286, .328, .366, .412),
                       (.19, .23, .454, .713, .787, .875, .957))
    fitted = Vector((length if side == "l" else -length, y * 1.7,
                     1.107 + cross * 1.32 - .036 * smoothstep(.76, .90, length)))
    elbow = smoothstep(.409, .507, length)
    wrist = smoothstep(.661, .756, length)
    hand = smoothstep(.755, .823, length)
    return fitted, {
        "upperarm." + side: 1 - elbow,
        "lowerarm." + side: elbow - wrist,
        "wrist." + side: wrist - hand,
        "hand." + side: hand,
    }, blend


def skin(body, arm):
    assert arm.name == "Rig_Medium" and len(arm.data.bones) == 23
    groups = {bone.name: body.vertex_groups.new(name=bone.name) for bone in arm.data.bones}
    to_rig = arm.matrix_world.inverted()
    for vertex in body.data.vertices:
        source = vertex.co
        side = "l" if source.x >= 0 else "r"
        point, weights = body_fit(source, side)
        arm_point, arm_weights, blend = arm_fit(source, side)
        weights = {name: weight * (1 - blend) for name, weight in weights.items()}
        weights.update({name: weight * blend for name, weight in arm_weights.items()})
        strongest = sorted(((weight, name) for name, weight in weights.items() if weight > .001),
                           reverse=True)[:4]
        total = sum(weight for weight, _ in strongest)
        for weight, name in strongest:
            groups[name].add([vertex.index], weight / total, "REPLACE")
        vertex.co = to_rig @ point.lerp(arm_point, blend)

    body.parent = arm
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    body.modifiers.new("Rig_Medium", "ARMATURE").object = arm
    body.data.update()


if __name__ == "__main__":
    body = load_body()
    bpy.ops.import_scene.fbx(filepath=str(RIG), use_anim=False)
    arm, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    skin(body, arm)
    export(ART / "BuriedKing.fbx", [arm, body])
