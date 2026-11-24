"""Rig the seed-42 Sundial Colossus and export its event-sampled clips.

Run from the repository root with Blender 4.1:
PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1 -P Tools/Blender/sundial_colossus.py
"""

from datetime import datetime
from functools import partial
from hashlib import sha256
from math import cos, pi, radians, sin, sqrt
from os import environ
from pathlib import Path
import sys
from unittest.mock import patch

import bmesh
import bpy
from io_scene_fbx import export_fbx_bin
from mathutils import Matrix, Quaternion, Vector
import numpy as np

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
RIG = ROOT / "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx"
OUTPUT = ROOT / "Assets/_Project/Art/Enemies/SundialColossus.fbx"
SOURCE_HASH = "e4a284fde8b2a74b8d03167f36d948b2e0bd27683912f2785a73d989e187322f"
TRIANGLE_LIMIT = 24000

sys.path.insert(0, str(TOOLS))
sys.dont_write_bytecode = True
from hierophant import load_scan
from jackal import mesh_bounds, smoothstep

# Landmarks measured on the fitted scan. Blender forward is -Y and anatomical right is -X.
HEADS = {
    "root": (0, 0, 0), "hips": (0, .06, 1.87), "spine": (0, .05, 2.14),
    "chest": (0, .04, 2.52), "head": (0, .06, 3.54),
    "upperarm.r": (-.87, .06, 3.42), "lowerarm.r": (-1.08, .045, 2.60),
    "wrist.r": (-1.39, -.15, 1.19), "hand.r": (-1.43, -.23, .99),
    "handslot.r": (-1.46, -.37, .89),
    "upperarm.l": (.87, .07, 3.44), "lowerarm.l": (1.04, .06, 2.66),
    "wrist.l": (1.28, -.20, 1.75), "hand.l": (1.27, -.24, 1.58),
    "handslot.l": (1.26, -.37, 1.45),
    "upperleg.r": (-.36, .06, 1.85), "lowerleg.r": (-.51, -.09, 1.18),
    "foot.r": (-.55, .04, .39), "toes.r": (-.59, -.31, .14),
    "upperleg.l": (.37, .08, 1.85), "lowerleg.l": (.49, -.08, 1.18),
    "foot.l": (.52, .06, .39), "toes.l": (.53, -.32, .14),
}
UP = Vector((0, 0, 1))
FRONT = Vector((0, -1, 0))
SIDE = Vector((1, 0, 0))
IDENTITY = Quaternion()


def fit_scan(low, high):
    return Matrix.Diagonal((3.33, 3.55, 4.0 / (high.z - low.z), 1)) @ Matrix.Translation(
        Vector((-.05, .025, -low.z))
    )


def bake_textures(source, body):
    material, = source.data.materials
    shader, = (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    albedo = shader.inputs["Base Color"].links[0].from_socket
    split = shader.inputs["Roughness"].links[0].from_node
    surface = split.inputs[0].links[0].from_socket
    emission = material.node_tree.nodes.new("ShaderNodeEmission")
    output, = (node for node in material.node_tree.nodes if node.type == "OUTPUT_MATERIAL")
    material.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])

    target = bpy.data.materials.new("colossus")
    target.use_nodes = True
    body.data.materials.clear()
    body.data.materials.append(target)
    target_node = target.node_tree.nodes.new("ShaderNodeTexImage")
    target.node_tree.nodes.active = target_node

    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=radians(70), island_margin=0)
    bpy.ops.uv.pack_islands(shape_method="AABB", rotate_method="CARDINAL", margin=.006)
    bpy.ops.object.mode_set(mode="OBJECT")
    source.select_set(True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False

    def bake(socket, suffix, colour_space, alpha):
        path = OUTPUT.with_name(f"T_SundialColossus_{suffix}.png")
        image = bpy.data.images.new(path.stem, 2048, 2048, alpha=alpha)
        image.colorspace_settings.name = colour_space
        if alpha:
            image.alpha_mode = "CHANNEL_PACKED"
        image.filepath_raw = str(path)
        image.file_format = "PNG"
        material.node_tree.links.new(socket, emission.inputs["Color"])
        target_node.image = image
        bpy.ops.object.bake(type="EMIT", use_selected_to_active=True, cage_extrusion=.055,
                            max_ray_distance=.12, margin=6, margin_type="EXTEND")
        return image

    bake(albedo, "Albedo", "sRGB", False).save()
    packed = bake(surface, "MetallicSmoothness", "Non-Color", True)

    # glTF stores metallic in blue and roughness in green. URP reads metallic
    # from red and smoothness from alpha, with both channels sampled as linear data.
    pixels = np.empty((packed.size[0] * packed.size[1], 4), dtype=np.float32)
    packed.pixels.foreach_get(pixels.ravel())
    pixels[:, 3] = 1 - pixels[:, 1]
    pixels[:, :3] = pixels[:, 2:3].copy()
    packed.pixels.foreach_set(pixels.ravel())
    packed.save()
    target.use_nodes = False


def reduce_scan(body):
    # The source has overlapping sheets that prevent edge collapse. Join their outer
    # surface at a fine resolution before baking the original textures onto it.
    for layer in list(body.data.uv_layers):
        body.data.uv_layers.remove(layer)
    mesh = bmesh.new()
    mesh.from_mesh(body.data)
    bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=.00001)
    bmesh.ops.dissolve_degenerate(mesh, edges=list(mesh.edges), dist=.00001)
    bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
    mesh.to_mesh(body.data)
    mesh.free()
    bpy.context.view_layer.objects.active = body
    shell = body.modifiers.new("Close the scan sheets", "SOLIDIFY")
    shell.thickness = .024
    shell.offset = 0
    bpy.ops.object.modifier_apply(modifier=shell.name)
    surface = body.modifiers.new("Join the scan surface", "REMESH")
    surface.mode = "VOXEL"
    surface.voxel_size = .009
    bpy.ops.object.modifier_apply(modifier=surface.name)
    mesh = bmesh.new()
    mesh.from_mesh(body.data)
    remaining = set(mesh.verts)
    largest = []
    while remaining:
        connected = [remaining.pop()]
        for vertex in connected:
            for edge in vertex.link_edges:
                neighbour = edge.other_vert(vertex)
                if neighbour in remaining:
                    remaining.remove(neighbour)
                    connected.append(neighbour)
        if len(connected) > len(largest):
            largest = connected
    exterior = set(largest)
    bmesh.ops.delete(mesh, geom=[vertex for vertex in mesh.verts if vertex not in exterior], context="VERTS")
    mesh.to_mesh(body.data)
    mesh.free()
    body.data.calc_loop_triangles()
    reduce = body.modifiers.new("Runtime triangle budget", "DECIMATE")
    reduce.ratio = min(1, TRIANGLE_LIMIT / len(body.data.loop_triangles))
    bpy.ops.object.modifier_apply(modifier=reduce.name)
    body.data.update()


def arm_weight(point, side):
    x, y, z = point
    x = abs(x)
    if side == "r":
        # The long blade overlaps the upper arm in silhouette. Its forward face and its
        # outside edge belong to the elbow so the entire blade sweeps as a solid wedge.
        boundary = (.84 + .12 * (1 - smoothstep(.63, .83, z))
                    - .12 * smoothstep(1.2, 2.11, z) + .10 * smoothstep(2.6, 3.08, z))
        blade = max(1 - smoothstep(2.35, 2.67, z),
                    smoothstep(.94, 1.15, x) * (1 - smoothstep(3.15, 3.43, z)),
                    (1 - smoothstep(-.31, -.14, y)) * (1 - smoothstep(3.05, 3.35, z)))
        shoulder = smoothstep(2.4, 2.95, z)
        rear = smoothstep(.22, .72, y) * shoulder
        return smoothstep(boundary - .05 * shoulder, boundary + .06 + .16 * shoulder, x) * (1 - rear), {
            "lowerarm.r": blade, "upperarm.r": 1 - blade,
        }
    boundary = .65 + .14 * smoothstep(2.1, 2.9, z) + .18 * (1 - smoothstep(1.3, 1.62, z))
    elbow = 1 - smoothstep(2.45, 2.82, z)
    wrist = 1 - smoothstep(1.59, 1.90, z)
    return smoothstep(boundary, boundary + .15, x), {
        "upperarm.l": 1 - elbow, "lowerarm.l": elbow - wrist, "hand.l": wrist,
    }


def body_weight(point):
    x, _, z = point
    side = "r" if x < 0 else "l"
    arm, weights = arm_weight(point, side)
    arm *= smoothstep(.57, .63, z) if side == "r" else smoothstep(1.04, 1.14, z)
    hips = smoothstep(1.62, 1.95, z)
    knee = smoothstep(1.04, 1.32, z)
    ankle = smoothstep(.47, .66, z)
    chest = smoothstep(2.06, 2.42, z)
    # The wide shoulder crowns overlap the head's height.
    head = smoothstep(3.43, 3.62, z) * (1 - smoothstep(.24, .42, abs(x)))
    rest = {
        "foot." + side: 1 - ankle, "lowerleg." + side: ankle - knee,
        "upperleg." + side: knee - hips, "hips": hips - chest,
        "chest": chest - head, "head": head,
    }
    return {**{bone: weight * (1 - arm) for bone, weight in rest.items()},
            **{bone: weight * arm for bone, weight in weights.items()}}


def fit_rig(body):
    bpy.ops.import_scene.fbx(filepath=str(RIG), use_anim=False)
    rig, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for obj in list(bpy.context.scene.objects):
        if obj not in (body, rig):
            bpy.data.objects.remove(obj, do_unlink=True)
    assert rig.name == "Rig_Medium" and set(rig.data.bones.keys()) == HEADS.keys()

    # Bake the KayKit axis conversion into the bone data before fitting the rest pose.
    rig.data.transform(rig.matrix_world)
    rig.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    bones = rig.data.edit_bones
    # Set each tail toward the next joint, retaining KayKit's local roll reference.
    for name, head in HEADS.items():
        bone = bones[name]
        reference = bone.z_axis.copy()
        old_direction = (bone.tail - bone.head).normalized()
        child = next((item for item in bone.children if not item.name.startswith("handslot")), None)
        new_tail = Vector(HEADS[child.name]) if child else Vector(head) + old_direction * .21
        bone.use_connect = False
        bone.head = head
        bone.tail = new_tail
        bone.align_roll(reference)

    blade = [v.co.copy() for v in body.data.vertices
             if v.co.x < -.93 and v.co.z < 2.48 and body_weight(v.co).get("lowerarm.r", 0) > .99]
    blade_tip = min(blade, key=lambda point: point.z)
    for name, parent, point in (("HandTip", "lowerarm.r", blade_tip),
                                ("FootContact", "foot.r", Vector((-.56, -.22, 0)))):
        marker = bones.new(name)
        marker.head = point
        marker.tail = point + UP * .1
        marker.parent = bones[parent]
        marker.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")

    groups = {name: body.vertex_groups.new(name=name) for name in HEADS}
    for vertex in body.data.vertices:
        weights = body_weight(vertex.co)
        active = sorted(((weight, name) for name, weight in weights.items() if weight > .001),
                        reverse=True)[:4]
        total = sum(weight for weight, _ in active)
        for weight, name in active:
            groups[name].add([vertex.index], weight / total, "REPLACE")

    body.parent = rig
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    body.modifiers.new("Rig_Medium", "ARMATURE").object = rig
    return rig, blade


def joint_position(origin, target, upper_length, lower_length, pole):
    line = target - origin
    distance = min(line.length, upper_length + lower_length - .0001)
    direction = line.normalized()
    along = (upper_length ** 2 - lower_length ** 2 + distance ** 2) / (2 * distance)
    height = sqrt(max(0, upper_length ** 2 - along ** 2))
    bend = (pole - direction * pole.dot(direction)).normalized()
    return origin + along * direction + height * bend


class Poses:
    def __init__(self, rig, blade):
        self.rig = rig
        self.bones = rig.pose.bones
        self.rest = {bone.name: bone.matrix_local.copy() for bone in rig.data.bones}
        self.blade = blade

    def refresh(self):
        bpy.context.view_layer.update()

    def position(self, name):
        return self.bones[name].matrix.translation.copy()

    def put(self, name, point, rotation):
        rest = self.rest[name].copy()
        posed = rotation.to_matrix().to_4x4() @ rest
        posed.translation = point
        self.bones[name].matrix = posed
        self.refresh()

    def aim(self, name, origin, child, target):
        source = self.rest[child].translation - self.rest[name].translation
        authored = self.bones[name].matrix.to_quaternion() @ self.rest[name].to_quaternion().inverted()
        direction = authored @ source
        rotation = direction.rotation_difference(target - origin) @ authored
        self.put(name, origin, rotation)

    def lower_body(self, drop, right, left):
        self.put("hips", self.rest["hips"].translation - UP * drop, IDENTITY)
        for side, offset in (("r", right), ("l", left)):
            upper, shin, foot = ("upperleg." + side, "lowerleg." + side, "foot." + side)
            a, b, c = (self.rest[name].translation for name in (upper, shin, foot))
            target = c + offset
            joint = joint_position(self.position(upper), target, (b - a).length, (c - b).length, FRONT)
            self.aim(upper, self.position(upper), shin, joint)
            self.aim(shin, self.position(shin), foot, target)
            self.put(foot, target, IDENTITY)

    def chest(self, pitch, twist=0):
        rotation = Quaternion(UP, radians(twist)) @ Quaternion(SIDE, radians(pitch))
        self.put("chest", self.position("chest"), rotation)

    def arm(self, target):
        upper, elbow, tip = "upperarm.r", "lowerarm.r", "HandTip"
        a, b, c = (self.rest[name].translation for name in (upper, elbow, tip))
        joint = joint_position(self.position(upper), target, (b - a).length, (c - b).length,
                               Vector((-1, -.8, .35)))
        self.aim(upper, self.position(upper), elbow, joint)
        self.aim(elbow, self.position(elbow), tip, target)

    def sweep_contact(self, bearing, reach, clearance, weight):
        angle = radians(bearing)
        center = Vector((-sin(angle) * reach, -cos(angle) * reach, clearance))
        idle = self.position("HandTip")
        target = idle.lerp(center, weight)
        # The very end of the blade is broad. Use its actual deformed sole instead of
        # assuming the reference tip is still its lowest point during the strike.
        for _ in range(6):
            self.arm(target)
            matrix = self.bones["lowerarm.r"].matrix @ self.rest["lowerarm.r"].inverted()
            bottom = min((matrix @ point).z for point in self.blade)
            target.z += (clearance - bottom) * weight

    def idle(self, t):
        self.chest(1.3 * sin(t * 2 * pi))

    def walk(self, t):
        def stride(phase):
            travel = cos(2 * pi * phase)
            lift = max(0, -sin(2 * pi * phase))
            return FRONT * (.57 * travel) + UP * (.24 * lift ** 1.4)

        self.lower_body(.11 + .04 * cos(4 * pi * t), stride(t), stride(t + .5))
        self.chest(2, 4 * sin(2 * pi * t))

    def tell(self, t, reverse):
        load = smoothstep(0, 1, t)
        bearing = 20 * (1 if reverse else -1)
        self.lower_body(.30 * load, Vector(), Vector())
        self.chest(12 * load, (13 - .5 * bearing) * load)
        self.sweep_contact(bearing, 1.75, 1.1, load)

    def strike(self, t, reverse):
        # ColossusView samples at (sector + .5) / 3 in both directions. Return
        # therefore traverses this clip from its last slot to its first.
        bearing = 60 * (t - .5)
        end = (1 - t) if reverse else t
        reach = 1.69 + .13 * smoothstep(.50, .83, end)
        self.lower_body(.44, Vector(), Vector())
        self.chest(50, 30 - bearing * .6)
        self.sweep_contact(bearing, reach, .025, 1)

    def stomp(self, t, striking):
        if striking:
            advance = .55
            lift = .52 * (1 - smoothstep(0, .85, t))
            pitch = 9 + 10 * smoothstep(.7, 1, t)
        else:
            progress = smoothstep(0, 1, t)
            advance = .55 * progress
            lift = .52 * progress
            pitch = 9 * progress
        self.lower_body(.13 * smoothstep(0, .45, t) if not striking else .13,
                        FRONT * advance + UP * lift, Vector())
        self.chest(pitch, -6 * (1 if striking else progress))

    def recover(self, t):
        # The view holds the impact frame until the core recovery expires, then
        # crossfades from it into this settling pose.
        self.lower_body(.09 * (1 - t), Vector(), Vector())
        self.chest(5 * (1 - t))

    def action(self, name, frames, pose):
        action = bpy.data.actions.new(name)
        self.rig.animation_data.action = action
        for frame in range(frames + 1):
            for bone in self.bones:
                bone.rotation_mode = "QUATERNION"
                bone.matrix_basis = Matrix.Identity(4)
            self.refresh()
            pose(frame / frames)
            for bone in self.bones:
                if bone.name not in HEADS or bone.name == "root":
                    continue
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
                bone.keyframe_insert("location", frame=frame, group=bone.name)
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"


def build():
    if environ.get("PYTHONHASHSEED") != "0":
        raise RuntimeError("Set PYTHONHASHSEED=0 before starting Blender.")
    source = load_scan("SundialColossus", SOURCE_HASH, fit_scan)
    body = source.copy()
    body.data = source.data.copy()
    bpy.context.scene.collection.objects.link(body)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    reduce_scan(body)
    bake_textures(source, body)
    low, _ = mesh_bounds([body])
    body.data.transform(Matrix.Translation(-UP * low.z))
    body.name = body.data.name = "SundialColossus_Body"
    rig, blade = fit_rig(body)
    rig.animation_data_create()
    bpy.context.scene.render.fps = 30
    poses = Poses(rig, blade)
    poses.action("Idle", 60, poses.idle)
    poses.action("Walk", 33, poses.walk)
    poses.action("SweepTell", 42, partial(poses.tell, reverse=False))
    poses.action("SweepStrike", 36, partial(poses.strike, reverse=False))
    poses.action("ReturnTell", 30, partial(poses.tell, reverse=True))
    poses.action("ReturnStrike", 36, partial(poses.strike, reverse=True))
    poses.action("StompTell", 30, partial(poses.stomp, striking=False))
    poses.action("StompStrike", 12, partial(poses.stomp, striking=True))
    poses.action("Recover", 9, poses.recover)
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    poses.refresh()

    # This unanimated node gives the view its existing sector-alignment control.
    pivot = bpy.data.objects.new("SweepPivot", None)
    bpy.context.scene.collection.objects.link(pivot)
    # Author the pivot in Y-up coordinates so its local yaw is Unity world up.
    pivot.rotation_euler.x = pi / 2
    poses.refresh()
    rig.parent = pivot
    rig.matrix_world = Matrix.Identity(4)
    bpy.ops.object.select_all(action="DESELECT")
    pivot.select_set(True)
    rig.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    header = partial(export_fbx_bin.fbx_header_elements, time=datetime(1970, 1, 1))
    with patch.object(export_fbx_bin, "fbx_header_elements", header):
        bpy.ops.export_scene.fbx(
            filepath=str(OUTPUT), use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
            apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", use_mesh_modifiers=False,
            add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype="NULL",
            bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, path_mode="STRIP",
        )


if __name__ == "__main__":
    build()
