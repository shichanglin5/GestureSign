using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using GestureSign.Common.UI;
using GestureSign.ControlPanel.Common;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GestureSign.ControlPanel.MainWindowControls
{
    /// <summary>
    /// WindowPresets.xaml 的交互逻辑
    /// </summary>
    public partial class WindowPresets : UserControl
    {
        private ObservableCollection<WindowRule> _presets;
        private WindowRule _selectedPreset;
        private bool _isEditing;
        private Point _dragStartPoint;
        private bool _isDragging;

        public WindowPresets()
        {
            InitializeComponent();
        }

        private void UserControl_Initialized(object sender, EventArgs e)
        {
            InitializeActivationMethodComboBox();

            _presets = new ObservableCollection<WindowRule>(WindowPresetManager.Instance.Presets);
            PresetsListBox.ItemsSource = _presets;

            WindowPresetManager.Instance.PresetsChanged += (s, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _presets.Clear();
                    foreach (var preset in WindowPresetManager.Instance.Presets)
                    {
                        _presets.Add(preset);
                    }
                });
            };
        }

        private void PresetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedPreset = PresetsListBox.SelectedItem as WindowRule;
            DeletePresetButton.IsEnabled = _selectedPreset != null;
            EditPanel.IsEnabled = _selectedPreset != null;

            if (_selectedPreset != null)
            {
                LoadPresetToEditor(_selectedPreset);
                _isEditing = true;
            }
            else
            {
                ClearEditor();
                _isEditing = false;
            }
        }

        private void LoadPresetToEditor(WindowRule preset)
        {
            PresetNameTextBox.Text = preset.Name ?? string.Empty;
            PresetConditionList.ApplicationPath = preset.ApplicationPath ?? string.Empty;
            PresetConditionList.SetConditions(preset.Conditions?.ToList());

            int methodIndex = (int)preset.ActivationMethod;
            PresetActivationMethodComboBox.SelectedIndex = methodIndex >= 0 && methodIndex <= 2
                ? methodIndex
                : 0;
        }

        private void ClearEditor()
        {
            PresetNameTextBox.Text = string.Empty;
            PresetConditionList.ApplicationPath = string.Empty;
            PresetConditionList.Clear();
            PresetActivationMethodComboBox.SelectedIndex = 0;
        }

        private void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            _isEditing = false;
            _selectedPreset = null;
            PresetsListBox.SelectedItem = null;

            ClearEditor();
            EditPanel.IsEnabled = true;
            PresetNameTextBox.Focus();
        }

        private async void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null) return;

            var parentWindow = UIHelper.GetParentWindow(this);

            // Check for references
            var references = WindowPresetManager.Instance.FindPresetReferences(_selectedPreset.Id);
            if (references.Count > 0)
            {
                var referenceList = string.Join("\n", references.Take(10));
                if (references.Count > 10)
                {
                    referenceList += $"\n... 还有 {references.Count - 10} 处引用";
                }

                var result = await parentWindow.ShowMessageAsync(
                    "确认删除",
                    $"以下位置引用了此预置规则，删除后这些引用将被清除：\n\n{referenceList}\n\n确定要删除吗？",
                    MessageDialogStyle.AffirmativeAndNegative,
                    new MetroDialogSettings
                    {
                        AffirmativeButtonText = "删除",
                        NegativeButtonText = "取消",
                        DefaultButtonFocus = MessageDialogResult.Negative
                    });

                if (result != MessageDialogResult.Affirmative)
                    return;

                // Clear references
                WindowPresetManager.Instance.ClearPresetReferences(_selectedPreset.Id);
            }
            else
            {
                var result = await parentWindow.ShowMessageAsync(
                    "确认删除",
                    $"确定要删除预置规则 \"{_selectedPreset.Name}\" 吗？",
                    MessageDialogStyle.AffirmativeAndNegative,
                    new MetroDialogSettings
                    {
                        AffirmativeButtonText = "删除",
                        NegativeButtonText = "取消",
                        DefaultButtonFocus = MessageDialogResult.Negative
                    });

                if (result != MessageDialogResult.Affirmative)
                    return;
            }

            WindowPresetManager.Instance.RemovePreset(_selectedPreset.Name);
            WindowPresetManager.Instance.SavePresets();
            ApplicationManager.Instance.SaveApplications();

            // PresetsChanged 事件会自动同步 _presets，不需要手动删除
            _selectedPreset = null;
            ClearEditor();
            EditPanel.IsEnabled = false;
        }

        private async void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var parentWindow = UIHelper.GetParentWindow(this);

            var presetName = PresetNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(presetName))
            {
                await parentWindow.ShowMessageAsync(
                    "提示",
                    "请输入预置规则名称");
                PresetNameTextBox.Focus();
                return;
            }

            var conditions = PresetConditionList.GetConditions();
            var applicationPath = PresetConditionList.ApplicationPath;

            // Validate: must have either conditions or applicationPath
            if ((conditions == null || conditions.Count == 0) && string.IsNullOrEmpty(applicationPath))
            {
                await parentWindow.ShowMessageAsync(
                    "提示",
                    "请至少添加一个匹配条件或指定应用程序路径");
                return;
            }

            // Check for duplicate names (when adding new or renaming)
            var existingPreset = WindowPresetManager.Instance.GetPresetByName(presetName);
            if (existingPreset != null)
            {
                if (!_isEditing || (_isEditing && _selectedPreset != null && !string.Equals(_selectedPreset.Name, presetName, StringComparison.OrdinalIgnoreCase)))
                {
                    await parentWindow.ShowMessageAsync(
                        "提示",
                        $"预置规则名称 \"{presetName}\" 已存在，请使用其他名称");
                    PresetNameTextBox.Focus();
                    return;
                }
            }

            if (_isEditing && _selectedPreset != null)
            {
                // Update existing preset
                _selectedPreset.Name = presetName;
                _selectedPreset.ApplicationPath = applicationPath;
                _selectedPreset.Conditions = conditions;
                _selectedPreset.ActivationMethod = GetSelectedActivationMethod();

                WindowPresetManager.Instance.SavePresets();

                // Refresh list item to show new name
                var index = _presets.IndexOf(_selectedPreset);
                if (index >= 0)
                {
                    // 暂时取消事件订阅，避免刷新时触发 SelectionChanged
                    PresetsListBox.SelectionChanged -= PresetsListBox_SelectionChanged;
                    try
                    {
                        _presets.RemoveAt(index);
                        _presets.Insert(index, _selectedPreset);
                        PresetsListBox.SelectedIndex = index;
                    }
                    finally
                    {
                        PresetsListBox.SelectionChanged += PresetsListBox_SelectionChanged;
                    }
                }
            }
            else
            {
                // Add new preset
                var newPreset = new WindowRule
                {
                    Name = presetName,
                    ApplicationPath = applicationPath,
                    Conditions = conditions,
                    ActivationMethod = GetSelectedActivationMethod()
                };

                WindowPresetManager.Instance.AddPreset(newPreset);
                WindowPresetManager.Instance.SavePresets();

                // PresetsChanged 事件会自动同步 _presets
                // 选中新添加的预置
                _selectedPreset = WindowPresetManager.Instance.GetPresetByName(presetName);
                PresetsListBox.SelectedItem = _presets.FirstOrDefault(p => p.Name == presetName);
                _isEditing = true;
            }
        }

        private void PresetCrosshair_Dragged(object sender, MouseButtonEventArgs e)
        {
            // 弹出窗口选择对话框
            var dialog = new GestureSign.Common.UI.WindowSelectorDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                var windowInfo = dialog.SelectedWindow;
                PresetConditionList.PopulateFromWindowInfo(windowInfo);

                // Auto-fill preset name if empty
                if (string.IsNullOrEmpty(PresetNameTextBox.Text))
                {
                    if (!string.IsNullOrEmpty(windowInfo.FileName))
                    {
                        PresetNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(windowInfo.FileName);
                    }
                }
            }
        }

        private void InitializeActivationMethodComboBox()
        {
            PresetActivationMethodComboBox.Items.Clear();
            PresetActivationMethodComboBox.Items.Add(LocalizationProvider.Instance.GetTextValue("WindowPresets.ActivationMethodUseGlobal"));
            PresetActivationMethodComboBox.Items.Add(LocalizationProvider.Instance.GetTextValue("WindowPresets.ActivationMethodAttachThreadInput"));
            PresetActivationMethodComboBox.Items.Add(LocalizationProvider.Instance.GetTextValue("WindowPresets.ActivationMethodSafeMode"));
            PresetActivationMethodComboBox.SelectedIndex = 0;
        }

        private ActivationMethod GetSelectedActivationMethod()
        {
            int selectedIndex = PresetActivationMethodComboBox.SelectedIndex;
            return selectedIndex >= 0 && selectedIndex <= 2
                ? (ActivationMethod)selectedIndex
                : ActivationMethod.UseGlobal;
        }

        #region Drag and Drop Sorting

        private void PresetsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void PresetsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
                return;

            Point position = e.GetPosition(null);
            if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (listBoxItem != null && listBoxItem.Content is WindowRule preset)
                {
                    _isDragging = true;
                    DragDrop.DoDragDrop(listBoxItem, preset, DragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }

        private void PresetsListBox_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(WindowRule)))
                return;

            var droppedPreset = e.Data.GetData(typeof(WindowRule)) as WindowRule;
            var target = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

            if (droppedPreset == null)
                return;

            int sourceIndex = _presets.IndexOf(droppedPreset);
            int targetIndex;

            if (target != null && target.Content is WindowRule targetPreset)
            {
                targetIndex = _presets.IndexOf(targetPreset);
            }
            else
            {
                // Dropped at the end
                targetIndex = _presets.Count - 1;
            }

            if (sourceIndex != targetIndex && sourceIndex >= 0 && targetIndex >= 0)
            {
                _presets.Move(sourceIndex, targetIndex);

                // Sync to manager
                WindowPresetManager.Instance.Presets.Clear();
                foreach (var preset in _presets)
                {
                    WindowPresetManager.Instance.Presets.Add(preset);
                }
                WindowPresetManager.Instance.SavePresets();
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                    return ancestor;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        #endregion
    }
}
