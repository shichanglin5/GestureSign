using System;
using System.Windows;
using System.Windows.Controls;
using GestureSign.Common.Applications;

namespace GestureSign.CorePlugins.ActivateApp
{
    /// <summary>
    /// MatchConditionItemControl.xaml 的交互逻辑
    /// </summary>
    public partial class MatchConditionItemControl : UserControl
    {
        public event EventHandler DeleteRequested;

        public MatchConditionItemControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 条件是否启用
        /// </summary>
        public bool IsConditionEnabled
        {
            get { return EnabledCheckBox.IsChecked == true; }
            set { EnabledCheckBox.IsChecked = value; }
        }

        public MatchConditionType ConditionType
        {
            get
            {
                var selectedItem = TypeComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem?.Tag is string tag)
                {
                    return tag switch
                    {
                        "ClassName" => MatchConditionType.ClassName,
                        "Title" => MatchConditionType.Title,
                        "ProcessName" => MatchConditionType.ProcessName,
                        "AUMID" => MatchConditionType.AUMID,
                        _ => MatchConditionType.ClassName
                    };
                }
                return MatchConditionType.ClassName;
            }
            set
            {
                int index = value switch
                {
                    MatchConditionType.ClassName => 0,
                    MatchConditionType.Title => 1,
                    MatchConditionType.ProcessName => 2,
                    MatchConditionType.AUMID => 3,
                    _ => 0
                };
                TypeComboBox.SelectedIndex = index;
            }
        }

        public string ConditionValue
        {
            get { return ValueTextBox.Text; }
            set { ValueTextBox.Text = value ?? string.Empty; }
        }

        public bool IsRegex
        {
            get { return RegexCheckBox.IsChecked == true; }
            set { RegexCheckBox.IsChecked = value; }
        }

        /// <summary>
        /// 获取条件（仅当启用且有值时返回）
        /// </summary>
        public MatchCondition GetCondition()
        {
            // 未启用或无值时返回 null
            if (!IsConditionEnabled || string.IsNullOrWhiteSpace(ConditionValue))
                return null;

            return new MatchCondition
            {
                Type = ConditionType,
                Value = ConditionValue,
                IsRegex = ConditionType == MatchConditionType.Title && IsRegex
            };
        }

        public void SetCondition(MatchCondition condition, bool isEnabled = true)
        {
            if (condition == null)
                return;

            IsConditionEnabled = isEnabled;
            ConditionType = condition.Type;
            ConditionValue = condition.Value;
            IsRegex = condition.IsRegex;
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 初始化时 RegexCheckBox 可能还未创建
            if (RegexCheckBox == null)
                return;

            // 只有 Title 类型支持正则
            RegexCheckBox.Visibility = ConditionType == MatchConditionType.Title
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (ConditionType != MatchConditionType.Title)
            {
                RegexCheckBox.IsChecked = false;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
