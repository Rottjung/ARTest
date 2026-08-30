using UnityEngine;

namespace ARReveal
{
    /// <summary>Test bootstrap only - triggers Grow() a moment after Play, so the
    /// full unfurl->idle cycle is visible immediately without any burst-sequence
    /// controller wired up yet.</summary>
    [RequireComponent(typeof(TentacleController))]
    public class TentacleGrowOnStart : MonoBehaviour
    {
        public float DelaySeconds = 0.5f;

        private void Start()
        {
            Invoke(nameof(Trigger), DelaySeconds);
        }

        private void Trigger()
        {
            GetComponent<TentacleController>().Grow();
        }
    }
}
