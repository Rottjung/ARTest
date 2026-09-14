using System;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Points a Directional Light at the real sun's actual position for a
    /// given real-world latitude/longitude and time, so the virtual
    /// tentacle/building shadows read as consistent with the real sun a
    /// viewer sees outside during an on-site demo.
    ///
    /// THIS IS NOT A BUILT-IN UNITY FEATURE for the render pipeline this
    /// project uses - "sync a light with the real sun by location/time" IS
    /// a real, existing Unity capability, but only as HDRP's "Physically
    /// Based Sky" Volume component (High Definition Render Pipeline only).
    /// This project is on URP (confirmed throughout - the whole camera
    /// Base/Overlay stacking setup elsewhere in this codebase is URP-
    /// specific), which has no equivalent built-in. So this script
    /// implements the actual astronomical calculation directly instead -
    /// the standard NOAA Solar Position Algorithm (the same well-known
    /// formula set behind most "real sun" implementations, games included,
    /// accurate to a fraction of a degree - overkill precision for
    /// consistent-looking shadows, which is all this needs to achieve).
    ///
    /// WHY UTC, NOT A TIMEZONE: solar position genuinely only depends on
    /// UTC time and longitude - never on which civil timezone/DST rule a
    /// place has been assigned. Using DateTime.UtcNow directly (which on
    /// WebGL reads the visitor's own device clock, converted to UTC by
    /// .NET/Mono under the hood) sidesteps ANY need to hardcode Central
    /// European (Summer) Time or its DST transition dates - it's simply
    /// correct year-round without that bookkeeping.
    ///
    /// THE ONE THING THIS SCRIPT CANNOT KNOW ON ITS OWN: which way is
    /// geographic North in this Unity scene's own coordinate system - that
    /// depends entirely on how this project's content was originally
    /// authored/oriented relative to the real building, which only
    /// whoever built that content knows. NorthOffsetDegrees exists
    /// specifically for this - see its own tooltip for how to calibrate it
    /// on-site.
    /// </summary>
    public class RealWorldSunLight : MonoBehaviour
    {
        [Tooltip("The Directional Light to point at the real sun - auto-found in the scene if left blank (there should only ever be one).")]
        public Light DirectionalLight;

        [Header("Location - Mercedes Platz / UCI Luxe, Berlin")]
        [Tooltip("Degrees, positive = North. General Berlin coordinates - precise enough for this purpose, since a few km of position difference across a city changes the sun's angle by a negligible fraction of a degree. Refine only if you have the exact site coordinates handy (e.g. from Google Maps).")]
        public double LatitudeDegrees = 52.52;
        [Tooltip("Degrees, positive = East.")]
        public double LongitudeDegrees = 13.405;

        [Header("Orientation calibration")]
        [Tooltip("Degrees to add to the computed compass azimuth before applying it as this scene's Y rotation - THE key calibration value, since Unity has no inherent idea which of this scene's own axes is geographic North. To calibrate on-site: at a known moment, compare the light's rendered direction against the real sun's actual direction (or use a phone compass to note true North's bearing relative to the tracked content's own forward axis), then adjust this offset until they visually match. Defaults to 0 (assumes scene +Z already faces North) purely as a starting point - almost certainly needs a real on-site adjustment.")]
        public float NorthOffsetDegrees = 0f;

        [Header("Time source")]
        [Tooltip("Live real-world time (DateTime.UtcNow - the visitor's own device clock) if checked. Uncheck to preview a specific fixed date/time instead (see OverrideUtcDateTime below) - useful for testing 'what will this look like at 3pm' without waiting for 3pm.")]
        public bool UseRealTime = true;
        [Tooltip("Only used when Use Real Time is unchecked - entered as UTC, NOT Berlin local time (see this class's own doc comment for why UTC, not a timezone, is what the actual sun-position math needs).")]
        public string OverrideUtcDateTime = "2026-09-14T14:00:00Z";

        [Tooltip("How often (real seconds) to recompute the sun's position - it moves slowly enough that recomputing every frame would be wasted work for an imperceptible visual difference.")]
        public float UpdateIntervalSeconds = 30f;

        /// <summary>Sun elevation above the horizon, in degrees - negative means the sun is below the horizon (night). Exposed for diagnostics.</summary>
        public float LastElevationDegrees { get; private set; }
        /// <summary>Sun compass azimuth (0=North, 90=East, 180=South, 270=West), in degrees, BEFORE NorthOffsetDegrees is applied. Exposed for diagnostics.</summary>
        public float LastAzimuthDegrees { get; private set; }

        private float _timeSinceUpdate;

        private void Awake()
        {
            if (DirectionalLight == null) DirectionalLight = FindFirstObjectByType<Light>();
        }

        private void Start()
        {
            Recompute();
        }

        private void Update()
        {
            _timeSinceUpdate += Time.deltaTime;
            if (_timeSinceUpdate < UpdateIntervalSeconds) return;
            _timeSinceUpdate = 0f;
            Recompute();
        }

        [ContextMenu("Recompute Now")]
        public void Recompute()
        {
            if (DirectionalLight == null)
            {
                Debug.LogError("[RealWorldSunLight] No Directional Light assigned/found.");
                return;
            }

            DateTime utc;
            if (UseRealTime)
            {
                utc = DateTime.UtcNow;
            }
            else if (!DateTime.TryParse(OverrideUtcDateTime, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out utc))
            {
                Debug.LogError("[RealWorldSunLight] Could not parse OverrideUtcDateTime '" + OverrideUtcDateTime + "' - expected an ISO 8601 format like '2026-09-14T14:00:00Z'.");
                return;
            }

            ComputeSunPosition(utc, LatitudeDegrees, LongitudeDegrees, out float elevation, out float azimuth);
            LastElevationDegrees = elevation;
            LastAzimuthDegrees = azimuth;

            // A directional light's own forward axis (+Z) is the direction light
            // TRAVELS - i.e. FROM the sun's position DOWN toward the ground - so
            // its pitch (X rotation) is how far DOWN from straight-ahead the sun
            // dips below/rises above the horizon: 90 degrees elevation (sun
            // straight overhead) needs the light pointing straight down (X=90);
            // 0 degrees elevation (sun on the horizon) needs it pointing level
            // (X=0).
            float pitch = elevation;
            float yaw = azimuth + NorthOffsetDegrees;
            DirectionalLight.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Standard NOAA Solar Position Algorithm (the same formula set
        /// behind most "real sun" implementations) - computes the sun's
        /// elevation (degrees above the horizon, negative = below/night)
        /// and azimuth (compass degrees, 0=North/90=East/180=South/270=West)
        /// for a given UTC instant and location. Accurate to a fraction of a
        /// degree - far more precision than shadow-consistency needs, but
        /// it's the standard reference algorithm, not worth simplifying
        /// further and risking a subtle formula mistake.
        /// </summary>
        public static void ComputeSunPosition(DateTime utc, double latDeg, double lonDeg, out float elevationDeg, out float azimuthDeg)
        {
            double lat = latDeg * Math.PI / 180.0;

            double jd = ToJulianDay(utc);
            double t = (jd - 2451545.0) / 36525.0; // Julian centuries since J2000.0

            double L0 = NormalizeDegrees(280.46646 + t * (36000.76983 + t * 0.0003032));
            double M = NormalizeDegrees(357.52911 + t * (35999.05029 - 0.0001537 * t));
            double Mrad = M * Math.PI / 180.0;
            double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

            double C = Math.Sin(Mrad) * (1.914602 - t * (0.004817 + 0.000014 * t))
                     + Math.Sin(2 * Mrad) * (0.019993 - 0.000101 * t)
                     + Math.Sin(3 * Mrad) * 0.000289;

            double trueLong = L0 + C;
            double omega = 125.04 - 1934.136 * t;
            double appLong = trueLong - 0.00569 - 0.00478 * Math.Sin(omega * Math.PI / 180.0);

            double meanObliq = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
            double obliqCorr = meanObliq + 0.00256 * Math.Cos(omega * Math.PI / 180.0);

            double appLongRad = appLong * Math.PI / 180.0;
            double obliqCorrRad = obliqCorr * Math.PI / 180.0;
            double decl = Math.Asin(Math.Sin(obliqCorrRad) * Math.Sin(appLongRad)); // radians

            // Equation of time, in minutes.
            double yTerm = Math.Tan(obliqCorrRad / 2.0);
            yTerm *= yTerm;
            double L0rad = L0 * Math.PI / 180.0;
            double eqTime = 4.0 * (180.0 / Math.PI) * (
                yTerm * Math.Sin(2 * L0rad)
                - 2 * e * Math.Sin(Mrad)
                + 4 * e * yTerm * Math.Sin(Mrad) * Math.Cos(2 * L0rad)
                - 0.5 * yTerm * yTerm * Math.Sin(4 * L0rad)
                - 1.25 * e * e * Math.Sin(2 * Mrad));

            double utcMinutes = utc.Hour * 60.0 + utc.Minute + utc.Second / 60.0;
            double trueSolarTime = (utcMinutes + eqTime + 4.0 * lonDeg) % 1440.0;
            if (trueSolarTime < 0) trueSolarTime += 1440.0;

            double hourAngle = trueSolarTime / 4.0 - 180.0; // degrees
            double haRad = hourAngle * Math.PI / 180.0;

            double zenithCos = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(haRad);
            zenithCos = ClampUnit(zenithCos);
            double zenith = Math.Acos(zenithCos); // radians

            elevationDeg = (float)(90.0 - zenith * 180.0 / Math.PI);

            double azDenom = Math.Cos(lat) * Math.Sin(zenith);
            double azimuth;
            if (Math.Abs(azDenom) < 1e-6)
            {
                azimuth = hourAngle > 0 ? 180.0 : 0.0; // sun near zenith - azimuth undefined, arbitrary but harmless
            }
            else
            {
                double azCos = (Math.Sin(lat) * Math.Cos(zenith) - Math.Sin(decl)) / azDenom;
                azCos = ClampUnit(azCos);
                azimuth = Math.Acos(azCos) * 180.0 / Math.PI;
                if (hourAngle > 0) azimuth = 360.0 - azimuth;
            }
            azimuthDeg = (float)azimuth;
        }

        private static double ToJulianDay(DateTime utc)
        {
            // Standard Julian Day from a Gregorian-calendar UTC DateTime.
            int y = utc.Year, m = utc.Month;
            double d = utc.Day + (utc.Hour + (utc.Minute + utc.Second / 60.0) / 60.0) / 24.0;
            if (m <= 2) { y -= 1; m += 12; }
            int a = y / 100;
            int b = 2 - a + a / 4;
            return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
        }

        private static double NormalizeDegrees(double deg)
        {
            deg %= 360.0;
            if (deg < 0) deg += 360.0;
            return deg;
        }

        /// <summary>Manual clamp to [-1, 1] instead of System.Math.Clamp - avoids depending on a .NET API version this project hasn't otherwise relied on, given past WebGL/IL2CPP compatibility headaches elsewhere in this project.</summary>
        private static double ClampUnit(double v)
        {
            if (v < -1.0) return -1.0;
            if (v > 1.0) return 1.0;
            return v;
        }
    }
}
