using System;
using System.Collections.Generic;
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
        /// Full path to the application executable
        /// </summary>
        public string ApplicationPath { get; set; }

        /// <summary>
        /// Process name (derived from path, used for matching)
        /// </summary>
        public string ProcessName { get; set; }

        /// <summary>
        /// Display name for UI
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Window class name filter (optional, for precise matching)
        /// </summary>
        public string WindowClassName { get; set; }

        /// <summary>
        /// Window title pattern (optional, for precise matching)
        /// </summary>
        public string WindowTitlePattern { get; set; }

        /// <summary>
        /// Whether to use regex for title matching
        /// </summary>
        public bool UseRegexMatching { get; set; }

        /// <summary>
        /// Application User Model ID (AUMID) for precise matching
        /// When set, this takes priority over process name/path matching
        /// Used to distinguish apps with the same executable (e.g., Edge browser vs Edge PWAs)
        /// </summary>
        public string AUMID { get; set; }

        /// <summary>
        /// Window cache expiration time in seconds
        /// Set to ≤ 0 to disable caching and re-scan every time
        /// Set to > 0 to cache for the specified number of seconds
        /// Default: 5 seconds (suitable for most scenarios)
        /// Recommendations:
        ///   - Stable apps (like browsers): 30-60 seconds
        ///   - Frequently changing apps: 1-3 seconds
        ///   - Testing/debugging: 0 (disable caching)
        /// </summary>
        public int CacheExpirationSeconds { get; set; } = 5;

        /// <summary>
        /// Track the last activated window handle for multi-window cycling
        /// (Note: This is runtime state, not persisted)
        /// </summary>
        [JsonIgnore]
        public IntPtr LastActivatedWindow { get; set; }

        #endregion
    }
}
