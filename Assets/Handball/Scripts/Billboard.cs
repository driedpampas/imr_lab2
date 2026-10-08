using UnityEngine;

namespace Handball
{
    /// <summary>Keeps a world-space label facing the player camera (yaw only).</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            var dir = transform.position - cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
