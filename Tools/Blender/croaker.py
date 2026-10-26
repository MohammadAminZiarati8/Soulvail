"""Old Croaker, the Jungle's first boss: the Frog grown old and huge, on the Frog's own skeleton, with
clips of its own (JG-04a).

Run from the repository root with Blender 4.1, headless, with Python's hash seed fixed (rootling.py's
docstring says why):

    PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background
        --factory-startup --python Tools/Blender/croaker.py [-- --pose-sheet <out.png>]

It writes, overwriting what it wrote last time, so both files are an output of this script:

- Assets/_Project/Art/Enemies/Croaker.fbx                 one skinned mesh on Rig_Frog, with its clips
- Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png the shared enemy atlas (enemy_atlas.py)

**The Frog's builder, not a copy of it.** frog.py's shells, skeleton, pose solver and clip writer are
imported, so a fix to the Frog's body reaches the boss too. The bones are Rig_Frog's, with the same
names and parents (AM_Frog_UpperBody masks it), set SCALE times further apart. The shells are built in
the Frog's metres and scaled at the end, and every pose below is written in the Frog's metres too
(frog.Poser's `unit`), so a hop here reads like a hop there.

**Old, not just big.** Over the Frog's shells: a darker, mossier skin (`croak_skin`), a lumpy moss mat
over the back (`croak_moss`), heavy lids drooping over the crimson eyes, a throat sac under the chin,
pale warts on the head and thighs, and a crown of three small mushrooms (wine caps with cream dots, on
cream stalks) between the eyes. The belly, spots, mouth, eyes and tongue keep the Frog's swatches; the
warts are `pitcher_skin`, the caps `pitcher_rim` and their dots `puff_cap`, the stalks `puff_stalk`.
Every colour obeys GD 16.4 (enemy_atlas.py), and the eyes stay crimson.

About 3.4 m tall at 1x, crown included: three Frogs as they stand in the arena (Frog.prefab is 1.2x).
One boss stands alone, so its budget is BOSS_BUDGET, twice the crowd's.

**The clips.** 30 fps, in place, no root motion. JG-04b's telegraphs are timed to these frames, so
the controller plays them at speed 1:

    Idle     60 f  loop   slow, heavy breathing; the sac swells; a glance
    Hop      24 f  loop   the Frog's hop at three quarters' speed: 3.2 m of ground a hop at 1x
    Slam     45 f         crouch to frame 12, launch at 14, held in the air 17-22, falling at 24,
                          IMPACT ON FRAME 27 (0.9 s, the slam's telegraph), squash to 30, up by 45
    Lash     42 f         wind-up to frame 20, THE TONGUE AT FULL REACH ON FRAME 24 (0.8 s, the lash's
                          telegraph), about 9 m out from the body's centre at 1x; drawn back by 35
    Croak    48 f         nose up, the throat sac swells and pulses, the call on frame 30 (1.0 s)
    Hit      12 f         the Frog's flinch
    Death    36 f         the Frog's roll onto its back, at five sixths' speed; belly up by frame 18
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True  # no __pycache__ beside the scripts
import frog  # noqa: E402
from enemy_atlas import build_atlas  # noqa: E402
from frog import P, Part, SIDES, ball, blob, capsule, dome, facing, landing, limbs, pitch, place, surface, v  # noqa: E402

SCALE = 3.75  # the Frog's metres to the Croaker's
BOSS_BUDGET = (2500, 5000)
NAME = "Croaker.fbx"

SKIN = {  # the Frog's swatches the Croaker repaints
    "frog_green": "croak_skin",
}


# ---------------------------------------------------------------------------------------------
# The boss's shells, in the Frog's metres, over the Frog's.
# ---------------------------------------------------------------------------------------------


def eye_centre(s):
    """Where frog.build() puts an eyeball, so a lid can sit on it."""
    socket = v(0.25 * s, -0.31, 0.64)
    return socket + v(0.50 * s, -0.78, 0.40).normalized() * 0.02 + v(0, 0, 0.05)


def dress():
    # The moss mat: a lumpy shell just proud of the back, narrower than it, so the skin shows on the
    # flanks and the mat's edge wanders where the two shells cross.
    def moss_warp(q, p):
        lump = 1.0 + 0.07 * math.sin(9.0 * p.x + 1.0) * math.sin(8.0 * p.y + 2.0) + 0.04 * math.sin(13.0 * p.y)
        return v(q.x * lump, q.y * lump, q.z * lump)
    place(blob(3), v(0, 0.12, 0.345), (0.31, 0.33, 0.23), "croak_moss", "body", pitch(-22), moss_warp)

    # Heavy lids over the top of each eye, drooping forward: what makes it old rather than big.
    for _, s in SIDES:
        eye = eye_centre(s)
        place(blob(2), eye + v(0.01 * s, 0.02, 0.10), (0.15, 0.15, 0.075), "croak_skin", "head", pitch(14))

    # The throat sac: a cream shell under the chin, on the jaw, so the clips' throat scale swells it.
    place(blob(2), v(0, -0.30, 0.355), (0.27, 0.21, 0.09), "frog_belly", "jaw", pitch(-6))

    # The crown: three small mushrooms on the head, between and behind the eyes, the middle tallest.
    head = surface(("croak_skin",), ("head",))
    for x, y, height, lean, cap in ((0.0, -0.13, 0.20, v(0, 0.25, 1), 0.070),
                                    (-0.12, -0.08, 0.13, v(-0.45, 0.2, 1), 0.055),
                                    (0.11, -0.06, 0.15, v(0.40, 0.15, 1), 0.060)):
        foot, _ = landing(head, x, y)
        foot = foot - v(0, 0, 0.02)
        top = foot + lean.normalized() * height
        frog.PARTS.append(Part(*capsule(foot, top, 0.024, 0.019, segments=6, cap=1), "puff_stalk", {"head": 1.0}))

        def cap_warp(q, p):
            return v(q.x, q.y, q.z * (0.3 if p.z < 0 else 1.0))  # flat underneath
        turn = facing(lean)
        place(blob(2, 0.85), top, (cap, cap, cap * 0.62), "pitcher_rim", "head", turn, cap_warp)
        for k in range(3):
            angle = 2.1 * k + x * 7.0
            dot = top + turn @ v(math.cos(angle) * cap * 0.55, math.sin(angle) * cap * 0.55, cap * 0.50)
            place(ball(6, 3), dot, (cap * 0.2, cap * 0.2, cap * 0.12), "puff_cap", "head", turn)


def warts():
    """Pale warts pressed into the skin of the head and thighs, as frog.spots() presses its spots."""
    tree = surface(("croak_skin",), ("head", "thigh.l", "thigh.r"))
    for x, y, r, bone in ((0.37, -0.16, 0.045, "head"), (-0.35, -0.21, 0.04, "head"), (0.22, -0.03, 0.035, "head"),
                          (-0.24, -0.05, 0.04, "head"), (0.11, -0.47, 0.03, "head"), (-0.13, -0.45, 0.032, "head"),
                          (0.33, 0.12, 0.045, "thigh.l"), (0.40, 0.28, 0.035, "thigh.l"),
                          (-0.35, 0.24, 0.045, "thigh.r"), (-0.30, 0.04, 0.035, "thigh.r")):
        hit, normal = landing(tree, x, y)
        place(dome(), hit, (r, r * 0.9, 0.026), "pitcher_skin", bone, facing(normal, v(0, -1, 0)), closed=False)


def grow():
    """Every shell from the Frog's metres to the Croaker's."""
    for part in frog.PARTS:
        part.verts = [p * SCALE for p in part.verts]


# ---------------------------------------------------------------------------------------------
# The clips, in the Frog's metres (frog.Poser scales them by SCALE).
# ---------------------------------------------------------------------------------------------


def retimed(keys, factor):
    return {round(f * factor): pose for f, pose in keys.items()}


def clips():
    rest = P()
    tuck = lambda d, pitch_: ("c", d, pitch_)  # noqa: E731
    braced = limbs(arm=("w", v(0.08, -0.04, 0), 0), leg=("w", v(0.06, 0.02, 0), 0))
    flown = limbs(arm=tuck(v(0.02, -0.16, 0.04), -50), leg=tuck(v(-0.10, 0.45, 0.0), 165))
    the_frogs = frog.clips()
    return {
        "Idle": (True, {
            0: rest,
            15: P(offset=v(0, 0, -0.006), throat=1.06),
            22: P(offset=v(0, 0, -0.003)),
            32: P(offset=v(0, 0, 0.004), yaw=4.0, head=-2.0),
            45: P(offset=v(0, 0, 0.002), yaw=1.5, throat=1.05, jaw=2.0),
            52: P(),
            60: rest,
        }),
        "Hop": (True, retimed(the_frogs["Hop"][1], 4 / 3)),
        "Slam": (False, {
            0: rest,
            6: P(offset=v(0, 0.03, -0.06), body=(-6, 0, 0), head=-4.0, throat=1.04),
            10: P(offset=v(0, 0.05, -0.10), body=(-10, 0, 0), head=-8.0, throat=1.06),
            12: P(offset=v(0, 0.05, -0.11), body=(-11, 0, 0), head=-8.0, throat=1.06),
            14: P(limbs(arm=tuck(v(0, -0.15, 0.10), -40), leg=("w", v(0, 0.06, 0.12), 50)), offset=v(0, -0.06, 0.16), body=(-24, 0, 0), jaw=6.0),
            17: P(flown, offset=v(0, -0.04, 0.40), body=(-10, 0, 0), jaw=8.0),
            22: P(flown, offset=v(0, -0.04, 0.44), body=(-4, 0, 0), jaw=8.0),
            24: P(limbs(arm=tuck(v(0.06, -0.10, -0.10), 10), leg=tuck(v(-0.04, 0.30, -0.02), 120)), offset=v(0, -0.02, 0.26), body=(8, 0, 0), head=-4.0, jaw=4.0),
            27: P(braced, offset=v(0, 0, -0.10), body=(10, 0, 0), spine=4.0, head=6.0, jaw=18.0),
            30: P(braced, offset=v(0, 0, -0.12), body=(8, 0, 0), spine=3.0, head=4.0, jaw=14.0),
            36: P(offset=v(0, 0, -0.04), body=(3, 0, 0), jaw=4.0),
            45: rest,
        }),
        "Lash": (False, {
            0: rest,
            10: P(offset=v(0, 0.05, -0.04), body=(-10, 0, 0), spine=-4.0, head=-8.0, throat=1.06, jaw=4.0),
            20: P(offset=v(0, 0.07, -0.06), body=(-14, 0, 0), spine=-5.0, head=-12.0, throat=1.10, jaw=8.0),
            24: P(offset=v(0, -0.10, -0.01), body=(12, 0, 0), spine=4.0, head=6.0, jaw=30.0, tongue=2.5),
            27: P(offset=v(0, -0.09, -0.01), body=(11, 0, 0), spine=3.0, head=5.0, jaw=28.0, tongue=2.45),
            31: P(offset=v(0, -0.05, 0.0), body=(5, 0, 0), jaw=18.0, tongue=0.6),
            35: P(offset=v(0, -0.02, 0.0), body=(2, 0, 0), jaw=4.0),
            42: rest,
        }),
        "Croak": (False, {
            0: rest,
            6: P(offset=v(0, 0.02, 0.02), body=(-12, 0, 0), head=-6.0, throat=1.06, jaw=3.0),
            12: P(offset=v(0, 0.02, 0.03), body=(-14, 0, 0), head=-8.0, throat=1.20, jaw=4.0),
            16: P(offset=v(0, 0.02, 0.03), body=(-14, 0, 0), head=-8.0, throat=1.08, jaw=3.0),
            21: P(offset=v(0, 0.02, 0.03), body=(-14, 0, 0), head=-8.0, throat=1.24, jaw=6.0),
            25: P(offset=v(0, 0.02, 0.03), body=(-14, 0, 0), head=-8.0, throat=1.10, jaw=4.0),
            30: P(offset=v(0, 0.02, 0.04), body=(-16, 0, 0), head=-10.0, throat=1.30, jaw=14.0),
            35: P(offset=v(0, 0.02, 0.03), body=(-12, 0, 0), head=-6.0, throat=1.12, jaw=8.0),
            40: P(offset=v(0, 0.01, 0.01), body=(-6, 0, 0), head=-2.0, throat=1.04, jaw=2.0),
            48: rest,
        }),
        "Hit": the_frogs["Hit"],
        "Death": (False, retimed(the_frogs["Death"][1], 1.2)),
    }


SHEET = {
    "Idle": (0, 15, 32, 45),
    "Hop": (0, 4, 8, 12, 16, 20),
    "Slam": (0, 12, 14, 22, 27, 36),
    "Lash": (0, 20, 24, 27, 31, 42),
    "Croak": (0, 12, 16, 30, 40),
    "Hit": (0, 3, 7, 12),
    "Death": (0, 6, 13, 18, 23, 36),
}


def reach(arm):
    """Where the tongue's tip is at Lash's reach frame, measured from the body's centre on the ground."""
    import bpy
    arm.animation_data.action = bpy.data.actions["Lash"]
    bpy.context.scene.frame_set(24)
    tip = arm.matrix_world @ arm.pose.bones["tongue.tip"].head
    arm.animation_data.action = None
    bpy.context.scene.frame_set(0)
    print(f"  Lash: the tongue's tip is {(-tip.y):.2f} m out at frame 24")


def main():
    import bpy
    if os.environ.get("PYTHONHASHSEED") != "0":
        sys.exit("croaker.py: set PYTHONHASHSEED=0 before starting Blender, or every re-run rewrites the "
                 "FBX's object ids (rootling.py's docstring).")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    print("atlas")
    atlas = build_atlas(frog.TEX)
    print("model")
    arm = frog.build_armature(SCALE)
    frog.build()
    frog.spots()
    for part in frog.PARTS:
        part.swatch = SKIN.get(part.swatch, part.swatch)
    dress()
    warts()
    frog.cull()
    grow()
    body = frog.assemble(arm, "Croaker_Body", frog.TOP_Z * SCALE)
    frog.check(body, atlas, "Croaker", BOSS_BUDGET)
    frog.into_armature_space(body, arm)
    print("clips")
    frog.author_clips(arm, clips(), SCALE)
    reach(arm)
    frog.export(arm, body, NAME)
    if "--pose-sheet" in args:
        frog.pose_sheet(os.path.abspath(args[args.index("--pose-sheet") + 1]), NAME, SHEET, SCALE,
                        ("Hop", "Slam", "Lash", "Croak", "Death"),
                        (("Hop", 8), ("Slam", 27), ("Lash", 24), ("Croak", 30), ("Death", 36)))


main()
