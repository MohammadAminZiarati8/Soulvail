"""The shared enemy atlas, T_Enemy_Albedo: every enemy body samples it through M_Enemy (M7-05f).

Imported by each body's script (rootling.py, frog.py), and each writes the whole atlas, so a re-run of
either leaves the same bytes. A body's swatches are added here, never in its own script, which is what
keeps one body's re-run from erasing another's columns.

The layout is M7-05b's at half the size: 512 px square in 64 x 128 px strips, each a vertical gradient
from light (top) to dark. Columns 0-1 are the Rootling's, 2-3 the Frog's, 4-7 free.
"""

import os

import bpy
import numpy as np

ATLAS = 512
COL_W, ROW_H = 64, 128

SWATCHES = {
    # The Rootling (M7-05f): bone, fungus-white and dry bark.
    "bone": (0, 0, "E2DBC8", "A89F8A"),
    "fungus": (0, 1, "ECE7D8", "B8B09D"),
    "bark": (0, 2, "BCAE96", "7E705D"),
    "lichen": (0, 3, "D2D2C0", "9A9B88"),
    "bark_dark": (1, 0, "A39380", "675A4B"),
    "socket": (1, 1, "3C352E", "221D19"),
    # The Frog (M7-05n): a warm lime skin with darker spots, a cream belly, jaw and toe pads, a dark
    # mouth that shows when the jaw drops, and a glint in each eye. The reference sheet's orange eyes
    # are crimson, because GD 16.4 keeps red-orange for danger alone and the frog's wind-up glows it.
    "frog_green": (2, 0, "B0D850", "66A02C"),
    "frog_shine": (2, 1, "FFFFFF", "F0ECE2"),
    "frog_mouth": (2, 2, "6A2433", "3C121D"),
    "frog_spot": (2, 3, "5F9632", "3A6A20"),
    "frog_belly": (3, 0, "EEE6B2", "C9B983"),
    "frog_red": (3, 1, "C8203A", "7E0F22"),
    "frog_pupil": (3, 2, "1B1616", "080606"),
    "frog_tongue": (3, 3, "EC8FA2", "B55570"),
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


def build_atlas(tex_dir):
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
    os.makedirs(tex_dir, exist_ok=True)
    save_png(img, os.path.join(tex_dir, "T_Enemy_Albedo.png"))
    return img
