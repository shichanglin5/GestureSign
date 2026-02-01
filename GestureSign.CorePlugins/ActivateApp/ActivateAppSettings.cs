using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Applications;
using Newtonsoft.Json;

namespace GestureSign.CorePlugins.ActivateApp
{
    /// <summary>
    /// Settings for the Activate Application Window plugin
    /// </summary>
    public class ActivateAppSettings
    {
        #region Public Properties

        /// <summary>
        /// Window matching rule (includes Name, ApplicationPath, Conditions)
        /// </summary>
        public WindowRule WindowRule { get; set; }

        /// <summary>
        /// Referenced preset ID (if using a preset rule)
        /// When set, WindowRule is a copy of the preset at the time of configuration
        /// </summary>
        public string PresetId { get; set; }

        /// <summary>
        /// Command line arguments for launching the application
        /// </summary>
        public string ApplicationArguments { get; set; }

        /// <summary>
        /// Window list cache expiration time in seconds
        /// Set to ≤ 0 to disable caching and re-scan every time
        /// Set to > 0 to cache for the specified number of seconds
        /// Default: 30 seconds (suitable for most scenarios)
        /// Note: This caches the list of matching window handles, not individual window properties
        /// </summary>
        public int CacheExpirationSeconds { get; set; } = 30;

        /// <summary>
        /// Minimize the window if it is already activated (foreground)
        /// Default: true (minimize when clicking on an already active window)
        /// </summary>
        public bool MinimizeIfActivated { get; set; } = true;

        /// <summary>
        /// Track the last activated window handle for multi-window cycling
        /// (Note: This is runtime state, not persisted)
        /// </summary>
        [JsonIgnore]
        public IntPtr LastActivatedWindow { get; set; }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Check if settings have valid matching configuration
        /// Valid if: WindowRule has conditions OR has ApplicationPath (for fallback matching)
        /// </summary>
        [JsonIgnore]
        public bool HasValidConditions =>
            WindowRule != null &&
            ((WindowRule.Conditions != null && WindowRule.Conditions.Count > 0) ||
             !string.IsNullOrEmpty(WindowRule.ApplicationPath));

        /// <summary>
        /// Get AUMID from WindowRule.Conditions (for PWA/UWP app launching)
        /// </summary>
        [JsonIgnore]
        public string AUMID =>
            WindowRule?.Conditions?.FirstOrDefault(c => c.Type == MatchConditionType.AUMID)?.Value;

        /// <summary>
        /// Generate cache key from matching conditions and ApplicationPath
        /// </summary>
        public string GenerateCacheKey()
        {
            if (WindowRule == null)
                return string.Empty;

            var parts = new List<string>();

            // Include ApplicationPath in cache key (used for fallback matching)
            if (!string.IsNullOrEmpty(WindowRule.ApplicationPath))
            {
                parts.Add($"AppPath:{WindowRule.ApplicationPath}");
            }

            if (WindowRule.Conditions != null)
            {
                foreach (var condition in WindowRule.Conditions)
                {
                    parts.Add($"{condition.Type}:{condition.Value}:{condition.IsRegex}");
                }
            }

            return parts.Count > 0 ? string.Join("|", parts) : string.Empty;
        }

        #endregion
    }
}
