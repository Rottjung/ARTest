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
        /// <summary>Trains an image into a flat .zpt at default scale. Returns the output path, or null on failure.</summary>
        public static string TrainImage(string imagePath, int maxTrainSize = 512)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                Debug.LogError("[Phase1ImageTrain] Missing or invalid image path: " + imagePath);
                return null;
            }

            byte[] data = File.ReadAllBytes(imagePath);
            string ext = Path.GetExtension(imagePath).ToLower();
            bool isJpg = ext == ".jpg" || ext == ".jpeg";

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
                -1f);          // physical scale factor -> default/unspecified

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
