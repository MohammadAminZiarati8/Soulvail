"""Build the Sunscar scorpion from smooth, overlapping KayKit-style shells.

From the repository root, with Blender 4.1 and PYTHONHASHSEED=0:

    blender --background --factory-startup --python Tools/Blender/scorpion.py
        -- --turnaround Temp/scorpion_turnaround.png

Without the turnaround flag, writes Scorpion.fbx and the shared enemy atlas.
The seed 42 reference was measured in a separate Blender scene. This generator uses
authored geometry and palette only. Coordinates are metres, Z up and -Y forward.
"""

import argparse
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from enemy_atlas import SWATCHES, build_atlas, uv_for
from frog import ball, capsule, ik2, outward


# Measurements in units where the reference is 2 m long: 1.50 m across, 1.37 m
# tall, with a ~0.94 x 0.74 x 0.49 m core. Each claw assembly spans ~0.70 m
# fore/aft. The tail begins ~0.55 m aft of centre, sweeps back to ~1 m,
# then returns to centre. The main leg roots sit near y=0.18 and -0.17,
# with a short forward pair tucked near the claw arms.
REVIEW_EXTENT = (1.496, 2.0, 1.373)
TAIL = ((0, 0.48, 0.27), (0, 0.74, 0.34), (0, 0.86, 0.54),
        (0, 0.72, 0.75), (0, 0.46, 0.86), (0, 0.15, 0.84))


STINGER_BULB = (0, 0.062, 0.807)
STINGER_TIP = (0, -0.165, 0.627)


def leg_joints(side):
    return [((side * 0.247, y, 0.235), (side * knee_x, knee_y, 0.178),
             (side * foot_x, foot_y, 0.042))
            for y, knee_y, foot_y, knee_x, foot_x in (
                (-0.292, -0.43, -0.56, 0.42, 0.49),
                (-0.095, -0.135, -0.23, 0.57, 0.68),
                (0.23, 0.30, 0.36, 0.55, 0.70))]


def shells():
    parts = []

    def add(name, shape, swatch):
        verts, faces = shape
        parts.append((name, verts, outward(verts, faces), swatch))

    def oval(name, center, half, swatch, segments=10, rings=6, tilt=0):
        verts, faces = ball(segments, rings)
        rotation = Quaternion((1, 0, 0), tilt)
        origin = Vector(center)
        add(name, ([origin + rotation @ Vector(tuple(p[i] * half[i] for i in range(3)))
                    for p in verts], faces), swatch)

    def limb(name, start, end, r1, r2, swatch, sides=8):
        add(name, capsule(Vector(start), Vector(end), r1, r2, segments=sides, cap=1), swatch)

    def taper(name, profile, swatch):
        # A rounded root, elliptical cross sections along the bend, and a closed tip.
        sides = 8
        centers = [Vector(center) for center, _, _ in profile]
        verts = [centers[0]]
        for i, (_, width, height) in enumerate(profile[1:-1], 1):
            tangent = (centers[i + 1] - centers[i - 1]).normalized()
            lateral = tangent.cross(Vector((0, 0, 1))).normalized()
            up = lateral.cross(tangent)
            for j in range(sides):
                angle = math.tau * j / sides
                verts.append(centers[i] + lateral * (width * math.cos(angle))
                             + up * (height * math.sin(angle)))
        verts.append(centers[-1])
        faces = []
        rings = len(profile) - 2
        for j in range(sides):
            k = (j + 1) % sides
            faces.extend(((0, 1 + j, 1 + k),
                          (len(verts) - 1, 1 + (rings - 1) * sides + k,
                           1 + (rings - 1) * sides + j)))
            for ring in range(rings - 1):
                a = 1 + ring * sides
                faces.append((a + j, a + k, a + sides + k, a + sides + j))
        add(name, (verts, faces), swatch)

    oval('Abdomen', (0, 0.13, 0.27), (0.365, 0.46, 0.205), 'scorp_ochre', 12, 7)
    oval('Head', (0, -0.29, 0.249), (0.312, 0.255, 0.172), 'scorp_sandstone', 12, 6)
    for index, (y, z, width, tilt) in enumerate((
        (0.36, 0.411, 0.258, -0.25), (0.17, 0.462, 0.337, -0.10),
        (-0.039, 0.458, 0.348, 0.12), (-0.241, 0.418, 0.305, 0.29),
        (-0.409, 0.36, 0.236, 0.37)), 1):
        oval(f'Carapace {index}', (0, y, z), (width, 0.139, 0.068),
             'scorp_sandstone' if index == 5 else 'scorp_plate', 10, 5, tilt)

    for side, suffix in ((1, 'L'), (-1, 'R')):
        def point(x, y, z):
            return (side * x, y, z)

        oval(f'Eye {suffix}', point(0.117, -0.522, 0.266),
             (0.036, 0.029, 0.037), 'scorp_plate', 8, 4)
        for index, (hip, knee, foot) in enumerate(leg_joints(side), 1):
            limb(f'Leg {index} upper {suffix}', hip, knee, 0.082, 0.074, 'scorp_ochre')
            limb(f'Leg {index} lower {suffix}', knee, foot, 0.069, 0.034, 'scorp_sandstone')

        limb(f'Pincer arm {suffix}', point(0.23, -0.34, 0.235),
             point(0.49, -0.61, 0.16), 0.088, 0.119, 'scorp_ochre')
        oval(f'Pincer palm {suffix}', point(0.525, -0.666, 0.146),
             (0.171, 0.184, 0.113), 'scorp_sandstone', 12, 6, 0.26)
        taper(f'Outer claw {suffix}', [
            (point(0.579, -0.62, 0.159), 0, 0),
            (point(0.619, -0.737, 0.131), 0.124, 0.095),
            (point(0.595, -0.855, 0.102), 0.095, 0.068),
            (point(0.502, -0.929, 0.084), 0.057, 0.04),
            (point(0.38, -0.994, 0.072), 0, 0),
        ], 'scorp_sandstone')
        taper(f'Inner claw {suffix}', [
            (point(0.465, -0.647, 0.155), 0, 0),
            (point(0.418, -0.708, 0.141), 0.075, 0.066),
            (point(0.338, -0.768, 0.113), 0.052, 0.04),
            (point(0.309, -0.853, 0.089), 0, 0),
        ], 'scorp_bone')

    for index, (start, end, radius) in enumerate(zip(
            TAIL, TAIL[1:], (0.167, 0.142, 0.132, 0.121, 0.110)), 1):
        a, b = Vector(start), Vector(end)
        middle = (a + b) * 0.5
        delta = b - a
        # Inset the capsule ends along the axis so only the rounded caps overlap.
        inset = min(0.28, radius * 0.56 / delta.length)
        limb(f'Tail {index}', middle - delta * (0.5 - inset),
             middle + delta * (0.5 - inset), radius, radius * 0.92,
             'scorp_sandstone' if index % 2 == 0 else 'scorp_ochre', 10)

    oval('Stinger bulb', STINGER_BULB, (0.148, 0.172, 0.128),
         'scorp_plate', 12, 6, 0.31)
    taper('Stinger hook', [
        ((0, 0.011, 0.792), 0, 0),
        ((0, -0.085, 0.764), 0.067, 0.064),
        ((0, -0.139, 0.706), 0.042, 0.043),
        (STINGER_TIP, 0, 0),
    ], 'scorp_bone')
    return parts


def preview_material(name, color):
    material = bpy.data.materials.new(name)
    srgb = [int(color[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    material.diffuse_color = (*(c / 12.92 if c <= 0.04045 else
                                ((c + 0.055) / 1.055) ** 2.4 for c in srgb), 1)
    return material


def turnaround(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = shells()
    materials = {name: preview_material(name, color) for name, (_, _, color, _) in SWATCHES.items()
                 if name.startswith('scorp_')}
    triangle_count = 0
    points = []
    for name, verts, faces, swatch in parts:
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(verts, [], faces)
        mesh.update()
        mesh.materials.append(materials[swatch])
        for face in mesh.polygons:
            face.use_smooth = True
        mesh.calc_loop_triangles()
        triangle_count += len(mesh.loop_triangles)
        points.extend(verts)
        bpy.context.scene.collection.objects.link(bpy.data.objects.new(name, mesh))
    width, length, height = np.ptp(np.array(points), axis=0)
    print(f'Scorpion: {len(parts)} shells, {triangle_count} triangles, '
          f'L/W/H={length:.3f}/{width:.3f}/{height:.3f} m', flush=True)
    assert 400 <= triangle_count <= 2500
    assert length >= 2 * height
    render_sheet(path, REVIEW_EXTENT)


def render_sheet(path, extent, front=-1):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = 'BOTH'
    scene.display.shading.background_type = 'WORLD'
    scene.world = bpy.data.worlds.new('Review world')
    scene.world.color = (0.11, 0.11, 0.11)
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = scene.render.resolution_y = 480
    scene.render.resolution_percentage = 100
    camera_data = bpy.data.cameras.new('Review')
    camera = bpy.data.objects.new('Review', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = 1.12 * max(extent)
    target = Vector((0, 0, extent[2] / 2))
    sheet = np.empty((960, 960, 4), np.float32)
    tile = path.with_name('_scorpion_tile.png')
    for index, direction in enumerate(((0, front, 0), (1, 0, 0), (0, -front, 0), (0, 0, 1))):
        camera.location = target + Vector(direction) * 5
        camera.rotation_euler = (-Vector(direction)).to_track_quat('-Z', 'Y').to_euler()
        if index == 3:
            camera.rotation_euler.z = math.pi
        scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(str(tile), check_existing=False)
        y = (1 - index // 2) * 480
        x = (index % 2) * 480
        pixels = np.empty(480 * 480 * 4, np.float32)
        img.pixels.foreach_get(pixels)
        sheet[y:y+480, x:x+480] = pixels.reshape(480, 480, 4)
        bpy.data.images.remove(img)
    tile.unlink()
    output = bpy.data.images.new('Turnaround', 960, 960)
    output.pixels.foreach_set(sheet.ravel())
    output.filepath_raw = str(path)
    output.file_format = 'PNG'
    output.save()


def rig_parts(parts, joints=leg_joints, tail_points=TAIL,
              stinger=(STINGER_BULB, STINGER_TIP)):
    bones = {'Root': (None, (0, 0, 0), (0, 0, 0.15)),
             'Body': ('Root', (0, 0, 0.27), (0, -0.32, 0.27))}
    groups = {name: 'Body' for name, _, _, _ in parts}
    legs = []
    for side, suffix in ((1, 'L'), (-1, 'R')):
        pincer = f'Pincer_{suffix}'
        bones[pincer] = ('Body', (side * 0.23, -0.34, 0.235),
                        (side * 0.49, -0.61, 0.16))
        for piece in ('Pincer arm', 'Pincer palm', 'Outer claw', 'Inner claw'):
            groups[f'{piece} {suffix}'] = pincer
        for index, (hip, knee, foot) in enumerate(joints(side), 1):
            upper = f'Leg_{suffix}_{index}_Upper'
            lower = f'Leg_{suffix}_{index}_Lower'
            bones[upper] = ('Body', hip, knee)
            bones[lower] = (upper, knee, foot)
            groups[f'Leg {index} upper {suffix}'] = upper
            groups[f'Leg {index} lower {suffix}'] = lower
            legs.append((upper, lower, side, index, Vector(hip), Vector(knee), Vector(foot)))
    parent = 'Body'
    for i, (head, tip) in enumerate(zip(tail_points, tail_points[1:]), 1):
        name = f'Tail_{i}'
        bones[name] = (parent, head, tip)
        groups[f'Tail {i}'] = name
        parent = name
    bones['Stinger'] = (parent, *stinger)
    groups['Stinger bulb'] = groups['Stinger hook'] = 'Stinger'
    return bones, groups, legs


def assemble(parts, groups):
    vertices, faces, swatches = [], [], []
    mesh = bpy.data.meshes.new('Scorpion_Body')
    for name, verts, shell_faces, swatch in parts:
        offset = len(vertices)
        vertices.extend(verts)
        faces.extend(tuple(offset + i for i in face) for face in shell_faces)
        top = max(p.z for p in verts)
        depth = top - min(p.z for p in verts)
        swatches.extend(uv_for(swatch, 0.42 + 0.58 * (top - p.z) / depth) for p in verts)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    material = bpy.data.materials.new('enemy')
    mesh.materials.append(material)
    uv = mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        face.use_smooth = True
        for index in face.loop_indices:
            uv.data[index].uv = swatches[mesh.loops[index].vertex_index]
    mesh.calc_loop_triangles()
    assert 400 <= len(mesh.loop_triangles) <= 2500 and len(mesh.materials) == 1
    body = bpy.data.objects.new(mesh.name, mesh)
    bpy.context.scene.collection.objects.link(body)
    offset = 0
    for name, verts, _, _ in parts:
        bone = groups[name]
        group = body.vertex_groups.get(bone) or body.vertex_groups.new(name=bone)
        group.add(list(range(offset, offset + len(verts))), 1.0, 'REPLACE')
        offset += len(verts)
    assert all(len(vertex.groups) == 1 and vertex.groups[0].weight == 1
               for vertex in mesh.vertices)
    return body


def armature(bones, body):
    data = bpy.data.armatures.new('Rig_Scorpion')
    arm = bpy.data.objects.new(data.name, data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (parent, head, tip) in bones.items():
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tip
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    body.parent = arm
    modifier = body.modifiers.new('Rigid shells', 'ARMATURE')
    modifier.object = arm
    return arm


def turned_about(pivot, rotation):
    return Matrix.Translation(pivot) @ rotation.to_matrix().to_4x4() @ Matrix.Translation(-pivot)


def aimed(rest, head, direction):
    rest_direction = rest.to_3x3() @ Vector((0, 1, 0))
    rotation = rest_direction.rotation_difference(direction.normalized())
    return Matrix.Translation(head) @ (rotation @ rest.to_quaternion()).to_matrix().to_4x4()


def pose(arm, legs, *, shift=(0, 0, 0), pitch=0, roll=0, pinch=0,
         gait=None, tail=None, settle=False, tail_points=TAIL,
         stinger=(STINGER_BULB, STINGER_TIP)):
    rest = {bone.name: bone.bone.matrix_local for bone in arm.pose.bones}
    body_turn = Quaternion((0, 1, 0), roll) @ Quaternion((1, 0, 0), pitch)
    body = Matrix.Translation(shift) @ turned_about(rest['Body'].translation, body_turn)
    matrices = {'Root': rest['Root'], 'Body': body @ rest['Body']}
    for side, suffix in ((1, 'L'), (-1, 'R')):
        name = f'Pincer_{suffix}'
        turn = Quaternion((0, 0, 1), side * pinch)
        matrices[name] = body @ turned_about(rest[name].translation, turn) @ rest[name]
    for upper, lower, side, index, hip, knee, foot in legs:
        target = foot.copy()
        pole = knee - hip
        if gait is not None:
            target.y = hip.y
            target.x -= 0.025 * side
            phase = (gait + (0.5 if (index + (side < 0)) % 2 else 0)) % 1
            if phase < 0.5:
                target.y += -0.22 + 0.88 * phase
            else:
                swing = 2 * phase - 1
                target.y += 0.22 - 0.44 * swing
                target.z += 0.14 * math.sin(math.pi * swing)
        if roll:
            target = body @ (hip + (foot - hip) * (1 - 0.45 * abs(roll) / math.pi))
            target.z += 0.03 * abs(roll)
            pole = body.to_3x3() @ pole
        start = body @ hip
        bend, end = ik2(start, target, (knee - hip).length, (foot - knee).length, pole)
        matrices[upper] = aimed(rest[upper], start, bend - start)
        matrices[lower] = aimed(rest[lower], bend, end - bend)

    head = body @ Vector(tail_points[0])
    for i, (start, end) in enumerate(zip(tail_points, tail_points[1:]), 1):
        name = f'Tail_{i}'
        axis = Vector(end) - Vector(start)
        if tail is None:
            matrices[name] = body @ rest[name]
            head = body @ Vector(end)
        else:
            angle = math.radians(tail[i - 1])
            direction = Vector((0, -math.cos(angle), math.sin(angle)))
            matrices[name] = aimed(rest[name], head, direction)
            head += direction * axis.length
    if tail is None:
        matrices['Stinger'] = body @ rest['Stinger']
    else:
        head = matrices['Tail_5'] @ rest['Tail_5'].inverted() @ Vector(stinger[0])
        reach = (Vector(stinger[1]) - Vector(stinger[0])).length
        angle = math.radians(tail[-1])
        if settle:
            angle = math.asin(max(-1, min(1, (0.035 - head.z) / reach)))
        direction = Vector((0, -math.cos(angle), math.sin(angle)))
        matrices['Stinger'] = aimed(rest['Stinger'], head, direction)

    for bone in arm.pose.bones:
        name = bone.name
        if name not in matrices:
            continue
        basis = rest[name].inverted()
        if bone.parent:
            parent = bone.parent.name
            basis = basis @ rest[parent] @ matrices[parent].inverted()
        bone.rotation_mode = 'QUATERNION'
        bone.matrix_basis = basis @ matrices[name]


CLIPS = {'Idle': 48, 'Skitter': 12, 'Windup': 18, 'Strike': 24, 'Hit': 12, 'Death': 30}
COCKED = (146, 118, 84, 42, 5, -60)
EXTENDED = (37, 11, -12, -30, -27, -24)
DOWN = (30, 6, -13, -30, -10, -24)


def clips(*, cocked=COCKED, extended=EXTENDED, down=DOWN,
          windup=(148, 110, 60, 9, -10, -43), settle=True):
    return {
        'Idle': {0: {}, 12: dict(shift=(0, 0, 0.018), pinch=0.08),
                 18: dict(shift=(0, 0, 0.012), pinch=-0.08), 24: {}, 48: {}},
        'Skitter': {frame: dict(gait=frame / 12, shift=(0, 0, -0.035),
                               pinch=0.06 * math.sin(frame * math.tau / 12))
                    for frame in range(13)},
        'Windup': {0: {}, 7: dict(shift=(0, 0.045, -0.016), tail=windup),
                   14: dict(shift=(0, 0.07, -0.025), tail=cocked, pinch=0.13),
                   18: dict(shift=(0, 0.07, -0.025), tail=cocked, pinch=0.13)},
        'Strike': {0: dict(shift=(0, 0.07, -0.025), tail=cocked, pinch=0.13),
                   2: dict(shift=(0, -0.10, 0.055), tail=extended, settle=settle, pinch=-0.11),
                   5: dict(shift=(0, -0.02, 0.03), tail=down, settle=settle),
                   24: dict(shift=(0, -0.02, 0.03), tail=down, settle=settle)},
        'Hit': {0: {}, 2: dict(shift=(0, 0.077, 0.025), pitch=-0.17, pinch=0.22),
                6: dict(shift=(0, 0.024, 0), pitch=0.06), 12: {}},
        'Death': {0: {}, 5: dict(shift=(0.02, 0.04, 0.12), roll=0.66, pinch=0.2),
                  12: dict(shift=(0.045, 0.05, 0.36), roll=1.85, pinch=0.35),
                  19: dict(shift=(0.07, 0.05, 0.421), roll=math.pi, pinch=0.36),
                  30: dict(shift=(0.07, 0.05, 0.421), roll=math.pi, pinch=0.42)},
    }


def animate(arm, legs, *, unkeyed=(), clip_options=None, **rest):
    arm.animation_data_create()
    for name, keys in clips(**(clip_options or {})).items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame, parameters in keys.items():
            pose(arm, legs, **parameters, **rest)
            for bone in arm.pose.bones:
                if bone.name in unkeyed:
                    continue
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert('location', frame=frame, group=bone.name)
                bone.keyframe_insert('rotation_quaternion', frame=frame, group=bone.name)
        assert max(keys) == CLIPS[name]
        action.frame_range = (0, CLIPS[name])
        action.use_frame_range = True
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = 'LINEAR'
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def export_model():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = shells()
    bones, groups, legs = rig_parts(parts)
    body = assemble(parts, groups)
    arm = armature(bones, body)
    animate(arm, legs)
    arm.select_set(True)
    body.select_set(True)
    bpy.context.scene.render.fps = 30
    destination = Path(__file__).resolve().parents[2] / 'Assets/_Project/Art/Enemies'
    build_atlas(str(destination / 'Textures'))
    bpy.ops.export_scene.fbx(
        filepath=str(destination / 'Scorpion.fbx'), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_simplify_factor=0.0, path_mode='STRIP',
    )



def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--turnaround', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.turnaround:
        turnaround(args.turnaround.resolve())
    else:
        export_model()


if __name__ == '__main__':
    main()
