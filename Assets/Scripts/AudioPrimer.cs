using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Warms up an AudioSource as early as possible (Awake) by silently
    /// Play()-ing then immediately Pause()-ing it, rather than letting the
    /// FIRST real playback of that clip - later, e.g. TentacleController.
    /// OnHeroGrowStart -> AudioSource.Play(), wired directly in the
    /// Inspector - pay a one-time "cold start" cost. Matches a real-device
    /// report exactly: the hero's own sound cue landed noticeably late ONLY
    /// on the very first reveal of a session, while a later Restart()
    /// (replaying the SAME already-warmed AudioSource) was perfectly in
    /// sync - the textbook signature of a one-time engine/browser audio
    /// warm-up cost, not a scripted timing bug (WebGL's underlying Web Audio
    /// API, and/or Unity's own internal voice/channel allocation, can take a
    /// real, audible moment the FIRST time ANY sound plays in a session -
    /// completely separate from whichever AudioSource/AudioClip happens to
    /// trigger it first).
    ///
    /// MUST live on a GameObject that is ALREADY ACTIVE from scene load -
    /// checked directly against HandoffToInstantTracking.ContentWrapper's
    /// own hierarchy (in UCI-RE-AR.unity, ContentParent - holding the real,
    /// intended-to-play AudioSource - is a CHILD of ContentWrapper, which
    /// HandoffToInstantTracking.Awake() deactivates immediately and only
    /// reactivates in RevealContent(), the same moment (or a hair before)
    /// the hero actually grows). A Play() call on an inactive GameObject's
    /// AudioSource does nothing at all - so the real AudioSource can never
    /// be pre-warmed while sitting there; this component needs to live
    /// somewhere else entirely, active well before tracking ever locks.
    /// ARShareController is kept outside ContentWrapper for exactly this
    /// same reason (see its own doc comment) - put this alongside it, or on
    /// any other always-active GameObject.
    ///
    /// Assign the SAME AudioClip that's actually played later onto a
    /// SEPARATE AudioSource here (muted, so the warm-up is never audible
    /// even though it technically "plays" for an instant) - warming this
    /// clip via a different AudioSource instance still pays almost all of
    /// the same real one-time cost (clip decode + engine/browser audio
    /// pipeline spin-up), leaving the REAL AudioSource's own first Play()
    /// call - later, once ContentParent finally activates - effectively
    /// instant. The real AudioSource/OnHeroGrowStart wiring under
    /// ContentParent is completely untouched by this - it doesn't need to
    /// know this component exists at all.
    ///
    /// Play() then Pause() (NOT Stop()) is deliberate: Pause() keeps the
    /// underlying voice/channel allocated (silently sitting ready), where
    /// Stop() would tear that allocation back down and lose the benefit.
    /// </summary>
    public class AudioPrimer : MonoBehaviour
    {
        [Tooltip("Each of these gets a silent Play()+Pause() warm-up in Awake() - assign a SEPARATE AudioSource here (NOT the real one under ContentParent), with the SAME AudioClip(s), Muted so the warm-up itself is never audible. Leave Play On Awake OFF on these - this script drives them directly.")]
        public AudioSource[] SourcesToPrime;

        private void Awake()
        {
            if (SourcesToPrime == null) return;
            foreach (var source in SourcesToPrime)
            {
                if (source == null || source.clip == null) continue;
                source.Play();
                source.Pause();
                source.time = 0f;
            }
        }
    }
}
