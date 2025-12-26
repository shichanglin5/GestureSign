using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace GestureSign.CorePlugins.SetTheme
{
    public class SetThemePlugin : IPlugin
    {
        #region Private Variables

        private SetThemeUI _GUI = null;
        private SetThemeSettings _settings = null;

        #endregion

        #region PInvoke

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        private const int HWND_BROADCAST = 0xFFFF;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        #endregion

        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.Name"); }
        }

        public string Description
        {
            get { return GetDescription(_settings); }
        }

        public object GUI
        {
            get
            {
                if (_GUI == null)
                    _GUI = CreateGUI();

                return _GUI;
            }
        }

        public bool ActivateWindowDefault
        {
            get { return false; }
        }

        public SetThemeUI TypedGUI
        {
            get { return (SetThemeUI)GUI; }
        }

        public string Category
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.Category"); }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.Windows;

        public IHostControl HostControl { get; set; }

        #endregion

        #region Public Methods

        public void Initialize()
        {
        }

        public bool Gestured(PointInfo ActionPoint)
        {
            return SetWindowsTheme(_settings);
        }

        public bool Deserialize(string SerializedData)
        {
            return PluginHelper.DeserializeSettings(SerializedData, out _settings);
        }

        public string Serialize()
        {
            if (_GUI != null)
                _settings = _GUI.Settings;

            if (_settings == null)
                _settings = new SetThemeSettings();

            return PluginHelper.SerializeSettings(_settings);
        }

        #endregion

        #region Private Methods

        private SetThemeUI CreateGUI()
        {
            SetThemeUI newGUI = new SetThemeUI();

            newGUI.Loaded += (o, e) =>
            {
                TypedGUI.Settings = _settings;
                TypedGUI.HostControl = HostControl;
            };

            return newGUI;
        }

        private string GetDescription(SetThemeSettings Settings)
        {
            if (Settings == null)
                return LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.Name");

            if (!Settings.ToggleAppTheme && !Settings.ToggleSystemTheme)
                return LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.Name");

            string appText = LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.ToggleAppTheme");
            string sysText = LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.ToggleSystemTheme");

            if (Settings.ToggleAppTheme && Settings.ToggleSystemTheme)
                return string.Format(LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.ToggleBoth"), appText, sysText);
            else if (Settings.ToggleAppTheme)
                return string.Format(LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.ToggleOnly"), appText);
            else
                return string.Format(LocalizationProvider.Instance.GetTextValue("CorePlugins.SetTheme.ToggleOnly"), sysText);
        }

        private bool SetWindowsTheme(SetThemeSettings settings)
        {
            if (settings == null)
                return false;

            if (!settings.ToggleAppTheme && !settings.ToggleSystemTheme)
                return false; // Nothing to toggle

            try
            {
                const string registryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryKeyPath, true))
                {
                    if (key == null)
                    {
                        // Registry key doesn't exist (older Windows versions)
                        return false;
                    }

                    // Toggle app theme if enabled
                    if (settings.ToggleAppTheme)
                    {
                        object currentValue = key.GetValue("AppsUseLightTheme");
                        if (currentValue != null)
                        {
                            int current = (int)currentValue;
                            int newValue = current == 1 ? 0 : 1; // Toggle: 1->0, 0->1
                            key.SetValue("AppsUseLightTheme", newValue, RegistryValueKind.DWord);
                        }
                    }

                    // Toggle system theme if enabled
                    if (settings.ToggleSystemTheme)
                    {
                        object currentValue = key.GetValue("SystemUsesLightTheme");
                        if (currentValue != null)
                        {
                            int current = (int)currentValue;
                            int newValue = current == 1 ? 0 : 1; // Toggle: 1->0, 0->1
                            key.SetValue("SystemUsesLightTheme", newValue, RegistryValueKind.DWord);
                        }
                    }
                }

                // Notify system of theme change
                NotifySystemThemeChange();

                return true;
            }
            catch (Exception ex)
            {
                // Log error if needed
                System.Diagnostics.Debug.WriteLine($"SetTheme error: {ex.Message}");
                return false;
            }
        }

        private void NotifySystemThemeChange()
        {
            try
            {
                // Notify all top-level windows that a setting has changed
                UIntPtr result;
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    "ImmersiveColorSet",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out result);
            }
            catch
            {
                // Ignore notification errors
            }
        }

        #endregion
    }
}
