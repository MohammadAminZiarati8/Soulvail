"""Repair TRELLIS/image-to-3D open scans in Blender 4.1. See README.md beside this tool.

PYTHONHASHSEED=0 blender --background --factory-startup --python Tools/Blender/repair_scan.py
    -- <raw.glb> <closed.glb> [--preview-dir <directory>] [--blend <review.blend>]

Source stays read-only. Largest mesh is rebuilt as a thin closed shell, voxel reconstructed,
validated and recolored by nearest-source UV island. Other meshes (e.g. eyes) are retained.
Use this source repair BEFORE rig fitting, atlas repainting or runtime polygon reduction.
The optional Blender review opens from behind with material preview and X-ray disabled.
"""
import bpy, bmesh, json, os, math, time, argparse, sys
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform,closest_point_on_tri


parser=argparse.ArgumentParser(description='Repair an image-to-3D scan without losing its albedo.')
parser.add_argument('source')
parser.add_argument('output')
parser.add_argument('--target-triangles',type=int,default=180000)
parser.add_argument('--texture-size',type=int,default=2048)
parser.add_argument('--preview-dir')
parser.add_argument('--blend')
parser.add_argument('--report')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
assert os.environ.get('PYTHONHASHSEED')=='0','Set PYTHONHASHSEED=0 before Blender starts'
SRC=os.path.abspath(args.source)
DEST=os.path.abspath(args.output)
assert SRC!=DEST,'Keep the raw scan; output must be a different file'
assert args.target_triangles>=1000 and args.texture_size>=256
os.makedirs(os.path.dirname(DEST),exist_ok=True)
OUT=os.path.abspath(args.preview_dir) if args.preview_dir else os.path.dirname(DEST)
if args.preview_dir:os.makedirs(OUT,exist_ok=True)
def log(s): print(s,flush=True)
def topology(me,weld=True):
    bm=bmesh.new();bm.from_mesh(me)
    if weld:bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
    stats={'vertices':len(bm.verts),'faces':len(bm.faces),
        'boundary_edges':sum(e.is_boundary for e in bm.edges),
        'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges)}
    bm.free();return stats
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)

for part in [o for o in bpy.context.scene.objects if o.type=='MESH']:
    world=part.matrix_world.copy()
    part.data=part.data.copy()
    part.data.transform(world)
    part.parent=None
    part.matrix_world=Matrix.Identity(4)
source=max((o for o in bpy.context.scene.objects if o.type=='MESH'),key=lambda o:len(o.data.polygons))
bpy.context.view_layer.update()
scale=max(source.dimensions)

source.name='Original_ForTransfer'
sm=source.data
assert len(sm.materials)==1 and sm.uv_layers.active,'Largest mesh needs one textured material and UVs'
sm.calc_loop_triangles()
before=topology(sm)
log('BEFORE='+json.dumps(before))
for mat in sm.materials:
    p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    log('MATERIAL='+json.dumps({'name':mat.name,'backface_culling':mat.use_backface_culling,
        'blend_method':mat.blend_method,'alpha':p.inputs['Alpha'].default_value,
        'transmission':p.inputs['Transmission Weight'].default_value}))
albedo=next(n.image for n in sm.materials[0].node_tree.nodes if n.type=='TEX_IMAGE' and n.image.colorspace_settings.name=='sRGB')
verts=[v.co.copy() for v in sm.vertices]
triangles=[tuple(t.vertices) for t in sm.loop_triangles]
source_uv=sm.uv_layers.active.data
uv_triangles=[tuple(Vector((*source_uv[i].uv,0)) for i in t.loops) for t in sm.loop_triangles]
bvh=BVHTree.FromPolygons(verts,triangles,all_triangles=True)
body=source.copy();body.data=sm.copy();body.name='RepairedBody'
bpy.context.scene.collection.objects.link(body)
bpy.ops.object.select_all(action='DESELECT');body.select_set(True)
bpy.context.view_layer.objects.active=body
# Clear split normals before rebuilding a genuine closed surface.
if body.data.has_custom_normals:
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(body.data);bm.free()
# Give open sheets a real closed thickness before voxelization. Voxelizing an open
# fragmented scan directly keeps only its few already-enclosed fragments.
solid=body.modifiers.new('CloseOpenSheets','SOLIDIFY')
solid.solidify_mode='EXTRUDE'
solid.thickness=0.006*scale;solid.offset=0
solid.use_even_offset=False
bpy.ops.object.modifier_apply(modifier=solid.name)
bpy.context.view_layer.update()
log('SHELL_SIZE='+str(tuple(body.dimensions)))
assert all(body.dimensions[k] <= source.dimensions[k] * 1.05 for k in range(3)), 'Shell distortion'
mod=body.modifiers.new('ClosedSurface','REMESH')
mod.mode='VOXEL';mod.voxel_size=0.002*scale
mod.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=mod.name)
log('REMESH='+json.dumps(topology(body.data)))
log('REMESH_RAW='+json.dumps(topology(body.data,False)))
body.data.calc_loop_triangles()
if len(body.data.loop_triangles)>args.target_triangles:
    dec=body.modifiers.new('SourceDensity','DECIMATE')
    dec.ratio=args.target_triangles/len(body.data.loop_triangles)
    bpy.ops.object.modifier_apply(modifier=dec.name)
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(body.data);bm.free()
after=topology(body.data,False)
log('AFTER='+json.dumps(after))
assert after['boundary_edges']==0 and after['nonmanifold_edges']==0,after
source_area=sum(p.area for p in sm.polygons)
repaired_area=sum(p.area for p in body.data.polygons)
log('SURFACE_AREAS='+json.dumps({'source':source_area,'repaired':repaired_area}))
assert repaired_area>=source_area*0.6,'Reconstruction lost the body surface'
for f in body.data.polygons:f.use_smooth=True
# Rebuild a clean data block. Modifier output inherited invalid corner data from
# the fragmented scan; retaining those layers can omit the body during export.
old_mesh=body.data
me=bpy.data.meshes.new('ClosedGeometry')
me.from_pydata([tuple(v.co) for v in old_mesh.vertices],[],[tuple(f.vertices) for f in old_mesh.polygons])
me.validate(clean_customdata=True);me.update(calc_edges=True)
body.data=me
bm=bmesh.new();bm.from_mesh(me)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-7)
specks=[f for f in bm.faces if f.calc_area()<1e-6 and all(e.is_boundary for e in f.edges)]
if specks:bmesh.ops.delete(bm,geom=specks,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=32)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(me);bm.free();me.update(calc_edges=True)
me.validate(clean_customdata=True)
after=topology(me,False)
log('CLEAN_RECONSTRUCTED='+json.dumps(after))
assert after['boundary_edges']==0 and after['nonmanifold_edges']==0,after
for f in me.polygons:f.use_smooth=True
while me.uv_layers:me.uv_layers.remove(me.uv_layers[0])
me.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=0.003)
bpy.ops.object.mode_set(mode='OBJECT')
me=body.data
me.update()
# Transfer source UV coordinates by nearest surface, including the newly closed gaps.
uv_attr=me.attributes.new('SourceUV','FLOAT_VECTOR','CORNER')
# Every new triangle must sample one source UV island. Interpolating independently
# selected per-vertex islands would interpolate across unrelated atlas locations.
for face in me.polygons:
    near,normal,index,distance=bvh.find_nearest(face.center)
    assert index is not None
    a,b,c=triangles[index]
    ua,ub,uc=uv_triangles[index]
    for li in face.loop_indices:
        point=me.vertices[me.loops[li].vertex_index].co
        nearest=closest_point_on_tri(point,verts[a],verts[b],verts[c])
        uv_attr.data[li].vector=barycentric_transform(nearest,verts[a],verts[b],verts[c],ua,ub,uc)
log('Transferred source UV samples')
mat=bpy.data.materials.new('M_Repaired_Opaque');mat.use_nodes=True
mat.blend_method='OPAQUE';mat.use_backface_culling=False
nodes=mat.node_tree.nodes;nodes.clear();links=mat.node_tree.links
out=nodes.new('ShaderNodeOutputMaterial')
emit=nodes.new('ShaderNodeEmission')
tex=nodes.new('ShaderNodeTexImage');tex.image=albedo
attr=nodes.new('ShaderNodeAttribute');attr.attribute_name='SourceUV'
links.new(attr.outputs['Vector'],tex.inputs['Vector'])
links.new(tex.outputs['Color'],emit.inputs['Color'])
links.new(emit.outputs['Emission'],out.inputs['Surface'])
image=bpy.data.images.new('Repaired_Albedo',args.texture_size,args.texture_size,alpha=False)
image.generated_color=(0.2,0.25,0.15,1)
target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
me.materials.clear();me.materials.append(mat)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU'
scene.cycles.samples=4;scene.render.bake.use_selected_to_active=False;scene.render.bake.margin=12
source.hide_render=True
bpy.ops.object.bake(type='EMIT')
image.pack()
# Replace the temporary transfer shader with an explicitly opaque, non-transmissive one.
nodes.clear()
out=nodes.new('ShaderNodeOutputMaterial');bsdf=nodes.new('ShaderNodeBsdfPrincipled')
tex=nodes.new('ShaderNodeTexImage');tex.image=image
links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
bsdf.inputs['Alpha'].default_value=1
bsdf.inputs['Transmission Weight'].default_value=0
bsdf.inputs['Metallic'].default_value=0
bsdf.inputs['Roughness'].default_value=0.8
links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
bpy.data.objects.remove(source,do_unlink=True)
if me.attributes.get('SourceUV'):me.attributes.remove(me.attributes['SourceUV'])
for m in bpy.data.materials:
    m.use_backface_culling=False;m.blend_method='OPAQUE'
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type=='BSDF_PRINCIPLED':
                for key in ('Alpha','Transmission Weight'):
                    for link in list(n.inputs[key].links):m.node_tree.links.remove(link)
                n.inputs['Alpha'].default_value=1;n.inputs['Transmission Weight'].default_value=0
meshes=[o for o in scene.objects if o.type=='MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=body
glb=DEST
bpy.ops.export_scene.gltf(filepath=glb,export_format='GLB',use_selection=True,export_normals=True)
# Inspect serialized GLB for alpha/transmission settings, not just Blender values.
import struct
with open(glb,'rb') as f:
    data=f.read()
length=struct.unpack_from('<I',data,12)[0]
gltf=json.loads(data[20:20+length])
assert any(n.get('name')==body.name and 'mesh' in n for n in gltf['nodes']), 'Body missing from GLB'
for m in gltf.get('materials',[]):
    assert m.get('alphaMode','OPAQUE')=='OPAQUE'
    assert m.get('pbrMetallicRoughness',{}).get('baseColorFactor',[1,1,1,1])[3]==1
    assert m.get('extensions',{}).get('KHR_materials_transmission',{}).get('transmissionFactor',0)==0
    assert m.get('doubleSided') is True
me.calc_loop_triangles()
report={'before':before,'after':after,'triangles':len(me.loop_triangles),
    'material_alpha':'OPAQUE','transmission':0,'double_sided':True,
    'method':'Voxel reconstruction, nearest-surface albedo transfer, opaque material',
    'limitations':['Unrigged source sculpt','Unseen surfaces are generator interpretations; check multiple views']}
if args.report:
    with open(args.report,'w') as f:json.dump(report,f,indent=2)
log('REPAIR_VERIFIED='+json.dumps(report))
if not args.preview_dir and not args.blend:
    sys.exit(0)
# Studio scene and explicitly non-transparent material-preview viewport.
scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.world=bpy.data.worlds.new('RepairStudio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(0.42,0.42,0.42,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.7
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
corners=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
lo=Vector(tuple(min(v[k] for v in corners) for k in range(3)))
hi=Vector(tuple(max(v[k] for v in corners) for k in range(3)))
center=(lo+hi)/2;size=hi-lo
for name,pos,power in [('Key',(-1.8,-2.5,2.8),180),('Fill',(2,-1.2,0.8),70),('Back',(0.5,2,2),180)]:
    ld=bpy.data.lights.new(name,'AREA');ld.energy=power*max(size)**2;ld.shape='DISK';ld.size=2*max(size)
    light=bpy.data.objects.new(name,ld);scene.collection.objects.link(light)
    light.location=center+Vector(pos)*max(size)
    light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
cd=bpy.data.cameras.new('ReviewCamera');cam=bpy.data.objects.new('ReviewCamera',cd)
scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO'
for label,direction in [('front',(0,-1,0)),('back',(0,1,0)),('side',(1,0,0)),('threequarter',(1,-2,0.25))]:
    cam.location=center+Vector(direction).normalized()*max(size)*4
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    right=cam.rotation_euler.to_quaternion()@Vector((1,0,0));up=cam.rotation_euler.to_quaternion()@Vector((0,1,0))
    w=max(v.dot(right) for v in corners)-min(v.dot(right) for v in corners)
    h=max(v.dot(up) for v in corners)-min(v.dot(up) for v in corners)
    cd.ortho_scale=max(h,w/(900/1100))*1.08
    scene.render.filepath=os.path.join(OUT,'Repaired-'+label+'.png')
    if args.preview_dir:bpy.ops.render.render(write_still=True)
    if label=='back' and args.blend:
        for screen in bpy.data.screens:
            for area in screen.areas:
                if area.type=='VIEW_3D':
                    area.spaces.active.shading.type='MATERIAL'
                    area.spaces.active.shading.show_xray=False
                    area.spaces.active.region_3d.view_perspective='CAMERA'
        bpy.ops.file.pack_all()
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.blend))
log('DONE')
