using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Handball.Tests
{
    /// <summary>
    /// Plays the game exactly like a person using the XR Interaction Simulator: virtual keyboard input turns the
    /// head (arrow keys = the same rotation as dragging with the right mouse button), the controller ray picks a
    /// ball off the rack, G grabs it, the player looks at a corner of the goal and lets go of G.
    /// </summary>
    public class SimulatorEndToEndTests
    {
        const string ScenePath = "Assets/Handball/Scenes/Handball.unity";
        Keyboard m_Keyboard;
        GameObject m_Simulator;
        InputSettings.BackgroundBehavior m_PrevBackground;
        InputSettings.EditorInputBehaviorInPlayMode m_PrevEditor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            m_PrevBackground = InputSystem.settings.backgroundBehavior;
            m_PrevEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(ScenePath);
#endif
#if UNITY_EDITOR
            // XRI skips its automatic simulator spawn while tests run, so spawn the same prefab Play mode uses.
            if (XRInteractionSimulator.instance == null)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Samples/XR Interaction Toolkit/3.3.2/XR Interaction Simulator/XR Interaction Simulator.prefab");
                Assert.IsNotNull(prefab, "Simulator prefab missing");
                m_Simulator = Object.Instantiate(prefab);
            }
#endif
            m_Keyboard = InputSystem.GetDevice<Keyboard>() ?? InputSystem.AddDevice<Keyboard>();
            yield return new WaitForSeconds(1.2f); // let the simulator spawn devices and the modality manager show the controllers
        }

        [TearDown]
        public void TearDown()
        {
            Release();
            if (m_Simulator) Object.Destroy(m_Simulator);
            InputSystem.settings.backgroundBehavior = m_PrevBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = m_PrevEditor;
        }

        void Hold(params Key[] keys) => InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState(keys));
        void Release() { if (m_Keyboard != null) InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState()); }

        static Transform Head => Camera.main.transform;

        /// <summary>Turns the simulated head with the arrow keys until the predicate holds.</summary>
        IEnumerator TurnUntil(Key key, System.Func<bool> done, int maxFrames = 3000)
        {
            for (int i = 0; i < maxFrames && !done(); i++)
            {
                Hold(key);
                if (i % 150 == 0) Debug.Log($"SIM TURN frame {i} yaw={SignedYaw(Head.forward):F1} kb={m_Keyboard[key].isPressed} sim={XRInteractionSimulator.instance?.manipulatingFPS}");
                yield return null;
            }
            Release();
            yield return null;
        }

        static float SignedYaw(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        static void CaptureXrCamera(string name)
        {
            var cam = Camera.main;
            Directory.CreateDirectory("Logs/shots");
            var rt = new RenderTexture(1280, 720, 24);
            var prevTarget = cam.targetTexture;
            float prevAspect = cam.aspect;
            cam.targetTexture = rt;
            cam.aspect = 16f / 9f;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = prevTarget;
            cam.aspect = prevAspect;
            File.WriteAllBytes($"Logs/shots/{name}.png", tex.EncodeToPNG());
            Object.Destroy(rt);
            Object.Destroy(tex);
        }

        IEnumerator GrabFromRackAndShoot(bool leftHand, Vector2 goalTarget, string tag)
        {
            Assert.IsNotNull(XRInteractionSimulator.instance, "XR Interaction Simulator should be spawned in the Editor");
            var game = HandballGame.Instance;
            var rig = Object.FindFirstObjectByType<XROrigin>();
            var controllerName = leftHand ? "Left Controller" : "Right Controller";
            var controller = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == controllerName);
            Assert.IsTrue(controller.gameObject.activeInHierarchy, controllerName + " should be tracked and visible");
            var interactor = controller.GetComponentInChildren<NearFarInteractor>();
            Assert.IsNotNull(interactor);

            CaptureXrCamera($"sim_{tag}_0_start");

            // 1) Turn towards the rack until this hand's ray is on a ball.
            var turnKey = leftHand ? Key.LeftArrow : Key.RightArrow;
            HandballBall Target()
            {
                var targets = new System.Collections.Generic.List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable>();
                interactor.GetValidTargets(targets);
                return targets.Select(t => (t as Component)?.GetComponent<HandballBall>()).FirstOrDefault(b => b && b.IsOnRack);
            }
            int dbg = 0;
            yield return TurnUntil(turnKey, () =>
            {
                if (dbg++ % 10 == 0)
                {
                    var targets = new System.Collections.Generic.List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable>();
                    interactor.GetValidTargets(targets);
                    var ctl = controller;
                    bool hit = Physics.Raycast(ctl.position, ctl.forward, out var h, 20f);
                    Debug.Log($"SIM RAY yaw={SignedYaw(Head.forward):F1} ctlPos={ctl.position} ctlFwd={ctl.forward} hit={(hit ? h.collider.name : "-")} targets={targets.Count} first={(targets.Count > 0 ? (targets[0] as Component)?.name : "-")} far={interactor.enableFarCasting} near={interactor.enableNearCasting}");
                }
                return Target() != null;
            }, 400);
            var ball = Target();
            Assert.IsNotNull(ball, "Turning towards the rack should put a ball under the controller ray");
            CaptureXrCamera($"sim_{tag}_1_pointing");

            // 2) Hold the grip (G, plus Shift for the left hand).
            var grip = leftHand ? new[] { Key.G, Key.LeftShift } : new[] { Key.G };
            for (int i = 0; i < 20; i++)
            {
                Hold(grip);
                yield return null;
            }
            Assert.IsTrue(ball.IsHeld, "Pressing G should grab the ball");

            // 3) Keep holding G and turn back to the goal, aiming at the requested spot.
            var aimPoint = game.goal.TransformPoint(new Vector3(goalTarget.x, goalTarget.y, 0f));
            float wantedYaw = SignedYaw(aimPoint - Head.position);
            var backKey = leftHand ? Key.RightArrow : Key.LeftArrow;
            for (int i = 0; i < 3000; i++)
            {
                float yaw = SignedYaw(Head.forward);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, wantedYaw)) < 0.3f) break;
                Hold(Key.G, Mathf.DeltaAngle(yaw, wantedYaw) > 0 ? Key.RightArrow : Key.LeftArrow, leftHand ? Key.LeftShift : Key.G);
                yield return null;
            }
            float wantedPitch = -Mathf.Asin((aimPoint - Head.position).normalized.y) * Mathf.Rad2Deg;
            for (int i = 0; i < 1500; i++)
            {
                float pitch = Mathf.DeltaAngle(0f, Head.eulerAngles.x);
                if (Mathf.Abs(pitch - wantedPitch) < 0.3f) break;
                Hold(Key.G, pitch > wantedPitch ? Key.UpArrow : Key.DownArrow, leftHand ? Key.LeftShift : Key.G);
                yield return null;
            }
            Hold(grip);
            yield return null;
            Assert.IsTrue(ball.IsHeld, "Ball should still be held while aiming");
            yield return new WaitForSeconds(0.2f);
            CaptureXrCamera($"sim_{tag}_2_aiming");

            // 4) Let go of G: the ball should fly at the aim point.
            // Let go of G first; for the left hand Shift stays held (the simulator only routes input to the
            // left controller while Shift is down).
            if (leftHand)
            {
                for (int i = 0; i < 3; i++)
                {
                    Hold(Key.LeftShift);
                    yield return null;
                }
            }
            Release();
            for (int i = 0; i < 6 && ball.IsHeld; i++) yield return null;
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(ball.IsHeld, "Letting go of G should release the ball");
            Assert.IsNotNull(ball.CurrentShot, "Releasing G should throw the ball");
            var shot = ball.CurrentShot;
            yield return new WaitForSeconds(0.25f);
            CaptureXrCamera($"sim_{tag}_3_flying");
            float t = 0f;
            while (shot.result == ShotResult.Pending && t < 6f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            CaptureXrCamera($"sim_{tag}_4_result_{shot.result}");
            Debug.Log($"SIM E2E {tag}: result={shot.result} v={shot.velocity}");
            Assert.AreNotEqual(ShotResult.Pending, shot.result);
            Assert.AreNotEqual(ShotResult.Miss, shot.result, "An aimed shot at the goal should be a goal or a save, not a miss");
        }

        [UnityTest]
        public IEnumerator Simulator_RightHand_GrabAimAndScore()
        {
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            keeper.enabled = false; // deterministic: aim away from a frozen keeper
            yield return GrabFromRackAndShoot(false, new Vector2(1.1f, 1.5f), "right");
            Assert.AreEqual(1, HandballGame.Instance.Goals);
        }

        [UnityTest]
        public IEnumerator Simulator_LeftHand_GrabAimAndShootAtKeeper()
        {
            yield return GrabFromRackAndShoot(true, new Vector2(-0.9f, 0.6f), "left");
        }
    }
}
