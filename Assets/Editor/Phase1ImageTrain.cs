using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Headless-capable equivalent of Zappar/Editor/Open Image Trainer for a flat,
    /// non-curved, default-scale target — good enough for a "does tracking + content
    /// actually work" rehearsal test. Not a substitute for training the real,
    /// physically-scaled facade target later (that needs a physical-size pass through
    /// the real Image Trainer window once the QR is actually mounted on site).
    /// </summary>
    public static class Phase1ImageTrain
    {
        /// <summary>
        /// Trains an image into a flat .zpt. Returns the output path, or null on failure.
        /// If physicalWidthMeters is given, calibrates the tracked space to real-world
        /// scale (1 Unity unit = 1 meter) using the same "half the physical height"
        /// convention as Zappar's own Image Trainer window. Leave null for the
        /// SDK's default/unspecified (arbitrary, non-metric) scale.
        /// </summary>
        public static string TrainImage(string imagePath, int maxTrainSize = 512, float? physicalWidthMeters = null)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                Debug.LogError("[Phase1ImageTrain] Missing or invalid image path: " + imagePath);
                return null;
            }

            byte[] data = File.ReadAllBytes(imagePath);
            string ext = Path.GetExtension(imagePath).ToLower();
            bool isJpg = ext == ".jpg" || ext == ".jpeg";

            float physicalScaleFactor = -1f;
            if (physicalWidthMeters.HasValue)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    float aspect = tex.height / (float)tex.width;
                    float physicalHeightMeters = physicalWidthMeters.Value * aspect;
                    physicalScaleFactor = 0.5f * physicalHeightMeters;
                    Debug.Log($"[Phase1ImageTrain] Physical calibration: width={physicalWidthMeters.Value}m, " +
                        $"derived height={physicalHeightMeters:F2}m (aspect {aspect:F3}), scaleFactor={physicalScaleFactor:F3}");
                }
                else
                {
                    Debug.LogError("[Phase1ImageTrain] Could not read image dimensions for physical calibration - training at default scale instead.");
                }
                Object.DestroyImmediate(tex);
            }

            var src = new Z.FileData { length = data.Length };
            src.data = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, src.data, data.Length);

            var preview = new Z.FileData { data = src.data, length = src.length };
            var zpt = new Z.FileData();

            int success = Z.TrainImageCompressedWithMaxCurved(
                ref src, ref zpt, ref preview,
                isJpg ? 1 : 0,
                maxTrainSize, maxTrainSize,
                -1f, -1f, -1f, // top/bottom radius, side length -> flat target
                physicalScaleFactor);

            string outPath = null;
            if (success == 1 && zpt.data != null && zpt.length > 0)
            {
                byte[] zdata = new byte[zpt.length];
                Marshal.Copy(zpt.data, zdata, 0, zpt.length);

                string outName = Path.GetFileNameWithoutExtension(imagePath) + ".zpt";
                outPath = Path.Combine(Application.streamingAssetsPath, outName);
                File.WriteAllBytes(outPath, zdata);
                Debug.Log("[Phase1ImageTrain] Trained OK -> " + outPath);

                Z.TrainImageFreeFileData(ref zpt);
            }
            else
            {
                Debug.LogError("[Phase1ImageTrain] Training failed for " + imagePath);
            }

            Marshal.FreeHGlobal(src.data);
            return outPath;
        }

        // CLI entry point for batch-mode use: -executeMethod ...TrainRehearsalTarget -imagePath "<path>"
        public static void TrainRehearsalTarget()
        {
            string imagePath = GetArg("-imagePath");
            TrainImage(imagePath);
        }

        private static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
