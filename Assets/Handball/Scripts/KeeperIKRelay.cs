using UnityEngine;

namespace Handball
{
    /// <summary>
    /// Unity only calls OnAnimatorIK on components that live next to the Animator, so this
    /// forwards the callback from Unity-chan's model to the keeper controller on the root.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class KeeperIKRelay : MonoBehaviour
    {
        public GoalkeeperController keeper;

        void OnAnimatorIK(int layerIndex)
        {
            if (keeper) keeper.ApplyIK(layerIndex);
        }
    }
}
