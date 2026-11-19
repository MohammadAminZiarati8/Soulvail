"""Bake Shardback on the shipped Glassback rig using Blender 4.1.

Set PYTHONHASHSEED=0 and run from the repository root:
    blender -b --factory-startup --python-exit-code 1 -P Tools/Blender/shardback.py
"""

from math import cos, radians, tau
from pathlib import Path
import sys
from tempfile import TemporaryDirectory

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import glassback
from beetle import step
from enemy_atlas import uv_for
from frog import ik2
from scorpion import aimed, turned_about


def refit(point):
    """Broaden the chassis and splay the forelegs without changing the ground plane."""
    x, y, z = point
    fore = min(1, max(0, (-y - .27) / .32)) * min(1, abs(x) / .4)
    return Vector((x * (1.95 + .34 * fore), y * (1.65 + .18 * fore), z * 1.58))


def thicken_foreleg(vertex, body, legs):
    point = vertex.co
    weights = {body.vertex_groups[group.group].name: group.weight for group in vertex.groups}
    for _, index, upper, lower, hip, knee, foot in legs:
        if index != 1:
            continue
        for name, start, end in ((upper, hip, knee), (lower, knee, foot)):
            amount = weights.get(name, 0)
            if amount == 0:
                continue
            axis = end - start
            along = min(1, max(0, (vertex.co - start).dot(axis) / axis.length_squared))
            point = point + (vertex.co - start - along * axis) * (.56 * amount)
    return Vector((point.x, point.y, max(vertex.co.z * .4, point.z)))


def refit_rig(arm, legs):
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bone in arm.data.edit_bones:
        if bone.name != "Root":
            bone.head = refit(bone.head)
            bone.tail = refit(bone.tail)
    bpy.ops.object.mode_set(mode="OBJECT")
    return [(side, index, upper, lower, refit(hip), refit(knee), refit(foot))
            for side, index, upper, lower, hip, knee, foot in legs]


def rebuild_underside(body, regions):
    """Remove the scan's old cases and close the exposed center with an umber abdomen."""
    mesh = bmesh.new()
    mesh.from_mesh(body.data)
    bmesh.ops.delete(mesh, geom=[v for v in mesh.verts if regions[v.index] in glassback.SHELLS],
                     context="VERTS")
    abdomen = bmesh.ops.create_uvsphere(mesh, u_segments=14, v_segments=6, radius=1)["verts"]
    weights = mesh.verts.layers.deform.verify()
    uv = mesh.loops.layers.uv.verify()
    for vertex in abdomen:
        x, y, z = vertex.co
        vertex.co = refit((x * .44, .17 + y * .56, .40 + z * .217))
        vertex[weights][body.vertex_groups["Abdomen"].index] = 1
    for face in {face for vertex in abdomen for face in vertex.link_faces}:
        face.smooth = False
        for loop in face.loops:
            loop[uv].uv = uv_for("glassback_umber", 1 - loop.vert.co.z)
    mesh.to_mesh(body.data)
    mesh.free()


def wing_case(arm, material, name, side):
    # Four longitudinal ridges per section: split, crown, shoulder and rolled rim.
    # Moving the crown and shoulder independently breaks up the broad highlights.
    sections = (
        ((.11, -.409, .563), (.22, -.435, .651), (.32, -.373, .591), (.35, -.335, .51)),
        ((.041, -.272, .635), (.21, -.278, .836), (.40, -.255, .71), (.48, -.233, .49)),
        ((.019, -.077, .649), (.25, -.094, .943), (.46, -.122, .768), (.54, -.098, .51)),
        ((.034, .151, .67), (.27, .184, .854), (.486, .109, .744), (.535, .13, .515)),
        ((.018, .393, .607), (.22, .366, .896), (.402, .426, .713), (.485, .40, .485)),
        ((.068, .592, .531), (.18, .555, .727), (.296, .608, .639), (.345, .554, .483)),
        ((.107, .7, .465), (.158, .713, .555), (.213, .718, .534), (.243, .66, .451)),
    )
    points = [refit((side * x, y, z)) for section in sections for x, y, z in section]
    faces, colours = [], []
    for station in range(len(sections) - 1):
        first = station * 4
        for band in range(4):
            a = first + band
            b = first + (band + 1) % 4
            if (station + band) % 2:
                faces.extend(((a, b, a + 4), (b, b + 4, a + 4)))
            else:
                faces.extend(((a, b, b + 4), (a, b + 4, a + 4)))
            # Smoky grey flank and a deep fracture on the inside of the ivory case.
            colours.extend((
                ("glassback_ivory", .64), ("vulture_ruff", .28),
                ("glassback_ivory", .11), ("vulture_skin", .12),
                ("vulture_skin", .83), ("vulture_ruff", .79),
                ("vulture_horn", .35), ("vulture_horn", .7),
            )[2 * band:2 * band + 2])
    faces.extend(((0, 1, 2, 3), tuple(range(len(points) - 4, len(points)))))
    colours.extend([("vulture_skin", .74)] * 2)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(points, [], faces)
    mesh.materials.append(material)
    uv = mesh.uv_layers.new(name="UVMap").data
    for face, (swatch, shade) in zip(mesh.polygons, colours):
        for index in face.loop_indices:
            uv[index].uv = uv_for(swatch, shade)

    closed = bmesh.new()
    closed.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(closed, faces=list(closed.faces))
    closed.to_mesh(mesh)
    closed.free()

    case = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(case)
    case.parent = arm
    skin = case.modifiers.new("Beetle skin", "ARMATURE")
    skin.object = arm
    case.vertex_groups.new(name=name).add(list(range(len(points))), 1, "REPLACE")
    return case


def pose(arm, legs, *, shift=(0, 0, 0), pitch=0, roll=0, nod=0, shell=0,
         gait=None, stride=.50, lift=.15, scrape=0, curl=0):
    rest = {bone.name: bone.bone.matrix_local for bone in arm.pose.bones}
    rotation = Quaternion((0, 1, 0), roll) @ Quaternion((1, 0, 0), pitch)
    body = Matrix.Translation(shift) @ turned_about(rest["Body"].translation, rotation)
    transforms = {"Root": rest["Root"], "Body": body @ rest["Body"], "Abdomen": body @ rest["Abdomen"]}
    transforms["Head"] = body @ turned_about(rest["Head"].translation, Quaternion((1, 0, 0), nod)) @ rest["Head"]
    for name, side in glassback.SHELLS.items():
        hinge = Quaternion((1, 0, -.72 * side), radians(shell))
        transforms[name] = body @ turned_about(rest[name].translation, hinge) @ rest[name]

    for side, index, upper, lower, hip, knee, foot in legs:
        target = foot.copy()
        pole = knee - hip
        if gait is not None:
            phase = (gait + .5 * ((index + (side < 0)) % 2)) % 1
            travel, height = step(phase, stride, lift)
            target.x -= .08 * side
            target.y += travel
            target.z += height
        if side == 1 and index == 1:
            target.y += scrape
            target.x -= .20 * abs(scrape)
        if curl:
            target = body @ (hip + (foot - hip) * (1 - .53 * curl) + Vector((0, 0, .14 * curl)))
            pole = body.to_3x3() @ pole
        start = body @ hip
        bend, end = ik2(start, target, (knee - hip).length, (foot - knee).length, pole)
        transforms[upper] = aimed(rest[upper], start, bend - start)
        transforms[lower] = aimed(rest[lower], bend, end - bend)

    for bone in arm.pose.bones:
        name = bone.name
        basis = rest[name].inverted()
        if bone.parent:
            parent = bone.parent.name
            basis = basis @ rest[parent] @ transforms[parent].inverted()
        bone.rotation_mode = "QUATERNION"
        bone.matrix_basis = basis @ transforms[name]


def clips():
    fan = dict(shift=(0, .05, .103), pitch=-.28, nod=-.17)
    charge = dict(shift=(0, -.111, -.095), pitch=.29, nod=.16)
    brace = dict(shift=(0, .026, -.122), pitch=.05, nod=.14)
    open_case = dict(shift=(0, .027, -.075), pitch=.08, shell=54)
    return {
        "Idle": {0: {}, 12: dict(shift=(0, 0, .016), nod=.027),
                 31: dict(shift=(0, 0, .004), nod=-.029), 48: {}},
        "Walk": {f: dict(gait=f / 24, shift=(0, 0, -.045), roll=.028 * cos(f * tau / 24))
                 for f in range(25)},
        "FanWindup": {0: {}, 8: dict(shift=(0, .05, -.045), nod=.08),
                      22: fan, 30: fan},
        "FanRelease": {0: fan, 3: dict(shift=(0, -.08, -.046), pitch=.14, nod=.22),
                       7: dict(shift=(0, -.02, -.054), pitch=.085, nod=.04), 10: {}},
        "ChargeWindup": {0: {}, 10: charge, 16: dict(**charge, scrape=-.18),
                         23: dict(**charge, scrape=.21, roll=.055),
                         30: dict(**charge, scrape=.10), 36: charge},
        "ChargeTravel": {f: dict(gait=f / 16, stride=.76, lift=.21,
                                 shift=(0, -.11, -.109 + .034 * cos(f * tau / 8)),
                                 pitch=.24, nod=.12, roll=.051 * cos(f * tau / 16))
                         for f in range(17)},
        "BurstWindup": {0: {}, 13: brace, 23: dict(**brace, shell=3),
                        29: dict(**brace, shell=8, roll=-.035), 36: dict(**brace, shell=10)},
        "BurstRelease": {0: dict(**brace, shell=10),
                         3: dict(shift=(0, 0, .063), pitch=-.13, shell=40),
                         6: dict(shift=(0, 0, -.036), pitch=.06, shell=29),
                         10: dict(shift=(0, 0, -.06), pitch=.08, shell=20)},
        "Recovery": {0: {}, 6: dict(shift=(0, 0, -.06), pitch=.08, shell=36),
                     11: open_case, 26: dict(**open_case, nod=.04, roll=.034),
                     43: open_case, 49: dict(**open_case, nod=.02), 60: {}},
        "Death": {0: {}, 8: dict(shift=(0, 0, .16), roll=.7, shell=25, curl=.22),
                  16: dict(shift=(.18, 0, .53), roll=1.9, shell=15, curl=.68),
                  29: dict(shift=(.20, 0, .40), roll=3.12, curl=.88),
                  36: dict(shift=(.20, 0, .37), roll=3.14, curl=1)},
    }


def animate(arm, legs):
    arm.animation_data_create()
    for name, keys in clips().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        previous = {}
        for frame, parameters in keys.items():
            pose(arm, legs, **parameters)
            for bone in arm.pose.bones:
                if bone.name == "Root":
                    continue
                rotation = bone.rotation_quaternion
                if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
                    rotation.negate()
                previous[bone.name] = rotation.copy()
                bone.keyframe_insert("location", frame=frame, group=bone.name)
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
        action.use_frame_range = True
        action.frame_range = (0, max(keys))
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def main():
    with TemporaryDirectory() as directory:
        closed = Path(directory) / "glassback.glb"
        glassback.repair(closed)
        body = glassback.load_body(closed)
        regions = glassback.prepare_runtime_mesh(body)

    arm, legs = glassback.build_rig(body)
    glassback.bind(body, arm, regions)
    for vertex, region in zip(body.data.vertices, regions):
        if region == "Body":
            vertex.co = refit(thicken_foreleg(vertex, body, legs))
    rebuild_underside(body, regions)
    body.name = body.data.name = "Shardback_Body"
    legs = refit_rig(arm, legs)
    cases = [wing_case(arm, body.data.materials[0], name, side) for name, side in glassback.SHELLS.items()]
    animate(arm, legs)
    bpy.context.scene.render.fps = 30

    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    triangles = 0
    for component in (body, *cases):
        component.select_set(True)
        component.data.calc_loop_triangles()
        triangles += len(component.data.loop_triangles)
    assert triangles <= 3000, triangles

    destination = Path(__file__).resolve().parents[2] / "Assets/_Project/Art/Enemies/Shardback.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(destination), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        use_armature_deform_only=False, armature_nodetype="NULL",
        bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, path_mode="STRIP",
    )
    print(f"{destination.name}: {triangles} triangles, {len(bpy.data.actions)} clips")


if __name__ == "__main__":
    main()
