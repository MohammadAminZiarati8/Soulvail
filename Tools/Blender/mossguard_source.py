"""Import the approved detailed Mossguard as a static Unity source preview, not a runtime enemy.

PYTHONHASHSEED=0 blender -b --factory-startup -P Tools/Blender/mossguard_source.py
Reads Sources/Mossguard_src.glb; writes MossguardSource.fbx and T_MossguardSource_Albedo.png.
Materials are remapped to the project's URP source-preview materials through the Unity importer.
No rig, atlas changes, prefab, gameplay registration or destructive source edits.
"""
import os
import bpy
from mathutils import Vector, Matrix

assert os.environ.get('PYTHONHASHSEED') == '0', 'Set PYTHONHASHSEED=0'
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ART = os.path.join(ROOT, 'Assets', '_Project', 'Art', 'Enemies')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT, 'Tools', 'Blender', 'Sources', 'Mossguard_src.glb'))
objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(objects) == 7, 'Body and six eye details must be present'
corners = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
floor = min(v.z for v in corners)
height = max(v.z for v in corners) - floor
fit = Matrix.Scale(1.8 / height, 4) @ Matrix.Translation(Vector((0, 0, -floor)))
body = max(objects, key=lambda o: len(o.data.polygons))
body.name = 'MossguardSource_Body'
for obj in objects:
    transform = fit @ obj.matrix_world
    obj.data = obj.data.copy()
    obj.data.transform(transform)
    obj.parent = None
    obj.matrix_world = Matrix.Identity(4)
    for material in obj.data.materials:
        material.use_backface_culling = False
        if obj is body:
            material.name = 'MossguardSource_Body'
        elif 'Iris' in material.name:
            material.name = 'MossguardSource_Iris'
        elif 'Pupil' in material.name:
            material.name = 'MossguardSource_Pupil'
        elif 'Highlight' in material.name:
            material.name = 'MossguardSource_Highlight'
    obj.data.calc_loop_triangles()
image = next(n.image for n in body.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE')
image.filepath_raw = os.path.join(ART, 'Textures', 'T_MossguardSource_Albedo.png')
image.file_format = 'PNG'
image.save()
triangles = sum(len(o.data.loop_triangles) for o in objects)
assert triangles == 183160, triangles
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.export_scene.fbx(filepath=os.path.join(ART, 'MossguardSource.fbx'),
    use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
    bake_anim=False, add_leaf_bones=False, path_mode='RELATIVE', mesh_smooth_type='FACE')
print('MOSS_SOURCE_EXPORTED triangles=' + str(triangles), flush=True)
