using System;
using System.Collections.Generic;
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
        /// Display name for UI
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Application path for launching when no matching window found
        /// </summary>
        public string ApplicationPath { get; set; }

        /// <summary>
        /// Matching conditions list (ClassName, Title, ProcessName, ProcessPath, AUMID)
        /// All conditions must match (AND logic)
        /// </summary>
        public List<MatchCondition> MatchConditions { get; set; }

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
        /// Valid if: has MatchConditions OR has ApplicationPath (for fallback matching)
        /// </summary>
        [JsonIgnore]
        public bool HasValidConditions =>
            (MatchConditions != null && MatchConditions.Count > 0) ||
            !string.IsNullOrEmpty(ApplicationPath);

        /// <summary>
        /// Generate cache key from matching conditions and ApplicationPath
        /// </summary>
        public string GenerateCacheKey()
        {
            var parts = new List<string>();

            // Include ApplicationPath in cache key (used for fallback matching)
            if (!string.IsNullOrEmpty(ApplicationPath))
            {
                parts.Add($"AppPath:{ApplicationPath}");
            }

            if (MatchConditions != null)
            {
                foreach (var condition in MatchConditions)
                {
                    parts.Add($"{condition.Type}:{condition.Value}:{condition.IsRegex}");
                }
            }

            return parts.Count > 0 ? string.Join("|", parts) : string.Empty;
        }

        #endregion
    }
}
