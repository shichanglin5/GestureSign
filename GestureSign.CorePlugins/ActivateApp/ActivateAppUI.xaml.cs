using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.UI;

namespace GestureSign.CorePlugins.ActivateApp
{
    public partial class ActivateAppUI : UserControl
    {
        #region Private Variables

        private ActivateAppSettings _settings;
        private WindowRule _windowRule;
        private string _applicationArguments;
        private string _presetId; // 引用的预置规则 ID

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

                // Set window rule
                _settings.WindowRule = _windowRule;

                // Set preset id
                _settings.PresetId = _presetId;

                // Set application arguments
                _settings.ApplicationArguments = _applicationArguments;

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

                // Get window rule
                _windowRule = _settings.WindowRule;

                // Get preset id
                _presetId = _settings.PresetId;

                // Get application arguments
                _applicationArguments = _settings.ApplicationArguments;

                // Set cache expiration
                CacheExpirationTextBox.Text = _settings.CacheExpirationSeconds.ToString();

                // Set minimize if activated checkbox
                MinimizeIfActivatedCheckBox.IsChecked = _settings.MinimizeIfActivated;

                // Update summary display
                UpdateWindowRuleSummary();
            }
        }

        #endregion

        #region Event Handlers

        private void AddWindowRuleButton_Click(object sender, RoutedEventArgs e)
        {
            ShowWindowSelectorDialogAndEdit();
        }

        private void WindowRuleBorder_Click(object sender, MouseButtonEventArgs e)
        {
            // 如果是预置引用，打开预置编辑对话框
            if (!string.IsNullOrEmpty(_presetId))
            {
                ShowPresetEditDialog();
            }
            else
            {
                // 点击窗口规则区域，打开编辑对话框
                ShowEditDialog();
            }
        }

        private void ReferencePresetButton_Click(object sender, RoutedEventArgs e)
        {
            // 引用预置规则
            try
            {
                var presets = WindowPresetManager.Instance.Presets;
                if (presets == null || presets.Count == 0)
                {
                    MessageBox.Show(
                        LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.NoPresetsAvailable"),
                        LocalizationProvider.Instance.GetTextValue("Common.OK"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var ownerWindow = Window.GetWindow(this);
                var dialog = new SelectPresetDialog();

                if (ownerWindow != null && ownerWindow.IsLoaded)
                {
                    dialog.Owner = ownerWindow;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedPresetId))
                {
                    // 获取预置规则并复制
                    var preset = WindowPresetManager.Instance.GetPresetById(dialog.SelectedPresetId);
                    if (preset != null)
                    {
                        _presetId = preset.Id;
                        // 复制预置规则作为 WindowRule
                        _windowRule = new WindowRule
                        {
                            Name = preset.Name,
                            ApplicationPath = preset.ApplicationPath,
                            Conditions = preset.Conditions?.ToList()
                        };

                        UpdateWindowRuleSummary();
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error referencing preset: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowWindowSelectorDialogAndEdit()
        {
            try
            {
                Logging.LogDebug("[ActivateAppUI] ShowWindowSelectorDialogAndEdit called");

                var ownerWindow = Window.GetWindow(this);
                var selectorDialog = new WindowSelectorDialog();

                if (ownerWindow != null && ownerWindow.IsLoaded)
                {
                    selectorDialog.Owner = ownerWindow;
                }
                else
                {
                    selectorDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                if (selectorDialog.ShowDialog() == true && selectorDialog.SelectedWindow != null)
                {
                    var windowInfo = selectorDialog.SelectedWindow;
                    Logging.LogDebug($"[ActivateAppUI] Dialog result: true, selected window: {windowInfo.Title}");

                    // 清除预置引用
                    _presetId = null;

                    // 创建新的 WindowRule 并打开编辑对话框
                    var rule = new WindowRule
                    {
                        Name = windowInfo.FileName ?? windowInfo.Title,
                        ApplicationPath = windowInfo.ProcessPath
                    };

                    var editDialog = new WindowRuleDialog(rule)
                    {
                        ShowRuleName = true,
                        ShowAddToPreset = true,
                        ShowArguments = true
                    };

                    if (ownerWindow != null && ownerWindow.IsLoaded)
                    {
                        editDialog.Owner = ownerWindow;
                    }
                    else
                    {
                        editDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }

                    // 使用 PopulateFromWindowInfo 填充所有可用条件供用户选择
                    editDialog.PopulateFromWindowInfo(windowInfo);

                    if (editDialog.ShowDialog() == true)
                    {
                        // 保存编辑结果
                        _windowRule = editDialog.WindowRule;
                        _applicationArguments = editDialog.ApplicationArguments;

                        // 更新摘要
                        UpdateWindowRuleSummary();
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening window selection dialog: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error opening window selection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowEditDialog()
        {
            try
            {
                var ownerWindow = Window.GetWindow(this);
                var dialog = new WindowRuleDialog(_windowRule ?? new WindowRule())
                {
                    ShowRuleName = true,
                    ShowAddToPreset = true,
                    ShowArguments = true
                };

                if (ownerWindow != null && ownerWindow.IsLoaded)
                {
                    dialog.Owner = ownerWindow;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                // Load current arguments
                dialog.ApplicationArguments = _applicationArguments ?? string.Empty;

                if (dialog.ShowDialog() == true)
                {
                    // Save settings from dialog
                    _windowRule = dialog.WindowRule;
                    _applicationArguments = dialog.ApplicationArguments;

                    // 清除预置引用（因为用户手动编辑了）
                    _presetId = null;

                    // Update summary
                    UpdateWindowRuleSummary();
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening edit dialog: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error opening edit dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowPresetEditDialog()
        {
            try
            {
                var preset = WindowPresetManager.Instance.GetPresetById(_presetId);
                if (preset == null)
                {
                    MessageBox.Show(
                        LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.PresetNotFound"),
                        LocalizationProvider.Instance.GetTextValue("Common.Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var ownerWindow = Window.GetWindow(this);
                var dialog = new WindowRuleDialog(preset)
                {
                    ShowRuleName = true,
                    ShowAddToPreset = false,
                    ShowArguments = true
                };

                if (ownerWindow != null && ownerWindow.IsLoaded)
                {
                    dialog.Owner = ownerWindow;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                // Load current arguments
                dialog.ApplicationArguments = _applicationArguments ?? string.Empty;

                if (dialog.ShowDialog() == true)
                {
                    // Save presets
                    WindowPresetManager.Instance.SavePresets();

                    // Update local cache
                    _windowRule = new WindowRule
                    {
                        Name = preset.Name,
                        ApplicationPath = preset.ApplicationPath,
                        Conditions = preset.Conditions?.ToList()
                    };
                    _applicationArguments = dialog.ApplicationArguments;

                    // Update summary
                    UpdateWindowRuleSummary();
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error editing preset: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Private Methods

        private void UpdateWindowRuleSummary()
        {
            var sb = new StringBuilder();

            // 如果有预置规则 ID，优先显示
            if (!string.IsNullOrEmpty(_presetId))
            {
                var preset = WindowPresetManager.Instance.GetPresetById(_presetId);
                var presetName = preset?.Name ?? _presetId;
                sb.AppendLine($"[{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.PresetReference")}] {presetName}");
            }

            if (_windowRule != null)
            {
                // Show rule name
                if (!string.IsNullOrEmpty(_windowRule.Name))
                {
                    sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("WindowRuleDialog.RuleName")}: {_windowRule.Name}");
                }

                // Show application path
                if (!string.IsNullOrEmpty(_windowRule.ApplicationPath))
                {
                    sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ApplicationPath")}: {Path.GetFileName(_windowRule.ApplicationPath)}");
                }

                // Show conditions summary
                if (_windowRule.Conditions != null && _windowRule.Conditions.Count > 0)
                {
                    foreach (var condition in _windowRule.Conditions.Take(3))
                    {
                        sb.AppendLine($"{condition.Type}: {condition.Value}");
                    }
                    if (_windowRule.Conditions.Count > 3)
                    {
                        sb.AppendLine($"... (+{_windowRule.Conditions.Count - 3})");
                    }
                }
            }

            if (sb.Length > 0)
            {
                WindowRuleSummaryText.Text = sb.ToString().TrimEnd();
                WindowRuleSummaryText.Foreground = (System.Windows.Media.Brush)FindResource("MahApps.Brushes.ThemeForeground");
            }
            else
            {
                WindowRuleSummaryText.Text = LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.NoWindowRuleConfigured");
                WindowRuleSummaryText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        #endregion
    }
}
