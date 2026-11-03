"""Gravemaw, the Jungle's stage-25 burrower, from two TRELLIS scans (JG-25e).

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/gravemaw.py -- [--preview <directory>]

Reads Sources/Gravemaw_trellis_43.glb (the shape) and Sources/Gravemaw_trellis_42.glb (the paint),
both read only, hashes pinned. Writes Art/Enemies/Gravemaw/Gravemaw.fbx and T_Gravemaw_Albedo.png.

**Shape from 43.** The open scan is closed as one shell thornheart.py's way (weld, thin extrusion,
voxel), hidden inner shells dropped, decimated to the boss budget and smooth-shaded.

**Paint from 42.** 42 is fitted onto 43's bounding box axis by axis, then ray-baked onto fresh UVs;
the few texels its rays miss keep 43's own colour, baked first. The colour law then moves any cyan
or near-Danger texel off it.

**The maw apart.** Faces at the front whose scan colour is the throat's warm orange become `Maw`, a
second skinned mesh on the same rig, repainted on an amber ramp so 25f can light it alone. The teeth
stay with `Body`.

**Its own rig**, panther.py's reason: Rig_Medium's clips cannot play on a worm. A spine of five
bones from tail to maw, a maw bone and two front claws, weighted by distance to each bone's segment.
The root never moves; Rise and Dive carry the body under the ground on the hips.
"""
import argparse
import colorsys
import hashlib
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from thornheart import (adjacency, apply, colour_law, components, drop_hidden_shells,  # noqa: E402
                        save_png, topology)

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SOURCES = os.path.join(HERE, 'Sources')
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies', 'Gravemaw')
SHAPE = ('Gravemaw_trellis_43.glb', '989ee3c3ae168054c1d538bcf44b91c1133f3a5bd545782fc2f06618fe1b23a6')
PAINT = ('Gravemaw_trellis_42.glb', 'a4bf68719ba0613fe98404f9192aef430b5ac28e065e9f46b3b3811bb1ee046b')
LENGTH = 1.6  # metres nose to tail at scale 1; 25g's BodyScale makes it three times that
BUDGET = 11500  # GravemawModelTests allows 12 000 together
TEXTURE = 2048
FPS = 30


def log(text):
    print(text, flush=True)


def load(name, digest):
    path = os.path.join(SOURCES, name)
    assert hashlib.sha256(open(path, 'rb').read()).hexdigest() == digest, f'{name} changed'
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    raw = max((o for o in new if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    for other in new:
        if other != raw:
            bpy.data.objects.remove(other, do_unlink=True)
    raw.data = raw.data.copy()
    raw.data.transform(raw.matrix_world)
    raw.parent = None
    raw.matrix_world = Matrix.Identity(4)
    raw.name = name.split('.')[0]
    bsdf = next(n for n in raw.data.materials[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    for link in list(bsdf.inputs['Metallic'].links):
        raw.data.materials[0].node_tree.links.remove(link)
    bsdf.inputs['Metallic'].default_value = 0
    return raw


def bounds(obj):
    co = np.array([v.co[:] for v in obj.data.vertices])
    return co.min(0), co.max(0)


def fit(paint, shape):
    """42 onto 43's box, axis by axis: the same creature, a little wider."""
    (pa, pb), (sa, sb) = bounds(paint), bounds(shape)
    k = (sb - sa) / (pb - pa)
    paint.data.transform(Matrix.Translation(Vector(sa)) @ Matrix.Diagonal((*k, 1))
                         @ Matrix.Translation(-Vector(pa)))


def face_colours(raw):
    """Each scan triangle's albedo at its UV centre, and a BVH over the triangles."""
    me = raw.data
    me.calc_loop_triangles()
    image = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE')
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    uv = me.uv_layers.active.data
    centre = np.array([[sum(uv[i].uv[k] for i in t.loops) / 3 for k in range(2)] for t in me.loop_triangles])
    colours = pixels[np.clip((centre[:, 1] * h).astype(int), 0, h - 1),
                     np.clip((centre[:, 0] * w).astype(int), 0, w - 1), :3]
    verts = [v.co.copy() for v in me.vertices]
    tris = [tuple(t.vertices) for t in me.loop_triangles]
    return colours, BVHTree.FromPolygons(verts, tris, all_triangles=True)


def closed(raw):
    """One closed shell round the open scan, its hidden inner shells dropped, at the budget."""
    scale = max(raw.dimensions)
    body = raw.copy()
    body.data = raw.data.copy()
    body.name = 'Body'
    bpy.context.scene.collection.objects.link(body)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(body.data)
    bm.free()
    solid = body.modifiers.new('Close', 'SOLIDIFY')
    solid.solidify_mode = 'EXTRUDE'
    solid.thickness = 0.006 * scale
    solid.offset = 0
    solid.use_even_offset = False
    apply(body, solid)
    remesh = body.modifiers.new('Shell', 'REMESH')
    remesh.mode = 'VOXEL'
    remesh.voxel_size = 0.003 * scale
    apply(body, remesh)
    drop_hidden_shells(body)
    for _ in range(8):
        tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
        if tris <= BUDGET:
            break
        dec = body.modifiers.new('Budget', 'DECIMATE')
        dec.ratio = max(BUDGET * 0.98 / tris, 0.3)
        dec.use_collapse_triangulate = True
        apply(body, dec)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=1e-6)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(body.data)
    bm.free()
    for p in body.data.polygons:
        p.use_smooth = True
    log(f'BODY {len(body.data.polygons)} tris, open/nonmanifold {topology(body.data)}')
    return body


def maw_faces(body, colours, bvh):
    """The throat: warm front faces that look forward out of the mouth, the largest such patch.
    The hide on the brow is as orange as the throat; it faces up, so it stays with the body."""
    me = body.data
    centres = np.array([p.center[:] for p in me.polygons])
    normals = np.array([p.normal[:] for p in me.polygons])
    rgb = colours[[bvh.find_nearest(Vector(c))[2] for c in centres]]
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in rgb])
    lo, hi = centres.min(0), centres.max(0)
    front = centres[:, 1] < lo[1] + 0.22 * (hi[1] - lo[1])
    # The teeth are pale bone, under 0.6 saturation: they stay with the body.
    warm = (hsv[:, 0] * 360 > 8) & (hsv[:, 0] * 360 < 50) & (hsv[:, 1] > 0.6) & (hsv[:, 2] > 0.4)
    pick = front & warm & (normals[:, 1] < -0.15)
    own = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    for f in np.flatnonzero(pick):  # nothing of its own in front of it: seen through the mouth
        pick[f] = own.ray_cast(Vector(centres[f]) + Vector((0, -1e-3, 0)), Vector((0, -1, 0)))[0] is None
    groups = components(np.flatnonzero(pick), adjacency(me))
    faces = np.array(groups[0])
    axis = centres[faces][:, [0, 2]].mean(0)
    log(f'MAW {len(faces)} faces of {pick.sum()} about x {axis[0]:.3f} z {axis[1]:.3f}')
    return faces, axis


def split(body, faces):
    """Move `faces` into their own object, Maw."""
    maw = body.copy()
    maw.data = body.data.copy()
    maw.name = 'Maw'
    bpy.context.scene.collection.objects.link(maw)
    keep = np.zeros(len(body.data.polygons), bool)
    keep[faces] = True
    for obj, mine in ((body, ~keep), (maw, keep)):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not mine[f.index]], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        bm.to_mesh(obj.data)
        bm.free()
    return maw


def unwrap(parts):
    for part in parts:
        while part.data.uv_layers:
            part.data.uv_layers.remove(part.data.uv_layers[0])
        part.data.uv_layers.new(name='UVMap')
    bpy.ops.object.select_all(action='DESELECT')
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_from(source, parts, image):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 4
    settings = scene.render.bake
    settings.use_selected_to_active = True
    settings.use_cage = False
    settings.cage_extrusion = 0.012
    settings.max_ray_distance = 0.05
    settings.margin = 8
    settings.use_clear = False
    settings.use_pass_direct = False
    settings.use_pass_indirect = False
    settings.use_pass_color = True
    for part in parts:
        mat = bpy.data.materials.new(part.name + '_Bake')
        mat.use_nodes = True
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = image
        mat.node_tree.nodes.active = node
        part.data.materials.clear()
        part.data.materials.append(mat)
        bpy.ops.object.select_all(action='DESELECT')
        source.select_set(True)
        part.select_set(True)
        bpy.context.view_layer.objects.active = part
        bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})


def paint(parts, shape, paint_raw):
    """43's own colour first, then 42's over every texel its rays reach; the maw on an amber ramp."""
    unwrap(parts)
    image = bpy.data.images.new('T_Gravemaw_Albedo', TEXTURE, TEXTURE, alpha=False)
    image.generated_color = (0.2, 0.14, 0.09, 1)
    bake_from(shape, parts, image)
    over = bpy.data.images.new('Paint42', TEXTURE, TEXTURE, alpha=True)
    over.generated_color = (0, 0, 0, 0)
    bake_from(paint_raw, parts, over)
    px = np.array(image.pixels[:], dtype=np.float32).reshape(-1, 4)
    ox = np.array(over.pixels[:], dtype=np.float32).reshape(-1, 4)
    used = ox[:, 3] > 0.5
    px[used, :3] = ox[used, :3]
    image.pixels[:] = px.ravel()
    maw = bpy.data.images.new('MawMask', TEXTURE, TEXTURE, alpha=True)
    maw.generated_color = (0, 0, 0, 0)
    bake_from(shape, [p for p in parts if p.name == 'Maw'], maw)
    mx = np.array(maw.pixels[:], dtype=np.float32).reshape(-1, 4)
    mask = mx[:, 3] > 0.5
    luma = np.clip((px[mask, :3] @ np.array([0.3, 0.55, 0.15]) - 0.15) / 0.6, 0, 1)[:, None]
    dark, bright = np.array([0.62, 0.38, 0.08]), np.array([1.0, 0.84, 0.36])
    px[mask, :3] = dark + (bright - dark) * luma
    image.pixels[:] = px.ravel()
    log(f'PAINT 42 covers {used.mean():.3f} of the texels; maw {mask.sum()} texels')
    log(f'COLOUR_LAW cyan/danger texels moved {colour_law(image)}')
    return image


def material(parts, image):
    mat = bpy.data.materials.new('M_Gravemaw')
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    bsdf = mat.node_tree.nodes['Principled BSDF']
    mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.85
    for part in parts:
        part.data.materials.clear()
        part.data.materials.append(mat)


def place(parts):
    """Foot on the ground, centred, LENGTH long; the maw stays toward -Y (Unity's +Z after the rig)."""
    co = np.concatenate([np.array([v.co[:] for v in p.data.vertices]) for p in parts])
    lo, hi = co.min(0), co.max(0)
    k = LENGTH / (hi[1] - lo[1])
    frame = Matrix.Scale(k, 4) @ Matrix.Translation(-Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2])))
    for part in parts:
        part.data.transform(frame)
    return frame


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    shape = load(*SHAPE)
    paint_raw = load(*PAINT)
    fit(paint_raw, shape)
    colours, bvh = face_colours(shape)
    body = closed(shape)
    faces, axis = maw_faces(body, colours, bvh)
    maw = split(body, faces)
    parts = [body, maw]
    image = paint(parts, shape, paint_raw)
    material(parts, image)
    place(parts)
    bpy.data.objects.remove(shape, do_unlink=True)
    bpy.data.objects.remove(paint_raw, do_unlink=True)
    log(f'TOTAL {sum(len(p.data.polygons) for p in parts)} tris')
    save_png(image, os.path.join(OUT, 'T_Gravemaw_Albedo.png'))
    mark = landmarks(body, maw)
    bones = skeleton(mark)
    weigh(parts, bones, mark)
    arm = build_armature(bones)
    bind(parts, arm)
    author_clips(arm, parts)
    export(arm, parts)
    return arm, parts


# ---------------------------------------------------------------------------------------------
# The rig. Model frame: Z up, the maw toward -Y, metres. The armature object is turned 90 degrees
# about X (panther.py's shape), so in Unity the maw faces +Z.
# ---------------------------------------------------------------------------------------------

SPINE = ('tail_2', 'tail_1', 'hips', 'chest', 'head')


def v(x, y, z):
    return Vector((x, y, z))


def landmarks(body, maw):
    co = np.array([vt.co[:] for vt in body.data.vertices])
    mouth = np.array([vt.co[:] for vt in maw.data.vertices])
    front, back, top = float(co[:, 1].min()), float(co[:, 1].max()), float(co[:, 2].max())
    low = co[(co[:, 2] < 0.25 * top) & (co[:, 1] < front + 0.35 * (back - front))]
    claws = {side: low[(low[:, 0] > 0) == (side == 'l')].mean(0) for side in ('l', 'r')}
    return dict(front=front, back=back, top=top, mid=0.5 * top, claws=claws,
                mouth=v(0, float(mouth[:, 1].max()), float(mouth[:, 2].mean())))


def skeleton(mark):
    f, b, z = mark['front'], mark['back'], mark['mid']
    length = b - f
    at = lambda t: f + t * length  # noqa: E731 - a point a fraction t of the way from the maw
    mouth = mark['mouth']
    bones = {
        'root': (None, v(0, 0, 0), v(0, 0, 0.12)),
        'hips': ('root', v(0, at(0.6), z), v(0, at(0.4), z)),
        'chest': ('hips', v(0, at(0.4), z), v(0, at(0.2), z)),
        'head': ('chest', v(0, at(0.2), z), v(0, mouth.y, mouth.z)),
        'maw': ('head', v(0, mouth.y, mouth.z), v(0, f, mouth.z)),
        'tail_1': ('hips', v(0, at(0.6), z), v(0, at(0.8), z - 0.03)),
        'tail_2': ('tail_1', v(0, at(0.8), z - 0.03), v(0, b, z - 0.08)),
    }
    for side, claw in mark['claws'].items():
        x = float(claw[0])
        bones[f'claw.{side}'] = ('chest', v(0.55 * x, at(0.28), 0.45 * z), v(x, float(claw[1]) - 0.06, 0.03))
    return bones


def build_armature(bones):
    data = bpy.data.armatures.new('Rig_Gravemaw')
    arm = bpy.data.objects.new('Rig_Gravemaw', data)
    bpy.context.scene.collection.objects.link(arm)
    arm.matrix_world = Matrix.Rotation(math.radians(90), 4, 'X')
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    to_arm = arm.matrix_world.inverted()
    edit = {}
    for name, (parent, head, tail) in bones.items():
        bone = data.edit_bones.new(name)
        bone.head = to_arm @ head
        bone.tail = to_arm @ tail
        direction = (tail - head).normalized()
        up = v(0, 0, 1) if abs(direction.z) < 0.7 else v(0, -1, 0)
        bone.align_roll(to_arm.to_3x3() @ up)
        if parent:
            bone.parent = edit[parent]
        edit[name] = bone
    bpy.ops.object.mode_set(mode='OBJECT')
    data.bones['root'].use_deform = False
    return arm


def segment_distance(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared == 0 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def allowed(name, p, mark, bones):
    if name == 'root':
        return False
    claw = abs(p.x) > 0.6 * abs(float(mark['claws']['l'][0])) and p.z < 0.7 * mark['mid'] \
        and p.y < bones['chest'][1].y
    if name.startswith('claw.'):
        return claw and (p.x > 0) == name.endswith('.l')
    if claw and p.z < 0.45 * mark['mid']:
        return False
    if name == 'maw':
        return p.y < mark['mouth'].y + 0.05
    if name.startswith('tail_'):
        return p.y > bones['hips'][1].y - 0.1
    return True


def weigh(objects, bones, mark):
    for obj in objects:
        for name in bones:
            obj.vertex_groups.new(name=name)
        groups = {g.name: g for g in obj.vertex_groups}
        for vert in obj.data.vertices:
            p = vert.co
            near = sorted((segment_distance(p, head, tail), name) for name, (_, head, tail) in bones.items()
                          if allowed(name, p, mark, bones))[:3]
            raw = [(1.0 / (d + 0.05) ** 4, name) for d, name in near]
            total = sum(w for w, _ in raw)
            kept = [(w / total, name) for w, name in raw if w / total >= 0.04]
            total = sum(w for w, _ in kept)
            for w, name in kept:
                groups[name].add([vert.index], w / total, 'REPLACE')


def bind(objects, arm):
    for obj in objects:
        modifier = obj.modifiers.new('Armature', 'ARMATURE')
        modifier.object = arm
        obj.parent = arm
        obj.data.transform(arm.matrix_world.inverted())
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_local = Matrix.Identity(4)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------------------------------------
# The clips. A pose names the hips' offset (model frame, metres) and turn, a turn for any other bone
# and the maw's scale. turn(pitch, roll, yaw) in degrees about the model's X, Y, Z: pitch > 0 tips a
# forward bone's far end down (the maw dips), a tail bone's up.
# ---------------------------------------------------------------------------------------------


def turn(pitch=0.0, roll=0.0, yaw=0.0):
    return (Quaternion(v(0, 0, 1), math.radians(yaw)) @ Quaternion(v(0, 1, 0), math.radians(roll))
            @ Quaternion(v(1, 0, 0), math.radians(pitch)))


class Poser:
    def __init__(self, arm):
        self.arm = arm
        self.A = arm.matrix_world.to_3x3()
        self.A_inv = self.A.inverted()
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}

    def _local(self, name, q, offset=None, scale=1.0):
        R = self.rest[name].to_3x3()
        qa = self.A_inv @ q.to_matrix() @ self.A
        basis = (R.inverted() @ qa @ R).to_4x4() @ Matrix.Scale(scale, 4)
        if offset is not None:
            basis.translation = R.inverted() @ (self.A_inv @ offset)
        self.arm.pose.bones[name].matrix_basis = basis

    def apply(self, pose):
        for pb in self.arm.pose.bones:
            pb.matrix_basis = Matrix.Identity(4)
        self._local('hips', turn(*pose.get('hips', (0, 0, 0))), pose.get('offset', v(0, 0, 0)))
        for name in ('chest', 'head', 'tail_1', 'tail_2', 'claw.l', 'claw.r'):
            if name in pose:
                self._local(name, turn(*pose[name]))
        self._local('maw', turn(*pose.get('maw', (0, 0, 0))), scale=pose.get('open', 1.0))
        bpy.context.view_layer.update()


def P(**kw):
    return dict(kw)


def extent(objects):
    """The posed body's lowest and highest points."""
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    lo, hi = math.inf, -math.inf
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        zs = [(evaluated.matrix_world @ vt.co).z for vt in mesh.vertices]
        lo, hi = min(lo, min(zs)), max(hi, max(zs))
        evaluated.to_mesh_clear()
    return lo, hi


def clips():
    rest = P()
    under = P(offset=v(0, 0.1, 0), hips=(-50, 0, 0), chest=(-10, 0, 0), bury=True)
    claws_up = {'claw.l': (-25, 0, -10), 'claw.r': (-25, 0, 10)}
    return {
        'Idle': (True, {
            0: rest,
            12: P(offset=v(0, 0, 0.015), chest=(-2, 0, 3), head=(-2, 0, 6), tail_1=(0, 0, -5), tail_2=(0, 0, -8), open=1.05),
            24: P(chest=(0, 0, 0), head=(1, 0, 0), tail_1=(0, 0, 0), tail_2=(2, 0, 0)),
            36: P(offset=v(0, 0, 0.015), chest=(-2, 0, -3), head=(-2, 0, -6), tail_1=(0, 0, 5), tail_2=(0, 0, 8), open=1.05),
            48: rest,
        }),
        # Erupting: from under the ground, maw first and up, then down onto its belly.
        'Rise': (False, {
            0: under,
            5: P(offset=v(0, 0, -0.35), hips=(-45, 0, 0), chest=(-12, 0, 0), head=(-8, 0, 0), open=1.2, **claws_up),
            10: P(offset=v(0, 0, 0.12), hips=(-18, 0, 0), chest=(-6, 0, 0), head=(-4, 0, 0), open=1.15, **claws_up),
            15: rest,
        }),
        # Back under: a rear, then maw first into the ground and gone.
        'Dive': (False, {
            0: rest,
            4: P(offset=v(0, 0, 0.08), hips=(-14, 0, 0), chest=(-6, 0, 0), open=0.9),
            9: P(offset=v(0, -0.2, -0.3), hips=(35, 0, 0), chest=(10, 0, 0), head=(8, 0, 0), tail_1=(10, 0, 0)),
            15: P(offset=v(0, -0.4, 0), hips=(50, 0, 0), chest=(12, 0, 0), head=(10, 0, 0), tail_1=(14, 0, 0), bury=True),
        }),
        'Spit': (False, {
            0: rest,
            7: P(offset=v(0, 0.1, 0.06), hips=(-6, 0, 0), chest=(-14, 0, 0), head=(-12, 0, 0), open=0.85),
            10: P(offset=v(0, -0.12, 0), hips=(4, 0, 0), chest=(10, 0, 0), head=(12, 0, 0), open=1.35),
            14: P(offset=v(0, -0.06, 0), chest=(4, 0, 0), head=(4, 0, 0), open=1.2),
            20: rest,
        }),
        'Tremor': (False, {
            0: rest,
            8: P(offset=v(0, 0.05, 0.18), hips=(-24, 0, 0), chest=(-10, 0, 0), head=(-6, 0, 0), open=1.1, **claws_up),
            12: P(offset=v(0, 0, -0.06), hips=(5, 0, 0), chest=(4, 0, 0), open=1.0),
            14: P(offset=v(0, 0, -0.04), hips=(3, 4, 0), chest=(2, -3, 0)),
            16: P(offset=v(0, 0, -0.04), hips=(3, -4, 0), chest=(2, 3, 0)),
            18: P(offset=v(0, 0, -0.03), hips=(2, 3, 0), chest=(1, -2, 0)),
            20: P(offset=v(0, 0, -0.02), hips=(1, -2, 0)),
            24: rest,
        }),
        'Hit': (False, {
            0: rest,
            3: P(offset=v(0, 0.07, 0.02), chest=(-8, 0, 0), head=(-12, 0, 0), open=0.85),
            7: P(offset=v(0, -0.02, 0), chest=(2, 0, 0), head=(3, 0, 0)),
            12: rest,
        }),
        'Death': (False, {
            0: rest,
            8: P(offset=v(0, 0.05, 0.15), hips=(-22, 0, 0), chest=(-12, 0, 0), head=(-10, 0, 0), open=1.3, **claws_up),
            20: P(hips=(4, 70, 0), chest=(6, 6, 0), head=(10, 6, 0), tail_1=(-8, 0, 0), open=1.1, ground=True),
            28: P(hips=(2, 88, 0), chest=(8, 8, 0), head=(14, 8, 0), tail_1=(-10, 0, 0), tail_2=(-6, 0, 0), open=0.95, ground=True),
            36: P(hips=(2, 86, 0), chest=(8, 8, 0), head=(16, 8, 0), tail_1=(-10, 0, 0), tail_2=(-8, 0, 0), open=0.9, ground=True),
        }),
    }


def author_clips(arm, objects):
    poser = Poser(arm)
    arm.animation_data_create()
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    for name, (loop, keys) in clips().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        last = {}
        for frame in sorted(keys):
            pose = dict(keys[frame])
            grounded, buried = pose.pop('ground', False), pose.pop('bury', False)
            poser.apply(pose)
            if grounded:  # rolled over: lowered until its lowest point meets the floor
                pose['offset'] = pose.get('offset', v(0, 0, 0)) - v(0, 0, extent(objects)[0])
                poser.apply(pose)
            if buried:  # under the ground: lowered until its highest point is below it
                pose['offset'] = pose.get('offset', v(0, 0, 0)) - v(0, 0, extent(objects)[1] + 0.03)
                poser.apply(pose)
            for pb in arm.pose.bones:
                q = pb.rotation_quaternion.copy()
                if pb.name in last and last[pb.name].dot(q) < 0:
                    q.negate()
                    pb.rotation_quaternion = q
                last[pb.name] = q
                pb.keyframe_insert('location', frame=frame, group=pb.name)
                pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
                pb.keyframe_insert('scale', frame=frame, group=pb.name)
        action.frame_range = (0, max(keys))
        action.use_frame_range = True
        log(f'CLIP {name}: {max(keys)} frames, {"loop" if loop else "once"}')
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)


def export(arm, objects):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, 'Gravemaw.fbx'), use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=False,
        add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='STRIP')
    log(f'EXPORTED {os.path.join(OUT, "Gravemaw.fbx")}')


def render(arm, directory, views):
    """Review renders: Eevee, sun and sky over the Jungle floor's colour."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = scene.render.resolution_y = 700
    scene.view_settings.view_transform = 'Standard'
    scene.world = bpy.data.worlds.new('Sky')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.36, 0.30, 1)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    floor = bpy.data.meshes.new('Floor')
    floor.from_pydata([(-9, -9, 0), (9, -9, 0), (9, 9, 0), (-9, 9, 0)], [], [(0, 1, 2, 3)])
    mat = bpy.data.materials.new('Floor')
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.22, 0.10, 1)
    floor.materials.append(mat)
    scene.collection.objects.link(bpy.data.objects.new('Floor', floor))
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    centre = Vector((0, 0, 0.4))

    def shoot(name, pitch, yaw):
        d = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(pitch)),
                    -math.cos(math.radians(yaw)) * math.cos(math.radians(pitch)), math.sin(math.radians(pitch))))
        cam.location = centre + d * 4.2
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(directory, f'Gravemaw-{name}.png')
        bpy.ops.render.render(write_still=True)
        log(f'RENDERED {scene.render.filepath}')
    for name, pitch, yaw in views:
        shoot(name, pitch, yaw)
    # Every clip from three-quarters, at its keys.
    scene.render.resolution_x = scene.render.resolution_y = 350
    for name, (_, keys) in clips().items():
        arm.animation_data.action = bpy.data.actions[name]
        for frame in sorted(keys):
            scene.frame_set(frame)
            shoot(f'{name}-{frame:02d}', 20, 60)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preview')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    assert os.environ.get('PYTHONHASHSEED') == '0', 'Set PYTHONHASHSEED=0 before Blender starts'
    os.makedirs(OUT, exist_ok=True)
    arm, parts = build()
    if args.preview:
        render(arm, args.preview, [('game', 55, 0), ('front', 8, 0), ('side', 10, 90), ('back', 30, 160)])


main()
