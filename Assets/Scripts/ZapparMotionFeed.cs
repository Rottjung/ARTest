using UnityEngine;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// Feeds the device's own accelerometer/gyroscope into Zappar's native tracking
    /// pipeline every frame - added after finding this project's SLAM/depth-tracking
    /// pipeline was very likely running on VISION-ONLY data this whole time, with
    /// zero motion-sensor fusion, which would plausibly explain the "walks meters,
    /// only registers as decimeters" scale problem chased across several sessions
    /// (see HandoffToInstantTracking's own doc comment for that history).
    ///
    /// How this was found: Zappar's native pipeline exposes real, callable methods
    /// for exactly this - Z.PipelineMotionAccelerometerSubmit /
    /// PipelineMotionRotationRateSubmit / PipelineMotionAttitudeSubmit /
    /// PipelineMotionAttitudeMatrixSubmit (confirmed in Z.cs, which P/Invokes into
    /// zappar_pipeline_motion_*_submit - confirmed present in the WebGL native
    /// plugin bridge too, Plugins/WebGL/zcv.jslib). Grepped this ENTIRE project,
    /// package included: NONE of these were ever called anywhere. Then read the
    /// actual zappar-cv.js library this project loads (v2.1.9, from the CDN URL in
    /// the WebGL template) directly rather than guessing - it contains no
    /// "devicemotion"/"deviceorientation" listener registration and never calls
    /// DeviceMotionEvent.requestPermission() itself, meaning the library does NOT
    /// capture motion sensors on its own - by its own design, something else has to
    /// capture browser motion events and push the values in via these exact
    /// functions. Nothing else in this project ever did. Official Zappar docs
    /// separately confirm Instant World Tracking "requires additional device
    /// sensors" (why it's unsupported in Editor PlayMode) - consistent with motion
    /// data being a real, expected input to this tracking mode, not an optional
    /// extra.
    ///
    /// UNVERIFIED, deliberately scoped down, both flagged clearly because this is
    /// genuinely new territory with no reference usage anywhere in the SDK to
    /// confirm against (unlike this project's other fixes, which all had Zappar's
    /// own reference code to check the fix against):
    ///  - Only accelerometer + gyroscope rotation-rate are submitted, NOT device
    ///    attitude (PipelineMotionAttitudeSubmit/AttitudeMatrixSubmit) - Unity's
    ///    Input.gyro.attitude is already remapped into Unity's own coordinate
    ///    convention, and whether that matches what the native pipeline expects
    ///    (likely raw platform-native, e.g. iOS CoreMotion's own convention) is a
    ///    real open question this project has no way to confirm without a device.
    ///    Getting that wrong risks doing more harm than good, so it's left out;
    ///    accelerometer + rotation rate alone should already restore SOME of the
    ///    IMU fusion this pipeline was designed to use.
    ///  - Timestamp uses Time.timeAsDouble (seconds since startup, monotonic) -
    ///    a reasonable-seeming default for a "double time" parameter, not confirmed
    ///    against any real usage since none exists anywhere to check against.
    ///  - Units: Unity's Input.acceleration reports in g (9.81 m/s^2 = 1g) and
    ///    Input.gyro.rotationRate in radians/second - both happen to match Apple's
    ///    CoreMotion conventions exactly (CMAccelerometerData.acceleration is also
    ///    in g, CMRotationRate also rad/s), which is a good sign this is likely the
    ///    right convention, but still unconfirmed for what THIS pipeline expects.
    /// Bottom line: this is a well-reasoned attempt at closing a real, concretely
    /// confirmed gap (motion data was never submitted, full stop) - not a confirmed
    /// fix. Needs a real on-device test to know if it actually improves tracking
    /// scale/quality, same as everything else about depth tracking in this project.
    /// </summary>
    public class ZapparMotionFeed : MonoBehaviour
    {
        [Tooltip("Off disables submission entirely (e.g. to A/B test whether this is actually helping or hurting on a real device) without removing the component.")]
        public bool Enabled = true;

        private bool _gyroEnabled;

        private void Start()
        {
            _gyroEnabled = SystemInfo.supportsGyroscope;
            if (_gyroEnabled) Input.gyro.enabled = true;
        }

        private void Update()
        {
            if (!Enabled) return;
            var cam = ZapparCamera.Instance;
            // PipelineIsInitialized - not just CameraSourceInitialized - since the
            // pipeline handle itself (GetPipeline) is what these submit calls need,
            // and it exists as soon as the pipeline is created, before the camera
            // source necessarily has started.
            if (cam == null || !cam.PipelineIsInitialized) return;

            System.IntPtr pipeline = cam.GetPipeline;
            double time = Time.timeAsDouble;

            Vector3 acc = Input.acceleration;
            Z.PipelineMotionAccelerometerSubmit(pipeline, time, acc.x, acc.y, acc.z);

            if (_gyroEnabled)
            {
                Vector3 rotRate = Input.gyro.rotationRate;
                Z.PipelineMotionRotationRateSubmit(pipeline, time, rotRate.x, rotRate.y, rotRate.z);
            }
        }
    }
}
