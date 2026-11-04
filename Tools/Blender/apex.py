"""JG-30d: TRELLIS matriarch -> enemy atlas, one skinned body on Rig_Panther, ten in-place clips.

PYTHONHASHSEED=0 blender --background --factory-startup --python Tools/Blender/apex.py
    [-- --preview-dir <directory>]

The source is read only. Fit the Panther's named hierarchy to this sculpt, preserving its heavy
shoulders, short ruff and naturally curved tail; BodyScale belongs to JG-30f's data, not this file.
The inherited distance weights, IK poser and FBX conventions are panther.py's. Head is an additional
non-deforming attachment bone, parented to the existing lowercase head. No mesh-name contract.
Mossguard's 1,200-2,200 body-triangle budget is the stricter interpretation of the spec's crowd budget.
"""
import argparse
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
import panther as cat
from enemy_atlas import build_atlas

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SOURCE = os.path.join(HERE, 'Sources', 'Apex_trellis.glb')
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies')
TEX = os.path.join(OUT, 'Textures')
FPS = 30


def shape(obj):
    """Source landmarks: feet z=-.267, forelegs y=-.32, hindlegs y=.10, rump y=.19.
    Normalise nose-to-rump to 1.52 m, as Panther; the heavier proportions come from the sculpt.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    origin = Vector((-.001, -.11, min(v.co.z for v in bm.verts)))
    scale = 1.52 / (.19 - min(v.co.y for v in bm.verts))
    for vert in bm.verts:
        vert.co = (vert.co - origin) * scale
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return scale


def source_colours(hi, lo):
    """Sample only within the nearest source triangle's own UV island, never across seams."""
    me = hi.data
    me.calc_loop_triangles()
    vertices = [v.co.copy() for v in me.vertices]
    triangles = list(me.loop_triangles)
    tree = BVHTree.FromPolygons(vertices, [tuple(t.vertices) for t in triangles], all_triangles=True)
    uv = me.uv_layers.active.data
    image = next(n.image for n in me.materials[0].node_tree.nodes
                 if n.type == 'TEX_IMAGE' and n.image.colorspace_settings.name == 'sRGB')
    w, h = image.size
    pixels = np.array(image.pixels[:], np.float32).reshape(h, w, 4)
    result = []
    for poly in lo.data.polygons:
        samples = []
        # Supersample inside the reduced face so a narrow painted scar survives a collapse.
        points = [poly.center] + [poly.center.lerp(lo.data.vertices[i].co, .7) for i in poly.vertices]
        for point in points:
            near, _, index, _ = tree.find_nearest(point)
            tri = triangles[index]
            a, b, c = (vertices[i] for i in tri.vertices)
            uvs = [Vector((*uv[i].uv, 0)) for i in tri.loops]
            q = barycentric_transform(near, a, b, c, *uvs)
            samples.append(pixels[min(h-1, max(0, int(q.y*h))), min(w-1, max(0, int(q.x*w))), :3])
        result.append(max(samples, key=lambda colour: float(cat.luma(colour))))
    return np.array(result)


def classify(body, colours):
    # Pale, low-saturation painted stripes and muzzle; all other paint becomes charcoal violet.
    # The source's eyes are dark. No green/glint swatch is introduced by the Panther classifier.
    kinds = np.where(cat.luma(colours) > .30, 'apex_scar', 'apex_coat').astype(object)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.faces.ensure_lookup_table()
    neighbours = [[g.index for e in f.edges for g in e.link_faces if g is not f] for f in bm.faces]
    for _ in range(2):
        cleaned = kinds.copy()
        for i, near in enumerate(neighbours):
            if len(near) and all(kinds[j] != kinds[i] for j in near):
                cleaned[i] = kinds[near[0]]
        kinds = cleaned
    bm.free()
    # Dark nose/eyes on the front of the head, kept independent of any pale muzzle stripe.
    top = max(v.co.z for v in body.data.vertices)
    nose = min(v.co.y for v in body.data.vertices)
    for poly in body.data.polygons:
        p = poly.center
        eye = nose + .15 < p.y < nose + .30 and .68*top < p.z < .80*top and abs(p.x) > .12
        nose_tip = p.y < nose + .055 and .52*top < p.z < .70*top
        if eye or nose_tip:
            kinds[poly.index] = 'frog_pupil'
    names, counts = np.unique(kinds.astype(str), return_counts=True)
    print('PAINT', dict(zip(names.tolist(), counts.tolist())), flush=True)
    assert np.sum(kinds == 'apex_coat') > len(kinds)*.55, 'Source paint no longer matches charcoal coat'
    assert np.sum(kinds == 'apex_scar') > len(kinds)*.025, 'Scar stripes lost in reduction'
    return kinds


def skeleton(body):
    marks = cat.landmarks(body.data)
    bones = cat.skeleton(marks)
    # Fit the three tail segments to the curve, rather than pulling the sculpt straight.
    co = np.array([tuple(v.co) for v in body.data.vertices])
    root, tip = marks['tail_root'], marks['tail_tip']
    points = [root]
    for i in range(1, 4):
        y = root.y + (tip.y-root.y)*i/3
        band = co[(np.abs(co[:, 1]-y) < .055) & (co[:, 2] > .35*marks['top'])]
        points.append(Vector(tuple(band.mean(0))) if len(band) else root.lerp(tip, i/3))
    for i in range(3):
        bones[f'tail_{i+1}'] = ('hips' if i == 0 else f'tail_{i}', points[i], points[i+1])
    # Eye-attachment anchor at the same skull location; old lowercase head remains unchanged.
    skull = bones['head'][1]
    bones['Head'] = ('head', skull.copy(), skull + Vector((0, -.06, 0)))
    return marks, bones


def clips():
    base = BASE_CLIPS()
    still = dict(offset=cat.v(0, 0, -.02))
    airborne = dict(hips=(-4, 0, 0), chest=(6, 0, 0), neck=(-8, 0, 0), head=(-3, 0, 0),
                    tail_1=(-10, 0, 0), tail_2=(-6, 0, 0),
                    legs={'front.l':(48,-25,-15,0), 'front.r':(42,-20,-15,0),
                          'back.l':(-38,35,10,0), 'back.r':(-32,30,10,0)})
    # Core supplies the leap's world-space height; this pose tucks the legs without translating root.
    pounce = {0: airborne, 9: dict(airborne, head=(-8,0,0)), 18: airborne}
    ready = dict(offset=cat.v(0,0,-.10), chest=(8,0,0), neck=(12,0,0), head=(-8,0,0))
    left = dict(offset=cat.v(0,0,-.08), hips=(0,0,12), chest=(-8,-5,-24), head=(0,0,12),
                legs={'front.l':(70,-20,-25,0), 'front.r':(-12,25,-10,0)})
    right = dict(offset=cat.v(0,0,-.08), hips=(0,0,-12), chest=(-8,5,24), head=(0,0,-12),
                 legs={'front.r':(70,-20,-25,0), 'front.l':(-12,25,-10,0)})
    return {
        'Idle': base['Idle'],
        'Run': (True, cat.lunge()),
        'Crouch': base['Crouch'],
        'Pounce': (False, pounce),
        'Land': (False, {0:airborne, 4:dict(offset=cat.v(0,0,-.20), chest=(12,0,0),
                       neck=(18,0,0), head=(-14,0,0)), 9:ready, 18:still}),
        'Dash': (True, {0:dict(airborne, offset=cat.v(0,0,-.09)),
                       6:dict(airborne, offset=cat.v(0,0,-.06), tail_3=(0,0,8)),
                       12:dict(airborne, offset=cat.v(0,0,-.09))}),
        'Rake': (False, {0:still, 4:ready, 9:left, 14:ready, 20:right, 25:ready, 30:still}),
        'Roar': (False, {0:still, 8:dict(offset=cat.v(0,0,-.06), chest=(-8,0,0),
                       neck=(-18,0,0), head=(-22,0,0), jaw=(-26,0,0)),
                       18:dict(offset=cat.v(0,0,-.03), chest=(-6,0,0), neck=(-14,0,0),
                       head=(-20,0,0), jaw=(-34,0,0)), 30:still}),
        'Hit': base['Hit'],
        'Death': base['Death'],
    }


def author_clips(arm, body, bones):
    # Reuse the Panther baker, including quaternion continuity, full bone keys and grounded death.
    original = cat.clips
    cat.clips = clips
    try:
        return cat.author_clips(arm, body, bones)
    finally:
        cat.clips = original


def export(arm, body):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Apex.fbx'), use_selection=True,
        object_types={'ARMATURE','MESH'}, apply_scale_options='FBX_SCALE_ALL',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype='NULL', bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode='STRIP')


def preview(directory):
    """Inspect the exported/reimported FBX, not the pre-export authoring object."""
    os.makedirs(directory, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT,'Apex.fbx'))
    arm = next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type=='MESH')
    material = bpy.data.materials.new('preview')
    material.use_nodes = True
    nodes = material.node_tree.nodes
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(os.path.join(TEX,'T_Enemy_Albedo.png'))
    tex.interpolation = 'Closest'
    material.node_tree.links.new(tex.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
    nodes.get('Principled BSDF').inputs['Roughness'].default_value=.85
    body.data.materials.clear()
    body.data.materials.append(material)
    scene = bpy.context.scene
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=600
    scene.render.resolution_y=600
    scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('World')
    scene.world.color=(.22,.28,.18)
    scene.view_settings.view_transform='Standard'
    for loc, energy in [((2,-3,5),500),((-3,1,3),300)]:
        bpy.ops.object.light_add(type='AREA',location=loc)
        bpy.context.object.data.energy=energy
        bpy.context.object.data.size=4
    target=Vector((0,0,.6))
    bpy.ops.object.camera_add(location=(3,-4,3.5))
    camera=bpy.context.object
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO'
    camera.data.ortho_scale=3.0
    scene.camera=camera
    for name, frame in [('Rest',0),('Idle',12),('Run',6),('Crouch',20),('Pounce',9),('Land',4),
                        ('Dash',6),('Rake',9),('RakeRight',20),('Roar',14),('Hit',3),('Death',30)]:
        clipname='Rake' if name=='RakeRight' else name
        arm.animation_data.action=next((a for a in bpy.data.actions if a.name.split('|')[-1]==clipname),None)
        scene.frame_set(frame+1)
        scene.render.filepath=os.path.join(directory,name+'.png')
        bpy.ops.render.render(write_still=True)


BASE_CLIPS = cat.clips


def main():
    assert os.environ.get('PYTHONHASHSEED')=='0', 'Start Blender with PYTHONHASHSEED=0'
    assert os.path.isfile(SOURCE), 'Supply Tools/Blender/Sources/Apex_trellis.glb'
    args=argparse.ArgumentParser()
    args.add_argument('--preview-dir')
    options=args.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cat.SOURCE=SOURCE
    cat.TRIANGLES=2100
    cat.VOXEL=.012
    cat.RUMP_Y=.19
    cat.LEG_Y=(-.32,.10)
    cat.TRIANGLE_BUDGET=(1200,2200)
    atlas=build_atlas(TEX)
    high=cat.load()
    cat.SCALE[0]=shape(high)
    body=cat.decimate(high)
    body.name='Apex_Body'
    cat.paint(body,classify(body,source_colours(high,body)))
    marks,bones=skeleton(body)
    # Attachment only; every deforming vertex remains on the Panther's original bones.
    cat.weigh(body,{name:bone for name,bone in bones.items() if name!='Head'},marks)
    tri_count=sum(len(p.vertices)-2 for p in body.data.polygons)
    assert 1200<=tri_count<=2200, tri_count
    for vertex in body.data.vertices:
        weights=[g.weight for g in vertex.groups if g.weight>0]
        assert 1<=len(weights)<=4 and abs(sum(weights)-1)<.001
    cat.dark(body,atlas)
    print('BODY',tri_count,'triangles',len(body.data.vertices),'vertices',cat.proportions(body.data),flush=True)
    bpy.data.objects.remove(high,do_unlink=True)
    arm=cat.build_armature(bones)
    arm.data.bones['Head'].use_deform=False
    cat.bind(body,arm)
    author_clips(arm,body,bones)
    export(arm,body)
    if options.preview_dir:
        preview(os.path.abspath(options.preview_dir))


if __name__=='__main__':
    main()
