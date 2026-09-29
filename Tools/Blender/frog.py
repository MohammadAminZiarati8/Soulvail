"""The Jungle Frog, in KayKit's style: built here from smooth shells, rigged on a skeleton of its own and
animated here.

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/frog.py [-- --pose-sheet <out.png>]

It writes, overwriting what it wrote last time, so both files are an output of this script:

- Assets/_Project/Art/Enemies/Frog.fbx                    one skinned mesh on Rig_Frog, with its clips
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas (enemy_atlas.py)

**Built the way KayKit builds a character (M7-05n).** KayKit's bodies are separate closed shells that
intersect (a head, a jaw, an eye, a limb), each smooth-shaded and one colour, with a soft light-to-dark
gradient from the atlas. So is this frog: every part below is a rounded shell placed on a bone and
bound to it rigidly. The shells overlap at every joint, so a bent joint opens no seam. Its proportions
are KayKit's too: a head wider than the body, big eyes, short thick limbs, round toe pads. It replaced
M7-05j's Tripo sculpt, cut to 1,200 flat-shaded triangles, which read as faceted polygon art beside
KayKit's bodies. Nothing outside the repository is read.

**Why a rig of its own, not Rig_Medium.** Every other body wears KayKit's humanoid rig so its 139
clips play unretargeted. A frog cannot use one of them: an upright walk or a two-handed chop on four
folded legs is not a frog. Its clips are authored below, so its bones are a frog's. Their names and
parents are M7-05j's, which the prefab's mask and the tests read.

**The clips.** Poses are written as a body offset and a few joint angles, with each leg solved by
two-bone IK to a target, planted on the ground or carried with the body. In place, 30 fps, no root
motion (the root bone never moves), as KayKit's are:

    Idle     48 f  loop   breathing, a throat pulse, a glance
    Hop      18 f  loop   the locomotion: one hop, 0.9 m of ground at 1x, so a stride of 1.5 m/s
    Attack   36 f         a tongue lash: wind-up to frame 12, the tongue at full reach on frame 15
                          (0.5 s), which is EnemyAnimatorView's strike time
    Leap     30 f         the Lunger's dash: a crouch, the launch at frame 17, then held in flight
    Hit      12 f         a flinch
    Death    30 f         flips onto its back, belly up, legs in the air, by frame 15

The tongue is modelled out, 0.86 m past the lips, so the model's bind pose shows it; every clip draws
it back into the mouth except when it strikes.

Every colour obeys GD 16.4 (enemy_atlas.py): the reference sheet's orange eyes are crimson.
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
FLOOR = os.path.join(ROOT, "Assets", "_Project", "Art", "Environment", "Jungle", "Textures", "T_JungleGround_Albedo.png")

TRIANGLE_BUDGET = (400, 2500)  # GD 17.1, the crowd's
STAND_OFF = (0.05, 0.15)  # lighter and more saturated than the floor, averaged (FrogModelTests)
FPS = 30
SIDES = (("l", 1.0), ("r", -1.0))  # a frog faces -Y, so its left is +X


def v(x, y, z):
    return Vector((x, y, z))


# ---------------------------------------------------------------------------------------------
# The skeleton, in the model's own frame: Z up, facing -Y, 0.83 m to the top of the eyes. Each bone
# is (parent, head, tail); a child's head is its parent's tail wherever a limb bends.
# ---------------------------------------------------------------------------------------------

TONGUE_ROOT, TONGUE_TIP = v(0, -0.30, 0.44), v(0, -1.16, 0.43)


def limb_joints(s):
    """One side's joints: the foreleg reaches down and out to a pad under the jaw; the hind leg's
    thigh lies along the flank from hip to knee, the shin folds back under it, the foot runs forward."""
    return {
        "shoulder": v(0.24 * s, -0.15, 0.28), "elbow": v(0.35 * s, -0.21, 0.15),
        "wrist": v(0.33 * s, -0.33, 0.075), "fingers": v(0.35 * s, -0.47, 0.03),
        "hip": v(0.22 * s, 0.37, 0.23), "knee": v(0.40 * s, -0.02, 0.15),
        "ankle": v(0.36 * s, 0.36, 0.07), "toes": v(0.52 * s, 0.03, 0.03),
    }


def skeleton():
    bones = {
        "root": (None, v(0, 0, 0), v(0, 0, 0.12)),
        "body": ("root", v(0, 0.28, 0.26), v(0, 0.02, 0.36)),
        "spine": ("body", v(0, 0.02, 0.36), v(0, -0.14, 0.44)),
        "head": ("spine", v(0, -0.14, 0.44), v(0, -0.46, 0.60)),
        "jaw": ("head", v(0, -0.06, 0.45), v(0, -0.46, 0.41)),
        "tongue": ("head", TONGUE_ROOT, TONGUE_ROOT + v(0, -0.10, 0)),
        "tongue.tip": ("head", TONGUE_TIP, TONGUE_TIP + v(0, -0.08, 0)),
    }
    for side, s in SIDES:
        j = limb_joints(s)
        bones[f"upperarm.{side}"] = ("spine", j["shoulder"], j["elbow"])
        bones[f"lowerarm.{side}"] = (f"upperarm.{side}", j["elbow"], j["wrist"])
        bones[f"hand.{side}"] = (f"lowerarm.{side}", j["wrist"], j["fingers"])
        bones[f"thigh.{side}"] = ("body", j["hip"], j["knee"])
        bones[f"shin.{side}"] = (f"thigh.{side}", j["knee"], j["ankle"])
        bones[f"foot.{side}"] = (f"shin.{side}", j["ankle"], j["toes"])
    return bones


LIMBS = {  # chain of three, and the parent that carries it
    **{f"arm.{side}": ((f"upperarm.{side}", f"lowerarm.{side}", f"hand.{side}"), "spine") for side, _ in SIDES},
    **{f"leg.{side}": ((f"thigh.{side}", f"shin.{side}", f"foot.{side}"), "body") for side, _ in SIDES},
}


def build_armature():
    data = bpy.data.armatures.new("Rig_Frog")
    arm = bpy.data.objects.new("Rig_Frog", data)
    bpy.context.scene.collection.objects.link(arm)
    # KayKit's shape: Y-up data under an object turned 90 degrees about X, so the FBX's root node
    # imports into Unity unrotated and the frog faces +Z there.
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
    return arm


# ---------------------------------------------------------------------------------------------
# Shells. Each shape is (verts, faces) in a unit local space; a part places one, names its swatch and
# the bone it rides, rigidly unless its weights say otherwise.
# ---------------------------------------------------------------------------------------------


def blob(cuts, roundness=1.0):
    """A cube cut into a grid and pushed toward a sphere: roundness 1 is a sphere, lower is a pillow
    with flatter faces, as KayKit's heads are."""
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


def dome(n=8):
    """A spot: a centre, a ring at the surface and a ring sunk under it, so no edge ever shows."""
    verts = [v(0, 0, 1)] + [v(math.cos(2 * math.pi * i / n), math.sin(2 * math.pi * i / n), 0.4) for i in range(n)]
    verts += [v(1.25 * math.cos(2 * math.pi * i / n), 1.25 * math.sin(2 * math.pi * i / n), -2.5) for i in range(n)]
    faces = [[0, 1 + i, 1 + (i + 1) % n] for i in range(n)]
    faces += [[1 + i, 1 + n + i, 1 + n + (i + 1) % n, 1 + (i + 1) % n] for i in range(n)]
    return verts, faces


def capsule(a, b, r1, r2, segments=8, cap=2):
    """A tube from a to b with round ends, already in place: a limb segment."""
    axis = b - a
    fwd = axis.normalized()
    side = fwd.cross(v(0, 0, 1))
    if side.length < 1e-3:
        side = fwd.cross(v(1, 0, 0))
    side.normalize()
    up = side.cross(fwd).normalized()
    rings = [(a - fwd * r1 * math.sin(math.pi / 2 * i / cap), r1 * math.cos(math.pi / 2 * i / cap)) for i in range(cap, 0, -1)]
    rings += [(a, r1), (b, r2)]
    rings += [(b + fwd * r2 * math.sin(math.pi / 2 * i / cap), r2 * math.cos(math.pi / 2 * i / cap)) for i in range(1, cap + 1)]
    verts, index = [], []
    for c, r in rings:
        if r < 1e-4:
            index.append([len(verts)])
            verts.append(c)
            continue
        index.append(list(range(len(verts), len(verts) + segments)))
        verts += [c + side * (math.cos(2 * math.pi * s / segments) * r) + up * (math.sin(2 * math.pi * s / segments) * r) for s in range(segments)]
    faces = []
    for ra, rb in zip(index, index[1:]):
        for s in range(segments):
            if len(ra) == 1:
                faces.append([ra[0], rb[(s + 1) % segments], rb[s]])
            elif len(rb) == 1:
                faces.append([ra[s], ra[(s + 1) % segments], rb[0]])
            else:
                faces.append([ra[s], ra[(s + 1) % segments], rb[(s + 1) % segments], rb[s]])
    return verts, faces


def tongue_tube(root, tip, sides=6, rings=5):
    """A flat tube from the mouth to the tip, blended root-to-tip so moving the tip stretches it."""
    axis = tip - root
    fwd = axis.normalized()
    side = fwd.cross(v(0, 0, 1)).normalized()
    up = side.cross(fwd).normalized()
    verts, weights = [], []
    for i in range(rings):
        t = i / (rings - 1)
        p = root + axis * t
        width, height = 0.058 - 0.012 * t, 0.032 - 0.006 * t
        for k in range(sides):
            verts.append(p + side * (math.cos(2 * math.pi * k / sides) * width) + up * (math.sin(2 * math.pi * k / sides) * height))
            weights.append({"tongue": 1.0 - t, "tongue.tip": t} if 0.0 < t < 1.0 else ({"tongue": 1.0} if t == 0.0 else {"tongue.tip": 1.0}))
    faces = [[i * sides + k, i * sides + (k + 1) % sides, (i + 1) * sides + (k + 1) % sides, (i + 1) * sides + k]
             for i in range(rings - 1) for k in range(sides)]
    faces.append(list(reversed(range(sides))))
    faces.append(list(range((rings - 1) * sides, rings * sides)))
    return verts, faces, weights


def outward(verts, faces):
    """The faces of a closed shell wound so every normal points out. Done per shell, while each is
    still closed: once cull() has opened them, which way is out is a guess."""
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
    def __init__(self, verts, faces, swatch, weights, closed=True):
        self.verts = verts
        self.faces = outward(verts, faces) if closed else faces
        self.swatch = swatch
        # one dict for a rigid part, or one per vertex
        self.weights = weights if isinstance(weights, list) else [weights] * len(verts)
        self.bone = max(self.weights[0], key=self.weights[0].get)


PARTS = []


def place(shape, center, half, swatch, bone, rot=Quaternion(), warp=None, closed=True):
    """A unit shape scaled by `half`, warped in its own frame, turned by `rot` and moved to `center`."""
    verts, faces = shape
    out = []
    for p in verts:
        q = v(p.x * half[0], p.y * half[1], p.z * half[2])
        if warp:
            q = warp(q, p)
        out.append(center + rot @ q)
    PARTS.append(Part(out, faces, swatch, {bone: 1.0}, closed))
    return out


def pitch(deg):
    return Quaternion(v(1, 0, 0), math.radians(deg))


def yaw(deg):
    return Quaternion(v(0, 0, 1), math.radians(deg))


def facing(z_axis, up=v(0, 0, 1)):
    """The turn that takes local +Z to `z_axis`, with local +Y as near `up` as it can be."""
    z = z_axis.normalized()
    x = up.cross(z)
    if x.length < 1e-4:
        x = v(1, 0, 0)
    x.normalize()
    y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_quaternion()


def along(forward, up=v(0, 0, 1)):
    """The turn that takes local -Y to `forward`, with local +Z as near `up` as it can be: a shell laid
    along a bone that points the way the frog faces at rest."""
    y = -forward.normalized()
    x = y.cross(up).normalized()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed().to_quaternion()


def build():
    # The body: a small egg, nose up, fuller at the hips. The head is the bigger mass, as KayKit's are.
    def body_warp(q, p):
        return v(q.x * (1.0 + 0.10 * p.y), q.y, q.z * (1.0 + 0.06 * p.y))  # +y is the rear
    place(blob(3), v(0, 0.10, 0.31), (0.34, 0.36, 0.25), "frog_green", "body", pitch(-22), body_warp)
    # The belly: a cream shell that shows on the chest and throat.
    place(blob(2), v(0, -0.06, 0.24), (0.28, 0.30, 0.19), "frog_belly", "body", pitch(-28))

    # The head: a wide pillow, narrower at the snout; the jaw is a cream pillow under it. The mouth is
    # two dark shells, a roof on the head and a floor on the jaw, each hidden inside the other's shell
    # while the mouth is shut, so a dropped jaw opens dark rather than cream.
    def snout_warp(q, p):
        k = min(p.y, 0.0)  # -y is the snout
        return v(q.x * (1.0 + 0.14 * k), q.y, q.z * (1.0 + 0.10 * k))
    place(blob(3, 0.62), v(0, -0.26, 0.54), (0.43, 0.29, 0.165), "frog_green", "head", pitch(-6), snout_warp)
    place(blob(2, 0.66), v(0, -0.24, 0.415), (0.395, 0.27, 0.095), "frog_belly", "jaw", pitch(-6), snout_warp)
    place(blob(2, 0.7), v(0, -0.26, 0.40), (0.34, 0.225, 0.045), "frog_mouth", "head", pitch(-6), snout_warp)
    place(blob(2, 0.7), v(0, -0.25, 0.45), (0.345, 0.225, 0.07), "frog_mouth", "jaw", pitch(-6), snout_warp)

    # The eyes: a crimson ball bulging out of a green lid, a big dark pupil and a glint.
    for _, s in SIDES:
        socket = v(0.25 * s, -0.31, 0.64)
        place(blob(2), socket, (0.115, 0.115, 0.10), "frog_green", "head")
        look = v(0.50 * s, -0.78, 0.40).normalized()
        eye = socket + look * 0.02 + v(0, 0, 0.05)
        place(blob(2), eye, (0.13, 0.13, 0.13), "frog_red", "head")
        place(blob(1), eye + look * 0.116, (0.060, 0.074, 0.024), "frog_pupil", "head", facing(look))
        glint = eye + (look + v(-0.30 * s, 0, 0.85)).normalized() * 0.124
        place(ball(6, 4), glint, (0.027, 0.027, 0.022), "frog_shine", "head")

    for side, s in SIDES:
        j = limb_joints(s)
        # The foreleg: short and fat, down to a round pad with three toe balls.
        PARTS.append(Part(*capsule(j["shoulder"], j["elbow"], 0.095, 0.085), "frog_green", {f"upperarm.{side}": 1.0}))
        PARTS.append(Part(*capsule(j["elbow"], j["wrist"], 0.085, 0.07), "frog_green", {f"lowerarm.{side}": 1.0}))
        pad = v(0.34 * s, -0.37, 0.03)
        place(blob(1), pad, (0.085, 0.08, 0.03), "frog_green", f"hand.{side}", yaw(-12 * s))
        for angle in (-40, 0, 40):
            toward = yaw((angle - 12) * s) @ v(0, -1, 0)
            place(ball(6, 3), pad + toward * 0.095 + v(0, 0, 0.006), (0.042, 0.042, 0.034), "frog_belly", f"hand.{side}")
        # The hind leg: a big thigh laid from hip to knee, the shin folded back under it, and a long
        # foot forward from the ankle with four toe balls.
        hip, knee, ankle, toes = j["hip"], j["knee"], j["ankle"], j["toes"]
        place(blob(2), (hip + knee) / 2 + v(0, 0, 0.01), (0.16, 0.27, 0.17), "frog_green", f"thigh.{side}", along(knee - hip))
        PARTS.append(Part(*capsule(knee, ankle, 0.09, 0.075), "frog_green", {f"shin.{side}": 1.0}))
        heel = v(ankle.x + 0.04 * s, ankle.y - 0.03, 0.035)
        ground = v(toes.x - heel.x, toes.y - heel.y, 0)
        place(blob(1), (heel + toes) / 2, (0.085, 0.19, 0.034), "frog_green", f"foot.{side}", along(ground))
        for angle in (-42, -14, 14, 42):
            toward = yaw(angle) @ ground.normalized()
            place(ball(6, 3), v(toes.x, toes.y, 0.036) + toward * 0.075, (0.044, 0.044, 0.035), "frog_belly", f"foot.{side}")

    # The tongue, modelled out, and its tip.
    verts, faces, weights = tongue_tube(TONGUE_ROOT, TONGUE_TIP - (TONGUE_TIP - TONGUE_ROOT).normalized() * 0.03)
    PARTS.append(Part(verts, faces, "frog_tongue", weights))
    place(ball(8, 5), TONGUE_TIP, (0.078, 0.072, 0.052), "frog_tongue", "tongue.tip")


def spots():
    """Dark spots pressed into the back: shallow domes laid where a ray from above lands."""
    verts, polys = [], []
    for part in PARTS:
        if part.swatch == "frog_green" and part.bone in ("body", "head", "thigh.l", "thigh.r"):
            base = len(verts)
            verts += part.verts
            polys += [[base + i for i in f] for f in part.faces]
    tree = BVHTree.FromPolygons(verts, polys)
    for x, y, r, bone in ((0.10, 0.06, 0.085, "body"), (-0.14, 0.16, 0.07, "body"), (0.04, 0.30, 0.06, "body"),
                          (0.33, 0.20, 0.055, "thigh.l"), (-0.32, 0.10, 0.05, "thigh.r")):
        hit, normal, _, _ = tree.ray_cast(v(x, y, 3.0), v(0, 0, -1))
        assert hit is not None, f"no surface under the spot at {x}, {y}"
        if normal.z < 0:
            normal = -normal
        place(dome(), hit, (r, r * 0.85, 0.012), "frog_spot", bone, facing(normal, v(0, -1, 0)), closed=False)


def cull():
    """Drop every face buried inside another closed shell on the same bone: they move together, so no
    clip can ever bring it into view. Shells on different bones keep theirs; the jaw's top, under the
    head, is what shows when the mouth opens."""
    by_bone = defaultdict(list)
    for part in PARTS:
        by_bone[part.bone].append(part)
    removed = 0
    for parts in by_bone.values():
        closed = [(p, BVHTree.FromPolygons(p.verts, p.faces)) for p in parts if len(p.faces) > 20 and p.swatch != "frog_spot"]
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
# One mesh: every shell, smooth-shaded, each corner sampling its swatch by height, so the frog is
# light on top and darker toward the ground, as KayKit's gradient atlases shade theirs.
# ---------------------------------------------------------------------------------------------

TOP_Z = 0.84


def assemble(arm):
    mesh = bpy.data.meshes.new("Frog_Body")
    body = bpy.data.objects.new("Frog_Body", mesh)
    bpy.context.scene.collection.objects.link(body)
    for name in arm.data.bones.keys():
        body.vertex_groups.new(name=name)
    groups = {g.name: g.index for g in body.vertex_groups}
    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()
    uv = bm.loops.layers.uv.new("UVMap")
    for part in PARTS:
        made = []
        for co, weights in zip(part.verts, part.weights):
            vert = bm.verts.new(co)
            for name, w in weights.items():
                vert[deform][groups[name]] = w
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
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    return body


def into_armature_space(body, arm):
    """Where KayKit's own meshes sit: under the armature, identity transform, data in its space."""
    body.parent = arm
    body.data.transform(arm.matrix_world.inverted())
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_local = Matrix.Identity(4)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------------------------------------
# Posing. A pose names a body offset and rotation, a few joint angles, and a target per limb; this
# turns it into world matrices bone by bone, then into each pose bone's basis.
# ---------------------------------------------------------------------------------------------


def rot(pitch=0.0, roll=0.0, yaw=0.0):
    """Degrees, about the rest frame's axes. Pitch > 0 dips the nose; roll > 0 turns about the
    forward axis; yaw > 0 turns the nose toward +X."""
    return (Quaternion(v(0, 0, 1), math.radians(yaw)) @ Quaternion(v(0, 1, 0), math.radians(roll))
            @ Quaternion(v(1, 0, 0), math.radians(pitch)))


def about(p, q):
    return Matrix.Translation(p) @ q.to_matrix().to_4x4() @ Matrix.Translation(-p)


def ik2(a, target, l1, l2, pole):
    d = target - a
    dist = max(min(d.length, (l1 + l2) * 0.999), abs(l1 - l2) + 1e-4)
    dn = d.normalized()
    cos_a = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
    angle = math.acos(cos_a)
    p = pole - dn * pole.dot(dn)
    if p.length < 1e-6:
        p = dn.orthogonal()
    p.normalize()
    return a + (dn * math.cos(angle) + p * math.sin(angle)) * l1, a + dn * dist


class Poser:
    def __init__(self, arm):
        self.arm = arm
        self.A = arm.matrix_world.copy()
        self.rest = {b.name: self.A @ b.matrix_local for b in arm.data.bones}
        self.head = {n: m.to_translation() for n, m in self.rest.items()}
        self.tail = {b.name: self.A @ b.tail_local for b in arm.data.bones}
        self.order = [b.name for b in arm.data.bones]  # parents before children
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
        self.poles = {}
        for limb, (chain, _) in LIMBS.items():
            a, k, e = self.head[chain[0]], self.head[chain[1]], self.head[chain[2]]
            line = (e - a).normalized()
            self.poles[limb] = (k - a) - line * (k - a).dot(line)

    def world(self, pose):
        W = {"root": self.rest["root"]}
        carry = {"root": Matrix.Identity(4)}

        def fk(name, parent, q=Quaternion(), scale=None, extra=None):
            m = carry[parent] @ about(self.head[name], q) @ self.rest[name]
            if extra is not None:
                m = extra @ m
            if scale is not None:
                m = m @ Matrix.Diagonal((scale, scale, scale, 1.0))
            W[name] = m
            carry[name] = m @ self.rest[name].inverted()

        body = Matrix.Translation(pose.get("offset", v(0, 0, 0))) @ about(self.head["body"], rot(*pose.get("body", (0, 0, 0))))
        W["body"] = body @ self.rest["body"]
        carry["body"] = body
        fk("spine", "body", rot(pose.get("spine", 0.0)))
        fk("head", "spine", rot(pose.get("head", 0.0), 0.0, pose.get("yaw", 0.0)))
        fk("jaw", "head", rot(pose.get("jaw", 0.0)), scale=pose.get("throat", 1.0))
        fk("tongue", "head")
        reach = pose.get("tongue", 0.0)
        root_c, tip_c = carry["head"] @ self.head["tongue"], carry["head"] @ self.head["tongue.tip"]
        fk("tongue.tip", "head", scale=0.6 + 0.4 * reach, extra=Matrix.Translation((root_c - tip_c) * (1.0 - reach)))
        for limb, (chain, parent) in LIMBS.items():
            self.limb(W, carry, pose.get(limb), chain, parent, limb)
        return W

    def limb(self, W, carry, spec, chain, parent, limb):
        """spec: (mode, joint_delta, end_pitch) — mode "w" plants the wrist or ankle in the world at
        its rest place plus delta, "c" carries it with the parent; end_pitch turns the hand or foot
        about the side axis, in degrees, from its rest direction."""
        mode, delta, end_pitch = spec if spec else ("c", v(0, 0, 0), 0.0)
        upper, lower, end = chain
        C = carry[parent]
        hip = C @ self.head[upper]
        rest_joint = self.head[end]
        rest_end_vec = self.tail[end] - self.head[end]
        end_vec = rot(end_pitch).to_matrix() @ rest_end_vec
        if mode == "w":
            target = rest_joint + delta
        else:
            target = C @ (rest_joint + delta)
            end_vec = C.to_3x3() @ end_vec
        l1 = (self.head[lower] - self.head[upper]).length
        l2 = (self.head[end] - self.head[lower]).length
        knee, joint = ik2(hip, target, l1, l2, C.to_3x3() @ self.poles[limb])
        m = C @ self.rest[upper]
        m = about(hip, (m.to_3x3() @ v(0, 1, 0)).rotation_difference(knee - hip)) @ m
        W[upper] = m
        c_up = m @ self.rest[upper].inverted()
        m = c_up @ self.rest[lower]
        m = about(knee, (m.to_3x3() @ v(0, 1, 0)).rotation_difference(joint - knee)) @ m
        W[lower] = m
        c_low = m @ self.rest[lower].inverted()
        m = c_low @ self.rest[end]
        m = about(joint, (m.to_3x3() @ v(0, 1, 0)).rotation_difference(end_vec)) @ m
        W[end] = m

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
                Bp = bones[p].matrix_local
                basis = (Bp.inverted() @ B).inverted() @ M[p].inverted() @ M[n]
            self.arm.pose.bones[n].matrix_basis = basis


def planted():
    return {**{f"arm.{s}": ("w", v(0, 0, 0), 0.0) for s, _ in SIDES}, **{f"leg.{s}": ("w", v(0, 0, 0), 0.0) for s, _ in SIDES}}


def mirror(spec_l):
    """A spec written for the left limb, and the same for the right."""
    mode, delta, pitch_ = spec_l
    return spec_l, (mode, v(-delta.x, delta.y, delta.z), pitch_)


def limbs(arm=None, leg=None):
    out = {}
    if arm:
        out["arm.l"], out["arm.r"] = mirror(arm)
    if leg:
        out["leg.l"], out["leg.r"] = mirror(leg)
    return out


def P(base=None, **kw):
    pose = dict(planted())
    if base:
        pose.update(base)
    for k, val in kw.items():
        pose[k] = val
    return pose


def clips():
    rest = P()
    tuck = lambda d, pitch_: ("c", d, pitch_)  # noqa: E731
    return {
        "Idle": (True, {
            0: rest,
            10: P(offset=v(0, 0, -0.004), throat=1.04),
            16: P(offset=v(0, 0, -0.002)),
            24: P(offset=v(0, 0, 0.004), yaw=5.0, head=-2.0),
            34: P(offset=v(0, 0, 0.002), yaw=2.0, throat=1.03),
            40: P(offset=v(0, 0, 0.0)),
            48: rest,
        }),
        "Hop": (True, {
            0: P(offset=v(0, 0.02, -0.04), body=(4, 0, 0), head=-4.0),
            3: P(limbs(arm=tuck(v(0, -0.06, 0.05), -20), leg=("w", v(0, 0.04, 0.10), 40)), offset=v(0, -0.06, 0.08), body=(-16, 0, 0), head=4.0),
            6: P(limbs(arm=tuck(v(0, -0.12, 0.06), -30), leg=tuck(v(-0.08, 0.40, 0.02), 160)), offset=v(0, -0.05, 0.17), body=(-8, 0, 0)),
            9: P(limbs(arm=tuck(v(0, -0.10, -0.04), 0), leg=tuck(v(-0.05, 0.25, 0.05), 90)), offset=v(0, -0.02, 0.12), body=(6, 0, 0)),
            12: P(limbs(arm=("w", v(0, -0.03, 0), 0), leg=tuck(v(0, 0.08, 0.06), 30)), offset=v(0, 0, 0.01), body=(10, 0, 0), head=-6.0),
            15: P(limbs(arm=("w", v(0, -0.03, 0), 0)), offset=v(0, 0.02, -0.05), body=(5, 0, 0), head=-4.0),
            18: P(offset=v(0, 0.02, -0.04), body=(4, 0, 0), head=-4.0),
        }),
        "Attack": (False, {
            0: rest,
            9: P(offset=v(0, 0.05, -0.04), body=(-10, 0, 0), spine=-4.0, head=-8.0, throat=1.05, jaw=4.0),
            12: P(offset=v(0, 0.06, -0.05), body=(-12, 0, 0), spine=-4.0, head=-10.0, throat=1.06, jaw=6.0),
            15: P(offset=v(0, -0.10, -0.01), body=(12, 0, 0), spine=4.0, head=6.0, jaw=26.0, tongue=1.0),
            18: P(offset=v(0, -0.08, -0.01), body=(10, 0, 0), spine=3.0, head=5.0, jaw=24.0, tongue=0.95),
            22: P(offset=v(0, -0.04, 0.0), body=(4, 0, 0), jaw=12.0, tongue=0.15),
            26: P(offset=v(0, -0.02, 0.0), body=(2, 0, 0), jaw=0.0),
            36: rest,
        }),
        "Leap": (False, {
            0: rest,
            10: P(offset=v(0, 0.04, -0.08), body=(-8, 0, 0), head=-6.0, throat=1.04),
            14: P(offset=v(0, 0.05, -0.09), body=(-10, 0, 0), head=-8.0, throat=1.05),
            17: P(limbs(arm=tuck(v(0, -0.15, 0.10), -40), leg=("w", v(0, 0.06, 0.12), 50)), offset=v(0, -0.10, 0.14), body=(-22, 0, 0), jaw=6.0),
            21: P(limbs(arm=tuck(v(0.02, -0.16, 0.04), -50), leg=tuck(v(-0.10, 0.45, 0.0), 165)), offset=v(0, -0.08, 0.22), body=(-12, 0, 0), jaw=8.0),
            30: P(limbs(arm=tuck(v(0.02, -0.16, 0.03), -50), leg=tuck(v(-0.10, 0.46, 0.01), 165)), offset=v(0, -0.08, 0.22), body=(-10, 0, 0), jaw=8.0),
        }),
        "Hit": (False, {
            0: rest,
            3: P(offset=v(0, 0.05, 0.02), body=(-12, 0, 0), spine=-4.0, head=-12.0, jaw=12.0),
            7: P(offset=v(0, 0.02, 0.0), body=(-4, 0, 0), head=-4.0, jaw=4.0),
            12: rest,
        }),
        "Death": (False, {
            0: rest,
            5: P(limbs(arm=tuck(v(0.08, -0.05, 0.0), -20), leg=tuck(v(0.10, 0.20, -0.05), 60)), offset=v(0, 0, 0.22), body=(-10, 60, 0), jaw=16.0),
            11: P(limbs(arm=tuck(v(0.10, -0.02, 0.04), -10), leg=tuck(v(0.12, 0.25, -0.02), 80)), offset=v(0, 0, 0.30), body=(-6, 150, 0), jaw=18.0),
            15: P(limbs(arm=tuck(v(0.04, 0, 0.02), 0), leg=tuck(v(0.05, 0.08, 0.02), 30)), offset=v(0, 0, 0.26), body=(0, 180, 0), jaw=18.0, tongue=0.3),
            19: P(limbs(arm=tuck(v(0.04, 0, 0.02), 0), leg=tuck(v(0.05, 0.08, 0.02), 30)), offset=v(0, 0, 0.29), body=(0, 180, 0), jaw=20.0, tongue=0.35),
            24: P(limbs(arm=tuck(v(0, 0, 0), 0), leg=tuck(v(0, 0, 0), 0)), offset=v(0, 0, 0.27), body=(0, 180, 0), jaw=20.0, tongue=0.4),
            30: P(limbs(arm=tuck(v(0, 0, 0), 0), leg=tuck(v(0, 0, 0), 0)), offset=v(0, 0, 0.27), body=(0, 180, 0), jaw=20.0, tongue=0.4),
        }),
    }


def author_clips(arm):
    poser = Poser(arm)
    arm.animation_data_create()
    made = []
    for name, (loop, keys) in clips().items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        last = {}
        for frame in sorted(keys):
            poser.apply(keys[frame])
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


# ---------------------------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------------------------


def stand_off(body, atlas):
    """FrogModelTests' last row, run here first: the surface's mean colour against the floor's."""
    floor = bpy.data.images.load(FLOOR)
    px = np.empty(len(floor.pixels), np.float32)
    floor.pixels.foreach_get(px)
    px = px.reshape(-1, 4)[:, :3]
    bpy.data.images.remove(floor)

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
    frog, ground = (lum / area, sat / area), (float(luma(px).mean()), float(saturation(px).mean()))
    print(f"  colour: luma {frog[0]:.3f} and saturation {frog[1]:.3f}, on a floor of {ground[0]:.3f} and {ground[1]:.3f}")
    assert frog[0] >= ground[0] + STAND_OFF[0] and frog[1] >= ground[1] + STAND_OFF[1], "the Frog does not stand off the Jungle's floor"


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
    stand_off(body, atlas)
    zs = [vert.co.z for vert in mesh.vertices]
    print(f"  Frog: {tris} triangles, {len(mesh.vertices)} vertices, {min(zs):.3f}-{max(zs):.3f} m tall")


def export(arm, body):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.fps = FPS
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, "Frog.fbx"),
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
# The pose sheet: the exported model, reloaded, in every clip, over the Jungle's floor colour.
# ---------------------------------------------------------------------------------------------

CELL = 288
SHEET = {
    "Idle": (0, 10, 24, 34),
    "Hop": (0, 3, 6, 9, 12, 15),
    "Attack": (0, 9, 12, 15, 18, 24),
    "Leap": (0, 10, 14, 17, 21, 30),
    "Hit": (0, 3, 7, 12),
    "Death": (0, 5, 11, 15, 19, 30),
}


def pose_sheet(path):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, "Frog.fbx"))
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
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_object_outline = True
    scene.display.shading.show_shadows = True
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
        "three_quarter": (v(-3.2, -3.4, 2.2), v(0, -0.15, 0.45), 1.9),
        "side": (v(5, 0, 0.5), v(0, -0.1, 0.5), 2.1),
        "game": (v(0, -math.cos(pitch_r) * 6, math.sin(pitch_r) * 6 + 0.4), v(0, -0.3, 0.4), 2.0),
    }
    width = max(len(f) for f in SHEET.values())
    rows = []
    arm.animation_data_create()
    for clip, frames in SHEET.items():
        action = actions[clip]
        arm.animation_data.action = action
        for view in (("three_quarter", "side") if clip in ("Hop", "Leap", "Attack", "Death") else ("three_quarter",)):
            aim(*views[view])
            cells = []
            for f in frames:
                scene.frame_set(int(f))
                cells.append(render())
            while len(cells) < width:
                cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
            rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {clip}")
    # The game camera's view, and the silhouette beside a capsule, as the Rootling's sheet has.
    cells = []
    for clip, f in (("Idle", 0), ("Hop", 6), ("Attack", 12), ("Attack", 15), ("Death", 30)):
        arm.animation_data.action = actions[clip]
        scene.frame_set(f)
        aim(*views["game"])
        cells.append(render())
    arm.animation_data.action = actions["Idle"]
    scene.frame_set(0)
    floor.hide_render = True
    scene.display.shading.light = "FLAT"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0, 0, 0)
    scene.display.shading.show_object_outline = False
    scene.display.shading.show_shadows = False
    scene.world = scene.world or bpy.data.worlds.new("sheet")
    scene.world.color = (1, 1, 1)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.4, depth=1.6, location=(1.3, 0, 0.8))
    aim(v(0.65, -math.cos(pitch_r) * 8, math.sin(pitch_r) * 8 + 0.5), v(0.65, 0, 0.5), 3.4)
    cells.append(render())
    rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("frog.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
                 "FBX's object ids (rootling.py's docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    print("atlas")
    atlas = build_atlas(TEX)
    print("model")
    arm = build_armature()
    build()
    spots()
    cull()
    body = assemble(arm)
    check(body, atlas)
    into_armature_space(body, arm)
    print("clips")
    author_clips(arm)
    export(arm, body)
    if "--pose-sheet" in args:
        pose_sheet(os.path.abspath(args[args.index("--pose-sheet") + 1]))  # Blender would not read it from here


main()
