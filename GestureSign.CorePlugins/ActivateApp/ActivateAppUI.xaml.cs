using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.CorePlugins.ActivateApp
{
    public partial class ActivateAppUI : UserControl
    {
        #region Private Variables

        private ActivateAppSettings _settings;
        private string _tempAUMID;

        #endregion

        #region Constructor

        public ActivateAppUI()
        {
            InitializeComponent();
        }

        #endregion

        #region Properties

        public ActivateAppSettings Settings
        {
            get
            {
                if (_settings == null)
                    _settings = new ActivateAppSettings();

                // Only save user-editable fields (DisplayName and title filters)
                _settings.DisplayName = DisplayNameTextBox.Text.Trim();
                _settings.WindowTitlePattern = WindowTitlePatternTextBox.Text.Trim();
                _settings.UseRegexMatching = UseRegexCheckBox.IsChecked ?? false;

                // Parse cache expiration
                if (int.TryParse(CacheExpirationTextBox.Text.Trim(), out int cacheExpiration))
                {
                    _settings.CacheExpirationSeconds = cacheExpiration;
                }
                else
                {
                    _settings.CacheExpirationSeconds = 5; // Default value
                }

                // AUMID, ClassName, ApplicationPath are set when selecting a window
                // ProcessName is derived from ApplicationPath
                if (!string.IsNullOrEmpty(_settings.ApplicationPath))
                {
                    _settings.ProcessName = Path.GetFileNameWithoutExtension(_settings.ApplicationPath);
                }

                return _settings;
            }
            set
            {
                _settings = value ?? new ActivateAppSettings();

                // Display read-only fields (no text boxes for them now)
                // DisplayName and filters are user-editable
                DisplayNameTextBox.Text = _settings.DisplayName ?? string.Empty;
                WindowTitlePatternTextBox.Text = _settings.WindowTitlePattern ?? string.Empty;
                UseRegexCheckBox.IsChecked = _settings.UseRegexMatching;
                CacheExpirationTextBox.Text = _settings.CacheExpirationSeconds.ToString();

                // Restore AUMID
                _tempAUMID = _settings.AUMID;

                // Set default display name if empty
                if (string.IsNullOrEmpty(DisplayNameTextBox.Text) && !string.IsNullOrEmpty(_settings.ProcessName))
                {
                    DisplayNameTextBox.Text = _settings.ProcessName;
                }

                // Update configuration display table
                UpdateConfigDisplay();
            }
        }

        #endregion

        #region Event Handlers

        private void SelectWindowButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SelectWindowDialog
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
                {
                    var windowInfo = dialog.SelectedWindow;

                    // Auto-fill core matching information
                    if (_settings == null)
                        _settings = new ActivateAppSettings();

                    _settings.AUMID = windowInfo.AUMID ?? string.Empty;
                    _settings.WindowClassName = windowInfo.ClassName;
                    _settings.ApplicationPath = windowInfo.ProcessPath;
                    _settings.ProcessName = windowInfo.ProcessName;

                    // Store AUMID in temp field as well
                    _tempAUMID = windowInfo.AUMID;

                    // Auto-fill display name (if empty)
                    if (string.IsNullOrEmpty(DisplayNameTextBox.Text))
                    {
                        DisplayNameTextBox.Text = windowInfo.ProcessName;
                    }

                    Logging.LogDebug($"[ActivateAppUI] Selected window: {windowInfo.ProcessName}, AUMID: {windowInfo.AUMID}, ClassName: {windowInfo.ClassName}");

                    // Update configuration display table
                    UpdateConfigDisplay();
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening window selection dialog: {ex.Message}");
                MessageBox.Show($"Error opening window selection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        private void CaptureCrosshair_CrosshairDragged(object sender, MouseButtonEventArgs e)
        {
            try
            {
                GetCursorPos(out System.Drawing.Point cursorPosition);
                var window = SystemWindow.FromPointEx(cursorPosition.X, cursorPosition.Y, true, true);
                if (window == null || window.HWnd == IntPtr.Zero)
                    return;

                var detailsDialog = new WindowDetailsDialog(window.HWnd)
                {
                    Owner = Window.GetWindow(this)
                };
                detailsDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error capturing window: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        private void UpdateConfigDisplay()
        {
            var configs = new List<ConfigProperty>();

            // AUMID - most important matching condition
            configs.Add(new ConfigProperty
            {
                Name = "AUMID",
                Value = string.IsNullOrEmpty(_tempAUMID) ? "(Not set / 未设置)" : _tempAUMID
            });

            // Window Class Name - core matching condition
            configs.Add(new ConfigProperty
            {
                Name = "Window Class Name",
                Value = string.IsNullOrEmpty(_settings?.WindowClassName)
                    ? "(Not set / 未设置)"
                    : _settings.WindowClassName
            });

            // Process Path - core matching condition
            configs.Add(new ConfigProperty
            {
                Name = "Process Path",
                Value = string.IsNullOrEmpty(_settings?.ApplicationPath)
                    ? "(Not set / 未设置)"
                    : _settings.ApplicationPath
            });

            // Process Name
            configs.Add(new ConfigProperty
            {
                Name = "Process Name",
                Value = string.IsNullOrEmpty(_settings?.ProcessName)
                    ? "(Not set / 未设置)"
                    : _settings.ProcessName
            });

            // Display Name
            configs.Add(new ConfigProperty
            {
                Name = "Display Name",
                Value = string.IsNullOrEmpty(DisplayNameTextBox.Text)
                    ? "(Not set / 未设置)"
                    : DisplayNameTextBox.Text
            });

            // Window Title Pattern (optional filter)
            configs.Add(new ConfigProperty
            {
                Name = "Window Title Pattern",
                Value = string.IsNullOrEmpty(WindowTitlePatternTextBox.Text)
                    ? "(Any / 任意)"
                    : WindowTitlePatternTextBox.Text
            });

            // Use Regex Matching
            configs.Add(new ConfigProperty
            {
                Name = "Use Regex Matching",
                Value = (UseRegexCheckBox.IsChecked ?? false) ? "Yes / 是" : "No / 否"
            });

            // Cache Expiration (from settings or default)
            int cacheExpiration = _settings?.CacheExpirationSeconds ?? 5;
            configs.Add(new ConfigProperty
            {
                Name = "Cache Expiration (seconds)",
                Value = cacheExpiration <= 0
                    ? "Disabled / 已禁用"
                    : $"{cacheExpiration} seconds / 秒"
            });

            ConfigDataGrid.ItemsSource = configs;
        }

        #endregion

        #region Helper Classes

        public class ConfigProperty
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }

        #endregion
    }
}
