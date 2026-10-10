# Handball with Unity-chan (IMR Lab 2)

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
