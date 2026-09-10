using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Zappar;
using Zappar.Additional.SNS;

namespace ARReveal
{
    /// <summary>
    /// Builds its own "Selfie" and "Share" buttons (procedural UI, same drop-in
    /// philosophy as CameraSlimeOverlay/ProceduralClouds - no manual Canvas/Button
    /// setup needed in the scene) so the viewer can:
    ///
    ///   1. Tap "Selfie" to flip to the front-facing camera via
    ///      ZapparBaseCamera.SwitchToFrontCameraMode() - the whole AR scene/
    ///      tentacles keep rendering and tracking exactly as before, just filmed
    ///      from the front camera, so a viewer can turn the phone around and pose
    ///      with the AR content behind them. Tapping it again (it relabels itself
    ///      "Back Camera") returns to the rear camera via SwitchToRearCameraMode().
    ///      Both are real, built-in methods on this project's own installed
    ///      Zappar SDK (confirmed directly in ZapparBaseCamera.cs, not guessed from
    ///      docs) - Zappar's own default mirroring per direction (mirrored for
    ///      front/selfie, not for rear) is left at each method's own default.
    ///
    ///   2. Tap "Share" to capture the current frame (AR content composited in,
    ///      not just the raw camera feed) via Zappar's WebGL Save & Share package
    ///      (com.zappar.sns, class ZSaveNShare) and open the device's native share
    ///      sheet - which is what actually offers X/Instagram/Facebook etc. as
    ///      destinations, IF the viewer has those apps installed. A web page has
    ///      no way to post directly into a specific platform's own feed/API -
    ///      "share to Instagram" from any website, this one included, always goes
    ///      through the OS's own share sheet, the same mechanism as every other
    ///      website's share button.
    ///
    /// Requires the com.zappar.sns package (added to Packages/manifest.json,
    /// resolves automatically) - UNLIKE the camera-switching calls above, the
    /// ZSaveNShare calls below are written from Zappar's own published
    /// documentation, not this project's actual installed source, since the
    /// package hadn't been pulled down/was not yet browsable in the Editor at the
    /// time this was written. If the exact namespace/method signature turns out to
    /// differ once Unity resolves it, that'll show up as a clear compile error
    /// pointing at ShareRoutine() below - worth a quick check once the package
    /// import finishes.
    /// </summary>
    public class ARShareController : MonoBehaviour
    {
        [Tooltip("Auto-found in the scene if left blank.")]
        public ZapparBaseCamera Camera;

        [Header("Button look")]
        public Color ButtonColor = new Color(1f, 1f, 1f, 0.85f);
        public Color ButtonTextColor = Color.black;
        [Tooltip("Above CameraSlimeOverlay's 500, so these buttons always draw on top of the slime splat.")]
        public int SortingOrder = 600;

        private bool _frontFacing;
        private Text _selfieLabel;

        private void Awake()
        {
            if (Camera == null) Camera = FindFirstObjectByType<ZapparBaseCamera>();
            BuildUI();
            // Per the package's own docs - call once at scene start before TakeSnapshot/OpenSNSSnapPrompt are used.
            ZSaveNShare.Initialize();
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("ARShareCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Bottom-right corner, Share further in from the edge than Selfie so
            // both sit comfortably above a phone's own home-gesture/nav bar area.
            _selfieLabel = BuildButton(canvasGo.transform, "Selfie", new Vector2(-170f, 70f), ToggleCamera);
            BuildButton(canvasGo.transform, "Share", new Vector2(-20f, 70f), Share);
        }

        private Text BuildButton(Transform parent, string label, Vector2 anchoredFromBottomRight, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = ButtonColor;
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(140f, 70f);
            rt.anchoredPosition = anchoredFromBottomRight;

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.color = ButtonTextColor;
            text.alignment = TextAnchor.MiddleCenter;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            return text;
        }

        /// <summary>Flips between rear and front (selfie) camera - AR tracking/content keeps rendering through the switch, per Zappar's own ZapparBaseCamera.SwitchTo*CameraMode methods (confirmed in this project's installed SDK source).</summary>
        public void ToggleCamera()
        {
            if (Camera == null) return;
            _frontFacing = !_frontFacing;
            if (_frontFacing) Camera.SwitchToFrontCameraMode();
            else Camera.SwitchToRearCameraMode();
            if (_selfieLabel != null) _selfieLabel.text = _frontFacing ? "Back Camera" : "Selfie";
        }

        /// <summary>Captures the current composited frame and opens the device's native share sheet via Zappar's WebGL Save & Share package.</summary>
        public void Share()
        {
            StartCoroutine(ShareRoutine());
        }

        private IEnumerator ShareRoutine()
        {
            yield return ZSaveNShare.TakeSnapshot();
            ZSaveNShare.OpenSNSSnapPrompt();
        }
    }
}
