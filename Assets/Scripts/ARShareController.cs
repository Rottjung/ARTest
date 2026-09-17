using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// Builds the client-facing UI overlay procedurally (Canvas + Image + Button
    /// components added via code, same drop-in philosophy as CameraSlimeOverlay/
    /// ProceduralClouds - no manual Canvas/Button setup needed in the scene, and
    /// none of this touches the .unity scene file at all) - this is still a
    /// completely standard Unity Canvas with real Button components underneath,
    /// exactly per convention, just constructed at runtime instead of hand-placed
    /// in the Editor. In practice the hand-tuned Share.prefab (picked up via
    /// AttachToExistingUI) is what's actually used - the procedural BuildUI()
    /// path below is a best-effort fallback, kept roughly in sync but not
    /// exercised day to day.
    ///
    /// CURRENT SCREEN FLOW (rewritten per a direct, wholesale UI restructure -
    /// see git history around "Restructure UI flow" for the previous version
    /// if any of this needs to be reverted):
    ///
    ///  - Page0_Calibration: shown as soon as the Zappar camera feed is
    ///    actually live (ZapparCamera.CameraSourceInitialized), for as long
    ///    as content hasn't ACTUALLY spawned yet (!HasRevealedContent - see
    ///    that property's own doc for why HasContentSpawned, not
    ///    HasHandedOff, is the right signal to gate on). A single static
    ///    background image (whatever's authored on it directly) plus a
    ///    looping loading ring (EnsureSpinner) - the dynamic text/viewfinder
    ///    frame an earlier version also had here are gone for good ("we
    ///    don't change the calibration text anymore, we now just have an
    ///    image"), but the spinner itself was brought back afterward per a
    ///    later direct request ("i need the spinner back"). Hides INSTANTLY
    ///    (no fade of its own) the moment content reveals - the fade job
    ///    moved to Page1_Ready below.
    ///  - Page1_Ready: shown the INSTANT content reveals, held for
    ///    ReadyDisplaySeconds, then eased out over ReadyFadeOutSeconds (a
    ///    CanvasGroup fade) - per direct request, this is exactly the same
    ///    hold-then-fade mechanism Page0 used to run internally for its own
    ///    "BEREIT!" confirmation, just moved onto this separate page/image
    ///    ("when we normally set the Bereit text we now show Page1_Ready...
    ///    same fade out as we have now"). Once it fades out, NOTHING is
    ///    shown until Page2_CallToAction appears (Page2DelaySeconds later) -
    ///    per direct request, the whole take-a-picture-during-the-experience
    ///    feature (an earlier Page1_Experience, with its own Record/Teilen/
    ///    Rescan buttons) is removed entirely.
    ///  - Page2_CallToAction: logo + "Heute: dieses Kino... die ganze Welt"
    ///    two-line text + a single ShareWin button (replacing an earlier
    ///    Restart+Foto pair, per direct request - Restart/replay-the-burst
    ///    is no longer reachable from this UI, though the underlying
    ///    Restart() method is left in place in case it's wanted back).
    ///    Tapping ShareWin calls Foto() (kept as the method name - its ROLE
    ///    is unchanged, just the button/label is new) which activates
    ///    Page3_Foto.
    ///  - Page3_Foto: the live camera + a single RecordButton - THE actual
    ///    "take the photo" action (RetakePhoto - can be tapped more than
    ///    once to retake). Capturing shows a full-screen framed PREVIEW
    ///    (PhotoPreview/ShowPhotoPreview - see that method's own doc for the
    ///    white-mat framing per direct request, "so its clear it a pic, not
    ///    just frozen screen") and switches EXCLUSIVELY to Page4_Share -
    ///    Page3_Foto itself turns off while the preview/Page4_Share is
    ///    showing (an earlier version left it on underneath, which per
    ///    direct report looked wrong - "Page4 is still showing page3
    ///    behind it"; all pages are mutually exclusive now, see Update()).
    ///  - Page4_Share: TeilenButton (shares via the existing native-share
    ///    plumbing - see Teilen's own doc comment, nothing about
    ///    ShareLastPhoto/ARReveal_ShareImage changed) and RetakeButton
    ///    (discards the preview and goes back to Page3_Foto - see
    ///    DiscardPreview, shared with PhotoPreview's own tap-to-dismiss).
    ///    Both hide Page4_Share/the preview afterward (ClearPreviewState).
    ///
    /// WARNING POPUP (WarningPopup, per direct request): overrides
    /// whichever OTHER page is currently showing - same "highest priority"
    /// precedent an even earlier "Distance Alert" screen set (see git
    /// history around "Remove the Distance Alert" if that's ever needed) -
    /// for either of two reasons (see UpdateWarningState/UpdateMotionPeak/
    /// UpdateTooClose). Only checked once content has actually revealed
    /// (Page1_Ready is on screen) - per direct request ("wait with the
    /// warnings until after bereit"), an earlier version deliberately
    /// checked this during the calibration/QR-scan phase too; reversed
    /// since it's a worse fit with Page1_Ready's own "BEREIT!" moment now.
    ///  - TOO FAST: the AR camera's own frame-to-frame linear/angular speed
    ///    spiked (MotionWarningSpeedThreshold/MotionWarningAngularThreshold)
    ///    - turns on the "Moving" image.
    ///  - TOO CLOSE: the viewer is within actual tentacle-attack range - the
    ///    SAME mechanism an earlier Distance Alert screen used
    ///    (TentacleController.IsCameraWithinAttackRange - see its own doc
    ///    comment) - turns on the "Distance" image instead. Wins over the
    ///    motion warning if both are somehow true at once - an actual
    ///    proximity/attack danger reads as more urgent than a scanning tip.
    /// Per direct request, WarningPopup shows one of two IMAGES now
    /// (Moving/Distance), not a background + dynamic text label like an
    /// earlier version - see UpdateWarningState.
    ///
    /// NO PER-BUTTON SPRITES ANYWHERE ANYMORE, per direct request ("all
    /// button do not use sprites anymore, we use transparent button on
    /// top of the design"). Every page's actual look is one combined
    /// background image baked directly into that page's GameObject in
    /// the hand-tuned Share.prefab (a real Image + sprite, e.g.
    /// Page2_CallToAction's own Image component); every button
    /// (ShareWin/RecordButton/TeilenButton/RetakeButton) is just an
    /// invisible Image (alpha 0) + Button sitting on top of that art as a
    /// hit-target - confirmed directly in the prefab's own YAML. Earlier
    /// versions of this class had a whole "Sprites" field block
    /// (CalibrateSprite, Page1ReadySprite, LogoSprite, Page2TextTop/
    /// BottomSprite, *ButtonSprite, plus a generated-red-circle fallback
    /// for RecordButton) for the procedural BuildUI() fallback to assign
    /// per-element art with - all removed as dead weight once the real
    /// design stopped working that way; BuildPage0-4 below now just
    /// build the same transparent-placeholder-background-plus-
    /// transparent-hit-target shape the real prefab uses, with no sprite
    /// loading of any kind.
    ///
    /// SHARING BYPASSES ZAPPAR'S OWN UI ENTIRELY, per direct request ("skip
    /// zappars ui and go native os"). An earlier version used Zappar's WebGL
    /// Save & Share package (com.zappar.sns, ZSaveNShare) - convenient, but
    /// it opens ITS OWN custom overlay (a "ZapparSnapshotContainer" div with
    /// Save/Share/Close buttons Zappar's own script renders, NOT a native OS
    /// dialog), and on-device testing found its Share button unreliable
    /// (silently inert on some devices, "Permission denied" from
    /// navigator.share() on others - confirmed by reading the actual
    /// minified zappar-sharing.min.js: Save is a plain `&lt;a download&gt;`, only
    /// Share attempts navigator.share(), wrapped in try/catch that logs but
    /// never surfaces the failure to the user). Now this project captures
    /// the photo itself (CapturePhotoBytes - same ReadPixels/EncodeToJPG
    /// technique, just our own code) and hands the raw bytes straight to a
    /// small custom plugin (Plugins/WebGL/ARReveal_Share.jslib,
    /// ShareLastPhoto/ARReveal_ShareImage) that calls navigator.share()
    /// DIRECTLY - the true native OS share sheet, a real system UI, appears
    /// with no custom overlay from either Zappar or this project in between.
    /// Falls back to a plain download in that same plugin if the browser/
    /// device has no file-sharing support at all, so tapping never silently
    /// does nothing. Whether navigator.share() actually succeeds still
    /// depends on the browser/device's own Web Share API support (not
    /// something any code can force) - notably, an in-app browser (e.g.
    /// Instagram/TikTok's own webview, plausible for a QR-driven promo) may
    /// restrict or lack it entirely, same as any other website would hit.
    ///
    /// CAPTURE FEEDBACK: the flash (PlayCaptureFlash) fires AFTER capture
    /// completes, not before/during - firing it first (an earlier version's
    /// bug) meant the captured screenshot included the flash overlay
    /// ITSELF, coming out an almost-all-white photo.
    /// </summary>
    public class ARShareController : MonoBehaviour
    {
        [Tooltip("QR+SLAM scenes (UCI-RE-AR) - auto-found in the scene if left blank.")]
        public HandoffToInstantTracking Handoff;

        [Tooltip("Direct image-tracking scenes with no QR/SLAM at all (UCI-RE, the safety backup) - auto-found in the scene if left blank. Only ever consulted when Handoff above is null, so this has zero effect on a scene where Handoff is assigned.")]
        public RevealOnTrackingFound DirectTrackingReveal;

        [Tooltip("Optional - auto-found in the scene if left blank. Currently unused by this class - see git history if the 'Preparing...' cold-cache distinction this used to drive is ever wanted back.")]
        public TargetPreloader Preloader;

        [Header("Optional: after hand-tuning a prebuilt UI (see ARReveal/Build Share UI In Scene), drag the resulting page groups/buttons in here directly. Leave blank to auto-find them by name instead (see AttachToExistingUI).")]
        public GameObject Page0GroupOverride;
        [Tooltip("The calibration screen's loading ring (see EnsureSpinner) - overrides the by-name lookup (Page0_Calibration/Spinner). Leave blank to have one auto-created under Page0_Calibration if none exists yet.")]
        public Image SpinnerImageOverride;
        [Tooltip("The brief 'BEREIT'-equivalent confirmation shown the instant content reveals (see Update()) - overrides the by-name lookup (Page1_Ready).")]
        public GameObject Page1ReadyGroupOverride;
        public GameObject Page2GroupOverride;
        public GameObject Page3GroupOverride;
        [Tooltip("Shown alongside PhotoPreview once a photo's been captured on Page3_Foto (see Update()) - overrides the by-name lookup (Page4_Share).")]
        public GameObject Page4GroupOverride;
        public Button ShareWinButtonOverride;
        public Button RecordButtonOverride;
        public Button TeilenButtonOverride;
        [Tooltip("Page4_Share's own button - discards the preview and goes back to Page3_Foto (see DiscardPreview). Overrides the by-name lookup (Page4_Share/RetakeButton).")]
        public Button RetakeButtonOverride;
        [Tooltip("Overrides whichever other page is currently showing (see Update()'s own WarningPopup section) - either a motion warning (scanning/tilting too fast) or a proximity warning (within tentacle attack range). Overrides the by-name lookup (WarningPopup).")]
        public GameObject WarningPopupGroupOverride;
        [Tooltip("Shown for the TOO FAST warning - overrides the by-name lookup (WarningPopup/Moving).")]
        public GameObject WarningMovingImageOverride;
        [Tooltip("Shown for the TOO CLOSE warning - overrides the by-name lookup (WarningPopup/Distance).")]
        public GameObject WarningDistanceImageOverride;

        [Header("Timing / thresholds")]
        [Tooltip("Seconds after tracking locks before the Call To Action screen (logo + ShareWin button) appears - requested directly as 30 seconds.")]
        public float Page2DelaySeconds = 30f;

        [Tooltip("Seconds to hold Page1_Ready's confirmation once content has actually spawned, before starting to fade it out (see ReadyFadeOutSeconds) - per direct request: without a generous hold here, a viewer staring at their phone could miss the reveal entirely and look up at the real building instead.")]
        public float ReadyDisplaySeconds = 3f;

        [Tooltip("Seconds to fade Page1_Ready out (CanvasGroup alpha 1->0) once ReadyDisplaySeconds has elapsed, instead of an instant SetActive(false) - a smoother, more noticeable 'ok, it's done now' transition.")]
        public float ReadyFadeOutSeconds = 0.6f;

        [Tooltip("How many full loops per second the calibration screen's loading ring fills at (see LateUpdate/EnsureSpinner).")]
        public float SpinnerLoopsPerSecond = 0.8f;

        [Header("Warning popup thresholds - untested, tune on a real device")]
        [Tooltip("Camera linear speed (meters/second) above which the TOO FAST warning shows - see UpdateMotionPeak.")]
        public float MotionWarningSpeedThreshold = 3f;
        [Tooltip("Camera angular speed (degrees/second) above which the TOO FAST warning shows - see UpdateMotionPeak.")]
        public float MotionWarningAngularThreshold = 90f;
        [Tooltip("Time window (seconds) speed/angular speed are measured over - NOT frame-to-frame (see UpdateMotionPeak's own doc comment for why comparing adjacent frames triggered constantly even holding the phone still: dividing normal per-frame tracking jitter by a tiny Time.deltaTime blows it up into an apparent spike). Smaller = more responsive but noisier; larger = smoother but slower to react.")]
        public float MotionSampleWindowSeconds = 0.15f;
        [Tooltip("Seconds the motion warning keeps showing after the last detected peak, so a single brief spike doesn't just flash on and off before anyone can actually read it.")]
        public float MotionWarningHoldSeconds = 1f;

        [Tooltip("Above CameraSlimeOverlay's 500, so this UI always draws on top of the slime splat.")]
        public int SortingOrder = 600;

        private enum UiScreen { None, CallToAction, SharePrompt }
        private UiScreen _screen = UiScreen.None;

        private enum WarningReason { None, TooFast, TooClose }
        private WarningReason _activeWarning = WarningReason.None;

        private GameObject _page0Group;
        private GameObject _page1ReadyGroup;
        private GameObject _page2Group;
        private GameObject _page3Group;
        private GameObject _page4Group;
        private GameObject _warningPopupGroup;
        private GameObject _warningMovingImage;
        private GameObject _warningDistanceImage;

        /// <summary>Drives Page1_Ready's fade-out (see ReadyFadeOutSeconds) - added whether it was built fresh (BuildPage1Ready) or picked up from a hand-tuned prefab (AttachToExistingUI).</summary>
        private CanvasGroup _page1ReadyCanvasGroup;

        /// <summary>The calibration screen's loading ring (see EnsureSpinner/LateUpdate) - restored per direct request after being dropped in the wholesale UI restructure.</summary>
        private Image _spinnerImage;

        private GameObject _photoPreviewGroup;
        private Image _photoPreviewImage;
        private ZapparCamera _zapparCamera;

        /// <summary>
        /// True from the moment a capture completes (ShowPhotoPreview) until
        /// something explicitly clears it (Foto()'s fresh-entry reset,
        /// ClearPreviewState via Teilen/DiscardPreview, or the full
        /// "!HasRevealedContent" reset) - the single source of truth for
        /// "should PhotoPreview/Page4_Share be showing right now" AND for
        /// pausing the Page2DelaySeconds Call To Action countdown while
        /// it's true (see Update()'s own "PAUSE" comment). Deliberately
        /// independent of _photoPreviewGroup's own active state, which
        /// Update() also forces false whenever a warning is overriding
        /// everything - this flag is what lets the preview correctly
        /// reappear once the warning clears.
        /// </summary>
        private bool _hasCapturedPreview;

        // --- Warning popup state (see UpdateWarningState/UpdateMotionPeak/UpdateTooClose) ---
        private Vector3 _lastCamPos;
        private Quaternion _lastCamRot = Quaternion.identity;
        private float _lastCamSampleTime;
        private bool _hasLastCamSample;
        private float _motionWarningHoldUntil = -1f;
        private int _lastKnownLockCount = -1;
        private Transform _tentacleCacheSource;
        private TentacleController[] _tentacles;

        /// <summary>The whole UI's own Canvas - toggled off (not the GameObject) for the duration of the actual capture in RetakePhotoRoutine, so none of our own buttons/text end up baked into the saved photo.</summary>
        private Canvas _canvas;

        private float _handoffStartTime = -1f;

        /// <summary>Time.time when content first actually spawned (HasRevealedContent flipped true) - drives Page1_Ready's hold (see ReadyDisplaySeconds). -1 while not yet spawned.</summary>
        private float _contentSpawnedAt = -1f;

        private Image _flashImage;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            if (Handoff == null) Handoff = FindFirstObjectByType<HandoffToInstantTracking>();
            if (DirectTrackingReveal == null) DirectTrackingReveal = FindFirstObjectByType<RevealOnTrackingFound>();
            if (Preloader == null) Preloader = FindFirstObjectByType<TargetPreloader>();
            _zapparCamera = ZapparCamera.Instance != null ? ZapparCamera.Instance : FindFirstObjectByType<ZapparCamera>();

            // If a hand-tuned "ARShareCanvas" hierarchy already exists as a
            // child (built via the ARReveal/Build Share UI In Scene menu
            // command, then saved into the scene or as a prefab), use it
            // as-is instead of rebuilding procedurally - see
            // AttachToExistingUI's own doc comment.
            var existingCanvas = transform.Find("ARShareCanvas");
            if (existingCanvas != null)
                AttachToExistingUI(existingCanvas);
            else
                BuildUI();
        }

        /// <summary>
        /// Picks up a hand-tuned hierarchy already saved in the scene/prefab
        /// (built via ARReveal/Build Share UI In Scene, then laid out by hand)
        /// instead of building one from scratch - layout can be freely edited
        /// (RectTransform positions/sizes) as long as the page groups and
        /// button GameObjects keep their original names, since this looks
        /// them up by name/path rather than assuming BuildUI()'s exact
        /// structure. Button onClick listeners are always re-wired here in
        /// code (never relying on serialized persistent calls) because
        /// AddListener-based listeners added by the procedural builder are
        /// never serialized into a prefab in the first place.
        /// </summary>
        private void AttachToExistingUI(Transform canvasRoot)
        {
            _canvas = canvasRoot.GetComponent<Canvas>();
            _page0Group = Page0GroupOverride != null ? Page0GroupOverride : FindChild(canvasRoot, "Page0_Calibration");
            EnsureSpinner(_page0Group);
            _page1ReadyGroup = Page1ReadyGroupOverride != null ? Page1ReadyGroupOverride : FindChild(canvasRoot, "Page1_Ready");
            _page1ReadyCanvasGroup = EnsureCanvasGroup(_page1ReadyGroup);
            _page2Group = Page2GroupOverride != null ? Page2GroupOverride : FindChild(canvasRoot, "Page2_CallToAction");
            _page3Group = Page3GroupOverride != null ? Page3GroupOverride : FindChild(canvasRoot, "Page3_Foto");
            _page4Group = Page4GroupOverride != null ? Page4GroupOverride : FindChild(canvasRoot, "Page4_Share");
            _warningPopupGroup = WarningPopupGroupOverride != null ? WarningPopupGroupOverride : FindChild(canvasRoot, "WarningPopup");
            _warningMovingImage = WarningMovingImageOverride != null ? WarningMovingImageOverride : FindChild(canvasRoot, "WarningPopup/Moving");
            _warningDistanceImage = WarningDistanceImageOverride != null ? WarningDistanceImageOverride : FindChild(canvasRoot, "WarningPopup/Distance");

            // On the canvas root, NOT nested under Page3_Foto - so it stays
            // correctly layered (see EnsurePhotoPreview's own doc comment)
            // regardless of which page happens to be active.
            EnsurePhotoPreview(canvasRoot);

            WireButton(ShareWinButtonOverride, canvasRoot, "Page2_CallToAction/ShareWin", Foto);
            WireButton(RecordButtonOverride, canvasRoot, "Page3_Foto/RecordButton", RetakePhoto);
            WireButton(TeilenButtonOverride, canvasRoot, "Page4_Share/TeilenButton", Teilen);
            WireButton(RetakeButtonOverride, canvasRoot, "Page4_Share/RetakeButton", DiscardPreview);

            SetActiveIfNotNull(_page0Group, false);
            SetActiveIfNotNull(_page1ReadyGroup, false);
            SetActiveIfNotNull(_page2Group, false);
            SetActiveIfNotNull(_page3Group, false);
            SetActiveIfNotNull(_page4Group, false);
            SetActiveIfNotNull(_warningPopupGroup, false);
            SetActiveIfNotNull(_warningMovingImage, false);
            SetActiveIfNotNull(_warningDistanceImage, false);

            EnsureFlashOverlay(canvasRoot);
        }

        private static GameObject FindChild(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// Prefers an explicitly-dragged-in Button (ShareWinButtonOverride etc.
        /// - immune to renaming/moving while hand-tuning the layout); falls
        /// back to finding it by name/path under canvasRoot if left blank.
        /// </summary>
        private static void WireButton(Button explicitButton, Transform root, string path, UnityEngine.Events.UnityAction onClick)
        {
            var button = explicitButton != null ? explicitButton : root.Find(path)?.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(onClick);
            else
                Debug.LogWarning("[ARShareController] Could not find button at '" + path + "' under " + root.name + " - either drag it into the matching *ButtonOverride field, or make sure it wasn't renamed/deleted while adjusting the layout.");
        }

        /// <summary>
        /// True once content has ACTUALLY been revealed (the real "content
        /// has spawned" moment - see HandoffToInstantTracking.
        /// HasContentSpawned's own doc comment for why HasHandedOff was the
        /// wrong signal for this: it fires the instant a handoff attempt
        /// merely STARTS, well before anything is actually visible). Works
        /// identically whether the scene uses HandoffToInstantTracking's
        /// QR+SLAM handoff (HasContentSpawned) if Handoff is assigned, or
        /// RevealOnTrackingFound's simpler direct-image-tracking reveal
        /// (HasRevealed) as a fallback. Handoff always wins when assigned.
        /// </summary>
        private bool HasRevealedContent =>
            Handoff != null ? Handoff.HasContentSpawned
            : DirectTrackingReveal != null && DirectTrackingReveal.HasRevealed;

        private void Update()
        {
            // WARNING POPUP - per direct request, overrides whichever OTHER
            // page would otherwise be showing. Computed FIRST, before any
            // page-specific logic below, because the motion warning
            // specifically only starts once Page1_Ready is actually on
            // screen - per direct request ("wait with the warnings until
            // after bereit... so only after Page1 is on the screen we
            // start listening"). An earlier version checked this
            // unconditionally so it could interrupt the calibration/QR-scan
            // phase too - reversed per this direct request, so no
            // motion/proximity warning can appear before content (and
            // Page1_Ready) has actually revealed. UpdateWarningState isn't
            // even called while !HasRevealedContent, so its internal
            // motion-sample baseline naturally starts fresh (_hasLastCamSample
            // is false) the first time it IS called after reveal, rather
            // than carrying over any pre-reveal jitter.
            if (HasRevealedContent)
            {
                UpdateWarningState();
                if (_activeWarning != WarningReason.None)
                {
                    SetActiveIfNotNull(_warningPopupGroup, true);
                    SetActiveIfNotNull(_page0Group, false);
                    SetActiveIfNotNull(_page1ReadyGroup, false);
                    SetActiveIfNotNull(_page2Group, false);
                    SetActiveIfNotNull(_page3Group, false);
                    SetActiveIfNotNull(_page4Group, false);
                    // Forced hidden too (NOT cleared - _hasCapturedPreview itself
                    // is untouched) so a warning can never be visually covered by
                    // an opaque captured-photo preview sitting behind it. Comes
                    // back on its own the moment this branch stops running (see
                    // the per-frame sync below), since that re-applies
                    // _hasCapturedPreview's own unchanged value.
                    SetActiveIfNotNull(_photoPreviewGroup, false);
                    return;
                }
            }
            else if (_activeWarning != WarningReason.None)
            {
                // Defensive only (e.g. a rescan mid-warning) - UpdateWarningState
                // itself never runs while !HasRevealedContent, so this can't
                // normally happen, but don't leave a stale warning "on" if it did.
                _activeWarning = WarningReason.None;
                SetActiveIfNotNull(_warningMovingImage, false);
                SetActiveIfNotNull(_warningDistanceImage, false);
            }
            SetActiveIfNotNull(_warningPopupGroup, false);

            if (!HasRevealedContent)
            {
                // Shown once camera access is actually live, until content
                // has ACTUALLY spawned.
                bool cameraReady = _zapparCamera != null && _zapparCamera.CameraSourceInitialized;
                SetActiveIfNotNull(_page0Group, cameraReady);

                SetActiveIfNotNull(_page1ReadyGroup, false);
                if (_page1ReadyCanvasGroup != null) _page1ReadyCanvasGroup.alpha = 1f; // reset any fade-out for next time
                SetActiveIfNotNull(_page2Group, false);
                SetActiveIfNotNull(_page3Group, false);
                SetActiveIfNotNull(_page4Group, false);
                _hasCapturedPreview = false;
                SetActiveIfNotNull(_photoPreviewGroup, false);
                _handoffStartTime = -1f;
                _screen = UiScreen.None;
                _contentSpawnedAt = -1f;
                return;
            }

            // Page0 hides INSTANTLY the moment content reveals - it's just a
            // static image now, nothing on it worth holding/fading (that
            // job belongs to Page1_Ready below).
            SetActiveIfNotNull(_page0Group, false);

            // Page1_Ready shows the INSTANT content reveals, holds for
            // ReadyDisplaySeconds, then fades over ReadyFadeOutSeconds -
            // per direct request, this is exactly the hold-then-fade
            // mechanism an earlier version ran on Page0 itself for its own
            // "BEREIT!" confirmation, just moved onto this separate page.
            if (_contentSpawnedAt < 0f)
            {
                _contentSpawnedAt = Time.time;
                if (_page1ReadyCanvasGroup != null) _page1ReadyCanvasGroup.alpha = 1f;
            }

            float readyElapsed = Time.time - _contentSpawnedAt;
            bool page1ReadyVisible;
            if (readyElapsed < ReadyDisplaySeconds)
            {
                page1ReadyVisible = true;
                if (_page1ReadyCanvasGroup != null) _page1ReadyCanvasGroup.alpha = 1f;
            }
            else if (readyElapsed < ReadyDisplaySeconds + ReadyFadeOutSeconds)
            {
                page1ReadyVisible = true;
                if (_page1ReadyCanvasGroup != null)
                    _page1ReadyCanvasGroup.alpha = Mathf.Lerp(1f, 0f, (readyElapsed - ReadyDisplaySeconds) / ReadyFadeOutSeconds);
            }
            else
            {
                page1ReadyVisible = false;
            }
            SetActiveIfNotNull(_page1ReadyGroup, page1ReadyVisible);

            if (_handoffStartTime < 0f) _handoffStartTime = Time.time;

            // PAUSE the Call To Action countdown while a captured photo is
            // being shown, per direct request ("we don't want the cta to
            // interrupt"). Pushing _handoffStartTime forward by exactly
            // this frame's elapsed time freezes Time.time - _handoffStartTime
            // at whatever it was the instant the capture happened - the
            // simplest way to pause a countdown expressed as "time since X"
            // without needing a separate accumulated-pause-duration
            // variable. Resumes exactly where it left off the moment
            // _hasCapturedPreview goes false again (Teilen/DiscardPreview).
            if (_hasCapturedPreview)
                _handoffStartTime += Time.deltaTime;
            else if (_screen == UiScreen.None && Time.time - _handoffStartTime >= Page2DelaySeconds)
                _screen = UiScreen.CallToAction;

            // Mutually exclusive - only ever one page on screen at a time.
            // Page4_Share (and the preview) share the exact same trigger -
            // both turn on together the instant RetakePhoto captures
            // something (see ShowPhotoPreview), and both turn off together
            // via ClearPreviewState (Teilen/DiscardPreview). Page3_Foto is
            // explicitly turned OFF while that's showing (per direct
            // report, "Page4 is still showing page3 behind it" - an
            // earlier version left Page3_Foto active underneath so
            // RecordButton stayed reachable to retake directly, but that's
            // not the wanted look) - retaking now goes through Page4's own
            // RetakeButton (DiscardPreview), which re-reveals Page3_Foto by
            // clearing _hasCapturedPreview.
            SetActiveIfNotNull(_page2Group, _screen == UiScreen.CallToAction);
            SetActiveIfNotNull(_page3Group, _screen == UiScreen.SharePrompt && !_hasCapturedPreview);
            SetActiveIfNotNull(_page4Group, _hasCapturedPreview);
            SetActiveIfNotNull(_photoPreviewGroup, _hasCapturedPreview);
        }

        /// <summary>
        /// Decides which (if either) of the two WarningPopup triggers is
        /// currently active, and toggles the Moving/Distance images ONLY on
        /// a change (not every frame) - see UpdateMotionPeak/UpdateTooClose
        /// for each trigger's own detection logic. If both happen to be
        /// true at the same instant, TooClose wins - an actual proximity/
        /// attack danger reads as more urgent than a scanning-technique tip.
        /// </summary>
        private void UpdateWarningState()
        {
            bool tooFast = UpdateMotionPeak();
            bool tooClose = UpdateTooClose();

            WarningReason reason = tooClose ? WarningReason.TooClose : tooFast ? WarningReason.TooFast : WarningReason.None;
            if (reason == _activeWarning) return;
            _activeWarning = reason;

            SetActiveIfNotNull(_warningMovingImage, reason == WarningReason.TooFast);
            SetActiveIfNotNull(_warningDistanceImage, reason == WarningReason.TooClose);
        }

        /// <summary>
        /// "High peaks in movement or rotation" per direct request - tracks
        /// the AR camera's own linear/angular speed and flags a peak once
        /// either exceeds its own threshold.
        ///
        /// Measured over a small time WINDOW (MotionSampleWindowSeconds),
        /// NOT frame-to-frame - an earlier version compared every single
        /// adjacent frame (delta / Time.deltaTime), which real-device
        /// testing found triggered constantly even holding the phone
        /// almost still. That's a real miscalculation, not just wrong
        /// threshold numbers: at a typical 60-120Hz frame rate,
        /// Time.deltaTime is tiny (0.008-0.017s), so completely ordinary
        /// tracking noise (a couple millimetres of position jitter, a
        /// fraction of a degree of rotation jitter - expected from ANY
        /// real-world 6DoF tracker regardless of how still the phone is
        /// held) gets divided by that tiny number and blows up into an
        /// apparent multi-m/s or many-degrees/s spike. Comparing against a
        /// sample from ~0.15s ago instead fixes this properly rather than
        /// just papering over it with higher thresholds: random jitter
        /// doesn't consistently push in one direction, so it mostly
        /// cancels out over that window, while genuine fast movement still
        /// shows a large net displacement across the same window.
        ///
        /// Deliberately checked unconditionally (not just before
        /// HasRevealedContent) - most relevant during the initial QR scan/
        /// tilt transition, but a real, sudden fast spin post-reveal is
        /// just as legitimate a "please slow down" moment.
        ///
        /// A single noisy window shouldn't make the popup flash on and off
        /// faster than anyone could read it, so a detected peak extends
        /// _motionWarningHoldUntil by MotionWarningHoldSeconds - the warning
        /// stays considered active until that hold expires, not just for
        /// the exact window that actually exceeded the threshold.
        /// </summary>
        private bool UpdateMotionPeak()
        {
            if (_zapparCamera == null) return false;

            // Discard the baseline (rather than measure across it) whenever
            // a lock/re-lock just completed - real-device testing found the
            // warning firing right after the very first lock finishes, even
            // holding the phone still. Root cause: HandoffToInstantTracking
            // re-seeds the SLAM anchor at that exact moment
            // (SeedAnchorPosition/PlaceTrackerAnchor), and the camera's OWN
            // rendered transform is computed FROM that anchor every frame
            // (see HandoffToInstantTracking's own "EARLY ANCHOR PLACEMENT"
            // doc comment) - so the anchor reference changing under it can
            // produce a real, one-time jump in the camera's reported pose
            // that has nothing to do with the phone actually moving. No
            // amount of time-windowing fixes that (a real discontinuity
            // averaged over any window still reads as a huge net
            // displacement) - the only correct fix is to never measure
            // across the moment it happens at all, and just resume
            // measuring fresh from the post-jump pose.
            if (Handoff != null && Handoff.TotalLocksCompleted != _lastKnownLockCount)
            {
                _lastKnownLockCount = Handoff.TotalLocksCompleted;
                _hasLastCamSample = false;
            }

            Transform camTransform = _zapparCamera.transform;
            Vector3 pos = camTransform.position;
            Quaternion rot = camTransform.rotation;

            if (!_hasLastCamSample)
            {
                _lastCamPos = pos;
                _lastCamRot = rot;
                _lastCamSampleTime = Time.time;
                _hasLastCamSample = true;
                return Time.time < _motionWarningHoldUntil;
            }

            float elapsed = Time.time - _lastCamSampleTime;
            if (elapsed >= MotionSampleWindowSeconds)
            {
                float speed = Vector3.Distance(pos, _lastCamPos) / elapsed;
                float angularSpeed = Quaternion.Angle(rot, _lastCamRot) / elapsed;
                if (speed > MotionWarningSpeedThreshold || angularSpeed > MotionWarningAngularThreshold)
                    _motionWarningHoldUntil = Time.time + MotionWarningHoldSeconds;

                _lastCamPos = pos;
                _lastCamRot = rot;
                _lastCamSampleTime = Time.time;
            }

            return Time.time < _motionWarningHoldUntil;
        }

        /// <summary>
        /// "Same as distance attack" per direct request - re-uses the exact
        /// mechanism an earlier Distance Alert screen relied on:
        /// TentacleController.IsCameraWithinAttackRange, the SAME per-
        /// tentacle, tip-based Snap Distance check ReachByDistance's own
        /// attack triggering uses (see that property's own doc comment for
        /// why this is more meaningful than a flat radius around some single
        /// content origin point). Only meaningful once a real handoff has
        /// happened (ContentWrapper's tentacles are hidden/inert before
        /// then) - see EnsureTentacleCache.
        /// </summary>
        private bool UpdateTooClose()
        {
            if (Handoff == null || !Handoff.HasHandedOff) return false;
            EnsureTentacleCache();
            if (_tentacles == null) return false;
            foreach (var tentacle in _tentacles)
                if (tentacle != null && tentacle.IsCameraWithinAttackRange) return true;
            return false;
        }

        /// <summary>Lazily caches every TentacleController under ContentWrapper the first time it's needed - the hierarchy is fixed/authored, never rebuilt at runtime, so one scan is enough; re-caches automatically if ContentWrapper is ever reassigned to a different Transform (defensive, not expected to happen in practice).</summary>
        private void EnsureTentacleCache()
        {
            Transform wrapper = Handoff != null ? Handoff.ContentWrapper : null;
            if (wrapper == null) { _tentacles = null; _tentacleCacheSource = null; return; }
            if (_tentacles != null && _tentacleCacheSource == wrapper) return;
            _tentacles = wrapper.GetComponentsInChildren<TentacleController>(true);
            _tentacleCacheSource = wrapper;
        }

        /// <summary>
        /// Animates the calibration screen's loading ring (see EnsureSpinner) -
        /// in LateUpdate (not Update) so it always reads this frame's actual
        /// Page0_Calibration active state, set earlier in Update(), rather
        /// than lagging a frame behind. A radial-fill loading ring
        /// (Image.Type.Filled/Radial360), not a rotated shape - fillAmount
        /// sweeps 0 -> 1 on a repeating sawtooth (Mathf.Repeat), so it fills
        /// up, snaps back to empty, and fills again in an endless loop while
        /// the calibration screen is up. No freeze-at-1 "READY" special case
        /// (an earlier version needed one because Page0 itself used to show
        /// the READY confirmation and stay up briefly afterward) -
        /// Page0_Calibration now hides INSTANTLY the moment content reveals
        /// (see Update()), so there's nothing left to freeze it for.
        /// </summary>
        private void LateUpdate()
        {
            if (_spinnerImage == null || _page0Group == null || !_page0Group.activeInHierarchy) return;
            _spinnerImage.fillAmount = Mathf.Repeat(Time.time * SpinnerLoopsPerSecond, 1f);
        }

        private static void SetActiveIfNotNull(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("ARShareCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            _canvas = canvas;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // First sibling - see EnsurePhotoPreview's own doc comment for
            // why it needs to render BEHIND whichever page ends up above it.
            EnsurePhotoPreview(canvasGo.transform);

            _page0Group = BuildPage0(canvasGo.transform);
            _page1ReadyGroup = BuildPage1Ready(canvasGo.transform);
            _page2Group = BuildPage2(canvasGo.transform);
            _page3Group = BuildPage3(canvasGo.transform);
            _page4Group = BuildPage4(canvasGo.transform);

            _page0Group.SetActive(false);
            _page1ReadyGroup.SetActive(false);
            _page2Group.SetActive(false);
            _page3Group.SetActive(false);
            _page4Group.SetActive(false);

            EnsureFlashOverlay(canvasGo.transform);
        }

        /// <summary>
        /// A full-screen white Image, always kept as the last sibling (so it
        /// renders above whichever page is currently showing) - see
        /// PlayCaptureFlash for why this exists. raycastTarget is false so it
        /// never blocks taps on the buttons underneath it even while fading.
        /// Safe to call repeatedly (e.g. every AttachToExistingUI/BuildUI) -
        /// reuses the existing "CaptureFlash" child instead of duplicating it.
        /// </summary>
        private void EnsureFlashOverlay(Transform canvasRoot)
        {
            var existing = canvasRoot.Find("CaptureFlash");
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("CaptureFlash");
                go.transform.SetParent(canvasRoot, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = false;
            }
            go.transform.SetAsLastSibling();
            _flashImage = go.GetComponent<Image>();
        }

        /// <summary>
        /// Brief white flash - the only "yes, a photo was just taken"
        /// feedback a web page can give INSTANTLY, independent of how long
        /// TakeSnapshot's WaitForEndOfFrame + native save/share dialog take
        /// to actually complete/appear. Fixes the reported "press the record
        /// button and nothing seems to happen" gap.
        /// </summary>
        private void PlayCaptureFlash()
        {
            if (_flashImage == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(CaptureFlashRoutine());
        }

        private IEnumerator CaptureFlashRoutine()
        {
            const float fadeSeconds = 0.35f;
            _flashImage.color = new Color(1f, 1f, 1f, 0.85f);
            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                _flashImage.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.85f, 0f, t / fadeSeconds));
                yield return null;
            }
            _flashImage.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// A full-screen "PhotoPreview" showing the most recently captured
        /// photo, FRAMED per direct request ("frame the preview a bit so its
        /// clear it a pic, not just frozen screen") - a dark dimming
        /// background (now fully covered in practice, see below, but kept in
        /// case that ever changes), a full-screen white "PhotoFrame" mat,
        /// and the actual "PhotoImage" inset within that by a border
        /// thickness, so it reads as a distinct framed photograph rather
        /// than the screen just having frozen. PhotoFrame itself is
        /// full-screen (edge-to-edge) per a later direct request ("make the
        /// previewframe size so the white border around it touches the
        /// edges of the screen") - an earlier version used a fixed centered
        /// size instead. All three layers have raycastTarget on, but only
        /// the OUTER "PhotoPreview" GameObject has the actual Button
        /// (calling DiscardPreview) - Unity's EventSystem walks UP the
        /// hierarchy from whichever layer was actually tapped to find it, so
        /// tapping the dimmed background, the frame, or the photo itself all
        /// correctly dismiss it (per direct request, "so we can click the
        /// preview away"), without needing three separate Button components.
        ///
        /// Lives directly on the CANVAS ROOT (a sibling of Page0-4, not
        /// nested inside Page3_Foto) so it stays visible/correctly layered
        /// regardless of which page happens to be active. Kept as the FIRST
        /// sibling on the root (opposite of CaptureFlash's LAST-sibling
        /// convention below) so whichever page IS active draws its own
        /// buttons/text on top of it, rather than this covering them.
        ///
        /// Starts hidden (nothing captured yet); ShowPhotoPreview() is what
        /// actually populates PhotoImage after a capture. Safe to call
        /// repeatedly (e.g. every AttachToExistingUI/BuildUI) - reuses an
        /// existing "PhotoPreview" child instead of duplicating it.
        /// </summary>
        private void EnsurePhotoPreview(Transform canvasRoot)
        {
            var existing = canvasRoot.Find("PhotoPreview");
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("PhotoPreview");
                go.transform.SetParent(canvasRoot, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var bg = go.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.92f);

                var frameGo = new GameObject("PhotoFrame");
                frameGo.transform.SetParent(go.transform, false);
                var frameRt = frameGo.AddComponent<RectTransform>();
                frameRt.anchorMin = Vector2.zero;
                frameRt.anchorMax = Vector2.one;
                frameRt.offsetMin = Vector2.zero;
                frameRt.offsetMax = Vector2.zero;
                frameGo.AddComponent<Image>().color = Color.white;

                var photoGo = new GameObject("PhotoImage");
                photoGo.transform.SetParent(frameGo.transform, false);
                var photoRt = photoGo.AddComponent<RectTransform>();
                photoRt.anchorMin = Vector2.zero;
                photoRt.anchorMax = Vector2.one;
                const float borderThickness = 24f;
                photoRt.offsetMin = new Vector2(borderThickness, borderThickness);
                photoRt.offsetMax = new Vector2(-borderThickness, -borderThickness);
                var photoImg = photoGo.AddComponent<Image>();
                photoImg.preserveAspect = true;
            }

            // Every layer needs raycastTarget on so a tap anywhere on the
            // dimmed background/frame/photo all hit-test successfully and
            // bubble up to the single Button below.
            var bgImage = go.GetComponent<Image>();
            if (bgImage != null) bgImage.raycastTarget = true;
            var frameTransform = go.transform.Find("PhotoFrame");
            var frameImage = frameTransform != null ? frameTransform.GetComponent<Image>() : null;
            if (frameImage != null) frameImage.raycastTarget = true;
            var photoTransform = frameTransform != null ? frameTransform.Find("PhotoImage") : null;
            var photoImage = photoTransform != null ? photoTransform.GetComponent<Image>() : null;
            if (photoImage != null) photoImage.raycastTarget = true;

            var tapButton = go.GetComponent<Button>();
            if (tapButton == null) tapButton = go.AddComponent<Button>();
            tapButton.transition = Selectable.Transition.None;
            tapButton.onClick.RemoveAllListeners();
            tapButton.onClick.AddListener(DiscardPreview);

            go.transform.SetAsFirstSibling();
            go.SetActive(false);
            _photoPreviewGroup = go;
            _photoPreviewImage = photoImage;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Entry point for the ARReveal/UI/Build Share UI In Scene menu command
        /// (see Assets/Editor/BuildShareUIInScene.cs) - destroys any previously-built
        /// "ARShareCanvas" child and rebuilds it fresh as real, persisted
        /// GameObjects (instead of the runtime-only ones Awake() creates),
        /// so it can be hand-tuned in the Scene view and saved as a prefab.
        /// Never called at runtime - Editor-only, hence the #if.
        /// </summary>
        public void EditorRebuildUI()
        {
            var existing = transform.Find("ARShareCanvas");
            if (existing != null)
                UnityEditor.Undo.DestroyObjectImmediate(existing.gameObject);

            BuildUI();
        }

        /// <summary>
        /// Surgical companion to EditorRebuildUI() - adds JUST the
        /// calibration screen to an already-hand-tuned "ARShareCanvas" if
        /// it's missing, without touching or rebuilding anything else
        /// already placed/tuned. Safe to call repeatedly - no-ops if
        /// Page0_Calibration already exists. See
        /// Assets/Editor/AddCalibrationScreenToSharePrefab.cs for the menu
        /// command that calls this on a prefab asset directly.
        /// </summary>
        public void EditorAddCalibrationScreenIfMissing()
        {
            var canvasRoot = transform.Find("ARShareCanvas");
            if (canvasRoot == null)
            {
                Debug.LogError("[ARShareController] No 'ARShareCanvas' child found - build the UI first (ARReveal/UI/Build Share UI In Scene).");
                return;
            }
            if (canvasRoot.Find("Page0_Calibration") != null)
            {
                Debug.Log("[ARShareController] Page0_Calibration already exists under " + canvasRoot.name + " - nothing to add.");
                return;
            }

            _page0Group = BuildPage0(canvasRoot);
            _page0Group.transform.SetSiblingIndex(0);
            _page0Group.SetActive(false);

            EnsureFlashOverlay(canvasRoot);

            Debug.Log("[ARShareController] Added Page0_Calibration under " + canvasRoot.name + ".");
        }
#endif

        // --- CALIBRATION ---------------------------------------------------------
        /// <summary>
        /// A single static background image (per direct request, "we don't
        /// change the calibration text anymore, we now just have an
        /// image" - the viewfinder frame/instruction text/ready-text swap
        /// an earlier version ran on this page are gone for good; that
        /// whole "content is ready" moment moved to the separate
        /// Page1_Ready, see Update()) plus a loading ring (EnsureSpinner) -
        /// restored per direct request ("i need the spinner back") after
        /// being dropped along with the rest of this page's old dynamic
        /// elements in the same restructure.
        /// </summary>
        private GameObject BuildPage0(Transform parent)
        {
            var group = new GameObject("Page0_Calibration");
            group.transform.SetParent(parent, false);
            AddPageBackground(group);
            EnsureSpinner(group);
            return group;
        }

        /// <summary>See Update()'s own doc comment for exactly when this shows/fades - the CanvasGroup here is what ReadyFadeOutSeconds animates.</summary>
        private GameObject BuildPage1Ready(Transform parent)
        {
            var group = new GameObject("Page1_Ready");
            group.transform.SetParent(parent, false);
            AddPageBackground(group);
            _page1ReadyCanvasGroup = EnsureCanvasGroup(group);
            return group;
        }

        /// <summary>Gets or adds a CanvasGroup - used to fade Page1_Ready out (see ReadyFadeOutSeconds) whether it was just built fresh here or picked up from a hand-tuned prefab.</summary>
        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            if (go == null) return null;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        /// <summary>
        /// The calibration screen's loading ring - restored per direct
        /// request ("i need the spinner back") after being dropped in the
        /// wholesale UI restructure (see git history around "Restructure UI
        /// flow") when Page0_Calibration became a single static image. No
        /// design asset exists for this (see CreateRingSprite's own doc
        /// comment) - a plain generated ring, animated via
        /// Image.Type.Filled/Radial360 rather than a rotated shape (see
        /// LateUpdate), exactly "a round loading bar that fills in a loop"
        /// per the original request this restores. Self-installing (like
        /// EnsurePhotoPreview/EnsureFlashOverlay) so it comes back
        /// automatically under Page0_Calibration whether that's the
        /// hand-tuned Share prefab or the procedural BuildUI() fallback -
        /// reuses an existing "Spinner" child (or SpinnerImageOverride) if
        /// one's already there, and only generates+assigns the ring sprite
        /// if it doesn't already have one, so a hand-placed custom spinner
        /// image is never clobbered.
        /// </summary>
        private void EnsureSpinner(GameObject page0Group)
        {
            if (page0Group == null) return;

            Transform existing = SpinnerImageOverride != null ? SpinnerImageOverride.transform : page0Group.transform.Find("Spinner");
            GameObject go;
            Image image;
            if (existing != null)
            {
                go = existing.gameObject;
                image = go.GetComponent<Image>();
                if (image == null) image = go.AddComponent<Image>();
            }
            else
            {
                go = new GameObject("Spinner");
                go.transform.SetParent(page0Group.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(140f, 140f);
                rt.anchoredPosition = new Vector2(0f, -700f);
                image = go.AddComponent<Image>();
            }

            if (image.sprite == null) image.sprite = CreateRingSprite(Color.white, 128, 0.16f);
            SetUpAsLoadingRing(image);
            _spinnerImage = image;
        }

        /// <summary>
        /// Generates a plain, fully-closed ring (donut) - the base shape for
        /// the calibration loading ring. No design asset exists for this -
        /// the actual "loading bar" animation isn't baked into this texture
        /// at all - see SetUpAsLoadingRing, which uses Unity's own
        /// Image.Type.Filled/Radial360 to progressively reveal this same
        /// closed ring around its circumference.
        /// </summary>
        private static Sprite CreateRingSprite(Color color, int size, float thicknessFraction)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
            float outerRadius = size / 2f - 2f;
            float innerRadius = outerRadius * (1f - thicknessFraction);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    bool onRing = dist >= innerRadius && dist <= outerRadius;
                    pixels[y * size + x] = onRing ? color : new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Configures a plain closed-ring Image as a radial-fill "loading
        /// bar that fills in a loop" (per the original request) - Unity's
        /// built-in Image.Type.Filled/Radial360 progressively reveals the
        /// ring starting from the top, clockwise, as fillAmount goes 0 -> 1
        /// (see LateUpdate for the actual looping animation).
        /// </summary>
        private static void SetUpAsLoadingRing(Image image)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
        }

        // --- CALL TO ACTION ----------------------------------------------------
        private GameObject BuildPage2(Transform parent)
        {
            var group = new GameObject("Page2_CallToAction");
            group.transform.SetParent(parent, false);

            AddPageBackground(group);
            BuildTransparentButton(group.transform, "ShareWin",
                new Vector2(0f, -600f), new Vector2(320f, 140f), Foto);

            return group;
        }

        // --- FOTO --------------------------------------------------------
        private GameObject BuildPage3(Transform parent)
        {
            var group = new GameObject("Page3_Foto");
            group.transform.SetParent(parent, false);

            AddPageBackground(group);
            BuildTransparentButton(group.transform, "RecordButton",
                new Vector2(0f, -700f), new Vector2(140f, 140f), RetakePhoto);

            return group;
        }

        // --- SHARE --------------------------------------------------------
        private GameObject BuildPage4(Transform parent)
        {
            var group = new GameObject("Page4_Share");
            group.transform.SetParent(parent, false);

            AddPageBackground(group);
            BuildTransparentButton(group.transform, "RetakeButton",
                new Vector2(-150f, -700f), new Vector2(280f, 140f), DiscardPreview);
            BuildTransparentButton(group.transform, "TeilenButton",
                new Vector2(150f, -700f), new Vector2(280f, 140f), Teilen);

            return group;
        }

        /// <summary>
        /// Full-screen, fully transparent placeholder Image added directly
        /// on a page group (not a separate child) - matches how the
        /// hand-tuned Share.prefab itself carries each page's real
        /// background art directly on the page GameObject (e.g.
        /// Page2_CallToAction's own Image component). This procedural
        /// fallback has no real design art to load anymore (see this
        /// class's own top doc comment - no more per-element Sprite
        /// fields), so it just gives the page the right GameObject/
        /// RectTransform shape; drag real art onto it by hand afterward.
        /// </summary>
        private static void AddPageBackground(GameObject group)
        {
            var rt = group.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            group.AddComponent<Image>().color = Color.clear;
        }

        /// <summary>
        /// A plain transparent hit-target - per direct request, real
        /// buttons no longer render their own sprite art at all ("all
        /// button do not use sprites anymore, we use transparent button on
        /// top of the design"); the visible button is whatever the page's
        /// own background art already shows underneath. The Image exists
        /// only so Button/raycast hit-testing has something to test against
        /// - alpha 0, Transition.None so there's no tint flash on tap over
        /// art this component knows nothing about. Matches the hand-tuned
        /// Share.prefab's own buttons exactly.
        /// </summary>
        private static Button BuildTransparentButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>
        /// Replays the tentacle/hole/FX burst in place - NOT reload the
        /// browser page (see HandoffToInstantTracking.RestartRevealSequence()).
        /// Not currently wired to any button (per direct request, the
        /// Call To Action screen's Restart button was replaced by ShareWin -
        /// see this class's own top doc comment) - left in place in case
        /// it's wanted back on some other trigger later.
        /// </summary>
        public void Restart()
        {
            if (Handoff != null) Handoff.RestartRevealSequence();
            else if (DirectTrackingReveal != null) DirectTrackingReveal.RestartRevealSequence();

            _screen = UiScreen.None;
            _handoffStartTime = Time.time;
            SetActiveIfNotNull(_page2Group, false);
            SetActiveIfNotNull(_page3Group, false);
        }

        /// <summary>
        /// Advances from the Call To Action screen to Page3_Foto (kept as
        /// the method name from an earlier "FOTO button" version - its ROLE
        /// is unchanged, just the button/label is now "ShareWin"). Does NOT
        /// capture anything itself - the round record button on Page3_Foto
        /// (RetakePhoto) is the one real "take the photo" action. Hides any
        /// stale preview/Page4_Share from a previous visit, per direct
        /// request - a fresh visit to this screen should always start from
        /// the same predictable state (nothing captured yet).
        /// </summary>
        public void Foto()
        {
            _screen = UiScreen.SharePrompt;
            ClearPreviewState();
        }

        /// <summary>
        /// The round record button on Page3_Foto - THE actual "take the
        /// photo" action. Can be tapped more than once to retake, in case
        /// the first one didn't land right - just re-runs the same routine,
        /// overwriting whatever was captured before.
        ///
        /// Captures and shows a framed PREVIEW (see ShowPhotoPreview) AND
        /// turns on Page4_Share alongside it (see Update()) - leaving
        /// Teilen (Page4_Share) as the actual "now share this" action.
        /// </summary>
        public void RetakePhoto()
        {
            StartCoroutine(RetakePhotoRoutine());
        }

        /// <summary>
        /// The whole Canvas is switched off for the capture itself so none
        /// of our own buttons/text end up baked into the photo - per direct
        /// request, the saved/shared image should be a clean AR view only.
        /// The flash then fires AFTER capture completes (and after the UI
        /// is back on), not before/during - firing it first (an earlier
        /// version's bug) meant a captured screenshot included the flash
        /// overlay ITSELF, coming out an almost-all-white photo. Capturing
        /// on a clean, UI-free frame first, then restoring the UI and
        /// flashing, guarantees neither can ever contaminate what actually
        /// gets shown/shared. Ends by showing the preview (ShowPhotoPreview)
        /// rather than sharing immediately - see RetakePhoto's own doc
        /// comment for why.
        /// </summary>
        private IEnumerator RetakePhotoRoutine()
        {
            if (_canvas != null) _canvas.enabled = false;
            // Let a fully rendered, UI-free frame actually happen before
            // ReadPixels - Canvas.enabled takes effect immediately, but
            // without waiting a frame here, ReadPixels could still read back
            // whatever the GPU had already queued up from the moment the
            // button was tapped.
            yield return new WaitForEndOfFrame();
            CapturePhotoBytes();
            if (_canvas != null) _canvas.enabled = true;
            PlayCaptureFlash();
            ShowPhotoPreview();
        }

        /// <summary>
        /// The most recently captured photo's raw JPEG bytes - kept around
        /// so Teilen can share (or re-share) the same photo without
        /// recapturing.
        /// </summary>
        private byte[] _lastPhotoBytes;

        /// <summary>Whatever Sprite/Texture2D ShowPhotoPreview last created - torn down before making a new one so retaking repeatedly doesn't leak a fresh Texture2D/Sprite pair on every tap.</summary>
        private Texture2D _previewTexture;

        /// <summary>
        /// Populates PhotoPreview's inner PhotoImage (see EnsurePhotoPreview)
        /// with the just-captured JPEG and reveals the whole framed preview
        /// (plus Page4_Share, via Update()'s _hasCapturedPreview sync) -
        /// leaving Teilen as the actual "share this" action (RetakePhoto can
        /// still be tapped again to retake, which simply calls this again
        /// and replaces what's shown).
        /// </summary>
        private void ShowPhotoPreview()
        {
            if (_photoPreviewImage == null || _lastPhotoBytes == null) return;

            if (_previewTexture != null) Destroy(_previewTexture);
            _previewTexture = new Texture2D(2, 2);
            _previewTexture.LoadImage(_lastPhotoBytes);

            if (_photoPreviewImage.sprite != null) Destroy(_photoPreviewImage.sprite);
            _photoPreviewImage.sprite = Sprite.Create(_previewTexture,
                new Rect(0f, 0f, _previewTexture.width, _previewTexture.height), new Vector2(0.5f, 0.5f));

            // The actual "show it" - _hasCapturedPreview is what Update()
            // reads every frame afterward to keep this (and Page4_Share)
            // active, and the CTA countdown paused - see that field's own
            // doc comment; setting the GameObject active here too just
            // avoids a one-frame delay before Update() next runs.
            _hasCapturedPreview = true;
            if (_photoPreviewGroup != null) _photoPreviewGroup.SetActive(true);
        }

        /// <summary>
        /// Hides the preview/Page4_Share and resumes the paused Call To
        /// Action countdown (by clearing _hasCapturedPreview - see that
        /// field's own doc comment) - shared by Foto() (fresh-entry reset),
        /// Teilen() (after sharing), and DiscardPreview() (retake/discard).
        /// Does NOT touch _lastPhotoBytes itself - Teilen still needs it
        /// (ShareLastPhoto runs first); DiscardPreview clears it separately.
        /// </summary>
        private void ClearPreviewState()
        {
            _hasCapturedPreview = false;
            SetActiveIfNotNull(_photoPreviewGroup, false);
            SetActiveIfNotNull(_page4Group, false);
        }

        /// <summary>
        /// Page4_Share's RetakeButton, and PhotoPreview's own tap-to-dismiss
        /// (see EnsurePhotoPreview) - discards the captured photo WITHOUT
        /// sharing it and goes back to Page3_Foto (ClearPreviewState hides
        /// the preview/Page4_Share, revealing Page3_Foto - which stayed
        /// active underneath the whole time - and its own RecordButton
        /// again). Also clears _lastPhotoBytes itself (not just hiding the
        /// preview) so a stray later Teilen tap can't accidentally re-share
        /// a photo the viewer explicitly discarded.
        /// </summary>
        public void DiscardPreview()
        {
            _lastPhotoBytes = null;
            ClearPreviewState();
        }

        /// <summary>
        /// Reads the current frame straight off the screen and encodes it to
        /// JPEG - same technique Zappar's own ZSaveNShare.TakeSnapshot uses
        /// internally, just done directly so this project owns the bytes
        /// and can hand them to its own native-share plugin instead of
        /// Zappar's overlay. Must be called right after a WaitForEndOfFrame
        /// with the Canvas already disabled - see RetakePhotoRoutine.
        /// </summary>
        private void CapturePhotoBytes()
        {
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
            tex.Apply();
            _lastPhotoBytes = tex.EncodeToJPG(85);
            Destroy(tex);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ARReveal_ShareImage(byte[] data, int length);
#endif

        /// <summary>
        /// Hands the most recently captured photo straight to the real OS
        /// native share sheet (Plugins/WebGL/ARReveal_Share.jslib calls
        /// navigator.share() directly) - bypassing Zappar's own
        /// zappar-sharing.js overlay entirely, per direct request. Falls
        /// back to a plain download in that same plugin if this
        /// browser/device has no file-sharing support at all, so this never
        /// silently does nothing.
        /// </summary>
        private void ShareLastPhoto()
        {
            if (_lastPhotoBytes == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            ARReveal_ShareImage(_lastPhotoBytes, _lastPhotoBytes.Length);
#else
            Debug.Log("[ARShareController] Share requested - only works in an actual WebGL build.");
#endif
        }

        /// <summary>
        /// Page4_Share's TeilenButton - THE "share this now" action, opening
        /// the native OS share sheet for whatever's currently shown in the
        /// preview. Nothing about ShareLastPhoto/ARReveal_ShareImage
        /// themselves changed - just where this gets called from as the UI
        /// itself was restructured around it.
        ///
        /// Resumes the experience afterward (ClearPreviewState) - hides the
        /// preview/Page4_Share and un-pauses the Call To Action countdown,
        /// revealing Page3_Foto again, so sharing doesn't leave the viewer
        /// stuck looking at the same captured photo indefinitely.
        /// </summary>
        public void Teilen()
        {
            ShareLastPhoto();
            ClearPreviewState();
        }
    }
}
