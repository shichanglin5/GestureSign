using System;
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
        /// Track the last activated window handle for multi-window cycling
        /// (Note: This is runtime state, not persisted)
        /// </summary>
        [JsonIgnore]
        public IntPtr LastActivatedWindow { get; set; }

        #endregion
    }
}
