"""Thornheart and its seed pod from the TRELLIS sources (JG-20f).

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/thornheart.py
    -- [--body-only | --pod-only] [--preview <directory>]

Sources are the copied raw scans under Art/Enemies/Thornheart/Source (read-only). Each scan is
closed as one shell (weld, thin extrusion, voxel remesh), split into parts by height, distance from
the trunk axis and source colour, each part capped, re-voxelized on the master's grid, lightly decimated and checked for
zero boundary edges. Colour is ray-baked from the original surface onto fresh UVs; the heart
crystal is repainted gold, and any texel in the cyan band or near Palette.Danger is moved off it. The source has four forked roots, not six.
"""
import argparse
import colorsys
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies', 'Thornheart')
SOURCE = os.path.join(OUT, 'Source')
DANGER = (0xFF / 255, 0x4A / 255, 0x1F / 255)
# Raw-scan frame: the trunk's axis, its foot, and the four root bearings (degrees, 0 = +X).
AXIS = (-0.02, 0.11)
TRUNK_RADIUS = 0.26
ROOT_BEARINGS = (-130.0, -50.0, 35.0, 145.0)
SHRINE = (-115.0, -65.0)
# Full detail, like the Shaman (owner, 2026-11-02): the scan's own density. Not yet measured on a phone.
BUDGET = {'Trunk': 80000, 'Crown': 40000, 'Heart': 8000, 'Root': 15000}
POD_BUDGET = 100000
TEXTURE = 2048


def log(text):
    print(text, flush=True)


def load_raw(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    raw = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    for other in [o for o in bpy.context.scene.objects if o != raw]:
        bpy.data.objects.remove(other, do_unlink=True)
    raw.data = raw.data.copy()
    raw.data.transform(raw.matrix_world)
    raw.parent = None
    raw.matrix_world = Matrix.Identity(4)
    raw.name = 'Raw'
    me = raw.data
    me.calc_loop_triangles()
    image = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE')
    # The pod's scan is 0.9 metallic: the colour bake would read its albedo as black.
    bsdf = next(n for n in me.materials[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    for link in list(bsdf.inputs['Metallic'].links):
        me.materials[0].node_tree.links.remove(link)
    bsdf.inputs['Metallic'].default_value = 0
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    verts = [v.co.copy() for v in me.vertices]
    tris = [tuple(t.vertices) for t in me.loop_triangles]
    uv = me.uv_layers.active.data
    uv_tris = [tuple(Vector((*uv[i].uv, 0)) for i in t.loops) for t in me.loop_triangles]
    centre_uv = np.array([[sum(u[k] for u in tri) / 3 for k in range(2)] for tri in uv_tris])
    colours = pixels[np.clip((centre_uv[:, 1] * h).astype(int), 0, h - 1),
                     np.clip((centre_uv[:, 0] * w).astype(int), 0, w - 1), :3]
    return {'object': raw, 'image': image, 'verts': verts, 'tris': tris, 'uv_tris': uv_tris,
            'colours': colours, 'bvh': BVHTree.FromPolygons(verts, tris, all_triangles=True)}


def topology(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    stats = (sum(e.is_boundary for e in bm.edges), sum(not e.is_manifold for e in bm.edges))
    bm.free()
    return stats


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply(obj, modifier):
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def closed_master(raw):
    """One closed shell round the open, self-overlapping scan."""
    scale = max(raw['object'].dimensions)
    master = raw['object'].copy()
    master.data = raw['object'].data.copy()
    master.name = 'Master'
    bpy.context.scene.collection.objects.link(master)
    bm = bmesh.new()
    bm.from_mesh(master.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(master.data)
    bm.free()
    solid = master.modifiers.new('Close', 'SOLIDIFY')
    solid.solidify_mode = 'EXTRUDE'
    solid.thickness = 0.006 * scale
    solid.offset = 0
    solid.use_even_offset = False
    apply(master, solid)
    remesh = master.modifiers.new('Shell', 'REMESH')
    remesh.mode = 'VOXEL'
    remesh.voxel_size = 0.003 * scale
    master['voxel'] = remesh.voxel_size
    apply(master, remesh)
    faces = sum(len(p.vertices) - 2 for p in master.data.polygons)
    if faces > 250000:
        dec = master.modifiers.new('Density', 'DECIMATE')
        dec.ratio = 250000 / faces
        apply(master, dec)
    log(f'MASTER faces {len(master.data.polygons)} open/nonmanifold {topology(master.data)}')
    return master


def face_frame(master, raw):
    """Per master face: centre, source colour (HSV) via the nearest source triangle."""
    me = master.data
    centres = np.array([p.center[:] for p in me.polygons])
    nearest = np.array([raw['bvh'].find_nearest(Vector(c))[2] for c in centres])
    rgb = raw['colours'][nearest]
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in rgb])
    return centres, hsv


def adjacency(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    pairs = [(e.link_faces[0].index, e.link_faces[1].index) for e in bm.edges if len(e.link_faces) == 2]
    bm.free()
    return np.array(pairs)


def smooth(labels, pairs, rounds=4):
    """Neighbour majority over edges; a face keeps its label on ties."""
    count = labels.max() + 1
    for _ in range(rounds):
        votes = np.zeros((len(labels), count))
        votes[np.arange(len(labels)), labels] += 1.5
        np.add.at(votes, (pairs[:, 0], labels[pairs[:, 1]]), 1)
        np.add.at(votes, (pairs[:, 1], labels[pairs[:, 0]]), 1)
        labels = votes.argmax(1)
    return labels


def components(face_ids, pairs):
    """Connected face sets (edge-sharing) among face_ids, largest first."""
    inside = np.zeros(pairs.max() + 1, bool)
    inside[face_ids] = True
    keep = pairs[inside[pairs[:, 0]] & inside[pairs[:, 1]]]
    parent = np.arange(len(inside))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for a, b in keep:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb
    groups = {}
    for f in face_ids:
        groups.setdefault(find(f), []).append(f)
    return sorted(groups.values(), key=len, reverse=True)


def body_labels(master, raw):
    """0 trunk, 1 heart, 2 crown, 3.. roots."""
    centres, hsv = face_frame(master, raw)
    h, s, v = hsv[:, 0] * 360, hsv[:, 1], hsv[:, 2]
    x, y, z = centres[:, 0] - AXIS[0], centres[:, 1] - AXIS[1], centres[:, 2]
    r = np.hypot(x, y)
    bearing = np.degrees(np.arctan2(y, x))
    labels = np.zeros(len(centres), int)
    labels[(h > 55) & (h < 170) & (z > 0.15)] = 2
    labels[((h < 45) | (h > 340)) & (s > 0.55) & (v > 0.45) & (z > 0.12) & (r < 0.2)] = 1
    rooted = (z < 0.0) & (r > TRUNK_RADIUS) & ~((bearing > SHRINE[0]) & (bearing < SHRINE[1]))
    gap = np.abs((bearing[:, None] - np.array(ROOT_BEARINGS)[None, :] + 180) % 360 - 180)
    labels[rooted] = 3 + gap[rooted].argmin(1)
    pairs = adjacency(master.data)
    labels = smooth(labels, pairs)
    # The heart is its crystal alone; every root is one limb. Strays rejoin the trunk.
    for label in [1] + [3 + k for k in range(len(ROOT_BEARINGS))]:
        groups = components(np.flatnonzero(labels == label), pairs)
        for stray in groups[1:]:
            labels[stray] = 0
    groups = components(np.flatnonzero(labels == 2), pairs)
    for stray in [g for g in groups if len(g) < 200]:
        labels[stray] = 0
    for k in range(labels.max() + 1):
        log(f'LABEL {k}: {(labels == k).sum()} faces')
    return labels


def extract(master, faces, name, budget):
    """A capped, decimated, closed part from a face set of the closed master."""
    me = master.data.copy()
    part = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(part)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    keep = np.zeros(len(bm.faces), bool)
    keep[faces] = True
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep[f.index]], context='FACES')
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    # Re-voxelize the capped part on the master's own grid, so the scan's detail survives: ragged
    # caps and cut seams become one clean manifold, hidden cavities go, then a light decimation.
    remesh = part.modifiers.new('Close', 'REMESH')
    remesh.mode = 'VOXEL'
    remesh.voxel_size = master['voxel']
    apply(part, remesh)
    drop_hidden_shells(part)
    assert decimate(part, budget), f'{name} will not decimate to {budget} closed triangles'
    return part


def drop_hidden_shells(part):
    """Delete shells enclosed by another (voxel cavities) and specks under 1 % of the largest."""
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.faces.ensure_lookup_table()
    shell = np.full(len(bm.faces), -1)
    shells = []
    for seed in bm.faces:
        if shell[seed.index] >= 0:
            continue
        stack, members = [seed], []
        shell[seed.index] = len(shells)
        while stack:
            face = stack.pop()
            members.append(face)
            for edge in face.edges:
                for other in edge.link_faces:
                    if shell[other.index] < 0:
                        shell[other.index] = len(shells)
                        stack.append(other)
        shells.append(members)
    areas = [sum(f.calc_area() for f in members) for members in shells]
    bvh = BVHTree.FromBMesh(bm)
    doomed = []
    for k, members in enumerate(shells):
        if areas[k] == max(areas):
            continue
        if areas[k] < 0.01 * max(areas):
            doomed += members
            continue
        # Inside another shell when most of three rays cross other shells an odd number of times.
        origin = members[0].calc_center_median() + members[0].normal * 1e-4
        odd = 0
        for direction in (Vector((0, 0, 1)), Vector((0.8, 0.1, -0.6)), Vector((-0.5, 0.85, 0.15))):
            crossings, start = 0, origin
            while True:
                hit = bvh.ray_cast(start, direction.normalized())
                if hit[0] is None:
                    break
                if shell[hit[2]] != k:
                    crossings += 1
                start = hit[0] + direction.normalized() * 1e-5
            odd += crossings % 2
        if odd >= 2:
            doomed += members
    dropped = len({shell[f.index] for f in doomed})
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bm.to_mesh(part.data)
    bm.free()
    log(f'SHELLS {part.name}: kept {len(shells) - dropped} of {len(shells)}')


def decimate(part, budget):
    pristine = part.data
    for attempt in range(10):
        me = pristine.copy()
        part.data = me
        goal = budget * (0.97 - 0.03 * attempt)
        for _ in range(8):
            tris = sum(len(p.vertices) - 2 for p in me.polygons)
            if tris <= goal:
                break
            dec = part.modifiers.new('Budget', 'DECIMATE')
            dec.ratio = max(goal / tris, 0.5)
            dec.use_collapse_triangulate = True
            apply(part, dec)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
        bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=1e-6)
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(me)
        bm.free()
        stats = topology(me)
        log(f'PART {part.name} try {attempt}: {len(me.polygons)} tris, open/nonmanifold {stats}')
        if stats == (0, 0) and len(me.polygons) <= budget:
            break
    for p in me.polygons:
        p.use_smooth = True
    return stats == (0, 0) and len(me.polygons) <= budget


def bake_target(part, image):
    mat = bpy.data.materials.new(part.name + '_Bake')
    mat.use_nodes = True
    target = mat.node_tree.nodes.new('ShaderNodeTexImage')
    target.image = image
    mat.node_tree.nodes.active = target
    part.data.materials.clear()
    part.data.materials.append(mat)


def bake(parts, raw, name, gold=()):
    """Ray-project the raw scan's albedo onto every part, one shared atlas. Gold parts bake to a
    scratch image first; their luminance is then mapped onto a gold ramp, off Palette.Danger."""
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
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.006)
    bpy.ops.object.mode_set(mode='OBJECT')
    image = bpy.data.images.new(name, TEXTURE, TEXTURE, alpha=False)
    image.generated_color = (0.2, 0.15, 0.1, 1)
    scratch = bpy.data.images.new(name + '_Gold', TEXTURE, TEXTURE, alpha=True)
    scratch.generated_color = (0, 0, 0, 0)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 4
    settings = scene.render.bake
    settings.use_selected_to_active = True
    settings.use_cage = False
    settings.cage_extrusion = 0.01
    settings.max_ray_distance = 0.04
    settings.margin = 8
    settings.use_clear = False
    settings.use_pass_direct = False
    settings.use_pass_indirect = False
    settings.use_pass_color = True
    for part in parts:
        bake_target(part, scratch if part.name in gold else image)
        bpy.ops.object.select_all(action='DESELECT')
        raw['object'].select_set(True)
        part.select_set(True)
        bpy.context.view_layer.objects.active = part
        bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
    if gold:
        px = np.array(image.pixels[:], dtype=np.float32).reshape(-1, 4)
        gx = np.array(scratch.pixels[:], dtype=np.float32).reshape(-1, 4)
        mask = gx[:, 3] > 0.5
        luma = np.clip((gx[mask, :3] @ np.array([0.3, 0.55, 0.15]) - 0.1) / 0.6, 0, 1)[:, None]
        dark, bright = np.array([0.55, 0.36, 0.06]), np.array([1.0, 0.86, 0.38])
        px[mask, :3] = dark + (bright - dark) * luma
        image.pixels[:] = px.ravel()
    return image


def srgb_to_lab(rgb):
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def colour_law(image):
    """No cyan-band texel; nothing within ΔE 20 of Danger (margin 25). Returns the counts moved."""
    px = np.array(image.pixels[:], dtype=np.float32).reshape(-1, 4)
    rgb = px[:, :3]
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in rgb])
    cyan = (hsv[:, 0] * 360 > 170) & (hsv[:, 0] * 360 < 205) & (hsv[:, 1] > 0.3)
    hsv[cyan, 0] = 140 / 360
    danger = np.linalg.norm(srgb_to_lab(rgb) - srgb_to_lab(np.array(DANGER)), axis=1) < 25
    hsv[danger, 0] = 44 / 360
    hsv[danger, 1] = np.minimum(hsv[danger, 1], 0.8)
    moved = cyan | danger
    if moved.any():
        rgb[moved] = np.array([colorsys.hsv_to_rgb(*c) for c in hsv[moved]])
    px[:, :3] = rgb
    image.pixels[:] = px.ravel()
    return int(cyan.sum()), int(danger.sum())


def game_material(parts, image, name, heart=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes['Principled BSDF']
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.85
    for part in parts:
        part.data.materials.clear()
        part.data.materials.append(mat)
    if heart:
        # Preview only: 20g lights the heart through its own renderer.
        glow = mat.copy()
        glow.name = name + '_HeartPreview'
        b = glow.node_tree.nodes['Principled BSDF']
        t = next(n for n in glow.node_tree.nodes if n.type == 'TEX_IMAGE')
        glow.node_tree.links.new(t.outputs['Color'], b.inputs['Emission Color'])
        b.inputs['Emission Strength'].default_value = 0.6
        heart.data.materials[0] = glow
    return mat


def place(parts, offset, scale):
    frame = Matrix.Scale(scale, 4) @ Matrix.Translation(-Vector(offset))
    for part in parts:
        part.data.transform(frame)


def set_pivot(part, pivot):
    part.data.transform(Matrix.Translation(-Vector(pivot)))
    part.location = pivot


def export(path, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=False,
        bake_anim=False, path_mode='STRIP', bake_space_transform=True)
    log(f'EXPORTED {path}')


def save_png(image, path):
    image.filepath_raw = path
    image.file_format = 'PNG'
    image.save()
    log(f'SAVED {path}')


def build_body(preview):
    raw = load_raw(os.path.join(SOURCE, 'Thornheart_trellis.glb'))
    foot = min(v.z for v in raw['verts'])
    top = max(v.z for v in raw['verts'])
    master = closed_master(raw)
    labels = body_labels(master, raw)
    parts = [extract(master, np.flatnonzero(labels == 0), 'Trunk', BUDGET['Trunk']),
             extract(master, np.flatnonzero(labels == 2), 'Crown', BUDGET['Crown']),
             extract(master, np.flatnonzero(labels == 1), 'Heart', BUDGET['Heart'])]
    # Root_0 is the front-left root; numbering runs round clockwise seen from above.
    order = sorted(range(len(ROOT_BEARINGS)), key=lambda k: (-90 - ROOT_BEARINGS[k]) % 360)
    for n, k in enumerate(order):
        parts.append(extract(master, np.flatnonzero(labels == 3 + k), f'Root_{n}', BUDGET['Root']))
    bpy.data.objects.remove(master, do_unlink=True)
    total = sum(len(p.data.polygons) for p in parts)
    log(f'TOTAL {total} tris')
    assert total <= 200000, total
    image = bake(parts, raw, 'T_Thornheart_Albedo', gold={'Heart'})
    log(f'COLOUR_LAW cyan/danger texels moved {colour_law(image)}')
    save_png(image, os.path.join(OUT, 'T_Thornheart_Albedo.png'))
    heart = next(p for p in parts if p.name == 'Heart')
    game_material(parts, image, 'M_Thornheart', heart if preview else None)
    scale = 2.0 / (top - foot)
    place(parts, (AXIS[0], AXIS[1], foot), scale)
    for part in parts:
        co = np.array([v.co[:] for v in part.data.vertices])
        if part.name == 'Trunk':
            pivot = (0, 0, 0)
        elif part.name == 'Crown':
            pivot = (*co[:, :2].mean(0), co[:, 2].min())
        elif part.name == 'Heart':
            pivot = tuple((co.min(0) + co.max(0)) / 2)
        else:
            reach = np.hypot(co[:, 0], co[:, 1])
            pivot = tuple(co[reach <= np.quantile(reach, 0.05)].mean(0))
        set_pivot(part, pivot)
    if not preview:
        heart.data.materials[0] = parts[0].data.materials[0]
    export(os.path.join(OUT, 'Thornheart.fbx'), parts)
    return parts, raw


def build_pod(preview):
    raw = load_raw(os.path.join(SOURCE, 'ThornPod_trellis.glb'))
    foot = min(v.z for v in raw['verts'])
    top = max(v.z for v in raw['verts'])
    centre = [sum(v[k] for v in raw['verts']) / len(raw['verts']) for k in range(2)]
    master = closed_master(raw)
    pod = extract(master, np.arange(len(master.data.polygons)), 'Pod', POD_BUDGET)
    bpy.data.objects.remove(master, do_unlink=True)
    image = bake([pod], raw, 'T_ThornPod_Albedo')
    log(f'COLOUR_LAW cyan/danger texels moved {colour_law(image)}')
    save_png(image, os.path.join(OUT, 'T_ThornPod_Albedo.png'))
    game_material([pod], image, 'M_ThornPod')
    place([pod], (centre[0], centre[1], foot), 1.0 / (top - foot))
    export(os.path.join(OUT, 'ThornPod.fbx'), [pod])
    return [pod], raw


def render(parts, raw, directory, label, views):
    """Review renders: Eevee, sun plus sky, from the game camera and the front."""
    scene = bpy.context.scene
    bpy.data.objects.remove(raw['object'], do_unlink=True)
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = scene.render.resolution_y = 900
    scene.view_settings.view_transform = 'Standard'
    scene.world = bpy.data.worlds.new('Sky')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.36, 0.30, 1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.9
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    floor = bpy.data.meshes.new('Floor')
    floor.from_pydata([(-9, -9, 0), (9, -9, 0), (9, 9, 0), (-9, 9, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new('Floor', floor)
    mat = bpy.data.materials.new('Floor')
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.22, 0.10, 1)
    floor.materials.append(mat)
    scene.collection.objects.link(ground)
    corners = [p.matrix_world @ Vector(c) for p in parts for c in p.bound_box]
    centre = sum(corners, Vector()) / len(corners)
    size = max((c - centre).length for c in corners)
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    cam.data.lens = 50
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, pitch in views:
        direction = Vector((0, -math.cos(math.radians(pitch)), math.sin(math.radians(pitch))))
        cam.location = centre + direction * size * 3.4
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(directory, f'{label}-{name}.png')
        bpy.ops.render.render(write_still=True)
        log(f'RENDERED {scene.render.filepath}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--body-only', action='store_true')
    parser.add_argument('--pod-only', action='store_true')
    parser.add_argument('--preview')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    assert os.environ.get('PYTHONHASHSEED') == '0', 'Set PYTHONHASHSEED=0 before Blender starts'
    views = [('game', 55), ('front', 8)]
    if not args.pod_only:
        parts, raw = build_body(args.preview)
        if args.preview:
            render(parts, raw, args.preview, 'Thornheart', views)
    if not args.body_only:
        parts, raw = build_pod(args.preview)
        if args.preview:
            render(parts, raw, args.preview, 'ThornPod', views)


main()
