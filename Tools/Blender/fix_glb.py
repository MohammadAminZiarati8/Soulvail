# Usage: PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" -b --factory-startup \
#            -P Tools/Blender/fix_glb.py -- <in.glb> <out.glb> [target_faces=30000]
# Copied from the TBD_ project's tools/art/3d/fix_glb.py (which made Sources/Panther_trellis.glb), plus
# step 6b for the Mother Pitcher sculpt (JG-12d). Its output is a body script's source, never shipped.
# Cleans an image-to-3D GLB (TRELLIS and the like) for the game:
#  1. welds UV-seam splits, points every face outward (ray test)
#  2. fills the empty (navy) texture gaps with neighbouring colours (no dark specks)
#  3. repaints stray colour patches on back-facing parts (hair colour bled onto cloth, cloth on hair)
#  4. decimates to target_faces
#  5. duplicates paper-thin faces (open air on both sides, e.g. hair strands) flipped, so they show from both sides
#  6. fills simple holes; 6b caps the rest (rims through non-manifold vertices) with fans;
#     6c flips faces open air sees only from behind, and twins lone sheets
#  7. double-sided, smooth-shaded, exported
import bpy, bmesh, sys
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[:2]
target = int(args[2]) if len(args) > 2 else 30000
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
me = obj.data
size = max(obj.dimensions); eps = size * 1e-4


def ray_sides(bvh, f):
    """Rays escaping the mesh from the front / back of face f (out of 5 each)."""
    c = f.calc_center_median(); nrm = f.normal
    t = nrm.orthogonal().normalized(); b = nrm.cross(t)
    dirs = [nrm] + [(nrm + 0.6 * v).normalized() for v in (t, -t, b, -b)]
    out = sum(bvh.ray_cast(c + d * eps, d)[0] is None for d in dirs)
    inn = sum(bvh.ray_cast(c - d * eps, -d)[0] is None for d in dirs)
    return out, inn


# 1. weld + orient (recalc_face_normals fails on TRELLIS meshes: thin open shells)
bm = bmesh.new(); bm.from_mesh(me)
n = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
bvh = BVHTree.FromBMesh(bm)
flip = [f for f in bm.faces if f.normal.length > 0.5 and (lambda o, i: i > o)(*ray_sides(bvh, f))]
bmesh.ops.reverse_faces(bm, faces=flip)
print("welded", n - len(bm.verts), "flipped to outside", len(flip))
# drop floating specks: loose parts under 0.5% of all faces
seen, specks = set(), []
for f in bm.faces:
    if f in seen: continue
    part, stack = [], [f]; seen.add(f)
    while stack:
        g = stack.pop(); part.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h not in seen: seen.add(h); stack.append(h)
    if len(part) < len(bm.faces) * 0.005: specks += part
bmesh.ops.delete(bm, geom=specks, context="FACES")
print("speck faces removed", len(specks))
bm.to_mesh(me); bm.free()

# 2. texture gaps
mat = me.materials[0]
bsdf = next(nd for nd in mat.node_tree.nodes if nd.type == "BSDF_PRINCIPLED")
img = bsdf.inputs["Base Color"].links[0].from_node.image
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
rgb = px[..., :3].copy()


def dilate(rgb, empty):
    for _ in range(64):
        if not empty.any(): break
        acc = np.zeros_like(rgb); cnt = np.zeros((h, w, 1), np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ok = ~np.roll(empty, (dy, dx), (0, 1))
            acc += np.where(ok[..., None], np.roll(rgb, (dy, dx), (0, 1)), 0); cnt += ok[..., None]
        grow = empty & (cnt[..., 0] > 0)
        rgb[grow] = acc[grow] / cnt[grow]
        empty &= ~grow


GAP = np.array([6, 6, 23]) / 255.0  # TRELLIS empty-atlas fill colour
empty = np.abs(rgb - GAP).max(-1) < 4 / 255.0
print("empty texture px %.1f%%" % (empty.mean() * 100))
dilate(rgb, empty)

# 3. stray patches on back-facing parts: a near-black face amid light cloth, or a light face amid hair
uvl = me.uv_layers.active.data
polys = me.polygons
uvs = [[uvl[l].uv.copy() for l in p.loop_indices] for p in polys]


def sample(i):
    u = sum(t.x for t in uvs[i]) / len(uvs[i]); v = sum(t.y for t in uvs[i]) / len(uvs[i])
    return rgb[min(h - 1, int(v * h)), min(w - 1, int(u * w))]


col = np.array([sample(i) for i in range(len(polys))])
lum = col @ np.array([0.299, 0.587, 0.114])
back = np.array([p.normal.y > 0.2 for p in polys])  # glTF front faces -Y in Blender
kd = KDTree(len(polys))
for p in polys: kd.insert(p.center, p.index)
kd.balance()
DARK, LIGHT, R = 0.15, 0.30, size * 0.025
painted = 0
for it in range(6):  # erode stripes from their edges inward
    changes = {}
    # back: dark stripes on cloth, light specks in hair; anywhere: mid-tone flecks deep inside hair
    # (skin/eye whites are > 0.5 lum, so the face is never touched)
    # hair-fleck rule runs once only: repeated, it creeps across dark leather
    fleck = (lum >= DARK) & (lum < 0.5) if it == 0 else np.zeros_like(back)
    cand = np.nonzero((back & ((lum < DARK) | (lum > LIGHT))) | fleck)[0]
    for i in cand:
        near = [j for _, j, _ in kd.find_range(polys[i].center, R) if j != i]
        if len(near) < 6: continue
        nl = lum[near]
        light, dark = nl > LIGHT, nl < DARK
        if back[i] and lum[i] < DARK and light.mean() >= 0.6:
            changes[i] = col[near][light].mean(0)
        elif (back[i] and lum[i] > LIGHT and dark.mean() >= 0.6) or (fleck[i] and dark.mean() >= 0.85):
            changes[i] = col[near][dark].mean(0)
    if not changes: break
    for i, c in changes.items():
        col[i] = c; lum[i] = c @ np.array([0.299, 0.587, 0.114])
        tri = np.array([(t.x * w, t.y * h) for t in uvs[i]])
        x0, y0 = np.floor(tri.min(0) - 1).astype(int).clip(0); x1, y1 = np.ceil(tri.max(0) + 1).astype(int)
        x1, y1 = min(x1, w - 1), min(y1, h - 1)
        if x1 < x0 or y1 < y0: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = tri[:3]
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-9: continue
        l1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / d
        l2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / d
        tol = 1.5 / max(1.0, np.sqrt(abs(d)))  # ~1.5 px bleed
        inside = (l1 >= -tol) & (l2 >= -tol) & (1 - l1 - l2 >= -tol)
        rgb[y0:y1 + 1, x0:x1 + 1][inside] = c
    painted += len(changes)
print("repainted back patches (face-passes)", painted)
px[..., :3] = rgb
img.pixels[:] = px.ravel()
img.pack()

# 4. decimate a copy, give it clean UVs and bake the colour from the original onto it
# (decimating TRELLIS's fragmented UVs directly smears the texture)
sc = bpy.context.scene
lo = obj.copy(); lo.data = me.copy(); lo.name = obj.name + "_lo"; sc.collection.objects.link(lo)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = lo; lo.select_set(True)
if len(polys) > target:
    mod = lo.modifiers.new("dec", "DECIMATE"); mod.ratio = target / len(polys)
    bpy.ops.object.modifier_apply(modifier=mod.name)
lme = lo.data
print("faces after decimate", len(lme.polygons))
while len(lme.uv_layers) > 1: lme.uv_layers.remove(lme.uv_layers[-1])
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.003)
bpy.ops.object.mode_set(mode="OBJECT")
out_img = bpy.data.images.new(obj.name + "_basecolor", w, h); out_img.generated_color = (0.5, 0.5, 0.5, 1)
lmat = bpy.data.materials.new("Margo" if "margo" in src.lower() else "Character"); lmat.use_nodes = True
lb = next(nd for nd in lmat.node_tree.nodes if nd.type == "BSDF_PRINCIPLED")
tex = lmat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = out_img
lmat.node_tree.links.new(tex.outputs["Color"], lb.inputs["Base Color"])
lb.inputs["Metallic"].default_value = 0.0; lb.inputs["Roughness"].default_value = 0.8
lmat.node_tree.nodes.active = tex
lme.materials.clear(); lme.materials.append(lmat)
# DIFFUSE bake = base colour * (1 - metallic): drop metallic on the source so metal props keep their colour
for ln in list(bsdf.inputs["Metallic"].links): mat.node_tree.links.remove(ln)
bsdf.inputs["Metallic"].default_value = 0.0
sc.render.engine = "CYCLES"; sc.cycles.samples = 4
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = size * 0.004; bk.max_ray_distance = size * 0.01; bk.margin = 8
# short bake rays: don't grab nearby parts (buckle, eye corner) -> no white smears
obj.select_set(True); lo.select_set(True); bpy.context.view_layer.objects.active = lo
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"})
out_img.pack()
print("baked", out_img.name)
bpy.data.objects.remove(obj)
obj, me = lo, lme

# 5. paper-thin faces -> add a flipped copy so they render from both sides
bm = bmesh.new(); bm.from_mesh(me)
bvh = BVHTree.FromBMesh(bm)
thin = [f for f in bm.faces if f.normal.length > 0.5 and min(ray_sides(bvh, f)) >= 3]
dup = bmesh.ops.duplicate(bm, geom=thin)
bmesh.ops.reverse_faces(bm, faces=[g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)])
print("thin faces doubled", len(thin))

# 6. patch real holes (up to 60 edges); new faces borrow UVs from the neighbouring surface
bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
uvk = bm.loops.layers.uv.active
old = set(bm.faces)
rim = [e for e in bm.edges if e.is_boundary]
bmesh.ops.holes_fill(bm, edges=rim, sides=60)
new = [f for f in bm.faces if f not in old]
bmesh.ops.triangulate(bm, faces=new)
new = [f for f in bm.faces if f not in old]
for f in new:
    for l in f.loops:
        src_l = next((x for x in l.vert.link_loops if x.face in old), None)
        if src_l: l[uvk].uv = src_l[uvk].uv
bmesh.ops.recalc_face_normals(bm, faces=new)
left = sum(e.is_boundary for e in bm.edges)
print("holes patched faces", len(new), "open edges left", left)

# 6b. cap what holes_fill leaves. A rim that touches a non-manifold vertex (TRELLIS's overlapping
# shells) is not a simple loop, so holes_fill skips it. Each connected run of open edges gets a fan to
# its centre instead, every cap triangle wound against the face already on its edge, so it faces out.
# Runs wider than CAP_SPAN of the model are left open: those are openings, not holes.
CAP_SPAN = 0.2
seen, runs = set(), []
for e in bm.edges:
    if not e.is_boundary or e in seen: continue
    run, stack = [], [e]; seen.add(e)
    while stack:
        x = stack.pop(); run.append(x)
        for v in x.verts:
            for y in v.link_edges:
                if y.is_boundary and y not in seen: seen.add(y); stack.append(y)
    runs.append(run)
capped, wide = [], 0
bm.verts.index_update()
for run in runs:
    rim_verts = sorted({v for x in run for v in x.verts}, key=lambda v: v.index)  # a set's order would vary the sum
    centre = sum((v.co for v in rim_verts), Vector()) / len(rim_verts)
    if max((v.co - centre).length for v in rim_verts) > size * CAP_SPAN: wide += 1; continue
    hub = bm.verts.new(centre)
    hub_uv = None
    for x in run:
        lp = next(l for l in x.link_faces[0].loops if l.edge == x)  # runs a -> b on the old face
        a, b = lp.vert, lp.link_loop_next.vert
        try: f = bm.faces.new((b, a, hub))
        except ValueError: continue  # that triangle already exists
        f.smooth = True
        f.loops[0][uvk].uv = lp.link_loop_next[uvk].uv; f.loops[1][uvk].uv = lp[uvk].uv
        hub_uv = hub_uv or lp[uvk].uv.copy()
        f.loops[2][uvk].uv = hub_uv
        capped.append(f)
left = sum(e.is_boundary for e in bm.edges)
print("caps", len(runs) - wide, "cap faces", len(capped), "left wide", wide, "open edges left", left)

# 6c. face the way the outside sees it. Step 1's two-sided ray vote misreads thin, toothy rims, and a
# culled material then shows straight through them. From 48 directions round the sphere, a face that
# open air sees only from behind is flipped, and one it sees from both sides (a lone sheet) gets a
# reversed twin.
gold = np.pi * (3 - np.sqrt(5))
dirs = []
for i in range(48):
    z = 1 - 2 * (i + 0.5) / 48; r = np.sqrt(1 - z * z)
    dirs.append(Vector((r * np.cos(gold * i), r * np.sin(gold * i), z)))
bm.normal_update()
bvh = BVHTree.FromBMesh(bm)
flip, sheet = [], []
for f in bm.faces:
    if f.normal.length < 0.5: continue
    c = f.calc_center_median(); front = back = 0
    for d in dirs:
        side = d.dot(f.normal)
        if abs(side) < 0.05 or bvh.ray_cast(c + d * eps, d)[0] is not None: continue
        if side > 0: front += 1
        else: back += 1
    if back and not front: flip.append(f)
    elif back >= 2 and front >= 2: sheet.append(f)
bmesh.ops.reverse_faces(bm, faces=flip)
twin = bmesh.ops.duplicate(bm, geom=sheet)
bmesh.ops.reverse_faces(bm, faces=[g for g in twin["geom"] if isinstance(g, bmesh.types.BMFace)])
print("seen from behind, flipped", len(flip), "sheets doubled", len(sheet))
bm.to_mesh(me); bm.free()
# 7. double-sided: any gap/wrong-facing face that remains still shows a surface, never see-through
for m in me.materials: m.use_backface_culling = False
me.shade_smooth()
print("final faces", len(me.polygons))

bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_image_format="AUTO")
print("SAVED", dst)
