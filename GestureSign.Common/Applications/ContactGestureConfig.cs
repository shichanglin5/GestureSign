using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;

namespace GestureSign.Common.Applications
{
    [Serializable]
    public class ContactGestureSettings
    {
        private List<TapGestureConfig> _taps = new List<TapGestureConfig>();
        private List<TipTapGestureConfig> _tipTaps = new List<TipTapGestureConfig>();
        private List<ClickGestureConfig> _clicks = new List<ClickGestureConfig>();

        public bool Enabled { get; set; } = true;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<TapGestureConfig> Taps
        {
            get => _taps ??= new List<TapGestureConfig>();
            set => _taps = value ?? new List<TapGestureConfig>();
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<TipTapGestureConfig> TipTaps
        {
            get => _tipTaps ??= new List<TipTapGestureConfig>();
            set => _tipTaps = value ?? new List<TipTapGestureConfig>();
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<ClickGestureConfig> Clicks
        {
            get => _clicks ??= new List<ClickGestureConfig>();
            set => _clicks = value ?? new List<ClickGestureConfig>();
        }

        public bool ShouldSerialize()
        {
            return !Enabled || Taps.Count > 0 || TipTaps.Count > 0 || Clicks.Count > 0;
        }
    }

    public enum ContactGestureDirection
    {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 3,
    }

    [Serializable]
    public class TapGestureRecognition
    {
        public const int DefaultMaxDurationMs = 300;
        public const double DefaultMaxMovementPx = 18;

        public int MaxDurationMs { get; set; } = DefaultMaxDurationMs;
        public double MaxMovementPx { get; set; } = DefaultMaxMovementPx;
        public int MinFingerCount { get; set; } = 2;
    }

    [Serializable]
    public class ClickGestureRecognition
    {
        public const int DefaultMaxPressDurationMs = 600;
        public const double DefaultMaxMovementPx = 18;

        public int MaxPressDurationMs { get; set; } = DefaultMaxPressDurationMs;
        public double MaxMovementPx { get; set; } = DefaultMaxMovementPx;
        public int MinFingerCount { get; set; } = 2;
    }

    [Serializable]
    public class TipTapRecognition
    {
        public const int DefaultFixMinHoldMs = 50;
        public const int DefaultMaxTapDurationMs = 150;
        public const double DefaultFixStillThresholdPx = 12;
        public const double DefaultTapMaxMovementPx = 18;
        public const double DefaultDirectionDeadzonePx = 24;
        public const int DefaultRepeatCooldownMs = 60;

        public int FixMinHoldMs { get; set; } = DefaultFixMinHoldMs;
        public int MaxTapDurationMs { get; set; } = DefaultMaxTapDurationMs;
        public double FixStillThresholdPx { get; set; } = DefaultFixStillThresholdPx;
        public double TapMaxMovementPx { get; set; } = DefaultTapMaxMovementPx;
        public double DirectionDeadzonePx { get; set; } = DefaultDirectionDeadzonePx;
        public int RepeatCooldownMs { get; set; } = DefaultRepeatCooldownMs;
    }

    [Serializable]
    public class TapGestureConfig
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool IsEnabled { get; set; } = true;

        public int FingerCount { get; set; } = 2;

        public TapGestureRecognition Recognition { get; set; } = new TapGestureRecognition();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public GestureModifiers Modifiers { get; set; } = GestureModifiers.Default;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Command> Commands { get; set; } = new List<Command>();
    }

    [Serializable]
    public class TipTapGestureConfig
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool IsEnabled { get; set; } = true;

        public int FingerCount { get; set; } = 2;

        [DefaultValue(1)]
        public int FixFingerCount { get; set; } = 1;

        public ContactGestureDirection Direction { get; set; } = ContactGestureDirection.None;

        public TipTapRecognition Recognition { get; set; } = new TipTapRecognition();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public GestureModifiers Modifiers { get; set; } = GestureModifiers.Default;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Command> Commands { get; set; } = new List<Command>();
    }

    [Serializable]
    public class ClickGestureConfig
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool IsEnabled { get; set; } = true;

        public int FingerCount { get; set; } = 2;

        public ClickGestureRecognition Recognition { get; set; } = new ClickGestureRecognition();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Command> Commands { get; set; } = new List<Command>();
    }
}

