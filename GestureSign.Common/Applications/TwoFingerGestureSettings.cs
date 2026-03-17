using Newtonsoft.Json;

namespace GestureSign.Common.Applications
{
    public enum InheritSwitch
    {
        Inherit = 0,
        Enabled = 1,
        Disabled = 2,
    }

    public class TwoFingerZoomSettings
    {
        public double ZoomSpeed { get; set; } = 1.0;
        public double ZoomSensitivity { get; set; } = 1.0;

        public bool IsDefault()
        {
            return ZoomSpeed == 1.0 && ZoomSensitivity == 1.0;
        }
    }

    public class TwoFingerGestureSettings
    {
        public InheritSwitch Scroll { get; set; } = InheritSwitch.Inherit;
        public InheritSwitch Zoom { get; set; } = InheritSwitch.Inherit;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public InertialScrollSettings ScrollSettings { get; set; } = new InertialScrollSettings();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public TwoFingerZoomSettings ZoomSettings { get; set; } = new TwoFingerZoomSettings();

        public bool ShouldSerialize()
        {
            return Scroll != InheritSwitch.Inherit ||
                   Zoom != InheritSwitch.Inherit ||
                   !IsDefault(ScrollSettings) ||
                   !(ZoomSettings?.IsDefault() ?? true);
        }

        private static bool IsDefault(InertialScrollSettings settings)
        {
            if (settings == null) return true;

            return settings.Direction == ScrollDirection.Both &&
                   settings.PixelsPerScrollUnit == 150.0 &&
                   settings.AccelerationFactor == 1.5 &&
                   !settings.ReverseDirection &&
                   !settings.ReverseHorizontalDirection &&
                   settings.NoiseRatio == 0.25 &&
                   settings.LockExitMinorDistancePx == 6.0 &&
                   settings.RelockRatioMultiplier == 0.8 &&
                   settings.EnableWinUIDetection &&
                   settings.WinUIScrollMultiplier == 3.0 &&
                   settings.EnableMomentum &&
                   settings.MomentumTimeConstantMs == 600.0 &&
                   settings.MomentumMinVelocity == 10.0 &&
                   settings.MomentumMaxDurationMs == 1500.0 &&
                   settings.MomentumTickMs == 8.0;
        }
    }
}
