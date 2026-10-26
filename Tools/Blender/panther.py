"""The Jungle's Lunger, a panther: TRELLIS's sculpt cut to the crowd's budget, repainted onto the shared
enemy atlas, rigged on a skeleton of its own and animated here.

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/panther.py [-- --pose-sheet <out.png>]

It reads one file and writes two, overwriting what it wrote last time:

- Tools/Blender/Sources/Panther_trellis.glb               the sculpt, read only
- Assets/_Project/Art/Enemies/Panther.fbx                 one skinned mesh on Rig_Panther, with its clips
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas (enemy_atlas.py)

**The owner's way, not the shells (JG-07b).** Every other Jungle body is built from smooth shells in its
script. This one is a Gemini picture made 3D by TRELLIS, then cleaned by the TBD_ project's fix_glb.py
(faces turned outward, holes patched, 30 039 faces), and kept in the repository as Sources/. Here it is
re-centred, its tail swung straight back, scaled, decimated to the crowd's budget and smooth-shaded:
M7-05j's Tripo frog read as faceted because it was flat-shaded, not because it was a sculpt.

**Repainted, not textured.** Every face takes one atlas swatch by what TRELLIS painted under it: the
coat, a darker rosette, the paler belly and muzzle, or an eye. The swatch's gradient runs by height, as
KayKit's bodies' do. Two swatches are new (enemy_atlas.py); the rest are borrowed.

**Its own rig, frog.py's reason**: Rig_Medium's humanoid clips cannot play on four legs. The skin is
weighted here, by distance to each bone's segment within the region that bone may move, so a front
paw never drags the chest and the tail never pulls a thigh. Heat weighting refuses TRELLIS's open shells.

**The clips**, in place at 30 fps, the root never moving:

    Idle    48 f  loop   breathing, the tail swaying, a glance
    Prowl   24 f  loop   a stalking walk, diagonal legs together; STRIDE metres of ground at 1x
    Crouch  27 f         sinks low, head down, tail lashing; held from frame 9 to the end
    Lunge   12 f  loop   a bound, legs gathered then flung out; played at up to 4x while dashing
    Hit     12 f         a flinch: head and chest knocked back, and a rebound
    Death   30 f         rolls onto its side and goes still
"""

import math
import os
import sys

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
SOURCE = os.path.join(HERE, "Sources", "Panther_trellis.glb")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Enemies")
TEX = os.path.join(OUT, "Textures")

TRIANGLE_BUDGET = (400, 2500)  # GD 17.1, the crowd's
TRIANGLES = 2300  # what the decimation aims at, under the budget's top
DARK = (0.15, 0.40, 0.35)  # mean luma between, most mean saturation: PantherModelTests' row
FPS = 30


def v(x, y, z):
    return Vector((x, y, z))


# ---------------------------------------------------------------------------------------------
# The sculpt's own landmarks, in its own units (TRELLIS fits it in a unit box: Z up, facing -Y). Read
# once off the checked-in source; a new source means new numbers here.
# ---------------------------------------------------------------------------------------------

RUMP_Y = 0.28  # where the back ends and the tail begins
TAIL_FROM = (0.27, 0.02)  # the tail is every vertex behind this Y and above this Z
TORSO_BAND = (-0.2, 0.2)  # the stretch of back whose middle is the body's centre line
LEG_Y = (-0.30, 0.15)  # the front and back legs' Y, whose middle is where the body stands
BODY_LENGTH = 1.52  # metres, nose to rump, in the game
VOXEL = 0.012  # metres: fine enough to keep the toes and ears, which the decimation then smooths


def load():
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    for other in [o for o in bpy.context.scene.objects if o is not obj]:
        bpy.data.objects.remove(other)
    return obj


def straighten_tail(bm):
    """Swings the tail, which TRELLIS left held out toward -X, straight back along +Y. Every tail vertex
    turns about the tail's root by the tail's own angle, eased in over its first 5 cm so the root does
    not kink."""
    tail = [vt for vt in bm.verts if vt.co.y > TAIL_FROM[0] and vt.co.z > TAIL_FROM[1]]
    root = [vt.co for vt in tail if vt.co.y < TAIL_FROM[0] + 0.04]
    pivot = sum(root, Vector()) / len(root)
    xy = np.array([(vt.co.x - pivot.x, vt.co.y - pivot.y) for vt in tail])
    _, _, axes = np.linalg.svd(xy - xy.mean(axis=0), full_matrices=False)
    d = axes[0] if axes[0][1] > 0 else -axes[0]
    angle = math.atan2(d[0], d[1])  # turning by this about +Z takes the tail onto +Y
    print(f"  tail: {len(tail)} vertices, swung {math.degrees(angle):.1f} degrees straight back")
    for vt in tail:
        along = (vt.co.x - pivot.x) * d[0] + (vt.co.y - pivot.y) * d[1]
        ease = min(1.0, max(0.0, along / 0.05))
        ease = ease * ease * (3 - 2 * ease)
        vt.co = pivot + Matrix.Rotation(angle * ease, 3, "Z") @ (vt.co - pivot)


def shape(obj):
    """Welded, centred on its back, its tail straight, at the game's size, standing on the floor with
    its origin between its legs."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    zs = [vt.co.z for vt in bm.verts]
    z0, h = min(zs), max(zs) - min(zs)
    band = [vt.co.x for vt in bm.verts if TORSO_BAND[0] < vt.co.y < TORSO_BAND[1] and vt.co.z > z0 + 0.45 * h]
    centre_x = (min(band) + max(band)) / 2
    straighten_tail(bm)
    nose = min(vt.co.y for vt in bm.verts)
    scale = BODY_LENGTH / (RUMP_Y - nose)
    stand = v(centre_x, (LEG_Y[0] + LEG_Y[1]) / 2, z0)
    for vt in bm.verts:
        vt.co = (vt.co - stand) * scale
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    print(f"  shape: centred {centre_x:+.3f}, scaled {scale:.3f}")
    return scale


def decimate(obj):
    """A copy cut to TRIANGLES; the original stays as the colour source."""
    lo = obj.copy()
    lo.data = obj.data.copy()
    lo.name = "Panther_Body"
    bpy.context.scene.collection.objects.link(lo)
    # TRELLIS's UV seams split every island's edge, and a collapse will not cross a seam: with them the
    # cut stalls near 6,600. The copy is repainted onto the atlas anyway, so its UVs go first.
    while lo.data.uv_layers:
        lo.data.uv_layers.remove(lo.data.uv_layers[0])
    bpy.context.view_layer.objects.active = lo
    # The head is a tangle of open and non-manifold shells (fix_glb.py leaves 2,584 such edges), and a
    # collapse will not touch those: it stalled with 5,000 triangles in the head alone. A voxel remesh
    # first makes the whole cat one closed surface, which also gives the skinning one shell to weigh.
    lo.data.remesh_voxel_size = VOXEL
    lo.data.remesh_voxel_adaptivity = 0.0
    bpy.ops.object.voxel_remesh()
    for _ in range(4):  # a collapse can stop short of its ratio on the shells' open rims; go again
        tris = sum(len(p.vertices) - 2 for p in lo.data.polygons)
        if tris <= TRIANGLES * 1.02:
            break
        mod = lo.modifiers.new("decimate", "DECIMATE")
        mod.ratio = TRIANGLES / tris
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    loose = [vt for vt in bm.verts if not vt.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(lo.data)
    bm.free()
    return lo


# ---------------------------------------------------------------------------------------------
# The repaint: what TRELLIS painted under each face, read off the full sculpt, becomes a swatch.
# ---------------------------------------------------------------------------------------------


def source_colours(hi, lo):
    """For each face of `lo`, the sculpt's texture colour at the nearest point of `hi`'s surface."""
    image = next(i for i in bpy.data.images if i.size[0] > 0)
    w, h = image.size
    px = np.array(image.pixels[:], np.float32).reshape(h, w, 4)[..., :3]
    bvh = BVHTree.FromObject(hi, bpy.context.evaluated_depsgraph_get())
    uv = hi.data.uv_layers.active.data
    polys = hi.data.polygons
    out = []
    for poly in lo.data.polygons:
        _, _, index, _ = bvh.find_nearest(poly.center)
        src = polys[index]
        # The texture under the nearest triangle's centre: the faces here are 30 times smaller than
        # the decimated ones, so a point is as good as an average and much cheaper.
        u = sum(uv[i].uv.x for i in src.loop_indices) / src.loop_total
        t = sum(uv[i].uv.y for i in src.loop_indices) / src.loop_total
        out.append(px[min(h - 1, max(0, int(t * h))), min(w - 1, max(0, int(u * w)))])
    return np.array(out)


def luma(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def saturation(c):
    hi, lo = c.max(axis=-1), c.min(axis=-1)
    return np.where(hi > 0, (hi - lo) / np.maximum(hi, 1e-9), 0.0)


# Swatches by what TRELLIS painted: the pale lime eyes, the grey belly and muzzle, the darkest tenth of
# the coat as rosettes, and the rest coat. Thresholds are luma, of the texture as stored.
EYE = (0.25, 0.5)  # least saturation and luma of an eye
BELLY = 0.40  # least luma of the belly and muzzle
ROSETTE = 0.138  # most luma of a rosette


def classify(lo, colours):
    lum, sat = luma(colours), saturation(colours)
    kind = np.where((sat > EYE[0]) & (lum > EYE[1]), "frog_green",
                    np.where(lum > BELLY, "panther_belly",
                             np.where(lum < ROSETTE, "socket", "panther_fur"))).astype(object)
    # Two passes of majority among edge neighbours, so a rosette is a patch and not a speckle. The eyes
    # are a handful of faces and are kept as painted.
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bm.faces.ensure_lookup_table()
    neighbours = [[g.index for e in f.edges for g in e.link_faces if g is not f] for f in bm.faces]
    bm.free()
    for _ in range(2):
        out = kind.copy()
        for i, near in enumerate(neighbours):
            if kind[i] == "frog_green" or not near:
                continue
            names, counts = np.unique(kind[near].astype(str), return_counts=True)
            best = names[np.argmax(counts)]
            if best != kind[i] and counts.max() * 3 >= len(near) * 2:
                out[i] = str(best)
        kind = out
    names, counts = np.unique(kind.astype(str), return_counts=True)
    print("  swatches", dict(zip(names.tolist(), counts.tolist())))
    return kind


def paint(lo, kind):
    """One UV per corner: the face's swatch, light at the top of the cat and dark at its feet."""
    mesh = lo.data
    top = max(vt.co.z for vt in mesh.vertices)
    uv = mesh.uv_layers.new(name="UVMap").data
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            z = mesh.vertices[mesh.loops[li].vertex_index].co.z
            uv[li].uv = uv_for(str(kind[poly.index]), (top - z) / top)
        poly.use_smooth = True
    mesh.materials.clear()
    mesh.materials.append(bpy.data.materials.new("enemy"))


# ---------------------------------------------------------------------------------------------
# Checks, run before anything is written: PantherModelTests' rows.
# ---------------------------------------------------------------------------------------------


def dark(body, atlas):
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
    assert DARK[0] <= lum / area <= DARK[1] and sat / area <= DARK[2], "the Panther is lost or loud"


def proportions(mesh):
    xs = np.array([vt.co.x for vt in mesh.vertices])
    ys = np.array([vt.co.y for vt in mesh.vertices])
    zs = np.array([vt.co.z for vt in mesh.vertices])
    return xs.max() - xs.min(), ys.max() - ys.min(), zs.max() - zs.min()


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
    dark(body, atlas)
    w, d, h = proportions(mesh)
    print(f"  Panther: {tris} triangles, {len(mesh.vertices)} vertices, {d:.3f} m long, {w:.3f} m wide, {h:.3f} m tall")
    assert d / h >= 2.0 and d / w >= 2.5, "the Panther is not long and low (PantherModelTests)"


# ---------------------------------------------------------------------------------------------
# The skeleton, placed on the decimated body's own landmarks. Each bone is (parent, head, tail) in the
# model's frame: Z up, facing -Y, its left at +X, standing on its origin.
# ---------------------------------------------------------------------------------------------

LEGS = ("front.l", "front.r", "back.l", "back.r")
JOINTS = (0.58, 0.30, 0.10)  # shoulder or hip, elbow or knee, wrist or ankle: fractions of the height


def landmarks(mesh):
    co = np.array([tuple(vt.co) for vt in mesh.vertices])
    top = float(co[:, 2].max())
    nose, tail_end = float(co[:, 1].min()), float(co[:, 1].max())
    low = co[co[:, 2] < 0.12 * top]
    paws = {}
    for leg in LEGS:
        front, left = leg.startswith("front"), leg.endswith(".l")
        pick = low[((low[:, 1] < 0) == front) & ((low[:, 0] > 0) == left)]
        paws[leg] = v(float(pick[:, 0].mean()), float(pick[:, 1].mean()), 0.0)
    head = co[co[:, 1] < nose + 0.30]
    # The rump: where the back ends behind the hind legs, scaled from the sculpt's own landmark.
    rump_y = (RUMP_Y - (LEG_Y[0] + LEG_Y[1]) / 2) * SCALE[0]
    root = co[(co[:, 1] > rump_y) & (co[:, 1] < rump_y + 0.08) & (co[:, 2] > 0.4 * top)]
    tip = co[co[:, 1] > tail_end - 0.06]
    return dict(top=top, nose=nose, tail_end=tail_end, paws=paws,
                head=v(0, float(head[:, 1].mean()), float(head[:, 2].mean())),
                tail_root=v(0, rump_y, float(root[:, 2].mean()) if len(root) else 0.55 * top),
                tail_tip=v(0, tail_end - 0.03, float(tip[:, 2].mean())))


def skeleton(mark):
    top = mark["top"]
    front_y = (mark["paws"]["front.l"].y + mark["paws"]["front.r"].y) / 2
    back_y = (mark["paws"]["back.l"].y + mark["paws"]["back.r"].y) / 2
    spine_z = JOINTS[0] * top + 0.04
    hips = v(0, back_y, spine_z)
    mid = v(0, (front_y + back_y) / 2, spine_z + 0.02)
    chest = v(0, front_y, spine_z + 0.03)
    head = mark["head"]
    skull = v(0, head.y + 0.06, head.z + 0.02)
    neck = v(0, front_y - 0.12, 0.80 * top)
    bones = {
        "root": (None, v(0, 0, 0), v(0, 0, 0.12)),
        "hips": ("root", hips, mid),
        "spine": ("hips", mid, chest),
        "chest": ("spine", chest, neck),
        "neck": ("chest", neck, skull),
        "head": ("neck", skull, v(0, mark["nose"] + 0.04, head.z - 0.03)),
        "jaw": ("head", v(0, head.y + 0.04, head.z - 0.06), v(0, mark["nose"] + 0.06, head.z - 0.11)),
    }
    root, tip = mark["tail_root"], mark["tail_tip"]
    for i in range(3):
        a, b = root.lerp(tip, i / 3), root.lerp(tip, (i + 1) / 3)
        bones[f"tail_{i + 1}"] = ("hips" if i == 0 else f"tail_{i}", a, b)
    for leg, paw in mark["paws"].items():
        parent = "chest" if leg.startswith("front") else "hips"
        x, y = paw.x * 0.85, paw.y  # the joints a little inside the paw, under the body
        shoulder, knee, ankle = (v(x, y, f * top) for f in JOINTS)
        bones[f"upper.{leg}"] = (parent, shoulder, knee)
        bones[f"lower.{leg}"] = (f"upper.{leg}", knee, ankle)
        bones[f"paw.{leg}"] = (f"lower.{leg}", ankle, v(x, y - 0.10, 0.02))
    return bones


def build_armature(bones):
    data = bpy.data.armatures.new("Rig_Panther")
    arm = bpy.data.objects.new("Rig_Panther", data)
    bpy.context.scene.collection.objects.link(arm)
    # frog.py's shape: Y-up data under an object turned 90 degrees about X, so it faces +Z in Unity.
    arm.matrix_world = Matrix.Rotation(math.radians(90), 4, "X")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    to_arm = arm.matrix_world.inverted()
    edit = {}
    for name, (parent, head, tail) in bones.items():
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
# The skin: each vertex to its nearest bones among those allowed to move it, by inverse distance to the
# bone's segment, at most three, normalised.
# ---------------------------------------------------------------------------------------------


def segment_distance(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared == 0 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def allowed(name, p, mark, bones):
    top = mark["top"]
    if name == "root":
        return False
    if name.startswith(("upper.", "lower.", "paw.")):
        leg = name.split(".", 1)[1]
        front, left = leg.startswith("front"), leg.endswith(".l")
        if (p.x > 0) != left or (p.y < 0) != front:
            return False
        return p.z < JOINTS[0] * top + (0.0 if name.startswith("upper.") else -0.05 * top)
    if name.startswith("tail_"):
        return p.y > mark["tail_root"].y - 0.04
    if name == "jaw":
        head = mark["head"]
        return p.y < head.y + 0.02 and p.z < head.z - 0.05
    if name in ("head", "neck"):
        return p.y < bones["chest"][1].y + 0.05
    return True


def weigh(body, bones, mark):
    for name in bones:
        body.vertex_groups.new(name=name)
    groups = {g.name: g for g in body.vertex_groups}
    for vert in body.data.vertices:
        p = vert.co
        near = sorted((segment_distance(p, head, tail), name) for name, (_, head, tail) in bones.items()
                      if allowed(name, p, mark, bones))[:3]
        raw = [(1.0 / (d + 0.03) ** 4, name) for d, name in near]
        total = sum(w for w, _ in raw)
        kept = [(w / total, name) for w, name in raw if w / total >= 0.04]
        total = sum(w for w, _ in kept)
        for w, name in kept:
            groups[name].add([vert.index], w / total, "REPLACE")


def bind(body, arm):
    """Under the armature, identity transform, data in its space, as KayKit's own meshes sit."""
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    body.parent = arm
    body.data.transform(arm.matrix_world.inverted())
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_local = Matrix.Identity(4)
    bpy.context.view_layer.update()


SCALE = [1.0]  # shape()'s factor, for landmarks() to place the sculpt's own numbers in metres


def model(tex_dir=TEX):
    """The atlas, the repainted, weighted mesh and its skeleton, checked."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    atlas = build_atlas(tex_dir)
    hi = load()
    SCALE[0] = shape(hi)
    lo = decimate(hi)
    kind = classify(lo, source_colours(hi, lo))
    image = next(i for i in bpy.data.images if i.size[0] > 0)
    bpy.data.objects.remove(hi)
    bpy.data.images.remove(image)
    paint(lo, kind)
    mark = landmarks(lo.data)
    bones = skeleton(mark)
    weigh(lo, bones, mark)
    check(lo, atlas)
    return lo, bones


# ---------------------------------------------------------------------------------------------
# Posing. A pose names the hips' offset and turn, a turn for each spine, neck, head, jaw and tail bone
# about the model's own axes, and each leg either a foot target (solved by two-bone IK, so a planted
# foot stays planted) or a set of swings. Angles are degrees.
#
#     pitch > 0 tips the bone's far end down (a head nods, a tail droops); roll > 0 rolls toward +X, the
#     cat's left; yaw > 0 turns the far end toward +X. A leg's swing > 0 is forward.
# ---------------------------------------------------------------------------------------------


def turn(pitch=0.0, roll=0.0, yaw=0.0):
    return (Quaternion(v(0, 0, 1), math.radians(yaw)) @ Quaternion(v(0, 1, 0), math.radians(roll))
            @ Quaternion(v(1, 0, 0), math.radians(pitch)))


class Poser:
    def __init__(self, arm, bones):
        self.arm = arm
        self.bones = bones
        self.A = arm.matrix_world.to_3x3()
        self.A_inv = self.A.inverted()
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}

    def _local(self, name, q, offset=None):
        """The basis that turns `name` by model-frame `q` about its head, relative to its parent."""
        R = self.rest[name].to_3x3()
        qa = self.A_inv @ q.to_matrix() @ self.A
        basis = (R.inverted() @ qa @ R).to_4x4()
        if offset is not None:
            basis.translation = R.inverted() @ (self.A_inv @ offset)
        self.arm.pose.bones[name].matrix_basis = basis

    def _aim(self, name, angle, roll=None):
        """Points a leg bone `angle` degrees from its rest direction toward +Y (back), in the model frame,
        turned with the body by `roll` (the hips' roll, so a cat on its side keeps its legs' shape)."""
        pb = self.arm.pose.bones[name]
        bpy.context.view_layer.update()
        head = pb.head.copy()
        R = self.rest[name].to_3x3()
        model = Matrix.Rotation(math.radians(angle), 3, "X")
        if roll is not None:
            model = roll.to_matrix() @ model
        want = (self.A_inv @ model @ self.A) @ R
        m = want.to_4x4()
        m.translation = head
        pb.matrix = m

    def apply(self, pose):
        for pb in self.arm.pose.bones:
            pb.matrix_basis = Matrix.Identity(4)
        hips = turn(*pose.get("hips", (0, 0, 0)))
        self._local("hips", hips, pose.get("offset", v(0, 0, 0)))
        for name in ("spine", "chest", "neck", "head", "jaw", "tail_1", "tail_2", "tail_3"):
            if name in pose:
                self._local(name, turn(*pose[name]))
        legs = pose.get("legs", {})
        for leg in LEGS:
            spec = legs.get(leg, (0.0, 0.0, 0.0))
            if len(spec) == 3:  # a foot target: forward, up, paw tilt
                self._ik(leg, *spec)
            else:  # swings: upper, lower and paw, each forward, carried round by the hips
                upper, lower, paw = spec[0], spec[1], spec[2]
                self._aim(f"upper.{leg}", -upper, hips)
                self._aim(f"lower.{leg}", -(upper + lower), hips)
                self._aim(f"paw.{leg}", -(upper + lower + paw), hips)
        bpy.context.view_layer.update()

    def _ik(self, leg, forward, up, tilt):
        _, shoulder, knee = self.bones[f"upper.{leg}"]
        _, _, ankle = self.bones[f"lower.{leg}"]
        l1, l2 = (knee - shoulder).length, (ankle - knee).length
        bpy.context.view_layer.update()
        hip = self.arm.matrix_world @ self.arm.pose.bones[f"upper.{leg}"].head
        target = ankle + v(0, -forward, up)
        dy, dz = target.y - hip.y, target.z - hip.z
        dist = max(1e-4, min(math.hypot(dy, dz), (l1 + l2) * 0.999))
        phi = math.atan2(dy, -dz)  # from straight down toward +Y
        alpha = math.acos(max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist))))
        beta = math.acos(max(-1.0, min(1.0, (l1 * l1 + l2 * l2 - dist * dist) / (2 * l1 * l2))))
        bend = 1.0 if leg.startswith("front") else -1.0  # an elbow points back, a knee forward
        upper = phi + bend * alpha
        lower = upper - bend * (math.pi - beta)
        self._aim(f"upper.{leg}", math.degrees(upper))
        self._aim(f"lower.{leg}", math.degrees(lower))
        self._aim(f"paw.{leg}", -tilt)


# ---------------------------------------------------------------------------------------------
# The clips. Prowl's diagonal walk covers STRIDE metres of ground a cycle at 1x, so JG-07c's _strideSpeed
# is STRIDE over the clip's 0.8 s; Lunge is a bound, authored for the up-to-4x EnemyAnimatorView plays it
# at while the body dashes.
# ---------------------------------------------------------------------------------------------

STRIDE = 0.72
CROUCH_HELD = 9


def P(**kw):
    return dict(kw)


def prowl():
    phase = {"back.l": 0.0, "front.l": 0.25, "back.r": 0.5, "front.r": 0.75}
    keys = {}
    for f in range(0, 25, 2):
        p = f / 24
        s = math.sin(2 * math.pi * p)
        legs = {}
        for leg, ph in phase.items():
            a = 2 * math.pi * (p + ph)
            # The foot runs forward through the cycle's lifted half and back, planted, through the other.
            lift = max(0.0, math.cos(a))
            legs[leg] = ((STRIDE / 2) * math.sin(a), 0.07 * lift * lift, 12 * lift)
        keys[f] = P(offset=v(0, 0, -0.05 + 0.012 * math.cos(4 * math.pi * p)), hips=(0, 2 * s, 0),
                    chest=(2, -2 * s, 0), neck=(10, 0, 0), head=(-8, 0, 0),
                    tail_1=(-6, 0, 10 * s), tail_2=(4, 0, 12 * math.sin(2 * math.pi * p - 0.8)),
                    tail_3=(4, 0, 14 * math.sin(2 * math.pi * p - 1.6)), legs=legs)
    return keys


def lunge():
    gathered = P(offset=v(0, 0, -0.06), hips=(-6, 0, 0), chest=(8, 0, 0), neck=(-4, 0, 0), head=(-4, 0, 0),
                 tail_1=(-10, 0, 0), tail_2=(6, 0, 0), tail_3=(6, 0, 0),
                 legs={"front.l": (-35, 30, 10, 0), "front.r": (-30, 30, 10, 0),
                       "back.l": (35, -40, 20, 0), "back.r": (30, -40, 20, 0)})
    flung = P(offset=v(0, 0, 0.10), hips=(4, 0, 0), chest=(-6, 0, 0), neck=(-6, 0, 0), head=(-2, 0, 0),
              tail_1=(-16, 0, 0), tail_2=(-4, 0, 0), tail_3=(-4, 0, 0),
              legs={"front.l": (60, -20, -10, 0), "front.r": (55, -20, -10, 0),
                    "back.l": (-50, 20, 30, 0), "back.r": (-45, 20, 30, 0)})
    return {0: gathered, 6: flung, 12: gathered}


def clips():
    still = P(offset=v(0, 0, -0.02))
    low = dict(offset=v(0, 0, -0.16), chest=(4, 0, 0), neck=(16, 0, 0), head=(-14, 0, 0), jaw=(-6, 0, 0))
    crouch = {0: still, 5: P(offset=v(0, 0, -0.10), chest=(3, 0, 0), neck=(10, 0, 0), head=(-8, 0, 0))}
    for i, f in enumerate(range(CROUCH_HELD, 28, 3)):
        lash = 22 if i % 2 == 0 else -22
        crouch[min(f, 27)] = P(**low, tail_1=(-14, 0, lash * 0.5), tail_2=(-4, 0, lash), tail_3=(0, 0, lash))
    limp = {leg: (25, -15, 0, 0) for leg in LEGS}
    return {
        "Idle": (True, {
            0: still,
            12: P(offset=v(0, 0, -0.012), chest=(-1, 0, 0), head=(0, 0, 10), tail_2=(0, 0, 10), tail_3=(4, 0, 14)),
            24: P(offset=v(0, 0, -0.02), chest=(1, 0, 0), head=(2, 0, 16), tail_2=(0, 0, -6), tail_3=(4, 0, -10)),
            36: P(offset=v(0, 0, -0.012), chest=(-1, 0, 0), head=(0, 0, 4), tail_2=(0, 0, -12), tail_3=(4, 0, -14)),
            48: still,
        }),
        "Prowl": (True, prowl()),
        "Crouch": (False, crouch),
        "Lunge": (True, lunge()),
        "Hit": (False, {
            0: still,
            3: P(offset=v(0, 0.06, -0.02), chest=(-10, 0, 0), neck=(-14, 0, 0), head=(-10, 0, 0), jaw=(-12, 0, 0)),
            7: P(offset=v(0, -0.02, -0.02), chest=(3, 0, 0), neck=(4, 0, 0), head=(3, 0, 0)),
            12: still,
        }),
        "Death": (False, {
            0: still,
            6: P(offset=v(0, 0.04, -0.06), hips=(0, 8, 0), neck=(-10, 0, 0), head=(-8, 0, 0)),
            16: P(hips=(0, 80, 0), neck=(0, 10, 0), head=(6, 10, 0), tail_1=(10, 0, 0), ground=True, legs=limp),
            22: P(hips=(0, 92, 0), neck=(0, 14, 0), head=(10, 14, 0), tail_1=(12, 0, 0), tail_2=(6, 0, 0), ground=True,
                  legs={leg: (30, -25, 0, 0) for leg in LEGS}),
            30: P(hips=(0, 90, 0), neck=(0, 14, 0), head=(10, 14, 0), jaw=(-8, 0, 0), tail_1=(12, 0, 0), tail_2=(6, 0, 0),
                  ground=True, legs={leg: (32, -28, 0, 0) for leg in LEGS}),
        }),
    }


def lowest(body):
    bpy.context.view_layer.update()
    evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    z = min((evaluated.matrix_world @ vt.co).z for vt in mesh.vertices)
    evaluated.to_mesh_clear()
    return z


def author_clips(arm, body, bones):
    poser = Poser(arm, bones)
    arm.animation_data_create()
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
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
            if grounded:  # rolled over: lowered until its lowest point meets the floor
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
        filepath=os.path.join(OUT, "Panther.fbx"),
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
# four Jungle bodies' black shapes from the game camera (puffball.py's).
# ---------------------------------------------------------------------------------------------

CELL = 288
SHEET = {
    "Idle": (0, 12, 24, 36),
    "Prowl": (0, 4, 8, 12, 16, 20),
    "Crouch": (0, 5, 9, 15, 21, 27),
    "Lunge": (0, 2, 4, 6, 8, 10),
    "Hit": (0, 3, 7, 12),
    "Death": (0, 6, 12, 16, 22, 30),
}


def pose_sheet(path):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, "Panther.fbx"))
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
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
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
        "three_quarter": (v(-4.2, -4.4, 3.0), v(0, 0.1, 0.5), 2.7),
        "side": (v(-6, 0.1, 0.6), v(0, 0.1, 0.5), 2.7),
        "top": (v(0, 0.1, 8), v(0, 0.1, 0), 2.7),
        "game": (v(0, -math.cos(pitch_r) * 8, math.sin(pitch_r) * 8 + 0.4), v(0, 0.1, 0.45), 2.7),
    }
    rows_of = {"Idle": ("three_quarter", "top"), "Prowl": ("three_quarter", "side"), "Crouch": ("three_quarter", "side"),
               "Lunge": ("side",), "Hit": ("side",), "Death": ("three_quarter",)}
    width = max(len(f) for f in SHEET.values())
    rows = []
    arm.animation_data_create()
    for clip, frames in SHEET.items():
        arm.animation_data.action = actions[clip]
        for view in rows_of[clip]:
            aim(*views[view])
            cells = []
            for f in frames:
                scene.frame_set(int(f))
                cells.append(render())
            while len(cells) < width:
                cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
            rows.append(np.concatenate(cells, axis=1))
        print(f"  pose sheet: {clip}")
    # The game camera's view, and the four Jungle bodies as black shapes: Frog, Pitcher, Puffball, Panther.
    cells = []
    for clip, f in (("Idle", 0), ("Prowl", 8), ("Crouch", 15), ("Lunge", 6)):
        arm.animation_data.action = actions[clip]
        scene.frame_set(f)
        aim(*views["game"])
        cells.append(render())
    arm.animation_data.action = actions["Idle"]
    scene.frame_set(0)

    def shift(obj, x):
        # Under an empty, because each clip keys the armature object's own transform (puffball.py's).
        holder = bpy.data.objects.new(f"at_{obj.name}", None)
        scene.collection.objects.link(holder)
        holder.location.x = x
        obj.parent = holder

    for name, x in (("Frog", -2.6), ("Pitcher", -1.2), ("Puffball", 0.1)):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, f"{name}.fbx"))
        for obj in set(bpy.data.objects) - before:
            if obj.parent is None:
                shift(obj, x)
    shift(arm, 1.8)
    floor.hide_render = True
    shading.light = "FLAT"
    shading.color_type = "SINGLE"
    shading.single_color = (0, 0, 0)
    shading.show_object_outline = False
    shading.show_shadows = False
    scene.world = scene.world or bpy.data.worlds.new("sheet")
    scene.world.color = (1, 1, 1)
    aim(v(-0.3, -math.cos(pitch_r) * 10, math.sin(pitch_r) * 10 + 0.5), v(-0.3, 0, 0.6), 6.4)
    cells.append(render())
    while len(cells) < width:
        cells.append(np.ones((CELL, CELL, 3), np.float32) * 0.15)
    rows.append(np.concatenate(cells, axis=1))
    save_png(np.concatenate(rows, axis=0), path)
    print(f"  pose sheet written to {path}")


def main():
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("panther.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
                 "FBX's object ids (rootling.py's docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    print("model")
    body, bones = model()
    arm = build_armature(bones)
    bind(body, arm)
    print("clips")
    author_clips(arm, body, bones)
    export(arm, body)
    if "--pose-sheet" in args:
        pose_sheet(os.path.abspath(args[args.index("--pose-sheet") + 1]))  # Blender would not read it from here


if __name__ == "__main__":
    main()
