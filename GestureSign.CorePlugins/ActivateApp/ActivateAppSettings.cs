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
        /// Matching conditions list (ClassName, Title, ProcessName, ProcessPath, AUMID)
        /// All conditions must match (AND logic)
        /// </summary>
        public List<MatchCondition> MatchConditions { get; set; }

        /// <summary>
        /// Window list cache expiration time in seconds
        /// Set to ≤ 0 to disable caching and re-scan every time
        /// Set to > 0 to cache for the specified number of seconds
        /// Default: 5 seconds (suitable for most scenarios)
        /// Note: This caches the list of matching window handles, not individual window properties
        /// </summary>
        public int CacheExpirationSeconds { get; set; } = 5;

        /// <summary>
        /// Track the last activated window handle for multi-window cycling
        /// (Note: This is runtime state, not persisted)
        /// </summary>
        [JsonIgnore]
        public IntPtr LastActivatedWindow { get; set; }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Check if settings have valid matching conditions
        /// </summary>
        [JsonIgnore]
        public bool HasValidConditions => MatchConditions != null && MatchConditions.Count > 0;

        /// <summary>
        /// Generate cache key from matching conditions
        /// </summary>
        public string GenerateCacheKey()
        {
            if (MatchConditions == null || MatchConditions.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (var condition in MatchConditions)
            {
                parts.Add($"{condition.Type}:{condition.Value}:{condition.IsRegex}");
            }
            return string.Join("|", parts);
        }

        #endregion
    }
}
