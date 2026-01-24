using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GestureSign.Common.Log;
using Microsoft.Win32;

namespace GestureSign.CorePlugins.ActivateApp
{
    public partial class ActivateAppUI : UserControl
    {
        #region Private Variables

        private ActivateAppSettings _settings;

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

                _settings.ApplicationPath = AppPathTextBox.Text.Trim();
                _settings.WindowClassName = WindowClassNameTextBox.Text.Trim();
                _settings.WindowTitlePattern = WindowTitlePatternTextBox.Text.Trim();
                _settings.UseRegexMatching = UseRegexCheckBox.IsChecked ?? false;
                _settings.DisplayName = DisplayNameTextBox.Text.Trim();

                // Extract process name from path
                if (!string.IsNullOrEmpty(_settings.ApplicationPath))
                {
                    _settings.ProcessName = Path.GetFileNameWithoutExtension(_settings.ApplicationPath);
                }

                return _settings;
            }
            set
            {
                _settings = value ?? new ActivateAppSettings();

                AppPathTextBox.Text = _settings.ApplicationPath ?? string.Empty;
                WindowClassNameTextBox.Text = _settings.WindowClassName ?? string.Empty;
                WindowTitlePatternTextBox.Text = _settings.WindowTitlePattern ?? string.Empty;
                UseRegexCheckBox.IsChecked = _settings.UseRegexMatching;
                DisplayNameTextBox.Text = _settings.DisplayName ?? string.Empty;

                // Set default display name if empty
                if (string.IsNullOrEmpty(DisplayNameTextBox.Text) && !string.IsNullOrEmpty(_settings.ApplicationPath))
                {
                    DisplayNameTextBox.Text = Path.GetFileNameWithoutExtension(_settings.ApplicationPath);
                }
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

                    AppPathTextBox.Text = windowInfo.ProcessPath;
                    WindowClassNameTextBox.Text = windowInfo.ClassName;

                    // Auto-fill display name
                    if (string.IsNullOrEmpty(DisplayNameTextBox.Text))
                    {
                        DisplayNameTextBox.Text = windowInfo.ProcessName;
                    }

                    Logging.LogDebug($"[ActivateAppUI] Selected window: {windowInfo.ProcessName} ({windowInfo.ClassName})");
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening window selection dialog: {ex.Message}");
                MessageBox.Show($"Error opening window selection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Application",
                InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs")
            };

            if (openFileDialog.ShowDialog() == true)
            {
                AppPathTextBox.Text = openFileDialog.FileName;

                // Auto-fill display name
                if (string.IsNullOrEmpty(DisplayNameTextBox.Text))
                {
                    DisplayNameTextBox.Text = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                }
            }
        }

        #endregion
    }
}
