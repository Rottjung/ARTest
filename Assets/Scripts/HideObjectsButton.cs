using UnityEngine;
using UnityEngine.UI;

namespace ARReveal
{
    /// <summary>
    /// Builds its own single "Hide"/"Show" button, bottom-LEFT corner (mirrors
    /// ARShareController's Selfie/Share buttons, which sit bottom-right) - same
    /// procedural-UI approach, no manual Canvas/Button setup needed in the scene.
    /// Tapping it toggles every GameObject in <see cref="ObjectsToToggle"/> off (or
    /// back on) together - e.g. drop the building in there to let a viewer/client
    /// hide it and compare against the real backdrop, or hide any other object the
    /// same way. All entries always end up in the SAME state as each other (one
    /// shared on/off flag, not per-object) - if that's ever not what's wanted,
    /// add a second HideObjectsButton with its own list rather than mixing groups
    /// into one.
    ///
    /// Like ARShareController, this should be placed as its own root-level scene
    /// object with NO parent under ContentWrapper - HandoffToInstantTracking's
    /// Awake() does ContentWrapper.gameObject.SetActive(false) until the QR is
    /// found, which would hide this button along with everything else if it were
    /// a child, and DisableAllChildScripts() would also leave its script disabled
    /// forever (see that class's own doc comment for why - the exact bug already
    /// hit once with the Share buttons).
    /// </summary>
    public class HideObjectsButton : MonoBehaviour
    {
        [Tooltip("Everything here gets SetActive(false)/(true) together whenever the button is tapped. Drop the building (or any other object) in here - as many as needed, they all toggle as one group.")]
        public GameObject[] ObjectsToToggle;

        [Header("Button look")]
        public Color ButtonColor = new Color(1f, 1f, 1f, 0.85f);
        public Color ButtonTextColor = Color.black;
        [Tooltip("Above CameraSlimeOverlay's 500, so this button always draws on top of the slime splat - same sorting order ARShareController's buttons use, so neither draws over the other.")]
        public int SortingOrder = 600;

        [Tooltip("If true, ObjectsToToggle start hidden (button starts reading 'Show') instead of visible ('Hide') - whatever they already are in the scene/prefab is left alone either way, this only decides the button's own starting label and which state a fresh tap moves them TO first.")]
        public bool StartHidden = false;

        private bool _hidden;
        private Text _label;

        private void Awake()
        {
            _hidden = StartHidden;
            BuildUI();
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("HideObjectsCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            _label = BuildButton(canvasGo.transform, _hidden ? "Show" : "Hide", Toggle);
        }

        private Text BuildButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = ButtonColor;
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var rt = go.GetComponent<RectTransform>();
            // Bottom-left corner, same distance from the bottom edge as
            // ARShareController's buttons (70px) so both rows line up visually.
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(140f, 70f);
            rt.anchoredPosition = new Vector2(20f, 70f);

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

        /// <summary>Flips every entry in ObjectsToToggle between active/inactive together, and relabels the button to say whichever action tapping it again would do next.</summary>
        public void Toggle()
        {
            _hidden = !_hidden;
            bool visible = !_hidden;
            if (ObjectsToToggle != null)
            {
                foreach (var go in ObjectsToToggle)
                    if (go != null) go.SetActive(visible);
            }
            if (_label != null) _label.text = _hidden ? "Show" : "Hide";
        }
    }
}
