"""The Jungle's Spitter, a pitcher plant in KayKit's style: built here from smooth shells, rigged on a
skeleton of its own and animated here.

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/pitcher.py [-- --pose-sheet <out.png>]

It writes, overwriting what it wrote last time, so both files are an output of this script:

- Assets/_Project/Art/Enemies/Pitcher.fbx                 one skinned mesh on Rig_Pitcher, with its clips
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas (enemy_atlas.py)

**Built as frog.py builds the Frog (M7-05n).** Every part is a rounded, smooth-shaded shell of one
colour, placed on a bone and bound to it rigidly; the shells overlap at every joint, so a bent joint
opens no seam. Nothing outside the repository is read.

**Tall and narrow, where the Frog is low and wide (GD 17.1: silhouettes must differ as black shapes).**
A jug 1.35 m tall and half a metre across: a round belly on two stubby root legs, a waist, and a neck
that widens to a rolled wine rim around a dark throat, with a lid hinged at the back of the mouth. Two
leaf arms hang at its sides and two small eyes sit under the rim, so it has a front.

**The lid rests half shut, at 35 degrees.** Near 57 degrees it would be edge-on to the game camera and
vanish; at 35 its top faces the camera, and the attack flips it wide open, so a lid snapping open is the
Spitter's telegraph. The jug is one shell from roots to lip, so it has no seam; only the rim, the throat
and the lid ride the mouth's bone above it.

**Why a rig of its own, not Rig_Medium**, frog.py's reason: no humanoid clip plays on a jug.
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
STAND_OFF = (0.05, 0.15)  # lighter and more saturated than the floor, averaged (PitcherModelTests)
FPS = 30
SIDES = (("l", 1.0), ("r", -1.0))  # the plant faces -Y, so its left is +X


def v(x, y, z):
    return Vector((x, y, z))


# ---------------------------------------------------------------------------------------------
# The skeleton's joints, in the model's own frame: Z up, facing -Y, 1.4 m to the top of the lid.
# ---------------------------------------------------------------------------------------------

MOUTH = v(0, 0, 1.04)  # the rim's centre
MOUTH_TILT = 12.0  # degrees: the rim rises toward the back, where the lid is hinged
RIM_R = 0.24
HINGE = v(0, 0.235, 1.10)
LID_OPEN = 35.0  # degrees above level, at rest: half shut, so the camera sees its top


def leg_joints(s):
    """One side's root leg: short and fat, from inside the belly down to a splayed root foot."""
    return {"hip": v(0.11 * s, 0.0, 0.27), "knee": v(0.165 * s, -0.02, 0.15),
            "ankle": v(0.17 * s, 0.02, 0.065), "toes": v(0.19 * s, -0.13, 0.03)}


def leaf_joints(s):
    """One side's leaf arm, from the belly's flank, out, forward a little and down."""
    return {"base": v(0.235 * s, 0.0, 0.57), "tip": v(0.37 * s, -0.09, 0.30)}


def lid_dir(open_deg):
    """The lid's forward direction from its hinge, `open_deg` above level."""
    a = math.radians(open_deg)
    return v(0, -math.cos(a), math.sin(a))


# ---------------------------------------------------------------------------------------------
# Shells. Each shape is (verts, faces) in a unit local space; a part places one, names its swatch and
# the bone it rides, rigidly.
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
    an r of 0 is a pole. `radial(theta, z)` scales r around the ring, for ribs and ovals."""
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


def torus(major, minor, segments, sides, radial=None):
    """A ring about Z at the origin. `radial(theta)` scales the tube's radius around the ring."""
    verts = []
    for s in range(segments):
        theta = 2 * math.pi * s / segments
        k = radial(theta) if radial else 1.0
        c, d = v(math.cos(theta), math.sin(theta), 0), v(0, 0, 1)
        for t in range(sides):
            phi = 2 * math.pi * t / sides
            verts.append(c * (major + minor * k * math.cos(phi)) + d * (minor * k * math.sin(phi)))
    faces = [[s * sides + t, s * sides + (t + 1) % sides, ((s + 1) % segments) * sides + (t + 1) % sides,
              ((s + 1) % segments) * sides + t] for s in range(segments) for t in range(sides)]
    return verts, faces


def capsule(a, b, r1, r2, segments=8, cap=2):
    """A tube from a to b with round ends, already in place: a limb segment (frog.py's)."""
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
    """The turn that takes local -Y to `forward`, with local +Z as near `up` as it can be."""
    y = -forward.normalized()
    x = y.cross(up).normalized()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed().to_quaternion()


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def build():
    # The jug: one smooth shell from the roots to the lip, a round belly low, a gentle waist, and a
    # neck that widens again toward the mouth, whose top slopes up toward the back as the rim does.
    # Closed at the top; the throat and the rim cover that cap.
    def slope(p):
        return p.z + (p.y * math.tan(math.radians(MOUTH_TILT))) * smooth(0.92, 1.04, p.z)
    jug = ((0.195, 0.0), (0.205, 0.10), (0.235, 0.185), (0.29, 0.245), (0.37, 0.272), (0.46, 0.268),
           (0.55, 0.245), (0.64, 0.212), (0.72, 0.195), (0.80, 0.198), (0.88, 0.207), (0.95, 0.220),
           (1.01, 0.232), (1.04, 0.228), (1.045, 0.0))
    verts, faces = lathe(jug, 16)
    add(([v(p.x, p.y, slope(p)) for p in verts], faces), "pitcher_skin", "jug")

    # The mouth: a rolled wine rim with a scalloped lip, and a dark throat inside it. The jug's own
    # opening is capped dark too, so a mouth tilted on its bone can only ever bare more dark.
    tilt = pitch(MOUTH_TILT)
    place(torus(RIM_R, 0.058, 24, 6, lambda t: 1.0 + 0.07 * math.cos(12 * t)), MOUTH, (1, 1, 1), "pitcher_rim", "mouth", tilt)
    place(blob(1, 0.55), MOUTH, (0.225, 0.225, 0.034), "pitcher_throat", "mouth", tilt)  # flat, thick at the edge
    place(blob(1), MOUTH + tilt @ v(0, 0, -0.008), (0.21, 0.21, 0.022), "pitcher_throat", "jug", tilt)

    # The lid: a thick rounded leaf hinged at the back of the rim, standing open over the mouth, domed
    # and curling down at its front edge like a hood, with a little spur behind the hinge.
    d = lid_dir(LID_OPEN)

    def lid_warp(q, p):
        k = 1.0 - 0.30 * max(0.0, p.y) ** 2  # +y is toward the hinge
        return v(q.x * k, q.y, q.z + 0.035 * (1 - p.x * p.x) - 0.05 * max(0.0, -p.y) ** 2)
    place(blob(3, 0.9), HINGE + d * 0.21, (0.25, 0.23, 0.042), "pitcher_lid", "lid", along(d), lid_warp)
    add(capsule(HINGE + v(0, 0.03, -0.07), HINGE + d * 0.06, 0.052, 0.045, segments=8), "pitcher_lid", "lid")

    # The eyes: small and dark with a glint, under the rim, so the plant has a front.
    for _, s in SIDES:
        x, z, r = 0.080 * s, 0.84, 0.204
        normal = v(x, -math.sqrt(r * r - x * x), 0) / r
        centre = v(0, 0, z) + normal * (r - 0.004)
        place(blob(1), centre, (0.04, 0.055, 0.024), "pitcher_eye", "jug", facing(normal))
        place(ball(6, 4), centre + normal * 0.02 + v(-0.012 * s, 0, 0.022), (0.013, 0.013, 0.011), "pitcher_shine", "jug")

    # The leaf arms: thick pointed leaves hanging from the flanks.
    for side, s in SIDES:
        j = leaf_joints(s)
        length = (j["tip"] - j["base"]).length

        def leaf_warp(q, p):
            tipward = max(0.0, -p.y)
            k = (1.0 - 0.62 * tipward ** 1.4) * (1.0 - 0.35 * max(0.0, p.y) ** 2)
            return v(q.x * k, q.y, q.z + 0.045 * (p.y * p.y) - 0.02 * p.x * p.x)
        place(blob(2), (j["base"] + j["tip"]) / 2, (0.115, length / 2 + 0.04, 0.028), "pitcher_leaf", f"leaf.{side}",
              along(j["tip"] - j["base"], v(0.35 * s, 0, 1)), leaf_warp)

    # The root legs: a fat thigh and shin, and a foot of three splayed root toes.
    for side, s in SIDES:
        j = leg_joints(s)
        add(capsule(j["hip"], j["knee"], 0.078, 0.068), "pitcher_root", f"thigh.{side}")
        add(capsule(j["knee"], j["ankle"], 0.068, 0.058), "pitcher_root", f"shin.{side}")
        heel = j["ankle"] + v(0, 0.02, -0.02)
        ground = (j["toes"] - j["ankle"])
        ground.z = 0
        for angle in (-38, 0, 38):
            toward = (yaw(angle * s) @ ground.normalized())
            tip = v(heel.x, heel.y, 0.028) + toward * 0.15
            add(capsule(heel, tip, 0.05, 0.022, segments=6), "pitcher_root", f"foot.{side}")


def spots():
    """Wine speckles pressed into the jug: shallow domes laid where a ray toward the axis lands."""
    by = {}
    for part in PARTS:
        if part.swatch == "pitcher_skin":
            by[part.bone] = part
    part = by["jug"]
    tree = BVHTree.FromPolygons(part.verts, part.faces)
    for angle, z, r in ((-150, 0.40, 0.050), (150, 0.46, 0.042), (-120, 0.30, 0.035), (40, 0.35, 0.045), (-35, 0.50, 0.036),
                        (100, 0.58, 0.04), (-60, 0.30, 0.03), (180, 0.80, 0.032), (130, 0.88, 0.028)):
        a = math.radians(angle - 90)  # 0 degrees is the front, -Y
        d = v(math.cos(a), math.sin(a), 0)
        hit, normal, _, _ = tree.ray_cast(v(0, 0, z) + d * 1.0, -d)
        assert hit is not None, f"no surface under the spot at {angle}, {z}"
        if normal.dot(d) < 0:
            normal = -normal
        place(dome(), hit, (r, r * 0.8, 0.010), "pitcher_rim", "jug", facing(normal, v(0, 0, 1)), closed=False)


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
# One mesh: every shell, smooth-shaded, each corner sampling its swatch by height, so the plant is
# light at the lid and darker toward its roots.
# ---------------------------------------------------------------------------------------------

TOP_Z = 1.42


def assemble(bone_names):
    mesh = bpy.data.meshes.new("Pitcher_Body")
    body = bpy.data.objects.new("Pitcher_Body", mesh)
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


def stand_off(body, atlas):
    """PitcherModelTests' colour row, run here first: the surface's mean colour against the floor's."""
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
    plant, ground = (lum / area, sat / area), (float(luma(px).mean()), float(saturation(px).mean()))
    print(f"  colour: luma {plant[0]:.3f} and saturation {plant[1]:.3f}, on a floor of {ground[0]:.3f} and {ground[1]:.3f}")
    assert plant[0] >= ground[0] + STAND_OFF[0] and plant[1] >= ground[1] + STAND_OFF[1], "the Pitcher does not stand off the Jungle's floor"


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
    xs = [vert.co.x for vert in mesh.vertices]
    ys = [vert.co.y for vert in mesh.vertices]
    zs = [vert.co.z for vert in mesh.vertices]
    print(f"  Pitcher: {tris} triangles, {len(mesh.vertices)} vertices, {max(zs) - min(zs):.3f} m tall, "
          f"{max(xs) - min(xs):.3f} m wide, {max(ys) - min(ys):.3f} m deep")


def model(tex_dir=TEX):
    """The atlas and the unrigged mesh, checked: where main() starts, and a review render can."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    PARTS.clear()
    atlas = build_atlas(tex_dir)
    build()
    spots()
    cull()
    body = assemble(list(skeleton()))
    check(body, atlas)
    return body


# ---------------------------------------------------------------------------------------------
# The skeleton. Each bone is (parent, head, tail) in the model's frame. `body` is the hips, a short
# bone the legs hang from; `jug` stands on it and carries everything above the roots, so a lean, a
# flinch or a wilt tilts the jug and leaves the feet where they are, as the Frog's spine does.
# ---------------------------------------------------------------------------------------------

BASE = v(0, 0, 0.24)  # where the jug pivots on its roots


def skeleton():
    bones = {
        "root": (None, v(0, 0, 0), v(0, 0, 0.12)),
        "body": ("root", BASE, BASE + v(0, 0, 0.10)),
        "jug": ("body", BASE, v(0, 0, 0.95)),
        "mouth": ("jug", MOUTH, MOUTH + v(0, 0, 0.12)),
        "lid": ("mouth", HINGE, HINGE + lid_dir(LID_OPEN) * 0.30),
    }
    for side, s in SIDES:
        leaf, leg = leaf_joints(s), leg_joints(s)
        bones[f"leaf.{side}"] = ("jug", leaf["base"], leaf["tip"])
        bones[f"thigh.{side}"] = ("body", leg["hip"], leg["knee"])
        bones[f"shin.{side}"] = (f"thigh.{side}", leg["knee"], leg["ankle"])
        bones[f"foot.{side}"] = (f"shin.{side}", leg["ankle"], leg["toes"])
    return bones


LEGS = {f"leg_{side}": ((f"thigh.{side}", f"shin.{side}", f"foot.{side}"), "body") for side, _ in SIDES}


def build_armature():
    data = bpy.data.armatures.new("Rig_Pitcher")
    arm = bpy.data.objects.new("Rig_Pitcher", data)
    bpy.context.scene.collection.objects.link(arm)
    # frog.py's shape: Y-up data under an object turned 90 degrees about X, so the plant faces +Z in Unity.
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
# Posing, frog.py's: a pose names a body offset and turn, a few joint angles and a target per leg,
# solved by two-bone IK, and becomes each pose bone's basis.
# ---------------------------------------------------------------------------------------------


def rot(pitch=0.0, roll=0.0, yaw=0.0):
    """Degrees, about the rest frame's axes. Pitch > 0 tips the top forward (-Y); roll > 0 tips it
    toward +X, the plant's left; yaw > 0 turns the front toward +X."""
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
        for leg, (chain, _) in LEGS.items():
            a, k, e = self.head[chain[0]], self.head[chain[1]], self.head[chain[2]]
            line = (e - a).normalized()
            self.poles[leg] = (k - a) - line * (k - a).dot(line)

    def world(self, pose):
        W = {"root": self.rest["root"]}
        carry = {"root": Matrix.Identity(4)}

        def fk(name, parent, q=Quaternion(), scale=None):
            m = carry[parent] @ about(self.head[name], q) @ self.rest[name]
            if scale is not None:
                m = m @ Matrix.Diagonal((scale, scale, scale, 1.0))
            W[name] = m
            carry[name] = m @ self.rest[name].inverted()

        body = Matrix.Translation(pose.get("offset", v(0, 0, 0))) @ about(self.head["body"], rot(*pose.get("body", (0, 0, 0))))
        W["body"] = body @ self.rest["body"]
        carry["body"] = body
        fk("jug", "body", rot(*pose.get("jug", (0, 0, 0))))
        fk("mouth", "jug", rot(pose.get("mouth", 0.0)), scale=pose.get("gulp"))  # >= 1: a smaller rim bares the lip
        fk("lid", "mouth", rot(-pose.get("lid", 0.0)))  # opening turns the lid up and back
        for side, s in SIDES:
            lift, swing = pose.get(f"leaf_{side}", (0.0, 0.0))
            fk(f"leaf.{side}", "jug", rot(-swing, -lift * s))  # lift raises the tip outward, swing carries it forward
        for leg, (chain, parent) in LEGS.items():
            self.limb(W, carry, pose.get(leg), chain, parent, leg)
        return W

    def limb(self, W, carry, spec, chain, parent, leg):
        """spec: (mode, ankle_delta, foot_pitch), frog.py's: "w" plants the ankle in the world at its
        rest place plus delta, "c" carries it with the hips; foot_pitch > 0 dips the toes."""
        mode, delta, end_pitch = spec if spec else ("w", v(0, 0, 0), 0.0)
        upper, lower, end = chain
        C = carry[parent]
        hip = C @ self.head[upper]
        rest_joint = self.head[end]
        end_vec = rot(end_pitch).to_matrix() @ (self.tail[end] - self.head[end])
        if mode == "w":
            target = rest_joint + delta
        else:
            target = C @ (rest_joint + delta)
            end_vec = C.to_3x3() @ end_vec
        l1 = (self.head[lower] - self.head[upper]).length
        l2 = (self.head[end] - self.head[lower]).length
        knee, joint = ik2(hip, target, l1, l2, C.to_3x3() @ self.poles[leg])
        m = C @ self.rest[upper]
        m = about(hip, (m.to_3x3() @ v(0, 1, 0)).rotation_difference(knee - hip)) @ m
        W[upper] = m
        m = (m @ self.rest[upper].inverted()) @ self.rest[lower]
        m = about(knee, (m.to_3x3() @ v(0, 1, 0)).rotation_difference(joint - knee)) @ m
        W[lower] = m
        m = (m @ self.rest[lower].inverted()) @ self.rest[end]
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
                basis = (bones[p].matrix_local.inverted() @ B).inverted() @ M[p].inverted() @ M[n]
            self.arm.pose.bones[n].matrix_basis = basis


# ---------------------------------------------------------------------------------------------
# The clips, in place at 30 fps, the root never moving:
#
#     Idle     48 f  loop   a sway, the lid bobbing, the leaves stirring
#     Shuffle  12 f  loop   two steps on its roots, 0.44 m of ground at 1x, so a stride of 1.1 m/s
#     Attack   36 f         leans back with the lid flipping wide open to frame 14, snaps forward and
#                           spits on frame 18 (0.6 s), the mouth flared and furthest forward:
#                           EnemyAnimatorView's strike time
#     Hit      12 f         a flinch: the jug knocked back, the lid slammed, the leaves flung up
#     Death    30 f         wilts, then topples onto its left side, on the ground by frame 15
# ---------------------------------------------------------------------------------------------

STEP = 0.11  # how far each foot plants ahead of and behind its hip


def P(**kw):
    pose = {leg: ("w", v(0, 0, 0), 0.0) for leg in LEGS}
    pose.update(kw)
    return pose


def shuffle():
    keys = {}
    for f in range(0, 13, 2):
        p = f / 12
        pose = {}
        for side, phase in (("l", p), ("r", (p + 0.5) % 1.0)):
            if phase < 0.5:  # planted, sliding back under the hip from a stride ahead to one behind
                y = -STEP + 2 * STEP * (phase / 0.5)
                pose[f"leg_{side}"] = ("w", v(0, y - 0.02, 0), 0.0)
            else:  # lifted and swung forward
                t = (phase - 0.5) / 0.5
                y = STEP - 2 * STEP * t
                pose[f"leg_{side}"] = ("w", v(0, y - 0.02, 0.075 * math.sin(math.pi * t)), -18 * math.sin(math.pi * t))
        swing = math.cos(2 * math.pi * p)
        keys[f] = P(**pose, offset=v(0, 0, -0.018 * math.cos(4 * math.pi * p)), body=(0, 5.0 * math.sin(2 * math.pi * p), 0),
                    jug=(6.0, -2.0 * math.sin(2 * math.pi * p), 0), lid=5.0 * math.cos(4 * math.pi * p),
                    leaf_l=(8.0, -14.0 * swing), leaf_r=(8.0, 14.0 * swing))
    return keys


def carried(delta, foot_pitch=0.0):
    """A foot carried with the hips, off the ground."""
    return ("c", delta, foot_pitch)


def clips():
    lying = dict(body=(0, 90, 0), jug=(12, 2, 0), mouth=8.0, lid=15.0, leaf_l=(-12.0, 8.0), leaf_r=(0.0, 8.0),
                 leg_l=carried(v(0, 0, 0.02), 10.0), leg_r=carried(v(0.02, 0.02, 0.05), 30.0), ground=True)
    return {
        "Idle": (True, {
            0: P(),
            12: P(jug=(-1.5, 2.0, 0), lid=6.0, gulp=1.02, leaf_l=(6.0, 3.0), leaf_r=(2.0, 0.0), offset=v(0, 0, -0.006)),
            24: P(jug=(1.5, 0.0, 3.0), lid=9.0, leaf_l=(3.0, -2.0), leaf_r=(6.0, 3.0)),
            36: P(jug=(-1.0, -2.0, 0), lid=4.0, gulp=1.02, leaf_l=(2.0, 0.0), leaf_r=(6.0, -3.0), offset=v(0, 0, -0.004)),
            48: P(),
        }),
        "Shuffle": (True, shuffle()),
        "Attack": (False, {
            0: P(),
            4: P(jug=(-8, 0, 0), mouth=-2.0, lid=30.0, gulp=1.02, leaf_l=(15.0, -8.0), leaf_r=(15.0, -8.0), offset=v(0, 0.01, -0.01)),
            10: P(jug=(-16, 0, 0), mouth=-4.0, lid=70.0, gulp=1.04, leaf_l=(30.0, -18.0), leaf_r=(30.0, -18.0), offset=v(0, 0.02, -0.025)),
            14: P(jug=(-19, 0, 0), mouth=-5.0, lid=78.0, gulp=1.05, leaf_l=(34.0, -20.0), leaf_r=(34.0, -20.0), offset=v(0, 0.02, -0.03)),
            18: P(jug=(14, 0, 0), mouth=3.0, lid=82.0, gulp=1.08, leaf_l=(10.0, 22.0), leaf_r=(10.0, 22.0), offset=v(0, -0.02, -0.02)),
            21: P(jug=(9, 0, 0), mouth=2.0, lid=75.0, gulp=1.03, leaf_l=(6.0, 12.0), leaf_r=(6.0, 12.0), offset=v(0, -0.01, -0.01)),
            26: P(jug=(3, 0, 0), mouth=0.5, lid=45.0, leaf_l=(3.0, 4.0), leaf_r=(3.0, 4.0)),
            36: P(),
        }),
        "Hit": (False, {
            0: P(),
            3: P(jug=(-10, 3, 0), mouth=-3.0, lid=-30.0, leaf_l=(28.0, -10.0), leaf_r=(28.0, -10.0)),
            7: P(jug=(-3, 0, 0), mouth=-1.0, lid=-8.0, leaf_l=(8.0, -3.0), leaf_r=(8.0, -3.0)),
            12: P(),
        }),
        "Death": (False, {
            0: P(),
            4: P(jug=(10, 0, 0), mouth=6.0, lid=-20.0, leaf_l=(-25.0, 5.0), leaf_r=(-25.0, 5.0), offset=v(0, 0, -0.02)),
            8: P(body=(0, 22, 0), jug=(16, 4, 0), mouth=8.0, lid=-28.0, leaf_l=(-30.0, 5.0), leaf_r=(-20.0, 5.0), offset=v(0, 0, -0.03),
                 leg_r=carried(v(0, 0, 0.04), 20.0)),
            12: P(body=(0, 60, 0), jug=(18, 6, 0), mouth=8.0, lid=-25.0, leaf_l=(-35.0, 8.0), leaf_r=(-10.0, 10.0),
                  leg_l=carried(v(0.02, 0, 0.03), 10.0), leg_r=carried(v(0.02, 0.02, 0.05), 25.0), ground=True),
            15: P(**{**lying, "body": (0, 94, 0), "lid": -10.0, "leaf_l": (-20.0, 10.0), "leaf_r": (10.0, 12.0)}),
            18: P(**{**lying, "body": (0, 86, 0), "lid": 5.0}),
            22: P(**lying),
            30: P(**lying),
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
            poser.apply(pose)
            if grounded:  # lying down: lifted until its lowest point meets the floor
                pose["offset"] = pose.get("offset", v(0, 0, 0)) - v(0, 0, lowest(body))
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
        filepath=os.path.join(OUT, "Pitcher.fbx"),
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
    "Idle": (0, 12, 24, 36),
    "Shuffle": (0, 2, 4, 6, 8, 10),
    "Attack": (0, 4, 10, 14, 18, 26),
    "Hit": (0, 3, 7, 12),
    "Death": (0, 4, 8, 12, 15, 30),
}


def pose_sheet(path):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, "Pitcher.fbx"))
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
    shading.show_specular_highlight = False  # M_Enemy's smoothness is 0.05; Workbench's sheen greys the throat
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
        "three_quarter": (v(-3.2, -3.4, 2.4), v(0.15, 0, 0.62), 2.1),
        "side": (v(-5, 0, 0.7), v(0, 0, 0.62), 2.1),
        "game": (v(0, -math.cos(pitch_r) * 6, math.sin(pitch_r) * 6 + 0.4), v(0, 0, 0.55), 2.1),
    }
    width = max(len(f) for f in SHEET.values())
    rows = []
    arm.animation_data_create()
    for clip, frames in SHEET.items():
        arm.animation_data.action = actions[clip]
        for view in (("three_quarter", "side") if clip in ("Shuffle", "Attack", "Death") else ("three_quarter",)):
            aim(*views[view])
            cells = []
            for f in frames:
                scene.frame_set(int(f))
                cells.append(render())
            while len(cells) < width:
                cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
            rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {clip}")
    # The game camera's view, and the silhouette beside a capsule, as the Frog's sheet has.
    cells = []
    for clip, f in (("Idle", 0), ("Shuffle", 3), ("Attack", 14), ("Attack", 18), ("Death", 30)):
        arm.animation_data.action = actions[clip]
        scene.frame_set(f)
        aim(*views["game"])
        cells.append(render())
    arm.animation_data.action = actions["Idle"]
    scene.frame_set(0)
    floor.hide_render = True
    shading.light = "FLAT"
    shading.color_type = "SINGLE"
    shading.single_color = (0, 0, 0)
    shading.show_object_outline = False
    shading.show_shadows = False
    scene.world = scene.world or bpy.data.worlds.new("sheet")
    scene.world.color = (1, 1, 1)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.4, depth=1.6, location=(1.1, 0, 0.8))
    aim(v(0.55, -math.cos(pitch_r) * 8, math.sin(pitch_r) * 8 + 0.5), v(0.55, 0, 0.6), 3.0)
    cells.append(render())
    rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("pitcher.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
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
