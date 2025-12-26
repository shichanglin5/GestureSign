namespace GestureSign.CorePlugins.SetTheme
{
    public class SetThemeSettings
    {
        #region Public Properties

        /// <summary>
        /// Toggle app theme (AppsUseLightTheme)
        /// </summary>
        public bool ToggleAppTheme { get; set; }

        /// <summary>
        /// Toggle Windows/system theme (SystemUsesLightTheme)
        /// </summary>
        public bool ToggleSystemTheme { get; set; }

        #endregion

        public SetThemeSettings()
        {
            // Default: toggle both
            ToggleAppTheme = true;
            ToggleSystemTheme = true;
        }
    }
}
