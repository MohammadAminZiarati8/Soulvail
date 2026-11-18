"""Bake Duneclaw from the shipped Scorpion shell model (Blender 4.1).

From the repository root with PYTHONHASHSEED=0:
    blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/duneclaw.py
    blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/duneclaw.py
        -- --turnaround Temp/duneclaw-review

The turnaround directory gets front, sides, rear, top and gameplay-angle views.
The default command exports only Duneclaw.fbx. The Scorpion and atlas are shared inputs.
"""

import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.dont_write_bytecode = True
import scorpion
from enemy_atlas import SWATCHES
from frog import ik2


SCALE = 1.55
RIGHT_SHOULDER = Vector((-0.23, -0.34, 0.235))
RIGHT_WRIST = Vector((-0.49, -0.61, 0.16))
SWEEP_READY = dict(claw=-0.56, lift=0.18, shift=(0.015, 0.028, -0.024))
SLAM_READY = dict(tail=(152, 121, 75, 31, -2, -49), pitch=-0.16, shift=(0, 0.035, 0.036))
SLAM_DOWN = dict(tail=(68, 29, 4, -16, -52, -58), settle=True, shift=(0, -0.025, -0.060))
TAIL_LOCK = ('Tail_5', 'Stinger')


def right_arm_point(point):
    return scorpion.turned_about(RIGHT_SHOULDER, Quaternion((0, 0, 1), -0.14)) @ point


def leg_joints(side):
    joints = scorpion.leg_joints(side)
    hip, _, _ = joints[0]
    joints[0] = (hip, (side * 0.56, -0.34, 0.189), (side * 0.79, -0.40, 0.042))
    return joints


def refit_shell(points, start, end, new_start, new_end):
    start, end, new_start, new_end = map(Vector, (start, end, new_start, new_end))
    old_axis = (end - start).normalized()
    new_axis = (new_end - new_start).normalized()
    turn = old_axis.rotation_difference(new_axis)
    stretch = (new_end - new_start).length / (end - start).length
    return [new_start + turn @ (point - start + old_axis * ((point - start).dot(old_axis) * (stretch - 1)))
            for point in points]


def boss_parts():
    parts = []
    for name, vertices, faces, swatch in scorpion.shells():
        if name == 'Pincer arm R':
            vertices = [right_arm_point(point) for point in vertices]
        if name in ('Pincer palm R', 'Outer claw R', 'Inner claw R'):
            vertices = [right_arm_point(RIGHT_WRIST + Vector((delta.x * 1.52, delta.y * 1.52,
                                                            delta.z * 1.26 + 0.037)))
                        for delta in (point - RIGHT_WRIST for point in vertices)]
        if name.startswith('Leg 1 '):
            side = 1 if name.endswith('L') else -1
            start = 0 if 'upper' in name else 1
            original = scorpion.leg_joints(side)[0]
            refit = leg_joints(side)[0]
            vertices = refit_shell(vertices, *original[start:start + 2], *refit[start:start + 2])
        if name.startswith('Carapace'):
            swatch = 'scorp_bone' if name.endswith(('2', '4')) else 'scorp_sandstone'
        if name == 'Stinger hook':
            swatch = 'scorp_plate'
        parts.append((name, vertices, faces, swatch))
    return parts


def turnaround(parts, directory):
    directory.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = 'BOTH'
    scene.display.shading.background_type = 'WORLD'
    scene.world = bpy.data.worlds.new('Review world')
    scene.world.color = (0.10, 0.10, 0.10)
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = scene.render.resolution_y = 480
    scene.render.resolution_percentage = 100
    palette = {name: scorpion.preview_material(name, SWATCHES[name][2])
               for name in {part[3] for part in parts}}
    for name, vertices, faces, swatch in parts:
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], faces)
        mesh.materials.append(palette[swatch])
        for face in mesh.polygons:
            face.use_smooth = True
        scene.collection.objects.link(bpy.data.objects.new(name, mesh))

    camera_data = bpy.data.cameras.new('Review')
    camera = bpy.data.objects.new('Review', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = 2.62
    target = Vector((0, -0.08, 0.45))
    views = {'front': (0, -1, 0), 'left': (1, 0, 0), 'rear': (0, 1, 0),
             'right': (-1, 0, 0), 'top': (0, -0.0001, 1), 'game': (0, -1, 1.5)}
    for name, direction in views.items():
        offset = Vector(direction)
        camera.location = target + offset.normalized() * 5
        camera.rotation_euler = (-offset).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(directory / (name + '.png'))
        bpy.ops.render.render(write_still=True)


def pose_basis(bone, world, parent_world):
    rest = bone.bone.matrix_local
    return rest.inverted() @ bone.parent.bone.matrix_local @ parent_world.inverted() @ world


def pose(arm, legs, *, claw=0, lift=0, lock=None, **parameters):
    scorpion.pose(arm, legs, **parameters)
    bpy.context.view_layer.update()
    bones = arm.pose.bones
    right = bones['Pincer_R']
    body = bones['Body'].matrix.copy()
    if claw or lift:
        pivot = right.bone.matrix_local.translation
        rotation = Quaternion((0, 0, 1), claw) @ Quaternion((1, 0, 0), -lift)
        relative = bones['Body'].bone.matrix_local.inverted() @ scorpion.turned_about(pivot, rotation)
        right.matrix_basis = pose_basis(right, body @ relative @ right.bone.matrix_local, body)

    if lock is not None:
        # Re-solve two segments as the shell pulls, keeping the distal tail and hook planted.
        third, fourth = bones['Tail_3'], bones['Tail_4']
        start = third.matrix.translation
        end = lock['Tail_5'].translation
        pole = fourth.matrix.translation - start
        bend, _ = ik2(start, end, third.bone.length, fourth.bone.length, pole)
        third_world = scorpion.aimed(third.bone.matrix_local, start, bend - start)
        fourth_world = scorpion.aimed(fourth.bone.matrix_local, bend, end - bend)
        third.matrix_basis = pose_basis(third, third_world, bones['Tail_2'].matrix)
        fourth.matrix_basis = pose_basis(fourth, fourth_world, third_world)
        bones['Tail_5'].matrix_basis = pose_basis(bones['Tail_5'], lock['Tail_5'], fourth_world)
        bones['Stinger'].matrix_basis = pose_basis(bones['Stinger'], lock['Stinger'], lock['Tail_5'])


def actions():
    base = scorpion.clips()
    phase = {frame: dict(shift=(0, 0, 0.022 * pulse), roll=0.13 * sway, pinch=0.08 * pulse,
                         claw=0.12 * sway, lift=0.09 * pulse)
             for frame in range(46)
             for pulse in (math.sin(math.pi * frame / 45),)
             for sway in (pulse * math.sin(math.tau * frame / 9),)}
    return {
        'Idle': base['Idle'],
        'Skitter': base['Skitter'],
        'SweepWindup': {0: {}, 9: dict(claw=-0.22, lift=0.08), 25: SWEEP_READY, 30: SWEEP_READY},
        'Sweep': {0: SWEEP_READY, 2: dict(claw=-0.10, lift=0.29, pinch=0.18),
                  6: dict(claw=0.96, lift=0.35, pinch=0.42, shift=(0, -0.07, 0.014)),
                  10: dict(claw=1.23, lift=0.35, pinch=0.45, shift=(0, -0.05, 0.01)),
                  24: {}},
        'SlamWindup': {0: {}, 11: dict(tail=(151, 113, 72, 29, 4, -47), pitch=-0.07),
                       24: SLAM_READY, 30: SLAM_READY},
        'Slam': {0: SLAM_READY, 2: dict(tail=(115, 76, 36, -5, -30, -43), pitch=-0.1),
                 6: SLAM_DOWN, 9: SLAM_DOWN},
        'Pinned': {frame: {**SLAM_DOWN, 'shift': (0, -0.025 + 0.020 * pull, -0.060 + 0.014 * pull),
                           'pitch': -0.049 * pull, 'pinch': 0.06 * pull}
                   for frame in range(31) for pull in (math.sin(math.pi * frame / 30) ** 2,)},
        'Phase': phase,
        'Hit': base['Hit'],
        'Death': {0: {}, 4: dict(shift=(0, 0.02, 0.035), pitch=-0.09),
                  15: dict(shift=(0, 0.02, -0.105), pitch=0.13, pinch=-0.17,
                           tail=(151, 89, 42, 13, -28, -60)),
                  30: dict(shift=(0, 0.02, -0.115), pitch=0.14, pinch=-0.20,
                           tail=(151, 90, 32, -3, -33, -68))},
    }


def animate(arm, legs):
    pose(arm, legs, **SLAM_DOWN)
    bpy.context.view_layer.update()
    lock = {name: arm.pose.bones[name].matrix.copy() for name in TAIL_LOCK}
    arm.animation_data_create()
    for name, frames in actions().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame, parameters in frames.items():
            pose(arm, legs, lock=lock if name == 'Pinned' else None, **parameters)
            for bone in arm.pose.bones:
                if bone.name == 'Root':
                    continue
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert('location', frame=frame, group=bone.name)
                bone.keyframe_insert('rotation_quaternion', frame=frame, group=bone.name)
        action.use_frame_range = True
        action.frame_range = (0, max(frames))
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = 'LINEAR'
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def export(parts):
    bones, groups, legs = scorpion.rig_parts(parts, joints=leg_joints)
    parent, head, wrist = bones['Pincer_R']
    bones['Pincer_R'] = (parent, head, right_arm_point(Vector(wrist)))
    assert set(groups.values()) == set(bones) - {'Root'}, 'Every deforming bone needs a shell'
    body = scorpion.assemble(parts, groups)
    body.name = body.data.name = 'Duneclaw_Body'
    assert 1500 <= len(body.data.loop_triangles) <= 3000
    arm = scorpion.armature(bones, body)
    animate(arm, legs)
    arm.select_set(True)
    body.select_set(True)
    bpy.context.scene.render.fps = 30
    destination = Path(__file__).resolve().parents[2] / 'Assets/_Project/Art/Enemies/Duneclaw.fbx'
    bpy.ops.export_scene.fbx(
        filepath=str(destination), use_selection=True, global_scale=SCALE,
        object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        path_mode='STRIP',
    )
    print(f'{destination.name}: {len(body.data.loop_triangles)} triangles, {len(bpy.data.actions)} clips')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--turnaround', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = boss_parts()
    if args.turnaround:
        turnaround(parts, args.turnaround.resolve())
    else:
        export(parts)


if __name__ == '__main__':
    main()
