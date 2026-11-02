"""The thorn wall's vine segment from its TRELLIS source (JG-20i).

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/thorn_vine.py -- [--preview <directory>]

The source is the copied raw scan Art/Vfx/ThornVine/Source/ThornVine_trellis.glb (read-only). It is
closed as one shell (weld, thin extrusion, voxel remesh), cut flat at both ends so segments tile,
cleared of hidden shells, decimated to a crowd budget and checked for zero boundary edges. Colour is
ray-baked from the original surface onto fresh UVs, then graded: bark to a dark green-brown, the
pale thorns to bone (off Palette.Essence gold), and anything near Danger or the cyan band moved off.
The export is a unit segment: 1 x 1 x 1 bounds, base on the floor, length along Unity Z, centred.
ThornWallView scales each segment to the wall.
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

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Vfx', 'ThornVine')
SOURCE = os.path.join(OUT, 'Source', 'ThornVine_trellis.glb')
DANGER = (0xFF / 255, 0x4A / 255, 0x1F / 255)
# Up to 12 walls of up to five segments stand at once on a phone.
BUDGET = 1000
TEXTURE = 1024
# The scan runs along X, +-0.5. Cut just inside its ends so each cap is one flat face.
TRIM = 0.47


def log(text):
    print(text, flush=True)


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


def load_raw():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    raw = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
    for other in [o for o in bpy.context.scene.objects if o != raw]:
        bpy.data.objects.remove(other, do_unlink=True)
    raw.data = raw.data.copy()
    raw.data.transform(raw.matrix_world)
    raw.parent = None
    raw.matrix_world = Matrix.Identity(4)
    raw.name = 'Raw'
    bsdf = next(n for n in raw.data.materials[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    for link in list(bsdf.inputs['Metallic'].links):
        raw.data.materials[0].node_tree.links.remove(link)
    bsdf.inputs['Metallic'].default_value = 0
    return raw


def closed_master(raw):
    """One closed shell round the open scan, cut flat at both ends."""
    scale = max(raw.dimensions)
    master = raw.copy()
    master.data = raw.data.copy()
    master.name = 'Vine'
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
    apply(master, remesh)
    bm = bmesh.new()
    bm.from_mesh(master.data)
    for sign in (1, -1):
        cut = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
            plane_co=(sign * TRIM, 0, 0), plane_no=(sign, 0, 0), clear_outer=True)
        rim = [e for e in cut['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
        bmesh.ops.holes_fill(bm, edges=rim, sides=0)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(master.data)
    bm.free()
    log(f'MASTER faces {len(master.data.polygons)} open/nonmanifold {topology(master.data)}')
    return master


def drop_small_shells(part):
    """Keep shells over 1 % of the largest's area; voxel specks and the cut's crumbs go."""
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
    doomed = [f for k, members in enumerate(shells) if areas[k] < 0.01 * max(areas) for f in members]
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bm.to_mesh(part.data)
    bm.free()
    log(f'SHELLS kept {sum(a >= 0.01 * max(areas) for a in areas)} of {len(shells)}')


def decimate(part, budget):
    pristine = part.data
    for attempt in range(10):
        me = pristine.copy()
        part.data = me
        goal = budget * (0.97 - 0.03 * attempt)
        for _ in range(12):
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
        log(f'DECIMATE try {attempt}: {len(me.polygons)} tris, open/nonmanifold {stats}')
        if stats == (0, 0) and len(me.polygons) <= budget:
            break
    for p in me.polygons:
        p.use_smooth = True
    return stats == (0, 0) and len(me.polygons) <= budget


def bake(part, raw):
    """Ray-project the raw scan's albedo onto the part's fresh UVs."""
    while part.data.uv_layers:
        part.data.uv_layers.remove(part.data.uv_layers[0])
    part.data.uv_layers.new(name='UVMap')
    activate(part)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    image = bpy.data.images.new('T_ThornVine_Albedo', TEXTURE, TEXTURE, alpha=False)
    image.generated_color = (0.12, 0.1, 0.06, 1)
    mat = bpy.data.materials.new('Bake')
    mat.use_nodes = True
    target = mat.node_tree.nodes.new('ShaderNodeTexImage')
    target.image = image
    mat.node_tree.nodes.active = target
    part.data.materials.clear()
    part.data.materials.append(mat)
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
    settings.use_clear = True
    settings.use_pass_direct = False
    settings.use_pass_indirect = False
    settings.use_pass_color = True
    bpy.ops.object.select_all(action='DESELECT')
    raw.select_set(True)
    part.select_set(True)
    bpy.context.view_layer.objects.active = part
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
    return image


def srgb_to_lab(rgb):
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def grade(image):
    """Bark to dark green-brown, thorns to bone, then the colour law. Returns texels per class."""
    px = np.array(image.pixels[:], dtype=np.float32).reshape(-1, 4)
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in px[:, :3]])
    h, s, v = hsv[:, 0] * 360, hsv[:, 1], hsv[:, 2]
    thorn = (h > 38) & (h < 75) & (v > 0.55) & (s > 0.2)
    bark = ~thorn
    # Bark: hue pulled a third of the way toward olive (62 deg), darker than the moss floor.
    h[bark] = h[bark] + (62 - np.clip(h[bark], 0, 62)) * 0.35
    s[bark] = np.clip(s[bark] * 0.9, 0, 0.7)
    v[bark] = v[bark] * 0.48
    # Thorns: bone, pale and barely tinted, so they read against the bark from above.
    h[thorn] = 70
    s[thorn] = np.minimum(s[thorn] * 0.3, 0.18)
    v[thorn] = np.clip(v[thorn] * 0.88, 0, 0.82)
    rgb = np.array([colorsys.hsv_to_rgb(a / 360, b, c) for a, b, c in zip(h, s, v)])
    cyan = (h > 170) & (h < 205) & (s > 0.3)
    danger = np.linalg.norm(srgb_to_lab(rgb) - srgb_to_lab(np.array(DANGER)), axis=1) < 25
    assert not cyan.any() and not danger.any(), (int(cyan.sum()), int(danger.sum()))
    px[:, :3] = rgb
    image.pixels[:] = px.ravel()
    return int(thorn.sum()), int(bark.sum())


def to_unit_segment(part):
    """Length along Blender Y (Unity Z), width X, base at z 0, bounds 1 x 1 x 1, centred."""
    me = part.data
    me.transform(Matrix.Rotation(math.radians(90), 4, 'Z'))
    co = np.array([v.co[:] for v in me.vertices])
    lo, hi = co.min(0), co.max(0)
    centre = (lo + hi) / 2
    size = hi - lo
    log(f'SOURCE_SIZE width {size[0]:.3f} length {size[1]:.3f} height {size[2]:.3f}')
    me.transform(Matrix.Diagonal((1 / size[0], 1 / size[1], 1 / size[2], 1))
                 @ Matrix.Translation((-centre[0], -centre[1], -lo[2])))
    me.update()


def export(part, path):
    activate(part)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=False,
        bake_anim=False, path_mode='STRIP', bake_space_transform=True)
    log(f'EXPORTED {path}')


def render(part, directory):
    """Review renders: Eevee, sun plus sky; the game camera at a 10 m spoke's scale and the side."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 1200, 700
    scene.view_settings.view_transform = 'Standard'
    scene.world = bpy.data.worlds.new('Sky')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.36, 0.30, 1)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    floor = bpy.data.meshes.new('Floor')
    floor.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new('Floor', floor)
    fm = bpy.data.materials.new('Floor')
    fm.use_nodes = True
    fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.22, 0.10, 1)
    floor.materials.append(fm)
    scene.collection.objects.link(ground)
    # A 10 m spoke, 1.2 m wide, 1.6 m tall: four segments, alternate ones turned round.
    for k in range(4):
        seg = part.copy()
        seg.scale = (1.2, 2.5, 1.6)
        seg.location = (0, -3.75 + 2.5 * k, 0)
        seg.rotation_euler = (0, 0, math.pi * (k % 2))
        scene.collection.objects.link(seg)
    part.hide_render = True
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    cam.data.lens = 50
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, pitch, dist in (('game', 60, 22), ('side', 12, 12)):
        direction = Vector((math.cos(math.radians(pitch)), 0, math.sin(math.radians(pitch))))
        cam.location = Vector((0, 0, 0.8)) + direction * dist
        cam.rotation_euler = (Vector((0, 0, 0.8)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(directory, f'ThornVine-{name}.png')
        bpy.ops.render.render(write_still=True)
        log(f'RENDERED {scene.render.filepath}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preview')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    assert os.environ.get('PYTHONHASHSEED') == '0', 'Set PYTHONHASHSEED=0 before Blender starts'
    raw = load_raw()
    vine = closed_master(raw)
    drop_small_shells(vine)
    assert decimate(vine, BUDGET), f'will not decimate to {BUDGET} closed triangles'
    log(f'TRIS {len(vine.data.polygons)}')
    image = bake(vine, raw)
    log(f'GRADE thorn/bark texels {grade(image)}')
    image.filepath_raw = os.path.join(OUT, 'T_ThornVine_Albedo.png')
    image.file_format = 'PNG'
    image.save()
    mat = bpy.data.materials.new('M_ThornVine')
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    mat.node_tree.links.new(tex.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    vine.data.materials.clear()
    vine.data.materials.append(mat)
    bpy.data.objects.remove(raw, do_unlink=True)
    to_unit_segment(vine)
    vine.name = 'ThornVine'
    export(vine, os.path.join(OUT, 'ThornVine.fbx'))
    if args.preview:
        render(vine, args.preview)


main()
