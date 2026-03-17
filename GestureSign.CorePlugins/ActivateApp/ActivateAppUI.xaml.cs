using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private readonly List<ActivateAppSettings> _candidates = new();
        private int _selectedCandidateIndex = -1;
        private bool _isUpdatingSelection;

        private sealed class CandidateListItem
        {
            public int Index { get; init; }
            public string Title { get; init; }
            public string Detail { get; init; }
        }

        #endregion

        #region Constructor

        public ActivateAppUI()
        {
            InitializeComponent();
            InitializeActivationMethodComboBox();
            UpdateButtonsState();
        }

        #endregion

        #region Properties

        public ActivateAppSettings Settings
        {
            get
            {
                if (_settings == null)
                    _settings = new ActivateAppSettings();

                SaveCurrentCandidate();

                var firstCandidate = _candidates.FirstOrDefault();
                if (firstCandidate == null)
                {
                    _settings.SortedList = null;
                    _settings.WindowRule = null;
                    _settings.PresetId = null;
                    _settings.ApplicationArguments = null;
                    _settings.CacheExpirationSeconds = 30;
                    _settings.MinimizeIfActivated = true;
                    _settings.AutoLaunch = true;
                    _settings.ActivationMethod = ActivationMethod.UseGlobal;
                    return _settings;
                }

                if (_candidates.Count > 1)
                {
                    _settings.SortedList = _candidates.Select(CloneCandidate).ToList();
                }
                else
                {
                    _settings.SortedList = null;
                }

                ApplyCandidateToRootSettings(_settings, firstCandidate);
                return _settings;
            }
            set
            {
                _settings = value ?? new ActivateAppSettings();
                LoadCandidatesFromSettings(_settings);
                RefreshCandidatesList(0);
            }
        }

        #endregion

        #region Event Handlers

        private void AddWindowRuleButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentCandidate();

            var candidate = CreateDefaultCandidate();
            if (TryConfigureCandidateFromWindowSelector(candidate))
            {
                _candidates.Add(candidate);
                RefreshCandidatesList(_candidates.Count - 1);
            }
        }

        private void ReferencePresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentCandidate();

            var candidate = CreateDefaultCandidate();
            if (TryConfigureCandidateFromPreset(candidate))
            {
                _candidates.Add(candidate);
                RefreshCandidatesList(_candidates.Count - 1);
            }
        }

        private void EditWindowRuleButton_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedCandidate();
        }

        private void DeleteWindowRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCandidateIndex < 0 || _selectedCandidateIndex >= _candidates.Count)
                return;

            _candidates.RemoveAt(_selectedCandidateIndex);
            int nextIndex = _selectedCandidateIndex;
            if (nextIndex >= _candidates.Count)
                nextIndex = _candidates.Count - 1;

            RefreshCandidatesList(nextIndex);
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCandidateIndex <= 0 || _selectedCandidateIndex >= _candidates.Count)
                return;

            SaveCurrentCandidate();

            var candidate = _candidates[_selectedCandidateIndex];
            _candidates.RemoveAt(_selectedCandidateIndex);
            _selectedCandidateIndex--;
            _candidates.Insert(_selectedCandidateIndex, candidate);
            RefreshCandidatesList(_selectedCandidateIndex);
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCandidateIndex < 0 || _selectedCandidateIndex >= _candidates.Count - 1)
                return;

            SaveCurrentCandidate();

            var candidate = _candidates[_selectedCandidateIndex];
            _candidates.RemoveAt(_selectedCandidateIndex);
            _selectedCandidateIndex++;
            _candidates.Insert(_selectedCandidateIndex, candidate);
            RefreshCandidatesList(_selectedCandidateIndex);
        }

        private void WindowCandidatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection)
                return;

            int nextIndex = WindowCandidatesListBox.SelectedIndex;
            if (nextIndex == _selectedCandidateIndex)
                return;

            SaveCurrentCandidate();
            SelectCandidate(nextIndex);
        }

        private void WindowCandidatesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditSelectedCandidate();
        }

        private void WindowCandidatesListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item)
                return;

            item.IsSelected = true;
            item.Focus();
        }

        private void CandidateSettingControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelection)
                return;

            SaveCurrentCandidate();
            UpdateSelectedCandidateSummary();
        }

        private void CacheExpirationTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelection)
                return;

            SaveCurrentCandidate();
            UpdateSelectedCandidateSummary();
        }

        private void WindowRuleBorder_Click(object sender, MouseButtonEventArgs e)
        {
            EditSelectedCandidate();
        }

        #endregion

        #region Candidate Editing

        private void EditSelectedCandidate()
        {
            if (_selectedCandidateIndex < 0 || _selectedCandidateIndex >= _candidates.Count)
                return;

            SaveCurrentCandidate();

            var candidate = CloneCandidate(_candidates[_selectedCandidateIndex]);
            bool updated = !string.IsNullOrEmpty(candidate.PresetId)
                ? TryEditPresetCandidate(candidate)
                : TryEditWindowRuleCandidate(candidate);

            if (!updated)
                return;

            _candidates[_selectedCandidateIndex] = candidate;
            RefreshCandidatesList(_selectedCandidateIndex);
        }

        private bool TryConfigureCandidateFromWindowSelector(ActivateAppSettings candidate)
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

                if (selectorDialog.ShowDialog() != true || selectorDialog.SelectedWindow == null)
                    return false;

                var windowInfo = selectorDialog.SelectedWindow;
                Logging.LogDebug($"[ActivateAppUI] Dialog result: true, selected window: {windowInfo.Title}");

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

                editDialog.PopulateFromWindowInfo(windowInfo);

                if (editDialog.ShowDialog() != true)
                    return false;

                candidate.PresetId = null;
                candidate.WindowRule = editDialog.WindowRule;
                candidate.ApplicationArguments = editDialog.ApplicationArguments;
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening window selection dialog: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error opening window selection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool TryConfigureCandidateFromPreset(ActivateAppSettings candidate)
        {
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
                    return false;
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

                if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.SelectedPresetId))
                    return false;

                var preset = WindowPresetManager.Instance.GetPresetById(dialog.SelectedPresetId);
                if (preset == null)
                    return false;

                candidate.PresetId = preset.Id;
                candidate.WindowRule = new WindowRule
                {
                    Name = preset.Name,
                    ApplicationPath = preset.ApplicationPath,
                    Conditions = preset.Conditions?.ToList()
                };
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error referencing preset: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool TryEditWindowRuleCandidate(ActivateAppSettings candidate)
        {
            try
            {
                var ownerWindow = Window.GetWindow(this);
                var dialog = new WindowRuleDialog(candidate.WindowRule ?? new WindowRule())
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

                dialog.ApplicationArguments = candidate.ApplicationArguments ?? string.Empty;

                if (dialog.ShowDialog() != true)
                    return false;

                candidate.PresetId = null;
                candidate.WindowRule = dialog.WindowRule;
                candidate.ApplicationArguments = dialog.ApplicationArguments;
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error opening edit dialog: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error opening edit dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool TryEditPresetCandidate(ActivateAppSettings candidate)
        {
            try
            {
                var preset = WindowPresetManager.Instance.GetPresetById(candidate.PresetId);
                if (preset == null)
                {
                    MessageBox.Show(
                        LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.PresetNotFound"),
                        LocalizationProvider.Instance.GetTextValue("Common.Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
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

                dialog.ApplicationArguments = candidate.ApplicationArguments ?? string.Empty;

                if (dialog.ShowDialog() != true)
                    return false;

                WindowPresetManager.Instance.SavePresets();

                candidate.WindowRule = new WindowRule
                {
                    Name = preset.Name,
                    ApplicationPath = preset.ApplicationPath,
                    Conditions = preset.Conditions?.ToList()
                };
                candidate.ApplicationArguments = dialog.ApplicationArguments;
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateAppUI] Error editing preset: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        #endregion

        #region Candidate State

        private void LoadCandidatesFromSettings(ActivateAppSettings settings)
        {
            _candidates.Clear();

            if (settings?.SortedList != null && settings.SortedList.Count > 0)
            {
                _candidates.AddRange(settings.SortedList.Where(candidate => candidate != null).Select(CloneCandidate));
            }
            else if (HasCandidateData(settings))
            {
                _candidates.Add(CloneCandidate(settings));
            }
        }

        private static bool HasCandidateData(ActivateAppSettings settings)
        {
            return settings != null && (settings.WindowRule != null || !string.IsNullOrEmpty(settings.PresetId));
        }

        private void SaveCurrentCandidate()
        {
            var candidate = GetSelectedCandidate();
            if (candidate == null)
                return;

            candidate.CacheExpirationSeconds = int.TryParse(CacheExpirationTextBox.Text.Trim(), out int cacheExpiration)
                ? cacheExpiration
                : 30;
            candidate.MinimizeIfActivated = MinimizeIfActivatedCheckBox.IsChecked ?? true;
            candidate.AutoLaunch = AutoLaunchCheckBox.IsChecked ?? true;

            int selectedIndex = ActivationMethodComboBox.SelectedIndex;
            candidate.ActivationMethod = selectedIndex >= 0 && selectedIndex <= 2
                ? (ActivationMethod)selectedIndex
                : ActivationMethod.UseGlobal;
        }

        private void LoadCandidateToEditor(ActivateAppSettings candidate)
        {
            _isUpdatingSelection = true;
            try
            {
                if (candidate == null)
                {
                    CandidateSettingsPanel.IsEnabled = false;
                    WindowRuleSummaryText.Text = LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.NoWindowCandidateConfigured");
                    WindowRuleSummaryText.Foreground = Brushes.Gray;
                    CacheExpirationTextBox.Text = "30";
                    MinimizeIfActivatedCheckBox.IsChecked = true;
                    AutoLaunchCheckBox.IsChecked = true;
                    ActivationMethodComboBox.SelectedIndex = 0;
                    return;
                }

                CandidateSettingsPanel.IsEnabled = true;
                CacheExpirationTextBox.Text = candidate.CacheExpirationSeconds.ToString();
                MinimizeIfActivatedCheckBox.IsChecked = candidate.MinimizeIfActivated;
                AutoLaunchCheckBox.IsChecked = candidate.AutoLaunch;

                int methodIndex = (int)candidate.ActivationMethod;
                ActivationMethodComboBox.SelectedIndex = methodIndex >= 0 && methodIndex <= 2
                    ? methodIndex
                    : 0;

                UpdateWindowRuleSummary(candidate);
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private ActivateAppSettings GetSelectedCandidate()
        {
            return _selectedCandidateIndex >= 0 && _selectedCandidateIndex < _candidates.Count
                ? _candidates[_selectedCandidateIndex]
                : null;
        }

        private void SelectCandidate(int index)
        {
            _selectedCandidateIndex = index >= 0 && index < _candidates.Count ? index : -1;

            _isUpdatingSelection = true;
            try
            {
                WindowCandidatesListBox.SelectedIndex = _selectedCandidateIndex;
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            LoadCandidateToEditor(GetSelectedCandidate());
            UpdateButtonsState();
        }

        private void RefreshCandidatesList(int selectedIndex)
        {
            SaveCurrentCandidate();

            var items = _candidates
                .Select((candidate, index) => new CandidateListItem
                {
                    Index = index,
                    Title = GetCandidateDisplayName(candidate, index),
                    Detail = BuildCandidateListDetail(candidate)
                })
                .ToList();

            WindowCandidatesListBox.ItemsSource = null;
            WindowCandidatesListBox.ItemsSource = items;

            SelectCandidate(selectedIndex);
        }

        private void UpdateSelectedCandidateSummary()
        {
            var candidate = GetSelectedCandidate();
            UpdateWindowRuleSummary(candidate);
        }

        private void UpdateButtonsState()
        {
            bool hasSelection = _selectedCandidateIndex >= 0 && _selectedCandidateIndex < _candidates.Count;
            EditWindowRuleButton.IsEnabled = hasSelection;
            DeleteWindowRuleButton.IsEnabled = hasSelection;
            MoveUpButton.IsEnabled = hasSelection && _selectedCandidateIndex > 0;
            MoveDownButton.IsEnabled = hasSelection && _selectedCandidateIndex >= 0 && _selectedCandidateIndex < _candidates.Count - 1;
            CandidateSettingsPanel.IsEnabled = hasSelection;
        }

        #endregion

        #region Summary Helpers

        private string BuildCandidateListDetail(ActivateAppSettings candidate)
        {
            string autoLaunch = candidate.AutoLaunch
                ? LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.AutoLaunchEnabled")
                : LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.AutoLaunchDisabled");

            string minimize = candidate.MinimizeIfActivated
                ? LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.MinimizeIfActivated")
                : LocalizationProvider.Instance.GetTextValue("Common.No");

            return $"{autoLaunch} | {LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.CacheExpiration")}: {candidate.CacheExpirationSeconds}s | {LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.MinimizeIfActivated")}: {minimize}";
        }

        private string GetCandidateDisplayName(ActivateAppSettings candidate, int index = -1)
        {
            string prefix = index >= 0 ? $"{index + 1}. " : string.Empty;

            if (candidate == null)
                return prefix + LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.NoWindowCandidateConfigured");

            if (!string.IsNullOrEmpty(candidate.PresetId))
            {
                var preset = WindowPresetManager.Instance.GetPresetById(candidate.PresetId);
                if (preset != null && !string.IsNullOrEmpty(preset.Name))
                    return $"{prefix}[{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.PresetReference")}] {preset.Name}";
            }

            if (!string.IsNullOrEmpty(candidate.WindowRule?.Name))
                return prefix + candidate.WindowRule.Name;

            if (!string.IsNullOrEmpty(candidate.WindowRule?.ApplicationPath))
                return prefix + Path.GetFileName(candidate.WindowRule.ApplicationPath);

            return prefix + LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.NoWindowCandidateConfigured");
        }

        private void UpdateWindowRuleSummary(ActivateAppSettings candidate)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(candidate?.PresetId))
            {
                var preset = WindowPresetManager.Instance.GetPresetById(candidate.PresetId);
                var presetName = preset?.Name ?? candidate.PresetId;
                sb.AppendLine($"[{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.PresetReference")}] {presetName}");
            }

            if (candidate?.WindowRule != null)
            {
                if (!string.IsNullOrEmpty(candidate.WindowRule.Name))
                {
                    sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("WindowRuleDialog.RuleName")}: {candidate.WindowRule.Name}");
                }

                if (!string.IsNullOrEmpty(candidate.WindowRule.ApplicationPath))
                {
                    sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ApplicationPath")}: {Path.GetFileName(candidate.WindowRule.ApplicationPath)}");
                }

                if (candidate.WindowRule.Conditions != null && candidate.WindowRule.Conditions.Count > 0)
                {
                    foreach (var condition in candidate.WindowRule.Conditions.Take(3))
                    {
                        sb.AppendLine($"{condition.Type}: {condition.Value}");
                    }

                    if (candidate.WindowRule.Conditions.Count > 3)
                    {
                        sb.AppendLine($"... (+{candidate.WindowRule.Conditions.Count - 3})");
                    }
                }
            }

            if (candidate != null)
            {
                sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.MinimizeIfActivated")}: {(candidate.MinimizeIfActivated ? LocalizationProvider.Instance.GetTextValue("Common.Yes") : LocalizationProvider.Instance.GetTextValue("Common.No"))}");
                sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.AutoLaunch")}: {(candidate.AutoLaunch ? LocalizationProvider.Instance.GetTextValue("Common.Yes") : LocalizationProvider.Instance.GetTextValue("Common.No"))}");
                sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod")}: {GetActivationMethodText(candidate.ActivationMethod)}");
                sb.AppendLine($"{LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.CacheExpiration")}: {candidate.CacheExpirationSeconds} s");
            }

            if (sb.Length > 0)
            {
                WindowRuleSummaryText.Text = sb.ToString().TrimEnd();
                WindowRuleSummaryText.Foreground = (Brush)FindResource("MahApps.Brushes.ThemeForeground");
            }
            else
            {
                WindowRuleSummaryText.Text = LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.NoWindowCandidateConfigured");
                WindowRuleSummaryText.Foreground = Brushes.Gray;
            }
        }

        #endregion

        #region Utility Methods

        private void InitializeActivationMethodComboBox()
        {
            ActivationMethodComboBox.Items.Clear();
            ActivationMethodComboBox.Items.Add(LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod.UseGlobal"));
            ActivationMethodComboBox.Items.Add(LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod.AttachThreadInput"));
            ActivationMethodComboBox.Items.Add(LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod.SafeMode"));
            ActivationMethodComboBox.SelectedIndex = 0;
        }

        private string GetActivationMethodText(ActivationMethod method)
        {
            return method switch
            {
                ActivationMethod.AttachThreadInput => LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod.AttachThreadInput"),
                ActivationMethod.SafeMode => LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod.SafeMode"),
                _ => LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.ActivationMethod.UseGlobal")
            };
        }

        private static ActivateAppSettings CreateDefaultCandidate()
        {
            return new ActivateAppSettings
            {
                CacheExpirationSeconds = 30,
                MinimizeIfActivated = true,
                AutoLaunch = true,
                ActivationMethod = ActivationMethod.UseGlobal
            };
        }

        private static ActivateAppSettings CloneCandidate(ActivateAppSettings source)
        {
            if (source == null)
                return CreateDefaultCandidate();

            return new ActivateAppSettings
            {
                WindowRule = CloneWindowRule(source.WindowRule),
                PresetId = source.PresetId,
                ApplicationArguments = source.ApplicationArguments,
                CacheExpirationSeconds = source.CacheExpirationSeconds,
                MinimizeIfActivated = source.MinimizeIfActivated,
                AutoLaunch = source.AutoLaunch,
                ActivationMethod = source.ActivationMethod,
                SortedList = null
            };
        }

        private static WindowRule CloneWindowRule(WindowRule source)
        {
            if (source == null)
                return null;

            return new WindowRule
            {
                Id = source.Id,
                Name = source.Name,
                ActivationMethod = source.ActivationMethod,
                ApplicationPath = source.ApplicationPath,
                Conditions = source.Conditions?.Select(condition => new MatchCondition
                {
                    Type = condition.Type,
                    Value = condition.Value,
                    IsRegex = condition.IsRegex
                }).ToList()
            };
        }

        private static void ApplyCandidateToRootSettings(ActivateAppSettings root, ActivateAppSettings candidate)
        {
            root.WindowRule = CloneWindowRule(candidate.WindowRule);
            root.PresetId = candidate.PresetId;
            root.ApplicationArguments = candidate.ApplicationArguments;
            root.CacheExpirationSeconds = candidate.CacheExpirationSeconds;
            root.MinimizeIfActivated = candidate.MinimizeIfActivated;
            root.AutoLaunch = candidate.AutoLaunch;
            root.ActivationMethod = candidate.ActivationMethod;
        }

        #endregion
    }
}
