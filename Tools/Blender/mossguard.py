"""JG-14b: approved Mossguard sources -> unchanged KayKit Rig_Medium and atlas props.

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/mossguard.py
    [-- --pose-sheet <png>]

Source repair (raw stays read-only; run once, inspect the review before runtime conversion):
    -- --prepare-shield <raw seed42.glb> --review-dir <directory>
Fits the generated rear backing and its inward-facing grip behind the front, removes only the
reddish hanging speck, then delegates closure/texture transfer to repair_scan.py. The repaired
source is Sources/MossguardShield_src.glb. Never regenerates or overwrites Mossguard_src.glb.
The runtime build reads both sources and the existing full atlas; it never writes the atlas.
"""
import argparse
import hashlib
import json
import math
import os
import runpy
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform, closest_point_on_tri

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

BODY_HASH = 'c2e4998b7ebbaF4aa212c8905bed4da3e5a9785da8ef695ed00d354509d9c39b'.lower()
SHEET_CLIPS = [('General', 'Idle_B'), ('MovementBasic', 'Walking_B'),
               ('CombatMelee', 'Melee_Blocking'), ('CombatMelee', 'Melee_Block_Attack'),
               ('CombatMelee', 'Melee_Block_Hit'), ('General', 'Hit_A'), ('General', 'Death_A')]


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def components(bm):
    remaining = set(bm.verts)
    result = []
    while remaining:
        seed = min(remaining, key=lambda v: v.index)
        remaining.remove(seed)
        part, stack = [seed], [seed]
        while stack:
            v = stack.pop()
            for e in v.link_edges:
                w = e.other_vert(v)
                if w in remaining:
                    remaining.remove(w)
                    part.append(w)
                    stack.append(w)
        result.append(part)
    return sorted(result, key=len, reverse=True)


def world_mesh(obj):
    obj.data = obj.data.copy()
    obj.data.transform(obj.matrix_world)
    obj.parent = None
    obj.matrix_world = Matrix.Identity(4)


def texture_sampler(obj):
    """Nearest source triangle, barycentrically sampled within its own UV island."""
    me = obj.data
    me.calc_loop_triangles()
    verts = [v.co.copy() for v in me.vertices]
    tris = [tuple(t.vertices) for t in me.loop_triangles]
    uv = me.uv_layers.active.data
    uvs = [tuple(Vector((*uv[i].uv, 0)) for i in t.loops) for t in me.loop_triangles]
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


def prepare_shield(raw, review):
    os.makedirs(review, exist_ok=True)
    bpy.ops.import_scene.gltf(filepath=os.path.abspath(raw))
    obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    world_mesh(obj)
    sample = texture_sampler(obj)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bm.verts.index_update()
    parts = components(bm)
    assert len(parts) == 2, 'Seed 42 has front and rear assemblies; do not guess another source'
    front, rear = parts
    assert max(v.co.y for v in front) < -.09
    # Remove the accidental red hanging nub, not the brown backing or green vine bindings.
    red = []
    for v in rear:
        r, g, b = sample(v.co)
        if (v.co.y > .18 and -.04 < v.co.z < .16 and r > g * 1.4 and r > b * 1.2
                or abs(v.co.x) < .045 and v.co.y > .20 and -.04 < v.co.z < .13):
            red.append(v)
    assert red, 'Red protrusion mask no longer matches selected scan'
    bmesh.ops.delete(bm, geom=red, context='VERTS')
    # Turning depth over exposes the generator's blank inside of the backing. Project its
    # existing outside wood texture onto that inside, within one source UV triangle per face.
    # This preserves the selected wood grain rather than inventing a replacement material.
    me = obj.data
    me.calc_loop_triangles()
    verts = [v.co.copy() for v in me.vertices]
    triangles = [t for t in me.loop_triangles if not
                 (abs(t.center.x) < .045 and t.center.y > .20 and -.04 < t.center.z < .13)]
    tree = BVHTree.FromPolygons(verts, [tuple(t.vertices) for t in triangles], all_triangles=True)
    source_uv = me.uv_layers.active.data
    layer = bm.loops.layers.uv.active
    rear_set = set(v for v in rear if v.is_valid)
    projected = 0
    for face in bm.faces:
        center = face.calc_center_median()
        if not all(v in rear_set for v in face.verts) or center.y < .07 or face.normal.y > -.3:
            continue
        hit, _, index, _ = tree.ray_cast(Vector((center.x, .6, center.z)), Vector((0, -1, 0)))
        if index is None:
            continue
        triangle = triangles[index]
        a, b, c = (verts[i] for i in triangle.vertices)
        ua, ub, uc = (Vector((*source_uv[i].uv, 0)) for i in triangle.loops)
        for loop in face.loops:
            point = Vector((loop.vert.co.x, hit.y, loop.vert.co.z))
            q = barycentric_transform(closest_point_on_tri(point, a, b, c), a, b, c, ua, ub, uc)
            loop[layer].uv = q.xy
        projected += 1
    for v in rear:
        if v.is_valid:
            # Original grip faces into the huge separation. Turn depth over so it faces the
            # wielder, compress depth 35%, and overlap the backing with the front's rear shell.
            v.co.y = -.045 - .65 * v.co.y
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    fitted = os.path.join(review, 'Shield-fitted-before-repair.glb')
    activate(obj)
    bpy.ops.export_scene.gltf(filepath=fitted, export_format='GLB', use_selection=True)
    with open(os.path.join(review, 'shield-fitting.json'), 'w') as f:
        json.dump({'raw': os.path.abspath(raw), 'raw_sha256': hashlib.sha256(open(raw, 'rb').read()).hexdigest(),
                   'removed_red_vertices': len(red), 'rear_depth_transform': 'y = -0.045 - 0.65*y',
                   'backing_faces_reusing_outer_wood_uv': projected,
                   'kept': ['stone face and emblem', 'bark frame', 'vine bindings', 'wooden backing', 'rear grip']}, f, indent=2)
    sys.argv = ['repair_scan.py', '--', fitted, os.path.join(SOURCES, 'MossguardShield_src.glb'),
                '--preview-dir', review, '--report', os.path.join(review, 'shield-repair.json')]
    runpy.run_path(os.path.join(HERE, 'repair_scan.py'), run_name='__main__')
    finish_shield_source(review)


def finish_shield_source(review):
    """Audit the serialized source; strip detached reconstruction crumbs, never grip holes."""
    path = os.path.join(SOURCES, 'MossguardShield_src.glb')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    obj = load_source('MossguardShield')[0]
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bm.verts.index_update()
    parts = components(bm)
    for part in parts[1:]:
        extent = max(max(v.co[k] for v in part) - min(v.co[k] for v in part) for k in range(3))
        assert extent < .04, 'A real shield assembly is still disconnected'
        bmesh.ops.delete(bm, geom=part, context='VERTS')
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume() > 0
    bm.to_mesh(obj.data)
    bm.free()
    activate(obj)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True)
    # Re-read the final file, not just the in-memory data block (the earlier missing-mesh trap).
    bpy.ops.wm.read_factory_settings(use_empty=True)
    obj = load_source('MossguardShield')[0]
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bm.verts.index_update()
    assert len(components(bm)) == 1 and all(e.is_manifold for e in bm.edges)
    assert bm.calc_volume() > 0
    report = {'connected_components': 1, 'boundary_edges': 0, 'nonmanifold_edges': 0,
              'outward_volume': bm.calc_volume(), 'triangles': sum(len(f.verts) - 2 for f in bm.faces),
              'discarded_detached_crumbs': [len(p) for p in parts[1:]],
              'dimensions': list(obj.dimensions)}
    bm.free()
    for mat in obj.data.materials:
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        assert mat.blend_method == 'OPAQUE' and not mat.use_backface_culling
        assert bsdf.inputs['Alpha'].default_value == 1 and bsdf.inputs['Transmission Weight'].default_value == 0
    report['opaque_double_sided'] = True
    with open(os.path.join(review, 'shield-final-reimport.json'), 'w') as f:
        json.dump(report, f, indent=2)
    print('SHIELD_SOURCE_REIMPORTED', report, flush=True)


def load_source(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SOURCES, name + '_src.glb'))
    objs = [o for o in set(bpy.data.objects) - before if o.type == 'MESH']
    for obj in objs:
        world_mesh(obj)
    return sorted(objs, key=lambda o: o.name)


def reduce(obj, count, voxel=None):
    low = obj.copy()
    low.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(low)
    activate(low)
    if low.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    while low.data.uv_layers:
        low.data.uv_layers.remove(low.data.uv_layers[0])
    bm = bmesh.new()
    bm.from_mesh(low.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bm.verts.index_update()
    for part in components(bm):
        if len(part) < 30:
            bmesh.ops.delete(bm, geom=part, context='VERTS')
    bm.to_mesh(low.data)
    bm.free()
    if voxel:
        # The detailed source has narrow cracks and layered sheets. A coarser runtime voxel
        # pass merges these while leaving the ~8 cm grip opening intact; otherwise collapse
        # stalls above 2k faces on its many tiny tunnels, despite the source being closed.
        low.data.remesh_voxel_size = voxel
        low.data.remesh_voxel_adaptivity = 0
        bpy.ops.object.voxel_remesh()
    low.data.calc_loop_triangles()
    dec = low.modifiers.new('RuntimeBudget', 'DECIMATE')
    dec.ratio = min(1, count / len(low.data.loop_triangles))
    dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=dec.name)
    bm = bmesh.new()
    bm.from_mesh(low.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(low.data)
    bm.free()
    low.data.update()
    return low


def paint(obj, sample, shield=False, eye=None):
    uv = obj.data.uv_layers.new(name='UVMap').data
    kinds = {}
    for face in obj.data.polygons:
        p = face.center
        r, g, b = sample(p) if sample else (0, 0, 0)
        lum = .2126 * r + .7152 * g + .0722 * b
        if eye:
            swatch = eye
        elif shield:
            swatch = 'pitcher_root' if r > g * 1.06 else 'croak_moss'
            if .26 < abs(p.z) < .38 and g > r * .96 and g > b * 1.3:
                swatch = 'pitcher_leaf'
        elif lum < .20 and (abs(p.x) > .36 or p.z < -.35):
            swatch = 'socket'
        elif r > g * 1.06 and r > b * 1.12:
            swatch = 'pitcher_root'
        elif g > r * 1.18 and g > b * 1.25 and p.z < .02:
            swatch = 'pitcher_leaf'
        elif lum > .29 and max(r, g, b) - min(r, g, b) < .13:
            swatch = 'panther_belly'
        else:
            swatch = 'panther_fur'
        kinds[swatch] = kinds.get(swatch, 0) + 1
        for li in face.loop_indices:
            z = obj.data.vertices[obj.data.loops[li].vertex_index].co.z
            uv[li].uv = uv_for(swatch, max(.08, min(.85, .45 - z * .6)))
        face.use_smooth = True
    obj.data.materials.clear()
    material = bpy.data.materials.get('enemy') or bpy.data.materials.new('enemy')
    obj.data.materials.append(material)
    print('PAINT', obj.name, kinds, flush=True)


def segment_distance(p, a, b):
    ab = b - a
    t = max(0, min(1, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - a - t * ab).length


def retain_shield_emblem(obj):
    """Trace the selected source's diamond/three-branch crest as a small dark stone inlay.

    A blind 384-triangle collapse spends the budget on the bark silhouette and erases this
    shallow engraving and vine bindings. Reserve 72 triangles for the existing graphic and
    80 for four crossed bindings, projected onto the cut front. Closed narrow reliefs overlap
    the front; no texture or material is added.
    Coordinates are landmarks from seed 42, in that scan's own frame.
    """
    tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    diamond = [(0, .185), (.032, .105), (0, .045), (-.032, .105)]
    branch = [(-.092, .065), (0, -.012), (.092, .065), (.059, -.003), (0, -.056), (-.059, -.003)]
    shapes = [diamond] + [[(x * s, z * s - shift) for x, z in branch]
                         for s, shift in [(1, 0), (.83, .080), (.65, .145)]]
    layers = [(shape, 'croak_moss', .96) for shape in shapes]
    for z, slope in [(.31, .13), (.31, -.13), (-.30, .15), (-.30, -.15)]:
        shape = [(-.275, z - slope * .275 - .012), (0, z - .012), (.275, z + slope * .275 - .012),
                 (.275, z + slope * .275 + .012), (0, z + .012), (-.275, z - slope * .275 + .012)]
        layers.append((shape, 'pitcher_leaf', .65))
    for shape, swatch, shade in layers:
        faces = []
        front, back = [], []
        for x, z in shape:
            hit, _, _, _ = tree.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)))
            assert hit is not None, 'Crest must lie on the selected shield face'
            front.append(bm.verts.new((x, hit.y - .004, z)))
            back.append(bm.verts.new((x, hit.y + .006, z)))
        if swatch == 'pitcher_leaf':
            # The middle pair follows the convex stone; a single quad cuts through the face
            # and only its ends remain visible. Two spans retain the crossing in both views.
            for ids in [(0, 1, 4, 5), (1, 2, 3, 4)]:
                faces += [bm.faces.new([front[i] for i in ids]), bm.faces.new([back[i] for i in reversed(ids)])]
        else:
            faces += [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
        for i in range(len(shape)):
            j = (i + 1) % len(shape)
            faces.append(bm.faces.new((front[i], back[i], back[j], front[j])))
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        result = bmesh.ops.triangulate(bm, faces=faces)
        for face in result['faces']:
            face.smooth = True
            for loop in face.loops:
                loop[uv].uv = uv_for(swatch, shade)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def fit_and_skin(obj, arm):
    """Read-only rig. Source landmarks fit the sculpt to that rig, never the reverse."""
    head = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    tail = {b.name: arm.matrix_world @ b.tail_local for b in arm.data.bones}
    names = [b.name for b in arm.data.bones]
    for name in names:
        obj.vertex_groups.new(name=name)
    floor, top = -.44786784, .44484299
    scale = tail['head'].z / (top - floor)
    # Source wrists are x=+/-0.414, shoulder x=+/-0.16; the hand bone starts at .787 m.
    shoulder = .16 * scale
    arm_stretch = (head['hand.l'].x - shoulder) / (.414 * scale - shoulder)
    for v in obj.data.vertices:
        source = v.co.copy()
        p = Vector((source.x * scale, (source.y + .075) * scale, (source.z - floor) * scale))
        side = 'l' if source.x >= 0 else 'r'
        is_arm = abs(source.x) > .16 and source.z > .065
        is_tail = source.y > .055 and source.z < -.10
        if is_arm:
            p.x = math.copysign(shoulder + (abs(p.x) - shoulder) * arm_stretch, p.x)
            # Shoulder/hand line of the sculpt is slightly pitched. Smoothly align its wrist
            # center with KayKit's T-pose without flattening the muscular cross-section.
            blend = min(1, max(0, (abs(source.x) - .16) / .19))
            p.z += (head['hand.l'].z - (.165 - floor) * scale) * blend
            p.y += (.115 * scale) * blend
            allowed = ['chest', 'upperarm.' + side, 'lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
            if abs(source.x) > .35:
                allowed = ['lowerarm.' + side, 'wrist.' + side, 'hand.' + side]
        elif is_tail:
            allowed = ['hips']
        elif source.z < -.15:
            allowed = ['hips', 'upperleg.' + side, 'lowerleg.' + side, 'foot.' + side, 'toes.' + side]
            if source.z < -.34:
                allowed = ['lowerleg.' + side, 'foot.' + side, 'toes.' + side]
        elif source.z > .26:
            allowed = ['head']
        else:
            allowed = ['hips', 'spine', 'chest', 'head']
        scored = sorted(((1 / (segment_distance(p, head[n], tail[n]) + .03) ** 4, n) for n in allowed), reverse=True)[:2]
        total = sum(w for w, _ in scored)
        for w, n in scored:
            obj.vertex_groups[n].add([v.index], w / total, 'REPLACE')
        v.co = arm.matrix_world.inverted() @ p
    obj.parent = arm
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    mod = obj.modifiers.new('Rig_Medium', 'ARMATURE')
    mod.object = arm


def export(path, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=False,
        add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim=False, path_mode='STRIP', bake_space_transform=len(objects) == 1)


def build():
    assert hashlib.sha256(open(os.path.join(SOURCES, 'Mossguard_src.glb'), 'rb').read()).hexdigest() == BODY_HASH
    atlas_hash = hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source = load_source('Mossguard')
    high = max(source, key=lambda o: len(o.data.polygons))
    body = reduce(high, 1960)
    body.name = 'Mossguard_Body'
    paint(body, texture_sampler(high))
    # Preserve the approved small eye meshes explicitly; a global 2k collapse loses the irises.
    eyes = []
    for obj in source:
        if obj == high or 'Highlight' in obj.name:
            continue
        low = reduce(obj, 24)
        paint(low, None, eye='frog_green' if 'Iris' in obj.name else 'socket')
        eyes.append(low)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [body] + eyes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    # Joining source eye meshes may duplicate the same material slot; keep exactly one.
    for f in body.data.polygons:
        f.material_index = 0
    while len(body.data.materials) > 1:
        body.data.materials.pop(index=1)
    for obj in source:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=RIG)
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    for obj in list(bpy.context.scene.objects):
        if obj not in (arm, body):
            bpy.data.objects.remove(obj, do_unlink=True)
    assert arm.name == 'Rig_Medium' and len(arm.data.bones) == 23
    fit_and_skin(body, arm)
    body_triangles = len(body.data.polygons)
    assert 1200 <= body_triangles <= 2200
    for v in body.data.vertices:
        assert 1 <= len(v.groups) <= 4 and abs(sum(g.weight for g in v.groups) - 1) < 1e-4
        assert all(body.vertex_groups[g.group].name not in ('root', 'handslot.l', 'handslot.r') for g in v.groups)
    export(os.path.join(OUT, 'Mossguard.fbx'), [arm, body])
    source = load_source('MossguardShield')
    high = max(source, key=lambda o: len(o.data.polygons))
    shield = reduce(high, 232, voxel=.012)
    shield.name = 'MossguardShield'
    paint(shield, texture_sampler(high), shield=True)
    retain_shield_emblem(shield)
    # The selected scan has a grip at each side of the backing, not at its center. Use its
    # +X grip (measured cross-section x=.200..268, y=-.050..-.003) for the left hand. This
    # puts the shield center over the chest in Melee_Blocking, rather than beside the arm.
    # Grip center in fitted source coordinates. Runtime local +Z is the front; Blender -Y.
    # Exporter maps Blender (X,Y,Z) -> Unity (X,Z,-Y). Origin lies inside the retained grip.
    for v in shield.data.vertices:
        v.co = (v.co - Vector((.234, -.026, 0))) * 1.05
    assert 150 <= len(shield.data.polygons) <= 400, len(shield.data.polygons)
    assert body_triangles + len(shield.data.polygons) <= 2500
    export(os.path.join(OUT, 'MossguardShield.fbx'), [shield])
    assert hashlib.sha256(open(ATLAS, 'rb').read()).hexdigest() == atlas_hash
    print('RUNTIME_TRIANGLES', len(body.data.polygons), len(shield.data.polygons), flush=True)


def pose_sheet(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, 'Mossguard.fbx'))
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, 'MossguardShield.fbx'))
    shield = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and o != body)
    world_mesh(shield)
    # Match Unity: identity transform under handslot.l, not a compensating pose offset.
    shield.parent = arm
    shield.parent_type = 'BONE'
    shield.parent_bone = 'handslot.l'
    bone = arm.data.bones['handslot.l']
    shield.matrix_world = arm.matrix_world @ bone.matrix_local @ Matrix.Rotation(-math.pi / 2, 4, 'X')
    # Blender bone parenting includes a tail offset; explicitly cancel it.
    bpy.context.view_layer.update()
    shield.matrix_world = arm.matrix_world @ bone.matrix_local @ Matrix.Rotation(-math.pi / 2, 4, 'X')
    image = bpy.data.images.load(ATLAS)
    for obj in (body, shield):
        mat = obj.data.materials[0]
        mat.use_nodes = True
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = image
        tex.interpolation = 'Closest'
        mat.node_tree.links.new(tex.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    actions = {}
    for file in sorted(set(f for f, _ in SHEET_CLIPS)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(CLIPS, 'Rig_Medium_' + file + '.fbx'))
        for obj in set(bpy.data.objects) - before:
            bpy.data.objects.remove(obj, do_unlink=True)
    for action in bpy.data.actions:
        actions[action.name.split('|')[-1]] = action
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
    arm.animation_data_create()
    for _, name in SHEET_CLIPS:
        assert name in actions, (name, sorted(actions))
        action = actions[name]
        arm.animation_data.action = action
        start, end = action.frame_range
        cells = []
        for eye in (Vector((0, -5, 3.4)), Vector((-3, -4, 2.2))):
            cam.location = eye
            cam.rotation_euler = (Vector((0, 0, .7)) - eye).to_track_quat('-Z', 'Y').to_euler()
            for t in (.0, .45, .8):
                scene.frame_set(round(start + (end - start) * t))
                scene.render.filepath = os.path.join(bpy.app.tempdir, 'mossguard-cell.png')
                bpy.ops.render.render(write_still=True)
                img = bpy.data.images.load(scene.render.filepath)
                cells.append(np.flipud(np.array(img.pixels[:], np.float32).reshape(cell, cell, 4)[..., :3]))
                bpy.data.images.remove(img)
        rows.append(np.concatenate(cells, axis=1))
        print('POSE_ROW', name, flush=True)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    save_png(np.concatenate(rows, axis=0), path)


def main():
    assert os.environ.get('PYTHONHASHSEED') == '0', 'Set PYTHONHASHSEED=0 for stable FBX node IDs'
    parser = argparse.ArgumentParser()
    parser.add_argument('--prepare-shield')
    parser.add_argument('--review-dir')
    parser.add_argument('--pose-sheet')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.prepare_shield:
        assert args.review_dir, 'Keep source fitting and repair audit in a review directory'
        prepare_shield(args.prepare_shield, os.path.abspath(args.review_dir))
    else:
        build()
        if args.pose_sheet:
            pose_sheet(args.pose_sheet)


if __name__ == '__main__':
    main()
