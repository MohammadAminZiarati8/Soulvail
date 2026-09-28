"""The Rootling: the Jungle's Husk, made in Blender and skinned onto KayKit's Rig_Medium (M7-05f).

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed:

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/rootling.py [-- --pose-sheet <out.png>]

The seed is what makes a re-run reproduce the FBX: Blender's exporter names every node by Python's
hash() of a key, which is randomised per process, so without it a re-run differs in a thousand
bytes of object ids while every vertex, weight and name is the same. With it, only the header's
timestamp moves. The script refuses to run without it.

It writes, overwriting what it wrote last time, so both files are an output of this script and are
changed here, never by hand:

- Assets/_Project/Art/Enemies/Rootling.fbx               one skinned mesh on Rig_Medium, unchanged
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas; its first two
                                                          columns are the Rootling's, the rest free

The rig is read, never rebuilt: Skeleton_Minion.fbx is a KayKit enemy on Rig_Medium, and its
armature is kept exactly as it loads while its meshes are thrown away. Unity binds a Generic clip
by transform path, so equal bone names and rest positions are what let every Rig_Medium clip play
on the Rootling with no retargeting (ThirdParty/KayKit/VERSIONS.md).

The weights are written, not computed by bone heat: each part is built for a few bones, and a
vertex is weighted to the nearest of them, blended where a part crosses a joint.

--pose-sheet renders the exported model, reloaded from its FBX, in the clips M7-05h chooses from,
three frames each from the front and three-quarters, and a black silhouette from the game camera.
It is the deformation check, written outside the repository.

Every colour obeys GD 16.4: bone, fungus-white and dry bark, pale enough to read on the Jungle's
green floor, and nothing near the player's cyan, danger's red-orange, Veilrot's violet or gold.
"""

import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Enemies")
TEX = os.path.join(OUT, "Textures")
RIG_SOURCE = os.path.join(ROOT, "Assets", "ThirdParty", "KayKit", "Skeletons", "Characters", "Skeleton_Minion.fbx")
CLIPS = os.path.join(ROOT, "Assets", "ThirdParty", "KayKit", "Animations", "Rig_Medium")

TRIANGLE_BUDGET = (400, 1200)  # GD 17.1, the crowd's

# ---------------------------------------------------------------------------------------------
# The shared enemy atlas: 512 px square in 64 x 128 px strips, each a vertical gradient from light
# (top) to dark, M7-05b's layout at half the size. Columns 0-1 are the Rootling's; 2-7 are free for
# the Jungle's other bodies.
# ---------------------------------------------------------------------------------------------

ATLAS = 512
COL_W, ROW_H = 64, 128

SWATCHES = {
    "bone": (0, 0, "E2DBC8", "A89F8A"),
    "fungus": (0, 1, "ECE7D8", "B8B09D"),
    "bark": (0, 2, "BCAE96", "7E705D"),
    "lichen": (0, 3, "D2D2C0", "9A9B88"),
    "bark_dark": (1, 0, "A39380", "675A4B"),
    "socket": (1, 1, "3C352E", "221D19"),
}
SWATCH_NAMES = list(SWATCHES)

# GD 16.4's reserved colours, which no pixel of the atlas may come near.
RESERVED = ("22D3EE", "FF4A1F", "A855F7", "FBBF24")


def rgb(hex_code):
    return np.array([int(hex_code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def uv_for(swatch, t):
    """Where on the atlas a surface of `swatch` samples, `t` from 0 (the light top) to 1."""
    col, row = SWATCHES[swatch][0], SWATCHES[swatch][1]
    t = min(1.0, max(0.0, t))
    u = (col * COL_W + COL_W * 0.5) / ATLAS
    y = row * ROW_H + 8 + t * (ROW_H - 16)  # a margin, so mip levels bleed less
    return u, 1.0 - y / ATLAS


def save_png(pixels, path):
    """`pixels` is H x W x 3 in 0..1, row 0 at the top."""
    h, w, _ = pixels.shape
    image = bpy.data.images.new("out", w, h, alpha=False)
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., :3] = np.clip(pixels, 0.0, 1.0)
    image.pixels.foreach_set(np.flipud(rgba).ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def build_atlas():
    img = np.zeros((ATLAS, ATLAS, 3), np.float32)
    ramp = np.linspace(0.0, 1.0, ROW_H, dtype=np.float32)[:, None]
    unused = rgb("9A9A9A") * (1 - ramp) + rgb("4A4A4A") * ramp
    for row in range(ATLAS // ROW_H):
        img[row * ROW_H:(row + 1) * ROW_H, :, :] = unused[:, None, :]
    for col, row, top, bottom in SWATCHES.values():
        strip = rgb(top) * (1 - ramp) + rgb(bottom) * ramp
        img[row * ROW_H:(row + 1) * ROW_H, col * COL_W:(col + 1) * COL_W, :] = strip[:, None, :]
    flat = img.reshape(-1, 3)
    for hex_code in RESERVED:
        nearest = np.abs(flat - rgb(hex_code)).max(axis=1).min()
        assert nearest > 0.12, f"the atlas comes within {nearest:.3f} of #{hex_code} (GD 16.4)"
    save_png(img, os.path.join(TEX, "T_Enemy_Albedo.png"))
    return img


# ---------------------------------------------------------------------------------------------
# The rig. Blender is Z-up and the Rootling faces -Y; the armature's own transform turns its
# Y-up data into that frame, and the mesh is written back into the armature's space at the end,
# which is how KayKit's own meshes sit under it.
# ---------------------------------------------------------------------------------------------

def load_rig():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    bpy.ops.import_scene.fbx(filepath=RIG_SOURCE)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for obj in list(bpy.data.objects):
        if obj.type != "ARMATURE":
            bpy.data.objects.remove(obj)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)
    for material in list(bpy.data.materials):
        bpy.data.materials.remove(material)
    assert arm.name == "Rig_Medium" and len(arm.data.bones) == 23, (arm.name, len(arm.data.bones))
    return arm


class Rig:
    """Bone heads and tails in the Rootling's frame, and the weighting that reads them."""

    def __init__(self, arm):
        self.arm = arm
        self.names = [b.name for b in arm.data.bones]
        self.head = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
        self.tail = {b.name: arm.matrix_world @ b.tail_local for b in arm.data.bones}

    def weights(self, p, bones):
        """The nearest two of `bones` to `p`, inverse-distance blended: rigid in a bone's middle,
        shared across a joint."""
        if len(bones) == 1:
            return {bones[0]: 1.0}
        scored = []
        for name in bones:
            a, b = self.head[name], self.tail[name]
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            d = (a + ab * t - p).length
            scored.append((1.0 / (d + 0.03) ** 4, name))
        scored.sort(reverse=True)
        top = scored[:2]
        total = sum(w for w, _ in top)
        return {name: w / total for w, name in top}


# ---------------------------------------------------------------------------------------------
# The mesh
# ---------------------------------------------------------------------------------------------

class Builder:
    def __init__(self, rig, seed):
        self.rig = rig
        self.rng = random.Random(seed)
        self.bm = bmesh.new()
        self.shade = self.bm.verts.layers.float.new("shade")
        self.swatch = self.bm.faces.layers.int.new("swatch")
        self.deform = self.bm.verts.layers.deform.verify()
        self.index = {name: i for i, name in enumerate(rig.names)}

    def vert(self, co, shade, bones):
        v = self.bm.verts.new(co)
        v[self.shade] = shade
        for name, w in self.rig.weights(Vector(co), bones).items():
            v[self.deform][self.index[name]] = w
        return v

    def face(self, verts, swatch):
        f = self.bm.faces.new(verts)
        f[self.swatch] = SWATCH_NAMES.index(swatch)
        return f

    def closed(self, faces):
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)

    def jitter(self, amount):
        return Vector((self.rng.uniform(-amount, amount), self.rng.uniform(-amount, amount), self.rng.uniform(-amount, amount)))

    # -- primitives ---------------------------------------------------------------------------

    def ellipsoid(self, center, radii, segments, rings, swatch, bones, roughness=0.0, shade=(0.05, 0.9), squash=None):
        """A faceted lump, pole to pole along Z. `squash(v)` may reshape each vertex."""
        verts, faces = [], []
        top = self.vert(center + Vector((0, 0, radii[2])), shade[0], bones)
        bottom = self.vert(center - Vector((0, 0, radii[2])), shade[1], bones)
        for r in range(1, rings):
            phi = math.pi * r / rings
            ring = []
            for s in range(segments):
                theta = 2 * math.pi * s / segments + (math.pi / segments if r % 2 else 0.0)
                d = Vector((math.sin(phi) * math.cos(theta) * radii[0], math.sin(phi) * math.sin(theta) * radii[1], math.cos(phi) * radii[2]))
                co = center + d + self.jitter(roughness * max(radii))
                if squash:
                    co = squash(co)
                t = shade[0] + (shade[1] - shade[0]) * (r / rings)
                ring.append(self.vert(co, t, bones))
            verts.append(ring)
        for s in range(segments):
            faces.append(self.face([top, verts[0][s], verts[0][(s + 1) % segments]], swatch))
            faces.append(self.face([bottom, verts[-1][(s + 1) % segments], verts[-1][s]], swatch))
        for a, b in zip(verts, verts[1:]):
            for s in range(segments):
                faces.append(self.face([a[s], b[s], b[(s + 1) % segments]], swatch))
                faces.append(self.face([a[s], b[(s + 1) % segments], a[(s + 1) % segments]], swatch))
        self.closed(faces)

    def tube(self, path, radii, sides, swatch, bones, shade=(0.1, 0.85), twist=0.0):
        """A closed tapered tube along any path — a root, a limb. Its rings follow the path."""
        rings = []
        up_hint = Vector((0, 0, 1))
        for i, p in enumerate(path):
            fwd = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            side = fwd.cross(up_hint)
            if side.length < 1e-3:
                side = fwd.cross(Vector((0, 1, 0)))
            side.normalize()
            up = side.cross(fwd).normalized()
            t = shade[0] + (shade[1] - shade[0]) * (i / (len(path) - 1))
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides + twist * i
                ring.append(self.vert(p + (side * math.cos(a) + up * math.sin(a)) * radii[i], t, bones))
            rings.append(ring)
        faces = []
        for a, b in zip(rings, rings[1:]):
            for k in range(sides):
                faces.append(self.face([a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]], swatch))
        faces.append(self.face(list(reversed(rings[0])), swatch))
        faces.append(self.face(rings[-1], swatch))
        self.closed(faces)

    def spike(self, base, direction, length, radius, swatch, bones, bend=Vector((0, 0, 0)), segments=3, sides=3):
        """A root that narrows to a point, curving along `bend`."""
        path, radii = [], []
        for i in range(segments + 1):
            t = i / segments
            path.append(base + direction * (length * t) + bend * (t * t))
            radii.append(max(radius * (1 - t) ** 0.8, 0.004))
        self.tube(path, radii, sides, swatch, bones, shade=(0.15, 0.7))

    def disc(self, center, normal, radius, sides, swatch, bones, depth=0.012):
        """An eye socket: a shallow prism facing `normal`."""
        n = normal.normalized()
        side = n.cross(Vector((0, 0, 1)))
        if side.length < 1e-3:
            side = Vector((1, 0, 0))
        side.normalize()
        up = side.cross(n).normalized()
        front = [self.vert(center + (side * math.cos(2 * math.pi * k / sides) + up * math.sin(2 * math.pi * k / sides)) * radius + n * depth, 0.4, bones) for k in range(sides)]
        back = [self.vert(center + (side * math.cos(2 * math.pi * k / sides) + up * math.sin(2 * math.pi * k / sides)) * radius * 1.1 - n * depth, 0.6, bones) for k in range(sides)]
        faces = [self.face(front, swatch), self.face(list(reversed(back)), swatch)]
        for k in range(sides):
            faces.append(self.face([front[k], back[k], back[(k + 1) % sides], front[(k + 1) % sides]], swatch))
        self.closed(faces)

    # -- output -------------------------------------------------------------------------------

    def finish(self, name, arm):
        bm = self.bm
        uv = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            swatch = SWATCH_NAMES[f[self.swatch]]
            for loop in f.loops:
                loop[uv].uv = uv_for(swatch, loop.vert[self.shade])
        # Back into the armature's space, where KayKit's own meshes sit with an identity transform.
        to_armature = arm.matrix_world.inverted()
        for v in bm.verts:
            v.co = to_armature @ v.co
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        for p in mesh.polygons:
            p.use_smooth = False
        mesh.materials.append(bpy.data.materials.new("enemy"))
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for bone in self.rig.names:
            obj.vertex_groups.new(name=bone)
        obj.parent = arm
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_local = Matrix.Identity(4)
        modifier = obj.modifiers.new("Armature", "ARMATURE")
        modifier.object = arm
        return obj


def rootling(rig):
    """A small hunched bundle of roots and moss around a pale skull. Built at the rig's own size,
    1.5 m to the top of the head bone; the prefab scales it to about 1.2 m."""
    b = Builder(rig, 91)
    H, T = rig.head, rig.tail
    body = ["hips", "spine", "chest"]

    # The bundle: a fungus-white mound whose back rises into a hump, a bark pelvis under it, moss on
    # the hump, and two roots wrapping it. The skull is set into its front, not stood on top of it.
    b.ellipsoid(Vector((0.0, 0.05, 1.00)), (0.32, 0.31, 0.38), 8, 6, "fungus", body, roughness=0.035, shade=(0.0, 0.8),
                squash=lambda co: co + Vector((0, max(0.0, co.z - 1.0) * 0.75, max(0.0, co.z - 1.1) * 0.2)))
    b.ellipsoid(Vector((0.0, 0.02, 0.58)), (0.21, 0.18, 0.15), 6, 4, "bark", ["hips", "spine"], roughness=0.02)
    b.ellipsoid(Vector((0.07, 0.24, 1.33)), (0.15, 0.13, 0.06), 8, 4, "lichen", ["chest"], roughness=0.02, shade=(0.0, 0.6))
    for k in range(2):
        path = []
        for i in range(7):
            t = i / 6
            a = (k * math.pi) + t * math.pi * 1.4
            r = 0.30 + 0.03 * math.sin(t * math.pi)
            path.append(Vector((math.cos(a) * r, 0.06 + math.sin(a) * r * 0.95, 0.64 + t * 0.52)))
        b.tube(path, [0.036] * 7, 3, "bark", body)

    # The face, rigid on `head`: a pale skull low in the front of the bundle, a narrow jaw, two
    # dark sockets looking forward and a little down, which is where a 57-degree camera meets them.
    skull = Vector((0.0, -0.24, 1.32))
    b.ellipsoid(skull, (0.175, 0.165, 0.155), 8, 5, "bone", ["head"], roughness=0.015, shade=(0.0, 0.7),
                squash=lambda co: co if co.z > skull.z - 0.04 else Vector((co.x * 0.86, co.y, co.z)))
    b.ellipsoid(Vector((0.0, -0.30, 1.19)), (0.10, 0.08, 0.05), 6, 3, "bone", ["head"], shade=(0.35, 0.9))
    for x in (-0.066, 0.066):
        b.disc(Vector((x, -0.393, 1.335)), Vector((x * 3.0, -1.0, 0.12)), 0.048, 6, "socket", ["head"])

    # The hood: roots rising from the hump and arching forward over the face, the shape that tells
    # a Rootling from a capsule seen as a black shape from above. Two more sweep back off the hump.
    for k, x in enumerate((-0.21, -0.12, -0.04, 0.04, 0.12, 0.21)):
        base = Vector((x, 0.07 - abs(x) * 0.25, 1.38 - abs(x) * 0.35))
        direction = Vector((x * 1.4, -0.30, 1.0)).normalized()
        length = 0.46 - abs(x) * 0.7 + b.rng.uniform(-0.03, 0.03)
        b.spike(base, direction, length, 0.048, "bark_dark", ["chest"], bend=Vector((x * 0.2, -0.20, -0.10)))
    for x in (-0.15, 0.15):
        b.spike(Vector((x, 0.30, 1.20)), Vector((x * 1.6, 0.85, 0.40)).normalized(), 0.38, 0.05, "bark_dark", ["chest"], bend=Vector((0, 0.04, -0.12)))

    # The arms: long bark roots from shoulder to wrist, three clawed roots for a hand.
    for side in ("l", "r"):
        s = 1.0 if side == "l" else -1.0
        shoulder, elbow, wrist, hand_tail = H[f"upperarm.{side}"], H[f"lowerarm.{side}"], H[f"wrist.{side}"], T[f"hand.{side}"]
        shoulder = shoulder + Vector((-s * 0.04, 0.02, 0.0))
        path = [shoulder, shoulder.lerp(elbow, 0.5), elbow, elbow.lerp(wrist, 0.5), wrist]
        b.tube(path, [0.085, 0.075, 0.066, 0.058, 0.052], 5, "bark", [f"upperarm.{side}", f"lowerarm.{side}", f"wrist.{side}"], twist=0.2)
        b.ellipsoid(H[f"hand.{side}"], (0.07, 0.06, 0.06), 6, 3, "bark_dark", [f"wrist.{side}", f"hand.{side}"])
        for k, (dy, dz) in enumerate(((-0.06, 0.02), (0.0, -0.03), (0.06, 0.02))):
            base = H[f"hand.{side}"] + Vector((s * 0.03, dy, dz))
            b.spike(base, Vector((s, dy * 2.5, -0.35)).normalized(), 0.30, 0.03, "bark_dark", [f"hand.{side}"], bend=Vector((0, 0, -0.09)))

    # The legs: root bundles to the ankle, feet of splayed roots.
    for side in ("l", "r"):
        s = 1.0 if side == "l" else -1.0
        hip, knee, ankle = H[f"upperleg.{side}"], H[f"lowerleg.{side}"], H[f"foot.{side}"]
        path = [hip, hip.lerp(knee, 0.5), knee, knee.lerp(ankle, 0.5), ankle]
        b.tube(path, [0.10, 0.09, 0.08, 0.072, 0.066], 5, "bark", [f"upperleg.{side}", f"lowerleg.{side}", f"foot.{side}"], twist=-0.15)
        for k, (dx, dy) in enumerate(((0.55, -0.6), (0.0, -1.0), (-0.45, -0.7))):
            base = ankle + Vector((0, 0, -0.02))
            b.spike(base, Vector((s * dx * 0.6, dy, -0.55)).normalized(), 0.24, 0.045, "bark", [f"foot.{side}", f"toes.{side}"], bend=Vector((0, -0.04, 0.02)))

    # Roots hanging from the pelvis, behind and to the sides, clear of the legs.
    for k, a in enumerate((0.9, 1.9, 2.8, 3.6)):
        base = Vector((math.cos(a) * 0.19, 0.02 + math.sin(a) * 0.17, 0.55))
        b.spike(base, Vector((math.cos(a) * 0.35, math.sin(a) * 0.35, -1.0)).normalized(), 0.36, 0.035, "bark", ["hips"], bend=Vector((0, 0.04, 0)))

    return b


def export(arm, mesh_obj):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, "Rootling.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        # "FBX All": the unit scale is baked into the data, so the armature node imports at scale 1
        # and every bone at KayKit's own offsets. The default leaves Rig_Medium at scale 100 over
        # bones a hundredth of the size, which binds the same paths and plays every clip wrong.
        apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE",
        use_mesh_modifiers=False,
        add_leaf_bones=False,
        use_armature_deform_only=False,
        armature_nodetype="NULL",
        bake_anim=False,
        path_mode="STRIP",
    )


def check(mesh_obj):
    mesh = mesh_obj.data
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    assert TRIANGLE_BUDGET[0] <= tris <= TRIANGLE_BUDGET[1], f"{tris} triangles is outside GD 17.1's crowd budget"
    names = [g.name for g in mesh_obj.vertex_groups]
    for v in mesh.vertices:
        groups = [(names[g.group], g.weight) for g in v.groups if g.weight > 0]
        assert groups, f"vertex {v.index} is unweighted"
        assert len(groups) <= 4 and abs(sum(w for _, w in groups) - 1.0) < 1e-3, f"vertex {v.index}: {groups}"
        assert not any(n in ("root", "handslot.l", "handslot.r") for n, _ in groups), f"vertex {v.index} rides {groups}"
    to_world = mesh_obj.parent.matrix_world
    zs = [(to_world @ v.co).z for v in mesh.vertices]
    print(f"  Rootling: {tris} triangles, {len(mesh.vertices)} vertices, {min(zs):.3f}-{max(zs):.3f} m tall")
    return tris


# ---------------------------------------------------------------------------------------------
# The pose sheet: the exported model, reloaded, in the clips M7-05h chooses from.
# ---------------------------------------------------------------------------------------------

SHEET_CLIPS = (
    ("MovementAdvanced", "Sneaking"),
    ("MovementAdvanced", "Crouching"),
    ("Special", "Skeletons_Walking"),
    ("CombatMelee", "Melee_2H_Attack_Chop"),
    ("CombatMelee", "Melee_Unarmed_Attack_Punch_A"),
    ("General", "Hit_A"),
    ("General", "Death_A"),
)
CELL = 256


def pose_sheet(path):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, "Rootling.fbx"))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    material = body.data.materials[0]
    material.use_nodes = True
    nodes = material.node_tree.nodes
    image = bpy.data.images.load(os.path.join(TEX, "T_Enemy_Albedo.png"))
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    material.node_tree.links.new(tex.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])

    actions = {}
    for file, _ in SHEET_CLIPS:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(CLIPS, f"Rig_Medium_{file}.fbx"))
        for obj in set(bpy.data.objects) - before:
            bpy.data.objects.remove(obj)
    for action in bpy.data.actions:
        actions[action.name.split("|")[-1]] = action

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = scene.render.resolution_y = CELL
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("sheet") if scene.world is None else scene.world
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.4
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    # The game camera's view — 57 degrees down, facing the Rootling as it walks at the player — and a
    # three-quarter view for the deformation.
    views = {
        "game": (Vector((0, -math.cos(math.radians(57)) * 6, math.sin(math.radians(57)) * 6 + 0.7)), Vector((0, 0, 0.7))),
        "three_quarter": (Vector((-4.2, -4.2, 2.6)), Vector((0, 0, 0.7))),
    }

    def aim(eye, target):
        cam.location = eye
        cam.rotation_euler = (target - eye).to_track_quat("-Z", "Y").to_euler()

    def render():
        out = os.path.join(bpy.app.tempdir, "cell.png")
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(out)
        px = np.array(img.pixels[:], np.float32).reshape(CELL, CELL, 4)[..., :3]
        bpy.data.images.remove(img)
        return np.flipud(px)

    rows = []
    arm.animation_data_create()
    for _, clip in SHEET_CLIPS:
        action = actions.get(clip)
        assert action is not None, f"no action {clip} in {sorted(actions)[:12]}"
        arm.animation_data.action = action
        start, end = action.frame_range
        cells = []
        for view in ("game", "three_quarter"):
            aim(*views[view])
            for f in (0.0, 0.45, 0.8):
                scene.frame_set(int(round(start + (end - start) * f)))
                cells.append(render())
        rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {clip} ({end - start:.0f} frames)")

    # The silhouette check (rule 8): black on white, from the game camera's 57 degrees, idle and
    # walking, beside the capsule every other archetype wears.
    arm.animation_data.action = actions["Sneaking"]
    scene.display.shading.light = "FLAT"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0, 0, 0)
    scene.display.shading.show_object_outline = False
    scene.world.color = (1, 1, 1)
    cells = []
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.4 / 0.78, depth=1.6 / 0.78, location=(1.6, 0, 0.8 / 0.78))
    capsule = bpy.context.object
    pitch = math.radians(57)
    for f in (0.0, 0.3, 0.6):
        frame = int(round(actions["Sneaking"].frame_range[0] + (actions["Sneaking"].frame_range[1] - actions["Sneaking"].frame_range[0]) * f))
        scene.frame_set(frame)
        cam_data.ortho_scale = 4.4
        aim(Vector((0.8, -math.cos(pitch) * 8, math.sin(pitch) * 8 + 0.6)), Vector((0.8, 0, 0.6)))
        cells.append(render())
    bpy.data.objects.remove(capsule)
    silhouette = np.concatenate(cells, axis=1)
    silhouette = np.concatenate([silhouette, np.ones((CELL, rows[0].shape[1] - silhouette.shape[1], 3), np.float32)], axis=1)
    rows.append(silhouette)
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("rootling.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites "
                 "the FBX's object ids (see the docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    os.makedirs(TEX, exist_ok=True)
    print("atlas")
    build_atlas()
    print("model")
    arm = load_rig()
    rig = Rig(arm)
    obj = rootling(rig).finish("Rootling_Body", arm)
    check(obj)
    export(arm, obj)
    if "--pose-sheet" in args:
        pose_sheet(args[args.index("--pose-sheet") + 1])


main()
