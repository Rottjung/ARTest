using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ARReveal
{
    /// <summary>
    /// A full-screen slime/goo splat that flashes onto the camera view and fades out -
    /// built entirely in code (procedural splat texture, procedural Canvas/Image), so
    /// it's a drop-in add to the camera with nothing to import. Meant to fire the
    /// instant a tentacle's strike actually reaches the camera - see
    /// TentacleController.SlimeOverlay/TouchCameraIfClose(), which calls Splat() the
    /// moment a strike's reach-clamped strength peaks near 1 (i.e. the tip actually
    /// got there, not a strike that correctly fell short of a distant camera).
    ///
    /// Attach anywhere (does not need to be on the camera itself, though that's the
    /// natural place) - it builds its own Screen Space Overlay canvas at Awake so it
    /// always renders on top regardless of scene camera setup.
    /// </summary>
    public class CameraSlimeOverlay : MonoBehaviour
    {
        [Header("Look")]
        public Color SlimeColor = new Color(0.22f, 0.32f, 0.14f, 1f);
        [Range(0f, 1f)] public float MaxOpacity = 0.85f;
        [Tooltip("Sorting order of the overlay canvas - high so it draws above everything else on screen.")]
        public int SortingOrder = 500;

        [Header("Timing")]
        public float FadeInDuration = 0.05f;
        public float HoldDuration = 0.4f;
        public float FadeOutDuration = 0.9f;

        [Tooltip("Auto-trigger Splat() shortly after Start(), for standalone testing.")]
        public bool OpenOnStart = false;
        public float OpenOnStartDelay = 0.5f;

        private Image _image;
        private Coroutine _routine;

        private void Awake()
        {
            BuildUI();
        }

        private void Start()
        {
            // A coroutine, not Invoke(nameof(Splat), ...) - Invoke() only works with
            // parameterless methods, which Splat() is, but kept consistent with every
            // other OpenOnStart in this project for the same reasoning.
            if (OpenOnStart) StartCoroutine(SplatAfterDelay());
        }

        private IEnumerator SplatAfterDelay()
        {
            yield return new WaitForSeconds(OpenOnStartDelay);
            Splat();
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("SlimeOverlayCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvasGo.AddComponent<CanvasScaler>();

            var imgGo = new GameObject("Slime");
            imgGo.transform.SetParent(canvasGo.transform, false);
            _image = imgGo.AddComponent<Image>();
            _image.sprite = BuildSlimeSprite();
            _image.color = new Color(SlimeColor.r, SlimeColor.g, SlimeColor.b, 0f);
            _image.raycastTarget = false;

            var rt = _image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A handful of overlapping soft irregular blobs rather than one clean circle, so it reads as an organic splat instead of a UI vignette.</summary>
        private static Sprite BuildSlimeSprite()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SlimeSplat", wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(12345);
            var center = new Vector2(size * 0.5f, size * 0.5f);

            const int blobCount = 10;
            var blobCenters = new Vector2[blobCount];
            var blobRadii = new float[blobCount];
            for (int i = 0; i < blobCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = (float)rng.NextDouble() * size * 0.28f;
                blobCenters[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                blobRadii[i] = size * (0.16f + (float)rng.NextDouble() * 0.2f);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 0f;
                    var p = new Vector2(x, y);
                    for (int i = 0; i < blobCount; i++)
                    {
                        float d = Vector2.Distance(p, blobCenters[i]) / blobRadii[i];
                        float a = d >= 1f ? 0f : Mathf.Pow(Mathf.Cos(d * Mathf.PI * 0.5f), 1.3f);
                        if (a > alpha) alpha = a;
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Flashes the slime on, holds, then fades out. Safe to call repeatedly - restarts the fade each time rather than stacking.</summary>
        public void Splat()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(SplatRoutine());
        }

        private IEnumerator SplatRoutine()
        {
            float t = 0f;
            while (t < FadeInDuration)
            {
                t += Time.deltaTime;
                SetAlpha(Mathf.Lerp(0f, MaxOpacity, Mathf.Clamp01(t / Mathf.Max(0.0001f, FadeInDuration))));
                yield return null;
            }
            SetAlpha(MaxOpacity);

            yield return new WaitForSeconds(HoldDuration);

            t = 0f;
            while (t < FadeOutDuration)
            {
                t += Time.deltaTime;
                SetAlpha(Mathf.Lerp(MaxOpacity, 0f, Mathf.Clamp01(t / Mathf.Max(0.0001f, FadeOutDuration))));
                yield return null;
            }
            SetAlpha(0f);
            _routine = null;
        }

        private void SetAlpha(float a)
        {
            if (_image == null) return;
            Color c = _image.color;
            c.a = a;
            _image.color = c;
        }
    }
}
