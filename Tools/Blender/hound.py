"""Build BM-03b with PYTHONHASHSEED=0 blender -b --python Tools/Blender/hound.py.

The TRELLIS input stays intact. Reuse the Panther remesher, distance weights,
IK poser and clip baker, and fit the named skeleton to this sculpt's landmarks.
"""
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
import apex
import panther as cat

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SOURCE = os.path.join(HERE, 'Sources', 'Hound_trellis.glb')
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Pets')
SCALE = 1.8
CENTRE_Y = .02
FLOOR = -.451
SWATCHES = ((211, 197, 171), (113, 99, 87), (70, 62, 56), (34, 211, 238), (37, 83, 89))
BASE_CLIPS = cat.clips


def point(x, y, z):
    return cat.v(x * SCALE, (y - CENTRE_Y) * SCALE, (z - FLOOR) * SCALE)


def fit(body):
    marks = cat.landmarks(body.data)
    bones = cat.skeleton(marks)
    hips, spine, chest = point(0, .30, -.035), point(0, .02, -.025), point(0, -.23, -.025)
    neck, head = point(0, -.27, .12), point(0, -.30, .255)
    tail = [point(0, .34, .055), point(0, .46, .158), point(0, .40, .252), point(0, .24, .229)]
    bones.update({
        'hips': ('root', hips, spine),
        'spine': ('hips', spine, chest),
        'chest': ('spine', chest, neck),
        'neck': ('chest', neck, head),
        'head': ('neck', head, point(0, -.45, .218)),
        'jaw': ('head', point(0, -.31, .205), point(0, -.45, .177)),
        'tail_1': ('hips', tail[0], tail[1]),
        'tail_2': ('tail_1', tail[1], tail[2]),
        'tail_3': ('tail_2', tail[2], tail[3]),
        'Head': ('head', head, point(0, -.34, .255)),
    })
    for leg, paw in marks['paws'].items():
        front = leg.startswith('front')
        shoulder, knee, ankle = (
            cat.v(paw.x * .90, paw.y, (z - FLOOR) * SCALE) for z in (-.065, -.235, -.381)
        )
        bones[f'upper.{leg}'] = ('chest' if front else 'hips', shoulder, knee)
        bones[f'lower.{leg}'] = (f'upper.{leg}', knee, ankle)
        bones[f'paw.{leg}'] = (f'lower.{leg}', ankle, paw + cat.v(0, -.055, .03))
    marks.update(head=head, tail_root=tail[0])
    return marks, bones


def paint(high, body):
    colours = apex.source_colours(high, body)
    uv = body.data.uv_layers.new(name='UVMap').data
    counts = [0] * len(SWATCHES)
    for face in body.data.polygons:
        x, y, z = face.center / SCALE
        y += CENTRE_Y
        z += FLOOR
        light = float(cat.luma(colours[face.index]))
        # The collar wraps the sloping throat. Harness plates flank the shoulders and back.
        collar = -.368 < y < -.186 and abs(z - (.116 - .65 * (y + .265))) < .037
        shoulder = .115 < abs(x) and -.255 < y < -.115 and -.018 < z < .101
        back_plate = abs(x) < .115 and -.10 < y < .17 and .045 < z < .129
        nose = y < -.457 and .155 < z < .244
        eye = abs(x) > .072 and -.391 < y < -.324 and .247 < z < .290 and light < .11
        strap = -.19 < y < -.135 and -.135 < z < .08
        if collar or shoulder or back_plate:
            swatch = 3
        elif strap:
            swatch = 4
        elif nose or eye:
            swatch = 2
        else:
            swatch = 1 if light < .27 else 0
        counts[swatch] += 1
        for index in face.loop_indices:
            uv[index].uv = ((swatch * 48 + 24) / 256, .25 + .5 * (z - FLOOR) / .9)
        face.use_smooth = True
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new('hound'))
    assert counts[0] > len(body.data.polygons) / 2 and counts[1] and counts[3], counts
    print('Hound paint faces:', counts)


def atlas():
    image = bpy.data.images.new('T_Hound_Albedo', width=256, height=128, alpha=False)
    pixels = np.ones((128, 256, 4), dtype=np.float32)
    for index, colour in enumerate(SWATCHES):
        start = index * 48
        end = start + 48 if index < len(SWATCHES) - 1 else 256
        pixels[:, start:end, :3] = np.array(colour) / 255
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = os.path.join(OUT, 'Textures', 'T_Hound_Albedo.png')
    image.file_format = 'PNG'
    image.save()


def clips():
    base = BASE_CLIPS()
    still = base['Idle'][1][0]
    fall = base['Death'][1]
    airborne = dict(offset=cat.v(0, 0, .09), chest=(-5, 0, 0), neck=(8, 0, 0), legs={
        'front.l': (44, -30, -10, 0), 'front.r': (36, -25, -10, 0),
        'back.l': (-32, 29, 8, 0), 'back.r': (-26, 25, 8, 0),
    })
    return {
        'Idle': base['Idle'],
        'Trot': (True, cat.prowl()),
        'Run': (True, cat.lunge()),
        'Bite': (False, {0: still, 3: dict(neck=(-13, 0, 0), jaw=(-23, 0, 0)),
                         5: dict(offset=cat.v(0, -.09, -.05), neck=(17, 0, 0), head=(8, 0, 0)),
                         9: still}),
        'Pounce': (False, {0: airborne, 7: dict(airborne, head=(11, 0, 0)), 14: airborne}),
        'Land': (False, {0: airborne, 4: dict(offset=cat.v(0, 0, -.15), chest=(10, 0, 0)),
                         13: still}),
        'Hit': base['Hit'],
        'Fall': (False, fall | {36: fall[30]}),
        'GetUp': (False, {0: fall[30], 7: fall[16],
                           17: dict(offset=cat.v(0, 0, -.16), chest=(8, 0, 0)), 27: still}),
        'Howl': (False, {0: still, 9: dict(neck=(-20, 0, 0), head=(-23, 0, 0), jaw=(-18, 0, 0)),
                          24: dict(neck=(-24, 0, 0), head=(-26, 0, 0), jaw=(-22, 0, 0)),
                          36: still}),
    }


def export(arm, body):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = cat.FPS
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Hound.fbx'), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype='NULL', bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        path_mode='STRIP')


def main():
    assert os.environ.get('PYTHONHASHSEED') == '0', 'Start Blender with PYTHONHASHSEED=0'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cat.SOURCE = SOURCE
    cat.TRIANGLES = 2100
    cat.VOXEL = .011
    high = cat.load()
    for vertex in high.data.vertices:
        vertex.co = point(*vertex.co)
    high.data.update()
    body = cat.decimate(high)
    body.name = 'Hound_Body'
    paint(high, body)
    marks, bones = fit(body)
    # The curl extends over the rump. Allow its chain to influence this higher band only.
    allowed = cat.allowed
    try:
        def hound_allowed(name, p, landmarks, rig):
            if name.startswith('tail_'):
                return p.y > point(0, .19, 0).y and p.z > point(0, 0, .065).z
            return allowed(name, p, landmarks, rig)

        cat.allowed = hound_allowed
        cat.weigh(body, {name: bone for name, bone in bones.items() if name != 'Head'}, marks)
    finally:
        cat.allowed = allowed
    assert 1200 <= len(body.data.polygons) <= 2200
    bpy.data.objects.remove(high, do_unlink=True)
    arm = cat.build_armature(bones)
    arm.data.bones['Head'].use_deform = False
    cat.bind(body, arm)
    try:
        cat.clips = clips
        cat.author_clips(arm, body, bones)
    finally:
        cat.clips = BASE_CLIPS
    os.makedirs(os.path.join(OUT, 'Textures'), exist_ok=True)
    atlas()
    export(arm, body)


if __name__ == '__main__':
    main()
