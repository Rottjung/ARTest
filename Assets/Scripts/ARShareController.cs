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
    /// in the Editor.
    ///
    /// Two of these screens are built from the final design's own PNGs (every
    /// screen's text is pre-rendered INTO its image asset - no font/TextMeshPro
    /// needed at all, just positioned Image components); the calibration screen
    /// below is the one exception, see its own doc paragraph for why:
    ///
    ///  - EXPERIENCE (Page1_Experience, per direct request): active for the
    ///    whole stretch between the calibration screen's fade-out finishing
    ///    and the Call To Action screen appearing (Page2DelaySeconds later) -
    ///    see Update()'s own "Page1_Experience" comment for the exact window.
    ///    Content/layout is whatever's placed directly in the prefab; this
    ///    class only owns its show/hide timing. The Rescan button typically
    ///    lives as a child of this page (see AttachToExistingUI/Rescan()'s
    ///    own doc comments) since they share the identical visibility window.
    ///  - CALL TO ACTION (shown Page2DelaySeconds after tracking locks): logo +
    ///    "Heute: dieses Kino... die ganze Welt" two-line text (split into
    ///    Page_02_Text_Top.png/Page_02_Text_Bottom.png so the logo can sit
    ///    between them, matching the actual design mockup - the source PNG had
    ///    both lines in one image) + RESTART and FOTO buttons.
    ///  - SHARE PROMPT / "the selfie screen" (shown after tapping FOTO -
    ///    Foto() is PURE NAVIGATION, it does not capture anything itself):
    ///    "share your photo, win tickets" text + FotoMode/VideoMode buttons
    ///    (SelectPhotoMode/SelectVideoMode - decide what the round record
    ///    button captures) + the round record button, the ONE real "take
    ///    the photo" action (RetakePhoto - can be tapped more than once to
    ///    retake/recapture) + TEILEN (share) button. An earlier version had
    ///    FOTO itself capture + immediately open the save/share dialog,
    ///    skipping this screen entirely - corrected per direct feedback
    ///    ("that not the design... foto button make you go to the selfie
    ///    screen... clicking the red button should make a native
    ///    screenshot").
    ///
    ///    RetakePhoto no longer shares immediately either, per a LATER
    ///    direct request ("client wants the video or foto shown first, and
    ///    if we click teilen we go to the native share") - it captures and
    ///    shows a full-screen PREVIEW instead (ShowPhotoPreview/
    ///    PhotoPreview), leaving Teilen as the actual "share this now"
    ///    trigger - Teilen already called ShareLastPhoto before (as a
    ///    secondary "re-share if the dialog was dismissed" convenience),
    ///    now promoted to the primary one. VIDEO capture itself is a
    ///    placeholder for now (see RetakePhoto's own doc comment) - Unity
    ///    WebGL has no built-in video encoder, so it needs new
    ///    MediaRecorder/canvas.captureStream()-based .jslib work, a
    ///    separate, larger piece of work than the mode-picker UI itself.
    ///
    /// A third "Distance Alert" screen (warning the viewer to step back) was
    /// tried and then removed entirely per direct request - see git history
    /// around "Distance Alert" if it's ever wanted back.
    ///
    /// CALIBRATION SCREEN (Page0_Calibration - see BuildCalibrationScreen):
    /// shown as soon as the Zappar camera feed is actually live
    /// (ZapparCamera.CameraSourceInitialized), for as long as content hasn't
    /// ACTUALLY spawned yet (!HasRevealedContent - see that property's own
    /// doc for why HasContentSpawned, not HasHandedOff, is the right signal
    /// to gate on, and for why this works identically whether the scene
    /// uses HandoffToInstantTracking's QR+SLAM handoff or
    /// RevealOnTrackingFound's simpler direct-image-tracking reveal). Shows
    /// a rounded-square viewfinder frame to place the QR in, an
    /// instructional text line ("Preparing..." while TargetPreloader hasn't
    /// finished warming the browser's cache yet, else "Scan the QR to
    /// calibrate" - see TargetPreloader's own doc comment for the real
    /// cold-cache bug this avoids), and a round loading ring (a "loading bar
    /// that fills in a loop" per direct request - see
    /// SetUpAsLoadingRing/LateUpdate) + "CALIBRATING..." label at the
    /// bottom. Once content actually spawns, the ring freezes full and the
    /// label switches to "READY!" - held for ReadyDisplaySeconds, then EASED
    /// OUT over ReadyFadeOutSeconds (a CanvasGroup fade, not an instant
    /// SetActive(false)) rather than just vanishing - both per direct
    /// on-site feedback that the READY confirmation was disappearing too
    /// fast for a viewer to actually register it (a viewer staring at their
    /// phone could otherwise miss the reveal entirely with nothing telling
    /// them to look). Normally shown exactly once per session after that -
    /// this falls straight out of HasContentSpawned/HasRevealed's own
    /// existing semantics on either underlying component (set true on the
    /// very first reveal and never reset back to false on their own,
    /// including a later passive re-scan/re-lock), so a passive re-scan
    /// after that first lock (the QR happening to be glimpsed again)
    /// silently just re-corrects the anchor as it always did, with no UI
    /// interruption at all - "if we scan the AR again just keep what we
    /// have now" per direct request. A DELIBERATE full recalibration is
    /// still available via the bottom-center Rescan button (see
    /// BuildRescanButton/Rescan) - shown once this screen has fully hidden
    /// and neither other page is up, it forces HasContentSpawned/HasRevealed
    /// back to false (HandoffToInstantTracking.RequestRescan()/
    /// RevealOnTrackingFound.RequestRescan()), which puts this exact screen
    /// straight back up and waits for a fresh QR scan through the same
    /// settle/lock pipeline as the very first time - per direct request,
    /// "put the calibrate UI back on and we can rescan and reset with the
    /// same flow as the first time." The frame and loading ring are generated at runtime
    /// (CreateRoundedFrameSprite/CreateRingSprite, same procedural-texture
    /// approach as CreateCircleSprite below) since no design asset exists
    /// for this screen yet; the two text labels use a
    /// plain UnityEngine.UI.Text with Unity's built-in font as a placeholder
    /// for the same reason - EVERY OTHER screen's text in this class is a
    /// pre-rendered PNG per this project's own convention, swap these two
    /// Text components for Image components once real assets exist.
    /// SetCalibrationMessage only ever auto-writes hardcoded wording
    /// ("CALIBRATING...", "Scan the QR to calibrate", "READY!", etc.) to
    /// whichever of InstructionText/CalibratingText has its OWN
    /// InstructionTextOverride/CalibratingTextOverride field left empty -
    /// per direct request, dragging a specific Text object into either
    /// override field means "hand off control of this label's wording
    /// entirely," so it's never touched/overwritten after that.
    ///
    /// The OLD "Selfie" button used to mean "flip to the front camera" - that's
    /// removed entirely per direct request (no camera-switching UI at all
    /// anymore).
    ///
    /// RESTART, per direct request, means replay the tentacle/hole/FX burst in
    /// place - NOT reload the browser page (an earlier version did exactly
    /// that via a same-tab jslib reload; superseded, see Restart()'s own doc
    /// comment and HandoffToInstantTracking.RestartRevealSequence()).
    ///
    /// SHARING BYPASSES ZAPPAR'S OWN UI ENTIRELY, per direct request ("skip
    /// zappars ui and go native os"). An earlier version used Zappar's WebGL
    /// Save & Share package (com.zappar.sns, ZSaveNShare) - convenient, but
    /// it opens ITS OWN custom overlay (a "ZapparSnapshotContainer" div with
    /// Save/Share/Close buttons Zappar's own script renders, NOT a native OS
    /// dialog), and on-device testing found its Share button unreliable
    /// (silently inert on some devices, "Permission denied" from
    /// navigator.share() on others - confirmed by reading the actual
    /// minified zappar-sharing.min.js: Save is a plain `<a download>`, only
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

        [Tooltip("Optional - auto-found in the scene if left blank. While this hasn't finished preloading the .zpt yet, the calibration screen shows a 'Preparing...' message instead of 'Scan the QR' - see TargetPreloader's own doc comment for the real bug this addresses (a cold browser cache losing the race against the QR actually being scanned).")]
        public TargetPreloader Preloader;

        [Header("Sprites - drag the matching PNG from Assets/Images/UI onto each")]
        public Sprite LogoSprite;
        public Sprite Page2TextTopSprite;
        public Sprite Page2TextBottomSprite;
        public Sprite Page3TextSprite;
        public Sprite RestartButtonSprite;
        public Sprite FotoButtonSprite;
        public Sprite TeilenButtonSprite;
        [Tooltip("The round record/retake button on the Share Prompt screen. Falls back to a plain generated red circle if left blank.")]
        public Sprite RecordButtonSprite;
        [Tooltip("Shown on the calibration screen once content is ready (see Update()) - only used by the procedural BuildPage0() path (a fresh build with no existing hand-tuned canvas); the hand-tuned Share.prefab has its own 'Ready' image placed directly, found by name (see AttachToExistingUI). Left blank here just means nothing shows in the Ready image's place for a freshly-built UI - CalibratingText's own 'READY!' wording still works regardless.")]
        public Sprite ReadyImageSprite;
        [Tooltip("The Foto/Video mode-select buttons on the Share Prompt screen (see SelectPhotoMode/SelectVideoMode) - only used by the procedural BuildPage3() path; the hand-tuned Share.prefab has its own FotoMode/VideoMode buttons placed directly, found by name.")]
        public Sprite FotoModeSprite;
        public Sprite VideoModeSprite;

        [Header("Optional: after hand-tuning a prebuilt UI (see ARReveal/Build Share UI In Scene), drag the resulting page groups/buttons in here directly. Leave blank to auto-find them by name instead (see AttachToExistingUI).")]
        public GameObject Page0GroupOverride;
        [Tooltip("Shown once the calibration screen (Page0) has fully finished its fade-out, and hidden again the instant the Call To Action screen (Page2) appears - see Update(). Overrides the by-name lookup (Page1_Experience).")]
        public GameObject Page1GroupOverride;
        public GameObject Page2GroupOverride;
        public GameObject Page3GroupOverride;
        public Button RestartButtonOverride;
        public Button FotoButtonOverride;
        public Button RecordButtonOverride;
        public Button TeilenButtonOverride;
        [Tooltip("The Foto/Video mode-select buttons on the Share Prompt screen (see SelectPhotoMode/SelectVideoMode) - override the by-name lookups (Page3_SharePrompt/FotoMode and .../VideoMode).")]
        public Button FotoModeButtonOverride;
        public Button VideoModeButtonOverride;
        [Tooltip("The bottom-center Rescan button (see BuildRescanButton/Rescan) - drag it in directly if hand-tuning its position/sprite, or leave blank to find it by name (RescanButton) under the canvas root.")]
        public Button RescanButtonOverride;
        [Tooltip("Drag the calibration screen's loading-ring Image component here directly if you're wiring/configuring it by hand - overrides the by-name lookup (Page0_Calibration/Spinner) entirely, so exact naming/nesting doesn't matter.")]
        public Image SpinnerImageOverride;
        [Tooltip("Drag the calibration screen's instructional Text component here directly - overrides the by-name lookup (Page0_Calibration/InstructionText).")]
        public Text InstructionTextOverride;
        [Tooltip("Drag the calibration screen's 'CALIBRATING...'/'READY!' Text component here directly - overrides the by-name lookup (Page0_Calibration/CalibratingText).")]
        public Text CalibratingTextOverride;
        [Tooltip("The QR viewfinder frame on the calibration screen - hidden the instant content is ready (see Update()), alongside InstructionText and the spinner. Overrides the by-name lookup (Page0_Calibration/QRFrame).")]
        public GameObject QrFrameOverride;
        [Tooltip("Shown the instant content is ready, replacing the QR frame/instruction text/spinner (see Update()) - CalibratingText stays visible throughout (just its wording changes to READY!). Overrides the by-name lookup (Page0_Calibration/Ready).")]
        public GameObject ReadyImageOverride;

        [Header("Timing / thresholds")]
        [Tooltip("Seconds after tracking locks before the Call To Action screen (logo + Restart/Foto buttons) appears - requested directly as 30 seconds.")]
        public float Page2DelaySeconds = 30f;

        [Tooltip("How many times per second the calibration loading ring fills up before looping back to empty and starting over.")]
        public float SpinnerLoopsPerSecond = 0.8f;

        [Tooltip("Seconds to hold a 'READY!' confirmation on the calibration screen once content has actually spawned, before starting to fade it out (see ReadyFadeOutSeconds) - per direct request: without a generous hold here, a viewer staring at their phone could miss the reveal entirely and look up at the real building instead. Bumped up from an original 1.5s after on-site feedback that it was disappearing before most viewers actually registered it. Runs in parallel with (doesn't delay) Page2DelaySeconds' own countdown.")]
        public float ReadyDisplaySeconds = 3f;

        [Tooltip("Seconds to fade the calibration screen out (CanvasGroup alpha 1->0) once ReadyDisplaySeconds has elapsed, instead of an instant SetActive(false) - a smoother, more noticeable 'ok, it's done now' transition per the same on-site feedback that prompted raising ReadyDisplaySeconds.")]
        public float ReadyFadeOutSeconds = 0.6f;

        [Tooltip("Above CameraSlimeOverlay's 500, so this UI always draws on top of the slime splat.")]
        public int SortingOrder = 600;

        private enum UiScreen { None, CallToAction, SharePrompt }
        private UiScreen _screen = UiScreen.None;

        private GameObject _page0Group;
        private GameObject _page1Group;
        private GameObject _page2Group;
        private GameObject _page3Group;
        private GameObject _rescanButton;

        /// <summary>Drives the calibration screen's fade-out (see ReadyFadeOutSeconds) - added to _page0Group whether it was built fresh (BuildPage0) or picked up from a hand-tuned prefab (AttachToExistingUI), so the same Update() logic works either way.</summary>
        private CanvasGroup _page0CanvasGroup;

        private Image _spinnerImage;
        private Text _instructionText;
        private Text _calibratingText;
        private GameObject _qrFrame;
        private GameObject _readyImage;
        private Image _fotoModeImage;
        private Image _videoModeImage;
        private Image _photoPreviewImage;
        private ZapparCamera _zapparCamera;

        private enum CaptureMode { Photo, Video }
        private CaptureMode _captureMode = CaptureMode.Photo;

        /// <summary>The whole UI's own Canvas - toggled off (not the GameObject) for the duration of the actual capture in RetakePhotoRoutine, so none of our own buttons/text end up baked into the saved photo.</summary>
        private Canvas _canvas;

        private float _handoffStartTime = -1f;

        /// <summary>Time.time when content first actually spawned (HasRevealedContent flipped true) - drives the "READY!" hold on the calibration screen (see ReadyDisplaySeconds). -1 while not yet spawned.</summary>
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
            _page1Group = Page1GroupOverride != null ? Page1GroupOverride : FindChild(canvasRoot, "Page1_Experience");
            _page2Group = Page2GroupOverride != null ? Page2GroupOverride : FindChild(canvasRoot, "Page2_CallToAction");
            _page3Group = Page3GroupOverride != null ? Page3GroupOverride : FindChild(canvasRoot, "Page3_SharePrompt");
            var spinnerTransform = canvasRoot.Find("Page0_Calibration/Spinner");
            _spinnerImage = SpinnerImageOverride != null ? SpinnerImageOverride
                : spinnerTransform != null ? spinnerTransform.GetComponent<Image>() : null;
            var instructionTransform = canvasRoot.Find("Page0_Calibration/InstructionText");
            _instructionText = InstructionTextOverride != null ? InstructionTextOverride
                : instructionTransform != null ? instructionTransform.GetComponent<Text>() : null;
            var calibratingTransform = canvasRoot.Find("Page0_Calibration/CalibratingText");
            _calibratingText = CalibratingTextOverride != null ? CalibratingTextOverride
                : calibratingTransform != null ? calibratingTransform.GetComponent<Text>() : null;
            var qrFrameTransform = canvasRoot.Find("Page0_Calibration/QRFrame");
            _qrFrame = QrFrameOverride != null ? QrFrameOverride : (qrFrameTransform != null ? qrFrameTransform.gameObject : null);
            var readyImageTransform = canvasRoot.Find("Page0_Calibration/Ready");
            _readyImage = ReadyImageOverride != null ? ReadyImageOverride : (readyImageTransform != null ? readyImageTransform.gameObject : null);
            _page0CanvasGroup = EnsureCanvasGroup(_page0Group);

            var fotoModeTransform = FotoModeButtonOverride != null ? FotoModeButtonOverride.transform : canvasRoot.Find("Page3_SharePrompt/FotoMode");
            _fotoModeImage = fotoModeTransform != null ? fotoModeTransform.GetComponent<Image>() : null;
            var videoModeTransform = VideoModeButtonOverride != null ? VideoModeButtonOverride.transform : canvasRoot.Find("Page3_SharePrompt/VideoMode");
            _videoModeImage = videoModeTransform != null ? videoModeTransform.GetComponent<Image>() : null;
            if (_page3Group != null) EnsurePhotoPreview(_page3Group.transform);

            // Checks Page1_Experience first - the button's own natural home
            // now that Page1 shares its exact visibility window (see
            // Update()'s own doc comment) - falling back to the old
            // root-level spot for a canvas that hasn't had it moved yet.
            var rescanTransform = RescanButtonOverride != null ? RescanButtonOverride.transform
                : (canvasRoot.Find("Page1_Experience/RescanButton") ?? canvasRoot.Find("RescanButton"));
            _rescanButton = rescanTransform != null ? rescanTransform.gameObject : null;
            var rescanButtonComponent = RescanButtonOverride != null ? RescanButtonOverride
                : (rescanTransform != null ? rescanTransform.GetComponent<Button>() : null);

            WireButton(RestartButtonOverride, canvasRoot, "Page2_CallToAction/RestartButton", Restart);
            WireButton(FotoButtonOverride, canvasRoot, "Page2_CallToAction/FotoButton", Foto);
            WireButton(RecordButtonOverride, canvasRoot, "Page3_SharePrompt/RecordButton", RetakePhoto);
            WireButton(TeilenButtonOverride, canvasRoot, "Page3_SharePrompt/TeilenButton", Teilen);
            WireButton(FotoModeButtonOverride, canvasRoot, "Page3_SharePrompt/FotoMode", SelectPhotoMode);
            WireButton(VideoModeButtonOverride, canvasRoot, "Page3_SharePrompt/VideoMode", SelectVideoMode);
            // rescanButtonComponent is already fully resolved above (override,
            // or found under either possible location) - passed straight
            // through as WireButton's own explicitButton so its internal
            // by-name fallback (which only knows the OLD root-level path)
            // never gets a chance to miss it.
            WireButton(rescanButtonComponent, canvasRoot, "RescanButton", Rescan);

            SetActiveIfNotNull(_page0Group, false);
            SetActiveIfNotNull(_page1Group, false);
            SetActiveIfNotNull(_page2Group, false);
            SetActiveIfNotNull(_page3Group, false);
            SetActiveIfNotNull(_rescanButton, false);
            UpdateModeButtonHighlights();

            EnsureFlashOverlay(canvasRoot);
        }

        private static GameObject FindChild(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// Prefers an explicitly-dragged-in Button (RestartButtonOverride etc.
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
        /// merely STARTS, well before anything is actually visible, found
        /// from an on-site report of the calibration screen disappearing
        /// with nothing yet on screen). Works identically whether the scene
        /// uses HandoffToInstantTracking's QR+SLAM handoff (HasContentSpawned)
        /// if Handoff is assigned, or RevealOnTrackingFound's simpler
        /// direct-image-tracking reveal (HasRevealed - already accurate for
        /// that simpler system, no separate settling delay exists there) as
        /// a fallback. Handoff always wins when assigned, so this has zero
        /// effect on a scene where Handoff is set (UCI-RE-AR) beyond fixing
        /// which of Handoff's own properties gets read.
        /// </summary>
        private bool HasRevealedContent =>
            Handoff != null ? Handoff.HasContentSpawned
            : DirectTrackingReveal != null && DirectTrackingReveal.HasRevealed;

        private void Update()
        {
            if (!HasRevealedContent)
            {
                // Shown once camera access is actually live, until content
                // has ACTUALLY spawned - see HasRevealedContent's own doc
                // comment for why that's a different (later) moment than
                // "tracking merely locked on". Also the state a Rescan()
                // returns to - HasRevealedContent reads false again the
                // instant Handoff/DirectTrackingReveal's own RequestRescan()
                // runs, so this exact branch is what puts the calibration
                // screen back up for a genuine second scan, no separate
                // rescan-specific logic needed here at all.
                bool cameraReady = _zapparCamera != null && _zapparCamera.CameraSourceInitialized;
                SetActiveIfNotNull(_page0Group, cameraReady);
                // Always reset to fully opaque here - undoes any fade-out
                // left over from a previous reveal cycle (see the revealed
                // branch below) now that this screen is showing again.
                if (_page0CanvasGroup != null) _page0CanvasGroup.alpha = 1f;

                // Always reset back to the CALIBRATING visual state here too -
                // undoes the READY swap below (frame/instruction/spinner off,
                // Ready image on) left over from a previous reveal cycle, so
                // a Rescan() puts back exactly the same screen a first-time
                // viewer sees, not whatever it looked like at the moment it
                // last faded out.
                SetActiveIfNotNull(_qrFrame, true);
                SetActiveIfNotNull(_instructionText != null ? _instructionText.gameObject : null, true);
                SetActiveIfNotNull(_spinnerImage != null ? _spinnerImage.gameObject : null, true);
                SetActiveIfNotNull(_readyImage, false);

                // "Preparing..." instead of "Scan the QR" while the .zpt
                // itself is still preloading - see TargetPreloader's own
                // doc comment for the real bug this avoids (scanning before
                // the target data has actually loaded can never be detected,
                // no matter how well the QR is framed).
                bool targetReady = Preloader == null || Preloader.IsReady;
                SetCalibrationMessage(cameraReady && !targetReady ? "Preparing..." : "Scan the QR to calibrate", "CALIBRATING...");

                SetActiveIfNotNull(_page1Group, false);
                SetActiveIfNotNull(_page2Group, false);
                SetActiveIfNotNull(_page3Group, false);
                SetActiveIfNotNull(_rescanButton, false);
                _handoffStartTime = -1f;
                _screen = UiScreen.None;
                _contentSpawnedAt = -1f;
                return;
            }

            // Content has just spawned - hold a "READY!" confirmation on the
            // calibration screen for ReadyDisplaySeconds, then fade it out
            // over ReadyFadeOutSeconds (rather than an instant
            // SetActive(false)), so a viewer looking at their phone gets a
            // clear "yes, look here now" cue instead of the screen just
            // vanishing with nothing yet visible behind it (per direct
            // request - both the hold length and the fade were increased/
            // added after on-site feedback that the original version
            // disappeared too fast for most viewers to actually see).
            // Runs in PARALLEL with, not instead of, the Page2DelaySeconds
            // countdown below - both start from this same moment.
            if (_contentSpawnedAt < 0f)
            {
                _contentSpawnedAt = Time.time;
                SetCalibrationMessage(null, "READY!");

                // Swap the calibrating visuals for the ready ones, per direct
                // request: instruction text/QR frame/spinner turn off
                // immediately, the new Ready image turns on immediately -
                // CalibratingText is the ONE thing that stays up throughout,
                // just with its wording changed above. All four then hold
                // together (or fade together, once past ReadyDisplaySeconds
                // below) as a single readable "you're set, look up" moment,
                // rather than the QR frame/spinner lingering pointlessly
                // alongside it.
                SetActiveIfNotNull(_qrFrame, false);
                SetActiveIfNotNull(_instructionText != null ? _instructionText.gameObject : null, false);
                SetActiveIfNotNull(_spinnerImage != null ? _spinnerImage.gameObject : null, false);
                SetActiveIfNotNull(_readyImage, true);
            }

            float readyElapsed = Time.time - _contentSpawnedAt;
            bool page0Visible;
            if (readyElapsed < ReadyDisplaySeconds)
            {
                page0Visible = true;
                if (_page0CanvasGroup != null) _page0CanvasGroup.alpha = 1f;
            }
            else if (readyElapsed < ReadyDisplaySeconds + ReadyFadeOutSeconds)
            {
                page0Visible = true;
                if (_page0CanvasGroup != null)
                    _page0CanvasGroup.alpha = Mathf.Lerp(1f, 0f, (readyElapsed - ReadyDisplaySeconds) / ReadyFadeOutSeconds);
            }
            else
            {
                page0Visible = false;
            }
            SetActiveIfNotNull(_page0Group, page0Visible);

            if (_handoffStartTime < 0f) _handoffStartTime = Time.time;

            if (_screen == UiScreen.None && Time.time - _handoffStartTime >= Page2DelaySeconds)
                _screen = UiScreen.CallToAction;

            SetActiveIfNotNull(_page2Group, _screen == UiScreen.CallToAction);
            SetActiveIfNotNull(_page3Group, _screen == UiScreen.SharePrompt);

            // Page1_Experience (per direct request) shares this EXACT same
            // window - active once the calibration screen has fully finished
            // (including its own fade-out above) and hidden again the
            // instant Page2 (Call To Action) appears. The Rescan button now
            // typically lives AS A CHILD of Page1_Experience (per direct
            // request - "I can add rescan to this page"), so toggling Page1
            // here already hides/shows it for free; _rescanButton is set
            // explicitly too regardless, harmless if it's already covered by
            // Page1's own active state, and still correct on its own if it
            // ever ends up back at the canvas root instead.
            bool experienceActive = !page0Visible && _screen == UiScreen.None;
            SetActiveIfNotNull(_page1Group, experienceActive);
            SetActiveIfNotNull(_rescanButton, experienceActive);
        }

        /// <summary>
        /// Sets the calibration screen's two placeholder text labels - pass
        /// null for either to leave it as-is (e.g. the instruction line
        /// doesn't need to change for the READY state). ONLY writes to a
        /// label whose own *TextOverride field was left empty - per direct
        /// request, dragging a specific Text object into
        /// InstructionTextOverride/CalibratingTextOverride means "I'm
        /// hand-authoring this label myself" (custom wording, a real
        /// translation, different styling split across runs, etc.), so this
        /// must never overwrite it. Auto-writing the hardcoded default
        /// strings only happens for the plain procedurally-built/by-name-found
        /// case (no override given), same as before.
        /// </summary>
        private void SetCalibrationMessage(string instruction, string calibrating)
        {
            if (instruction != null && _instructionText != null && InstructionTextOverride == null) _instructionText.text = instruction;
            if (calibrating != null && _calibratingText != null && CalibratingTextOverride == null) _calibratingText.text = calibrating;
        }

        private void LateUpdate()
        {
            if (_spinnerImage == null || _page0Group == null || !_page0Group.activeInHierarchy) return;

            if (_contentSpawnedAt >= 0f)
            {
                // READY state - freeze the ring as a complete, filled circle
                // (reads as "done") instead of continuing to loop, for the
                // brief window this screen stays up after content spawns.
                _spinnerImage.fillAmount = 1f;
                return;
            }

            // A radial-fill loading ring (Image.Type.Filled/Radial360), not a
            // rotated shape - fillAmount sweeps 0 -> 1 on a repeating sawtooth
            // (Mathf.Repeat), so it fills up, snaps back to empty, and fills
            // again in an endless loop, exactly "a round loading bar that
            // fills in a loop" per direct request.
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

            _page0Group = BuildPage0(canvasGo.transform);
            _page2Group = BuildPage2(canvasGo.transform);
            _page3Group = BuildPage3(canvasGo.transform);
            _rescanButton = BuildRescanButton(canvasGo.transform);

            _page0Group.SetActive(false);
            _page2Group.SetActive(false);
            _page3Group.SetActive(false);
            _rescanButton.SetActive(false);
            UpdateModeButtonHighlights();

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
        /// A full-screen Image showing the most recently captured photo -
        /// per direct request: "the client wants the video or foto to be
        /// shown first, and if we click teilen we go to the native share."
        /// Kept as the FIRST sibling under Page3_SharePrompt (opposite of
        /// CaptureFlash's LAST-sibling convention above) so it renders
        /// BEHIND the mode buttons/record button/Teilen/text - it's meant
        /// to visually replace the live camera view once something's been
        /// captured, not cover up the controls needed to retake or share it.
        /// Starts hidden (nothing captured yet); ShowPhotoPreview() is what
        /// actually populates and reveals it after a capture. Safe to call
        /// repeatedly (e.g. every AttachToExistingUI/BuildPage3) - reuses an
        /// existing "PhotoPreview" child instead of duplicating it.
        /// </summary>
        private void EnsurePhotoPreview(Transform page3Root)
        {
            var existing = page3Root.Find("PhotoPreview");
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("PhotoPreview");
                go.transform.SetParent(page3Root, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            go.transform.SetAsFirstSibling();
            go.SetActive(false);
            _photoPreviewImage = go.GetComponent<Image>();
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
        /// it's missing, without touching or rebuilding Page2/Page3 or
        /// anything else already placed/tuned. For a canvas built before
        /// the calibration screen existed (e.g. a hand-tuned prefab saved
        /// from an earlier version of this tool) - EditorRebuildUI() would
        /// wipe that hand-tuning entirely, this doesn't touch it at all.
        /// Safe to call repeatedly - no-ops if Page0_Calibration already
        /// exists. See Assets/Editor/AddCalibrationScreenToSharePrefab.cs
        /// for the menu command that calls this on a prefab asset directly.
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
            _page0Group.transform.SetSiblingIndex(0); // first, ahead of Page2/Page3
            _page0Group.SetActive(false);

            // Keep the flash overlay on top of the newly-added screen too.
            EnsureFlashOverlay(canvasRoot);

            Debug.Log("[ARShareController] Added Page0_Calibration under " + canvasRoot.name + ".");
        }

        /// <summary>
        /// Upgrades an ALREADY-PLACED "Page0_Calibration/Spinner" in place to
        /// a radial-fill loading ring (per direct request: "a round loading
        /// bar that fills in a loop") - only touches its sprite and Image
        /// fill settings (see SetUpAsLoadingRing), leaving whatever
        /// position/size it was hand-adjusted to completely untouched. For
        /// a spinner built by an earlier version of this tool (the old
        /// rotating gapped-ring "C" shape). Safe to re-run. See
        /// Assets/Editor/UpgradeSpinnerToRadialFill.cs for the menu command
        /// that applies this to a prefab asset directly.
        /// </summary>
        public void EditorUpgradeSpinnerToRadialFill()
        {
            Image image = SpinnerImageOverride;
            if (image == null)
            {
                var canvasRoot = transform.Find("ARShareCanvas");
                var spinnerTransform = canvasRoot != null ? canvasRoot.Find("Page0_Calibration/Spinner") : null;
                if (spinnerTransform == null)
                {
                    Debug.LogError("[ARShareController] No 'Page0_Calibration/Spinner' found (and SpinnerImageOverride isn't set) - either drag the Image into SpinnerImageOverride directly, or add the calibration screen first (ARReveal/UI/Add Calibration Screen To Share Prefab).");
                    return;
                }
                image = spinnerTransform.GetComponent<Image>();
            }
            if (image == null)
            {
                Debug.LogError("[ARShareController] Spinner object has no Image component.");
                return;
            }

            image.sprite = CreateRingSprite(Color.white, 128, 0.16f);
            SetUpAsLoadingRing(image);
            _spinnerImage = image;

            Debug.Log("[ARShareController] Spinner upgraded to a radial-fill loading ring - its position/size were left untouched.");
        }

        /// <summary>
        /// Surgical companion to EditorRebuildUI(), same spirit as
        /// EditorAddCalibrationScreenIfMissing() above - adds JUST the
        /// bottom-center Rescan button to an already-hand-tuned
        /// "ARShareCanvas" if it's missing, without touching or rebuilding
        /// anything else already placed/tuned. For a canvas built/saved
        /// before the Rescan button existed. Safe to call repeatedly -
        /// no-ops if a "RescanButton" already exists directly under the
        /// canvas root. See Assets/Editor/AddRescanButtonToSharePrefab.cs
        /// for the menu command that calls this on a prefab asset directly.
        /// </summary>
        public void EditorAddRescanButtonIfMissing()
        {
            var canvasRoot = transform.Find("ARShareCanvas");
            if (canvasRoot == null)
            {
                Debug.LogError("[ARShareController] No 'ARShareCanvas' child found - build the UI first (ARReveal/UI/Build Share UI In Scene).");
                return;
            }
            if (canvasRoot.Find("RescanButton") != null)
            {
                Debug.Log("[ARShareController] RescanButton already exists under " + canvasRoot.name + " - nothing to add.");
                return;
            }

            _rescanButton = BuildRescanButton(canvasRoot);
            _rescanButton.SetActive(false);

            // Keep the flash overlay on top of the newly-added button too.
            EnsureFlashOverlay(canvasRoot);

            Debug.Log("[ARShareController] Added RescanButton under " + canvasRoot.name + ".");
        }
#endif

        // --- CALIBRATION ---------------------------------------------------------
        private GameObject BuildPage0(Transform parent)
        {
            var group = new GameObject("Page0_Calibration");
            group.transform.SetParent(parent, false);

            // Rounded-square viewfinder frame - where the QR should be placed.
            // Generated at runtime (see CreateRoundedFrameSprite) - no design
            // asset for this exists yet. Named "QRFrame" (not left as the
            // AddImage default "Image") so the by-name lookup in
            // AttachToExistingUI/EditorAddCalibrationScreenIfMissing still
            // finds it consistently, matching the hand-tuned Share.prefab's
            // own naming.
            var frameSprite = CreateRoundedFrameSprite(new Color(1f, 1f, 1f, 0.9f), 512, 64, 10);
            var frameImage = AddImage(group.transform, frameSprite, new Vector2(0f, 150f), new Vector2(650f, 650f));
            frameImage.gameObject.name = "QRFrame";
            _qrFrame = frameImage.gameObject;

            _instructionText = AddPlaceholderText(group.transform, "InstructionText", "Scan the QR to calibrate",
                48, new Vector2(0f, 620f), new Vector2(800f, 160f));

            var spinnerImage = AddImage(group.transform, CreateRingSprite(Color.white, 128, 0.16f), new Vector2(0f, -700f), new Vector2(100f, 100f));
            spinnerImage.gameObject.name = "Spinner";
            SetUpAsLoadingRing(spinnerImage);
            _spinnerImage = spinnerImage;

            _calibratingText = AddPlaceholderText(group.transform, "CalibratingText", "CALIBRATING...",
                36, new Vector2(0f, -820f), new Vector2(500f, 80f));

            // Shown in place of the three above once content is ready (see
            // Update()) - starts hidden, matching the "calibrating" baseline
            // state every other element of this page starts in.
            var readyImage = AddImage(group.transform, ReadyImageSprite, new Vector2(0f, 150f), new Vector2(650f, 650f));
            readyImage.gameObject.name = "Ready";
            readyImage.gameObject.SetActive(false);
            _readyImage = readyImage.gameObject;

            _page0CanvasGroup = EnsureCanvasGroup(group);

            return group;
        }

        /// <summary>Gets or adds a CanvasGroup - used to fade the calibration screen out (see ReadyFadeOutSeconds) whether it was just built fresh here or picked up from a hand-tuned prefab that predates this fade (AttachToExistingUI).</summary>
        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            if (go == null) return null;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        /// <summary>
        /// Plain UnityEngine.UI.Text with Unity's built-in font - a placeholder
        /// until a real design asset exists for this screen (every OTHER
        /// screen's text in this class is a pre-rendered PNG per this
        /// project's own convention - see this class's own "CALIBRATION
        /// SCREEN" doc comment for why this one's different for now).
        /// </summary>
        private static Text AddPlaceholderText(Transform parent, string name, string text, int fontSize, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return label;
        }

        // --- CALL TO ACTION ----------------------------------------------------
        private GameObject BuildPage2(Transform parent)
        {
            var group = new GameObject("Page2_CallToAction");
            group.transform.SetParent(parent, false);

            // Stacked top-to-bottom: "Heute: dieses Kino." / logo / "Ab 17.
            // September: die ganze Welt." / Restart+Foto buttons - approximate
            // first-pass positions matching the design mockup's own vertical
            // order; adjust once visible on a real device, I can't preview this
            // without one.
            AddImage(group.transform, Page2TextTopSprite, new Vector2(0f, 520f), new Vector2(700f, 130f));
            AddImage(group.transform, LogoSprite, new Vector2(0f, 330f), new Vector2(800f, 225f));
            AddImage(group.transform, Page2TextBottomSprite, new Vector2(0f, 150f), new Vector2(700f, 130f));

            BuildImageButton(group.transform, "RestartButton", RestartButtonSprite,
                new Vector2(-150f, -600f), new Vector2(280f, 140f), Restart);
            BuildImageButton(group.transform, "FotoButton", FotoButtonSprite,
                new Vector2(150f, -600f), new Vector2(280f, 140f), Foto);

            return group;
        }

        // --- SHARE PROMPT --------------------------------------------------------
        private GameObject BuildPage3(Transform parent)
        {
            var group = new GameObject("Page3_SharePrompt");
            group.transform.SetParent(parent, false);

            // First sibling - see EnsurePhotoPreview's own doc comment for
            // why it needs to render BEHIND every button/text added below.
            EnsurePhotoPreview(group.transform);

            AddImage(group.transform, Page3TextSprite, new Vector2(0f, 400f), new Vector2(760f, 260f));

            // Foto/Video mode-select buttons - per direct request, choosing
            // between them decides what the round record button below
            // actually captures (see SelectPhotoMode/SelectVideoMode/
            // RetakePhoto). Falls back to a plain generated pill if no
            // design asset is assigned, same "never breaks, just plainer"
            // convention as RecordButtonSprite below.
            var fotoModeSprite = FotoModeSprite != null ? FotoModeSprite : CreateRoundedFillSprite(new Color(1f, 1f, 1f, 0.85f), 220, 90, 20);
            var fotoModeButton = BuildImageButton(group.transform, "FotoMode", fotoModeSprite,
                new Vector2(-130f, -250f), new Vector2(220f, 90f), SelectPhotoMode);
            _fotoModeImage = fotoModeButton.GetComponent<Image>();

            var videoModeSprite = VideoModeSprite != null ? VideoModeSprite : CreateRoundedFillSprite(new Color(1f, 1f, 1f, 0.85f), 220, 90, 20);
            var videoModeButton = BuildImageButton(group.transform, "VideoMode", videoModeSprite,
                new Vector2(130f, -250f), new Vector2(220f, 90f), SelectVideoMode);
            _videoModeImage = videoModeButton.GetComponent<Image>();

            // Uses RecordButtonSprite (RecButton.png) if assigned; falls back
            // to a plain generated red circle otherwise so this never breaks
            // if that field is left blank. Tapping it captures per whichever
            // mode is currently selected above (can be tapped more than once
            // to retake/recapture - see RetakePhoto's own doc comment).
            var recordSprite = RecordButtonSprite != null ? RecordButtonSprite : CreateCircleSprite(new Color(0.85f, 0.1f, 0.1f, 1f), 128);
            BuildImageButton(group.transform, "RecordButton", recordSprite,
                new Vector2(0f, 0f), new Vector2(140f, 140f), RetakePhoto);

            BuildImageButton(group.transform, "TeilenButton", TeilenButtonSprite,
                new Vector2(0f, -500f), new Vector2(280f, 140f), Teilen);

            return group;
        }

        // --- RESCAN --------------------------------------------------------
        /// <summary>
        /// Bottom-center "Rescan" button, per direct request - added directly
        /// on the root canvas (a sibling of Page0/Page2/Page3, not nested
        /// inside any of them), since its own visibility is gated
        /// independently of all three (see Update(): HasRevealedContent AND
        /// the calibration screen has fully hidden/faded AND neither Page2
        /// nor Page3 is up) rather than being tied to any single page.
        /// Tapping it calls Rescan(), which does nothing more than forward to
        /// Handoff/DirectTrackingReveal's own RequestRescan() - see that
        /// method's own doc comment for why that alone is enough to put the
        /// exact same calibration screen back up and run a genuine fresh
        /// settle/lock, with zero extra state to manage here. No design
        /// asset exists for this yet (added ad hoc, per direct request) - a
        /// generated rounded pill background (see CreateRoundedFillSprite)
        /// plus plain placeholder text, same "swap for a real asset later"
        /// caveat as the calibration screen's own two text labels.
        /// </summary>
        private GameObject BuildRescanButton(Transform parent)
        {
            var buttonSprite = CreateRoundedFillSprite(new Color(0f, 0f, 0f, 0.55f), 320, 110, 28);
            var button = BuildImageButton(parent, "RescanButton", buttonSprite,
                new Vector2(0f, -880f), new Vector2(320f, 110f), Rescan);
            button.GetComponent<Image>().preserveAspect = false;

            var label = AddPlaceholderText(button.transform, "Label", "RESCAN", 36, Vector2.zero, new Vector2(320f, 110f));
            label.raycastTarget = false;

            return button.gameObject;
        }

        private static Image AddImage(Transform parent, Sprite sprite, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("Image");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return image;
        }

        private static Button BuildImageButton(Transform parent, string name, Sprite sprite, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>Generates a plain filled circle sprite at runtime - see BuildPage3's own doc comment for why (no dedicated asset was provided for this button).</summary>
        private static Sprite CreateCircleSprite(Color color, int diameter)
        {
            var tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2((diameter - 1) / 2f, (diameter - 1) / 2f);
            float radius = diameter / 2f;
            var pixels = new Color[diameter * diameter];
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    pixels[y * diameter + x] = dist <= radius ? color : new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Generates a rounded-square OUTLINE (transparent fill, so the live
        /// camera feed shows through the middle - it's a viewfinder, not a
        /// solid shape) - the "typical QR rounded square window" for the
        /// calibration screen. Standard rounded-box signed-distance-field
        /// technique (see RoundedBoxSDF) - thickness is drawn as a band
        /// around the zero-distance boundary.
        /// </summary>
        private static Sprite CreateRoundedFrameSprite(Color color, int size, int cornerRadius, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float dist = RoundedBoxSDF(p, half - 1f, cornerRadius);
                    bool onBorder = Mathf.Abs(dist) <= thickness * 0.5f;
                    pixels[y * size + x] = onBorder ? color : new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Signed distance from p to the boundary of a square of half-extent halfExtent with rounded corners of the given radius - negative inside, positive outside, zero exactly on the boundary. Standard technique (Inigo Quilez's rounded-box SDF).</summary>
        private static float RoundedBoxSDF(Vector2 p, float halfExtent, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(halfExtent - radius, halfExtent - radius);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return outside + inside - radius;
        }

        /// <summary>
        /// Generates a SOLID filled rounded-rect (a "pill" background) at
        /// exactly width x height pixels - unlike CreateRoundedFrameSprite
        /// (an outline) or the square-only RoundedBoxSDF/CreateRingSprite
        /// above, this supports a genuinely rectangular (non-square) shape
        /// at its own real aspect ratio, generated at the exact pixel size
        /// the button will actually display at so it can be shown with
        /// preserveAspect OFF with no stretching - see BuildRescanButton.
        /// </summary>
        private static Sprite CreateRoundedFillSprite(Color color, int width, int height, int cornerRadius)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            Vector2 halfExtents = new Vector2(width / 2f - 1f, height / 2f - 1f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - width / 2f, y + 0.5f - height / 2f);
                    Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (halfExtents - new Vector2(cornerRadius, cornerRadius));
                    float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                    float dist = outside + inside - cornerRadius;
                    pixels[y * width + x] = dist <= 0f ? color : new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Generates a plain, fully-closed ring (donut) - the base shape for
        /// the calibration loading ring. No design asset exists for this yet
        /// - see this class's own "CALIBRATION SCREEN" doc comment. The
        /// actual "loading bar" animation isn't baked into this texture at
        /// all - see SetUpAsLoadingRing, which uses Unity's own
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
        /// bar that fills in a loop" (per direct request) - Unity's built-in
        /// Image.Type.Filled/Radial360 progressively reveals the ring
        /// starting from the top, clockwise, as fillAmount goes 0 -> 1 (see
        /// LateUpdate for the actual looping animation). Reusable so both a
        /// fresh BuildPage0 build and EditorUpgradeSpinnerToRadialFill
        /// (upgrading an already hand-placed spinner in place) configure it
        /// identically.
        /// </summary>
        private static void SetUpAsLoadingRing(Image image)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
        }

        /// <summary>
        /// Replays the tentacle/hole/FX burst in place - per direct request,
        /// NOT a page reload (an earlier version did that via a same-tab
        /// jslib call; superseded now that both reveal mechanisms expose
        /// their own proper in-place reset: HandoffToInstantTracking.
        /// RestartRevealSequence() for QR+SLAM scenes, or
        /// RevealOnTrackingFound.RestartRevealSequence() for direct
        /// image-tracking scenes - Handoff always wins if assigned, same as
        /// HasRevealedContent). Tracking/SLAM (if any) is untouched, so this
        /// is instant and needs no rescan. Also resets THIS controller's own
        /// screen timing, so the Call To Action screen reappears
        /// Page2DelaySeconds after this restart (not the original handoff),
        /// and immediately hides whichever page was showing rather than
        /// leaving it stuck up while the burst replays.
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
        /// The bottom-center Rescan button's own action, per direct request:
        /// "it put the calibrate UI back on and we can rescan and reset with
        /// the same flow as the first time." Unlike Restart() above (an
        /// instant in-place FX replay that leaves tracking completely
        /// untouched), this is a genuine full recalibration - it forwards to
        /// Handoff.RequestRescan()/DirectTrackingReveal.RequestRescan(),
        /// which hides the content again and forces HasContentSpawned/
        /// HasRevealed back to false. That alone is enough: the moment
        /// HasRevealedContent reads false again, Update()'s own existing
        /// "!HasRevealedContent" branch takes over on its own and puts the
        /// calibration screen ("Scan the QR to calibrate") straight back up,
        /// resets _screen/_handoffStartTime/_contentSpawnedAt, and hides
        /// Page2/Page3/this button - exactly the same state the very first
        /// launch starts in, with no separate reset logic needed here.
        /// </summary>
        public void Rescan()
        {
            if (Handoff != null) Handoff.RequestRescan();
            else if (DirectTrackingReveal != null) DirectTrackingReveal.RequestRescan();
        }

        /// <summary>
        /// Just advances from the Call To Action screen to the Share Prompt
        /// screen ("the selfie screen, the one with the red button" per
        /// direct correction) - does NOT capture anything itself. An earlier
        /// version captured + immediately opened the save/share dialog right
        /// here, skipping that screen entirely - wrong per the actual design,
        /// where FOTO is purely a navigation step and the round record
        /// button (RetakePhoto) is the one real "take the photo" action.
        /// Resets to Photo mode and hides any stale preview from a previous
        /// visit, per direct request - a fresh visit to this screen should
        /// always start from the same predictable state (mode picker up,
        /// nothing captured yet), not wherever the LAST visit left off.
        /// </summary>
        public void Foto()
        {
            _screen = UiScreen.SharePrompt;
            SetCaptureMode(CaptureMode.Photo);
            SetActiveIfNotNull(_photoPreviewImage != null ? _photoPreviewImage.gameObject : null, false);
        }

        /// <summary>Wired to the FotoMode/VideoMode buttons on the Share Prompt screen - see RetakePhoto/UpdateModeButtonHighlights.</summary>
        public void SelectPhotoMode() => SetCaptureMode(CaptureMode.Photo);
        public void SelectVideoMode() => SetCaptureMode(CaptureMode.Video);

        private void SetCaptureMode(CaptureMode mode)
        {
            _captureMode = mode;
            UpdateModeButtonHighlights();
        }

        /// <summary>Dims whichever of FotoMode/VideoMode ISN'T currently selected, so it's visually obvious which one the round record button below will act on - a plain alpha tint rather than needing two separate "selected/unselected" sprite assets per button.</summary>
        private void UpdateModeButtonHighlights()
        {
            const float selectedAlpha = 1f;
            const float unselectedAlpha = 0.45f;
            if (_fotoModeImage != null) SetImageAlpha(_fotoModeImage, _captureMode == CaptureMode.Photo ? selectedAlpha : unselectedAlpha);
            if (_videoModeImage != null) SetImageAlpha(_videoModeImage, _captureMode == CaptureMode.Video ? selectedAlpha : unselectedAlpha);
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            var c = image.color;
            c.a = alpha;
            image.color = c;
        }

        /// <summary>
        /// The round record button on the Share Prompt screen - THE actual
        /// "take the photo" action, for whichever of Photo/Video mode is
        /// currently selected (see SelectPhotoMode/SelectVideoMode above).
        /// Can be tapped more than once to retake/recapture, in case the
        /// first one didn't land right - just re-runs the same routine,
        /// overwriting whatever was captured before.
        ///
        /// Per direct request, this no longer jumps straight to the native
        /// share sheet - it just captures and shows a PREVIEW (see
        /// ShowPhotoPreview), leaving Teilen as the actual "now share this"
        /// action (see Teilen's own doc comment for why that's a clean,
        /// small change rather than a restructure - ShareLastPhoto/
        /// ARReveal_ShareImage, the actual native-share plumbing, is
        /// completely unchanged).
        ///
        /// VIDEO capture is NOT implemented yet - a real feature on its own
        /// (Unity WebGL has no built-in video encoder; capturing a video
        /// means bridging to the browser's own MediaRecorder/
        /// canvas.captureStream() APIs via new custom .jslib code, which
        /// doesn't exist in this project yet). Selecting Video mode and
        /// tapping record currently just logs a warning and does nothing -
        /// intentional placeholder, not a bug, until that's built.
        /// </summary>
        public void RetakePhoto()
        {
            if (_captureMode == CaptureMode.Video)
            {
                Debug.LogWarning("[ARShareController] Video capture isn't implemented yet - see RetakePhoto's own doc comment. Switch to Foto mode to actually capture something.");
                return;
            }
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
        /// Populates PhotoPreview (see EnsurePhotoPreview) with the just-
        /// captured JPEG and reveals it - per direct request, the client
        /// wants the photo shown first, with Teilen as the actual "share
        /// this" action (RetakePhoto can still be tapped again to retake,
        /// which simply calls this again and replaces what's shown).
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

            _photoPreviewImage.gameObject.SetActive(true);
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
        /// back to a plain download inside that same plugin if this
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
        /// THE "share this now" action, per direct request: RetakePhoto no
        /// longer shares automatically (it just captures and shows a
        /// preview - see ShowPhotoPreview), so this is what actually opens
        /// the native OS share sheet for whatever's currently shown in that
        /// preview. Nothing about ShareLastPhoto/ARReveal_ShareImage
        /// themselves changed - Teilen already called this exact method
        /// before, just as a secondary "re-share if the dialog was
        /// dismissed by mistake" convenience; it's simply promoted to the
        /// primary trigger now that capture and share are two separate
        /// steps instead of one.
        /// </summary>
        public void Teilen()
        {
            ShareLastPhoto();
        }
    }
}
