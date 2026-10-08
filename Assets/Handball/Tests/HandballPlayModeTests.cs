using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Handball.Tests
{
    /// <summary>
    /// End-to-end checks of the handball scene: throwing (including through XRI grab/release),
    /// goal detection, the keeper's dive/ragdoll/get-up cycle, scoring feedback and difficulty balance.
    /// </summary>
    public class HandballPlayModeTests
    {
        const string ScenePath = "Assets/Handball/Scenes/Handball.unity";

        HandballGame m_Game;
        GoalkeeperController m_Keeper;
        HandballBall[] m_Balls;
        XROrigin m_Rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // No OpenXR runtime on the test machine: XR init errors are expected and irrelevant here.
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(ScenePath);
#endif
            yield return null;
            yield return new WaitForSeconds(0.3f);
            m_Game = HandballGame.Instance;
            m_Keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            m_Balls = Object.FindObjectsByType<HandballBall>(FindObjectsSortMode.InstanceID);
            m_Rig = Object.FindFirstObjectByType<XROrigin>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------ helpers

        Vector3 HandPosition(bool right = true) => m_Rig.transform.TransformPoint(new Vector3(right ? 0.25f : -0.25f, 1.45f, 0.3f));

        HandballBall FreeBall() => m_Balls.First(b => b.IsOnRack);

        Shot Shoot(Vector2 goalLocalTarget, float speed = 12.5f, bool rightHand = true)
        {
            var ball = FreeBall();
            var from = HandPosition(rightHand);
            var target = m_Game.goal.TransformPoint(new Vector3(goalLocalTarget.x, goalLocalTarget.y, 0f));
            var v = Ballistics.SolveLaunch(from, target, speed, -Physics.gravity.y);
            ball.Launch(from, v);
            return ball.CurrentShot;
        }

        IEnumerator WaitResolved(Shot shot, float timeout = 6f)
        {
            float t = 0f;
            while (shot.result == ShotResult.Pending && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator WaitKeeperState(GoalkeeperController.State state, float timeout)
        {
            float t = 0f;
            while (m_Keeper.CurrentState != state && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        void MakeKeeperDeterministic()
        {
            m_Keeper.wrongGuessChance = 0f;
            m_Keeper.perceptionError = 0f;
            m_Keeper.reactionJitter = 0f;
            m_Keeper.celebrateChance = 0f;
        }

        static void Capture(string name, Vector3 pos, Vector3 target, float fov = 50f)
        {
            Directory.CreateDirectory("Logs/shots");
            var go = new GameObject("Test Capture Camera");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
            cam.fieldOfView = fov;
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"Logs/shots/{name}.png", tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(tex);
            Object.Destroy(go);
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator Scene_HasEverythingWiredUp()
        {
            Assert.IsNotNull(m_Game, "HandballGame missing");
            Assert.IsNotNull(m_Game.goal, "Goal missing");
            Assert.IsNotNull(m_Game.scoreboard, "Scoreboard missing");
            Assert.IsNotNull(m_Game.sfx, "SfxPlayer missing");
            Assert.IsNotNull(m_Keeper, "Keeper missing");
            Assert.AreEqual(6, m_Balls.Length, "Expected 6 balls");
            Assert.IsTrue(m_Balls.All(b => b.IsOnRack), "All balls start on the racks");
            Assert.AreEqual(2, m_Rig.GetComponentsInChildren<HandballThrowAssist>(true).Length, "Throw assist on both controllers");
            Assert.AreEqual(GoalkeeperController.State.Idle, m_Keeper.CurrentState);
            Assert.Less(Vector3.Distance(m_Keeper.transform.position, m_Keeper.HomePosition), 0.05f, "Keeper starts at home");
            foreach (var cue in System.Enum.GetValues(typeof(SfxPlayer.Cue)).Cast<SfxPlayer.Cue>())
                Assert.IsTrue(m_Game.sfx.Has(cue), "Missing sound for " + cue);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Balls_FloatOnRackUntilGrabbed()
        {
            var positions = m_Balls.Select(b => b.transform.position).ToArray();
            yield return new WaitForSeconds(1.5f);
            for (int i = 0; i < m_Balls.Length; i++)
                Assert.Less(Vector3.Distance(positions[i], m_Balls[i].transform.position), 0.02f, "Ball drifted off the rack");
        }

        [UnityTest]
        public IEnumerator Throw_IntoCornerAwayFromKeeper_ScoresGoal()
        {
            MakeKeeperDeterministic();
            m_Keeper.enabled = false; // keeper frozen in the middle
            var shot = Shoot(new Vector2(1.25f, 1.6f));
            Assert.IsNotNull(shot, "Throw was not registered as a shot");
            yield return WaitResolved(shot);
            Assert.AreEqual(ShotResult.Goal, shot.result);
            Assert.AreEqual(1, m_Game.Goals);
            Assert.AreEqual("1", m_Game.scoreboard.goalsValue.text, "Scoreboard shows the goal");
            Assert.IsTrue(m_Game.scoreboard.popup.gameObject.activeSelf, "GOAL popup shown");
            Assert.Greater(m_Game.goalConfetti.Sum(c => c.GetComponent<ParticleSystem>().particleCount), 50, "Confetti burst");
            var playing = m_Game.sfx.GetComponentsInChildren<AudioSource>().Where(s => s.isPlaying && s.clip).Select(s => s.clip.name).ToList();
            CollectionAssert.Contains(playing, "jingles_STEEL02", "Goal jingle playing");
            yield return new WaitForSeconds(0.4f);
            Capture("test_goal_confetti", new Vector3(1.5f, 1.6f, 3.5f), new Vector3(0f, 1.6f, 7f));
        }

        [UnityTest]
        public IEnumerator Throw_StraightAtKeeper_IsSaved()
        {
            MakeKeeperDeterministic();
            var shot = Shoot(new Vector2(0.0f, 1.15f));
            yield return WaitResolved(shot);
            Assert.AreEqual(ShotResult.Save, shot.result, "A shot straight at Unity-chan's chest should be saved");
            Assert.AreEqual(1, m_Game.Saves);
            Assert.AreEqual("1", m_Game.scoreboard.savesValue.text);
        }

        [UnityTest]
        public IEnumerator Throw_WideOfGoal_IsMiss_AndKeeperDoesNotDive()
        {
            MakeKeeperDeterministic();
            var shot = Shoot(new Vector2(3.5f, 1.0f));
            yield return WaitResolved(shot);
            Assert.AreEqual(ShotResult.Miss, shot.result);
            Assert.AreNotEqual(GoalkeeperController.State.Down, m_Keeper.CurrentState);
            Assert.AreEqual(0, m_Game.Goals);
        }

        [UnityTest]
        public IEnumerator Keeper_DivesRagdollsRecoversAndReturns()
        {
            MakeKeeperDeterministic();
            var shot = Shoot(new Vector2(-1.2f, 0.5f));
            yield return WaitKeeperState(GoalkeeperController.State.Diving, 0.6f);
            Assert.AreEqual(GoalkeeperController.State.Diving, m_Keeper.CurrentState, "Keeper should dive at a low corner shot");
            yield return new WaitForSeconds(0.25f);
            Capture("test_dive_mid", new Vector3(0f, 1.3f, 3.2f), new Vector3(0f, 0.9f, 6.5f));
            yield return WaitKeeperState(GoalkeeperController.State.Down, 1.5f);
            Assert.AreEqual(GoalkeeperController.State.Down, m_Keeper.CurrentState, "Keeper should go limp after the dive");
            yield return new WaitForSeconds(1.2f);
            Capture("test_dive_down", new Vector3(0f, 1.3f, 3.2f), new Vector3(0f, 0.4f, 6.5f));
            var hips = m_Keeper.ragdoll.Hips.position;
            Assert.Less(hips.y, 0.6f, "Keeper should be lying on the ground");
            Assert.Greater(Mathf.Abs(hips.x - m_Keeper.HomePosition.x), 0.25f, "Keeper should have moved sideways");
            yield return WaitKeeperState(GoalkeeperController.State.GettingUp, 4f);
            Assert.AreEqual(GoalkeeperController.State.GettingUp, m_Keeper.CurrentState);
            yield return new WaitForSeconds(0.45f);
            Capture("test_getup_mid", new Vector3(0f, 1.3f, 3.2f), new Vector3(0f, 0.8f, 6.5f));
            yield return WaitKeeperState(GoalkeeperController.State.Idle, 4f);
            Assert.AreEqual(GoalkeeperController.State.Idle, m_Keeper.CurrentState, "Keeper should be back in goal");
            Assert.Less(Vector3.Distance(m_Keeper.transform.position, m_Keeper.HomePosition), 0.06f);
            var head = m_Keeper.animator.GetBoneTransform(HumanBodyBones.Head).position;
            Assert.Greater(head.y, 1.1f, "Keeper should be standing upright again");
        }

        [UnityTest]
        public IEnumerator Keeper_IsHelplessWhileDown_SecondBallScores()
        {
            MakeKeeperDeterministic();
            var first = Shoot(new Vector2(-1.3f, 0.4f));
            yield return WaitKeeperState(GoalkeeperController.State.Down, 2f);
            Assert.AreEqual(GoalkeeperController.State.Down, m_Keeper.CurrentState);
            yield return new WaitForSeconds(0.3f);
            var second = Shoot(new Vector2(1.0f, 1.2f), 12.5f, false);
            yield return WaitResolved(second);
            Assert.AreEqual(ShotResult.Goal, second.result, "While Unity-chan is down the open side should be a goal");
        }

        [UnityTest]
        public IEnumerator Ball_ReturnsToRackAfterComingToRest()
        {
            var ball = FreeBall();
            ball.Launch(HandPosition(), new Vector3(0.5f, 2f, 3f));
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(ball.IsOnRack);
            float t = 0f;
            while (!ball.IsOnRack && t < 8f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(ball.IsOnRack, "Ball should respawn on its rack");
            yield return new WaitForSeconds(0.5f);
            Assert.Less(Vector3.Distance(ball.transform.position, ball.rack.SlotPosition(ball.slot)), 0.03f);
            Assert.AreEqual(Vector3.one * 0.19f, ball.transform.localScale, "Pop-in finished at full size");
        }

        [UnityTest]
        public IEnumerator XRGrabAndRelease_ThrowsTowardsAimPoint()
        {
            MakeKeeperDeterministic();
            m_Keeper.enabled = false;
            var manager = Object.FindFirstObjectByType<XRInteractionManager>();
            // Controllers are hidden by the input modality manager until a device is tracked; force the right one on.
            var assist = m_Rig.GetComponentsInChildren<HandballThrowAssist>(true).First(a => a.name.Contains("Right"));
            assist.gameObject.SetActive(true);
            yield return null;
            var interactor = assist.GetComponentInChildren<NearFarInteractor>(true);
            Assert.IsNotNull(interactor, "Right controller has a Near-Far interactor");
            interactor.gameObject.SetActive(true);
            assist.mode = HandballThrowAssist.Mode.PointAndThrow;
            assist.pointThrowSpread = 0f;
            var aim = new GameObject("Aim").transform;
            var target = m_Game.goal.TransformPoint(new Vector3(-1.2f, 1.5f, 0f));
            aim.position = m_Rig.Camera.transform.position;
            aim.rotation = Quaternion.LookRotation(target - aim.position);
            assist.aimSource = aim;

            // The front-most slot, so the flight path does not clip the other floating balls.
            var ball = m_Balls.First(b => b.rack && b.rack.name.Contains("Right") && b.slot == 0);
            manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)ball.Grab);
            yield return null;
            Assert.IsTrue(ball.IsHeld || !ball.IsOnRack, "Ball should have been grabbed");
            if (ball.Grab.isSelected) manager.SelectExit((IXRSelectInteractor)interactor, (IXRSelectInteractable)ball.Grab);
            Debug.Log($"XRI TEST released at {ball.transform.position} target {target}");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsNotNull(ball.CurrentShot, "Release should register a throw");
            var shot = ball.CurrentShot;
            var flat = new Vector3(shot.velocity.x, 0f, shot.velocity.z);
            Assert.Greater(flat.magnitude, 10f, "Point-and-throw speed");
            Debug.Log($"XRI TEST shot origin {shot.origin} velocity {shot.velocity}");
            for (int i = 0; i < 40 && shot.result == ShotResult.Pending; i++)
            {
                Debug.Log($"XRI TEST t={i * 0.05f:F2} ball {ball.transform.position} v {ball.Rigidbody.linearVelocity}");
                yield return new WaitForSeconds(0.05f);
            }
            yield return WaitResolved(shot);
            Assert.AreEqual(ShotResult.Goal, shot.result, "Aimed throw into the empty corner should score");
            Object.Destroy(aim.gameObject);
        }

        [UnityTest]
        public IEnumerator PhysicalThrowAssist_KeepsRealVelocityAndOnlyNudges()
        {
            var assist = m_Rig.GetComponentsInChildren<HandballThrowAssist>(true).First();
            assist.mode = HandballThrowAssist.Mode.Physical;
            var source = HandPosition();
            var raw = (m_Game.GoalCenter - source).normalized * 12f + Vector3.right * 1.5f;
            var assisted = assist.GetAssistedVelocity(source, raw, 9.81f);
            Assert.AreEqual(raw.magnitude * assist.velocityScale, assisted.magnitude, 0.01f, "Speed is only scaled");
            Assert.LessOrEqual(Vector3.Angle(raw, assisted), assist.maxAssistAngle + 0.01f, "Direction bent by at most maxAssistAngle");
            var away = Vector3.back * 10f;
            Assert.AreEqual(0f, Vector3.Angle(away, assist.GetAssistedVelocity(source, away, 9.81f)), 0.01f, "Throws away from goal are untouched");
            yield return null;
        }

        [UnityTest, Explicit]
        public IEnumerator Debug_CaptureStandingBlock()
        {
            MakeKeeperDeterministic();
            var shot = Shoot(new Vector2(0.28f, 1.05f));
            for (int i = 0; i < 14; i++)
            {
                yield return new WaitForSeconds(0.05f);
                Capture($"block_{i:00}", new Vector3(2.2f, 1.3f, 4.5f), new Vector3(0f, 1.0f, 6.4f), 40f);
            }
            yield return WaitResolved(shot);
            Debug.Log("BLOCK RESULT " + shot.result);
        }

        /// <summary>
        /// Difficulty check: random shots spread over the whole goal mouth from the 7 m line,
        /// keeper reset before each. Logs the goal rate per zone; the overall rate must be "fair".
        /// </summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Balance_RandomShotsAreNeitherTooEasyNorTooHard()
        {
            Random.InitState(1234);
            const int shots = 60;
            int goals = 0, saves = 0, misses = 0;
            var zones = new int[3, 2]; // [left/centre/right, goal/save]
            var log = new StringBuilder();
            Time.timeScale = 2f;
            for (int i = 0; i < shots; i++)
            {
                m_Keeper.ResetToHome();
                foreach (var b in m_Balls) b.PlaceOnRack(false);
                yield return new WaitForSeconds(0.3f);
                var target = new Vector2(Random.Range(-1.4f, 1.4f), Random.Range(0.15f, 1.85f));
                var shot = Shoot(target, Random.Range(11f, 14f), i % 2 == 0);
                yield return WaitResolved(shot);
                int zone = target.x < -0.5f ? 0 : (target.x > 0.5f ? 2 : 1);
                if (shot.result == ShotResult.Goal) { goals++; zones[zone, 0]++; }
                else if (shot.result == ShotResult.Save) { saves++; zones[zone, 1]++; }
                else misses++;
                log.AppendLine($"shot {i,2} target=({target.x,5:F2},{target.y,4:F2}) -> {shot.result}");
            }
            Time.timeScale = 1f;
            float rate = goals / (float)shots;
            log.Insert(0, $"BALANCE goals={goals} saves={saves} misses={misses} rate={rate:P0} | left {zones[0, 0]}/{zones[0, 1]} centre {zones[1, 0]}/{zones[1, 1]} right {zones[2, 0]}/{zones[2, 1]} (goal/save)\n");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/balance.txt", log.ToString());
            Debug.Log(log.ToString());
            Assert.That(rate, Is.InRange(0.35f, 0.65f), "Goal rate for random on-target shots should be balanced");
            Assert.Greater(zones[1, 1], zones[1, 0], "Central shots should mostly be saved");
        }
    }
}
