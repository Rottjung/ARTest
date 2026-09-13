using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using Zappar;
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
    /// Three screens, all built from the final design's own PNGs (every screen's
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
    ///  - DISTANCE ALERT: overrides whichever of the above is showing, any time
    ///    the viewer gets too close to the content itself (not the QR) - see
    ///    TooCloseDistanceMeters's own doc comment.
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
    /// </summary>
    public class ARShareController : MonoBehaviour
    {
        [Tooltip("Auto-found in the scene if left blank.")]
        public HandoffToInstantTracking Handoff;

        [Header("Sprites - drag the matching PNG from Assets/Images/UI onto each")]
        public Sprite LogoSprite;
        public Sprite Page1TextSprite;
        public Sprite Page2TextTopSprite;
        public Sprite Page2TextBottomSprite;
        public Sprite Page3TextSprite;
        public Sprite RestartButtonSprite;
        public Sprite FotoButtonSprite;
        public Sprite TeilenButtonSprite;

        [Header("Timing / thresholds")]
        [Tooltip("Seconds after tracking locks before the Call To Action screen (logo + Restart/Foto buttons) appears - requested directly as 30 seconds.")]
        public float Page2DelaySeconds = 30f;
        [Tooltip("How close (meters) the viewer can get to the content before the Distance Alert screen takes over. UNTESTED - no client spec given for this number, chosen as a reasonable starting guess; tune on-site.")]
        public float TooCloseDistanceMeters = 3f;

        [Tooltip("Above CameraSlimeOverlay's 500, so this UI always draws on top of the slime splat.")]
        public int SortingOrder = 600;

        private enum UiScreen { None, CallToAction, SharePrompt }
        private UiScreen _screen = UiScreen.None;

        private GameObject _page1Group;
        private GameObject _page2Group;
        private GameObject _page3Group;

        private Transform _camTransform;
        private float _handoffStartTime = -1f;

        private void Awake()
        {
            if (Handoff == null) Handoff = FindFirstObjectByType<HandoffToInstantTracking>();
            var zCam = ZapparCamera.Instance != null ? ZapparCamera.Instance : FindFirstObjectByType<ZapparCamera>();
            if (zCam != null) _camTransform = zCam.transform;

            BuildUI();
            // Per the package's own docs - call once at scene start before TakeSnapshot/OpenSNSSnapPrompt are used.
            ZSaveNShare.Initialize();
        }

        private void Update()
        {
            if (Handoff == null || !Handoff.HasHandedOff)
            {
                SetActiveIfNotNull(_page1Group, false);
                SetActiveIfNotNull(_page2Group, false);
                SetActiveIfNotNull(_page3Group, false);
                _handoffStartTime = -1f;
                _screen = UiScreen.None;
                return;
            }

            if (_handoffStartTime < 0f) _handoffStartTime = Time.time;

            // DISTANCE ALERT overrides whichever screen would otherwise be
            // showing, any time the viewer is too close to the CONTENT itself
            // (ContentRoot if assigned, else ContentWrapper) - not the QR, since
            // being close to the QR on the ground is normal/expected, being
            // close to the virtual building/tentacles is what this warns about.
            Transform contentTransform = Handoff.ContentRoot != null ? Handoff.ContentRoot : Handoff.ContentWrapper;
            bool tooClose = false;
            if (_camTransform != null && contentTransform != null)
            {
                float dist = Vector3.Distance(_camTransform.position, contentTransform.position);
                tooClose = dist < TooCloseDistanceMeters;
            }

            if (tooClose)
            {
                SetActiveIfNotNull(_page1Group, true);
                SetActiveIfNotNull(_page2Group, false);
                SetActiveIfNotNull(_page3Group, false);
                return;
            }

            SetActiveIfNotNull(_page1Group, false);

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

            _page1Group = BuildPage1(canvasGo.transform);
            _page2Group = BuildPage2(canvasGo.transform);
            _page3Group = BuildPage3(canvasGo.transform);

            _page1Group.SetActive(false);
            _page2Group.SetActive(false);
            _page3Group.SetActive(false);
        }

        // --- DISTANCE ALERT ---------------------------------------------------
        private GameObject BuildPage1(Transform parent)
        {
            var group = new GameObject("Page1_DistanceAlert");
            group.transform.SetParent(parent, false);

            // Positioned lower-middle, matching the design mockup's own layout
            // for this screen (no buttons on this one - it's purely a "step
            // back" instruction that clears itself once the viewer does).
            AddImage(group.transform, Page1TextSprite, new Vector2(0f, -150f), new Vector2(760f, 380f));

            return group;
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

            AddImage(group.transform, Page3TextSprite, new Vector2(0f, 400f), new Vector2(760f, 260f));

            // No dedicated asset was provided for the round record button in the
            // design (only the three "Use_Button_*" stamp graphics) - built
            // procedurally instead, same spirit as this project's other
            // procedural content (ProceduralClouds etc.) rather than leaving it
            // out. Tapping it retakes the photo without leaving this screen.
            var recordSprite = CreateCircleSprite(new Color(0.85f, 0.1f, 0.1f, 1f), 128);
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

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ARReveal_ReloadPage();
#endif

        /// <summary>
        /// Reloads the page - the simplest, most reliable "restart everything"
        /// for a WebGL AR experience (re-running the tracking/reveal state
        /// machine in place would need resetting a lot of independent state -
        /// HandoffToInstantTracking, every TentacleController/WallHoleEffect/
        /// DebrisRing, this controller's own screen state - a full reload
        /// guarantees a genuinely clean slate).
        ///
        /// Deliberately NOT Application.OpenURL(Application.absoluteURL) - on
        /// WebGL that can call window.open(url, "_blank") depending on the
        /// template, opening a SECOND tab and leaving the original (with its
        /// live camera feed) still running behind it. Instead this calls a
        /// tiny native plugin (Plugins/WebGL/ARReveal_Reload.jslib) that does
        /// window.location.reload() directly - guaranteed same-tab, in-place.
        ///
        /// Because the reload stays on the same origin, the browser does NOT
        /// re-prompt for camera/microphone permission - permissions are
        /// granted per-origin, not per page-load, so whatever the user
        /// already granted carries straight over automatically. No-ops
        /// outside a WebGL build (nothing to reload in the Editor/other
        /// platforms).
        /// </summary>
        public void Restart()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ARReveal_ReloadPage();
#else
            Debug.Log("[ARShareController] Restart requested - only reloads the page in an actual WebGL build.");
#endif
        }

        /// <summary>Takes the snapshot that will later be shared, then advances from the Call To Action screen to the Share Prompt screen.</summary>
        public void Foto()
        {
            StartCoroutine(TakePhotoThenShowSharePrompt());
        }

        private IEnumerator TakePhotoThenShowSharePrompt()
        {
            yield return ZSaveNShare.TakeSnapshot();
            _screen = UiScreen.SharePrompt;
        }

        /// <summary>The round record button on the Share Prompt screen - retakes the photo without changing screens, in case the first one didn't land right.</summary>
        public void RetakePhoto()
        {
            StartCoroutine(RetakePhotoRoutine());
        }

        private IEnumerator RetakePhotoRoutine()
        {
            yield return ZSaveNShare.TakeSnapshot();
        }

        /// <summary>Opens the device's native share sheet for whichever photo was most recently captured (Foto or the record button) - no fresh capture here, sharing is a separate step from taking the photo now.</summary>
        public void Teilen()
        {
            ZSaveNShare.OpenSNSSnapPrompt();
        }
    }
}
