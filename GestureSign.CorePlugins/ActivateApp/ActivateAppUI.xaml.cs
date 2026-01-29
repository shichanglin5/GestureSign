using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using GestureSign.Common.UI;

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

                // Get display name
                _settings.DisplayName = DisplayNameTextBox.Text.Trim();

                // Get matching conditions
                _settings.MatchConditions = MatchConditionList.GetConditions();

                // Parse cache expiration
                if (int.TryParse(CacheExpirationTextBox.Text.Trim(), out int cacheExpiration))
                {
                    _settings.CacheExpirationSeconds = cacheExpiration;
                }
                else
                {
                    _settings.CacheExpirationSeconds = 5; // Default value
                }

                return _settings;
            }
            set
            {
                _settings = value ?? new ActivateAppSettings();

                // Set display name
                DisplayNameTextBox.Text = _settings.DisplayName ?? string.Empty;

                // Set matching conditions
                MatchConditionList.SetConditions(_settings.MatchConditions);

                // Set cache expiration
                CacheExpirationTextBox.Text = _settings.CacheExpirationSeconds.ToString();

                // Set default display name if empty but has ProcessPath condition
                if (string.IsNullOrEmpty(DisplayNameTextBox.Text) && _settings.MatchConditions != null)
                {
                    foreach (var condition in _settings.MatchConditions)
                    {
                        if (condition.Type == MatchConditionType.ProcessPath && !string.IsNullOrEmpty(condition.Value))
                        {
                            DisplayNameTextBox.Text = Path.GetFileNameWithoutExtension(condition.Value);
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region Event Handlers

        private void SelectWindowButton_Click(object sender, RoutedEventArgs e)
        {
            ShowWindowSelectorDialog();
        }

        private void CaptureCrosshair_CrosshairDragged(object sender, MouseButtonEventArgs e)
        {
            ShowWindowSelectorDialog();
        }

        private void ShowWindowSelectorDialog()
        {
            try
            {
                var dialog = new WindowSelectorDialog
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
                {
                    var windowInfo = dialog.SelectedWindow;
                    PopulateFromWindowInfo(windowInfo);
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening window selection dialog: {ex.Message}");
                MessageBox.Show($"Error opening window selection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Private Methods

        private void PopulateFromWindowInfo(WindowMatchInfo windowInfo)
        {
            if (_settings == null)
                _settings = new ActivateAppSettings();

            // Auto-fill display name (if empty)
            if (string.IsNullOrEmpty(DisplayNameTextBox.Text))
            {
                DisplayNameTextBox.Text = windowInfo.FileName ?? windowInfo.Title ?? string.Empty;
            }

            // Populate condition list
            MatchConditionList.PopulateFromWindowInfo(windowInfo);

            Logging.LogDebug($"[ActivateAppUI] Selected window: {windowInfo.FileName}, AUMID: {windowInfo.AUMID}, ClassName: {windowInfo.ClassName}");
        }

        #endregion
    }
}
