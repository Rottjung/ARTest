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
        [Tooltip("Color of the vein-like streaks running through the slime - a distinct color from the base slime (default a dull blood red) so they read as a separate layer, like the red veins in an eyeball, rather than texture noise.")]
        public Color VeinColor = new Color(0.55f, 0.05f, 0.05f, 1f);
        [Range(0f, 1f)] public float VeinIntensity = 0.85f;
        [Tooltip("How many vein streaks branch in from the rim.")]
        public int VeinCount = 16;
        [Tooltip("0 = the original soft Gaussian falloff (blurred on top). 1 = crisp, cleanly-edged veins with no extra blur. Reshapes the per-vein falloff curve itself (a 'super-Gaussian' - still smooth, just steeper near the edge) AND dials out the post-blur pass, so both things that were making the veins soft move together off one slider.")]
        [Range(0f, 1f)] public float VeinSharpness = 0f;
        [Range(0f, 1f)] public float MaxOpacity = 0.85f;
        [Tooltip("How strongly the splat favors the screen edges over the center - 0 is the old even/centered blob cluster, 1 pushes coverage almost entirely to the rim, leaving the center mostly clear (like a frame rather than a full-screen splat).")]
        [Range(0f, 1f)] public float EdgeBias = 0.7f;
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
            // The texture now bakes its own per-pixel color (slime-to-vein blend), so
            // the Image's own color is just a global alpha dial for the fade in/out -
            // RGB stays white (a no-op multiplier) rather than tinting everything
            // uniformly with SlimeColor the way the old flat-white/alpha-only texture
            // needed it to.
            _image.color = new Color(1f, 1f, 1f, 0f);
            _image.raycastTarget = false;

            var rt = _image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Two layers baked into one texture: a soft blob mass (as before) pushed out
        /// toward the rim by EdgeBias so it reads as a frame around the view rather
        /// than a centered splat, plus a set of vein-like streaks that start at the
        /// rim and branch inward, thinning and fading as they go - the same idea as
        /// the red veins in an eyeball, radiating in from the edge rather than
        /// covering the middle. Not a static instance method any more (unlike the old
        /// version) because it now reads SlimeColor/VeinColor/EdgeBias etc. straight
        /// off this component, same reasoning as ProceduralClouds.BuildCloudTexture.
        /// </summary>
        private Sprite BuildSlimeSprite()
        {
            // 512, not the original 256 - at 256, this gets stretched full-screen on
            // device and the GPU's own bilinear upscale re-blurs every fine vein
            // regardless of what's baked in, which is why cranking VeinSharpness
            // toward 1 barely showed any difference: the resolution ceiling was
            // swamping it. Doubling gives the crisp end of the slider actual detail
            // to show.
            const int size = 512;
            float sizeScale = size / 256f; // keeps vein width/blur radius, tuned at 256, visually the same when size changes
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SlimeSplat", wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(12345);
            var center = new Vector2(size * 0.5f, size * 0.5f);
            float half = size * 0.5f;

            // --- Base slime mass: same overlapping-blob technique as before, but the
            // distance each blob is placed from center is biased outward (toward the
            // rim) as EdgeBias increases - r^p with p<1 skews a uniform 0..1 draw
            // toward 1 (the edge).
            const int blobCount = 12;
            float placementPow = Mathf.Lerp(1f, 0.3f, EdgeBias);
            var blobCenters = new Vector2[blobCount];
            var blobRadii = new float[blobCount];
            for (int i = 0; i < blobCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Pow((float)rng.NextDouble(), placementPow);
                float dist = r * half * 0.85f;
                blobCenters[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                blobRadii[i] = size * (0.16f + (float)rng.NextDouble() * 0.2f);
            }

            // --- Veins: each one starts right at the rim aimed inward, then takes a
            // handful of short, gently curving steps (a mild random turn each step)
            // so it wanders rather than running arrow-straight, tapering in width and
            // occasionally throwing off a thinner branch - then it stops well short of
            // the center. Rendered below as distance-to-segment with a soft (Gaussian,
            // not hard-edged) falloff, then box-blurred on top for the "not too sharp"
            // look the eyeball reference wants.
            var veinSegA = new System.Collections.Generic.List<Vector2>();
            var veinSegB = new System.Collections.Generic.List<Vector2>();
            var veinWidth = new System.Collections.Generic.List<float>();
            for (int v = 0; v < VeinCount; v++)
            {
                float startAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector2 outward = new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle));
                Vector2 pos = center + outward * (half * 0.98f);
                Vector2 dir = -outward;
                float width = Mathf.Lerp(2.5f, 5f, (float)rng.NextDouble()) * sizeScale;
                float maxReach = half * Mathf.Lerp(0.35f, 0.62f, (float)rng.NextDouble());
                int steps = 7 + rng.Next(0, 5);
                float traveled = 0f;

                for (int s = 0; s < steps && traveled < maxReach; s++)
                {
                    float stepLen = size * 0.055f * (0.6f + (float)rng.NextDouble() * 0.6f);
                    float turn = ((float)rng.NextDouble() - 0.5f) * 0.8f;
                    dir = RotateVector(dir, turn).normalized;
                    Vector2 next = pos + dir * stepLen;
                    float taper = Mathf.Lerp(1f, 0.25f, traveled / maxReach);
                    veinSegA.Add(pos); veinSegB.Add(next); veinWidth.Add(width * taper);

                    // Occasional thinner branch peeling off the main vein.
                    if (s > 1 && rng.NextDouble() < 0.22)
                    {
                        float branchTurn = ((float)rng.NextDouble() - 0.5f) * 1.6f;
                        Vector2 branchDir = RotateVector(dir, branchTurn).normalized;
                        Vector2 branchEnd = next + branchDir * stepLen * 0.75f;
                        veinSegA.Add(next); veinSegB.Add(branchEnd); veinWidth.Add(width * taper * 0.5f);
                    }

                    pos = next;
                    traveled += stepLen;
                }
            }

            var baseAlpha = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x, y);
                    int idx = y * size + x;

                    float a = 0f;
                    for (int i = 0; i < blobCount; i++)
                    {
                        float d = Vector2.Distance(p, blobCenters[i]) / blobRadii[i];
                        float ba = d >= 1f ? 0f : Mathf.Pow(Mathf.Cos(d * Mathf.PI * 0.5f), 1.3f);
                        if (ba > a) a = ba;
                    }
                    // Radial dampening: pushes coverage down near the center and up
                    // toward the rim, on top of the biased blob placement above - this
                    // is what actually guarantees "more on the edges" regardless of
                    // how the random blob placement happened to land.
                    float distNorm = Vector2.Distance(p, center) / half;
                    float radial = Mathf.Lerp(1f, Mathf.SmoothStep(0.05f, 0.9f, distNorm), EdgeBias);
                    baseAlpha[idx] = a * radial;
                }
            }

            // Veins rendered segment-by-segment into each segment's own local
            // bounding box, NOT the old pixel-major "every pixel checks every
            // segment" scan - that was the actual cause of a multi-second Awake()
            // freeze: ~176 segments (16 veins x ~9-11 steps/branches) x 512x512
            // pixels is ~46 million iterations, each with a Pow AND an Exp call
            // (the super-Gaussian falloff below) - 90m+ transcendental calls just to
            // build one splat texture. A segment's falloff is already negligible
            // (exp(-0.5*4^2) ~= 0.0003, and steeper still at higher VeinSharpness)
            // beyond a few widths of it, so pixels farther away than that can never
            // have contributed anything visible - scanning them was pure waste.
            // Rasterizing only each segment's own (width-padded) bounding box
            // instead touches a small fraction of the canvas per segment, typically
            // a 10-20x reduction in pixel-segment evaluations for these short, thin
            // veins, without changing the rendered result at all.
            var veinAlpha = new float[size * size];
            float falloffExponent = Mathf.Lerp(2f, 24f, VeinSharpness);
            for (int i = 0; i < veinSegA.Count; i++)
            {
                Vector2 segA = veinSegA[i];
                Vector2 segB = veinSegB[i];
                float w = veinWidth[i];
                float margin = w * 4f + 2f;
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(segA.x, segB.x) - margin));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(segA.x, segB.x) + margin));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(segA.y, segB.y) - margin));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(segA.y, segB.y) + margin));

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var p = new Vector2(x, y);
                        float d = DistancePointToSegment(p, segA, segB);
                        if (d > margin) continue;
                        // A "super-Gaussian": exp(-0.5*(d/w)^n). n=2 is exactly the
                        // plain Gaussian (soft) - the 0.5 matters, it's what makes
                        // VeinSharpness=0 reproduce the original falloff exactly
                        // rather than something already a bit tighter than it.
                        // Raising n toward the VeinSharpness-driven max makes the
                        // curve stay near 1 out to almost the full width, then drop
                        // steeply right at the edge - a crisp-but-still-antialiased
                        // line instead of a literal hard cutoff (which would alias
                        // badly even at 512px).
                        float falloff = Mathf.Exp(-0.5f * Mathf.Pow(d / Mathf.Max(w, 0.0001f), falloffExponent));
                        int idx = y * size + x;
                        veinAlpha[idx] = 1f - (1f - veinAlpha[idx]) * (1f - falloff); // screen-combine, stays soft
                    }
                }
            }

            // Blur the vein layer specifically - the segment-distance field on its own
            // has a slightly defined edge even with the Gaussian falloff; a couple of
            // cheap box-blur passes rounds that off into a softer look. Blended back
            // against the unblurred buffer by VeinSharpness so the slider sweeps
            // smoothly from fully blurred (0) to the raw, unblurred falloff (1),
            // rather than jumping between a couple of fixed blur radii.
            if (VeinSharpness < 1f)
            {
                int blurRadius = Mathf.Max(1, Mathf.RoundToInt(2 * sizeScale));
                var blurred = (float[])veinAlpha.Clone();
                BoxBlur(blurred, size, blurRadius);
                BoxBlur(blurred, size, blurRadius);
                for (int i = 0; i < veinAlpha.Length; i++)
                    veinAlpha[i] = Mathf.Lerp(blurred[i], veinAlpha[i], VeinSharpness);
            }

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float ba = baseAlpha[i];
                float va = Mathf.Clamp01(veinAlpha[i]) * VeinIntensity;
                float finalAlpha = 1f - (1f - ba) * (1f - va);
                Color c = Color.Lerp(SlimeColor, VeinColor, va);
                pixels[i] = new Color(c.r, c.g, c.b, finalAlpha);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Vector2 RotateVector(Vector2 v, float radians)
        {
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
            Vector2 closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }

        /// <summary>Simple in-place box blur (separable, single-channel) - used to soften the vein layer. radius=2 samples a 5x5 neighborhood.</summary>
        private static void BoxBlur(float[] buffer, int size, int radius)
        {
            var tmp = new float[buffer.Length];
            // Horizontal pass
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f; int count = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = x + k;
                        if (sx < 0 || sx >= size) continue;
                        sum += buffer[y * size + sx]; count++;
                    }
                    tmp[y * size + x] = sum / count;
                }
            }
            // Vertical pass
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f; int count = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = y + k;
                        if (sy < 0 || sy >= size) continue;
                        sum += tmp[sy * size + x]; count++;
                    }
                    buffer[y * size + x] = sum / count;
                }
            }
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
