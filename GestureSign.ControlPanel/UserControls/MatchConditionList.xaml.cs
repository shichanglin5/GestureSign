using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GestureSign.Common.Applications;
using GestureSign.Common.UI;
using Microsoft.Win32;

namespace GestureSign.ControlPanel.UserControls
{
    /// <summary>
    /// MatchConditionList.xaml 的交互逻辑
    /// </summary>
    public partial class MatchConditionList : UserControl
    {
        public static readonly DependencyProperty ShowCheckBoxProperty =
            DependencyProperty.Register("ShowCheckBox", typeof(bool), typeof(MatchConditionList), new PropertyMetadata(false));

        public bool ShowCheckBox
        {
            get { return (bool)GetValue(ShowCheckBoxProperty); }
            set { SetValue(ShowCheckBoxProperty, value); }
        }

        /// <summary>
        /// 应用程序路径
        /// </summary>
        public string ApplicationPath
        {
            get => ApplicationPathTextBox.Text.Trim();
            set => ApplicationPathTextBox.Text = value ?? string.Empty;
        }

        public MatchConditionList()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 获取所有条件（仅返回启用且有值的条件）
        /// </summary>
        public List<MatchCondition> GetConditions()
        {
            var conditions = new List<MatchCondition>();
            foreach (var child in ConditionPanel.Children)
            {
                if (child is MatchConditionItem item)
                {
                    // 如果显示 CheckBox，只取勾选的；否则全取
                    if (ShowCheckBox && !item.IsConditionEnabled)
                        continue;

                    var condition = item.GetCondition();
                    if (condition != null)
                    {
                        conditions.Add(condition);
                    }
                }
            }
            return conditions;
        }

        /// <summary>
        /// 设置条件列表
        /// </summary>
        public void SetConditions(List<MatchCondition> conditions)
        {
            ConditionPanel.Children.Clear();
            if (conditions == null || conditions.Count == 0)
                return;

            foreach (var condition in conditions)
            {
                AddConditionItem(condition);
            }
        }

        /// <summary>
        /// 从窗口信息填充条件（捕获模式）
        /// </summary>
        public void PopulateFromWindowInfo(WindowMatchInfo info)
        {
            ConditionPanel.Children.Clear();

            // 自动填充应用程序路径
            if (!string.IsNullOrEmpty(info.ProcessPath))
            {
                ApplicationPathTextBox.Text = info.ProcessPath;
            }

            if (!string.IsNullOrEmpty(info.ClassName))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ClassName,
                    Value = info.ClassName
                }, isEnabled: false);
            }

            if (!string.IsNullOrEmpty(info.Title))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.Title,
                    Value = info.Title
                }, isEnabled: false);
            }

            if (!string.IsNullOrEmpty(info.FileName))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ProcessName,
                    Value = info.FileName
                }, isEnabled: true); // 默认勾选进程名
            }

            if (!string.IsNullOrEmpty(info.ProcessPath))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ProcessPath,
                    Value = info.ProcessPath
                }, isEnabled: false);
            }

            if (!string.IsNullOrEmpty(info.AUMID))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.AUMID,
                    Value = info.AUMID
                }, isEnabled: false);
            }
        }

        /// <summary>
        /// 清空所有条件
        /// </summary>
        public void Clear()
        {
            ConditionPanel.Children.Clear();
        }

        /// <summary>
        /// 添加条件项
        /// </summary>
        private MatchConditionItem AddConditionItem(MatchCondition condition = null, bool isEnabled = true)
        {
            var item = new MatchConditionItem
            {
                ShowCheckBox = ShowCheckBox,
                Margin = new Thickness(0, 0, 0, 5)
            };

            if (condition != null)
            {
                item.SetCondition(condition);
                item.IsConditionEnabled = isEnabled;
            }

            item.DeleteRequested += (s, e) =>
            {
                ConditionPanel.Children.Remove(item);
            };

            ConditionPanel.Children.Add(item);
            return item;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddConditionItem();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                ApplicationPathTextBox.Text = dialog.FileName;
            }
        }
    }
}
