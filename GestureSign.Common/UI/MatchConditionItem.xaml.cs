using System;
using System.Windows;
using System.Windows.Controls;
using GestureSign.Common.Applications;

namespace GestureSign.Common.UI
{
    /// <summary>
    /// MatchConditionItem.xaml 的交互逻辑
    /// </summary>
    public partial class MatchConditionItem : UserControl
    {
        public event EventHandler DeleteRequested;

        public static readonly DependencyProperty ShowCheckBoxProperty =
            DependencyProperty.Register("ShowCheckBox", typeof(bool), typeof(MatchConditionItem),
                new PropertyMetadata(false, OnShowCheckBoxChanged));

        private static void OnShowCheckBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MatchConditionItem item && item.EnabledCheckBox != null)
            {
                item.EnabledCheckBox.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public bool ShowCheckBox
        {
            get { return (bool)GetValue(ShowCheckBoxProperty); }
            set { SetValue(ShowCheckBoxProperty, value); }
        }

        public MatchConditionItem()
        {
            InitializeComponent();
            Loaded += MatchConditionItem_Loaded;
        }

        private void MatchConditionItem_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化后更新 CheckBox 可见性
            EnabledCheckBox.Visibility = ShowCheckBox ? Visibility.Visible : Visibility.Collapsed;
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
                        "ProcessPath" => MatchConditionType.ProcessPath,
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
                    MatchConditionType.ProcessPath => 3,
                    MatchConditionType.AUMID => 4,
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

        public bool IsConditionEnabled
        {
            get { return EnabledCheckBox.IsChecked == true; }
            set { EnabledCheckBox.IsChecked = value; }
        }

        /// <summary>
        /// 获取条件（当 ShowCheckBox 为 true 时仅当启用且有值时返回，否则仅当有值时返回）
        /// </summary>
        public MatchCondition GetCondition()
        {
            // 如果显示 CheckBox 且未启用，返回 null
            if (ShowCheckBox && !IsConditionEnabled)
                return null;

            if (string.IsNullOrWhiteSpace(ConditionValue))
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

            ConditionType = condition.Type;
            ConditionValue = condition.Value;
            IsRegex = condition.IsRegex;
            IsConditionEnabled = isEnabled;
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
