using UnityEngine;

namespace Handball
{
    /// <summary>Floating slots the balls hover in until they are grabbed; balls return here after each throw.</summary>
    public class BallRack : MonoBehaviour
    {
        public Transform[] slots;
        public ParticleSystem respawnPuff;

        public Vector3 SlotPosition(int index)
        {
            if (slots == null || slots.Length == 0) return transform.position;
            return slots[Mathf.Clamp(index, 0, slots.Length - 1)].position;
        }
    }
}
