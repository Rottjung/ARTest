using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Zappar.Additional.SNS;

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
    /// Two screens, both built from the final design's own PNGs (every screen's
    /// text is pre-rendered INTO its image asset - no font/TextMeshPro needed at
    /// all, just positioned Image components):
    ///
    ///  - CALL TO ACTION (shown Page2DelaySeconds after tracking locks): logo +
    ///    "Heute: dieses Kino... die ganze Welt" two-line text (split into
    ///    Page_02_Text_Top.png/Page_02_Text_Bottom.png so the logo can sit
    ///    between them, matching the actual design mockup - the source PNG had
    ///    both lines in one image) + RESTART and FOTO buttons.
    ///  - SHARE PROMPT (shown after tapping FOTO): "share your photo, win
    ///    tickets" text + a record button (retakes the photo) + TEILEN (share)
    ///    button.
    ///
    /// A third "Distance Alert" screen (warning the viewer to step back) was
    /// tried and then removed entirely per direct request - see git history
    /// around "Distance Alert" if it's ever wanted back.
    ///
    /// The OLD "Selfie" button used to mean "flip to the front camera" - that's
    /// removed entirely per direct request (no camera-switching UI at all
    /// anymore). "FOTO" in the new design means something different: take a
    /// snapshot of the current AR view, then move on to the share prompt - see
    /// TakePhotoThenShowSharePrompt(). Sharing itself is unchanged from before -
    /// Zappar's WebGL Save & Share package (com.zappar.sns, class ZSaveNShare)
    /// captures the composited AR view (not just the raw camera feed) and opens
    /// the device's native share sheet, which is what actually offers
    /// Instagram/Messages/etc as destinations - a web page has no way to post
    /// directly into a specific platform's own feed/API, this native-share-sheet
    /// approach is the only way any website can "share to Instagram".
    ///
    /// RESTART, per direct request, means replay the tentacle/hole/FX burst in
    /// place - NOT reload the browser page (an earlier version did exactly
    /// that via a same-tab jslib reload; superseded, see Restart()'s own doc
    /// comment and HandoffToInstantTracking.RestartRevealSequence()).
    ///
    /// CAPTURE FEEDBACK: a website cannot silently write an image into the
    /// device's photo gallery - no permission grants that, it's a hard
    /// browser security boundary, not a Zappar/Unity limitation. The only
    /// way to get a captured photo into Photos/gallery is the native OS
    /// save/share sheet ZSaveNShare.OpenSNSSnapPrompt() opens (the user taps
    /// "Save" in THAT sheet - one tap, no separate permission dialog). Both
    /// Foto and the round record button now open that sheet immediately
    /// after capturing (previously only Teilen did, so tapping the record
    /// button silently captured nothing anyone could see or save - the
    /// reported bug this fixes) - plus a brief white flash (PlayCaptureFlash)
    /// fires the instant either is tapped, giving immediate "yes, that
    /// registered" feedback regardless of how long the capture/dialog takes
    /// to actually appear. Whether that native sheet offers "Share" as well
    /// as "Save" depends on the browser's own Web Share API support (not
    /// something this code controls) - notably, an in-app browser (e.g.
    /// Instagram/TikTok's own webview, plausible for a QR-driven promo) may
    /// only offer a save/download fallback with no share option at all.
    /// </summary>
    public class ARShareController : MonoBehaviour
    {
        [Tooltip("Auto-found in the scene if left blank.")]
        public HandoffToInstantTracking Handoff;

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

        [Header("Optional: after hand-tuning a prebuilt UI (see ARReveal/Build Share UI In Scene), drag the resulting page groups/buttons in here directly. Leave blank to auto-find them by name instead (see AttachToExistingUI).")]
        public GameObject Page2GroupOverride;
        public GameObject Page3GroupOverride;
        public Button RestartButtonOverride;
        public Button FotoButtonOverride;
        public Button RecordButtonOverride;
        public Button TeilenButtonOverride;

        [Header("Timing / thresholds")]
        [Tooltip("Seconds after tracking locks before the Call To Action screen (logo + Restart/Foto buttons) appears - requested directly as 30 seconds.")]
        public float Page2DelaySeconds = 30f;

        [Tooltip("Above CameraSlimeOverlay's 500, so this UI always draws on top of the slime splat.")]
        public int SortingOrder = 600;

        private enum UiScreen { None, CallToAction, SharePrompt }
        private UiScreen _screen = UiScreen.None;

        private GameObject _page2Group;
        private GameObject _page3Group;

        private float _handoffStartTime = -1f;

        private Image _flashImage;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            if (Handoff == null) Handoff = FindFirstObjectByType<HandoffToInstantTracking>();

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

            // Per the package's own docs - call once at scene start before TakeSnapshot/OpenSNSSnapPrompt are used.
            ZSaveNShare.Initialize();
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
            _page2Group = Page2GroupOverride != null ? Page2GroupOverride : FindChild(canvasRoot, "Page2_CallToAction");
            _page3Group = Page3GroupOverride != null ? Page3GroupOverride : FindChild(canvasRoot, "Page3_SharePrompt");

            WireButton(RestartButtonOverride, canvasRoot, "Page2_CallToAction/RestartButton", Restart);
            WireButton(FotoButtonOverride, canvasRoot, "Page2_CallToAction/FotoButton", Foto);
            WireButton(RecordButtonOverride, canvasRoot, "Page3_SharePrompt/RecordButton", RetakePhoto);
            WireButton(TeilenButtonOverride, canvasRoot, "Page3_SharePrompt/TeilenButton", Teilen);

            SetActiveIfNotNull(_page2Group, false);
            SetActiveIfNotNull(_page3Group, false);

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

        private void Update()
        {
            if (Handoff == null || !Handoff.HasHandedOff)
            {
                SetActiveIfNotNull(_page2Group, false);
                SetActiveIfNotNull(_page3Group, false);
                _handoffStartTime = -1f;
                _screen = UiScreen.None;
                return;
            }

            if (_handoffStartTime < 0f) _handoffStartTime = Time.time;

            if (_screen == UiScreen.None && Time.time - _handoffStartTime >= Page2DelaySeconds)
                _screen = UiScreen.CallToAction;

            SetActiveIfNotNull(_page2Group, _screen == UiScreen.CallToAction);
            SetActiveIfNotNull(_page3Group, _screen == UiScreen.SharePrompt);
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

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            _page2Group = BuildPage2(canvasGo.transform);
            _page3Group = BuildPage3(canvasGo.transform);

            _page2Group.SetActive(false);
            _page3Group.SetActive(false);

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
#endif

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

            AddImage(group.transform, Page3TextSprite, new Vector2(0f, 400f), new Vector2(760f, 260f));

            // Uses RecordButtonSprite (RecButton.png) if assigned; falls back
            // to a plain generated red circle otherwise so this never breaks
            // if that field is left blank. Tapping it retakes the photo
            // without leaving this screen.
            var recordSprite = RecordButtonSprite != null ? RecordButtonSprite : CreateCircleSprite(new Color(0.85f, 0.1f, 0.1f, 1f), 128);
            BuildImageButton(group.transform, "RecordButton", recordSprite,
                new Vector2(0f, 0f), new Vector2(140f, 140f), RetakePhoto);

            BuildImageButton(group.transform, "TeilenButton", TeilenButtonSprite,
                new Vector2(0f, -500f), new Vector2(280f, 140f), Teilen);

            return group;
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
        /// Replays the tentacle/hole/FX burst in place - per direct request,
        /// NOT a page reload (an earlier version did that via a same-tab
        /// jslib call; superseded now that HandoffToInstantTracking exposes a
        /// proper in-place reset - see RestartRevealSequence's own doc
        /// comment). Tracking/SLAM is untouched, so this is instant and needs
        /// no QR rescan. Also resets THIS controller's own screen timing, so
        /// the Call To Action screen reappears Page2DelaySeconds after this
        /// restart (not the original handoff), and immediately hides
        /// whichever page was showing rather than leaving it stuck up while
        /// the burst replays.
        /// </summary>
        public void Restart()
        {
            if (Handoff != null) Handoff.RestartRevealSequence();

            _screen = UiScreen.None;
            _handoffStartTime = Time.time;
            SetActiveIfNotNull(_page2Group, false);
            SetActiveIfNotNull(_page3Group, false);
        }

        /// <summary>Takes the snapshot that will later be shared, then advances from the Call To Action screen to the Share Prompt screen. See this class's own "CAPTURE FEEDBACK" doc comment for the flash + why the save/share dialog opens immediately.</summary>
        public void Foto()
        {
            PlayCaptureFlash();
            StartCoroutine(TakePhotoThenShowSharePrompt());
        }

        private IEnumerator TakePhotoThenShowSharePrompt()
        {
            yield return ZSaveNShare.TakeSnapshot();
            _screen = UiScreen.SharePrompt;
            ZSaveNShare.OpenSNSSnapPrompt();
        }

        /// <summary>The round record button on the Share Prompt screen - retakes the photo without changing screens, in case the first one didn't land right. See this class's own "CAPTURE FEEDBACK" doc comment - previously this only captured silently with no way to ever save/see it, which is the reported "nothing gets added to my gallery" bug; now it flashes immediately and opens the same native save/share dialog Teilen does.</summary>
        public void RetakePhoto()
        {
            PlayCaptureFlash();
            StartCoroutine(RetakePhotoRoutine());
        }

        private IEnumerator RetakePhotoRoutine()
        {
            yield return ZSaveNShare.TakeSnapshot();
            ZSaveNShare.OpenSNSSnapPrompt();
        }

        /// <summary>Re-opens the native save/share dialog for whichever photo was most recently captured, without taking a new one - useful if the dialog from Foto/RetakePhoto was dismissed by mistake.</summary>
        public void Teilen()
        {
            ZSaveNShare.OpenSNSSnapPrompt();
        }
    }
}
