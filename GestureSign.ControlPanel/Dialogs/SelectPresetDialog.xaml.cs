using GestureSign.Common.Applications;
using MahApps.Metro.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// SelectPresetDialog.xaml 的交互逻辑
    /// </summary>
    public partial class SelectPresetDialog : MetroWindow
    {
        private ObservableCollection<PresetViewModel> _presets;

        /// <summary>
        /// 选中的预置规则 ID
        /// </summary>
        public string SelectedPresetId { get; private set; }

        public SelectPresetDialog()
        {
            InitializeComponent();

            // 加载预置规则列表
            _presets = new ObservableCollection<PresetViewModel>(
                WindowPresetManager.Instance.Presets.Select(p => new PresetViewModel(p)));
            PresetsListBox.ItemsSource = _presets;

            PresetsListBox.SelectionChanged += (s, e) =>
            {
                OkButton.IsEnabled = PresetsListBox.SelectedItem != null;
            };
        }

        private void PresetsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PresetsListBox.SelectedItem is PresetViewModel selected)
            {
                SelectedPresetId = selected.Id;
                DialogResult = true;
                Close();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsListBox.SelectedItem is PresetViewModel selected)
            {
                SelectedPresetId = selected.Id;
                DialogResult = true;
                Close();
            }
        }

        /// <summary>
        /// 预置规则视图模型
        /// </summary>
        private class PresetViewModel
        {
            private readonly WindowRule _rule;

            public PresetViewModel(WindowRule rule)
            {
                _rule = rule;
            }

            public string Id => _rule.Id;
            public string Name => _rule.Name;

            public string DisplaySummary
            {
                get
                {
                    if (_rule.Conditions != null && _rule.Conditions.Count > 0)
                    {
                        var firstCondition = _rule.Conditions[0];
                        var summary = $"{firstCondition.Type}: {firstCondition.Value}";
                        if (_rule.Conditions.Count > 1)
                        {
                            summary += $" (+{_rule.Conditions.Count - 1})";
                        }
                        return summary;
                    }

                    if (!string.IsNullOrEmpty(_rule.ApplicationPath))
                    {
                        return System.IO.Path.GetFileName(_rule.ApplicationPath);
                    }

                    return "(空规则)";
                }
            }
        }
    }
}
