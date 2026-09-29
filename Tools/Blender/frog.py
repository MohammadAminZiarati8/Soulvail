"""The Jungle Frog: the owner's Tripo sculpt cut to the crowd's budget, repainted from their Gemini
reference sheet, rigged on a skeleton of its own and animated here.

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/frog.py [-- --source <tripo.fbx>] [--pose-sheet <out.png>]

It writes, overwriting what it wrote last time, so both files are an output of this script:

- Assets/_Project/Art/Enemies/Frog.fbx                    one skinned mesh on Rig_Frog, with its clips
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas (enemy_atlas.py)

**The source is not in the repository.** It is the 501 600-triangle Tripo export the owner made, at
SOURCE below unless --source names another copy. Only its shape is kept: its texture is a mustard
bake with grey holes, so the frog is repainted from the atlas face by face, which is also what gives
it the reference sheet's faceted camo.

**Why a rig of its own, not Rig_Medium.** Every other body wears KayKit's humanoid rig so its 139
clips play unretargeted. A frog cannot use one of them: an upright walk or a two-handed chop on four
folded legs is not a frog. Its clips are authored below, so its bones are a frog's.

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

Every colour obeys GD 16.4 (enemy_atlas.py): the reference sheet's orange eyes and toes are crimson.
"""

import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True  # no __pycache__ beside the scripts
from enemy_atlas import SWATCHES, build_atlas, save_png, uv_for  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Enemies")
TEX = os.path.join(OUT, "Textures")
SOURCE = "C:/Programing/Unity/Resources/Soulvail/Models/low+poly+frog+3d/tripo_convert_4a7ccfa9-9435-4da8-8679-d6fad3eb57f0.fbx"

TRIANGLE_BUDGET = (400, 1200)  # GD 17.1, the crowd's
BODY_TRIANGLES = 920  # what the sculpt is cut to; the eyes and the tongue take the rest
FPS = 30
SIDES = (("l", 1.0), ("r", -1.0))  # a frog faces -Y, so its left is +X

# ---------------------------------------------------------------------------------------------
# The skeleton, in the sculpt's own frame: Z up, facing -Y, 0.84 m to the top of the eyes. Each bone
# is (parent, head, tail); a child's head is its parent's tail wherever a limb bends.
# ---------------------------------------------------------------------------------------------


def v(x, y, z):
    return Vector((x, y, z))


def skeleton():
    bones = {
        "root": (None, v(0, 0, 0), v(0, 0, 0.12)),
        "body": ("root", v(0, 0.30, 0.28), v(0, 0.02, 0.40)),
        "spine": ("body", v(0, 0.02, 0.40), v(0, -0.18, 0.50)),
        "head": ("spine", v(0, -0.18, 0.50), v(0, -0.45, 0.64)),
        "jaw": ("head", v(0, -0.16, 0.50), v(0, -0.46, 0.54)),
        "tongue": ("head", v(0, -0.30, 0.57), v(0, -0.40, 0.57)),
        "tongue.tip": ("head", v(0, -1.16, 0.54), v(0, -1.24, 0.54)),
    }
    for side, s in SIDES:
        shoulder, elbow, wrist, fingers = v(0.16 * s, -0.16, 0.38), v(0.24 * s, -0.18, 0.18), v(0.26 * s, -0.22, 0.04), v(0.28 * s, -0.40, 0.01)
        bones[f"upperarm.{side}"] = ("spine", shoulder, elbow)
        bones[f"lowerarm.{side}"] = (f"upperarm.{side}", elbow, wrist)
        bones[f"hand.{side}"] = (f"lowerarm.{side}", wrist, fingers)
        hip, knee, ankle, toes = v(0.14 * s, 0.30, 0.25), v(0.40 * s, 0.12, 0.25), v(0.36 * s, 0.38, 0.08), v(0.46 * s, 0.14, 0.02)
        bones[f"thigh.{side}"] = ("body", hip, knee)
        bones[f"shin.{side}"] = (f"thigh.{side}", knee, ankle)
        bones[f"foot.{side}"] = (f"shin.{side}", ankle, toes)
    return bones


NON_DEFORM = ("root", "tongue", "tongue.tip")  # kept out of bone heat; the tongue is weighted by hand
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
    for name in NON_DEFORM:
        data.bones[name].use_deform = False
    return arm


# ---------------------------------------------------------------------------------------------
# The mesh: the sculpt, welded and cut with symmetry, then eyes and a tongue of our own.
# ---------------------------------------------------------------------------------------------


def load_sculpt(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    body = next(o for o in new if o.type == "MESH")
    for obj in new:
        if obj is not body:
            bpy.data.objects.remove(obj)
    body.data.transform(body.matrix_world)
    body.matrix_world = Matrix.Identity(4)
    body.data.materials.clear()
    while body.data.uv_layers:  # Tripo's own bake; paint() writes the atlas's
        body.data.uv_layers.remove(body.data.uv_layers[0])
    for image in list(bpy.data.images):
        bpy.data.images.remove(image)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(body.data)
    bm.free()
    decimate = body.modifiers.new("cut", "DECIMATE")
    decimate.decimate_type = "COLLAPSE"
    decimate.ratio = BODY_TRIANGLES / len(body.data.polygons)
    decimate.use_symmetry = True
    decimate.symmetry_axis = "X"
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier="cut")
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    body.name = body.data.name = "Frog_Body"
    return body


def heat_weights(body, arm):
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for name in NON_DEFORM:
        if name != "root":
            arm.data.bones[name].use_deform = True
    for name in arm.data.bones.keys():
        if body.vertex_groups.get(name) is None:
            body.vertex_groups.new(name=name)


class Parts:
    """Closed low-poly pieces added to the body's bmesh, each weighted as it is built."""

    def __init__(self, bm, groups):
        self.bm = bm
        self.deform = bm.verts.layers.deform.verify()
        self.part = bm.faces.layers.int.get("part") or bm.faces.layers.int.new("part")
        self.groups = groups

    def vert(self, co, weights):
        vert = self.bm.verts.new(co)
        for name, w in weights.items():
            vert[self.deform][self.groups[name]] = w
        return vert

    def ellipsoid(self, center, axes, segments, rings, part, weights):
        """Pole to pole along axes[2]; `axes` are three scaled, orthogonal vectors."""
        a, b, c = axes
        top = self.vert(center + c, weights)
        bottom = self.vert(center - c, weights)
        rows = []
        for r in range(1, rings):
            phi = math.pi * r / rings
            ring = []
            for s in range(segments):
                theta = 2 * math.pi * s / segments + (math.pi / segments if r % 2 else 0.0)
                ring.append(self.vert(center + a * (math.sin(phi) * math.cos(theta)) + b * (math.sin(phi) * math.sin(theta)) + c * math.cos(phi), weights))
            rows.append(ring)
        faces = []
        n = segments
        for s in range(n):
            faces.append(self.bm.faces.new([top, rows[0][s], rows[0][(s + 1) % n]]))
            faces.append(self.bm.faces.new([bottom, rows[-1][(s + 1) % n], rows[-1][s]]))
        for ra, rb in zip(rows, rows[1:]):
            for s in range(n):
                faces.append(self.bm.faces.new([ra[s], rb[s], rb[(s + 1) % n]]))
                faces.append(self.bm.faces.new([ra[s], rb[(s + 1) % n], ra[(s + 1) % n]]))
        self.finish(faces, part)

    def tongue(self, root, tip, sides, rings):
        """A flat tube from the mouth to the tip, blended root-to-tip so moving the tip stretches it."""
        axis = (tip - root)
        fwd = axis.normalized()
        side = fwd.cross(v(0, 0, 1)).normalized()
        up = side.cross(fwd).normalized()
        loops = []
        for i in range(rings):
            t = i / (rings - 1)
            p = root + axis * t
            width, height = 0.055 - 0.012 * t, 0.030 - 0.006 * t
            w = {"tongue.tip": t, "tongue": 1.0 - t} if 0.0 < t < 1.0 else ({"tongue": 1.0} if t == 0.0 else {"tongue.tip": 1.0})
            loops.append([self.vert(p + side * (math.cos(2 * math.pi * k / sides) * width) + up * (math.sin(2 * math.pi * k / sides) * height), w) for k in range(sides)])
        faces = []
        for la, lb in zip(loops, loops[1:]):
            for k in range(sides):
                faces.append(self.bm.faces.new([la[k], la[(k + 1) % sides], lb[(k + 1) % sides], lb[k]]))
        faces.append(self.bm.faces.new(list(reversed(loops[0]))))
        faces.append(self.bm.faces.new(loops[-1]))
        self.finish(faces, 3)

    def finish(self, faces, part):
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        tris = bmesh.ops.triangulate(self.bm, faces=faces)["faces"]
        for f in tris:
            f[self.part] = part


EYE_RADIUS = 0.115


def eye_frame(s):
    """The eye's centre and its axes: it looks out, forward and a little up."""
    center = v(0.20 * s, -0.295, 0.75)
    look = v(0.45 * s, -1.0, 0.25).normalized()
    side = look.cross(v(0, 0, 1)).normalized()
    up = side.cross(look).normalized()
    return center, look, side, up


def add_parts(body):
    groups = {g.name: g.index for g in body.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(body.data)
    parts = Parts(bm, groups)
    for f in bm.faces:
        f[parts.part] = 0
    for _, s in SIDES:
        center, look, side, up = eye_frame(s)
        # The eye: a crimson ball with its pole on the look axis, rigid on the head.
        parts.ellipsoid(center, (side * EYE_RADIUS, up * EYE_RADIUS, look * EYE_RADIUS), 8, 5, 1, {"head": 1.0})
        # The pupil: a black vertical slit standing proud of the eye's front.
        slit = center + look * (EYE_RADIUS * 0.94)
        parts.ellipsoid(slit, (side * 0.022, look * 0.016, up * 0.072), 6, 3, 2, {"head": 1.0})
    bones = skeleton()
    root, tip = bones["tongue"][1], bones["tongue.tip"][1]
    parts.tongue(root, tip - (tip - root).normalized() * 0.03, 5, 5)
    parts.ellipsoid(tip, (v(0.075, 0, 0), v(0, 0.07, 0), v(0, 0, 0.05)), 6, 4, 3, {"tongue.tip": 1.0})
    bm.to_mesh(body.data)
    bm.free()


# ---------------------------------------------------------------------------------------------
# Weights: bone heat for the sculpt, then the jaw cut along the mouth, and every vertex cleaned to at
# most four bones summing to one.
# ---------------------------------------------------------------------------------------------


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def mouth_z(y):
    """The lip line down the side of the face: 0.60 m at the snout, 0.55 m under the eye."""
    return 0.60 + (min(max(y, -0.5), -0.2) + 0.5) * (-0.05 / 0.3)


def clean_weights(body):
    names = [g.name for g in body.vertex_groups]
    index = {n: i for i, n in enumerate(names)}
    bones = skeleton()
    bm = bmesh.new()
    bm.from_mesh(body.data)
    deform = bm.verts.layers.deform.verify()
    part = bm.faces.layers.int.get("part")
    body_verts = set()
    for f in bm.faces:
        if f[part] == 0:
            body_verts.update(f.verts)
    for vert in bm.verts:
        w = {names[i]: x for i, x in vert[deform].items() if x > 1e-4}
        if vert in body_verts:
            if not w:  # heat found no bone: take the nearest one's
                best = min((n for n in bones if n not in NON_DEFORM), key=lambda n: distance_to_bone(vert.co, bones[n]))
                w = {best: 1.0}
            # The jaw: whatever head and jaw share is split along the lip line, so the mouth opens
            # there and nowhere else.
            share = w.pop("head", 0.0) + w.pop("jaw", 0.0)
            if share > 0.0:
                below = smoothstep(0.0, 0.035, mouth_z(vert.co.y) - vert.co.z) * smoothstep(-0.10, -0.22, vert.co.y)
                if below > 0.0:
                    w["jaw"] = share * below
                if below < 1.0:
                    w["head"] = share * (1.0 - below)
        top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
        top = [(n, x) for n, x in top if x >= 0.02] or top[:1]
        total = sum(x for _, x in top)
        vert[deform].clear()
        for n, x in top:
            vert[deform][index[n]] = x / total
    bm.to_mesh(body.data)
    bm.free()


def distance_to_bone(p, bone):
    _, a, b = bone
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length


# ---------------------------------------------------------------------------------------------
# Paint: one swatch per face, as the reference sheet's facets are one colour each.
# ---------------------------------------------------------------------------------------------

LIMB_BONES = {n for chain, _ in LIMBS.values() for n in chain}
CAMO = ("frog_lime",) * 4 + ("frog_dark",) * 3 + ("frog_spot",) * 3


def paint(body):
    names = [g.name for g in body.vertex_groups]
    rng = random.Random(4127)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    deform = bm.verts.layers.deform.verify()
    part = bm.faces.layers.int.get("part")
    uv = bm.loops.layers.uv.new("UVMap")
    bm.normal_update()

    def owner(f):
        total = {}
        for vert in f.verts:
            for i, x in vert[deform].items():
                total[names[i]] = total.get(names[i], 0.0) + x
        return max(total, key=total.get)

    skin = [f for f in bm.faces if f[part] == 0]
    blobs = []
    for _ in range(30):
        f = rng.choice(skin)
        blobs.append((f.calc_center_median(), rng.uniform(0.05, 0.11), rng.choice(CAMO)))
    counts = {}
    for f in bm.faces:
        c = f.calc_center_median()
        n = f.normal
        t = 0.5
        if f[part] == 1:
            _, s = SIDES[0] if c.x > 0 else SIDES[1]
            center, look, side, up = eye_frame(s)
            swatch, t = "frog_red", 0.5 - 0.5 * (c - center).normalized().dot(up)
        elif f[part] == 2:
            swatch, t = "frog_pupil", 0.5
        elif f[part] == 3:
            swatch, t = "frog_tongue", 0.3 + 0.4 * (1.0 if n.z < 0 else 0.0)
        else:
            bone = owner(f)
            limb = bone in LIMB_BONES
            t = min(0.95, max(0.05, (0.84 - c.z) / 0.84))
            if limb and (c.z < 0.03 or (bone.startswith(("hand.", "foot.")) and c.z < 0.065)):
                swatch, t = "frog_red", 0.25  # the toe pads
            elif not limb and ((c.y < 0.12 and n.y < -0.15 and n.z < 0.45 and abs(c.x) < 0.30 and 0.05 < c.z < mouth_z(c.y) - 0.01)
                               or (n.z < -0.4 and c.z < 0.4 and abs(c.x) < 0.32)):
                swatch = "frog_belly"
                t = min(0.9, max(0.0, (0.6 - c.z) / 0.6))
            else:
                swatch = "frog_green"
                for center, radius, colour in blobs:
                    if (c - center).length < radius:
                        swatch = colour
                        break
                roll = rng.random()
                if roll < 0.10:
                    swatch = "frog_lime"
                elif roll < 0.16:
                    swatch = "frog_dark"
        counts[swatch] = counts.get(swatch, 0) + 1
        for loop in f.loops:
            loop[uv].uv = uv_for(swatch, t)
    bm.faces.layers.int.remove(part)
    bm.to_mesh(body.data)
    bm.free()
    for p in body.data.polygons:
        p.use_smooth = False
    body.data.materials.append(bpy.data.materials.new("enemy"))
    print("  paint:", ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))


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
    mode, delta, pitch = spec_l
    return spec_l, (mode, v(-delta.x, delta.y, delta.z), pitch)


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
    tuck = lambda d, pitch: ("c", d, pitch)  # noqa: E731
    return {
        "Idle": (True, {
            0: rest,
            10: P(offset=v(0, 0, -0.004), throat=1.12),
            16: P(offset=v(0, 0, -0.002)),
            24: P(offset=v(0, 0, 0.004), yaw=5.0, head=-2.0),
            34: P(offset=v(0, 0, 0.002), yaw=2.0, throat=1.10),
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
            9: P(offset=v(0, 0.05, -0.04), body=(-10, 0, 0), spine=-4.0, head=-8.0, throat=1.18, jaw=4.0),
            12: P(offset=v(0, 0.06, -0.05), body=(-12, 0, 0), spine=-4.0, head=-10.0, throat=1.2, jaw=6.0),
            15: P(offset=v(0, -0.10, -0.01), body=(12, 0, 0), spine=4.0, head=6.0, jaw=22.0, tongue=1.0),
            18: P(offset=v(0, -0.08, -0.01), body=(10, 0, 0), spine=3.0, head=5.0, jaw=20.0, tongue=0.95),
            22: P(offset=v(0, -0.04, 0.0), body=(4, 0, 0), jaw=10.0, tongue=0.15),
            26: P(offset=v(0, -0.02, 0.0), body=(2, 0, 0), jaw=0.0),
            36: rest,
        }),
        "Leap": (False, {
            0: rest,
            10: P(offset=v(0, 0.04, -0.08), body=(-8, 0, 0), head=-6.0, throat=1.1),
            14: P(offset=v(0, 0.05, -0.09), body=(-10, 0, 0), head=-8.0, throat=1.12),
            17: P(limbs(arm=tuck(v(0, -0.15, 0.10), -40), leg=("w", v(0, 0.06, 0.12), 50)), offset=v(0, -0.10, 0.14), body=(-22, 0, 0), jaw=6.0),
            21: P(limbs(arm=tuck(v(0.02, -0.16, 0.04), -50), leg=tuck(v(-0.10, 0.45, 0.0), 165)), offset=v(0, -0.08, 0.22), body=(-12, 0, 0), jaw=8.0),
            30: P(limbs(arm=tuck(v(0.02, -0.16, 0.03), -50), leg=tuck(v(-0.10, 0.46, 0.01), 165)), offset=v(0, -0.08, 0.22), body=(-10, 0, 0), jaw=8.0),
        }),
        "Hit": (False, {
            0: rest,
            3: P(offset=v(0, 0.05, 0.02), body=(-12, 0, 0), spine=-4.0, head=-12.0, jaw=10.0),
            7: P(offset=v(0, 0.02, 0.0), body=(-4, 0, 0), head=-4.0, jaw=3.0),
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


def check(body):
    mesh = body.data
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    assert TRIANGLE_BUDGET[0] <= tris <= TRIANGLE_BUDGET[1], f"{tris} triangles is outside GD 17.1's crowd budget"
    names = [g.name for g in body.vertex_groups]
    for vert in mesh.vertices:
        groups = [(names[g.group], g.weight) for g in vert.groups if g.weight > 0]
        assert groups, f"vertex {vert.index} is unweighted"
        assert len(groups) <= 4 and abs(sum(w for _, w in groups) - 1.0) < 1e-3, f"vertex {vert.index}: {groups}"
        assert not any(n == "root" for n, _ in groups), f"vertex {vert.index} rides the root"
    zs = [(body.matrix_world @ vert.co).z for vert in mesh.vertices]
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

    pitch = math.radians(57)
    views = {
        "three_quarter": (v(-3.2, -3.4, 2.2), v(0, -0.15, 0.45), 1.9),
        "side": (v(5, 0, 0.5), v(0, -0.1, 0.5), 2.1),
        "game": (v(0, -math.cos(pitch) * 6, math.sin(pitch) * 6 + 0.4), v(0, -0.3, 0.4), 2.0),
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
    aim(v(0.65, -math.cos(pitch) * 8, math.sin(pitch) * 8 + 0.5), v(0.65, 0, 0.5), 3.4)
    cells.append(render())
    rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("frog.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
                 "FBX's object ids (rootling.py's docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    source = args[args.index("--source") + 1] if "--source" in args else SOURCE
    if not os.path.exists(source):
        sys.exit(f"frog.py: the Tripo sculpt is not at {source}; pass --source <path>.")
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    print("atlas")
    build_atlas(TEX)
    print("model")
    body = load_sculpt(source)
    arm = build_armature()
    heat_weights(body, arm)
    add_parts(body)
    clean_weights(body)
    paint(body)
    into_armature_space(body, arm)
    check(body)
    print("clips")
    author_clips(arm)
    export(arm, body)
    if "--pose-sheet" in args:
        pose_sheet(args[args.index("--pose-sheet") + 1])


main()
