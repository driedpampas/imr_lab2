"""Builds the Lab 3 hand: mesh, UVs, texture, rig, animations, FBX.

Run headless:  blender -b --factory-startup --python Blender/make_hand.py -- [--preview DIR]

The hand is a left hand lying palm-down: fingers along +Y, thumb towards +X, back of the hand towards +Z,
wrist at the origin. Units are metres. The right hand is the same model mirrored on X in Unity.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW_DIR = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
UNITY_DIR = os.path.normpath(os.path.join(HERE, "..", "Assets", "Handball", "Hands"))
STAGES = ARGS[ARGS.index("--stages") + 1].split(",") if "--stages" in ARGS else ["shape"]

# A Catmull-Clark surface sits inside its control cage; an 8-sided ring ends up at ~0.9 of the cage radius.
CAGE = 1.0 / 0.90
SUBDIV_LEVELS = 2
NAIL_RIM, NAIL_TUCK, NAIL_LIFT = 0.00040, 0.00035, 0.00040
NAIL_ROUND_ROOT, NAIL_ROUND_TIP, NAIL_WIDTH = 0.95, 0.50, 0.93
UV_MARGIN = 0.012
TEXTURE_SIZE, TEXTURE_BLEED = 2048, 24
ROOT_BONE = "Palm"
THUMB_BONE_ROLL = (25.0, 45.0, 45.0)
ANIM_FRAMES, ANIM_FPS = 16, 30
# Flexion in degrees per bone at the end of each animation.
POSES = {
    "Grab": {
        "Middle1": 68, "Middle2": 92, "Middle3": 58,
        "Ring1": 72, "Ring2": 94, "Ring3": 60,
        "Pinky1": 76, "Pinky2": 92, "Pinky3": 60,
    },
    "Trigger": {"Index1": 34, "Index2": 72, "Index3": 46},
}
# Hand signs the player can show Unity-chan. Fingers: (flexion per joint, sideways spread of the finger);
# thumb: a THUMB_POSES entry; wrist: rotations of the whole hand applied in order, in hand axes.
CURLED, STRAIGHT = (84, 98, 64), (-6, -9, -6)
X_AXIS, Y_AXIS, Z_AXIS = (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)
THUMB_POSES = {  # direction of the thumb's first bone, flexion of its two joints
    "out": ((0.93, 0.28, 0.12), (-4, -6)),
    "folded": ((0.30, 0.78, -0.55), (38, 52)),
    "relaxed": ((0.80, 0.56, -0.10), (0, 4)),
}
FINGERS_UP = [(X_AXIS, 84.0)]  # fingers pointing up, palm towards whoever the hand is held out to
GESTURES = {
    "ThumbsUp": dict(fingers={f: (CURLED, 0) for f in ("Index", "Middle", "Ring", "Pinky")}, thumb="out", wrist=[(Y_AXIS, -90.0)]),
    "ThumbsDown": dict(fingers={f: (CURLED, 0) for f in ("Index", "Middle", "Ring", "Pinky")}, thumb="out", wrist=[(Y_AXIS, 90.0)]),
    "Peace": dict(fingers={"Index": (STRAIGHT, -10), "Middle": (STRAIGHT, 8), "Ring": (CURLED, 0), "Pinky": (CURLED, 0)},
                  thumb="folded", wrist=FINGERS_UP),
    "MiddleFinger": dict(fingers={"Index": (CURLED, 0), "Middle": (STRAIGHT, 0), "Ring": (CURLED, 0), "Pinky": (CURLED, 0)},
                         thumb="folded", wrist=FINGERS_UP + [(Z_AXIS, 180.0)]),  # back of the hand towards them
    "Rock": dict(fingers={"Index": (STRAIGHT, -6), "Middle": (CURLED, 0), "Ring": (CURLED, 0), "Pinky": (STRAIGHT, 9)},
                 thumb="folded", wrist=FINGERS_UP),
    "Wave": dict(fingers={"Index": (STRAIGHT, -8), "Middle": (STRAIGHT, -2), "Ring": (STRAIGHT, 4), "Pinky": (STRAIGHT, 12)},
                 thumb="relaxed", wrist=FINGERS_UP, sway=(Y_AXIS, 24.0)),
}
NAIL_FLEN = (2.34, 2.92)  # nail root and free edge in finger coordinates (2 = last joint, 3 = fingertip)

# ---------------------------------------------------------------------------------------------- proportions

FINGERS = [
    # mcp: knuckle joint centre, yaw: splay in the palm plane (+ = towards the thumb), roll: tilt of the nail,
    # L: phalanx lengths, a: half width at the base, curl: rest flexion per joint (relaxed, not flat).
    dict(name="Pinky", mcp=(-0.0305, 0.0780, -0.0035), yaw=-11.0, roll=-12.0, L=(0.0335, 0.0200, 0.0210), a=0.0084, curl=(11, 15, 9)),
    dict(name="Ring", mcp=(-0.0108, 0.0875, -0.0005), yaw=-4.0, roll=-5.0, L=(0.0425, 0.0265, 0.0230), a=0.0094, curl=(9, 13, 8)),
    dict(name="Middle", mcp=(0.0092, 0.0920, 0.0015), yaw=0.5, roll=0.0, L=(0.0450, 0.0285, 0.0240), a=0.0100, curl=(7, 11, 7)),
    dict(name="Index", mcp=(0.0292, 0.0885, 0.0000), yaw=5.0, roll=6.0, L=(0.0410, 0.0250, 0.0225), a=0.0098, curl=(6, 10, 6)),
]

# (segment, position along the segment, half width, dorsal height, palmar height) - the last three relative to `a`.
# Three rings bracket each joint so it can bend; `t == 1` rings sit exactly on a joint.
FINGER_RINGS = [
    (0, 0.42, 1.00, 0.84, 0.86),  # shared with the palm: where the finger leaves the web
    (0, 0.64, 0.97, 0.83, 0.88),
    (0, 0.86, 0.96, 0.84, 0.83),
    (0, 1.00, 0.97, 0.88, 0.77),  # PIP
    (1, 0.15, 0.94, 0.84, 0.81),
    (1, 0.50, 0.89, 0.81, 0.86),
    (1, 0.84, 0.88, 0.82, 0.81),
    (1, 1.00, 0.89, 0.85, 0.75),  # DIP
    (2, 0.16, 0.86, 0.79, 0.81),
    (2, 0.34, 0.85, 0.73, 0.88),  # nail root
    (2, 0.92, 0.76, 0.62, 0.84),  # nail end
    (2, 0.99, 0.56, 0.42, 0.56),
]
NAIL_RINGS = (9, 10)  # the nail is the dorsal strip between these two rings

THUMB = dict(
    name="Thumb",
    cmc=(0.0180, 0.0200, -0.0050),
    dirs=((0.68, 0.68, -0.27), (0.50, 0.84, -0.21), (0.40, 0.90, -0.17)),
    L=(0.0430, 0.0310, 0.0270),
    a=0.0134,
)
# (segment, position, half width, dorsal, palmar, roll of the nail in degrees)
THUMB_BASE_SHEAR = 0.0065  # the first ring leans into the palm wall it grows out of
THUMB_RINGS = [
    (0, 0.70, 1.15, 0.86, 0.96, 15.0),
    (0, 1.00, 1.04, 0.90, 0.84, 35.0),  # MCP
    (1, 0.18, 0.98, 0.84, 0.84, 45.0),
    (1, 0.50, 0.92, 0.80, 0.88, 45.0),
    (1, 0.84, 0.92, 0.81, 0.82, 45.0),
    (1, 1.00, 0.95, 0.85, 0.75, 45.0),  # IP
    (2, 0.16, 0.92, 0.78, 0.82, 45.0),
    (2, 0.34, 0.92, 0.71, 0.88, 45.0),  # nail root
    (2, 0.92, 0.82, 0.60, 0.84, 45.0),  # nail end
    (2, 0.99, 0.58, 0.40, 0.56, 45.0),
]
THUMB_NAIL_RINGS = (7, 8)

# Palm cross-sections from the wrist (u = 0) to the knuckles (u = PALM_MCP_U). Columns run pinky side -> thumb side.
PALM_MCP_U = 0.78
PALM_RINGS = [
    # u, width relative to the knuckle line, dorsal heights (9 columns), palmar heights (9 columns)
    (0.00, 0.70,
     (0.0095, 0.0150, 0.0175, 0.0185, 0.0190, 0.0185, 0.0175, 0.0150, 0.0095),
     (-0.0095, -0.0150, -0.0175, -0.0180, -0.0175, -0.0180, -0.0180, -0.0160, -0.0105)),
    (0.17, 0.90,
     (0.0100, 0.0140, 0.0160, 0.0170, 0.0175, 0.0170, 0.0160, 0.0140, 0.0100),
     (-0.0140, -0.0195, -0.0205, -0.0180, -0.0160, -0.0175, -0.0215, -0.0235, -0.0190)),
    (0.37, 0.975,
     (0.0090, 0.0125, 0.0145, 0.0155, 0.0160, 0.0155, 0.0145, 0.0125, 0.0090),
     (-0.0125, -0.0175, -0.0180, -0.0150, -0.0125, -0.0140, -0.0190, -0.0225, -0.0195)),
    (0.57, 1.00,
     (0.0080, 0.0115, 0.0135, 0.0145, 0.0150, 0.0145, 0.0135, 0.0115, 0.0080),
     (-0.0100, -0.0145, -0.0150, -0.0130, -0.0118, -0.0120, -0.0140, -0.0160, -0.0130)),
    (PALM_MCP_U, 1.00,
     (0.0075, 0.0125, 0.0108, 0.0140, 0.0118, 0.0150, 0.0118, 0.0140, 0.0080),
     (-0.0090, -0.0128, -0.0128, -0.0138, -0.0133, -0.0138, -0.0133, -0.0133, -0.0095)),
]
THUMB_WEB_PUSH = {0.17: (0.0030, 0.0, -0.0030), 0.57: (0.0045, 0.0025, -0.0030)}  # thenar bulge, thumb web
THENAR_PUSH = {0.17: 0.0040, 0.37: 0.0050, 0.57: 0.0020}  # the ball of the thumb widens the palm
THUMB_PATCH_RINGS = (1, 2, 3)  # palm rings whose thumb-side wall the thumb grows out of
# y, width scale, thickness scale of the wrist ring. The step between the first two is the lip of the sweatband.
FOREARM_RINGS = [(-0.0105, 1.00, 1.00), (-0.0125, 1.08, 1.09), (-0.040, 1.095, 1.105)]
WRISTBAND_Y = -0.0115

# Ring vertex order, looking along the finger: 0 TL, 1 T, 2 TR, 3 R (seam), 4 BR, 5 B, 6 BL, 7 L (seam).
T_L, T_M, T_R, SIDE_R, B_R, B_M, B_L, SIDE_L = range(8)
# Palm ring vertex order: t0..t8, m4 (thumb side), b8..b0, m0 (pinky side).
PALM_N = 20
P_M4, P_M0 = 9, 19


def p_top(c):
    return c


def p_bot(c):
    return 18 - c


def ring_points(c, side, up, a, bt, bb):
    return [
        c - side * (0.72 * a) + up * (0.80 * bt), c + up * bt, c + side * (0.72 * a) + up * (0.80 * bt),
        c + side * a,
        c + side * (0.70 * a) - up * (0.78 * bb), c - up * bb, c - side * (0.70 * a) - up * (0.78 * bb),
        c - side * a,
    ]


def finger_skeleton(f):
    """Joint positions (MCP, PIP, DIP, tip) and the direction / dorsal vector of each phalanx."""
    yaw, roll = math.radians(f["yaw"]), math.radians(f["roll"])
    d = Vector((math.sin(yaw), math.cos(yaw), 0.0))
    side = Vector((math.cos(yaw), -math.sin(yaw), 0.0))
    up = (Vector((0, 0, 1)) * math.cos(roll) + side * math.sin(roll)).normalized()
    side = d.cross(up).normalized()
    joints, dirs, ups = [Vector(f["mcp"])], [], []
    for k in range(3):
        c = math.radians(f["curl"][k])
        d, up = (d * math.cos(c) - up * math.sin(c)).normalized(), (up * math.cos(c) + d * math.sin(c)).normalized()
        dirs.append(d)
        ups.append(up)
        joints.append(joints[-1] + d * f["L"][k])
    return joints, dirs, ups, side


def thumb_skeleton():
    joints, dirs = [Vector(THUMB["cmc"])], []
    for k in range(3):
        d = Vector(THUMB["dirs"][k]).normalized()
        dirs.append(d)
        joints.append(joints[-1] + d * THUMB["L"][k])
    return joints, dirs


def thumb_frame(d, roll_deg):
    """Dorsal and side vectors of the thumb: the nail is rolled away from the fingers."""
    z = Vector((0, 0, 1))
    up0 = (z - d * z.dot(d)).normalized()
    out = d.cross(z).normalized()
    r = math.radians(roll_deg)
    up = (up0 * math.cos(r) + out * math.sin(r)).normalized()
    return up, d.cross(up).normalized()


def ring_frame(seg, t, dirs, ups):
    """Direction and dorsal vector at a ring; joint rings use the bisector so the bend is shared evenly."""
    if t >= 1.0 and seg < 2:
        return (dirs[seg] + dirs[seg + 1]).normalized(), (ups[seg] + ups[seg + 1]).normalized()
    return dirs[seg], ups[seg]


# ---------------------------------------------------------------------------------------------- cage


class Cage:
    def __init__(self):
        self.bm = bmesh.new()
        self.seams = []  # vertex pairs
        self.nail_faces = []
        self.info = {}  # vert -> dict(dors, flen, bones)

    def vert(self, co, dors=0.0, flen=-1.0, bones=None, acr=0.0):
        v = self.bm.verts.new(co)
        self.info[v] = dict(dors=dors, flen=flen, acr=acr, bones=bones or {ROOT_BONE: 1.0})
        return v

    def quad(self, a, b, c, d):
        return self.bm.faces.new((a, b, c, d))

    def tube(self, r0, r1, skip=()):
        faces = {}
        for n in range(len(r0)):
            if n in skip:
                continue
            m = (n + 1) % len(r0)
            faces[n] = self.quad(r0[n], r0[m], r1[m], r1[n])
        return faces

    def seam(self, a, b):
        self.seams.append((a, b))


RING_DORS = (0.8, 1.0, 0.8, 0.0, -0.8, -1.0, -0.8, 0.0)
RING_ACR = (-1.0, 0.0, 1.0, 1.4, 1.0, 0.0, -1.0, -1.4)  # position across the finger, used to shape the nails


def joint_weights(names, ring_specs):
    """Per ring bone weights for a chain of three bones, blending 50/50 on the joint rings."""
    out = []
    for seg, t in ring_specs:
        if t >= 1.0 and seg < 2:
            w = {names[seg]: 0.5, names[seg + 1]: 0.5}
        elif t >= 0.8 and seg < 2:
            w = {names[seg]: 0.85, names[seg + 1]: 0.15}
        elif t <= 0.2 and seg > 0:
            w = {names[seg]: 0.85, names[seg - 1]: 0.15}
        else:
            w = {names[seg]: 1.0}
        out.append(w)
    return out


def build_cage():
    cg = Cage()
    skel = [finger_skeleton(f) for f in FINGERS]

    # ---- fingers: ideal ring positions (ring 0 is merged into the palm below)
    rings_co = []
    for f, (joints, dirs, ups, _side) in zip(FINGERS, skel):
        cos = []
        for seg, t, wa, wt, wb in FINGER_RINGS:
            d, up = ring_frame(seg, t, dirs, ups)
            side = d.cross(up).normalized()
            c = joints[seg] + dirs[seg] * (f["L"][seg] * t)
            a = f["a"] * CAGE * wa
            if (seg, t) == FINGER_RINGS[-1][:2]:
                c = c - up * (0.18 * f["a"])  # the fingertip's far point sits below the nail
            cos.append(ring_points(c, side, up, a, f["a"] * CAGE * wt, f["a"] * CAGE * wb))
        rings_co.append(cos)

    # ---- the split ring: last palm ring, shared by the four fingers
    def avg(a, b):
        return (a + b) * 0.5

    split = [None] * PALM_N
    webs = [None] * 5
    for i in range(4):
        r = rings_co[i][0]
        split[p_top(2 * i + 1)] = r[T_M]
        split[p_bot(2 * i + 1)] = r[B_M]
        if i < 3:
            nxt = rings_co[i + 1][0]
            split[p_top(2 * i + 2)] = avg(r[T_R], nxt[T_L])
            split[p_bot(2 * i + 2)] = avg(r[B_R], nxt[B_L])
            webs[i + 1] = avg(r[SIDE_R], nxt[SIDE_L])
    split[p_top(0)], split[p_bot(0)], split[P_M0] = rings_co[0][0][T_L], rings_co[0][0][B_L], rings_co[0][0][SIDE_L]
    split[p_top(8)], split[p_bot(8)], split[P_M4] = rings_co[3][0][T_R], rings_co[3][0][B_R], rings_co[3][0][SIDE_R]

    # ---- palm rings
    def mcp_col(c):
        """Knuckle-line value for a palm column: odd columns are finger centres, even ones lie between."""
        i = min(c // 2, 3)
        vals = [Vector(f["mcp"]) for f in FINGERS]
        if c % 2 == 1:
            return vals[i]
        if c == 0:
            return vals[0] + Vector((-0.80 * FINGERS[0]["a"] * CAGE, -0.004, 0))
        if c == 8:
            return vals[3] + Vector((0.80 * FINGERS[3]["a"] * CAGE, -0.003, 0))
        return avg(vals[c // 2 - 1], vals[c // 2]) + Vector((0, -0.0025, 0))

    mcp_cols = [mcp_col(c) for c in range(9)]
    y_mean = sum(v.y for v in mcp_cols) / 9.0
    palm_co = []
    for u, width, top, bot in PALM_RINGS:
        k = u / PALM_MCP_U
        ring = [None] * PALM_N
        for c in range(9):
            x = mcp_cols[c].x * width
            y = k * (y_mean + (mcp_cols[c].y - y_mean) * k)
            arch = mcp_cols[c].z * k
            thenar = THENAR_PUSH.get(u, 0.0) * {7: 0.5, 8: 1.0}.get(c, 0.0)
            ring[p_top(c)] = Vector((x + thenar * 0.5, y, top[c] * CAGE + arch))
            ring[p_bot(c)] = Vector((x + thenar, y, bot[c] * CAGE + arch))
        for c, n, sign in ((0, P_M0, -1), (8, P_M4, 1)):
            t, b = ring[p_top(c)], ring[p_bot(c)]
            ring[n] = Vector((max(t.x, b.x) if sign > 0 else min(t.x, b.x), t.y, (t.z + b.z) * 0.5))
            ring[n].x += sign * 0.0062 * width
        if u in THUMB_WEB_PUSH:
            ring[P_M4] += Vector(THUMB_WEB_PUSH[u])
        palm_co.append(ring)
    palm_co.append(split)

    forearm_co = []
    wrist = palm_co[0]
    for y, ws, ts in FOREARM_RINGS:
        forearm_co.append([Vector((p.x * ws, y, p.z * ts)) for p in wrist])
    all_rings_co = forearm_co[::-1] + palm_co  # proximal -> distal
    first_palm = len(forearm_co)
    last = len(all_rings_co) - 1

    # ---- palm verts and weights
    finger_names = [[f["name"] + str(k + 1) for k in range(3)] for f in FINGERS]
    thumb_names = ["Thumb1", "Thumb2", "Thumb3"]

    def palm_bones(ring_index, n):
        j = ring_index - first_palm
        if j < 0:
            return {ROOT_BONE: 1.0}
        c = n if n <= 8 else (18 - n if n <= 18 else 0)
        if n == P_M4:
            c = 8
        w = {}
        share = {len(PALM_RINGS) - 1: 0.5, len(PALM_RINGS): 1.0}.get(j, 0.0)  # knuckle ring, split ring
        if share:
            owners = [min(c // 2, 3)] if (c % 2 == 1 or c in (0, 8)) else [c // 2 - 1, c // 2]
            for i in owners:
                w[finger_names[i][0]] = share / len(owners)
        if j in THUMB_PATCH_RINGS and c >= 7:
            w["Thumb1"] = 0.45 if c == 8 else 0.2
        w[ROOT_BONE] = max(0.0, 1.0 - sum(w.values()))
        return w

    palm = []
    for ri, ring in enumerate(all_rings_co):
        verts = []
        for n, co in enumerate(ring):
            j = ri - first_palm
            if j == THUMB_PATCH_RINGS[1] and n == P_M4:
                verts.append(None)  # swallowed by the thumb opening
                continue
            dors = 0.0 if n in (P_M4, P_M0) else (1.0 if n <= 8 else -1.0)
            if n in (0, 8, 10, 18):
                dors *= 0.8
            flen = FINGER_RINGS[0][1] if ri == last else (ri - (last - 1)) / (last - 1.0)  # -1 forearm .. 0 knuckles
            verts.append(cg.vert(co, dors=dors, flen=flen, bones=palm_bones(ri, n)))
        palm.append(verts)
    web_verts = [None] * 5
    for i in (1, 2, 3):
        web_verts[i] = cg.vert(webs[i], dors=0.0, flen=FINGER_RINGS[0][1],
                               bones={finger_names[i - 1][0]: 0.5, finger_names[i][0]: 0.5})
    web_verts[0], web_verts[4] = palm[last][P_M0], palm[last][P_M4]

    patch = [first_palm + j for j in THUMB_PATCH_RINGS]
    for ri in range(last):
        skip = (8, 9) if ri in patch[:2] else ()
        cg.tube(palm[ri], palm[ri + 1], skip=skip)
        cg.seam(palm[ri][P_M0], palm[ri + 1][P_M0])
        if not skip:
            cg.seam(palm[ri][P_M4], palm[ri + 1][P_M4])

    # ---- wrist cap: a strip of quads either side of a centre row, its own UV island
    cap = palm[0]
    mids = [cap[P_M0]] + [cg.vert(avg(cap[p_top(c)].co, cap[p_bot(c)].co) + Vector((0, -0.004, 0)), flen=-1.0)
                           for c in range(1, 8)] + [cap[P_M4]]
    tops = [cap[P_M0]] + [cap[p_top(c)] for c in range(1, 8)] + [cap[P_M4]]
    bots = [cap[P_M0]] + [cap[p_bot(c)] for c in range(1, 8)] + [cap[P_M4]]
    cg.bm.faces.new((cap[P_M0], cap[p_top(0)], cap[p_top(1)], mids[1]))
    cg.bm.faces.new((cap[P_M0], mids[1], cap[p_bot(1)], cap[p_bot(0)]))
    cg.bm.faces.new((cap[P_M4], mids[7], cap[p_top(7)], cap[p_top(8)]))
    cg.bm.faces.new((cap[P_M4], cap[p_bot(8)], cap[p_bot(7)], mids[7]))
    for c in range(1, 7):
        cg.quad(tops[c], tops[c + 1], mids[c + 1], mids[c])
        cg.quad(mids[c], mids[c + 1], bots[c + 1], bots[c])
    for n in range(PALM_N):
        cg.seam(cap[n], cap[(n + 1) % PALM_N])

    # ---- fingers
    def chain(base_ring, ring_cos, tip_co, names, ring_specs, nail_rings, flen_of):
        weights = joint_weights(names, ring_specs)
        prev = base_ring
        rings = [base_ring]
        for k in range(1, len(ring_cos)):
            ring = [cg.vert(co, dors=RING_DORS[n], flen=flen_of(k), bones=weights[k], acr=RING_ACR[n])
                    for n, co in enumerate(ring_cos[k])]
            faces = cg.tube(prev, ring)
            if (k - 1, k) == nail_rings:
                cg.nail_faces += [faces[T_L], faces[T_M]]
            cg.seam(prev[SIDE_R], ring[SIDE_R])
            cg.seam(prev[SIDE_L], ring[SIDE_L])
            rings.append(ring)
            prev = ring
        tip = cg.vert(tip_co, dors=0.0, flen=3.0, bones=weights[-1])
        for n in (7, 1, 3, 5):
            cg.quad(prev[n], prev[(n + 1) % 8], prev[(n + 2) % 8], tip)
        cg.seam(prev[SIDE_R], tip)
        cg.seam(prev[SIDE_L], tip)
        return rings

    for i, f in enumerate(FINGERS):
        joints, dirs, ups, _ = skel[i]
        base = [palm[last][p_top(2 * i)], palm[last][p_top(2 * i + 1)], palm[last][p_top(2 * i + 2)], web_verts[i + 1],
                palm[last][p_bot(2 * i + 2)], palm[last][p_bot(2 * i + 1)], palm[last][p_bot(2 * i)], web_verts[i]]
        tip = joints[2] + dirs[2] * (f["L"][2] * 1.03) - ups[2] * (0.22 * f["a"])
        specs = [(seg, t) for seg, t, *_ in FINGER_RINGS]
        chain(base, rings_co[i], tip, finger_names[i], specs, NAIL_RINGS, lambda k: FINGER_RINGS[k][0] + FINGER_RINGS[k][1])

    # ---- thumb
    tj, td = thumb_skeleton()
    a0 = THUMB["a"] * CAGE
    thumb_cos = [None]
    for seg, t, wa, wt, wb, roll in THUMB_RINGS:
        d = (td[seg] + td[seg + 1]).normalized() if (t >= 1.0 and seg < 2) else td[seg]
        up, side = thumb_frame(d, roll)
        c = tj[seg] + td[seg] * (THUMB["L"][seg] * t)
        if (seg, t) == THUMB_RINGS[-1][:2]:
            c = c - up * (0.18 * THUMB["a"])
        pts = ring_points(c, side, up, a0 * wa, a0 * wt, a0 * wb)
        if len(thumb_cos) == 1:
            pts = [p - d * (THUMB_BASE_SHEAR * (p - c).dot(side) / (a0 * wa)) for p in pts]
        thumb_cos.append(pts)
    up_tip, _ = thumb_frame(td[2], THUMB_RINGS[-1][5])
    tip = tj[2] + td[2] * (THUMB["L"][2] * 1.03) - up_tip * (0.22 * THUMB["a"])
    pa, pb, pc = (palm[r] for r in patch)
    base = [pc[p_top(8)], pb[p_top(8)], pa[p_top(8)], pa[P_M4], pa[p_bot(8)], pb[p_bot(8)], pc[p_bot(8)], pc[P_M4]]
    specs = [(0, 0.5)] + [(seg, t) for seg, t, *_ in THUMB_RINGS]
    chain(base, thumb_cos, tip, thumb_names, specs, tuple(r + 1 for r in THUMB_NAIL_RINGS),
          lambda k: THUMB_RINGS[k - 1][0] + THUMB_RINGS[k - 1][1])

    bmesh.ops.recalc_face_normals(cg.bm, faces=cg.bm.faces[:])
    return cg, skel, (tj, td)


def cage_to_object(cg, name="HandMesh"):
    bm = cg.bm
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    for a, b in cg.seams:
        e = bm.edges.get((a, b))
        assert e is not None, "seam between unconnected vertices"
        e.seam = True
    for f in bm.faces:
        f.smooth = True
    bm.faces.index_update()
    nail_ids = {f.index for f in cg.nail_faces}
    for f in bm.faces:
        f.material_index = 1 if f.index in nail_ids else 0

    mesh = bpy.data.meshes.new(name)
    bm.verts.index_update()
    order = list(bm.verts)
    bm.to_mesh(mesh)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)

    dors = mesh.attributes.new("dors", "FLOAT", "POINT")
    flen = mesh.attributes.new("flen", "FLOAT", "POINT")
    acr = mesh.attributes.new("acr", "FLOAT", "POINT")
    for v in order:
        info = cg.info[v]
        dors.data[v.index].value = info["dors"]
        flen.data[v.index].value = info["flen"]
        acr.data[v.index].value = info["acr"]
        for bone, w in info["bones"].items():
            if w <= 0.0:
                continue
            group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            group.add([v.index], w, "REPLACE")
    return obj


def report_topology(obj, label):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    non_manifold = [e for e in bm.edges if not e.is_manifold]
    quads = sum(1 for f in bm.faces if len(f.verts) == 4)
    print(f"[hand] {label}: {len(bm.verts)} verts, {len(bm.faces)} faces ({quads} quads), "
          f"{len(non_manifold)} non-manifold edges, {sum(1 for e in bm.edges if e.seam)} seam edges")
    bm.free()


def subdivide(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mod = obj.modifiers.new("Subdivision", "SUBSURF")
    mod.levels = SUBDIV_LEVELS
    mod.subdivision_type = "CATMULL_CLARK"
    bpy.ops.object.modifier_apply(modifier=mod.name)


def round_nail(faces, flen, acr):
    """Slides a nail's grid of vertices over the surface so its outline is rounded instead of rectangular."""
    verts = list({v for f in faces for v in f.verts})
    lo_a, hi_a = min(v[flen] for v in verts), max(v[flen] for v in verts)
    lo_b, hi_b = min(v[acr] for v in verts), max(v[acr] for v in verts)
    param = {v: (2.0 * (v[flen] - lo_a) / (hi_a - lo_a) - 1.0, 2.0 * (v[acr] - lo_b) / (hi_b - lo_b) - 1.0) for v in verts}
    rest = {v: v.co.copy() for v in verts}
    tris = []
    for f in faces:
        q = list(f.verts)
        tris += [(q[0], q[1], q[2]), (q[0], q[2], q[3])]

    def sample(a, b):
        best, best_err = None, 1e9
        for t in tris:
            (a0, b0), (a1, b1), (a2, b2) = (param[v] for v in t)
            det = (b1 - b2) * (a0 - a2) + (a2 - a1) * (b0 - b2)
            if abs(det) < 1e-12:
                continue
            w0 = ((b1 - b2) * (a - a2) + (a2 - a1) * (b - b2)) / det
            w1 = ((b2 - b0) * (a - a2) + (a0 - a2) * (b - b2)) / det
            w = (w0, w1, 1.0 - w0 - w1)
            err = -min(w)
            if err < best_err:
                best, best_err = sum((rest[v] * k for v, k in zip(t, w)), Vector()), err
            if err <= 1e-6:
                break
        return best

    for v in verts:
        a, b = param[v]
        k = NAIL_ROUND_ROOT + (NAIL_ROUND_TIP - NAIL_ROUND_ROOT) * (a + 1.0) * 0.5  # rounder at the root
        v.co = sample(a * math.sqrt(1.0 - k * b * b * 0.5), NAIL_WIDTH * b * math.sqrt(1.0 - k * a * a * 0.5))


def sculpt_nails(obj):
    """Turns the nail faces into a plate: tucked under the skin at the root and sides, lifted at the free edge."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    flen = bm.verts.layers.float["flen"]
    acr = bm.verts.layers.float["acr"]
    nail = [f for f in bm.faces if f.material_index == 1]
    todo, count = set(nail), 0
    while todo:
        island, stack = [], [todo.pop()]
        while stack:
            f = stack.pop()
            island.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g in todo:
                        todo.remove(g)
                        stack.append(g)
        round_nail(island, flen, acr)
        count += 1
    bm.normal_update()
    rim = bmesh.ops.inset_region(bm, faces=nail, thickness=NAIL_RIM, depth=0.0, use_even_offset=True,
                                 use_boundary=True)["faces"]
    for f in rim:
        f.material_index = 0
    plate = {v for f in nail for v in f.verts}
    lo = min(v[flen] for v in plate)
    hi = max(v[flen] for v in plate)
    for v in plate:
        k = (v[flen] - lo) / (hi - lo)
        k = k * k * (3.0 - 2.0 * k)
        v.co += v.normal * (-NAIL_TUCK + (NAIL_TUCK + NAIL_LIFT) * k)
    for e in {e for f in nail for e in f.edges}:
        if any(f.material_index == 0 for f in e.link_faces):
            e.smooth = False
    bm.normal_update()
    bm.to_mesh(obj.data)
    bm.free()
    print(f"[hand] nails: {count} nails, {len(nail)} plate faces, {len(rim)} rim faces")


# ---------------------------------------------------------------------------------------------- UVs


def unwrap(obj):
    """Three islands cut along the seams: back of the hand, palm, and the wrist cap."""
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.unwrap(method="MINIMUM_STRETCH", fill_holes=True, correct_aspect=True, margin=0.0)
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=UV_MARGIN)
    bpy.ops.object.mode_set(mode="OBJECT")


def report_uv_distortion(obj):
    """Texel density spread and angle error: both would be 1.0 / 0 deg for a surface that unrolls perfectly."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    scales, areas, angle_err, flipped = [], [], [], 0
    for f in bm.faces:
        loops = list(f.loops)
        pts = [Vector((l[uv].uv.x, l[uv].uv.y, 0.0)) for l in loops]
        signed = sum(pts[i].x * pts[(i + 1) % 4].y - pts[(i + 1) % 4].x * pts[i].y for i in range(len(pts))) * 0.5
        flipped += signed <= 0.0
        area3 = f.calc_area()
        if area3 <= 0.0 or signed == 0.0:
            continue
        scales.append(math.sqrt(abs(signed) / area3))
        areas.append(area3)
        for i, l in enumerate(loops):
            a3 = l.calc_angle()
            e0, e1 = pts[(i + 1) % len(pts)] - pts[i], pts[i - 1] - pts[i]
            if e0.length > 0 and e1.length > 0:
                angle_err.append(abs(math.degrees(e0.angle(e1) - a3)))
    order = sorted(range(len(scales)), key=scales.__getitem__)
    total = sum(areas)

    def weighted(q):
        acc = 0.0
        for i in order:
            acc += areas[i]
            if acc >= q * total:
                return scales[i]
        return scales[order[-1]]

    median = weighted(0.5)
    angle_err.sort()
    islands = len({tuple(round(c, 5) for c in l[uv].uv) for f in bm.faces for l in f.loops})
    used = sum(abs(s * s * a) for s, a in zip(scales, areas))
    print(f"[hand] uv: texel density p5/p50/p95 = {weighted(0.05) / median:.2f} / 1.00 / {weighted(0.95) / median:.2f}, "
          f"min/max = {scales[order[0]] / median:.2f} / {scales[order[-1]] / median:.2f}")
    print(f"[hand] uv: angle error mean {sum(angle_err) / len(angle_err):.1f} deg, p95 {angle_err[int(len(angle_err) * 0.95)]:.1f} deg, "
          f"max {angle_err[-1]:.1f} deg; flipped faces {flipped}; uv space used {used * 100:.0f}%")
    bm.free()


def checker_material():
    mat = bpy.data.materials.new("UVChecker")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    coord = nodes.new("ShaderNodeTexCoord")
    checker = nodes.new("ShaderNodeTexChecker")
    checker.inputs["Scale"].default_value = 48.0
    checker.inputs["Color1"].default_value = (0.9, 0.9, 0.9, 1)
    checker.inputs["Color2"].default_value = (0.15, 0.25, 0.6, 1)
    links.new(coord.outputs["UV"], checker.inputs["Vector"])
    links.new(checker.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.7
    return mat


# ---------------------------------------------------------------------------------------------- texture


def _hash3(ix, iy, iz):
    import numpy as np
    h = (ix * 374761393 + iy * 668265263 + iz * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float32) / 65535.0


def _noise(p, freq, seed=0.0):
    """Value noise in [-1, 1] over an (N, 3) array of positions."""
    import numpy as np
    q = p * freq + seed
    i = np.floor(q).astype(np.int64)
    f = (q - i).astype(np.float32)
    f = f * f * (3.0 - 2.0 * f)
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * _hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
    return out * 2.0 - 1.0


def _fbm(p, freq, octaves=3, seed=0.0):
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * _noise(p, freq * (2 ** o), seed + 17.0 * o)
        norm += amp
        amp *= 0.5
    return total / norm


def _curve_distance(x, y, points, samples=24):
    """Distance in the XY plane from texels to a quadratic Bezier, and how far along it the nearest point is."""
    import numpy as np
    (x0, y0), (x1, y1), (x2, y2) = points
    t = np.linspace(0.0, 1.0, samples)
    cx = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * x1 + t ** 2 * x2
    cy = (1 - t) ** 2 * y0 + 2 * (1 - t) * t * y1 + t ** 2 * y2
    best = np.full(x.shape, 1e9, dtype=np.float32)
    where = np.zeros(x.shape, dtype=np.float32)
    for k in range(samples - 1):
        ax, ay, bx, by = cx[k], cy[k], cx[k + 1], cy[k + 1]
        dx, dy = bx - ax, by - ay
        u = np.clip(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0.0, 1.0)
        dist = np.hypot(x - (ax + u * dx), y - (ay + u * dy))
        closer = dist < best
        best = np.where(closer, dist, best)
        where = np.where(closer, (k + u) / (samples - 1), where)
    return best, where


PALM_CREASES = [  # (control points in the palm plane, half width, strength)
    (((-0.0400, 0.0640), (-0.0050, 0.0700), (0.0200, 0.0880)), 0.00065, 0.62),  # distal transverse ("heart")
    (((0.0370, 0.0660), (0.0080, 0.0640), (-0.0260, 0.0430)), 0.00065, 0.62),  # proximal transverse ("head")
    (((0.0370, 0.0660), (0.0050, 0.0520), (0.0060, 0.0040)), 0.00070, 0.68),  # thenar ("life")
    (((-0.0260, 0.0010), (0.0000, -0.0010), (0.0270, 0.0020)), 0.00070, 0.5),  # wrist
    (((-0.0250, -0.0070), (0.0000, -0.0085), (0.0260, -0.0060)), 0.00060, 0.3),
]
DORSAL_VEINS = [
    (((-0.0180, 0.0100), (-0.0240, 0.0450), (-0.0200, 0.0760)), 0.0016, 1.0),
    (((0.0020, 0.0080), (-0.0020, 0.0400), (-0.0010, 0.0800)), 0.0015, 0.8),
    (((0.0020, 0.0300), (0.0150, 0.0480), (0.0200, 0.0780)), 0.0014, 0.7),
]


def paint_texture(obj, path, size=TEXTURE_SIZE):
    """Paints the skin texture straight into UV space from each texel's position on the hand.

    Everything is a function of where the texel sits on the model (not of its UV), so the painting is continuous
    across the seams."""
    import numpy as np
    mesh = obj.data
    nv = len(mesh.vertices)
    co = np.empty(nv * 3, np.float32)
    mesh.vertices.foreach_get("co", co)
    channels = [co.reshape(-1, 3)]
    for name in ("dors", "flen", "acr"):
        a = np.empty(nv, np.float32)
        mesh.attributes[name].data.foreach_get("value", a)
        channels.append(a[:, None])
    vdata = np.hstack(channels)  # x y z dors flen acr
    uv = np.empty(len(mesh.loops) * 2, np.float32)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2) * size - 0.5
    loop_vert = np.empty(len(mesh.loops), np.int32)
    mesh.loops.foreach_get("vertex_index", loop_vert)

    buf = np.zeros((size, size, 7), np.float32)
    cover = np.zeros((size, size), bool)
    for poly in mesh.polygons:
        li = list(poly.loop_indices)
        nail = 1.0 if poly.material_index == 1 else 0.0
        for tri in ((li[0], li[1], li[2]), (li[0], li[2], li[3])):
            pts = uv[list(tri)]
            x0, y0 = np.floor(pts.min(axis=0)).astype(int) - 1
            x1, y1 = np.ceil(pts.max(axis=0)).astype(int) + 1
            x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size - 1), min(y1, size - 1)
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            (ax, ay), (bx, by), (cx, cy) = pts
            det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(det) < 1e-9:
                continue
            w0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / det
            w1 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / det
            w2 = 1.0 - w0 - w1
            inside = (w0 >= -0.03) & (w1 >= -0.03) & (w2 >= -0.03)
            if not inside.any():
                continue
            v = vdata[loop_vert[list(tri)]]
            val = w0[inside, None] * v[0] + w1[inside, None] * v[1] + w2[inside, None] * v[2]
            buf[gy[inside], gx[inside], :6] = val
            buf[gy[inside], gx[inside], 6] = nail
            cover[gy[inside], gx[inside]] = True

    d = buf[cover]
    pos, dors, flen, acr, nail = d[:, :3], d[:, 3], d[:, 4], d[:, 5], d[:, 6] > 0.5
    x, y = pos[:, 0], pos[:, 1]

    def smooth(lo, hi, v):
        t = np.clip((v - lo) / (hi - lo), 0.0, 1.0)
        return t * t * (3.0 - 2.0 * t)

    def gauss(v, sigma):
        return np.exp(-(v / sigma) ** 2)

    back = smooth(-0.5, 0.5, dors)  # 1 on the back of the hand, 0 on the palm
    palm = 1.0 - back
    is_palm_area = smooth(0.42, 0.12, flen)  # 1 on the palm / back of the hand, 0 on the fingers
    color = (1.0 - back)[:, None] * np.array((0.935, 0.735, 0.655), np.float32) + back[:, None] * np.array((0.870, 0.665, 0.545), np.float32)

    # broad mottling, then a redness mask built up from the parts of a hand that carry more blood
    color *= (1.0 + 0.045 * _fbm(pos, 85.0, 3))[:, None]
    red = 0.10 * np.clip(_fbm(pos, 40.0, 2, seed=5.0) + 0.3, 0.0, 1.0)
    red += back * (0.42 * gauss(flen - 1.0, 0.13) + 0.34 * gauss(flen - 2.0, 0.10))
    for f in FINGERS:
        red += back * 0.36 * gauss(np.hypot(x - f["mcp"][0], y - f["mcp"][1]), 0.0075)
    red += palm * 0.42 * smooth(2.25, 2.95, flen)
    red += palm * is_palm_area * 0.20 * smooth(0.012, 0.035, np.abs(x)) * smooth(0.0, 0.03, y)
    red += palm * 0.16 * gauss(flen - 0.2, 0.25)

    # skin folds: broken, wavy arcs over the knuckles; deeper single creases on the palmar side of each joint
    fold = np.zeros(len(d), np.float32)
    arc = 0.045 * acr * acr + 0.018 * _noise(pos, 170.0, 11.0)
    for joint, offsets, sigma, strength in ((1.0, (-0.115, -0.06, -0.01, 0.045, 0.10), 0.012, 0.34),
                                            (2.0, (-0.075, -0.02, 0.04), 0.012, 0.28)):
        for k, o in enumerate(offsets):
            broken = np.clip(0.75 + 0.6 * _noise(pos, 230.0, 20.0 + 7.0 * k), 0.0, 1.0)
            fold += back * strength * broken * gauss(flen - joint - o + arc, sigma) * smooth(1.25, 0.7, np.abs(acr))
    for joint, offsets, strength in ((1.0, (-0.035, 0.035), 0.55), (2.0, (0.0,), 0.55)):
        for o in offsets:
            fold += palm * strength * gauss(flen - joint - o, 0.015)
    fold += palm * 0.55 * gauss(flen - 0.47, 0.022) * smooth(0.078, 0.086, y)
    palm_mask = palm * is_palm_area
    for points, width, strength in PALM_CREASES:
        dist, along_curve = _curve_distance(x, y, points)
        fold += palm_mask * strength * gauss(dist, width) * np.sin(np.pi * along_curve) ** 0.6
    fold += 0.10 * np.clip(_fbm(pos, 260.0, 2, seed=9.0), 0.0, 1.0) * (0.5 + 0.5 * back)  # fine skin grain

    vein = np.zeros(len(d), np.float32)
    for points, width, strength in DORSAL_VEINS:
        vein += strength * gauss(_curve_distance(x, y, points)[0], width)
    vein *= back * is_palm_area * (0.75 + 0.25 * _noise(pos, 60.0, 3.0))

    red = np.clip(red, 0.0, 1.0)[:, None]
    color = color * (1.0 - red) + color * np.array((1.02, 0.80, 0.78), np.float32) * red
    vein = np.clip(vein, 0.0, 1.0)[:, None] * 0.22
    color = color * (1.0 - vein) + color * np.array((0.86, 0.95, 1.00), np.float32) * vein
    fold = np.clip(fold, 0.0, 1.0)[:, None]
    color = color * (1.0 - 0.30 * fold) * (1.0 - fold * np.array((0.0, 0.07, 0.07), np.float32))
    color *= (1.0 + 0.018 * _noise(pos, 1300.0, 2.0))[:, None]  # pores

    # nails: pink plate, pale half-moon at the root, off-white free edge (in the nail's own coordinates)
    along = np.clip((flen - NAIL_FLEN[0]) / (NAIL_FLEN[1] - NAIL_FLEN[0]), 0.0, 1.0)
    plate = np.array((0.935, 0.705, 0.675), np.float32) * (1.0 + 0.02 * _noise(pos, 300.0, 4.0))[:, None]
    moon = smooth(0.26, 0.16, np.hypot(along / 0.9, acr / 2.6))[:, None]
    plate = plate * (1.0 - moon) + np.array((0.965, 0.865, 0.840), np.float32) * moon
    edge = smooth(0.80, 0.87, along - 0.05 * acr * acr)[:, None]
    plate = plate * (1.0 - edge) + np.array((0.960, 0.930, 0.885), np.float32) * edge
    plate *= (1.0 - 0.10 * smooth(0.75, 1.0, np.abs(acr)))[:, None]  # darker towards the side folds
    color[nail] = plate[nail]

    # sweatband: ribbed towelling in the game's blue with a white stripe, hiding where the model ends
    color *= (1.0 - 0.22 * smooth(WRISTBAND_Y + 0.0050, WRISTBAND_Y, y))[:, None]  # its shadow on the skin
    band = y < WRISTBAND_Y
    ribs = 0.5 + 0.5 * np.sin(np.arctan2(pos[:, 2], x) * 64.0)
    towel = (0.90 + 0.10 * ribs) * (1.0 + 0.05 * _noise(pos, 900.0, 6.0))
    stripe = smooth(0.0045, 0.0035, np.abs(y - (WRISTBAND_Y + FOREARM_RINGS[-1][0]) * 0.5))[:, None]
    cloth = np.array((0.16, 0.50, 0.90), np.float32) * (1.0 - stripe) + np.array((0.97, 0.97, 0.95), np.float32) * stripe
    color[band] = (cloth * towel[:, None])[band]
    cap = y < FOREARM_RINGS[-1][0] - 0.0003
    color[cap] = np.array((0.07, 0.16, 0.30), np.float32)

    img = np.zeros((size, size, 4), np.float32)
    img[cover, :3] = np.clip(color, 0.0, 1.0)
    img[cover, 3] = 1.0
    # bleed the island borders outwards so filtering and mip-maps never pick up the background
    filled = cover.copy()
    for _ in range(TEXTURE_BLEED):
        acc = np.zeros_like(img[..., :3])
        cnt = np.zeros((size, size), np.float32)
        for sy, sx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            dst = (slice(max(sy, 0), size + min(sy, 0)), slice(max(sx, 0), size + min(sx, 0)))
            src = (slice(max(-sy, 0), size + min(-sy, 0)), slice(max(-sx, 0), size + min(-sx, 0)))
            acc[dst] += img[..., :3][src] * filled[src][..., None]
            cnt[dst] += filled[src]
        grow = (~filled) & (cnt > 0)
        img[grow, :3] = acc[grow] / cnt[grow, None]
        filled |= grow
    img[~filled, :3] = np.array((0.87, 0.665, 0.545), np.float32)
    img[..., 3] = 1.0

    image = bpy.data.images.new("HandSkin", size, size, alpha=False)
    image.pixels.foreach_set(img.ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print(f"[hand] texture: {size}x{size}, {cover.mean() * 100:.0f}% of texels on the model -> {path}")
    return image


def skin_materials(image):
    mats = []
    for name, rough in (("HandSkin", 0.55), ("HandNail", 0.28)):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = nodes["Principled BSDF"]
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = image
        links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = rough
        mats.append(mat)
    return mats


# ---------------------------------------------------------------------------------------------- rig


def build_rig(obj, skel, thumb):
    """One bone per phalanx. Every bone's local Z points out of the back of the finger, so -X rotation is flexion."""
    arm = bpy.data.armatures.new("HandRig")
    rig = bpy.data.objects.new("HandRig", arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    hand = arm.edit_bones.new(ROOT_BONE)
    hand.head, hand.tail = (0.0, FOREARM_RINGS[-1][0], 0.0), (0.0, 0.045, 0.0)
    hand.align_roll((0, 0, 1))

    def chain(names, joints, ups):
        parent = hand
        for k, name in enumerate(names):
            bone = arm.edit_bones.new(name)
            bone.head, bone.tail = joints[k], joints[k + 1]
            bone.align_roll(ups[k])
            bone.parent = parent
            bone.use_connect = k > 0
            parent = bone

    for f, (joints, dirs, ups, _side) in zip(FINGERS, skel):
        chain([f["name"] + str(k + 1) for k in range(3)], joints, ups)
    tj, td = thumb
    chain(["Thumb1", "Thumb2", "Thumb3"], tj, [thumb_frame(td[k], roll)[0] for k, roll in enumerate(THUMB_BONE_ROLL)])
    bpy.ops.object.mode_set(mode="OBJECT")

    obj.parent = rig
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


def gesture_rotations(rig, gesture, phase=0.0):
    """Bone rotations for one of GESTURES; `phase` (0..1) drives the gestures that move."""
    out = {}
    for finger, (curl, spread) in gesture["fingers"].items():
        for k, flex in enumerate(curl):
            q = Quaternion((1.0, 0.0, 0.0), -math.radians(flex))
            if k == 0:
                q = Quaternion((0.0, 0.0, 1.0), math.radians(spread)) @ q
            out[finger + str(k + 1)] = q
    # The thumb's first bone is aimed at a direction in hand space; the other two only flex.
    direction, flex = THUMB_POSES[gesture["thumb"]]
    rest = rig.data.bones["Thumb1"].matrix_local.to_quaternion()
    aim = (rest @ Vector((0.0, 1.0, 0.0))).rotation_difference(Vector(direction).normalized())
    out["Thumb1"] = rest.inverted() @ aim @ rest
    out["Thumb2"] = Quaternion((1.0, 0.0, 0.0), -math.radians(flex[0]))
    out["Thumb3"] = Quaternion((1.0, 0.0, 0.0), -math.radians(flex[1]))
    # The root bone's axes are the hand's own (X thumb side, Y fingers, Z back of the hand).
    wrist = Quaternion()
    for axis, angle in gesture["wrist"]:
        wrist = Quaternion(axis, math.radians(angle)) @ wrist
    if "sway" in gesture:
        axis, angle = gesture["sway"]
        wrist = Quaternion(axis, math.radians(angle) * math.sin(2.0 * math.pi * phase)) @ wrist
    out[ROOT_BONE] = wrist
    return out


def set_pose(rig, pose, amount=1.0):
    """Poses the rig from flexion angles per bone (POSES) or from ready-made rotations (gesture_rotations)."""
    for pb in rig.pose.bones:
        value = pose.get(pb.name)
        if isinstance(value, Quaternion):
            pb.rotation_quaternion = value
        else:
            pb.rotation_quaternion = Quaternion((1.0, 0.0, 0.0), -math.radians((value or 0.0) * amount))


def build_actions(rig):
    """Grab and Trigger run from the rest pose (first frame) to fully closed (last frame); gestures hold a pose."""
    rig.animation_data_create()
    actions = []
    for name, pose in POSES.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data.action = action
        for frame in range(1, ANIM_FRAMES + 1):
            t = (frame - 1) / (ANIM_FRAMES - 1.0)
            for pb in rig.pose.bones:
                if pb.name not in pose:
                    continue
                # fingers close from the knuckle outwards, the way a hand actually wraps around a grip
                order = int(pb.name[-1]) - 1
                k = min(1.0, max(0.0, t * 1.25 - 0.125 * order))
                pb.rotation_quaternion = Quaternion((1.0, 0.0, 0.0), -math.radians(pose[pb.name] * k * k * (3.0 - 2.0 * k)))
                pb.keyframe_insert("rotation_quaternion", frame=frame)
        actions.append(action)
        set_pose(rig, {})
    for name, gesture in GESTURES.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data.action = action
        for frame in range(1, ANIM_FRAMES + 1):
            # the last frame repeats the first, so a swaying gesture loops cleanly
            set_pose(rig, gesture_rotations(rig, gesture, (frame - 1) / (ANIM_FRAMES - 1.0)))
            for pb in rig.pose.bones:
                pb.keyframe_insert("rotation_quaternion", frame=frame)
        actions.append(action)
        set_pose(rig, {})
    rig.animation_data.action = None
    return actions


def export_fbx(obj, rig, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in (obj, rig):
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, ANIM_FRAMES
    bpy.context.scene.render.fps = ANIM_FPS
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
        mesh_smooth_type="OFF", use_mesh_modifiers=False, use_tspace=False,
        add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode="STRIP", embed_textures=False)
    print(f"[hand] fbx: {os.path.getsize(path) // 1024} KB -> {path}")


# ---------------------------------------------------------------------------------------------- preview


# name -> (direction the camera sits in, image up)
DEFAULT_VIEWS = {
    "top": ((0, 0, 1), (0, 1, 0)), "palm": ((0, 0, -1), (0, 1, 0)),
    "thumbside": ((1, 0, 0.05), (0, 0, 1)), "pinkyside": ((-1, 0, 0.05), (0, 0, 1)),
    "front": ((0, 1, 0.12), (0, 0, 1)), "persp": ((0.75, -0.9, 0.8), (0, 0, 1)),
    "persp_palm": ((0.7, 0.9, -0.8), (0, 0, -1)),
}


def draw_uv_layout(obj, path, size=1024):
    """Wireframe of the UV islands (seam edges in red), drawn without needing a UV editor."""
    import numpy as np
    img = np.full((size, size, 4), (0.08, 0.08, 0.09, 1.0), dtype=np.float32)
    mesh = obj.data
    uv = mesh.uv_layers.active.data
    seam = {tuple(sorted(e.vertices)) for e in mesh.edges if e.use_seam}
    for poly in mesh.polygons:
        loops = list(poly.loop_indices)
        for i, li in enumerate(loops):
            lj = loops[(i + 1) % len(loops)]
            a, b = np.array(uv[li].uv), np.array(uv[lj].uv)
            key = tuple(sorted((mesh.loops[li].vertex_index, mesh.loops[lj].vertex_index)))
            color = (1.0, 0.25, 0.2, 1.0) if key in seam else (0.75, 0.78, 0.82, 1.0)
            n = int(max(abs(b - a)) * size) + 2
            t = np.linspace(0.0, 1.0, n)[:, None]
            pts = np.clip(((a + (b - a) * t) * (size - 1)).astype(int), 0, size - 1)
            if key in seam or img[pts[:, 1], pts[:, 0], 0].max() < 0.9:
                img[pts[:, 1], pts[:, 0]] = color
    image = bpy.data.images.new("uv_layout", size, size)
    image.pixels.foreach_set(img.ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def render_previews(obj, out_dir, tag, views=None, target=None, scale=0.26):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 900, 900
    scene.render.film_transparent = False
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.19, 0.20, 0.22, 1)
    bg.inputs[1].default_value = 1.0
    for name, rot, energy in (("Key", (math.radians(50), 0, math.radians(35)), 3.0),
                              ("Fill", (math.radians(-60), 0, math.radians(-140)), 1.0),
                              ("Under", (math.radians(150), 0, math.radians(20)), 2.2)):
        light = bpy.data.objects.get(name)
        if not light:
            light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
            scene.collection.objects.link(light)
        light.rotation_euler = rot
        light.data.energy = energy
    cam = bpy.data.objects.get("PreviewCam")
    if not cam:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    scene.camera = cam
    target = target or Vector((0.012, 0.075, -0.004))
    views = views or DEFAULT_VIEWS
    for name, (direction, up) in views.items():
        back = Vector(direction).normalized()
        right = Vector(up).cross(back).normalized()
        cam_up = back.cross(right)
        cam.matrix_world = Matrix.Translation(target + back * 0.6) @ Matrix((right, cam_up, back)).transposed().to_4x4()
        scene.render.filepath = os.path.join(out_dir, f"{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    cg, skel, thumb = build_cage()
    obj = cage_to_object(cg)
    report_topology(obj, "cage")
    subdivide(obj)
    sculpt_nails(obj)
    report_topology(obj, "subdivided")
    unwrap(obj)
    report_uv_distortion(obj)
    os.makedirs(UNITY_DIR, exist_ok=True)
    image = paint_texture(obj, os.path.join(UNITY_DIR, "HandSkin.png"))
    for mat in skin_materials(image):
        obj.data.materials.append(mat)
    rig = build_rig(obj, skel, thumb)
    build_actions(rig)
    if PREVIEW_DIR:
        os.makedirs(PREVIEW_DIR, exist_ok=True)
        six = {k: v for k, v in DEFAULT_VIEWS.items() if k in ("top", "palm", "persp", "persp_palm", "thumbside", "front")}
        closeups = dict(views={
            "fingers": ((0.25, 0.55, 1.0), (0, 1, 0)), "thumb": ((1.0, -0.2, 0.9), (0, 1, 0)),
            "tips": ((0.1, 1.0, 0.45), (0, 0, 1)), "pads": ((0.1, 0.3, -1.0), (0, 1, 0)),
            "knuckles": ((-0.2, -0.5, 1.0), (0, 1, 0)), "palm": ((0.0, 0.0, -1.0), (0, 1, 0)),
        }, scale=0.10)
        if "shape" in STAGES:
            render_previews(obj, PREVIEW_DIR, "shape", views=six)
        if "nails" in STAGES:
            render_previews(obj, PREVIEW_DIR, "nails", target=Vector((0.012, 0.165, -0.012)), **closeups)
        if "detail" in STAGES:
            render_previews(obj, PREVIEW_DIR, "detail", target=Vector((0.0, 0.085, -0.004)), **closeups)
        if "uv" in STAGES:
            skin = [m for m in obj.data.materials]
            checker = checker_material()
            for i in range(len(skin)):
                obj.data.materials[i] = checker
            render_previews(obj, PREVIEW_DIR, "uv", views=six)
            for i, m in enumerate(skin):
                obj.data.materials[i] = m
            draw_uv_layout(obj, os.path.join(PREVIEW_DIR, "uv_layout.png"))
        if "pose" in STAGES:
            fist = dict(POSES["Grab"], **POSES["Trigger"])
            for tag, pose in (("pose_grab", POSES["Grab"]), ("pose_trigger", POSES["Trigger"]), ("pose_fist", fist)):
                set_pose(rig, pose)
                render_previews(obj, PREVIEW_DIR, tag, views={
                    "top": ((0.2, -0.3, 1.0), (0, 1, 0)), "side": ((1.0, 0.25, 0.15), (0, 0, 1)),
                    "under": ((-0.5, 0.6, -0.8), (0, 0, -1)),
                })
            set_pose(rig, {})
        if "gesture" in STAGES:
            for name, gesture in GESTURES.items():
                set_pose(rig, gesture_rotations(rig, gesture, 0.25))
                render_previews(obj, PREVIEW_DIR, "gesture_" + name, views={
                    "shown": ((0.25, 1.0, 0.25), (0, 0, 1)), "own": ((-0.2, -1.0, 0.45), (0, 0, 1)),
                }, target=Vector((0.01, 0.0, 0.07)), scale=0.34)
            set_pose(rig, {})
    blend = os.path.join(HERE, "hand.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    image.filepath = bpy.path.relpath(image.filepath_raw)
    bpy.ops.wm.save_mainfile()
    export_fbx(obj, rig, os.path.join(UNITY_DIR, "Hand.fbx"))


main()
