"""The Jungle's Bloater, a puffball mushroom in KayKit's style: built here from smooth shells, rigged on a
skeleton of its own and animated here.

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/puffball.py [-- --pose-sheet <out.png>]

It writes, overwriting what it wrote last time, so both files are an output of this script:

- Assets/_Project/Art/Enemies/Puffball.fbx                one skinned mesh on Rig_Puffball, with its clips
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas (enemy_atlas.py)

**Built as pitcher.py builds the Pitcher (M7-05o).** Every part is a rounded, smooth-shaded shell of one
colour, placed on a bone and bound to it rigidly; the shells overlap at every joint, so a bent joint
opens no seam. Nothing outside the repository is read. The shell, posing and export helpers are
pitcher.py's, copied rather than shared, as pitcher.py copied frog.py's (JG-03a's Out of scope).

**Round, where the Frog is low and wide and the Pitcher tall (GD 17.1: silhouettes must differ as black
shapes).** A cap a metre across and about as tall, tapering underneath onto a short stubby stalk, with a
darker pore on top, a few warm tan spots and two small eyes so it has a front. As deep as it is wide,
so from the game camera it is a circle with nothing sticking out.

**No legs.** It waddles by rocking from one edge of its stalk's foot to the other, twisting, with a
little hop as it crosses the middle; every waddle frame is lifted until its lowest point meets the floor.

**The cap is the telegraph.** Swell inflates it on its bone, trembling, to its largest on frame 21, the
moment the Bloater detonates; Burst pops it further and collapses it to a flat skin. The glow is the
body's (EnemyHitFeedback), not the model's.

**Why a rig of its own, not Rig_Medium**, frog.py's reason: no humanoid clip plays on a mushroom.
"""

import math
import os
import sys
from collections import defaultdict

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True  # no __pycache__ beside the scripts
from enemy_atlas import build_atlas, save_png, uv_for  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Enemies")
TEX = os.path.join(OUT, "Textures")

TRIANGLE_BUDGET = (400, 2500)  # GD 17.1, the crowd's
PALE = (0.55, 0.25)  # least mean luma, most mean saturation: RootlingModelTests' bars (PuffballModelTests)
FPS = 30
SIDES = (("l", 1.0), ("r", -1.0))  # the mushroom faces -Y, so its left is +X


def v(x, y, z):
    return Vector((x, y, z))


# ---------------------------------------------------------------------------------------------
# The skeleton's joints, in the model's own frame: Z up, facing -Y, 1.06 m to the top of the pore.
# ---------------------------------------------------------------------------------------------

BASE = v(0, 0, 0.02)  # where the body rocks on its foot
CAP = v(0, 0, 0.26)  # where the cap sits on the stalk: it swells and collapses about this point
STALK_TOP = 0.32


# ---------------------------------------------------------------------------------------------
# Shells. Each shape is (verts, faces) in a unit local space; a part places one, names its swatch and
# the bone it rides, rigidly. pitcher.py's helpers.
# ---------------------------------------------------------------------------------------------


def blob(cuts, roundness=1.0):
    """A cube cut into a grid and pushed toward a sphere (frog.py's)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    if cuts:
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    for vert in bm.verts:
        p = vert.co.copy()
        p = p.lerp(p.normalized(), roundness)
        p /= max(abs(p.x), abs(p.y), abs(p.z), 1e-6)
        vert.co = p.lerp(p.normalized(), roundness)
    verts = [vt.co.copy() for vt in bm.verts]
    faces = [[vt.index for vt in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


def ball(segments, rings):
    """A UV sphere, for the small round bits a blob would spend too much on."""
    verts = [v(0, 0, 1)]
    for r in range(1, rings):
        phi = math.pi * r / rings
        for s in range(segments):
            theta = 2 * math.pi * s / segments
            verts.append(v(math.sin(phi) * math.cos(theta), math.sin(phi) * math.sin(theta), math.cos(phi)))
    verts.append(v(0, 0, -1))
    last, n = len(verts) - 1, segments
    faces = []
    for s in range(n):
        faces.append([0, 1 + s, 1 + (s + 1) % n])
        faces.append([last, 1 + (rings - 2) * n + (s + 1) % n, 1 + (rings - 2) * n + s])
    for r in range(rings - 2):
        for s in range(n):
            a, b = 1 + r * n + s, 1 + r * n + (s + 1) % n
            faces.append([a, a + n, b + n, b])
    return verts, faces


def rings_to_faces(index, segments):
    faces = []
    for ra, rb in zip(index, index[1:]):
        for s in range(segments):
            if len(ra) == 1:
                faces.append([ra[0], rb[(s + 1) % segments], rb[s]])
            elif len(rb) == 1:
                faces.append([ra[s], ra[(s + 1) % segments], rb[0]])
            else:
                faces.append([ra[s], ra[(s + 1) % segments], rb[(s + 1) % segments], rb[s]])
    return faces


def lathe(profile, segments, radial=None):
    """A surface of revolution about Z, already in place: `profile` is (z, r) from the bottom up, and
    an r of 0 is a pole. `radial(theta, z)` scales r around the ring, for lumps and ovals."""
    verts, index = [], []
    for z, r in profile:
        if r < 1e-4:
            index.append([len(verts)])
            verts.append(v(0, 0, z))
            continue
        index.append(list(range(len(verts), len(verts) + segments)))
        for s in range(segments):
            theta = 2 * math.pi * s / segments
            k = radial(theta, z) if radial else 1.0
            verts.append(v(r * k * math.cos(theta), r * k * math.sin(theta), z))
    return verts, rings_to_faces(index, segments)


def dome(n=8):
    """A spot: a centre, a ring at the surface and a ring sunk under it, so no edge ever shows."""
    verts = [v(0, 0, 1)] + [v(math.cos(2 * math.pi * i / n), math.sin(2 * math.pi * i / n), 0.4) for i in range(n)]
    verts += [v(1.25 * math.cos(2 * math.pi * i / n), 1.25 * math.sin(2 * math.pi * i / n), -2.5) for i in range(n)]
    faces = [[0, 1 + i, 1 + (i + 1) % n] for i in range(n)]
    faces += [[1 + i, 1 + n + i, 1 + n + (i + 1) % n, 1 + (i + 1) % n] for i in range(n)]
    return verts, faces


def outward(verts, faces):
    """The faces of a closed shell wound so every normal points out, done per shell while it is
    still closed (frog.py's)."""
    bm = bmesh.new()
    made = [bm.verts.new(co) for co in verts]
    for f in faces:
        bm.faces.new([made[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.verts.index_update()
    wound = [[vt.index for vt in f.verts] for f in bm.faces]
    bm.free()
    return wound


class Part:
    def __init__(self, verts, faces, swatch, bone, closed=True):
        self.verts = verts
        self.faces = outward(verts, faces) if closed else faces
        self.swatch = swatch
        self.bone = bone
        self.closed = closed


PARTS = []


def add(shape, swatch, bone, closed=True):
    """A shape already in place."""
    PARTS.append(Part(shape[0], shape[1], swatch, bone, closed))
    return shape[0]


def place(shape, center, half, swatch, bone, rot=Quaternion(), warp=None, closed=True):
    """A unit shape scaled by `half`, warped in its own frame, turned by `rot` and moved to `center`."""
    verts, faces = shape
    out = []
    for p in verts:
        q = v(p.x * half[0], p.y * half[1], p.z * half[2])
        if warp:
            q = warp(q, p)
        out.append(center + rot @ q)
    PARTS.append(Part(out, faces, swatch, bone, closed))
    return out


def facing(z_axis, up=v(0, 0, 1)):
    """The turn that takes local +Z to `z_axis`, with local +Y as near `up` as it can be."""
    z = z_axis.normalized()
    x = up.cross(z)
    if x.length < 1e-4:
        x = v(1, 0, 0)
    x.normalize()
    y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_quaternion()


def surface(part, angle, z):
    """Where a ray toward the axis at height `z` lands on `part`, `angle` degrees round from the front
    (-Y) toward the left (+X), and the outward normal there."""
    a = math.radians(angle - 90)  # 0 degrees is the front, -Y
    d = v(math.cos(a), math.sin(a), 0)
    hit, normal, _, _ = BVHTree.FromPolygons(part.verts, part.faces).ray_cast(v(0, 0, z) + d * 1.0, -d)
    assert hit is not None, f"no surface at {angle}, {z}"
    if normal.dot(d) < 0:
        normal = -normal
    return hit, normal


def build():
    # The cap: one smooth ball, broadest a little above its middle, tucked in underneath onto the stalk
    # like a pear's bottom and a little flattened on top. A faint lumpiness, so it is a fungus and not
    # a sphere.
    cap = ((0.21, 0.0), (0.225, 0.15), (0.26, 0.26), (0.32, 0.355), (0.40, 0.43), (0.49, 0.475), (0.59, 0.49),
           (0.69, 0.478), (0.78, 0.44), (0.86, 0.375), (0.925, 0.29), (0.97, 0.19), (0.995, 0.09), (1.003, 0.0))

    def lumps(theta, z):
        return 1.0 + 0.018 * math.cos(5 * theta + 1.3) * math.sin(math.pi * min(1.0, max(0.0, (z - 0.21) / 0.8)))
    PARTS.append(Part(*lathe(cap, 24, lumps), "puff_cap", "cap"))
    shell = PARTS[-1]

    # The pore on top, where the spores come out: a low dark nub.
    place(blob(1), v(0, 0, 0.995), (0.075, 0.075, 0.03), "puff_pore", "cap")

    # The stalk: short and stubby, flaring into a foot, its top buried in the cap.
    stalk = ((0.0, 0.0), (0.004, 0.19), (0.03, 0.225), (0.08, 0.215), (0.15, 0.185), (0.23, 0.165),
             (STALK_TOP, 0.155), (STALK_TOP + 0.01, 0.0))
    add(lathe(stalk, 20), "puff_stalk", "stalk")

    # The eyes: small and dark with a glint, high on the front of the cap, where the game camera
    # looks down on them (JG-03b), so it has a front.
    for _, s in SIDES:
        hit, normal = surface(shell, 21 * s, 0.75)
        centre = hit - normal * 0.004
        place(blob(1), centre, (0.06, 0.08, 0.03), "puff_eye", "cap", facing(normal))
        place(ball(6, 4), centre + normal * 0.026 + v(-0.018 * s, 0, 0.03), (0.018, 0.018, 0.014), "puff_shine", "cap")
    return shell


def spots(shell):
    """A few warm tan spots pressed into the cap, none on the face."""
    for angle, z, r in ((150, 0.78, 0.075), (-140, 0.68, 0.062), (62, 0.86, 0.052), (-72, 0.88, 0.056),
                        (104, 0.52, 0.06), (-108, 0.47, 0.05), (20, 0.94, 0.045), (180, 0.52, 0.07)):
        hit, normal = surface(shell, angle, z)
        place(dome(), hit, (r, r * 0.85, 0.010), "puff_spot", "cap", facing(normal, v(0, 0, 1)), closed=False)


def cull():
    """Drop every face buried inside another closed shell on the same bone (frog.py's)."""
    by_bone = defaultdict(list)
    for part in PARTS:
        by_bone[part.bone].append(part)
    removed = 0
    for parts in by_bone.values():
        closed = [(p, BVHTree.FromPolygons(p.verts, p.faces)) for p in parts if len(p.faces) > 20 and p.closed]
        for part in parts:
            def buried(point):
                for other, tree in closed:
                    if other is part:
                        continue
                    near, normal, _, _ = tree.find_nearest(point)
                    if near is not None and (point - near).dot(normal) < -0.004:
                        return True
                return False
            inside = [buried(p) for p in part.verts]
            kept = [f for f in part.faces if not all(inside[i] for i in f)]
            removed += len(part.faces) - len(kept)
            part.faces = kept
    print(f"  cull: {removed} buried faces dropped")


# ---------------------------------------------------------------------------------------------
# One mesh: every shell, smooth-shaded, each corner sampling its swatch by height, so the mushroom is
# light on top and darker toward its foot.
# ---------------------------------------------------------------------------------------------

TOP_Z = 1.06


def assemble(bone_names):
    mesh = bpy.data.meshes.new("Puffball_Body")
    body = bpy.data.objects.new("Puffball_Body", mesh)
    bpy.context.scene.collection.objects.link(body)
    for name in bone_names:
        body.vertex_groups.new(name=name)
    groups = {g.name: g.index for g in body.vertex_groups}
    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()
    uv = bm.loops.layers.uv.new("UVMap")
    for part in PARTS:
        made = []
        for co in part.verts:
            vert = bm.verts.new(co)
            vert[deform][groups[part.bone]] = 1.0
            made.append(vert)
        for f in part.faces:
            face = bm.faces.new([made[i] for i in f])
            for loop in face.loops:
                loop[uv].uv = uv_for(part.swatch, (TOP_Z - loop.vert.co.z) / TOP_Z)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.verts.ensure_lookup_table()
    unused = [vt for vt in bm.verts if not vt.link_faces]  # every face around it was buried
    bmesh.ops.delete(bm, geom=unused, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    mesh.materials.append(bpy.data.materials.new("enemy"))
    return body


# ---------------------------------------------------------------------------------------------
# Checks, run before anything is written.
# ---------------------------------------------------------------------------------------------


def pale(body, atlas):
    """PuffballModelTests' colour row, run here first: the surface's mean luma and saturation."""
    def luma(c):
        return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722

    def saturation(c):
        hi, lo = c.max(axis=-1), c.min(axis=-1)
        return np.where(hi > 0, (hi - lo) / np.maximum(hi, 1e-9), 0.0)

    mesh = body.data
    uv = mesh.uv_layers.active.data
    area, lum, sat = 0.0, 0.0, 0.0
    for poly in mesh.polygons:
        u = sum(uv[i].uv.x for i in poly.loop_indices) / poly.loop_total
        w = sum(uv[i].uv.y for i in poly.loop_indices) / poly.loop_total
        colour = atlas[min(int((1.0 - w) * atlas.shape[0]), atlas.shape[0] - 1), min(int(u * atlas.shape[1]), atlas.shape[1] - 1)]
        area += poly.area
        lum += poly.area * float(luma(colour))
        sat += poly.area * float(saturation(colour))
    print(f"  colour: luma {lum / area:.3f} and saturation {sat / area:.3f}")
    assert lum / area >= PALE[0] and sat / area <= PALE[1], "the Puffball is not pale"


def check(body, atlas):
    mesh = body.data
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    assert TRIANGLE_BUDGET[0] <= tris <= TRIANGLE_BUDGET[1], f"{tris} triangles is outside GD 17.1's crowd budget"
    names = [g.name for g in body.vertex_groups]
    for vert in mesh.vertices:
        groups = [(names[g.group], g.weight) for g in vert.groups if g.weight > 0]
        assert groups, f"vertex {vert.index} is unweighted"
        assert len(groups) <= 4 and abs(sum(w for _, w in groups) - 1.0) < 1e-3, f"vertex {vert.index}: {groups}"
        assert not any(n == "root" for n, _ in groups), f"vertex {vert.index} rides the root"
    pale(body, atlas)
    xs = [vert.co.x for vert in mesh.vertices]
    ys = [vert.co.y for vert in mesh.vertices]
    zs = [vert.co.z for vert in mesh.vertices]
    w, d, h = max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)
    print(f"  Puffball: {tris} triangles, {len(mesh.vertices)} vertices, {h:.3f} m tall, {w:.3f} m wide, {d:.3f} m deep")
    assert 1.0 <= h / w <= 1.35 and 0.85 <= d / w <= 1.15, "the Puffball is not round (PuffballModelTests)"


def model(tex_dir=TEX):
    """The atlas and the unrigged mesh, checked: where main() starts, and a review render can."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    PARTS.clear()
    atlas = build_atlas(tex_dir)
    shell = build()
    spots(shell)
    cull()
    body = assemble(list(skeleton()))
    check(body, atlas)
    return body


# ---------------------------------------------------------------------------------------------
# The skeleton. Each bone is (parent, head, tail) in the model's frame. `body` is a short bone at the
# foot the whole mushroom rocks on; `stalk` stands on it, and `cap` on the stalk, carrying the pore, the
# spots and the eyes, so a swell or a collapse is one bone's scale.
# ---------------------------------------------------------------------------------------------


def skeleton():
    return {
        "root": (None, v(0, 0, 0), v(0, 0, 0.12)),
        "body": ("root", BASE, BASE + v(0, 0, 0.10)),
        "stalk": ("body", BASE, CAP),
        "cap": ("stalk", CAP, v(0, 0, 0.95)),
    }


def build_armature():
    data = bpy.data.armatures.new("Rig_Puffball")
    arm = bpy.data.objects.new("Rig_Puffball", data)
    bpy.context.scene.collection.objects.link(arm)
    # frog.py's shape: Y-up data under an object turned 90 degrees about X, so it faces +Z in Unity.
    arm.matrix_world = Matrix.Rotation(math.radians(90), 4, "X")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    to_arm = arm.matrix_world.inverted()
    edit = {}
    for name, (parent, head, tail) in skeleton().items():
        bone = data.edit_bones.new(name)
        bone.head = to_arm @ head
        bone.tail = to_arm @ tail
        direction = (tail - head).normalized()
        up = v(0, 0, 1) if abs(direction.z) < 0.7 else v(0, -1, 0)
        bone.align_roll(to_arm.to_3x3() @ up)
        if parent:
            bone.parent = edit[parent]
        edit[name] = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    data.bones["root"].use_deform = False
    data.bones["body"].use_deform = False
    return arm


def bind(body, arm):
    """Under the armature, identity transform, data in its space, as KayKit's own meshes sit."""
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    body.parent = arm
    body.data.transform(arm.matrix_world.inverted())
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_local = Matrix.Identity(4)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------------------------------------
# Posing, pitcher.py's without the legs: a pose names a body offset and turn, a turn for the stalk and
# the cap, and a (width, height) scale for each, and becomes each pose bone's basis.
# ---------------------------------------------------------------------------------------------


def rot(pitch=0.0, roll=0.0, yaw=0.0):
    """Degrees, about the rest frame's axes. Pitch > 0 tips the top forward (-Y); roll > 0 tips it
    toward +X, the mushroom's left; yaw > 0 turns the front toward +X."""
    return (Quaternion(v(0, 0, 1), math.radians(yaw)) @ Quaternion(v(0, 1, 0), math.radians(roll))
            @ Quaternion(v(1, 0, 0), math.radians(pitch)))


def about(p, q):
    return Matrix.Translation(p) @ q.to_matrix().to_4x4() @ Matrix.Translation(-p)


class Poser:
    def __init__(self, arm):
        self.arm = arm
        self.A = arm.matrix_world.copy()
        self.rest = {b.name: self.A @ b.matrix_local for b in arm.data.bones}
        self.head = {n: m.to_translation() for n, m in self.rest.items()}
        self.order = [b.name for b in arm.data.bones]  # parents before children
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}

    def world(self, pose):
        W = {"root": self.rest["root"]}
        carry = {"root": Matrix.Identity(4)}

        def fk(name, parent, q=Quaternion(), scale=(1.0, 1.0)):
            # A bone's Y runs along it, up the mushroom; X and Z are across it, so (width, height) is
            # (X and Z, Y) in its own frame.
            width, height = scale
            m = carry[parent] @ about(self.head[name], q) @ self.rest[name] @ Matrix.Diagonal((width, height, width, 1.0))
            W[name] = m
            carry[name] = m @ self.rest[name].inverted()

        body = Matrix.Translation(pose.get("offset", v(0, 0, 0))) @ about(self.head["body"], rot(*pose.get("body", (0, 0, 0))))
        W["body"] = body @ self.rest["body"]
        carry["body"] = body
        fk("stalk", "body", rot(*pose.get("stalk", (0, 0, 0))), pose.get("stalk_scale", (1.0, 1.0)))
        fk("cap", "stalk", rot(*pose.get("cap", (0, 0, 0))), pose.get("cap_scale", (1.0, 1.0)))
        return W

    def apply(self, pose):
        W = self.world(pose)
        A_inv = self.A.inverted()
        M = {n: A_inv @ W[n] for n in self.order}
        bones = self.arm.data.bones
        for n in self.order:
            B = bones[n].matrix_local
            p = self.parent[n]
            if p is None:
                basis = B.inverted() @ M[n]
            else:
                basis = (bones[p].matrix_local.inverted() @ B).inverted() @ M[p].inverted() @ M[n]
            self.arm.pose.bones[n].matrix_basis = basis


# ---------------------------------------------------------------------------------------------
# The clips, in place at 30 fps, the root never moving:
#
#     Idle    48 f  loop   breathing: the cap swelling and settling, a little sway
#     Waddle  20 f  loop   two rocks, foot edge to foot edge, twisting, with a hop as it crosses the
#                          middle; 0.9 m of ground a cycle at 1x by the rule of thumb a stride is
#                          for a body with no feet, so 1.35 m/s
#     Swell   30 f         squats, then the cap trembles and inflates to its largest on frame 21 (0.7 s):
#                          EnemyAnimatorView's strike time, the Bloater's detonation
#     Burst   24 f         pops a little further and collapses to a flat skin by frame 9
#     Hit     12 f         a flinch: knocked back, the cap squashed, and a rebound
# ---------------------------------------------------------------------------------------------

STRIDE = 0.9  # metres of ground a Waddle cycle stands for (JG-03b's _strideSpeed)
STRIKE_FRAME = 21


def P(**kw):
    return dict(kw)


def waddle():
    keys = {}
    for f in range(0, 21, 2):
        p = f / 20
        rock = math.sin(2 * math.pi * p)  # +1 tipped onto its left edge, -1 its right
        cross = math.cos(4 * math.pi * p)  # +1 crossing the middle, -1 at either edge
        keys[f] = P(body=(6.0, 11.0 * rock, 8.0 * rock), stalk=(0, -3.0 * rock, 0),
                    cap=(0, -4.0 * math.sin(2 * math.pi * p - 0.6), 0),
                    cap_scale=(1.0 - 0.035 * cross, 1.0 + 0.05 * cross),
                    hop=0.05 * ((1 + cross) / 2) ** 2, ground=True)
    return keys


def clips():
    swollen = (1.28, 1.26)
    # The cap rides the stalk's scale, so a squashed stalk flattens and widens the cap too: these are
    # 1.14 wide and 0.32 tall in all.
    flat = dict(stalk_scale=(1.2, 0.45), cap_scale=(0.95, 0.7))
    return {
        "Idle": (True, {
            0: P(),
            12: P(cap=(0, 1.5, 0), cap_scale=(1.03, 0.975)),
            24: P(body=(1.0, 0, 0), cap_scale=(1.0, 1.01)),
            36: P(cap=(0, -1.5, 0), cap_scale=(1.025, 0.98)),
            48: P(),
        }),
        "Waddle": (True, waddle()),
        "Swell": (False, {
            0: P(),
            4: P(body=(-3, 0, 0), stalk_scale=(1.04, 0.92), cap_scale=(1.04, 0.97)),
            8: P(body=(-2, 0, 0), stalk_scale=(1.03, 0.95), cap=(0, 2.0, 0), cap_scale=(1.08, 1.05)),
            11: P(body=(-2, 0, 0), stalk_scale=(1.03, 0.96), cap=(0, -2.5, 0), cap_scale=(1.11, 1.08)),
            14: P(body=(-1, 0, 0), stalk_scale=(1.04, 0.97), cap=(0, 3.0, 0), cap_scale=(1.15, 1.12)),
            17: P(body=(-1, 0, 0), stalk_scale=(1.05, 0.99), cap=(0, -3.0, 0), cap_scale=(1.19, 1.16)),
            19: P(stalk_scale=(1.05, 1.01), cap=(0, 2.0, 0), cap_scale=(1.23, 1.2)),
            STRIKE_FRAME: P(stalk_scale=(1.06, 1.04), cap_scale=swollen),
            25: P(cap_scale=(1.1, 1.08)),
            30: P(),
        }),
        "Burst": (False, {
            0: P(stalk_scale=(1.06, 1.04), cap_scale=swollen),
            2: P(stalk_scale=(1.08, 1.06), cap_scale=(1.4, 1.36)),
            5: P(stalk_scale=(1.1, 0.75), cap_scale=(1.12, 0.75)),
            9: P(**flat),
            13: P(stalk_scale=(1.18, 0.5), cap_scale=(0.95, 0.75)),
            17: P(**flat),
            24: P(**flat),
        }),
        "Hit": (False, {
            0: P(),
            3: P(body=(-12, 3, 0), cap=(-6, 0, 0), cap_scale=(1.1, 0.86), ground=True),
            7: P(body=(3, -1, 0), cap=(2, 0, 0), cap_scale=(0.97, 1.05), ground=True),
            12: P(),
        }),
    }


def lowest(body):
    """The lowest point of the posed mesh, in the model's frame."""
    bpy.context.view_layer.update()
    evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    z = min((evaluated.matrix_world @ vt.co).z for vt in mesh.vertices)
    evaluated.to_mesh_clear()
    return z


def author_clips(arm, body):
    poser = Poser(arm)
    arm.animation_data_create()
    made = []
    for name, (loop, keys) in clips().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        last = {}
        for frame in sorted(keys):
            pose = dict(keys[frame])
            grounded = pose.pop("ground", False)
            hop = pose.pop("hop", 0.0)
            poser.apply(pose)
            if grounded:  # tipped: lifted until its lowest point meets the floor, then hopped
                pose["offset"] = pose.get("offset", v(0, 0, 0)) - v(0, 0, lowest(body) - hop)
                poser.apply(pose)
            for pb in arm.pose.bones:
                q = pb.rotation_quaternion.copy()
                if pb.name in last and last[pb.name].dot(q) < 0:
                    q.negate()
                    pb.rotation_quaternion = q
                last[pb.name] = q
                pb.keyframe_insert("location", frame=frame, group=pb.name)
                pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
                pb.keyframe_insert("scale", frame=frame, group=pb.name)
        action.frame_range = (0, max(keys))
        action.use_frame_range = True
        made.append(action)
        print(f"  clip {name}: {max(keys)} frames, {'loop' if loop else 'once'}")
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    return made


def export(arm, body):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, "Puffball.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",  # rootling.py says why
        mesh_smooth_type="FACE",
        use_mesh_modifiers=False,
        add_leaf_bones=False,
        use_armature_deform_only=False,
        armature_nodetype="NULL",
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
    )


# ---------------------------------------------------------------------------------------------
# The pose sheet: the exported model, reloaded, in every clip, over the Jungle's floor colour, and the
# three Jungle bodies' black shapes from the game camera.
# ---------------------------------------------------------------------------------------------

CELL = 288
SHEET = {
    "Idle": (0, 12, 24, 36),
    "Waddle": (0, 3, 5, 8, 10, 15),
    "Swell": (0, 4, 11, 17, 21, 25),
    "Burst": (0, 2, 5, 9, 13, 24),
    "Hit": (0, 3, 7, 12),
}


def pose_sheet(path):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, "Puffball.fbx"))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    image = bpy.data.images.load(os.path.join(TEX, "T_Enemy_Albedo.png"))

    def textured(material, img):
        material.use_nodes = True
        nodes = material.node_tree.nodes
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = "Closest"
        material.node_tree.links.new(tex.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
        nodes.active = tex  # Workbench draws the active image node

    textured(body.data.materials[0], image)
    floor_img = bpy.data.images.new("floor", 4, 4)
    floor_img.pixels.foreach_set(np.tile(np.array([0x4E / 255, 0x6E / 255, 0x3F / 255, 1.0], np.float32), 16))
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    floor = bpy.context.object
    floor.data.materials.append(bpy.data.materials.new("floor"))
    textured(floor.data.materials[0], floor_img)

    actions = {a.name.split("|")[-1]: a for a in bpy.data.actions}
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.view_settings.view_transform = "Standard"  # AgX, the default, greys the atlas's colours
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "TEXTURE"
    shading.show_object_outline = True
    shading.show_shadows = True
    shading.show_specular_highlight = False  # M_Enemy's smoothness is 0.05 (pitcher.py's finding 1)
    scene.render.resolution_x = scene.render.resolution_y = CELL
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def aim(eye, target, scale):
        cam_data.ortho_scale = scale
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

    pitch_r = math.radians(57)
    views = {
        "three_quarter": (v(-3.2, -3.4, 2.4), v(0, 0, 0.5), 2.0),
        "front": (v(0, -5, 0.6), v(0, 0, 0.5), 2.0),
        "side": (v(-5, 0, 0.6), v(0, 0, 0.5), 2.0),
        "game": (v(0, -math.cos(pitch_r) * 6, math.sin(pitch_r) * 6 + 0.4), v(0, 0, 0.45), 2.0),
    }
    rows_of = {"Waddle": ("three_quarter", "front"), "Swell": ("three_quarter", "side"),
               "Burst": ("three_quarter", "side"), "Hit": ("three_quarter", "side")}
    width = max(len(f) for f in SHEET.values())
    rows = []
    arm.animation_data_create()
    for clip, frames in SHEET.items():
        arm.animation_data.action = actions[clip]
        for view in rows_of.get(clip, ("three_quarter",)):
            aim(*views[view])
            cells = []
            for f in frames:
                scene.frame_set(int(f))
                cells.append(render())
            while len(cells) < width:
                cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
            rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {clip}")
    # The game camera's view, and the three Jungle bodies as black shapes: Frog, Pitcher, Puffball.
    cells = []
    for clip, f in (("Idle", 0), ("Waddle", 5), ("Swell", STRIKE_FRAME), ("Burst", 9)):
        arm.animation_data.action = actions[clip]
        scene.frame_set(f)
        aim(*views["game"])
        cells.append(render())
    arm.animation_data.action = actions["Idle"]
    scene.frame_set(0)
    def shift(obj, x):
        # Under an empty, because each clip keys the armature object's own transform and would put it
        # back at the origin on the next frame drawn.
        holder = bpy.data.objects.new(f"at_{obj.name}", None)
        scene.collection.objects.link(holder)
        holder.location.x = x
        obj.parent = holder

    for name, x in (("Frog", -1.6), ("Pitcher", 0.0)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, f"{name}.fbx"))
        for obj in set(bpy.data.objects) - before:
            if obj.parent is None:
                shift(obj, x)
    shift(arm, 1.4)
    floor.hide_render = True
    shading.light = "FLAT"
    shading.color_type = "SINGLE"
    shading.single_color = (0, 0, 0)
    shading.show_object_outline = False
    shading.show_shadows = False
    scene.world = scene.world or bpy.data.worlds.new("sheet")
    scene.world.color = (1, 1, 1)
    aim(v(-0.1, -math.cos(pitch_r) * 8, math.sin(pitch_r) * 8 + 0.5), v(-0.1, 0, 0.6), 4.4)
    cells.append(render())
    while len(cells) < width:
        cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
    rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("puffball.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
                 "FBX's object ids (rootling.py's docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    print("model")
    body = model()
    arm = build_armature()
    bind(body, arm)
    print("clips")
    author_clips(arm, body)
    export(arm, body)
    if "--pose-sheet" in args:
        pose_sheet(os.path.abspath(args[args.index("--pose-sheet") + 1]))  # Blender would not read it from here


if __name__ == "__main__":
    main()
