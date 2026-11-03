"""JG-22b: the Chameleon stalker's sculpt -> unchanged KayKit Rig_Medium, atlas-painted, six clips.

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/stalker.py [-- --pose-sheet <png>]

The only input is Sources/Stalker_src.glb (TRELLIS seed 42, cleaned by fix_glb.py; hash pinned).
The sculpt is collapsed to the crowd's budget, painted from the existing enemy atlas (read, never
written) and fitted onto the rig, as mossguard.py fits the Mossguard: the 23 bones never move.
Its proportions are not KayKit's (big head, short torso, crouched bow legs), so the fit is
piecewise: legs to the hips, a stretched torso to the arm line, the head kept whole above it; the
bow legs are pulled 60 % toward the leg bones so a knee turns about its own joint. The curled tail
rides the hips alone. Six Rig_Medium clips ride the unchanged rig as they are, so they are baked
into the FBX untouched, as Shaman.fbx carries its own, behind a one-frame rest take.
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
from mathutils.bvhtree import BVHTree
from mathutils.geometry import area_tri, barycentric_transform, closest_point_on_tri
from mossguard import activate, reduce, segment_distance, world_mesh
from shaman import export

SOURCE_HASH = '001eb12bd3c3fcf22a9f992d1c459791f2b1ad702581635560b1650580abe52c'
TRIANGLES = 2000
# The cleaned scan is full of thin twinned sheets; a plain collapse stalls near 11k triangles.
VOXEL = .008
# The scan's olive bands sample below this luminance, its leaf-green skin above.
BAND = .30
CLIP_FILES = [('General', 'Idle_B'), ('MovementAdvanced', 'Sneaking'), ('MovementAdvanced', 'Crouching'),
              ('CombatMelee', 'Melee_Unarmed_Attack_Punch_A'), ('General', 'Hit_A'), ('General', 'Death_A')]

# Source landmarks (seed 42's own frame, face toward -Y) -> rig heights. Floor, hip joint, arm
# line, neck, crest top. The arm line sits 15 cm under the rig's (1.107 m) and the head is scaled
# whole (HEAD both ways), so the torso stretches less and the lurk stays under the Mossguard.
HEAD = 1.3
Z_SOURCE = [-.488, -.08, .13, .18, .484]
Z_RIG = [0, .62, .96, 1.03, 1.03 + .304 * HEAD]
WIDTH = 1.6
# The bow legs' centre line |x| by height; the hip's stays, knees and feet move toward it.
LEG_SOURCE_Z = [-.488, -.42, -.30, -.20, -.08]
LEG_CENTRE = [.24, .20, .17, .16, .08]
STRAIGHTEN = .6


def texture_sampler(obj):
    """mossguard.py's sampler, minus the cleaned scan's zero-area triangles (they sample NaN)."""
    me = obj.data
    me.calc_loop_triangles()
    verts = [v.co.copy() for v in me.vertices]
    uv = me.uv_layers.active.data
    kept = [t for t in me.loop_triangles if area_tri(*(verts[i] for i in t.vertices)) > 1e-10]
    tris = [tuple(t.vertices) for t in kept]
    uvs = [tuple(Vector((*uv[i].uv, 0)) for i in t.loops) for t in kept]
    tree = BVHTree.FromPolygons(verts, tris, all_triangles=True)
    image = next(n.image for n in me.materials[0].node_tree.nodes
                 if n.type == 'TEX_IMAGE' and n.image.colorspace_settings.name == 'sRGB')
    w, h = image.size
    pixels = np.array(image.pixels[:], np.float32).reshape(h, w, 4)

    def sample(p):
        near, _, index, _ = tree.find_nearest(p)
        a, b, c = (verts[i] for i in tris[index])
        q = barycentric_transform(closest_point_on_tri(near, a, b, c), a, b, c, *uvs[index])
        return pixels[min(h - 1, max(0, int(q.y * h))), min(w - 1, max(0, int(q.x * w))), :3]
    return sample


def vertical(z):
    return float(np.interp(z, Z_SOURCE, Z_RIG, left=Z_RIG[0] + (z - Z_SOURCE[0]) * 1.52))


def is_tail(p):
    """The curl lies out on +X behind the pelvis; its root runs back (+Y) from the hips."""
    if not -.38 < p.z < 0:
        return False
    return p.x > .21 or p.y > .10 or (p.x > .04 and p.y > .055 and p.y > .03 + .45 * p.x)


def paint(obj, sample):
    uv = obj.data.uv_layers.new(name='UVMap').data
    kinds = {}
    for face in obj.data.polygons:
        p = face.center
        r, g, b = sample(p)
        lum = .2126 * r + .7152 * g + .0722 * b
        eye = p.z > .22 and abs(p.x) > .085 and p.y < -.02
        if eye and lum < .07:
            swatch = 'frog_pupil'
        elif eye and r > g * .95:
            swatch = 'frog_belly'
        elif lum < BAND:
            swatch = 'panther_fur'
        else:
            swatch = 'frog_green'
        kinds[swatch] = kinds.get(swatch, 0) + 1
        for li in face.loop_indices:
            z = obj.data.vertices[obj.data.loops[li].vertex_index].co.z
            uv[li].uv = uv_for(swatch, .5 - z * .6)
        face.use_smooth = True
    obj.data.materials.clear()
    material = bpy.data.materials.get('enemy') or bpy.data.materials.new('enemy')
    obj.data.materials.append(material)
    print('PAINT', sorted(kinds.items()), flush=True)


def fit_and_skin(obj, arm):
    """Read-only rig. Source landmarks fit the sculpt to it, never the reverse."""
    head = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    tail = {b.name: arm.matrix_world @ b.tail_local for b in arm.data.bones}
    for bone in arm.data.bones:
        obj.vertex_groups.new(name=bone.name)
    hip_centre = LEG_CENTRE[-1]
    for v in obj.data.vertices:
        s = v.co.copy()
        side = 'l' if s.x >= 0 else 'r'
        ax = abs(s.x)
        across = WIDTH + (HEAD - WIDTH) * min(1, max(0, (s.z - .13) / .05))
        p = Vector((s.x * across, s.y * across, vertical(s.z)))
        if is_tail(s):
            p.z = (s.z - Z_SOURCE[0]) * 1.52
            allowed = ['hips']
        elif s.z < Z_SOURCE[1]:
            shift = STRAIGHTEN * (float(np.interp(s.z, LEG_SOURCE_Z, LEG_CENTRE)) - hip_centre)
            p.x = math.copysign(max(0, ax - shift) * WIDTH, s.x)
            allowed = ['hips', 'upperleg.' + side, 'lowerleg.' + side, 'foot.' + side, 'toes.' + side]
            if s.z < -.40:
                allowed = ['lowerleg.' + side, 'foot.' + side, 'toes.' + side]
        elif .04 < s.z < .2 and ax > .075:
            blend = min(1, (ax - .075) / .1)
            p.x = math.copysign((1 - blend) * ax * WIDTH + blend * (.12 + (ax - .075) * 1.95), s.x)
            p.z = (1 - blend) * p.z + blend * (head['upperarm.l'].z + (s.z - .13) * WIDTH)
            allowed = ['chest', 'upperarm.' + side, 'lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
            if ax > .3:
                allowed = ['lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
        elif s.z > .2:
            allowed = ['head']
        elif s.z > .12:
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
    """The six Rig_Medium actions, named plainly; every other imported action is dropped."""
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
    # Unity takes a model's default pose from the file's first take, which would be Crouching. A
    # one-frame take at KayKit's rest sorts first; the .meta imports only the six clips.
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
    assert hashlib.sha256(open(os.path.join(SOURCES, 'Stalker_src.glb'), 'rb').read()).hexdigest() == SOURCE_HASH
    atlas_hash = hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=os.path.join(SOURCES, 'Stalker_src.glb'))
    high = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    world_mesh(high)
    body = reduce(high, TRIANGLES, voxel=VOXEL)
    body.name = 'Stalker_Body'
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
    export(os.path.join(OUT, 'Stalker.fbx'), [arm, body], animated=True)
    assert hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest() == atlas_hash
    print('RUNTIME_TRIANGLES', triangles, flush=True)


def import_body(path):
    before, actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    new = set(bpy.data.objects) - before
    arm = next(o for o in new if o.type == 'ARMATURE')
    body = max((o for o in new if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    idle = [a for a in set(bpy.data.actions) - actions if a.name.endswith('Idle_B')]
    arm.animation_data_create()
    arm.animation_data.action = idle[0] if idle else None
    return arm, body


def textured(obj, image):
    material = obj.data.materials[0]
    material.use_nodes = True
    tex = material.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Closest'
    material.node_tree.links.new(tex.outputs['Color'], material.node_tree.nodes['Principled BSDF'].inputs['Base Color'])


def render_cells(scene, cam, frames, eyes, target, cell):
    cells = []
    for eye in eyes:
        cam.location = eye
        cam.rotation_euler = (target - eye).to_track_quat('-Z', 'Y').to_euler()
        for frame in frames:
            scene.frame_set(frame)
            scene.render.filepath = os.path.join(bpy.app.tempdir, 'stalker-cell.png')
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(scene.render.filepath)
            cells.append(np.flipud(np.array(img.pixels[:], np.float32).reshape(cell, cell, 4)[..., :3]))
            bpy.data.images.remove(img)
    return cells


def height(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = obj.evaluated_get(depsgraph).to_mesh()
    z = [(obj.matrix_world @ v.co).z for v in mesh.vertices]
    obj.evaluated_get(depsgraph).to_mesh_clear()
    return max(z) - min(z)


def pose_sheet(path):
    """Six clip rows (front and three-quarter back, three moments each), then a black silhouette
    row: Mossguard, Stalker and Shaman in idle, from the front, at one scale."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm, body = import_body(os.path.join(OUT, 'Stalker.fbx'))
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
        rows.append(np.concatenate(render_cells(scene, cam, frames, eyes, Vector((0, 0, .7)), cell), axis=1))
        print('POSE_ROW', name, flush=True)
    arm.animation_data.action = actions['Idle_B']
    others = [import_body(os.path.join(OUT, n + '.fbx')) for n in ('Mossguard', 'Shaman')]
    for (other_arm, _), x in zip(others, (-1.4, 1.4)):
        # The baked takes key the armature object itself, so offset a parent instead.
        holder = bpy.data.objects.new('Holder', None)
        scene.collection.objects.link(holder)
        holder.location.x = x
        other_arm.parent = holder
        # The Mossguard shares the unchanged rig and so the KayKit idle; the Shaman brings its own.
        other_arm.animation_data.action = other_arm.animation_data.action or actions['Idle_B']
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0, 0, 0)
    scene.display.shading.light = 'FLAT'
    scene.world.color = (.85, .85, .85)
    cd.ortho_scale = 4.4
    frame = round(actions['Idle_B'].frame_range[0])
    scene.frame_set(frame)
    guard = height(others[0][1])
    arm.animation_data.action = actions['Crouching']
    for t in (0, .5):
        scene.frame_set(round(actions['Crouching'].frame_range[0] + t * (actions['Crouching'].frame_range[1] - actions['Crouching'].frame_range[0])))
        print('CROUCH_RATIO', t, round(height(body) / guard, 3), flush=True)
    arm.animation_data.action = actions['Idle_B']
    cells = render_cells(scene, cam, [frame], (Vector((0, -6, .8)),), Vector((0, 0, .8)), cell)
    cells += [np.full_like(cells[0], .85)] * 5
    rows.append(np.concatenate(cells, axis=1))
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
