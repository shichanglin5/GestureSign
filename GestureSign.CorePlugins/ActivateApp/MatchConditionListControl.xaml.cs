using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using GestureSign.Common.Applications;
using GestureSign.Common.UI;
using Microsoft.Win32;

namespace GestureSign.CorePlugins.ActivateApp
{
    /// <summary>
    /// MatchConditionListControl.xaml 的交互逻辑
    /// </summary>
    public partial class MatchConditionListControl : UserControl
    {
        public MatchConditionListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 应用程序路径
        /// </summary>
        public string ApplicationPath
        {
            get => ApplicationPathTextBox.Text.Trim();
            set => ApplicationPathTextBox.Text = value ?? string.Empty;
        }

        /// <summary>
        /// 获取所有条件（仅返回启用且有值的条件）
        /// </summary>
        public List<MatchCondition> GetConditions()
        {
            var conditions = new List<MatchCondition>();
            foreach (var child in ConditionPanel.Children)
            {
                if (child is MatchConditionItemControl item)
                {
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
        /// 设置条件列表（加载已保存的配置）
        /// </summary>
        public void SetConditions(List<MatchCondition> conditions)
        {
            ConditionPanel.Children.Clear();
            if (conditions == null || conditions.Count == 0)
                return;

            foreach (var condition in conditions)
            {
                AddConditionItem(condition, isEnabled: true);
            }
        }

        /// <summary>
        /// 从窗口信息填充条件（捕获模式）
        /// 默认添加所有可用条件，用户可以通过 CheckBox 选择启用哪些
        /// </summary>
        public void PopulateFromWindowInfo(WindowMatchInfo info)
        {
            ConditionPanel.Children.Clear();

            // 自动填充应用程序路径
            if (!string.IsNullOrEmpty(info.ProcessPath))
            {
                ApplicationPathTextBox.Text = info.ProcessPath;
            }

            // 添加 ClassName（默认不启用）
            if (!string.IsNullOrEmpty(info.ClassName))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ClassName,
                    Value = info.ClassName
                }, isEnabled: false);
            }

            // 添加 ProcessName（默认不启用）
            if (!string.IsNullOrEmpty(info.FileName))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ProcessName,
                    Value = info.FileName
                }, isEnabled: false);
            }

            // 添加 AUMID（如果有，UWP 应用，默认启用）
            if (!string.IsNullOrEmpty(info.AUMID))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.AUMID,
                    Value = info.AUMID
                }, isEnabled: true);
            }

            // 添加 Title（默认不启用）
            if (!string.IsNullOrEmpty(info.Title))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.Title,
                    Value = info.Title
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
        private MatchConditionItemControl AddConditionItem(MatchCondition condition = null, bool isEnabled = true)
        {
            var item = new MatchConditionItemControl
            {
                Margin = new Thickness(0, 0, 0, 5)
            };

            if (condition != null)
            {
                item.SetCondition(condition, isEnabled);
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
