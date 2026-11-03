"""JG-24b: the Thornweaver's sculpt -> unchanged KayKit Rig_Medium, atlas-painted, five clips.

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/thornweaver.py [-- --pose-sheet <png>]

The only input is Sources/Thornweaver_src.glb (TRELLIS seed 42, cleaned by fix_glb.py; hash pinned).
The sculpt is collapsed to the crowd's budget, painted from the existing enemy atlas (read, never
written) and fitted onto the rig as stalker.py fits the Stalker: the 23 bones never move. A crone's
legs are longer than KayKit's, so the fit is piecewise by height: knee and crotch to the leg bones,
the torso to a shoulder line 6 cm over the rig's arm line (the hunch), the head scaled up and set
forward of the neck (the stoop). The arms are pulled onto their bones. The crown alone wears the
pale moss swatch, so JG-24c can light it. Five Rig_Medium clips are baked behind a one-frame rest take.
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
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies')
SOURCES = os.path.join(HERE, 'Sources')
ATLAS = os.path.join(OUT, 'Textures', 'T_Enemy_Albedo.png')
RIG = os.path.join(ROOT, 'Assets', 'ThirdParty', 'KayKit', 'Skeletons', 'Characters', 'Skeleton_Minion.fbx')
CLIPS = os.path.join(ROOT, 'Assets', 'ThirdParty', 'KayKit', 'Animations', 'Rig_Medium')
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
from enemy_atlas import save_png, uv_for
from mossguard import activate, reduce, segment_distance, world_mesh
from shaman import export
from stalker import height, import_body, render_cells, texture_sampler, textured

SOURCE_HASH = '9d40ddca7701c9a10c1c36aac4dc37f06c879f4341b4a500cd4808ed8f421b9f'
TRIANGLES = 2000
VOXEL = .008
CLIP_FILES = [('General', 'Idle_B'), ('MovementBasic', 'Walking_B'), ('CombatRanged', 'Ranged_Magic_Spellcasting'),
              ('General', 'Hit_A'), ('General', 'Death_A')]
CROWN = 'pitcher_skin'
# The skull's axis and radius under the crown, in the source's frame.
SKULL_Y = -.035
SKULL_R = .075

# Source landmarks (seed 42's own frame, face toward -Y) -> rig heights: floor, knee, crotch,
# shoulder line, neck, crown top. The shoulder sits over the rig's arm line (1.107 m); the arms are
# pulled down onto it.
Z_SOURCE = [-.478, -.27, -.05, .24, .32, .473]
Z_RIG = [0, .29, .60, 1.17, 1.35, 1.72]
WIDTH = 1.85
HEAD = 2.1
STOOP = .07
# The sculpt's arm line (median height by |x|), which the arm bones' line replaces.
ARM_X = [.13, .19, .25, .31, .37, .43, .49]
ARM_Z = [.275, .235, .243, .232, .219, .207, .168]
# Its legs stand off-centre: the +X leg 6.4 cm out, the -X leg 10.6 cm; both move to the rig's.
LEG_SOURCE = {'l': .064, 'r': .106}
LEG_RIG = .171


def vertical(z):
    return float(np.interp(z, Z_SOURCE, Z_RIG))


def is_crown(p):
    """The thorn ring round the brow and its spikes, out from the skull's axis; not the skull."""
    return p.z > .375 and (p.x ** 2 + (p.y - SKULL_Y) ** 2 > SKULL_R ** 2 or p.z > .455)


def paint(obj, sample):
    uv = obj.data.uv_layers.new(name='UVMap').data
    kinds = {}
    for face in obj.data.polygons:
        p = face.center
        r, g, b = (float(c) for c in sample(p))
        lum = .2126 * r + .7152 * g + .0722 * b
        if is_crown(p):
            swatch, t = CROWN, .2
        elif .33 < p.z < .41 and p.y < -.09 and lum < .12:
            swatch, t = 'socket', .5
        elif g > r * .9 and g > b * 1.6:
            swatch, t = 'croak_moss', 1 - min(1, max(0, (lum - .2) / .35))
        elif lum < .12:
            swatch, t = 'socket', .2
        else:
            swatch, t = 'pitcher_root', 1 - min(1, max(0, (lum - .15) / .3))
        kinds[swatch] = kinds.get(swatch, 0) + 1
        for li in face.loop_indices:
            uv[li].uv = uv_for(swatch, .1 + .8 * t)
        face.use_smooth = True
    obj.data.materials.clear()
    material = bpy.data.materials.get('enemy') or bpy.data.materials.new('enemy')
    obj.data.materials.append(material)
    print('PAINT', sorted(kinds.items()), flush=True)


def fit_and_skin(obj, arm):
    """Read-only rig. Source landmarks fit the sculpt to it, never the reverse."""
    head = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    tail = {b.name: arm.matrix_world @ b.tail_local for b in arm.data.bones}
    arm_line = head['upperarm.l'].z
    for bone in arm.data.bones:
        obj.vertex_groups.new(name=bone.name)
    for v in obj.data.vertices:
        s = v.co.copy()
        side = 'l' if s.x >= 0 else 'r'
        ax = abs(s.x)
        scale = WIDTH + (HEAD - WIDTH) * min(1, max(0, (s.z - .30) / .04))
        p = Vector((s.x * scale, s.y * scale, vertical(s.z)))
        if s.z > .30:
            p.y -= STOOP * min(1, (s.z - .30) / .08)
        if s.z < .05:
            # Legs to the rig's, fading out up the pelvis.
            fade = min(1, max(0, (.05 - s.z) / .1))
            p.x = math.copysign(ax * WIDTH + fade * (LEG_RIG - LEG_SOURCE[side] * WIDTH), s.x)
        if s.z < -.05:
            allowed = ['hips', 'upperleg.' + side, 'lowerleg.' + side, 'foot.' + side, 'toes.' + side]
            if s.z < -.38:
                allowed = ['lowerleg.' + side, 'foot.' + side, 'toes.' + side]
        elif ax > .12 and .10 < s.z < .30:
            blend = min(1, (ax - .12) / .1)
            line = float(np.interp(ax, ARM_X, ARM_Z))
            p.z = (1 - blend) * p.z + blend * (arm_line + (s.z - line) * WIDTH)
            allowed = ['chest', 'upperarm.' + side, 'lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
            if ax > .3:
                allowed = ['lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
        elif s.z > .32:
            allowed = ['head']
        elif s.z > .26:
            allowed = ['chest', 'head']
        else:
            allowed = ['hips', 'spine', 'chest']
        scored = sorted(((1 / (segment_distance(p, head[n], tail[n]) + .03) ** 4, n) for n in allowed), reverse=True)[:2]
        total = sum(w for w, _ in scored)
        for w, n in scored:
            obj.vertex_groups[n].add([v.index], w / total, 'REPLACE')
        v.co = arm.matrix_world.inverted() @ p
    obj.parent = arm
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    modifier = obj.modifiers.new('Rig_Medium', 'ARMATURE')
    modifier.object = arm


def load_clips(arm):
    """The five Rig_Medium actions, named plainly; every other imported action is dropped."""
    for file in sorted(set(f for f, _ in CLIP_FILES)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(CLIPS, 'Rig_Medium_' + file + '.fbx'))
        for obj in set(bpy.data.objects) - before:
            bpy.data.objects.remove(obj, do_unlink=True)
    wanted = [name for _, name in CLIP_FILES]
    kept = {}
    for action in list(bpy.data.actions):
        name = action.name.split('|')[-1]
        if name in wanted and name not in kept:
            kept[name] = action
        else:
            bpy.data.actions.remove(action)
    assert sorted(kept) == sorted(wanted), sorted(kept)
    for name, action in kept.items():
        action.name = name
        action.use_fake_user = True
    # Unity takes a model's default pose from the file's first take; a one-frame take at KayKit's
    # rest sorts first, and the .meta imports only the five clips.
    arm.animation_data_create()
    rest = bpy.data.actions.new('0_Rest')
    rest.use_fake_user = True
    arm.animation_data.action = rest
    for pose in arm.pose.bones:
        pose.rotation_mode = 'QUATERNION'
        pose.matrix_basis = Matrix.Identity(4)
        for frame in (0, 1):
            for path in ('location', 'rotation_quaternion', 'scale'):
                pose.keyframe_insert(path, frame=frame)
    arm.animation_data.action = None
    return kept


def build():
    assert hashlib.sha256(open(os.path.join(SOURCES, 'Thornweaver_src.glb'), 'rb').read()).hexdigest() == SOURCE_HASH
    atlas_hash = hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=os.path.join(SOURCES, 'Thornweaver_src.glb'))
    high = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    world_mesh(high)
    body = reduce(high, TRIANGLES, voxel=VOXEL)
    body.name = 'Thornweaver_Body'
    paint(body, texture_sampler(high))
    for obj in list(bpy.context.scene.objects):
        if obj != body:
            bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=RIG)
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    assert arm.name == 'Rig_Medium' and len(arm.data.bones) == 23
    fit_and_skin(body, arm)
    triangles = len(body.data.polygons)
    assert 400 <= triangles <= 2500, triangles
    for v in body.data.vertices:
        assert 1 <= len(v.groups) <= 4 and abs(sum(g.weight for g in v.groups) - 1) < 1e-4
        assert all(body.vertex_groups[g.group].name not in ('root', 'handslot.l', 'handslot.r') for g in v.groups)
    load_clips(arm)
    activate(arm)
    export(os.path.join(OUT, 'Thornweaver.fbx'), [arm, body], animated=True)
    assert hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest() == atlas_hash
    print('RUNTIME_TRIANGLES', triangles, flush=True)


def pose_sheet(path):
    """Five clip rows (front and three-quarter back, three moments each), then a black silhouette
    row: Mossguard, Shaman, Thornweaver and Stalker in idle, from the front, at one scale."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm, body = import_body(os.path.join(OUT, 'Thornweaver.fbx'))
    textured(body, bpy.data.images.load(ATLAS))
    actions = {a.name.split('|')[-1]: a for a in bpy.data.actions}
    assert sorted(actions) == sorted(['0_Rest'] + [name for _, name in CLIP_FILES]), sorted(actions)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.show_shadows = True
    scene.world = bpy.data.worlds.new('Review')
    scene.world.color = (.16, .16, .16)
    cell = 320
    scene.render.resolution_x = scene.render.resolution_y = cell
    cd = bpy.data.cameras.new('ReviewCamera')
    cam = bpy.data.objects.new('ReviewCamera', cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cd.type = 'ORTHO'
    cd.ortho_scale = 2.35
    rows = []
    eyes = (Vector((0, -5, 3.4)), Vector((-3, 4, 2.2)))
    for _, name in CLIP_FILES:
        action = actions[name]
        arm.animation_data.action = action
        start, end = action.frame_range
        frames = [round(start + (end - start) * t) for t in (0, .45, .8)]
        rows.append(np.concatenate(render_cells(scene, cam, frames, eyes, Vector((0, 0, .8)), cell), axis=1))
        print('POSE_ROW', name, flush=True)
    arm.animation_data.action = actions['Idle_B']
    others = [import_body(os.path.join(OUT, n + '.fbx')) for n in ('Mossguard', 'Shaman', 'Stalker')]
    for (other_arm, _), x in zip(others, (-1.8, -.6, 1.8)):
        # The baked takes key the armature object itself, so offset a parent instead.
        holder = bpy.data.objects.new('Holder', None)
        scene.collection.objects.link(holder)
        holder.location.x = x
        other_arm.parent = holder
        other_arm.animation_data.action = other_arm.animation_data.action or actions['Idle_B']
    holder = bpy.data.objects.new('Holder', None)
    scene.collection.objects.link(holder)
    holder.location.x = .6
    arm.parent = holder
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0, 0, 0)
    scene.display.shading.light = 'FLAT'
    scene.world.color = (.85, .85, .85)
    frame = round(actions['Idle_B'].frame_range[0])
    scene.frame_set(frame)
    guard = height(others[0][1])
    print('HEIGHT_RATIO', 'mossguard', round(height(body) / guard, 3), 'shaman', round(height(body) / height(others[1][1]), 3), flush=True)
    # One wide cell, three cells across, so the four stand at a readable size.
    scene.render.resolution_x = cell * 3
    cd.ortho_scale = 5.4
    cam.location = Vector((0, -6, .85))
    cam.rotation_euler = (Vector((0, 0, .85)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(bpy.app.tempdir, 'thornweaver-row.png')
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(scene.render.filepath)
    wide = np.flipud(np.array(img.pixels[:], np.float32).reshape(cell, cell * 3, 4)[..., :3])
    rows.append(np.concatenate([wide, np.full((cell, cell * 3, 3), .85, np.float32)], axis=1))
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    save_png(np.concatenate(rows, axis=0), path)


def main():
    assert os.environ.get('PYTHONHASHSEED') == '0', 'Set PYTHONHASHSEED=0 for stable FBX node IDs'
    parser = argparse.ArgumentParser()
    parser.add_argument('--pose-sheet')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    build()
    if args.pose_sheet:
        pose_sheet(args.pose_sheet)


if __name__ == '__main__':
    main()
