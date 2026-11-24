"""Build SS-19e with Blender 4.1 from the supplied sculpt.

PYTHONHASHSEED=0 blender -b --factory-startup -t 8 --python-exit-code 1
    -P Tools/Blender/carrion_matriarch.py
"""

from hashlib import sha256
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory

import bpy
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from vulture import ART, TOOLS, WINGSPAN, bind, build_armature, fit_skeleton, mesh_bounds

SOURCE = TOOLS / "Sources" / "CarrionMatriarch_src.glb"
SOURCE_HASH = "e3269ca629e260427fce4c5e2d00d4b9d413308834af6e134a0bcb8c01c59c4a"


def repair_sculpt(destination):
    if sha256(SOURCE.read_bytes()).hexdigest() != SOURCE_HASH:
        raise ValueError(f"Unexpected source sculpt: {SOURCE}")

    subprocess.run([
        bpy.app.binary_path, "-b", "--factory-startup", "-t", "8", "--python-exit-code", "1",
        "-P", str(TOOLS / "repair_scan.py"), "--", str(SOURCE), str(destination),
        "--target-triangles", "10000", "--texture-size", "2048", "--point-shell",
    ], check=True)


def load_sculpt(repaired):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(repaired))
    body, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    low, high = mesh_bounds([body])
    # Align the sculpt talons with the shared grounded clip's contact height.
    scale = WINGSPAN / (high.x - low.x)
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z - .11 / scale))
    body.data.transform(Matrix.Scale(scale, 4)
                        @ Matrix.Translation(-offset) @ body.matrix_world)
    body.matrix_world = Matrix.Identity(4)

    material, = body.data.materials
    principled, = (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    albedo, = principled.inputs["Base Color"].links
    image = albedo.from_node.image
    (ART / "T_CarrionMatriarch_Albedo.png").write_bytes(image.packed_file.data)
    material.name = "CarrionMatriarch"

    body.name = body.data.name = "Vulture"
    return body


def smooth(start, end, value):
    t = min(1.0, max(0.0, (value - start) / (end - start)))
    return t * t * (3 - 2 * t)


def skin(body, bones):
    groups = {name: body.vertex_groups.new(name=name) for name in bones if name != "root"}
    for vertex in body.data.vertices:
        x, y, z = vertex.co
        side = "l" if x >= 0 else "r"
        # Keep the ruff on the torso and the hanging primaries on their wing,
        # even where their tips reach the same height as the legs.
        wing = smooth(.28, .49, abs(x))
        wrist = smooth(.64, .94, abs(x))
        leg = (1 - smooth(.22, .43, z)) * (1 - smooth(.26, .46, y))
        tail = smooth(.24, .52, y) * (1 - smooth(.52, .93, z))
        neck = smooth(.79, 1.16, z) * (1 - smooth(-.16, .18, y))
        head = 1 - smooth(-.58, -.34, y)

        trunk = (1 - wing) * (1 - leg) * (1 - tail)
        weights = {
            f"wing.{side}.1": wing * (1 - wrist),
            f"wing.{side}.2": wing * wrist,
            f"leg.{side}": (1 - wing) * leg,
            "tail": (1 - wing) * (1 - leg) * tail,
            "body": trunk * (1 - neck),
            "neck": trunk * neck * (1 - head),
            "head": trunk * neck * head,
        }
        strongest = sorted(weights.items(), key=lambda pair: -pair[1])[:4]
        total = sum(weight for _, weight in strongest)
        for name, weight in strongest:
            if weight > 0:
                groups[name].add([vertex.index], weight / total, "REPLACE")


def main():
    with TemporaryDirectory(prefix="matriarch-") as temporary:
        repaired = Path(temporary) / "repaired.glb"
        repair_sculpt(repaired)
        body = load_sculpt(repaired)
    bones = fit_skeleton()
    skin(body, bones)
    arm = build_armature(bones)
    arm.name = arm.data.name = "Rig_Hawk"
    bind(body, arm)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=str(ART / "CarrionMatriarch.fbx"), use_selection=True,
        object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False,
        armature_nodetype="NULL", bake_anim=False, path_mode="STRIP",
    )
    body.data.calc_loop_triangles()
    print(f"Carrion Matriarch: {len(body.data.loop_triangles)} triangles")


if __name__ == "__main__":
    main()
