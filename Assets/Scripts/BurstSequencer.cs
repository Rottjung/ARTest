using System.Collections;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// One burst location: any subset of a tentacle, a wall-hole decal, and a debris
    /// ring - wire up whichever are present at that spot. Delay is seconds after the
    /// sequence starts before this point fires, for staggering several across the
    /// facade instead of everything going off in lockstep.
    /// </summary>
    [System.Serializable]
    public class BurstPoint
    {
        public string Name = "Burst Point";
        public TentacleController Tentacle;
        public WallHoleEffect Hole;
        public DebrisRing Debris;
        [Tooltip("Seconds after the sequence starts before this point fires.")]
        public float Delay = 0f;
    }

    /// <summary>
    /// Fires a list of burst points with per-point delays, so several tentacles can
    /// punch through the facade in a staggered wave instead of wiring each one up by
    /// hand. At each point, the hole and tentacle start together and the debris ring
    /// starts a moment later (once the hole is most of the way open), so it reads as
    /// material being pushed out by the tentacle rather than announcing the break.
    /// </summary>
    public class BurstSequencer : MonoBehaviour
    {
        public BurstPoint[] Points;

        [Tooltip("Auto-fire the whole sequence shortly after Start(), for standalone testing.")]
        public bool PlayOnStart = false;
        public float PlayOnStartDelay = 0.5f;

        private void Start()
        {
            if (PlayOnStart) Invoke(nameof(PlaySequence), PlayOnStartDelay);
        }

        [ContextMenu("Play Sequence")]
        public void PlaySequence()
        {
            if (Points == null) return;
            foreach (var point in Points)
                StartCoroutine(FireAfterDelay(point));
        }

        /// <summary>Resets every wired point back to hidden/closed.</summary>
        [ContextMenu("Reset All")]
        public void ResetAll()
        {
            if (Points == null) return;
            StopAllCoroutines();
            foreach (var point in Points)
            {
                if (point.Hole != null) point.Hole.Hide();
                if (point.Tentacle != null) point.Tentacle.Hide();
                if (point.Debris != null) point.Debris.Hide();
            }
        }

        private IEnumerator FireAfterDelay(BurstPoint point)
        {
            if (point.Delay > 0f) yield return new WaitForSeconds(point.Delay);

            if (point.Hole != null) point.Hole.Open();
            if (point.Tentacle != null) point.Tentacle.Grow();

            if (point.Debris != null)
            {
                float debrisDelay = point.Hole != null ? point.Hole.OpenDuration * 0.8f : 0.3f;
                yield return new WaitForSeconds(debrisDelay);
                point.Debris.Open();
            }
        }
    }
}
