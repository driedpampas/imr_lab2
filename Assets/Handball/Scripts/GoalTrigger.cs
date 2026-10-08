using UnityEngine;

namespace Handball
{
    /// <summary>
    /// Trigger volume behind the goal line. In handball the whole ball must cross the line,
    /// so the volume starts one ball radius behind it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalTrigger : MonoBehaviour
    {
        void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var ball = other.attachedRigidbody ? other.attachedRigidbody.GetComponent<HandballBall>() : null;
            if (ball && HandballGame.Instance) HandballGame.Instance.ReportGoalLineCrossed(ball);
        }
    }
}
