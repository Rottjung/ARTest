using System.IO;
using UnityEditor;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Retrains kiosk_cropped.jpg at 1024px (vs Zappar's 512px default) to test whether
    /// downsample-blur is why tracking broke - this crop is dense with small text/icons
    /// that could get lost at 512. Overwrites kiosk_cropped.zpt in place, so no scene
    /// changes needed (Target already points at it).
    /// </summary>
    public static class RetrainHighRes
    {
        [MenuItem("ARReveal/Building Test/Retrain kiosk_cropped at 1024px")]
        public static void Run()
        {
            string fullImagePath = Path.GetFullPath("Assets/images/kiosk_cropped.jpg");
            Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024);
        }
    }
}
