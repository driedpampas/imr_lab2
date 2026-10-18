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

namespace Handball.Tests
{
    /// <summary>
    /// The Blender hands on the controllers, driven the way a player without a headset drives them: the
    /// XR Interaction Simulator's grip key (G) must play the Grab animation and its trigger key (T) the Trigger
    /// animation, each on its own fingers; its button keys (1, 2, 3) must make hand signs, and Unity-chan must
    /// answer them.
    /// </summary>
    public class HandTests
    {
        const string ScenePath = "Assets/Handball/Scenes/Handball.unity";
        static readonly string[] k_GrabFingers = { "Middle", "Ring", "Pinky" };

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
            // XRI skips its automatic simulator spawn while tests run, so spawn the same prefab Play mode uses.
            if (XRInteractionSimulator.instance == null)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Samples/XR Interaction Toolkit/3.3.2/XR Interaction Simulator/XR Interaction Simulator.prefab");
                Assert.IsNotNull(prefab, "Simulator prefab missing");
                m_Simulator = Object.Instantiate(prefab);
            }
#else
            yield return SceneManager.LoadSceneAsync(ScenePath);
#endif
            // A keyboard of our own: the machine's real one is disabled while the Editor is unfocused or in batch mode.
            m_Keyboard = InputSystem.AddDevice<Keyboard>("Hand Test Keyboard");
            yield return new WaitForSeconds(1.2f); // let the simulator spawn devices and the modality manager show the controllers
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            if (m_Keyboard != null) InputSystem.RemoveDevice(m_Keyboard);
            if (m_Simulator) Object.Destroy(m_Simulator);
            InputSystem.settings.backgroundBehavior = m_PrevBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = m_PrevEditor;
        }

        void Hold(params Key[] keys) => InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState(keys));
        void Release() => InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState());

        IEnumerator HoldFor(float seconds, params Key[] keys)
        {
            for (float end = Time.time + seconds; Time.time < end;)
            {
                Hold(keys);
                yield return null;
            }
        }

        static HandAnimator Hand(string side)
        {
            var rig = Object.FindFirstObjectByType<XROrigin>();
            var controller = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == side + " Controller");
            return controller.GetComponentInChildren<HandAnimator>(true);
        }

        static Transform Bone(HandAnimator hand, string name) => hand.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        /// <summary>How far (degrees) each finger's middle joint is bent away from its rest pose.</summary>
        static System.Func<string, float> BendFromRest(HandAnimator hand)
        {
            var rest = new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" }.ToDictionary(f => f, f => Bone(hand, f + "2").localRotation);
            return finger => Quaternion.Angle(rest[finger], Bone(hand, finger + "2").localRotation);
        }

        static void Capture(string name, HandAnimator hand)
        {
            Directory.CreateDirectory("Logs/shots");
            var head = Camera.main.transform;
            var target = hand.transform.parent.position + head.forward * 0.05f;
            var views = new (string tag, Vector3 position)[]
            {
                ("above", target + Vector3.up * 0.30f - head.forward * 0.16f + head.right * 0.04f),
                ("side", target - head.right * 0.34f + Vector3.up * 0.06f),
                ("front", target + head.forward * 0.5f + Vector3.up * 0.12f),
                ("player", head.position),
            };
            foreach (var (tag, position) in views)
            {
                var go = new GameObject("Hand Capture");
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(Camera.main);
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.fieldOfView = tag == "player" ? 70f : 40f;
                cam.nearClipPlane = 0.01f;
                go.transform.position = position;
                go.transform.rotation = tag == "player" ? head.rotation : Quaternion.LookRotation(target - position, Vector3.up);
                var rt = new RenderTexture(960, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes($"Logs/shots/hand_{name}_{tag}.png", tex.EncodeToPNG());
                cam.targetTexture = null;
                Object.Destroy(rt);
                Object.Destroy(tex);
                Object.Destroy(go);
            }
        }

        [UnityTest]
        public IEnumerator Hands_AreOnBothControllers_AndTheRightOneIsTheLeftMirroredOnX()
        {
            var left = Hand("Left");
            var right = Hand("Right");
            Assert.IsNotNull(left, "Left Controller should carry a hand");
            Assert.IsNotNull(right, "Right Controller should carry a hand");
            Assert.IsTrue(left.gameObject.activeInHierarchy && right.gameObject.activeInHierarchy, "Hands should be visible with the controllers");

            Assert.AreEqual(new Vector3(1f, 1f, 1f), left.transform.parent.localScale);
            Assert.AreEqual(new Vector3(-1f, 1f, 1f), right.transform.parent.localScale, "The right hand is the left model scaled by -1 on X");
            var leftMesh = left.GetComponentInChildren<SkinnedMeshRenderer>();
            var rightMesh = right.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.AreSame(leftMesh.sharedMesh, rightMesh.sharedMesh, "Both hands use the one Blender model");
            Assert.IsNotNull(leftMesh.sharedMaterial.mainTexture, "The hand should be textured");
            Assert.IsTrue(leftMesh.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0), "The mesh should be UV mapped");

            // Thumbs point at each other and both hands have their back up, as real hands do.
            var controllerRight = right.transform.parent.parent.right;
            Assert.Greater(Vector3.Dot(Bone(left, "Thumb3").position - Bone(left, "Pinky3").position, left.transform.parent.parent.right), 0.03f);
            Assert.Less(Vector3.Dot(Bone(right, "Thumb3").position - Bone(right, "Pinky3").position, controllerRight), -0.03f);
            Capture("rest_left", left);
            Capture("rest_right", right);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Simulator_Grip_PlaysGrabOnMiddleRingAndLittleFinger()
        {
            var hand = Hand("Right");
            var bend = BendFromRest(hand);

            yield return HoldFor(0.6f, Key.G);
            Assert.Greater(hand.Grip, 0.99f, "Holding grip should play Grab to the end");
            Assert.Less(hand.Trigger, 0.01f);
            foreach (var finger in k_GrabFingers)
                Assert.Greater(bend(finger), 60f, finger + " finger should curl in the Grab animation");
            Assert.Less(bend("Index"), 1f, "Grab must leave the index finger alone");
            Capture("grab", hand);

            Release();
            yield return new WaitForSeconds(0.6f);
            Assert.Less(hand.Grip, 0.01f, "Letting go should play Grab back to the open hand");
            foreach (var finger in k_GrabFingers)
                Assert.Less(bend(finger), 1f, finger + " finger should reopen");
        }

        [UnityTest]
        public IEnumerator Simulator_Trigger_PlaysTriggerOnIndexFinger()
        {
            var hand = Hand("Right");
            var bend = BendFromRest(hand);

            yield return HoldFor(0.6f, Key.T);
            Assert.Greater(hand.Trigger, 0.99f, "Holding the trigger should play Trigger to the end");
            Assert.Less(hand.Grip, 0.01f);
            Assert.Greater(bend("Index"), 50f, "Index finger should curl in the Trigger animation");
            foreach (var finger in k_GrabFingers)
                Assert.Less(bend(finger), 1f, "Trigger must leave the " + finger + " finger alone");
            Capture("trigger", hand);

            Release();
            yield return new WaitForSeconds(0.6f);
            Assert.Less(bend("Index"), 1f, "Index finger should reopen");
        }

        [UnityTest]
        public IEnumerator Simulator_GripAndTrigger_PlayTogether_OnTheLeftHandToo()
        {
            var left = Hand("Left");
            var right = Hand("Right");
            var bend = BendFromRest(left);

            // The simulator routes input to the left controller while Shift is held.
            yield return HoldFor(0.7f, Key.LeftShift, Key.G, Key.T);
            Assert.Greater(left.Grip, 0.99f);
            Assert.Greater(left.Trigger, 0.99f);
            Assert.Less(right.Grip, 0.01f, "Only the hand whose controller is squeezed should close");
            foreach (var finger in k_GrabFingers)
                Assert.Greater(bend(finger), 60f);
            Assert.Greater(bend("Index"), 50f);
            Capture("fist_left", left);

            yield return HoldFor(0.1f, Key.LeftShift); // let go of G and T before Shift so the left hand gets the release
        }

        // ------------------------------------------------------------------ hand signs

        static readonly (HandGesture gesture, Key[] keys)[] k_Signs =
        {
            (HandGesture.ThumbsUp, new[] { Key.Digit1 }),
            (HandGesture.Peace, new[] { Key.Digit2 }),
            (HandGesture.MiddleFinger, new[] { Key.Digit3 }),
            (HandGesture.Rock, new[] { Key.Digit1, Key.Digit2 }),
            (HandGesture.ThumbsDown, new[] { Key.N }),
            (HandGesture.Wave, new[] { Key.B }),
        };

        static void CaptureKeeper(string name)
        {
            Directory.CreateDirectory("Logs/shots");
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            var target = keeper.transform.position + Vector3.up * 1.25f;
            var go = new GameObject("Keeper Capture");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 38f;
            go.transform.position = target + keeper.transform.forward * 2.6f + keeper.transform.right * 0.5f + Vector3.up * 0.15f;
            go.transform.rotation = Quaternion.LookRotation(target - go.transform.position, Vector3.up);
            var rt = new RenderTexture(720, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"Logs/shots/keeper_{name}.png", tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(tex);
            Object.Destroy(go);
        }

        IEnumerator HoldUntil(System.Func<bool> done, float timeout, params Key[] keys)
        {
            for (float end = Time.time + timeout; Time.time < end && !done();)
            {
                Hold(keys);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Simulator_Buttons_MakeEveryHandSign_AndUnityChanAnswersEach()
        {
            var hand = Hand("Right");
            var signs = hand.GetComponent<HandGestures>();
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            var reactions = keeper.GetComponent<KeeperGestureReactions>();
            var face = keeper.GetComponent<KeeperFace>();
            Assert.IsNotNull(signs, "The hand should be able to make signs");
            Assert.IsNotNull(reactions, "Unity-chan should react to hand signs");

            foreach (var (gesture, keys) in k_Signs)
            {
                float asked = Time.time;
                yield return HoldUntil(() => reactions.LastAnswered == gesture && reactions.LastAnsweredTime >= asked, 5f, keys);
                Assert.AreEqual(gesture, signs.Shown, $"Holding {string.Join("+", keys)} should show {gesture}");
                Assert.AreEqual(gesture, reactions.LastAnswered, $"Unity-chan should answer {gesture}");
                Assert.GreaterOrEqual(reactions.LastAnsweredTime, asked);
                Assert.AreEqual(GoalkeeperController.State.Reacting, keeper.CurrentState);
                Assert.IsTrue(keeper.emote.bubble.gameObject.activeSelf, "She should say something");
                Debug.Log($"HAND SIGN {gesture}: \"{keeper.emote.label.text}\" face={face.Current}");

                yield return HoldFor(0.45f, keys);
                Capture("sign_" + gesture, hand);
                CaptureKeeper("answers_" + gesture);

                Release();
                yield return new WaitForSeconds(0.4f);
                Assert.AreEqual(HandGesture.None, signs.Shown, "Letting go should end the sign");
                for (float end = Time.time + 4f; Time.time < end && keeper.CurrentState != GoalkeeperController.State.Idle;) yield return null;
                Assert.AreEqual(GoalkeeperController.State.Idle, keeper.CurrentState, "She should go back to guarding the goal");
            }
        }

        [UnityTest]
        public IEnumerator MiddleFinger_MakesUnityChanAngryAndFiredUp_ThumbsUpDoesNot()
        {
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            var reactions = keeper.GetComponent<KeeperGestureReactions>();
            var face = keeper.GetComponent<KeeperFace>();

            yield return HoldUntil(() => reactions.LastAnswered == HandGesture.ThumbsUp, 4f, Key.Digit1);
            Assert.AreEqual(HandGesture.ThumbsUp, reactions.LastAnswered);
            Assert.AreEqual(KeeperFace.Expression.Smile, face.Current);
            Assert.IsFalse(keeper.IsFiredUp, "Being nice should not make her harder to beat");
            Release();
            for (float end = Time.time + 4f; Time.time < end && keeper.CurrentState != GoalkeeperController.State.Idle;) yield return null;

            yield return HoldUntil(() => reactions.LastAnswered == HandGesture.MiddleFinger, 5f, Key.Digit3);
            Assert.AreEqual(HandGesture.MiddleFinger, reactions.LastAnswered);
            Assert.AreEqual(KeeperFace.Expression.Angry, face.Current);
            Assert.IsTrue(keeper.IsFiredUp, "An insult should fire her up");
            Assert.AreEqual(keeper.emote.angryColor, keeper.emote.label.color);
        }

        [UnityTest]
        public IEnumerator ThumbSign_IsReadFromWhereTheThumbPoints_AndTheLeftHandSignsWithShift()
        {
            var right = Hand("Right").GetComponent<HandGestures>();
            var left = Hand("Left").GetComponent<HandGestures>();

            // Same button, hand turned over: what was a thumbs up now points down.
            right.transform.parent.localRotation = Quaternion.Euler(0f, 0f, 180f);
            yield return HoldFor(0.7f, Key.Digit1);
            Assert.AreEqual(HandGesture.ThumbsUp, right.Pose);
            Assert.AreEqual(HandGesture.ThumbsDown, right.Shown, "A thumbs up held upside down is a thumbs down");
            Assert.AreEqual(HandGesture.None, left.Shown);
            right.transform.parent.localRotation = Quaternion.identity;
            Release();
            yield return new WaitForSeconds(0.4f);

            yield return HoldFor(0.7f, Key.LeftShift, Key.Digit2);
            Assert.AreEqual(HandGesture.Peace, left.Shown, "Shift routes the simulator's buttons to the left controller");
            Assert.AreEqual(HandGesture.None, right.Shown);
            yield return HoldFor(0.1f, Key.LeftShift);
        }

        [Test]
        public void WaveDetector_SpotsAWave_ButNotAReachOrALoweredHand()
        {
            bool Run(System.Func<float, float> sideways, bool raised)
            {
                var detector = new WaveDetector();
                bool waved = false;
                for (float t = 0f; t < 2.5f; t += 1f / 72f) waved |= detector.Sample(t, sideways(t), raised);
                return waved;
            }

            Assert.IsTrue(Run(t => 0.12f * Mathf.Sin(t * 2f * Mathf.PI * 2f), true), "Swinging 12 cm either way twice a second is a wave");
            Assert.IsFalse(Run(t => 0.25f * t, true), "Reaching sideways once is not a wave");
            Assert.IsFalse(Run(t => 0.01f * Mathf.Sin(t * 30f), true), "A trembling hand is not a wave");
            Assert.IsFalse(Run(t => 0.12f * Mathf.Sin(t * 2f * Mathf.PI * 2f), false), "A hand down by the hip is not waving at anyone");
        }

        // ------------------------------------------------------------------ recording

        /// <summary>
        /// Films a few cameras as numbered frames in a folder (one "{tag}_{frame}.jpg" per camera and frame) at a
        /// fixed 30 frames per second of game time, plus a captions.tsv saying from which frame each caption shows.
        /// The videos in the README are put together from these frames.
        /// </summary>
        sealed class Recorder
        {
            readonly string m_Dir;
            readonly System.Collections.Generic.List<(Camera cam, string tag, Texture2D tex)> m_Cameras = new();
            readonly System.Text.StringBuilder m_Captions = new();

            public int Frame { get; private set; }
            /// <summary>Called before every frame, to move the cameras.</summary>
            public System.Action BeforeShot;

            public Recorder(string dir)
            {
                m_Dir = dir;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                Time.captureFramerate = 30;
                Caption("");
            }

            public Camera Film(string tag, int width, int height, float fov)
            {
                var cam = new GameObject("Recording " + tag).AddComponent<Camera>();
                cam.CopyFrom(Camera.main);
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.02f;
                cam.targetTexture = new RenderTexture(width, height, 24);
                cam.enabled = false;
                m_Cameras.Add((cam, tag, new Texture2D(width, height, TextureFormat.RGB24, false)));
                return cam;
            }

            public void Caption(string text) => m_Captions.AppendLine($"{Frame}\t{text}");

            public void Shoot()
            {
                BeforeShot?.Invoke();
                foreach (var (cam, tag, tex) in m_Cameras)
                {
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = cam.targetTexture;
                    tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes($"{m_Dir}/{tag}_{Frame:D5}.jpg", tex.EncodeToJPG(92));
                }
                Frame++;
            }

            public void Finish()
            {
                Time.captureFramerate = 0;
                File.WriteAllText($"{m_Dir}/captions.tsv", m_Captions.ToString());
                Debug.Log($"RECORDING: {Frame} frames in {m_Dir}");
                foreach (var (cam, _, tex) in m_Cameras)
                {
                    var rt = cam.targetTexture;
                    cam.targetTexture = null;
                    Object.Destroy(rt);
                    Object.Destroy(cam.gameObject);
                    Object.Destroy(tex);
                }
            }
        }

        /// <summary>Holds the keys for a while (at least one frame), filming every frame.</summary>
        IEnumerator Roll(Recorder recorder, float seconds, params Key[] keys)
        {
            var end = Time.time + seconds;
            do
            {
                Hold(keys);
                yield return null;
                recorder.Shoot();
            } while (Time.time < end);
        }

        /// <summary>A camera on the thumb side of the hand, looking along the palm so every finger's curl shows in profile.</summary>
        static void SideOn(Camera cam, HandAnimator hand, bool leftHand)
        {
            var head = Camera.main.transform;
            var controller = hand.transform.parent.parent;
            var target = controller.position + controller.forward * 0.07f;
            var towardsThumb = leftHand ? head.right : -head.right;
            var position = target + towardsThumb * 0.30f + head.forward * 0.13f + Vector3.up * 0.10f;
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
        }

        /// <summary>
        /// Films the Grab and Trigger animations as the simulator's G and T keys play them (Shift for the left hand):
        /// both hands from the front, and the hand that moves from its thumb side. Run it by name; it is skipped otherwise.
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Debug_RecordGrabAndTrigger()
        {
            var left = Hand("Left");
            var right = Hand("Right");
            var head = Camera.main.transform;
            var recorder = new Recorder("Logs/recording_fingers");
            var frontCam = recorder.Film("front", 640, 720, 48f);
            var sideCam = recorder.Film("side", 640, 720, 36f);
            var filmLeft = false;
            recorder.BeforeShot = () =>
            {
                var middle = (left.transform.parent.parent.position + right.transform.parent.parent.position) * 0.5f + head.forward * 0.05f;
                var position = middle + head.forward * 0.42f + Vector3.up * 0.16f;
                frontCam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(middle - position, Vector3.up));
                SideOn(sideCam, filmLeft ? left : right, filmLeft);
            };

            var takes = new (string caption, Key[] keys, bool leftHand)[]
            {
                ("Grab  -  G", new[] { Key.G }, false),
                ("Trigger  -  T", new[] { Key.T }, false),
                ("Grab + Trigger  -  G + T", new[] { Key.G, Key.T }, false),
                ("Left hand: Grab  -  Shift + G", new[] { Key.LeftShift, Key.G }, true),
                ("Left hand: Trigger  -  Shift + T", new[] { Key.LeftShift, Key.T }, true),
                ("Left hand: both  -  Shift + G + T", new[] { Key.LeftShift, Key.G, Key.T }, true),
            };
            yield return Roll(recorder, 0.8f);
            foreach (var (caption, keys, leftHand) in takes)
            {
                filmLeft = leftHand;
                recorder.Caption(caption);
                yield return Roll(recorder, 1.6f, keys);
                // Let go of the buttons before Shift, so the left hand gets the release too.
                yield return Roll(recorder, 0.2f, leftHand ? new[] { Key.LeftShift } : new Key[0]);
                yield return Roll(recorder, 0.7f);
                recorder.Caption("");
                yield return Roll(recorder, 0.3f);
            }
            recorder.Finish();
            Assert.Less(left.Grip + left.Trigger + right.Grip + right.Trigger, 0.01f, "Both hands should be open again");
        }

        /// <summary>
        /// Films the player grabbing a ball off the rack with the right hand (G), carrying it to the goal and
        /// throwing it by letting go: the player's view and a camera following the hand. Run it by name.
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Debug_RecordBallGrab()
        {
            var hand = Hand("Right");
            var controller = hand.transform.parent.parent;
            var interactor = controller.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>();
            var game = HandballGame.Instance;
            var head = Camera.main.transform;
            var recorder = new Recorder("Logs/recording_ball");
            var playerCam = recorder.Film("player", 1280, 720, 60f);
            var handCam = recorder.Film("hand", 480, 480, 42f);
            HandballBall held = null;
            recorder.BeforeShot = () =>
            {
                // Look a little down towards the hand, so it stays in the player's shot.
                var toHand = (controller.position - head.position).normalized;
                playerCam.transform.SetPositionAndRotation(head.position, Quaternion.LookRotation(head.forward * 0.8f + toHand * 0.2f, Vector3.up));
                var target = held && held.IsHeld ? held.transform.position : controller.position + controller.forward * 0.06f;
                var position = target - head.right * 0.22f + head.forward * 0.26f + Vector3.up * 0.12f;
                handCam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            };

            HandballBall Target()
            {
                var targets = new System.Collections.Generic.List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable>();
                interactor.GetValidTargets(targets);
                return targets.Select(t => (t as Component)?.GetComponent<HandballBall>()).FirstOrDefault(b => b && b.IsOnRack);
            }
            static float Yaw(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            yield return Roll(recorder, 0.8f);
            recorder.Caption("Turn to the ball rack  -  arrow keys");
            for (int i = 0; i < 600 && Target() == null; i++) yield return Roll(recorder, 0f, Key.RightArrow);
            var ball = Target();
            Assert.IsNotNull(ball, "Turning towards the rack should put a ball under the controller ray");
            yield return Roll(recorder, 0.5f);

            recorder.Caption("Grab  -  G");
            held = ball;
            yield return Roll(recorder, 1.4f, Key.G);
            Assert.IsTrue(ball.IsHeld, "Pressing G should grab the ball");

            recorder.Caption("Hold G and turn to the goal");
            var aimPoint = game.goal.TransformPoint(new Vector3(1.1f, 1.5f, 0f));
            float wantedYaw = Yaw(aimPoint - head.position);
            for (int i = 0; i < 900 && Mathf.Abs(Mathf.DeltaAngle(Yaw(head.forward), wantedYaw)) > 0.3f; i++)
                yield return Roll(recorder, 0f, Key.G, Mathf.DeltaAngle(Yaw(head.forward), wantedYaw) > 0 ? Key.RightArrow : Key.LeftArrow);
            float wantedPitch = -Mathf.Asin((aimPoint - head.position).normalized.y) * Mathf.Rad2Deg;
            for (int i = 0; i < 600 && Mathf.Abs(Mathf.DeltaAngle(0f, head.eulerAngles.x) - wantedPitch) > 0.3f; i++)
                yield return Roll(recorder, 0f, Key.G, Mathf.DeltaAngle(0f, head.eulerAngles.x) > wantedPitch ? Key.UpArrow : Key.DownArrow);
            yield return Roll(recorder, 0.6f, Key.G);
            Assert.IsTrue(ball.IsHeld, "The ball should still be in the hand while aiming");

            recorder.Caption("Throw  -  let go of G");
            yield return Roll(recorder, 0.3f);
            Assert.IsFalse(ball.IsHeld, "Letting go of G should throw the ball");
            var shot = ball.CurrentShot;
            Assert.IsNotNull(shot);
            for (float t = 0f; shot.result == ShotResult.Pending && t < 6f; t += Time.deltaTime) yield return Roll(recorder, 0f);
            recorder.Caption(shot.result == ShotResult.Goal ? "Goal!" : shot.result == ShotResult.Save ? "Saved by Unity-chan!" : "Missed");
            yield return Roll(recorder, 2f);
            recorder.Finish();
            Debug.Log($"BALL RECORDING: {shot.result}");
        }

        /// <summary>
        /// Films every hand sign and Unity-chan's answer as numbered frames in Logs/recording (the player's view and
        /// a camera on her). Run it by name; it is skipped otherwise.
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Debug_RecordHandSigns()
        {
            var hand = Hand("Right");
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            var head = Camera.main.transform;
            var recorder = new Recorder("Logs/recording");
            var playerCam = recorder.Film("player", 640, 720, 62f);
            var keeperCam = recorder.Film("keeper", 640, 720, 38f);
            var herFocus = keeper.transform.position + Vector3.up * 1.35f;
            keeperCam.transform.position = herFocus + keeper.transform.forward * 3.6f + keeper.transform.right * 0.45f;
            keeperCam.transform.rotation = Quaternion.LookRotation(herFocus - keeperCam.transform.position, Vector3.up);
            recorder.BeforeShot = () =>
            {
                // Look past the right hand at the goal, so both the sign and her answer are in the player's shot.
                var toHand = (hand.transform.parent.position + Vector3.up * 0.12f - head.position).normalized;
                playerCam.transform.SetPositionAndRotation(head.position, Quaternion.LookRotation(head.forward * 0.55f + toHand * 0.45f, Vector3.up));
            };

            var takes = new (string caption, Key[] keys)[]
            {
                ("Wave  -  B", new[] { Key.B }),
                ("Thumbs up  -  1", new[] { Key.Digit1 }),
                ("Peace  -  2", new[] { Key.Digit2 }),
                ("Rock  -  1 + 2", new[] { Key.Digit1, Key.Digit2 }),
                ("Thumbs down  -  N", new[] { Key.N }),
                ("Middle finger  -  3", new[] { Key.Digit3 }),
            };
            yield return Roll(recorder, 1.0f);
            foreach (var (caption, keys) in takes)
            {
                recorder.Caption(caption);
                yield return Roll(recorder, 2.3f, keys);
                yield return Roll(recorder, 0.4f);
                recorder.Caption("");
                for (float end = Time.time + 4f; Time.time < end && keeper.CurrentState != GoalkeeperController.State.Idle;) yield return Roll(recorder, 0.05f);
                yield return Roll(recorder, 0.9f);
            }
            recorder.Finish();
        }
    }
}
