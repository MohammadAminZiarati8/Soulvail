"""Moss Shaman body: repaired owner sculpt -> atlas + unchanged KayKit Rig_Medium.

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/shaman.py
    [-- --pose-sheet <png>]

The separate staff uses handslot.r unchanged and a per-renderer seed emission mask.
Uses the same shared KayKit clips as other enemies; never rewrites the rig, clips or atlas.
Raw source repair: repair_scan.py <owner GLB> Tools/Blender/Sources/Shaman_src.glb
The pinned master has zero boundary/non-manifold edges and remains untouched by this build.
"""
import argparse
import hashlib
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
from mossguard import activate, reduce, texture_sampler, export, segment_distance, world_mesh
from enemy_atlas import uv_for, save_png

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies')
SOURCE = os.path.join(HERE, 'Sources', 'Shaman_src.glb')
SOURCE_HASH = '68f92aebef08af1037dc8446f6333bd23dc5620f618a69c1b915d5e14ead4059'
STAFF_SOURCE = os.path.join(HERE, 'Sources', 'ShamanStaff_src.glb')
STAFF_HASH = '83268bdc48d50ffeae6cb363343a67e428b30fc283abf1e8811bdf91c5aa5de3'
ATLAS = os.path.join(OUT, 'Textures', 'T_Enemy_Albedo.png')
RIG = os.path.join(ROOT, 'Assets', 'ThirdParty', 'KayKit', 'Skeletons', 'Characters', 'Skeleton_Minion.fbx')
CLIPS = os.path.join(ROOT, 'Assets', 'ThirdParty', 'KayKit', 'Animations', 'Rig_Medium')
SHEET_CLIPS = [('General', 'Idle_B'), ('MovementBasic', 'Walking_B'),
               ('MovementBasic', 'Running_A'), ('CombatRanged', 'Ranged_Magic_Spellcasting'),
               ('General', 'Hit_A'), ('General', 'Death_A')]


def paint(body, sample):
    uv = body.data.uv_layers.new(name='UVMap').data
    for face in body.data.polygons:
        p = face.center
        r, g, b = sample(p)
        # Bone mask/necklace, dark bark cloak, skin and leaf crest keep separate silhouettes.
        if min(r, g, b) > .23 and max(r, g, b) - min(r, g, b) < .27:
            swatch = 'bone'
        elif g > r * 1.12 and g > b * 1.35:
            swatch = 'frog_green' if p.z < .18 else 'croak_moss'
        elif max(r, g, b) < .10:
            swatch = 'socket'
        else:
            swatch = 'panther_fur'
        for li in face.loop_indices:
            uv[li].uv = uv_for(swatch, .42)
        face.use_smooth = True
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new('enemy'))


def fit_and_skin(body, arm):
    head = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    tail = {b.name: arm.matrix_world @ b.tail_local for b in arm.data.bones}
    for bone in arm.data.bones:
        body.vertex_groups.new(name=bone.name)
    for v in body.data.vertices:
        s = v.co.copy()
        side = 'l' if s.x >= 0 else 'r'
        # Fit the A-pose arms to the unchanged T-pose skeleton. Cross-section thickness is
        # independent of arm length: stretching all three axes makes oversized mittens.
        blend = max(0, min(1, (abs(s.x) - .17) / .065))
        blend = blend * blend * (3 - 2 * blend)
        blend *= max(0, min(1, (s.z + .43) / .075))
        blend *= max(0, min(1, (-s.z - .065) / .07))
        blend *= max(0, min(1, (.18 - s.y) / .065))
        z = float(np.interp(s.z, [-.503, -.36, -.08, .12, .503], [0, .50, 1.10676, 1.35, 1.60]))
        p = Vector((s.x * 1.25, (s.y + .025) * 1.30, z))
        if blend > 0:
            dx, dz = abs(s.x) - .17, s.z + .08
            along = dx * .54 - dz * .84
            across = dx * .84 + dz * .54
            arm_p = Vector((math.copysign(.212 + along * 2.7, s.x),
                            (s.y + .045) * 1.3, 1.10676 + across * 1.4))
            p = p.lerp(arm_p, blend)
            allowed = ['chest', 'upperarm.' + side, 'lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
        elif s.y > .15 and s.z < -.22:
            allowed = ['hips']  # tail and trailing cloak never follow a knee
        elif s.z < -.36:
            allowed = ['upperleg.' + side, 'lowerleg.' + side, 'foot.' + side, 'toes.' + side]
        elif s.z > .025:
            allowed = ['head']  # keep the bone mask and crest rigid
        else:
            allowed = ['hips', 'spine', 'chest', 'head']
        scores = sorted(((1 / (segment_distance(p, head[n], tail[n]) + .025) ** 4, n) for n in allowed), reverse=True)[:2]
        total = sum(w for w, _ in scores)
        for w, name in scores:
            body.vertex_groups[name].add([v.index], w / total, 'REPLACE')
        v.co = arm.matrix_world.inverted() @ p
    body.parent = arm
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    mod = body.modifiers.new('Rig_Medium', 'ARMATURE')
    mod.object = arm


def build():
    assert hashlib.sha256(open(SOURCE, 'rb').read()).hexdigest() == SOURCE_HASH
    atlas_hash = hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    high = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    world_mesh(high)
    body = reduce(high, 2350, voxel=.008)
    body.name = 'Shaman_Body'
    paint(body, texture_sampler(high))
    bpy.ops.import_scene.fbx(filepath=RIG)
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    assert arm.name == 'Rig_Medium' and len(arm.data.bones) == 23
    fit_and_skin(body, arm)
    assert 400 <= len(body.data.polygons) <= 2500
    for v in body.data.vertices:
        assert 1 <= len(v.groups) <= 4 and abs(sum(g.weight for g in v.groups) - 1) < 1e-4
        assert all(body.vertex_groups[g.group].name != 'root' for g in v.groups)
    export(os.path.join(OUT, 'Shaman.fbx'), [arm, body])
    assert hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest() == atlas_hash
    print('SHAMAN_TRIANGLES', len(body.data.polygons), flush=True)
    build_staff()


def build_staff():
    assert hashlib.sha256(open(STAFF_SOURCE, 'rb').read()).hexdigest() == STAFF_HASH
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=STAFF_SOURCE)
    high = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    world_mesh(high)
    staff = reduce(high, 390, voxel=.008)
    staff.name = 'ShamanStaff'
    sample = texture_sampler(high)
    uv = staff.data.uv_layers.new(name='UVMap').data
    seed_faces = 0
    for face in staff.data.polygons:
        p = face.center
        r, g, b = sample(p)
        if .215 < p.z < .375 and -.125 < p.x < .085 and r > .35 and g > .35 and b < g * .65:
            swatch = 'pitcher_skin'
            seed_faces += 1
        elif min(r, g, b) > .25 and max(r, g, b) - min(r, g, b) < .25:
            swatch = 'bone'
        elif g > r * 1.03 and g > b * 1.4:
            swatch = 'croak_moss'
        else:
            swatch = 'pitcher_root'
        for li in face.loop_indices:
            uv[li].uv = uv_for(swatch, .25)
        face.use_smooth = True
    staff.data.materials.clear()
    staff.data.materials.append(bpy.data.materials.new('enemy'))
    # The shaft at source z=-.16 is the palm grip. Bottom is .65 m below the hand;
    # the lantern is .9 m above it. Local +Y in Unity follows KayKit's weapon axis.
    for v in staff.data.vertices:
        # A 0.8 right + 0.6 up weapon axis keeps this staff upright both in Idle_B's
        # lowered palm and in Spellcasting's raised palm; no per-clip socket offsets.
        v.co = Matrix.Rotation(-math.atan2(.8, .6), 4, 'Y') @ ((v.co - Vector((-.008, 0, -.16))) * 1.9)
    assert 100 <= len(staff.data.polygons) <= 400
    assert seed_faces > 0
    export(os.path.join(OUT, 'ShamanStaff.fbx'), [staff])
    # Only the seed uses this atlas swatch on the staff. M_Enemy stays shared;
    # ShamanView supplies this mask through a property block on this renderer alone.
    mask = np.zeros((512, 512, 3), np.float32)
    mask[0:128, 256:320] = 1
    save_png(mask, os.path.join(OUT, 'Textures', 'T_ShamanStaff_Emission.png'))
    print('STAFF_TRIANGLES', len(staff.data.polygons), 'SEED_FACES', seed_faces, flush=True)


def pose_sheet(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, 'Shaman.fbx'))
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, 'ShamanStaff.fbx'))
    staff = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and o != body)
    world_mesh(staff)
    staff.parent = arm
    staff.parent_type = 'BONE'
    staff.parent_bone = 'handslot.r'
    bpy.context.view_layer.update()
    staff.matrix_world = arm.matrix_world @ arm.data.bones['handslot.r'].matrix_local @ Matrix.Rotation(-math.pi / 2, 4, 'X')
    tex = bpy.data.images.load(ATLAS)
    for obj in (body, staff):
        mat = obj.data.materials[0]
        mat.use_nodes = True
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = tex
        node.interpolation = 'Closest'
        mat.node_tree.links.new(node.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    for file in sorted(set(f for f, _ in SHEET_CLIPS)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(CLIPS, 'Rig_Medium_' + file + '.fbx'))
        for obj in set(bpy.data.objects) - before:
            bpy.data.objects.remove(obj, do_unlink=True)
    actions = {a.name.split('|')[-1]: a for a in bpy.data.actions}
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.world = bpy.data.worlds.new('Review')
    scene.world.color = (.16, .16, .16)
    cell = 300
    scene.render.resolution_x = scene.render.resolution_y = cell
    cd = bpy.data.cameras.new('ReviewCamera')
    cam = bpy.data.objects.new('ReviewCamera', cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cd.type = 'ORTHO'
    cd.ortho_scale = 2.25
    rows = []
    arm.animation_data_create()
    for _, name in SHEET_CLIPS:
        assert name in actions, (name, sorted(actions))
        arm.animation_data.action = actions[name]
        start, end = actions[name].frame_range
        cells = []
        for eye in (Vector((0, -5, 2.8)), Vector((3, 4, 2.3))):
            cam.location = eye
            cam.rotation_euler = (Vector((0, 0, .8)) - eye).to_track_quat('-Z', 'Y').to_euler()
            for t in (.0, .45, .8):
                scene.frame_set(round(start + (end - start) * t))
                scene.render.filepath = os.path.join(bpy.app.tempdir, 'shaman-cell.png')
                bpy.ops.render.render(write_still=True)
                img = bpy.data.images.load(scene.render.filepath)
                cells.append(np.flipud(np.array(img.pixels[:], np.float32).reshape(cell, cell, 4)[..., :3]))
                bpy.data.images.remove(img)
        rows.append(np.concatenate(cells, axis=1))
        print('POSE_ROW', name, flush=True)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    save_png(np.concatenate(rows, axis=0), os.path.abspath(path))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--pose-sheet')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    build()
    if args.pose_sheet:
        pose_sheet(args.pose_sheet)
