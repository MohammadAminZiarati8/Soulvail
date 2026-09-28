"""The Jungle's kit: its textures and its models, generated rather than drawn (M7-05b).

Run from the repository root with Blender 4.1, headless:

    "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background --factory-startup \
        --python Tools/Blender/jungle_kit.py

It writes into Assets/_Project/Art/Environment/Jungle/ and overwrites what it wrote last time, so
the files there are an output of this script and are changed here, never by hand:

- Textures/T_Jungle_Albedo.png       KayKit Forest Nature's palette atlas with its three strips
                                     repainted jungle-green and this kit's own swatches in the
                                     space KayKit leaves free. One atlas, so the KayKit pieces and
                                     these models share one material (GD 17.1).
- Textures/T_JungleRuins_Albedo.png  KayKit Dungeon's atlas, its stone turned moss-grey.
- Textures/T_JungleGround_Albedo.png The jungle floor: seamless, 12 m to a tile.
- *.fbx                              The pieces KayKit has no equivalent for.

Every colour obeys GD 16.4: muted greens, browns and greys, and nothing near the player's cyan,
danger's red-orange, Veilrot's violet or reward gold.
"""

import math
import os
import random

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Environment", "Jungle")
TEX = os.path.join(OUT, "Textures")
DUNGEON_ATLAS = os.path.join(ROOT, "Assets", "ThirdParty", "KayKit", "Dungeon", "Textures", "dungeon_texture.png")

# ---------------------------------------------------------------------------------------------
# The palette. KayKit's forest atlas is 1024 px square, in 128 x 256 px strips, each a vertical
# gradient from light (top) to dark; its models only use the three strips in column 0, and the
# rest is space it reserves for "your own colours". Column 0 is repainted; columns 1-7 are ours.
# ---------------------------------------------------------------------------------------------

ATLAS = 1024
COL_W, ROW_H = 128, 256

SWATCHES = {
    # KayKit's own three strips, repainted (column 0).
    "kk_foliage": (0, 0, "7FA85A", "1F4A2A"),
    "kk_bark": (0, 1, "7A6149", "3A2B20"),
    "kk_rock": (0, 2, "9AA496", "4A5550"),
    # This kit's swatches.
    "leaf": (1, 0, "8DB660", "3F6B35"),
    "leaf_deep": (2, 0, "4F7F45", "1C3D26"),
    "fern": (3, 0, "6E9A4E", "2D5230"),
    "vine": (4, 0, "6B8744", "2E4524"),
    "moss": (5, 0, "7E9C58", "3E5A34"),
    "lichen": (6, 0, "B8C4A0", "7F8C6C"),
    "canopy_dark": (7, 0, "3A5A38", "16281A"),
    "bark": (1, 1, "8A7156", "4A392C"),
    "bark_dark": (2, 1, "5A4636", "261C16"),
    "root": (3, 1, "6E5A46", "33271F"),
    "earth": (4, 1, "7D6A50", "45382A"),
    "stone": (1, 2, "9CA595", "4A544D"),
    "stone_dark": (2, 2, "6E7670", "2E3431"),
    "bone": (3, 2, "DAD3C0", "A69E88"),
}
SWATCH_NAMES = list(SWATCHES)


def rgb(hex_code):
    return np.array([int(hex_code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def uv_for(swatch, t):
    """Where on the atlas a surface of `swatch` samples, `t` from 0 (the light top) to 1."""
    col, row = SWATCHES[swatch][0], SWATCHES[swatch][1]
    t = min(1.0, max(0.0, t))
    u = (col * COL_W + COL_W * 0.5) / ATLAS
    y = row * ROW_H + 12 + t * (ROW_H - 24)  # a margin, so mip levels bleed less
    return u, 1.0 - y / ATLAS


# ---------------------------------------------------------------------------------------------
# Textures
# ---------------------------------------------------------------------------------------------

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
    save_png(img, os.path.join(TEX, "T_Jungle_Albedo.png"))


def rgb_to_hsv(c):
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    mx, mn = c.max(-1), c.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rm, gm = nz & (mx == r), nz & (mx == g) & (mx != r)
    bm = nz & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return np.stack([h / 6.0, s, mx], -1)


def hsv_to_rgb(c):
    h, s, v = c[..., 0] * 6.0, c[..., 1], c[..., 2]
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    table = [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]
    out = np.zeros(c.shape, np.float32)
    for k, (r, g, b) in enumerate(table):
        m = i == k
        out[..., 0][m], out[..., 1][m], out[..., 2][m] = r[m], g[m], b[m]
    return out


def build_ruins_atlas():
    """KayKit Dungeon's atlas with its stone turned moss-grey and its wood darkened."""
    src = bpy.data.images.load(DUNGEON_ATLAS)
    w, h = src.size
    px = np.array(src.pixels[:], np.float32).reshape(h, w, 4)[..., :3]
    bpy.data.images.remove(src)
    px = np.flipud(px)
    hsv = rgb_to_hsv(px)
    hue, sat, val = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    grey = sat < 0.28
    brown = ~grey & (hue > 0.02) & (hue < 0.13)
    other = ~grey & ~brown
    hue = np.where(grey, 0.27, hue)                                   # stone: towards moss green
    sat = np.where(grey, np.minimum(0.10 + sat * 0.35, 0.20), sat)
    val = np.where(grey, val * 0.92, val)
    # Wood, and the atlas's oranges, reds and golds, which share its hue band: capped, because
    # a salmon or a peach left on a ruin still reads as danger or reward (GD 16.4).
    sat = np.where(brown, np.minimum(sat * 0.5, 0.28), sat)
    val = np.where(brown, val * 0.78, val)
    hue = np.where(other, 0.25, hue)                                  # anything else loud: moss
    sat = np.where(other, np.minimum(sat * 0.3, 0.2), sat)
    val = np.where(other, val * 0.80, val)
    save_png(hsv_to_rgb(np.stack([hue, sat, val], -1)), os.path.join(TEX, "T_JungleRuins_Albedo.png"))


def periodic_noise(n, size, seed):
    """Smooth noise that tiles: white noise low-passed in the frequency domain."""
    rng = np.random.default_rng(seed)
    spectrum = np.fft.fft2(rng.standard_normal((n, n)))
    fx = np.fft.fftfreq(n)[None, :]
    fy = np.fft.fftfreq(n)[:, None]
    out = np.real(np.fft.ifft2(spectrum * np.exp(-((np.sqrt(fx * fx + fy * fy) * size) ** 2))))
    return (out - out.mean()) / out.std()


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def build_ground():
    """The jungle floor, 12 m to a tile: moss in three close tones, bare earth, and leaf litter.

    Posterised on purpose — flat bands with a narrow soft edge, which is what a low-poly frame
    expects of its ground — and kept low in contrast: the ground is the quietest thing on screen.
    """
    n = 1024
    zones = 0.75 * periodic_noise(n, 260, 1) + 0.25 * periodic_noise(n, 90, 2)
    earth = 0.8 * periodic_noise(n, 150, 3) + 0.2 * periodic_noise(n, 45, 4)
    litter = periodic_noise(n, 9, 5)
    img = np.empty((n, n, 3), np.float32)
    # Three close tones: at arena scale, wider steps read as a camouflage print (first render).
    dark, mid, light = rgb("44623A"), rgb("4E6E3F"), rgb("587A46")
    img[:] = dark
    img = img * (1 - smoothstep(-0.45, -0.30, zones)[..., None]) + mid * smoothstep(-0.45, -0.30, zones)[..., None]
    img = img * (1 - smoothstep(0.55, 0.70, zones)[..., None]) + light * smoothstep(0.55, 0.70, zones)[..., None]
    rim = smoothstep(1.30, 1.40, earth)[..., None]
    img = img * (1 - rim) + rgb("58503D") * rim
    soil = smoothstep(1.50, 1.60, earth)[..., None]
    img = img * (1 - soil) + rgb("675A45") * soil
    fleck_light = smoothstep(2.3, 2.5, litter)[..., None]
    img = img * (1 - fleck_light) + rgb("6E8F52") * fleck_light
    fleck_dark = smoothstep(2.35, 2.55, -litter)[..., None]
    img = img * (1 - fleck_dark) + rgb("37502E") * fleck_dark
    save_png(img, os.path.join(TEX, "T_JungleGround_Albedo.png"))


# ---------------------------------------------------------------------------------------------
# Models. Blender is Z-up; the FBX export converts to Unity's Y-up. Every model stands on its
# origin, and every face samples one swatch, darker towards its bottom — KayKit's own trick.
# ---------------------------------------------------------------------------------------------

class Builder:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.bm = bmesh.new()
        self.shade = self.bm.verts.layers.float.new("shade")
        self.swatch = self.bm.faces.layers.int.new("swatch")

    def vert(self, co, shade):
        v = self.bm.verts.new(co)
        v[self.shade] = shade
        return v

    def face(self, verts, swatch):
        f = self.bm.faces.new(verts)
        f[self.swatch] = SWATCH_NAMES.index(swatch)
        return f

    def jitter(self, amount):
        return Vector((self.rng.uniform(-amount, amount), self.rng.uniform(-amount, amount), self.rng.uniform(-amount, amount)))

    # -- primitives ---------------------------------------------------------------------------

    def tube(self, path, radii, sides, swatch, shade=(0.15, 0.9), cap=False, twist=0.0):
        """A tapered tube through `path`, open at the bottom; the rings are CCW seen from above."""
        rings = []
        for i, (p, r) in enumerate(zip(path, radii)):
            s = shade[0] + (shade[1] - shade[0]) * (1 - i / (len(path) - 1))
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides + twist * i
                ring.append(self.vert(p + Vector((math.cos(a) * r, math.sin(a) * r, 0)), s))
            rings.append(ring)
        for lo, hi in zip(rings, rings[1:]):
            for k in range(sides):
                self.face([lo[k], lo[(k + 1) % sides], hi[(k + 1) % sides], hi[k]], swatch)
        if cap:
            self.face(rings[-1], swatch)
        return rings

    def strand(self, path, radius, swatch, sides=4, shade=(0.2, 0.8)):
        """A thin closed tube along any path — a vine, a stem. Its frames follow the path."""
        verts = []
        for i, p in enumerate(path):
            fwd = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            side = fwd.cross(Vector((0, 0, 1)))
            if side.length < 1e-3:
                side = fwd.cross(Vector((1, 0, 0)))
            side.normalize()
            up = side.cross(fwd).normalized()
            s = shade[0] + (shade[1] - shade[0]) * (i / (len(path) - 1))
            ring = [self.vert(p + (side * math.cos(2 * math.pi * k / sides) + up * math.sin(2 * math.pi * k / sides)) * radius, s) for k in range(sides)]
            verts.append(ring)
        faces = []
        for a, b in zip(verts, verts[1:]):
            for k in range(sides):
                faces.append(self.face([a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]], swatch))
        faces.append(self.face(list(reversed(verts[0])), swatch))
        faces.append(self.face(verts[-1], swatch))
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)

    def blade(self, path, widths, thickness, swatch, shade=(0.55, 0.1)):
        """A leaf or a frond: a thin closed slab along `path`, `widths` across it, lying flat."""
        top, bottom = [], []
        for i, (p, w) in enumerate(zip(path, widths)):
            fwd = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            side = fwd.cross(Vector((0, 0, 1)))
            if side.length < 1e-3:
                side = Vector((1, 0, 0))
            side.normalize()
            up = side.cross(fwd).normalized()
            s = shade[0] + (shade[1] - shade[0]) * (i / (len(path) - 1))
            w = max(w, 0.004)
            half = up * (thickness * 0.5)
            top.append((self.vert(p - side * w * 0.5 + half, s), self.vert(p + side * w * 0.5 + half, s)))
            bottom.append((self.vert(p - side * w * 0.5 - half, s), self.vert(p + side * w * 0.5 - half, s)))
        faces = []
        for i in range(len(path) - 1):
            (tl0, tr0), (tl1, tr1) = top[i], top[i + 1]
            (bl0, br0), (bl1, br1) = bottom[i], bottom[i + 1]
            faces.append(self.face([tl0, tr0, tr1, tl1], swatch))
            faces.append(self.face([bl1, br1, br0, bl0], swatch))
            faces.append(self.face([tl0, tl1, bl1, bl0], swatch))
            faces.append(self.face([tr1, tr0, br0, br1], swatch))
        faces.append(self.face([top[0][1], top[0][0], bottom[0][0], bottom[0][1]], swatch))
        faces.append(self.face([top[-1][0], top[-1][1], bottom[-1][1], bottom[-1][0]], swatch))
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)

    def blob(self, center, radii, swatch, subdivisions=2, roughness=0.08, shade=(0.05, 0.85)):
        """A chunky faceted lump — a canopy clump, a moss cushion."""
        made = bmesh.ops.create_icosphere(self.bm, subdivisions=subdivisions, radius=1.0, matrix=Matrix.Identity(4))
        verts = made["verts"]
        for v in verts:
            v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2])) + center + self.jitter(roughness * max(radii))
        zs = [v.co.z for v in verts]
        lo, hi = min(zs), max(zs)
        for v in verts:
            v[self.shade] = shade[0] + (shade[1] - shade[0]) * (1 - (v.co.z - lo) / max(hi - lo, 1e-3))
        for f in {f for v in verts for f in v.link_faces}:
            f[self.swatch] = SWATCH_NAMES.index(swatch)

    def fin(self, angle, r_in, r_out, height, thickness, swatch):
        """A buttress root: a closed wedge from the trunk down to the ground."""
        d = Vector((math.cos(angle), math.sin(angle), 0))
        s = Vector((-d.y, d.x, 0)) * (thickness * 0.5)
        it, ib, tip = d * r_in + Vector((0, 0, height)), d * r_in + Vector((0, 0, -0.15)), d * r_out + Vector((0, 0, -0.05))
        v = [self.vert(it + s, 0.35), self.vert(ib + s, 0.9), self.vert(tip + s * 0.4, 0.8),
             self.vert(it - s, 0.35), self.vert(ib - s, 0.9), self.vert(tip - s * 0.4, 0.8)]
        faces = [self.face([v[0], v[1], v[2]], swatch), self.face([v[3], v[5], v[4]], swatch),
                 self.face([v[0], v[2], v[5], v[3]], swatch), self.face([v[1], v[4], v[5], v[2]], swatch),
                 self.face([v[0], v[3], v[4], v[1]], swatch)]
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)

    # -- output -------------------------------------------------------------------------------

    def finish(self, name):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        uv = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            swatch = SWATCH_NAMES[f[self.swatch]]
            for loop in f.loops:
                loop[uv].uv = uv_for(swatch, loop.vert[self.shade])
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        for p in mesh.polygons:
            p.use_smooth = False
        material = bpy.data.materials.get("jungle") or bpy.data.materials.new("jungle")
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        export(obj)
        print(f"  {name}: {sum(len(p.vertices) - 2 for p in mesh.polygons)} triangles")


def export(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, obj.name + ".fbx"),
        use_selection=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        object_types={"MESH"},
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )
    bpy.data.objects.remove(obj)


def arc(start, direction, length, rise, droop, segments):
    """A path that leaves `start` along `direction`, climbs by `rise` and falls by `droop`."""
    pts = []
    for i in range(segments + 1):
        t = i / segments
        pts.append(start + direction * (length * t) + Vector((0, 0, rise * math.sin(t * math.pi * 0.6) - droop * t * t)))
    return pts


# -- the pieces --------------------------------------------------------------------------------

def jungle_tree(name, seed, height, crown, clumps):
    """A tall tree with a buttressed foot and a chunky crown — for edges and outskirts only.

    Its crown is well above head height, which is exactly why it never stands inside the play
    area: from a 57-degree camera a crown there would hide the player (GD 7.2).
    """
    b = Builder(seed)
    lean = Vector((b.rng.uniform(-0.6, 0.6), b.rng.uniform(-0.6, 0.6), 0))
    path = [Vector((0, 0, -0.2)) + lean * (t * t) + Vector((0, 0, height * t)) for t in (0, 0.25, 0.5, 0.75, 1.0)]
    b.tube(path, [0.55, 0.42, 0.36, 0.31, 0.27], 7, "bark", shade=(0.2, 0.95))
    for k in range(5):
        b.fin(2 * math.pi * k / 5 + b.rng.uniform(-0.3, 0.3), 0.45, b.rng.uniform(1.3, 1.8), b.rng.uniform(1.0, 1.6), 0.16, "root")
    top = path[-1]
    b.blob(top + Vector((0, 0, crown * 0.25)), (crown, crown, crown * 0.7), "leaf", roughness=0.1)
    for k in range(clumps):
        a = 2 * math.pi * k / clumps + b.rng.uniform(-0.4, 0.4)
        off = Vector((math.cos(a), math.sin(a), 0)) * crown * b.rng.uniform(0.8, 1.0)
        size = crown * b.rng.uniform(0.62, 0.8)
        centre = top + off + Vector((0, 0, -crown * b.rng.uniform(0.15, 0.4)))
        b.blob(centre, (size, size, size * 0.72), "leaf_deep", roughness=0.1)
        b.strand([top + Vector((0, 0, -crown * 0.8)), (top + centre) * 0.5 + Vector((0, 0, -0.4)), centre], 0.12, "bark_dark", sides=4)
    b.finish(name)


def buttress_trunk(name, seed):
    """Cover: the broken stump of a giant, 3.6 m tall, on five buttress roots. No crown at all."""
    b = Builder(seed)
    sides, h = 10, 3.6
    path = [Vector((0, 0, -0.2)), Vector((0, 0, 1.2)), Vector((0, 0, 2.4)), Vector((0, 0, h))]
    rings = b.tube(path, [1.05, 0.95, 0.88, 0.82], sides, "bark", shade=(0.15, 0.95))
    for v in rings[-1]:
        v.co.z += b.rng.uniform(-0.45, 0.25)  # the break is jagged
    hollow = b.vert(Vector((0, 0, h - 0.55)), 0.2)
    top = rings[-1]
    for k in range(sides):
        b.face([top[k], top[(k + 1) % sides], hollow], "moss")
    for k in range(5):
        b.fin(2 * math.pi * k / 5 + b.rng.uniform(-0.25, 0.25), 0.9, b.rng.uniform(1.8, 2.1), b.rng.uniform(1.9, 2.4), 0.3, "root")
    # Moss hugs the wood: flat cushions, because upright ones read as leaves from above.
    b.blob(Vector((0.35, 0.3, 3.3)), (0.6, 0.5, 0.2), "moss", subdivisions=1, roughness=0.03)
    b.blob(Vector((-0.86, 0.1, 2.1)), (0.18, 0.55, 0.45), "moss", subdivisions=1, roughness=0.03)
    for z, a in ((1.7, 3.6), (2.2, 3.9)):
        d = Vector((math.cos(a), math.sin(a), 0))
        b.blob(d * 0.95 + Vector((0, 0, z)), (0.38, 0.38, 0.1), "bone", subdivisions=1, roughness=0.02)
    b.finish(name)


def big_leaf_plant(name, seed, leaves, size):
    """Dressing: a clump of broad leaves on thin stems, arching out and down."""
    b = Builder(seed)
    for k in range(leaves):
        a = 2 * math.pi * k / leaves + b.rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        stem_top = d * (0.25 * size) + Vector((0, 0, b.rng.uniform(0.45, 0.8) * size))
        b.strand([Vector((0, 0, 0)), d * (0.1 * size) + Vector((0, 0, stem_top.z * 0.6)), stem_top], 0.035 * size, "vine", sides=4)
        length = b.rng.uniform(0.9, 1.2) * size
        path = arc(stem_top, (d + Vector((0, 0, 0.35))).normalized(), length, 0.12 * size, 0.55 * size, 5)
        widths = [w * size for w in (0.12, 0.42, 0.5, 0.44, 0.28, 0.02)]
        b.blade(path, widths, 0.035, "leaf", shade=(0.6, 0.08))
    b.finish(name)


def fern(name, seed, fronds, size):
    """Dressing: a low fern, its fronds rising out of a moss cushion and bowing to the ground."""
    b = Builder(seed)
    b.blob(Vector((0, 0, 0.05)), (0.22 * size, 0.22 * size, 0.12 * size), "moss", subdivisions=1, roughness=0.03)
    for k in range(fronds):
        a = 2 * math.pi * k / fronds + b.rng.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0))
        path = arc(Vector((0, 0, 0.1 * size)), (d + Vector((0, 0, 0.9))).normalized(), b.rng.uniform(0.75, 1.0) * size, 0.35 * size, 0.7 * size, 6)
        widths = [w * size for w in (0.03, 0.14, 0.2, 0.19, 0.15, 0.09, 0.01)]
        b.blade(path, widths, 0.025, "fern", shade=(0.65, 0.1))
    b.finish(name)


def vine_curtain(name, seed, width, height, parted):
    """The gate's seal, or — `parted` — the same vines pulled to the sides once it opens.

    Sized to KayKit Dungeon's wall_doorway, whose opening is 2.0 m wide and 2.75 m tall.
    """
    b = Builder(seed)
    b.strand([Vector((-width * 0.55, 0, height)), Vector((0, 0.05, height + 0.1)), Vector((width * 0.55, 0, height))], 0.14, "root", sides=5)
    count = 12
    for k in range(count):
        x0 = -width * 0.5 + width * (k + 0.5) / count
        sway, phase = b.rng.uniform(0.05, 0.14), b.rng.uniform(0, math.tau)
        pts = []
        for i in range(7):
            t = i / 6
            x = x0 + math.sin(phase + t * 5) * sway
            if parted:
                side = -1 if x0 < 0 else 1
                x = side * (width * 0.5 - 0.1) + (x0 - side * (width * 0.5 - 0.1)) * (1 - t) ** 0.35 * 0.25
            y = math.cos(phase + t * 3) * 0.08 + (0.12 if k % 2 else -0.08)
            z = height - (height + 0.05) * t * (b.rng.uniform(0.75, 1.0) if parted else 1.0)
            pts.append(Vector((x, y, z)))
        b.strand(pts, b.rng.uniform(0.045, 0.075), "vine", sides=4)
        for i in range(2, 6, 2):
            p = pts[i]
            d = Vector((b.rng.uniform(-1, 1), b.rng.uniform(-1, 1), -0.6)).normalized()
            b.blade([p, p + d * 0.22, p + d * 0.4], [0.05, 0.16, 0.02], 0.02, "leaf_deep", shade=(0.5, 0.2))
    if not parted:
        for k in range(3):
            x = b.rng.uniform(-0.6, 0.6) * width * 0.5
            b.strand([Vector((x - 0.9, 0.18, height - 0.2)), Vector((x, 0.22, height * 0.5)), Vector((x + 0.9, 0.18, 0.1))], 0.07, "root", sides=4)
    b.finish(name)


def hanging_vines(name, seed, width):
    """Wall dressing: vines spilling over a wall's top. The origin is the top edge they hang from."""
    b = Builder(seed)
    for k in range(6):
        x = -width * 0.5 + width * (k + 0.5) / 6 + b.rng.uniform(-0.1, 0.1)
        length = b.rng.uniform(1.0, 2.4)
        pts = [Vector((x, -0.1, 0.05)), Vector((x, 0.12, -0.05))] + [Vector((x + math.sin(i * 1.3 + k) * 0.08, 0.16, -length * i / 4)) for i in range(1, 5)]
        b.strand(pts, b.rng.uniform(0.04, 0.06), "vine", sides=4)
        for p in pts[2::2]:
            d = Vector((b.rng.uniform(-1, 1), 1.0, -0.4)).normalized()
            b.blade([p, p + d * 0.2, p + d * 0.36], [0.05, 0.15, 0.02], 0.02, "leaf_deep", shade=(0.45, 0.2))
    b.blob(Vector((0, 0, 0.05)), (width * 0.5, 0.3, 0.14), "moss", subdivisions=1, roughness=0.02)
    b.finish(name)


def main():
    os.makedirs(TEX, exist_ok=True)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    print("textures")
    build_atlas()
    build_ruins_atlas()
    build_ground()
    print("models")
    jungle_tree("JungleTree_A", 11, 7.5, 2.6, 4)
    jungle_tree("JungleTree_B", 23, 9.0, 3.0, 5)
    buttress_trunk("ButtressTrunk_A", 31)
    big_leaf_plant("BigLeafPlant_A", 41, 7, 1.0)
    big_leaf_plant("BigLeafPlant_B", 43, 5, 1.4)
    fern("Fern_A", 51, 10, 1.0)
    vine_curtain("VineCurtain_Sealed", 61, 2.3, 3.0, parted=False)
    vine_curtain("VineCurtain_Parted", 61, 2.3, 3.0, parted=True)
    hanging_vines("HangingVines_A", 71, 3.6)


main()
