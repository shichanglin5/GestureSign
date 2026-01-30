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

                // Get application path
                _settings.ApplicationPath = MatchConditionList.ApplicationPath;

                // Get application arguments
                _settings.ApplicationArguments = MatchConditionList.ApplicationArguments;

                // Get matching conditions
                _settings.MatchConditions = MatchConditionList.GetConditions();

                // Parse cache expiration
                if (int.TryParse(CacheExpirationTextBox.Text.Trim(), out int cacheExpiration))
                {
                    _settings.CacheExpirationSeconds = cacheExpiration;
                }
                else
                {
                    _settings.CacheExpirationSeconds = 30; // Default value
                }

                // Get minimize if activated setting
                _settings.MinimizeIfActivated = MinimizeIfActivatedCheckBox.IsChecked ?? true;

                return _settings;
            }
            set
            {
                _settings = value ?? new ActivateAppSettings();

                // Set display name
                DisplayNameTextBox.Text = _settings.DisplayName ?? string.Empty;

                // Set application path
                MatchConditionList.ApplicationPath = _settings.ApplicationPath ?? string.Empty;

                // Set application arguments
                MatchConditionList.ApplicationArguments = _settings.ApplicationArguments ?? string.Empty;

                // Set matching conditions
                MatchConditionList.SetConditions(_settings.MatchConditions);

                // Set cache expiration
                CacheExpirationTextBox.Text = _settings.CacheExpirationSeconds.ToString();

                // Set minimize if activated checkbox
                MinimizeIfActivatedCheckBox.IsChecked = _settings.MinimizeIfActivated;

                // Set default display name if empty but has ApplicationPath
                if (string.IsNullOrEmpty(DisplayNameTextBox.Text) && !string.IsNullOrEmpty(_settings.ApplicationPath))
                {
                    DisplayNameTextBox.Text = Path.GetFileNameWithoutExtension(_settings.ApplicationPath);
                }
            }
        }

        #endregion

        #region Event Handlers

        private void CaptureCrosshair_CrosshairDragged(object sender, MouseButtonEventArgs e)
        {
            ShowWindowSelectorDialog();
        }

        private void ShowWindowSelectorDialog()
        {
            try
            {
                Logging.LogDebug("[ActivateAppUI] ShowWindowSelectorDialog called");

                var ownerWindow = Window.GetWindow(this);
                Logging.LogDebug($"[ActivateAppUI] Owner window: {(ownerWindow != null ? ownerWindow.GetType().Name : "null")}");

                var dialog = new WindowSelectorDialog();

                // 只有当 Owner 窗口有效时才设置，否则使用 CenterScreen
                if (ownerWindow != null && ownerWindow.IsLoaded)
                {
                    dialog.Owner = ownerWindow;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                Logging.LogDebug("[ActivateAppUI] Showing dialog...");

                if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
                {
                    var windowInfo = dialog.SelectedWindow;
                    Logging.LogDebug($"[ActivateAppUI] Dialog result: true, selected window: {windowInfo.Title}");
                    PopulateFromWindowInfo(windowInfo);
                }
                else
                {
                    Logging.LogDebug("[ActivateAppUI] Dialog result: false or no selection");
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening window selection dialog: {ex.Message}\n{ex.StackTrace}");
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
