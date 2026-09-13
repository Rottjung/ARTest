using System.Collections;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// One burst location: any subset of a tentacle, a wall-hole decal, and a debris
    /// ring - wire up whichever are present at that spot. Delay is seconds after the
    /// sequence starts before this point fires, for staggering several across the
    /// facade instead of everything going off in lockstep.
    ///
    /// Debris/Smoke/Rubble only need an explicit reference here if they AREN'T
    /// already a child of Hole - same convention HandoffToInstantTracking's own
    /// Pairs uses (see ResolveChild), so content ported between the two (e.g. via
    /// the ARReveal/UCI-RE content-porting tools) keeps working without rewiring:
    /// every real burst point in this project nests its Debris/Smoke/Rubble under
    /// its own Hole, so leaving these blank is the normal case.
    /// </summary>
    [System.Serializable]
    public class BurstPoint
    {
        public string Name = "Burst Point";
        public TentacleController Tentacle;
        public WallHoleEffect Hole;
        [Tooltip("Only needed if this isn't already a child of Hole above - if it is (the usual setup), leave this blank and it's found automatically.")]
        public DebrisRing Debris;
        [Tooltip("Only needed if this isn't already a child of Hole above - if it is (the usual setup), leave this blank and it's found automatically.")]
        public SmokePuff Smoke;
        [Tooltip("Only needed if this isn't already a child of Hole above - if it is (the usual setup), leave this blank and it's found automatically.")]
        public FallingRubble Rubble;
        [Tooltip("Seconds after the sequence starts before this point fires.")]
        public float Delay = 0f;
    }

    /// <summary>
    /// Fires a list of burst points with per-point delays, so several tentacles can
    /// punch through the facade in a staggered wave instead of wiring each one up by
    /// hand. At each point, the hole, tentacle, smoke and falling rubble start
    /// together and the static debris ring starts a moment later (once the hole is
    /// most of the way open), so it reads as material being pushed out by the
    /// tentacle rather than announcing the break - same pacing
    /// HandoffToInstantTracking.TriggerPairAfterDelay uses for its own Pairs.
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
                var debris = ResolveChild(point.Debris, point.Hole);
                if (debris != null) debris.Hide();
                var smoke = ResolveChild(point.Smoke, point.Hole);
                if (smoke != null) smoke.Hide();
                var rubble = ResolveChild(point.Rubble, point.Hole);
                if (rubble != null) rubble.Hide();
            }
        }

        private IEnumerator FireAfterDelay(BurstPoint point)
        {
            if (point.Delay > 0f) yield return new WaitForSeconds(point.Delay);

            var debris = ResolveChild(point.Debris, point.Hole);
            var smoke = ResolveChild(point.Smoke, point.Hole);
            var rubble = ResolveChild(point.Rubble, point.Hole);

            if (point.Hole != null) point.Hole.Open();
            if (point.Tentacle != null) point.Tentacle.Grow();
            if (smoke != null) smoke.Open();
            if (rubble != null) rubble.Open();

            if (debris != null)
            {
                float debrisDelay = point.Hole != null ? point.Hole.OpenDuration * 0.8f : 0.3f;
                yield return new WaitForSeconds(debrisDelay);
                debris.Open();
            }
        }

        /// <summary>Same convention as HandoffToInstantTracking.ResolveChild - Debris/Smoke/Rubble are usually authored as children of their hole, so they don't need their own explicit field.</summary>
        private static T ResolveChild<T>(T explicitRef, WallHoleEffect hole) where T : Component
        {
            if (explicitRef != null) return explicitRef;
            return hole != null ? hole.GetComponentInChildren<T>(true) : null;
        }
    }
}
