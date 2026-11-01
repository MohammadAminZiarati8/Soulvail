"""Preserve the repaired Shaman sculpt in game (JG-16g/h).

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/shaman.py
    [-- --body-only | --staff-only] [--pose-sheet <png>]

Reads the pinned, hole-repaired masters without changing their geometry, UVs or colors.
No decimation, second voxel pass, proportion warping or atlas repaint. The 23-bone KayKit
hierarchy is fitted to the sculpt's natural pose; six clips are retargeted from Idle_B.
Original repaired textures are exported alongside the models. Full-detail cost is deliberate
until the owner accepts the in-game likeness; mobile performance is not yet measured.
"""
import argparse
import hashlib
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies')
TEX = os.path.join(OUT, 'Textures')
SOURCES = os.path.join(HERE, 'Sources')
RIG = os.path.join(ROOT, 'Assets', 'ThirdParty', 'KayKit', 'Skeletons', 'Characters', 'Skeleton_Minion.fbx')
CLIPS = os.path.join(ROOT, 'Assets', 'ThirdParty', 'KayKit', 'Animations', 'Rig_Medium')
BODY_HASH = '68f92aebef08af1037dc8446f6333bd23dc5620f618a69c1b915d5e14ead4059'
STAFF_HASH = '83268bdc48d50ffeae6cb363343a67e428b30fc283abf1e8811bdf91c5aa5de3'
SHEET_CLIPS = [('General', 'Idle_B'), ('MovementBasic', 'Walking_B'),
               ('MovementBasic', 'Running_A'), ('CombatRanged', 'Ranged_Magic_Spellcasting'),
               ('General', 'Hit_A'), ('General', 'Death_A')]
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
from mossguard import activate, world_mesh, texture_sampler, segment_distance
from enemy_atlas import save_png


def load_source(name, expected_hash):
    path = os.path.join(SOURCES, name + '_src.glb')
    assert hashlib.sha256(open(path, 'rb').read()).hexdigest() == expected_hash
    bpy.ops.import_scene.gltf(filepath=path)
    obj = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    world_mesh(obj)
    obj.data.calc_loop_triangles()
    return obj


def preserve_material(obj, name, texture_name):
    assert len(obj.data.materials) == 1 and obj.data.uv_layers.active
    material = obj.data.materials[0]
    material.name = name
    material.use_backface_culling = False
    bsdf = material.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Alpha'].default_value = 1
    bsdf.inputs['Transmission Weight'].default_value = 0
    image = next(n.image for n in material.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image.colorspace_settings.name == 'sRGB')
    image.filepath_raw = os.path.join(TEX, texture_name)
    image.file_format = 'PNG'
    image.save()


def source_point(point):
    return Vector((point[0], point[1] + .025, point[2] + .503)) * 1.59


def clips_for(source):
    for file in sorted(set(file for file, _ in SHEET_CLIPS)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(CLIPS, 'Rig_Medium_' + file + '.fbx'))
        for obj in set(bpy.data.objects) - before:
            bpy.data.objects.remove(obj, do_unlink=True)
    actions = {a.name.split('|')[-1]: a for a in bpy.data.actions}
    source.animation_data_create()
    source.animation_data.action = actions['Idle_B']
    bpy.context.scene.frame_set(round(actions['Idle_B'].frame_range[0]))
    bpy.context.view_layer.update()
    return {name: actions[name] for _, name in SHEET_CLIPS}


def fit_rig(source, target):
    """Move joints to the source landmarks, never move the sculpt to the old T-pose."""
    inverse = target.matrix_world.inverted()
    idle_world = {b.name: source.matrix_world @ source.pose.bones[b.name].matrix for b in source.data.bones}
    heads = {'root': (0, -.025, -.503), 'hips': (0, 0, -.31),
             'spine': (0, 0, -.24), 'chest': (0, -.005, -.10), 'head': (0, -.08, .02)}
    for side, sign in [('l', 1), ('r', -1)]:
        heads.update({
            'upperarm.' + side: (sign * .17, -.03, -.13),
            'lowerarm.' + side: (sign * .235, -.04, -.22),
            'wrist.' + side: (sign * .278, -.04, -.28),
            'hand.' + side: (sign * .28, -.045, -.29),
            'handslot.' + side: (sign * .282, -.04, -.29),
            'upperleg.' + side: (sign * .105, .015, -.34),
            'lowerleg.' + side: (sign * .115, 0, -.415),
            'foot.' + side: (sign * .13, -.01, -.46),
            'toes.' + side: (sign * .13, -.085, -.49),
        })
    links = {'root': 'hips', 'hips': 'spine', 'spine': 'chest', 'chest': 'head'}
    for side in ('l', 'r'):
        for a, b in [('upperarm', 'lowerarm'), ('lowerarm', 'wrist'), ('wrist', 'hand'),
                     ('upperleg', 'lowerleg'), ('lowerleg', 'foot'), ('foot', 'toes')]:
            links[a + '.' + side] = b + '.' + side
    activate(target)
    bpy.ops.object.mode_set(mode='EDIT')
    for bone in target.data.edit_bones:
        head = source_point(heads[bone.name])
        if bone.name in links:
            tail = source_point(heads[links[bone.name]])
        elif bone.name == 'head':
            tail = source_point((0, -.08, .28))
        elif bone.name.startswith('hand.'):
            side = bone.name[-1]
            tail = source_point(((1 if side == 'l' else -1) * .28, -.045, -.34))
        elif bone.name.startswith('toes.'):
            tail = head + Vector((0, -.10, 0))
        else:
            # Keep KayKit's weapon-frame orientation while placing its origin inside the palm.
            tail = head + idle_world[bone.name].to_quaternion() @ Vector((0, .112, 0))
        bone.use_connect = False
        bone.head = inverse @ head
        bone.tail = inverse @ tail
        bone.align_roll(inverse.to_3x3() @ (idle_world[bone.name].to_quaternion() @ Vector((0, 0, 1))))
    bpy.ops.object.mode_set(mode='OBJECT')
    return idle_world


def skin_without_reshaping(body, arm):
    inverse = arm.matrix_world.inverted()
    for bone in arm.data.bones:
        body.vertex_groups.new(name=bone.name)
    for vertex in body.data.vertices:
        p = vertex.co.copy()
        side, sign = ('l', 1) if p.x >= 0 else ('r', -1)
        if abs(p.x) > .26 and -.36 < p.z < -.23 and p.y < .11:
            weights = [(1, 'hand.' + side)]
        else:
            if abs(p.x) > .195 and -.37 < p.z < -.12 and p.y < .11:
                segments = [('chest', (0, 0, -.24), (0, 0, -.08)),
                            ('upperarm.' + side, (sign * .17, -.03, -.13), (sign * .235, -.04, -.22)),
                            ('lowerarm.' + side, (sign * .235, -.04, -.22), (sign * .278, -.04, -.28)),
                            ('hand.' + side, (sign * .278, -.04, -.28), (sign * .28, -.045, -.34))]
            elif p.z > -.075:
                segments = [('head', (0, -.08, .02), (0, -.08, .28))]
            elif p.y > .12 or (abs(p.x) < .065 and p.z < -.30):
                segments = [('hips', (0, .06, -.36), (0, .06, -.26)), ('spine', (0, .06, -.26), (0, .06, -.13))]
            elif p.z < -.355 and abs(p.x) < .20:
                segments = [('upperleg.' + side, (sign * .105, .015, -.34), (sign * .115, 0, -.415)),
                            ('lowerleg.' + side, (sign * .115, 0, -.415), (sign * .13, -.01, -.46)),
                            ('foot.' + side, (sign * .13, -.01, -.46), (sign * .13, -.085, -.49))]
            else:
                segments = [('hips', (0, 0, -.36), (0, 0, -.25)), ('spine', (0, 0, -.25), (0, 0, -.14)),
                            ('chest', (0, 0, -.14), (0, 0, -.075))]
            scores = sorted([(1 / (segment_distance(p, Vector(a), Vector(b)) + .045) ** 4, name)
                             for name, a, b in segments], reverse=True)[:3]
            total = sum(weight for weight, _ in scores)
            weights = [(weight / total, name) for weight, name in scores]
        for weight, name in weights:
            body.vertex_groups[name].add([vertex.index], weight, 'REPLACE')
        vertex.co = inverse @ source_point(p)
    body.parent = arm
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    modifier = body.modifiers.new('Rig_Medium', 'ARMATURE')
    modifier.object = arm


def retarget(source, target, source_actions, idle_world):
    scene = bpy.context.scene
    scene.render.fps = 30
    inverse = target.matrix_world.inverted()
    rest_world = {b.name: target.matrix_world @ b.matrix_local for b in target.data.bones}
    rest_parent = {b.name: (rest_world[b.parent.name].inverted() @ rest_world[b.name]) if b.parent else rest_world[b.name]
                   for b in target.data.bones}
    names = [b.name for b in target.data.bones]
    made = []
    target.animation_data_create()
    for _, name in SHEET_CLIPS:
        original = source_actions[name]
        source.animation_data.action = original
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        target.animation_data.action = action
        first, last = map(round, original.frame_range)
        for frame in range(first, last + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            posed = {}
            for bone_name in names:
                bone = target.data.bones[bone_name]
                original_world = source.matrix_world @ source.pose.bones[bone_name].matrix
                rotation = original_world.to_quaternion() @ idle_world[bone_name].to_quaternion().inverted() @ rest_world[bone_name].to_quaternion()
                if bone.parent:
                    position = posed[bone.parent.name] @ rest_parent[bone_name].translation
                else:
                    position = rest_world[bone_name].translation
                matrix = Matrix.Translation(position) @ rotation.to_matrix().to_4x4()
                if bone_name == 'root':
                    matrix = rest_world[bone_name].copy()
                posed[bone_name] = matrix
                pose = target.pose.bones[bone_name]
                pose.rotation_mode = 'QUATERNION'
                pose.matrix = inverse @ matrix
                pose.keyframe_insert('location', frame=frame)
                pose.keyframe_insert('rotation_quaternion', frame=frame)
                pose.keyframe_insert('scale', frame=frame)
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = 'LINEAR'
        made.append(action)
        print('RETARGETED', name, first, last, flush=True)
    target.animation_data_clear()
    for pose in target.pose.bones:
        pose.matrix_basis = Matrix.Identity(4)
    for action in list(bpy.data.actions):
        if action not in made:
            bpy.data.actions.remove(action)
    bpy.context.view_layer.update()
    return made


def export(path, objects, animated=False):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=False,
        add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim=animated, bake_anim_use_all_actions=animated, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        path_mode='STRIP', bake_space_transform=not animated)


def build_body():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    body = load_source('Shaman', BODY_HASH)
    body.name = 'Shaman_Body'
    triangles = len(body.data.loop_triangles)
    preserve_material(body, 'shaman_body', 'T_Shaman_Albedo.png')
    bpy.ops.import_scene.fbx(filepath=RIG)
    source = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    source.name = 'KayKitSource'
    for obj in list(bpy.context.scene.objects):
        if obj not in (body, source):
            bpy.data.objects.remove(obj, do_unlink=True)
    actions = clips_for(source)
    target = source.copy()
    target.data = source.data.copy()
    target.animation_data_clear()
    bpy.context.scene.collection.objects.link(target)
    target.name = 'Rig_Medium'
    for pose in target.pose.bones:
        pose.matrix_basis = Matrix.Identity(4)
    idle = fit_rig(source, target)
    skin_without_reshaping(body, target)
    assert len(target.data.bones) == 23
    retargeted = retarget(source, target, actions, idle)
    target.animation_data_create()
    target.animation_data.action = retargeted[0]
    bpy.context.scene.frame_set(round(retargeted[0].frame_range[0]))
    bpy.context.view_layer.update()
    bpy.data.objects.remove(source, do_unlink=True)
    body.data.calc_loop_triangles()
    assert len(body.data.loop_triangles) == triangles == 179970
    for vertex in body.data.vertices:
        assert 1 <= len(vertex.groups) <= 4
        assert abs(sum(g.weight for g in vertex.groups) - 1) < 1e-4
        assert all(body.vertex_groups[g.group].name != 'root' for g in vertex.groups)
    export(os.path.join(OUT, 'Shaman.fbx'), [target, body], animated=True)
    print('BODY_PRESERVED', triangles, 'triangles', len(body.data.vertices), 'vertices', flush=True)


def build_staff():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    staff = load_source('ShamanStaff', STAFF_HASH)
    triangles = len(staff.data.loop_triangles)
    sample = texture_sampler(staff)
    seed_faces = set()
    for face in staff.data.polygons:
        p = face.center
        r, g, b = sample(p)
        if .215 < p.z < .375 and -.125 < p.x < .085 and r > .35 and g > .35 and b < g * .65:
            seed_faces.add(face.index)
    preserve_material(staff, 'shaman_staff', 'T_ShamanStaff_Albedo.png')
    seed = staff.copy()
    seed.data = staff.data.copy()
    bpy.context.scene.collection.objects.link(seed)
    staff.name, seed.name = 'ShamanStaff_Wood', 'ShamanStaff_Seed'
    for obj, keep_seed in [(staff, False), (seed, True)]:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        remove = [face for face in bm.faces if (face.index in seed_faces) != keep_seed]
        bmesh.ops.delete(bm, geom=remove, context='FACES')
        bm.to_mesh(obj.data)
        bm.free()
        # Rigid prop fit only: original geometry/UVs survive. Face the crescent outward.
        rotation = Matrix.Rotation(-math.atan2(.8, .6), 4, 'Y') @ Matrix.Rotation(math.pi / 2, 4, 'Z')
        for vertex in obj.data.vertices:
            vertex.co = rotation @ ((vertex.co - Vector((-.008, 0, -.29))) * 1.9)
        obj.data.calc_loop_triangles()
    assert sum(len(obj.data.loop_triangles) for obj in (staff, seed)) == triangles == 179996
    export(os.path.join(OUT, 'ShamanStaff.fbx'), [staff, seed])
    save_png(np.ones((2, 2, 3), np.float32), os.path.join(TEX, 'T_ShamanStaff_Emission.png'))
    print('STAFF_PRESERVED', triangles, 'triangles; seed faces', len(seed_faces), flush=True)


def pose_sheet(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, 'Shaman.fbx'))
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    material = body.data.materials[0]
    material.use_nodes = True
    tex = material.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(os.path.join(TEX, 'T_Shaman_Albedo.png'))
    material.node_tree.links.new(tex.outputs['Color'], material.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    actions = {a.name.split('|')[-1]: a for a in bpy.data.actions}
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.world = bpy.data.worlds.new('SourceReview')
    scene.world.color = (.2, .2, .2)
    cell = 320
    scene.render.resolution_x = scene.render.resolution_y = cell
    camera_data = bpy.data.cameras.new('ReviewCamera')
    camera = bpy.data.objects.new('ReviewCamera', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = 1.95
    rows = []
    arm.animation_data_create()
    for _, name in SHEET_CLIPS:
        action = actions[name]
        arm.animation_data.action = action
        first, last = action.frame_range
        cells = []
        for eye in (Vector((0, -5, 2.3)), Vector((3, 4, 2.2))):
            camera.location = eye
            camera.rotation_euler = (Vector((0, 0, .8)) - eye).to_track_quat('-Z', 'Y').to_euler()
            for fraction in (0, .45, .8):
                scene.frame_set(round(first + (last - first) * fraction))
                scene.render.filepath = os.path.join(bpy.app.tempdir, 'shaman-source-cell.png')
                bpy.ops.render.render(write_still=True)
                image = bpy.data.images.load(scene.render.filepath)
                cells.append(np.flipud(np.array(image.pixels[:], np.float32).reshape(cell, cell, 4)[..., :3]))
                bpy.data.images.remove(image)
        rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), os.path.abspath(path))


if __name__ == '__main__':
    assert os.environ.get('PYTHONHASHSEED') == '0'
    parser = argparse.ArgumentParser()
    phase = parser.add_mutually_exclusive_group()
    phase.add_argument('--body-only', action='store_true')
    phase.add_argument('--staff-only', action='store_true')
    parser.add_argument('--pose-sheet')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if not args.staff_only:
        build_body()
    if not args.body_only:
        build_staff()
    if args.pose_sheet:
        pose_sheet(args.pose_sheet)
