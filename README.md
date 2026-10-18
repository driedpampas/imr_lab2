# Handball with Unity-chan (IMR Lab 2/3)
Alexandru Bianca, Gurzu Matei

AI was used for:
- help with implementation
- help with debugging Unity errors
- help guiding the 3D modelling process
- enhancing the texture of the 3d model
- wrote the readme

A VR throwing game built with **OpenXR + XR Interaction Toolkit 3.3** in Unity 6 (6000.3.25f1).
You stand on the 7 m line of a sunny, toy-like field. Grab a handball from the floating racks
with either hand and throw it past **Unity-chan**, who guards the goal: she reads every shot,
reacts like a real keeper, dives across the goal, flops to the grass, lies there dizzy for a
couple of seconds (your chance to score again!) and springs back up.

Scene: `Assets/Handball/Scenes/Handball.unity` (first scene in Build Settings).

**Demo video:** [`video.mp4`](video.mp4) (recorded in the Editor with the XR Interaction Simulator).

## Important: two throwing modes

The game supports **both** a real physical throw and a simulator-friendly throw. It picks the right one
automatically (`HandballThrowAssist`, on each controller):

| | **On a VR headset** | **In the XR Interaction Simulator** (no headset) |
| --- | --- | --- |
| Grab | grip button, via XRI Near-Far **Interactor** + XR Grab **Interactable** | **G** (right hand) / **Shift + G** (left hand), same Interactor + Interactable |
| Throw | **Real hand velocity**: on release, `XRGrabInteractable` (throw-on-detach) measures how fast your hand was moving and gives the ball that velocity. A light assist only scales it by 1.15× and bends it at most 7° towards the goal. Throw soft and it falls short; throw sideways and it goes sideways. | **Point and throw**: the simulator cannot swing a controller (the mouse only rotates it and keyboard movement is ~0.2 m/s), so a physical throw would just drop the ball. Releasing G launches the ball on a ballistic arc (~12.5 m/s, a real penalty-throw speed) towards where you are looking, shown by a dotted arc and a target ring. |

> **The demo video uses the simulator mode, because no headset was available for recording.**
> The headset mode is implemented through XRI's standard throw-on-detach and its official aim-assist hook
> (`IXRAimAssist`), and its velocity handling is covered by an automated test, but it has not been tried on
> physical hardware. To force a mode, set **Mode** on the `HandballThrowAssist` component of
> *Left/Right Controller* (`Auto` / `Physical` / `PointAndThrow`).

## Playing in the Editor (XR Interaction Simulator, no headset)

Press **Play**. The XR Interaction Simulator spawns automatically (Editor only). Click inside
the Game view so it receives the keyboard and mouse.

| Action | Simulator input |
| --- | --- |
| Look / turn | hold **Right Mouse** and move the mouse (or the arrow keys) |
| Walk | **W A S D** (you can stay on the 7 m line, you don't need to walk) |
| Point at a ball | turn towards a rack (left or right of you) until the controller ray touches a ball |
| Grab with right hand | hold **G** |
| Grab with left hand | hold **Shift + G** |
| Aim | keep G held, look where you want the ball to go: a dotted arc and a ring on the goal show the throw (the ring turns yellow when the shot is on target) |
| Throw | release **G** (left hand: release **G** first, *then* Shift, because the simulator only routes input to the left hand while Shift is down) |

In the simulator a release launches the ball towards where you are looking; on a headset the throw
uses your real hand velocity (see **Two throwing modes** above).

## Playing on a headset

OpenXR is enabled for Standalone with the Oculus Touch, Meta Quest Touch Plus, Valve Index,
HTC Vive and Khronos Simple controller profiles. Connect via Quest Link / Air Link (or SteamVR),
press Play, grab a ball with the **grip** button of either hand and throw it for real.

## What happens in a shot

1. **Throw event**: releasing the ball (`XRGrabInteractable.selectExited`) goes through the
   `HandballThrowAssist` (`IXRAimAssist`) and is reported to `HandballGame` as a `Shot`.
2. **Unity-chan reacts**: `GoalkeeperController` predicts where the ball crosses her plane,
   adds human reaction time and misreading, then blocks standing, jumps, or dives. Her hands are
   driven by humanoid IK and her body has physics colliders, so saves are real collisions. After a
   dive she switches to a ragdoll (with the dive's momentum), lies dizzy, then blends back up.
3. **Result**: the whole ball crossing the goal line (trigger behind the line) is a **goal**.
   If Unity-chan touched the ball and it didn't go in, it's a **save**; anything else is a **miss**.
4. **Feedback**: scoreboard on the crossbar (goals + Unity-chan's saves), "GOAL!" / "SAVED!" /
   "POST!" pop-ups, confetti, goal jingle, cheer and party popper, net bulge, her speech bubbles
   ("Nooo~", "Hehe~", ...) and dizzy stars.

**Score** = number of goals. Difficulty is tuned so that shots straight at Unity-chan are almost
always saved, well-placed corner shots go in about 2 out of 3 times (she sometimes pulls off a
super save), and the time she is down after a dive is a free window.

## Lab 3: hands modelled in Blender

Both controllers now carry a hand modelled in Blender instead of the controller model.

![The hand model](Blender/previews/hand.jpg)

| UV islands (seams in red) | Checker texture: even squares, no stretching |
| --- | --- |
| ![UV layout](Blender/previews/uv_layout.png) | ![UV checker](Blender/previews/uv_checker.jpg) |

In the game, on both controllers (open hand, Grab, Trigger, and both on the left hand):

![The hands in the game](Docs/images/hands_in_game.jpg)

The Grab and Trigger animations in Blender (rows: Grab, Trigger, both):

![Grab and Trigger animations](Blender/previews/animations.jpg)

- **Model**: a five-finger left hand with nails and a sweatband at the wrist, about 10k quads
  (no triangles or n-gons, watertight). It is built by `Blender/make_hand.py`, a Blender Python
  script that lays out a quad control cage, subdivides it, shapes the nails, unwraps, paints the
  texture, rigs and animates the hand, then saves `Blender/hand.blend` and exports the FBX.
  Regenerate everything with `blender -b --factory-startup --python Blender/make_hand.py`.
- **UV unwrap**: seams run along the sides of the hand and fingers, giving three islands: back of
  the hand, palm, and the end of the wrist. Texel density stays within about 12% of the median
  over 90% of the surface and the mean angle error is 3 degrees, so a checker texture shows no
  visible stretching.
- **Texture**: `Assets/Handball/Hands/HandSkin.png` (2048x2048), painted by the script from each
  texel's position on the model, so it is continuous across the seams: skin tones, knuckle
  wrinkles, palm and finger creases, nails and the sweatband.
- **In Unity**: `Assets/Handball/Hands/Hand.fbx` is attached under *Left Controller* and
  *Right Controller* by **Handball > Setup > Attach Hands To Scene** (also part of
  **Handball > Build Scene**). The right hand is the same model under a parent scaled to -1 on X.
- **Animations**: the FBX carries clips made in Blender. **Grab** curls the middle, ring and
  little finger; **Trigger** curls the index finger. `HandAnimator` plays Grab from the
  controller's grip value and Trigger from its trigger value, on two animator layers so they can
  overlap. Six more clips are hand signs (next section).

| Action | Headset | XR Interaction Simulator |
| --- | --- | --- |
| Grab animation | grip button | **G** (left hand: **Shift + G**) |
| Trigger animation | trigger | **T** (left hand: **Shift + T**) |

**Videos** (no sound; filmed by the `Debug_RecordGrabAndTrigger` and `Debug_RecordBallGrab` tests
in `HandTests.cs`):

- [`hand_grab_trigger.mp4`](hand_grab_trigger.mp4): Grab, Trigger and both together, on the right
  hand and then the left. One side shows both hands from the front (mirrored, so your right hand is
  on the right), the other a close-up of the hand that moves.
- [`hand_ball_grab.mp4`](hand_ball_grab.mp4): the hand grabs a ball off the rack (**G**), carries it
  to the goal and throws it in by letting go, with a close-up of the fingers around the ball. The
  turn back to the goal plays at 3x speed.

| Lab 3 requirement | Where |
| --- | --- |
| Model a hand with 5 fingers (6p) | `Blender/hand.blend`, built by `Blender/make_hand.py` |
| UV unwrap + texture (2p) | seams and unwrap in `make_hand.py`; `Assets/Handball/Hands/HandSkin.png` |
| Export as FBX | `Assets/Handball/Hands/Hand.fbx`, exported by `make_hand.py` |
| Hand on both controllers, right one scaled -1 on X (2p) | `HandballHands` (Editor), `Left Hand` / `Right Hand` under the controllers |
| Grab and Trigger animations on the controller actions (bonus 2p) | `Grab` / `Trigger` clips in `Hand.fbx`, `HandAnimator`, `Assets/Handball/Generated/AC_Hand.controller`; videos `hand_grab_trigger.mp4`, `hand_ball_grab.mp4` |
| Creativity (bonus 1p) | hand signs that Unity-chan answers: `HandGestures`, `KeeperGestureReactions`, `KeeperFace` |

`Assets/Handball/Tests/HandTests.cs` checks all of this in Play mode through the simulator.

### Hand signs: Unity-chan reacts to you

With an empty hand you can show Unity-chan a sign and she answers it. The signs are six more
animations of the same Blender hand:

![Hand signs in Blender](Blender/previews/gestures.jpg)

![Hand signs in the game and Unity-chan's answers](Docs/images/hand_signs_in_game.jpg)

**Video:** [`hand_signs.mp4`](hand_signs.mp4) shows all six signs and her answers (split screen,
no sound; filmed by the `Debug_RecordHandSigns` test in `HandTests.cs`).

| Sign | Headset | Simulator | Unity-chan |
| --- | --- | --- | --- |
| Thumbs up | primary button (A / X) | **1** | smiles and raises her hand: "Thanks~!" |
| Thumbs down | primary button with the hand turned over | **N** | sulks and looks at the ground: "So mean..." |
| Peace | secondary button (B / Y) | **2** | poses back: "Peace~!" |
| Rock | primary + secondary | **1 + 2** | jumps: "Rock on!" |
| Middle finger | thumbstick click | **3** | gets angry ("How rude!") and stays **fired up for 12 s**: quicker reactions and faster hands, so insulting her makes her harder to beat |
| Wave | wave your open, raised hand | **B** | waves back: "Hi hi~!" |

In the simulator add **Shift** for the left hand, as with G and T. She only answers when she is
standing ready and you are looking her way, once per sign, and a shot interrupts her; while she
is answering she reacts to a shot 0.1 s later. A thumb sign is read from where the thumb actually
points, so turning your hand over really does turn a thumbs up into a thumbs down.

`HandGestures` reads the buttons and blends the sign in on a third animator layer;
`KeeperGestureReactions` picks her answer (speech bubble, facial expression through her blend
shapes in `KeeperFace`, an arm gesture or animation, a sound). The headset bindings and the
motion-based wave follow the same code path as the simulator keys but have not been tried on a
physical headset.

### Working on Linux

Unity's OpenXR plugin has no Linux runtime, so on Linux the game runs in the Editor with the
XR Interaction Simulator: pressing Play logs one OpenXR error and carries on. For a standalone
build use **Handball > Build > Linux Test Build (simulator, no OpenXR)**; it leaves the OpenXR
settings used on Windows untouched.

## Rubric mapping

| Requirement | Where |
| --- | --- |
| Setup OpenXR + XR Toolkit (5p) | OpenXR loader + controller profiles (`Assets/XR`), XRI 3.3 Starter Assets rig, Near-Far interactors on both hands, XR Interaction Simulator auto-spawn (`Assets/XRI/Settings`). Re-runnable via **Handball > Setup > Configure Project** |
| Handle throw event (3p) | `HandballBall` (grab/release with Interactor/Interactable), `HandballThrowAssist`, `GoalTrigger`, `GoalkeeperController` |
| Generate score + UI (2p) | `HandballGame` (shot outcome, goal count), `ScoreboardUI` (world-space TextMeshPro) |
| Bonus | Unity-chan AI with IK, dives and ragdoll, toon shader, confetti, sounds, net physics, trajectory preview, speech bubbles, stylized environment |

## Project layout

- `Assets/Handball/Scripts`: runtime code (assembly `Handball`)
- `Assets/Handball/Editor`: **Handball > Build Scene** regenerates the whole scene and all
  generated art (`Assets/Handball/Generated`); **Handball > Open Scene**
- `Assets/Handball/Shaders`: toon shader (cel shading, rim light, outlines; XR single-pass ready), sky, unlit FX
- `Assets/Handball/Tests`: play-mode tests (Window > General > Test Runner > PlayMode), including
  end-to-end tests that drive the real XR Interaction Simulator with keyboard input
- `Assets/UnityChan`: Unity-chan model and animations

## Credits

- Unity-chan © Unity Technologies Japan/UCL, used under the [Unity-chan License](https://unity-chan.com/contents/guideline_en/) (see `Docs/UnityChanLicense.md`).
- Sound effects: [Kenney](https://kenney.nl) Impact Sounds, Interface Sounds and Music Jingles (CC0). Whoosh, whistle, party popper, crowd cheer and birdsong are synthesized.
